[CmdletBinding()]
param(
    [string]$Cases = (Join-Path $PSScriptRoot "assistant-cases-v3.json"),
    [Parameter(Mandatory = $true)][string]$ReviewAttestation,
    [Parameter(Mandatory = $true)][string]$ReviewSignature,
    [Parameter(Mandatory = $true)][string]$CandidateContainerAppResourceId,
    [Parameter(Mandatory = $true)][string]$CandidateRevision,
    [Parameter(Mandatory = $true)][string]$OutputAdmission,
    [Parameter(Mandatory = $true)][string]$OutputSignature,
    [string]$Vault = "kv-lex-eval-review",
    [string]$Key = "lex-evaluation-review-v1",
    [string]$KeyVersion = "b716c12bb450464ebad5d3046a3ae2e7"
)

$ErrorActionPreference = "Stop"
$repository = Split-Path $PSScriptRoot -Parent
if ($CandidateContainerAppResourceId -notmatch
    '^/subscriptions/([^/]+)/resourceGroups/([^/]+)/providers/Microsoft\.App/containerApps/([^/]+)$') {
    throw "CandidateContainerAppResourceId is not an exact Container App resource id."
}
$subscription = $Matches[1]
$resourceGroup = $Matches[2]
$containerApp = $Matches[3]
$revisionJson = az containerapp revision show `
    -g $resourceGroup -n $containerApp --revision $CandidateRevision `
    --subscription $subscription `
    -o json --only-show-errors
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revisionJson)) {
    throw "The exact candidate revision could not be authenticated."
}
$revision = $revisionJson | ConvertFrom-Json -Depth 32
if ($revision.name -ne $CandidateRevision -or
    @($revision.properties.template.containers).Count -ne 1) {
    throw "Azure returned a different or ambiguous candidate revision."
}
$container = @($revision.properties.template.containers)[0]
$image = [string]$container.image
if ($image -notmatch
    '^[a-z0-9][a-z0-9.-]*\.azurecr\.io/[a-z0-9._/-]+(@sha256:[0-9a-f]{64}|:sha-[0-9a-f]{12,64})$') {
    throw "Candidate image is not an exact immutable Lex ACR image identity."
}

function Get-ExactEnvironmentValue([string]$Name) {
    $matches = @($container.env | Where-Object { $_.name -ceq $Name })
    if ($matches.Count -ne 1) {
        throw "Candidate revision must expose one non-secret $Name value."
    }
    $value = $matches[0].PSObject.Properties["value"]
    $secretRef = $matches[0].PSObject.Properties["secretRef"]
    if ($null -eq $value -or
        [string]::IsNullOrWhiteSpace([string]$value.Value) -or
        ($null -ne $secretRef -and [bool]$secretRef.Value)) {
        throw "Candidate revision must expose one non-secret $Name value."
    }
    return [string]$value.Value
}

$codeCommit = Get-ExactEnvironmentValue "LEX_CODE_COMMIT"
$artifactManifestSet = Get-ExactEnvironmentValue "LEX_ARTIFACT_MANIFEST_ID"
$deployedImage = Get-ExactEnvironmentValue "LEX_DEPLOY_IMAGE"
$deployedCatalog = Get-ExactEnvironmentValue "LEX_ASSISTANT_EVAL_CATALOG_SHA256"
$casesPath = (Resolve-Path -LiteralPath $Cases).Path
$casesDigest = (Get-FileHash -LiteralPath $casesPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($codeCommit -notmatch '^[0-9a-f]{40}$' -or
    $artifactManifestSet -notmatch '^[0-9a-f]{64}$' -or
    $deployedCatalog -cne $casesDigest -or
    $deployedImage -cne $image) {
    throw "Candidate revision identity does not match the exact reviewed catalog/image release."
}

$admissionPath = [IO.Path]::GetFullPath($OutputAdmission)
$signaturePath = [IO.Path]::GetFullPath($OutputSignature)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($admissionPath)) | Out-Null
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($signaturePath)) | Out-Null

& dotnet run --project (Join-Path $repository "src/Lex.Ingest/Lex.Ingest.csproj") `
    -c Release -- assistant-eval create-admission `
    --cases $casesPath `
    --review-attestation $ReviewAttestation `
    --review-signature $ReviewSignature `
    --candidate-revision $CandidateRevision `
    --candidate-image $image `
    --code-commit $codeCommit `
    --artifact-manifest-set $artifactManifestSet `
    --out $admissionPath
if ($LASTEXITCODE -ne 0) {
    throw "The reviewed evaluation admission capability could not be created."
}

$admissionBytes = [IO.File]::ReadAllBytes($admissionPath)
$digestAlgorithm = [Security.Cryptography.SHA256]::Create()
try { $digestBytes = $digestAlgorithm.ComputeHash($admissionBytes) }
finally { $digestAlgorithm.Dispose() }
$signatureJson = az keyvault key sign --vault-name $Vault --name $Key --version $KeyVersion `
    --algorithm ES256 --digest ([Convert]::ToBase64String($digestBytes)) `
    -o json --only-show-errors
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($signatureJson)) {
    throw "Key Vault did not sign the evaluation admission capability."
}
$signatureResult = $signatureJson | ConvertFrom-Json
$signature = if ($signatureResult -is [string]) { $signatureResult }
elseif ($signatureResult.signature) { $signatureResult.signature }
elseif ($signatureResult.value) { $signatureResult.value }
else { $signatureResult.result }
if ([string]::IsNullOrWhiteSpace($signature)) {
    throw "Key Vault returned no evaluation admission signature."
}
[IO.File]::WriteAllText(
    $signaturePath, $signature.Trim() + "`n", [Text.UTF8Encoding]::new($false))

& dotnet run --project (Join-Path $repository "src/Lex.Ingest/Lex.Ingest.csproj") `
    -c Release -- assistant-eval verify-admission `
    --cases $casesPath `
    --review-attestation $ReviewAttestation `
    --review-signature $ReviewSignature `
    --candidate-revision $CandidateRevision `
    --candidate-image $image `
    --code-commit $codeCommit `
    --artifact-manifest-set $artifactManifestSet `
    --admission $admissionPath `
    --signature $signaturePath
if ($LASTEXITCODE -ne 0) {
    throw "The signed evaluation admission capability failed local verification."
}
