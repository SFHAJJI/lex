#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Report,
    [Parameter(Mandatory = $true)][string]$Cases,
    [Parameter(Mandatory = $true)][string]$ReviewAttestation,
    [Parameter(Mandatory = $true)][string]$ReviewSignature,
    [Parameter(Mandatory = $true)][string]$CandidateRevision,
    [string]$BootstrapRollbackRevision,
    [string]$BootstrapCanonicalTemplateDigest,
    [string]$BootstrapExpectedImageDigest,
    [string]$Repository = "SFHAJJI/lex-ops"
)

$ErrorActionPreference = "Stop"
$bootstrapInputs = @(
    $BootstrapRollbackRevision,
    $BootstrapCanonicalTemplateDigest,
    $BootstrapExpectedImageDigest
)
$bootstrapInputCount = @($bootstrapInputs | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    }).Count
if ($bootstrapInputCount -ne 0 -and $bootstrapInputCount -ne 3) {
    throw "The bootstrap equivalence inputs must be supplied together."
}
$bootstrapMode = $bootstrapInputCount -eq 3
if ($CandidateRevision -cnotmatch '\Aca-lex-web--[a-z0-9-]+\z') {
    throw "CandidateRevision is not an exact Lex Container Apps revision name."
}
if ($bootstrapMode) {
    if ($BootstrapRollbackRevision -cnotmatch '\Aca-lex-web--[a-z0-9-]+\z' -or
        $BootstrapRollbackRevision -eq $CandidateRevision) {
        throw "BootstrapRollbackRevision is not a distinct exact Lex revision name."
    }
    if ($BootstrapCanonicalTemplateDigest -cnotmatch '\Asha256:[0-9a-f]{64}\z') {
        throw "BootstrapCanonicalTemplateDigest must be a lowercase sha256 digest."
    }
    if ($BootstrapExpectedImageDigest -cnotmatch '\Asha256:[0-9a-f]{64}\z') {
        throw "BootstrapExpectedImageDigest must be a lowercase sha256 digest."
    }
}

$sourceReport = (Resolve-Path -LiteralPath $Report).Path
$sourceCases = (Resolve-Path -LiteralPath $Cases).Path
$sourceReview = (Resolve-Path -LiteralPath $ReviewAttestation).Path
$sourceReviewSignature = (Resolve-Path -LiteralPath $ReviewSignature).Path
$reportJson = Get-Content -LiteralPath $sourceReport -Raw | ConvertFrom-Json
$target = $reportJson.identity.target
if ($reportJson.schema -ne "lex-assistant-eval-report/3" -or
    $reportJson.activation_gate_passed -ne $true -or
    @($reportJson.gate_failures).Count -ne 0) {
    throw "The assistant evaluation report is not a passing release report."
}
if ($target.revision_name -ne $CandidateRevision) {
    throw "The report does not describe the requested candidate revision."
}

$reportDigest = (Get-FileHash -LiteralPath $sourceReport -Algorithm SHA256).Hash.ToLowerInvariant()
$tag = "assistant-eval-$($target.code_commit.Substring(0, 12))-$($reportDigest.Substring(0, 12))"
gh release view $tag --repo $Repository *> $null
if ($LASTEXITCODE -eq 0) { throw "Release '$tag' already exists; evidence releases are immutable." }
gh release create $tag `
    "$sourceReport#assistant-eval-report.json" `
    "$sourceCases#assistant-cases-v3.json" `
    "$sourceReview#assistant-cases-v3.review.json" `
    "$sourceReviewSignature#assistant-cases-v3.review.sig" `
    --repo $Repository --target main --draft `
    --title "Pending Lex assistant evaluation $($target.code_commit.Substring(0, 12))" `
    --notes "Unsigned staging evidence for candidate revision $CandidateRevision. The production OIDC publisher must authenticate, sign, verify and publish it."
if ($LASTEXITCODE -ne 0) { throw "GitHub did not stage the assistant evaluation evidence." }

$workflowArguments = @(
    "workflow", "run", "publish-assistant-evaluation.yml",
    "--repo", $Repository, "--ref", "main",
    "-f", "evaluation_release=$tag",
    "-f", "candidate_revision=$CandidateRevision"
)
if ($bootstrapMode) {
    $workflowArguments += @(
        "-f", "bootstrap_rollback_revision=$BootstrapRollbackRevision",
        "-f", "bootstrap_canonical_template_digest=$BootstrapCanonicalTemplateDigest",
        "-f", "bootstrap_expected_image_digest=$BootstrapExpectedImageDigest"
    )
}
& gh @workflowArguments
if ($LASTEXITCODE -ne 0) {
    throw "The signed assistant-evaluation publication workflow was not dispatched."
}
Write-Output $tag
