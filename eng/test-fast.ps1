[CmdletBinding()]
param(
    [Parameter()][ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [Parameter()][string]$Filter
)

# The fast lane: the contract, platform and API tests (about 3,000 tests, under a minute).
# The ingest suite (about 15 minutes) runs before a merge to v3/integration and in CI, not here.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'tests/Lex.V3.Tests/Lex.V3.Tests.csproj'
$arguments = @('test', '--project', $project, '--configuration', $Configuration)
if ($Filter) { $arguments += @('--filter', $Filter) }
& dotnet @arguments
exit $LASTEXITCODE
