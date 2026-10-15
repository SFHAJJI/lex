[CmdletBinding()]
param(
    [Parameter()][ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [Parameter()][string]$Filter,
    # CI's image-conditions lane on this machine (plan 0.8): its two test runs, each with the filter the lane runs it with
    # (tests/Lex.V3.TestSupport/image-conditions-lane-filters.json), under the two switches that take away what the
    # release image lacks, the language library and the zone database. The lane runs them inside the image's own base;
    # LEX_IMAGE_CONDITIONS makes the canary (ImageConditionsTests) assert that the switches held. The second run, the
    # request-time Lex.V3.Ingest.Tests classes, is where a title the image cannot find shows, and takes minutes, not
    # seconds. -Filter narrows both runs, and a run it leaves empty is not a failure.
    [Parameter()][switch]$ImageConditions
)

# The fast lane: the contract, platform and API tests (about 3,000 tests, under a minute).
# The ingest suite (about 15 minutes) runs before a merge to v3/integration and in CI, not here, but for its
# request-time classes under -ImageConditions.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runs = @(@{ Project = 'tests/Lex.V3.Tests/Lex.V3.Tests.csproj'; Lane = $null })
$switches = [ordered]@{}
if ($ImageConditions) {
    $switches['DOTNET_SYSTEM_GLOBALIZATION_INVARIANT'] = 'true'
    $switches['DOTNET_SYSTEM_TIMEZONE_INVARIANT'] = 'true'
    $switches['LEX_IMAGE_CONDITIONS'] = '1'
    $lane = Get-Content -Raw -LiteralPath (Join-Path $root 'tests/Lex.V3.TestSupport/image-conditions-lane-filters.json') |
        ConvertFrom-Json
    $runs = @(
        @{ Project = 'tests/Lex.V3.Tests/Lex.V3.Tests.csproj'; Lane = $lane.lex_v3_tests },
        @{ Project = 'tests/Lex.V3.Ingest.Tests/Lex.V3.Ingest.Tests.csproj'; Lane = $lane.ingest_request_time }
    )
}

$previous = @{}
foreach ($name in $switches.Keys) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $switches[$name])
}
$code = 0
try {
    foreach ($run in $runs) {
        $arguments = @('test', '--project', (Join-Path $root $run.Project), '--configuration', $Configuration)
        $runFilter = if ($Filter -and $run.Lane) { "($Filter)&($($run.Lane))" } elseif ($Filter) { $Filter } else { $run.Lane }
        if ($runFilter) { $arguments += @('--filter', $runFilter) }
        # 8 is the test platform's "zero tests ran": a -Filter may name tests of one run only.
        if ($Filter -and $ImageConditions) { $arguments += @('--ignore-exit-code', '8') }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0 -and $code -eq 0) { $code = $LASTEXITCODE }
    }
}
finally {
    foreach ($name in $switches.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
exit $code
