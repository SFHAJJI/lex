#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Report,
    [Parameter(Mandatory = $true)][string]$Cases,
    [Parameter(Mandatory = $true)][string]$ReviewAttestation,
    [Parameter(Mandatory = $true)][string]$ReviewSignature,
    [Parameter(Mandatory = $true)][string]$Admission,
    [Parameter(Mandatory = $true)][string]$AdmissionSignature,
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
$sourceAdmission = (Resolve-Path -LiteralPath $Admission).Path
$sourceAdmissionSignature = (Resolve-Path -LiteralPath $AdmissionSignature).Path
$reportJson = Get-Content -LiteralPath $sourceReport -Raw | ConvertFrom-Json
$admissionJson = Get-Content -LiteralPath $sourceAdmission -Raw | ConvertFrom-Json
$target = $reportJson.identity.target
if ($reportJson.schema -ne "lex-assistant-eval-report/3" -or
    $reportJson.activation_gate_passed -ne $true -or
    @($reportJson.gate_failures).Count -ne 0) {
    throw "The assistant evaluation report is not a passing release report."
}
if ($target.revision_name -ne $CandidateRevision) {
    throw "The report does not describe the requested candidate revision."
}
$admissionDigest = (Get-FileHash -LiteralPath $sourceAdmission -Algorithm SHA256).Hash.ToLowerInvariant()
if ($reportJson.admission_sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
    $reportJson.admission_sha256 -cne $admissionDigest -or
    $reportJson.admission_run_identity -cnotmatch '\A[0-9a-f]{16}\z' -or
    $admissionJson.schema -ne "lex-assistant-eval-admission/2" -or
    $admissionJson.catalog_sha256 -cne $reportJson.cases_sha256 -or
    $admissionJson.candidate_revision -cne $target.revision_name -or
    $admissionJson.candidate_image -cne $target.image -or
    $admissionJson.code_commit -cne $target.code_commit -or
    $admissionJson.artifact_manifest_set -cne $target.artifact_manifest_set -or
    $admissionJson.target_evidence_sha256 -cne $target.evidence_sha256 -or
    $admissionJson.candidate_model_evidence_sha256 -cne
        $reportJson.identity.candidate_model.evidence_sha256 -or
    $admissionJson.grader_model_evidence_sha256 -cne
        $reportJson.identity.grader_model.evidence_sha256) {
    throw "The report does not bind the exact signed evaluation admission."
}
$admissionSignatureInfo = Get-Item -LiteralPath $sourceAdmissionSignature
if ($admissionSignatureInfo.Length -le 0 -or $admissionSignatureInfo.Length -gt 514) {
    throw "The evaluation admission signature is outside its byte limit."
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
    "$sourceAdmission#assistant-eval-admission.json" `
    "$sourceAdmissionSignature#assistant-eval-admission.sig" `
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
