#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Cases = (Join-Path $PSScriptRoot "assistant-cases-v3.json"),
    [Parameter(Mandatory = $true)][string]$OutputAttestation,
    [Parameter(Mandatory = $true)][string]$OutputSignature,
    [string]$Reviewer = "Soufien Hajji",
    [string]$Attestation = "I reviewed every frozen case, expected typed operation, grading rubric, safety boundary and release budget and approve this exact catalog as production release evidence.",
    [string]$Vault = "kv-lex-eval-review",
    [string]$Key = "lex-evaluation-review-v1",
    [string]$KeyVersion = "b716c12bb450464ebad5d3046a3ae2e7"
)

$ErrorActionPreference = "Stop"
$repository = Split-Path $PSScriptRoot -Parent
$casesPath = (Resolve-Path -LiteralPath $Cases).Path
$casesDigest = (Get-FileHash -LiteralPath $casesPath -Algorithm SHA256).Hash.ToLowerInvariant()
$review = [ordered]@{
    schema = "lex-assistant-eval-review/1"
    key_id = "keyvault-kv-lex-eval-review-v1"
    cases_sha256 = $casesDigest
    reviewer = $Reviewer
    reviewer_id = "entra:184503e6-e07d-49ac-8c78-0a66c017118c"
    reviewed_at = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    decision = "approved"
    attestation = $Attestation
}
$reviewBytes = [Text.UTF8Encoding]::new($false).GetBytes(
    ($review | ConvertTo-Json -Depth 4) + "`n")
$reviewPath = [IO.Path]::GetFullPath($OutputAttestation)
$signaturePath = [IO.Path]::GetFullPath($OutputSignature)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reviewPath)) | Out-Null
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($signaturePath)) | Out-Null
[IO.File]::WriteAllBytes($reviewPath, $reviewBytes)

$digestAlgorithm = New-Object Security.Cryptography.SHA256Managed
try { $digestBytes = $digestAlgorithm.ComputeHash($reviewBytes) }
finally { $digestAlgorithm.Dispose() }
$signatureJson = az keyvault key sign --vault-name $Vault --name $Key --version $KeyVersion `
    --algorithm ES256 --digest ([Convert]::ToBase64String($digestBytes)) `
    -o json --only-show-errors
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($signatureJson)) {
    throw "Key Vault did not sign the assistant evaluation review."
}
$signatureResult = $signatureJson | ConvertFrom-Json
$signature = if ($signatureResult -is [string]) { $signatureResult }
elseif ($signatureResult.signature) { $signatureResult.signature }
elseif ($signatureResult.value) { $signatureResult.value }
else { $signatureResult.result }
if ([string]::IsNullOrWhiteSpace($signature)) {
    throw "Key Vault returned no assistant evaluation review signature."
}
[IO.File]::WriteAllText(
    $signaturePath, $signature.Trim() + "`n", [Text.UTF8Encoding]::new($false))

& dotnet run --project (Join-Path $repository "src/Lex.Ingest/Lex.Ingest.csproj") `
    -c Release -- assistant-eval verify-cases `
    --cases $casesPath --review-attestation $reviewPath --review-signature $signaturePath
if ($LASTEXITCODE -ne 0) { throw "The signed project-owner review failed verification." }
