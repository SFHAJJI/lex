[CmdletBinding()]
param(
    [Parameter()][ValidateSet('Full', 'Focused', 'Mutation')][string]$Mode = 'Full',
    [Parameter()][ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [Parameter()][string]$Project = 'tests/Lex.V3.Ingest.Tests/Lex.V3.Ingest.Tests.csproj',
    [Parameter()][string]$Filter,
    [Parameter()][ValidateRange(1, [int]::MaxValue)][int]$ExpectedTests,
    [Parameter()][ValidateRange(0, [int]::MaxValue)][int]$ExpectedIngestSkipped,
    [Parameter()][ValidateRange(0, [int]::MaxValue)][int]$ExpectedContractsSkipped,
    [Parameter()][ValidateRange(0, 86400)][int]$MutexWaitSeconds = 0,
    [Parameter()][string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $repositoryRoot 'Lex.V3.slnx'
$hasExpectedTests = $PSBoundParameters.ContainsKey('ExpectedTests')
$hasExpectedIngestSkipped = $PSBoundParameters.ContainsKey('ExpectedIngestSkipped')
$hasExpectedContractsSkipped = $PSBoundParameters.ContainsKey('ExpectedContractsSkipped')

function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = @(& git -C $repositoryRoot @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return ($output -join "`n").TrimEnd()
}

function Get-SourceState {
    param([Parameter(Mandatory)][string]$ScratchDirectory)

    $status = Invoke-Git -Arguments @('status', '--porcelain=v1', '--untracked-files=all')
    $diffPath = Join-Path $ScratchDirectory 'source.diff'
    & git -C $repositoryRoot diff --binary --no-ext-diff --output=$diffPath HEAD --
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to capture the tracked source diff.'
    }

    $diffHash = Invoke-Git -Arguments @('hash-object', $diffPath)
    $head = Invoke-Git -Arguments @('rev-parse', 'HEAD')
    $tree = Invoke-Git -Arguments @('rev-parse', 'HEAD^{tree}')
    $material = "$head`n$tree`n$status`n$diffHash"
    $bytes = [Text.Encoding]::UTF8.GetBytes($material)
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()

    [pscustomobject]@{
        Head = $head
        Tree = $tree
        Status = $status
        DiffHash = $diffHash
        Fingerprint = $fingerprint
    }
}

function Find-GovernedDotnet {
    $globalJson = Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json
    $sdkVersion = [string]$globalJson.sdk.version
    if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw 'global.json does not declare sdk.version.'
    }

    $directory = [IO.DirectoryInfo]$repositoryRoot
    while ($null -ne $directory) {
        $candidate = Join-Path $directory.FullName ".dotnet-$sdkVersion\dotnet.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [pscustomobject]@{ Path = $candidate; Root = Split-Path -Parent $candidate; Version = $sdkVersion }
        }
        $directory = $directory.Parent
    }

    throw "The governed .NET SDK $sdkVersion was not found above $repositoryRoot."
}

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & $script:dotnet.Path @Arguments | Out-Host
    return $LASTEXITCODE
}

function Read-TestResults {
    param([Parameter(Mandatory)][string]$Directory)

    $files = @(Get-ChildItem -LiteralPath $Directory -Filter '*.trx' -File -Recurse)
    if ($files.Count -eq 0) {
        throw 'The test run produced no TRX result.'
    }

    $totals = [ordered]@{ Total = 0; Executed = 0; Passed = 0; Failed = 0; Skipped = 0; RunErrors = 0; Modules = @() }
    foreach ($file in $files) {
        [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
        $namespaces = [Xml.XmlNamespaceManager]::new($document.NameTable)
        $namespaces.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $counters = $document.SelectSingleNode('//t:ResultSummary/t:Counters', $namespaces)
        if ($null -eq $counters) {
            throw "TRX result has no counters: $($file.FullName)"
        }

        $module = [pscustomobject]@{
            Name = $file.BaseName -replace '_net10\.0_.+$', ''
            Total = [int]$counters.total
            Executed = [int]$counters.executed
            Passed = [int]$counters.passed
            Failed = [int]$counters.failed
            Skipped = [int]$counters.notExecuted
            RunErrors = @($document.SelectNodes('//t:ResultSummary/t:RunInfos/t:RunInfo', $namespaces)).Count
        }
        $totals.Modules += $module
        $totals.Total += $module.Total
        $totals.Executed += $module.Executed
        $totals.Passed += $module.Passed
        $totals.Failed += $module.Failed
        $totals.Skipped += $module.Skipped
        $totals.RunErrors += $module.RunErrors
    }

    return [pscustomobject]$totals
}

function Remove-WorktreeBuildOutputs {
    $rootPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $searchRoots = @('src', 'tests').ForEach({ Join-Path $repositoryRoot $_ })
    $directories = @(Get-ChildItem -LiteralPath $searchRoots -Directory -Recurse -Force |
        Where-Object { $_.Name -in @('bin', 'obj') })
    foreach ($directory in $directories) {
        $resolved = [IO.Path]::GetFullPath($directory.FullName)
        if (-not $resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a build directory outside the worktree: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

if ($Mode -eq 'Full') {
    if (-not $hasExpectedIngestSkipped -or -not $hasExpectedContractsSkipped) {
        throw 'Full mode requires -ExpectedIngestSkipped and -ExpectedContractsSkipped so each module is accounted explicitly.'
    }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        throw 'Full mode does not accept -Filter.'
    }
}
else {
    if ([string]::IsNullOrWhiteSpace($Filter)) {
        throw "$Mode mode requires a non-empty -Filter."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $Project) -PathType Leaf)) {
        throw "Test project does not exist: $Project"
    }
}

if ($Mode -eq 'Mutation' -and -not $hasExpectedTests) {
    throw 'Mutation mode requires -ExpectedTests to prevent ambiguous mutation attribution.'
}

$dotnet = Find-GovernedDotnet
$env:PATH = "$($dotnet.Root);$env:PATH"
$env:DOTNET_ROOT = $dotnet.Root
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$env:DOTNET_NOLOGO = '1'
$actualSdk = (& $dotnet.Path --version).Trim()
if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $dotnet.Version) {
    throw "Expected governed SDK $($dotnet.Version), resolved $actualSdk."
}

$lexVariables = @(Get-ChildItem Env: | Where-Object { $_.Name.StartsWith('LEX_', [StringComparison]::Ordinal) })
if ($lexVariables.Count -gt 0) {
    throw "Local evidence refuses inherited LEX_* variables: $($lexVariables.Name -join ', '). Publisher-gated runs require their separately authorized command."
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $outputBase = Join-Path (Split-Path -Parent $dotnet.Root) 'test-output\v3-local'
}
else {
    $outputBase = [IO.Path]::GetFullPath($OutputRoot)
}
$utcTimestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ')
$runId = '{0}-{1}-{2}' -f $utcTimestamp, $PID, ([guid]::NewGuid().ToString('N'))
$runDirectory = Join-Path $outputBase $runId
$resultsDirectory = Join-Path $runDirectory 'results'
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null

$initial = Get-SourceState -ScratchDirectory $runDirectory
$statusLines = @($initial.Status -split "`n" | Where-Object { $_ })
$untracked = @($statusLines | Where-Object { $_.StartsWith('?? ', [StringComparison]::Ordinal) })
if ($untracked.Count -gt 0) {
    throw "Untracked files cannot be bound to evidence:`n$($untracked -join "`n")"
}
if ($Mode -eq 'Mutation') {
    if ($statusLines.Count -eq 0) {
        throw 'Mutation mode requires a tracked source change.'
    }
    $gitDirectory = [IO.Path]::GetFullPath((Invoke-Git -Arguments @('rev-parse', '--absolute-git-dir')))
    $commonDirectory = [IO.Path]::GetFullPath((Invoke-Git -Arguments @('rev-parse', '--path-format=absolute', '--git-common-dir')))
    if ($gitDirectory -ceq $commonDirectory) {
        throw 'Mutation mode requires a linked worktree so mutated source is isolated.'
    }
}
elseif ($statusLines.Count -ne 0) {
    throw "$Mode evidence requires a clean worktree."
}

$receipt = [ordered]@{
    Schema = 'lex-v3-local-test-receipt/1'
    RunId = $runId
    Mode = $Mode
    Configuration = $Configuration
    Head = $initial.Head
    Tree = $initial.Tree
    SourceFingerprint = $initial.Fingerprint
    DiffHash = $initial.DiffHash
    Project = if ($Mode -eq 'Full') { 'Lex.V3.slnx' } else { $Project.Replace('\', '/') }
    Filter = if ($Mode -eq 'Full') { $null } else { $Filter }
    ExpectedTests = if ($hasExpectedTests) { $ExpectedTests } else { $null }
    ExpectedSkipped = if ($Mode -eq 'Full') {
        [ordered]@{ 'Lex.V3.Ingest.Tests' = $ExpectedIngestSkipped; 'Lex.V3.Tests' = $ExpectedContractsSkipped }
    } else { $null }
    Sdk = $actualSdk
    MaxParallelTestModules = if ($Mode -eq 'Full') { 2 } else { 1 }
    RestoreSeconds = $null
    BuildSeconds = $null
    TestSeconds = $null
    TestExitCode = $null
    Tests = $null
    Outcome = 'InfrastructureFailure'
}

$mutex = [Threading.Mutex]::new($false, 'Global\LexV3LocalTestWorkflow-v1')
$ownsMutex = $false
$receiptPath = Join-Path $runDirectory 'receipt.json'
try {
    try {
        if ($MutexWaitSeconds -gt 0) {
            $ownsMutex = $mutex.WaitOne([TimeSpan]::FromSeconds($MutexWaitSeconds))
        }
        else {
            $waitingSince = [DateTimeOffset]::UtcNow
            while (-not $ownsMutex) {
                $ownsMutex = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
                if (-not $ownsMutex) {
                    Write-Host "Waiting for the machine-wide V3 test slot since $($waitingSince.ToString('O')); the current holder keeps priority."
                }
            }
        }
    }
    catch [Threading.AbandonedMutexException] {
        $ownsMutex = $true
    }
    if (-not $ownsMutex) {
        throw "Timed out waiting $MutexWaitSeconds seconds for the machine-wide V3 test slot; no work was started."
    }

    Write-Host "V3 local test slot acquired. Run: $runId"
    $target = if ($Mode -eq 'Full') { $solution } else { Join-Path $repositoryRoot $Project }

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $restoreExit = Invoke-Dotnet -Arguments @('restore', $target, '--locked-mode')
    $timer.Stop()
    $receipt.RestoreSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
    if ($restoreExit -ne 0) {
        throw "Restore failed with exit code $restoreExit."
    }

    $timer.Restart()
    $buildExit = Invoke-Dotnet -Arguments @('build', $target, '--configuration', $Configuration, '--no-restore')
    $timer.Stop()
    $receipt.BuildSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
    if ($buildExit -ne 0) {
        throw "Build failed with exit code $buildExit; no mutation result was recorded."
    }

    $testArguments = if ($Mode -eq 'Full') {
        @('test', '--solution', $solution)
    }
    else {
        @('test', $target, '--filter', $Filter)
    }
    $testArguments += @(
        '--configuration', $Configuration,
        '--no-restore', '--no-build',
        '--results-directory', $resultsDirectory,
        '--report-trx',
        '--report-trx-filename', '{pname}_{tfm}_{arch}.trx',
        '--minimum-expected-tests', '1',
        '--max-parallel-test-modules', [string]$receipt.MaxParallelTestModules,
        '--no-ansi'
    )

    $timer.Restart()
    $testExit = Invoke-Dotnet -Arguments $testArguments
    $timer.Stop()
    $receipt.TestSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
    $receipt.TestExitCode = $testExit
    $results = Read-TestResults -Directory $resultsDirectory
    $receipt.Tests = $results

    $final = Get-SourceState -ScratchDirectory $runDirectory
    if ($final.Fingerprint -cne $initial.Fingerprint) {
        throw "Source changed during the run: $($initial.Fingerprint) -> $($final.Fingerprint)."
    }
    if ($results.Total -lt 1) {
        throw 'Zero tests executed; the run cannot be evidence.'
    }
    if ($hasExpectedTests -and $results.Total -ne $ExpectedTests) {
        throw "Expected exactly $ExpectedTests tests, but the TRX results contain $($results.Total)."
    }
    if ($Mode -eq 'Full') {
        $ingest = @($results.Modules | Where-Object Name -ceq 'Lex.V3.Ingest.Tests')
        $contracts = @($results.Modules | Where-Object Name -ceq 'Lex.V3.Tests')
        if ($ingest.Count -ne 1 -or $contracts.Count -ne 1) {
            throw 'Full evidence requires exactly one TRX result for each test module.'
        }
        if ($ingest[0].Skipped -ne $ExpectedIngestSkipped -or $contracts[0].Skipped -ne $ExpectedContractsSkipped) {
            throw "Expected skips Ingest=$ExpectedIngestSkipped and Contracts=$ExpectedContractsSkipped; observed Ingest=$($ingest[0].Skipped) and Contracts=$($contracts[0].Skipped)."
        }
    }

    if ($Mode -eq 'Mutation') {
        if ($testExit -eq 2 -and $results.Failed -gt 0) {
            $receipt.Outcome = 'Killed'
        }
        elseif ($testExit -eq 0 -and $results.Failed -eq 0) {
            $receipt.Outcome = 'Survived'
            throw 'Mutation survived its focused test.'
        }
        else {
            throw "Mutation test ended with exit code $testExit, $($results.Failed) failed tests and $($results.RunErrors) run errors; this is not a valid kill."
        }
    }
    elseif ($testExit -eq 0 -and $results.Failed -eq 0) {
        $receipt.Outcome = 'Passed'
    }
    else {
        throw "Test run failed with exit code $testExit, $($results.Failed) failed tests and $($results.RunErrors) run errors."
    }
}
finally {
    $cleanupError = $null
    try {
        if ($Mode -eq 'Mutation') {
            Remove-WorktreeBuildOutputs
        }
    }
    catch {
        $receipt.Outcome = 'InfrastructureFailure'
        $cleanupError = $_
    }
    $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding utf8
    if ($ownsMutex) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
    Write-Host "Receipt: $receiptPath"
    if ($null -ne $cleanupError) {
        throw $cleanupError
    }
}

Write-Host "Outcome: $($receipt.Outcome); tests $($receipt.Tests.Total), passed $($receipt.Tests.Passed), failed $($receipt.Tests.Failed), skipped $($receipt.Tests.Skipped)."
