#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$ReviewAttestation,
    [Parameter(Mandatory = $true)][string]$ReviewSignature,
    [Parameter(Mandatory = $true)][string]$Admission,
    [Parameter(Mandatory = $true)][string]$AdmissionSignature,
    [Parameter(Mandatory = $true)][string]$Output,
    [Parameter(Mandatory = $true)][string]$CandidateContainerAppResourceId,
    [Parameter(Mandatory = $true)][string]$CandidateRevision,
    [Parameter(Mandatory = $true)][string]$CandidateModelResourceId,
    [Parameter(Mandatory = $true)][string]$CandidateDeployment,
    [Parameter(Mandatory = $true)][string]$GraderModelResourceId,
    [Parameter(Mandatory = $true)][string]$GraderDeployment,
    [string]$GraderKeyEnvironment = "AOAI_GRADER_KEY",
    [string]$Cases = (Join-Path $PSScriptRoot "assistant-cases-v3.json")
)

$ErrorActionPreference = "Stop"
$repository = Split-Path $PSScriptRoot -Parent
$arguments = @(
    "run", "--project", (Join-Path $repository "src/Lex.Ingest/Lex.Ingest.csproj"),
    "-c", "Release", "--no-restore", "--", "assistant-eval",
    "--base-url", $BaseUrl,
    "--cases", $Cases,
    "--review-attestation", $ReviewAttestation,
    "--review-signature", $ReviewSignature,
    "--admission", $Admission,
    "--admission-signature", $AdmissionSignature,
    "--out", $Output,
    "--candidate-container-app-resource-id", $CandidateContainerAppResourceId,
    "--candidate-revision", $CandidateRevision,
    "--candidate-model-resource-id", $CandidateModelResourceId,
    "--candidate-deployment", $CandidateDeployment,
    "--grader-model-resource-id", $GraderModelResourceId,
    "--grader-deployment", $GraderDeployment,
    "--grader-key-env", $GraderKeyEnvironment
)

if ($CandidateContainerAppResourceId -notmatch
    '^/subscriptions/[^/]+/resourceGroups/([^/]+)/providers/Microsoft\.App/containerApps/([^/]+)$') {
    throw "CandidateContainerAppResourceId is not an exact Container App resource id."
}
$resourceGroup = $Matches[1]
$containerApp = $Matches[2]

function Invoke-AzureText([string[]]$CommandArguments) {
    $result = & az @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($CommandArguments -join ' ')"
    }
    return (($result | Out-String).Trim())
}

$active = Invoke-AzureText @(
    "containerapp", "revision", "show", "-g", $resourceGroup, "-n", $containerApp,
    "--revision", $CandidateRevision, "--query", "properties.active", "-o", "tsv")
$traffic = Invoke-AzureText @(
    "containerapp", "revision", "show", "-g", $resourceGroup, "-n", $containerApp,
    "--revision", $CandidateRevision, "--query", "properties.trafficWeight", "-o", "tsv")
if ($active -ne "false" -or $traffic -ne "0") {
    throw "The evaluation runner must own activation of an inactive zero-traffic candidate."
}

$evaluationExitCode = 1
$cleanupFailure = $null
try {
    & az containerapp revision activate -g $resourceGroup -n $containerApp `
        --revision $CandidateRevision | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The candidate revision could not be activated." }
    $ready = $false
    foreach ($attempt in 1..20) {
        $running = Invoke-AzureText @(
            "containerapp", "revision", "show", "-g", $resourceGroup, "-n", $containerApp,
            "--revision", $CandidateRevision, "--query", "properties.runningState", "-o", "tsv")
        if ($running -in @("Running", "RunningAtMaxScale")) { $ready = $true; break }
        Start-Sleep -Seconds 3
    }
    if (-not $ready) { throw "The candidate revision did not become ready." }

    & dotnet @arguments
    $evaluationExitCode = $LASTEXITCODE
}
finally {
    $inactive = $false
    foreach ($attempt in 1..5) {
        & az containerapp revision deactivate -g $resourceGroup -n $containerApp `
            --revision $CandidateRevision 2>$null | Out-Null
        try {
            $state = Invoke-AzureText @(
                "containerapp", "revision", "show", "-g", $resourceGroup,
                "-n", $containerApp, "--revision", $CandidateRevision,
                "--query", "properties.active", "-o", "tsv")
            if ($state -eq "false") { $inactive = $true; break }
        }
        catch { }
        Start-Sleep -Seconds $attempt
    }
    if (-not $inactive) {
        $cleanupFailure = "The candidate revision could not be returned to inactive state."
    }
    try {
        $activeCount = [int](Invoke-AzureText @(
            "containerapp", "revision", "list", "-g", $resourceGroup, "-n", $containerApp,
            "--query", '[?properties.active==`true`] | length(@)', "-o", "tsv"))
        if ($activeCount -ne 1) {
            $cleanupFailure = "Expected one active quota authority after evaluation; found $activeCount."
        }
    }
    catch { $cleanupFailure = $_.Exception.Message }
}

if ($cleanupFailure) { throw $cleanupFailure }
exit $evaluationExitCode
