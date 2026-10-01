<#
.SYNOPSIS
Offline validation of the deployment kit: no Azure, no login, no network.

.DESCRIPTION
- `main.json` must be exactly `main.bicep`'s build (the generator's own metadata aside), so what is reviewed is what deploys;
- `bicep lint` must report nothing;
- `deploy.ps1` must parse.
The static rules on the template and the script (no secret, identity pull, one server, zero-traffic candidate) are the
web suite's `deploy-kit.test.mjs`, which reads the same committed files and runs in CI, where bicep is not installed.
Exit 0 when everything holds; otherwise each failure is printed and the exit is 1.
#>
$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()
$template = Join-Path $PSScriptRoot 'main.bicep'
$compiled = Join-Path $PSScriptRoot 'main.json'

if (-not (Get-Command bicep -ErrorAction SilentlyContinue)) { throw "bicep is not on PATH; install the Bicep CLI to validate the template offline." }

$built = Join-Path ([System.IO.Path]::GetTempPath()) "lex-v3-main-$([guid]::NewGuid().ToString('N')).json"
try {
    $output = bicep build $template --outfile $built 2>&1
    if ($LASTEXITCODE -ne 0) { $failures.Add("bicep build failed: $output") }
    elseif ($output) { $failures.Add("bicep build reported: $output") }
    else {
        $strip = { param($path) $json = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable; $json.metadata.Remove('_generator'); $json | ConvertTo-Json -Depth 64 }
        if ((& $strip $built) -ne (& $strip $compiled)) { $failures.Add("main.json is not main.bicep's build: run 'bicep build deploy/main.bicep --outfile deploy/main.json'") }
    }
}
finally {
    Remove-Item $built -ErrorAction SilentlyContinue
}

$lint = bicep lint $template 2>&1
if ($LASTEXITCODE -ne 0 -or $lint) { $failures.Add("bicep lint reported: $lint") }

$errors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'deploy.ps1'), [ref]$null, [ref]$errors)
if ($errors) { $failures.Add("deploy.ps1 does not parse: $($errors | ForEach-Object Message)") }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "- $_" }
    exit 1
}
Write-Host "the deployment kit validates offline: main.json is main.bicep's build, lint is clean, deploy.ps1 parses"
