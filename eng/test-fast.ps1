[CmdletBinding()]
param(
    [Parameter()][ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [Parameter()][string]$Filter,
    # The tests as the release image runs the API (plan 0.8): no language library and no zone database. CI's
    # image-conditions lane runs them inside the image's own base; here two switches reproduce it on any machine, and
    # LEX_IMAGE_CONDITIONS makes the canary (ImageConditionsTests) assert that they did.
    [Parameter()][switch]$ImageConditions
)

# The fast lane: the contract, platform and API tests (about 3,000 tests, under a minute).
# The ingest suite (about 15 minutes) runs before a merge to v3/integration and in CI, not here.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'tests/Lex.V3.Tests/Lex.V3.Tests.csproj'
$arguments = @('test', '--project', $project, '--configuration', $Configuration)
$switches = [ordered]@{}
if ($ImageConditions) {
    $switches['DOTNET_SYSTEM_GLOBALIZATION_INVARIANT'] = 'true'
    $switches['DOTNET_SYSTEM_TIMEZONE_INVARIANT'] = 'true'
    $switches['LEX_IMAGE_CONDITIONS'] = '1'
    # As the lane: the two classes that drive tools the image does not carry (the OCI verifier's pwsh, the SDK's publish).
    $outside = 'FullyQualifiedName!~OciVerifierTests&FullyQualifiedName!~ProductionPublishBoundaryTests'
    $Filter = if ($Filter) { "($Filter)&$outside" } else { $outside }
}
if ($Filter) { $arguments += @('--filter', $Filter) }

$previous = @{}
foreach ($name in $switches.Keys) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $switches[$name])
}
try {
    & dotnet @arguments
    $code = $LASTEXITCODE
}
finally {
    foreach ($name in $switches.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
exit $code
