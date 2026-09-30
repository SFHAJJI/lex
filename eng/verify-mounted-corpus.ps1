<#
.SYNOPSIS
Retain coverage and two resolve answers from a built corpus served on loopback.
.DESCRIPTION
Copy an API build and verified mount into a fresh evidence directory, start a private API
process, check the response contracts and corpus/index digests, and retain the raw envelopes
and their hashes. The script stops its own API process and keeps all evidence on failure.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApiDirectory,
    [Parameter(Mandatory)][string]$MountDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$EuIdentifier = '32016R0679',
    [string]$LuxembourgIdentifier = 'http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo'
)
$ErrorActionPreference = 'Stop'
$apiRoot = (Resolve-Path -LiteralPath $ApiDirectory).Path
$mountRoot = (Resolve-Path -LiteralPath $MountDirectory).Path
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidenceRoot) { throw 'Use a fresh evidence directory to preserve earlier results.' }
foreach ($sourceRoot in @($apiRoot, $mountRoot)) {
    $relative = [IO.Path]::GetRelativePath($sourceRoot, $evidenceRoot)
    if (-not [IO.Path]::IsPathRooted($relative) -and $relative -ne '..' -and
        -not $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'The evidence directory must be outside the API and mount source directories.'
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $apiRoot 'Lex.V3.Api.dll'))) { throw 'The API build is missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $mountRoot 'build-report.json'))) { throw 'The mount build report is missing.' }
$buildReport = Get-Content -LiteralPath (Join-Path $mountRoot 'build-report.json') -Raw | ConvertFrom-Json -Depth 32
$runtimeRoot = Join-Path $evidenceRoot 'runtime'
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
Get-ChildItem -LiteralPath $apiRoot | Where-Object Name -ne 'v3-corpus' | Copy-Item -Destination $runtimeRoot -Recurse
Copy-Item -LiteralPath $mountRoot -Destination (Join-Path $runtimeRoot 'v3-corpus') -Recurse

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$baseUri = "http://127.0.0.1:$port"
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.WorkingDirectory = $runtimeRoot
$start.ArgumentList.Add((Join-Path $runtimeRoot 'Lex.V3.Api.dll'))
$start.ArgumentList.Add('--urls')
$start.ArgumentList.Add($baseUri)
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseProxy = $false
$handler.UseCookies = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(10)
$utf8 = [Text.UTF8Encoding]::new($false)
try {
    $ready = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while (-not $ready -and [DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "The API exited with $($process.ExitCode)." }
        try {
            $response = $client.GetAsync($baseUri + '/').GetAwaiter().GetResult()
            $response.Dispose()
            $ready = $true
        } catch {
            Start-Sleep -Milliseconds 200
        }
    }
    if (-not $ready) { throw 'The loopback API did not become reachable within 30 seconds.' }

    $cases = @(
        @{ Name = 'coverage'; Operation = 'coverage'; Parameters = @{} },
        @{ Name = 'eu-resolve'; Operation = 'resolve'; Parameters = @{ identifier = $EuIdentifier } },
        @{ Name = 'lu-resolve'; Operation = 'resolve'; Parameters = @{ identifier = $LuxembourgIdentifier } }
    )
    $results = @()
    foreach ($case in $cases) {
        $request = @{ operation_id = $case.Operation; parameters = $case.Parameters } | ConvertTo-Json -Depth 8 -Compress
        $content = [Net.Http.StringContent]::new($request, $utf8, 'application/json')
        try {
            $response = $client.PostAsync($baseUri + '/api/v3/' + $case.Operation, $content).GetAwaiter().GetResult()
            try {
                $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                [IO.File]::WriteAllBytes((Join-Path $evidenceRoot ($case.Name + '.json')), $bytes)
                $envelope = $utf8.GetString($bytes) | ConvertFrom-Json -Depth 64
                if ([int]$response.StatusCode -ne 200 -or $envelope.verdict -cne 'answer') {
                    throw "$($case.Name) did not answer: HTTP $([int]$response.StatusCode), verdict $($envelope.verdict)."
                }
                if ($envelope.schema -cne 'lex-v3-envelope/1' -or $envelope.version -cne 'v3' -or
                    $envelope.object_type -cne 'envelope' -or $envelope.operation_id -cne $case.Operation -or
                    $envelope.result.schema -cne "lex-v3-$($case.Operation)-result/1" -or $null -ne $envelope.refusal) {
                    throw "$($case.Name) returned an unexpected envelope or result contract."
                }
                if ($case.Operation -eq 'resolve') {
                    $expectedPublisher = if ($case.Name -eq 'eu-resolve') { 'eu-eurlex' } else { 'lu-legilux' }
                    $expectedIndex = if ($case.Name -eq 'eu-resolve') { $buildReport.europeIndex.Sha256 } else { $buildReport.luxembourgIndex.Sha256 }
                    if ($envelope.result.object_type -cne 'work_resolution' -or
                        $envelope.result.value.requested_identifier -cne $case.Parameters.identifier -or
                        $envelope.result.value.publisher -cne $expectedPublisher -or
                        $envelope.result.value.corpus_sha256 -cne $buildReport.corpus.Sha256 -or
                        $envelope.result.value.index_sha256 -cne $expectedIndex) {
                        throw "$($case.Name) did not bind the requested identifier, publisher and built corpus/index digests."
                    }
                }
                $results += [ordered]@{
                    name = $case.Name
                    identifier = $case.Parameters.identifier
                    http_status = [int]$response.StatusCode
                    verdict = $envelope.verdict
                    envelope_sha256 = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))
                }
            } finally { $response.Dispose() }
        } finally { $content.Dispose() }
    }
    $report = [ordered]@{ schema = 'lex-v3-local-mount-smoke/1'; mount = $mountRoot; checked_at = [DateTimeOffset]::UtcNow.ToString('O'); results = $results }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'smoke-report.json'), ($report | ConvertTo-Json -Depth 12) + "`n", $utf8)
    $results | Format-Table
} finally {
    $client.Dispose()
    if (-not $process.HasExited) { $process.Kill($true) }
    $process.WaitForExit()
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'api.stdout.log'), $stdout.GetAwaiter().GetResult(), $utf8)
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'api.stderr.log'), $stderr.GetAwaiter().GetResult(), $utf8)
    $process.Dispose()
}
