<#
.SYNOPSIS
The owner's go-live for Lex V3, as one command: a verified release deployed as a zero-traffic candidate revision of the
one-server container, probed, and removed again if the probe fails.

.DESCRIPTION
Going live is the owner's decision (production credentials, deployment and promotion; STATUS, owner decisions). This
script is the kit for it. It never logs in, never reads, prints or stores a secret, and never promotes:
- it runs in the owner's own Azure CLI session (`az login` is the owner's, before this script);
- the registry access token (short-lived) travels from `az acr login --expose-token` to `oras login` on standard input,
  into a registry config in a fresh private directory that is removed when the copy ends; never on a command line;
- the app pulls its image with the managed identity the owner names, and the template holds no secret.

Without -Apply it only plans: it verifies the release and prints every command it would run, and changes nothing.
With -Apply, in order:
1. the release is read back under the signing identity's public key (`web/scripts/deploy-probe.mjs`, without
   --origin), and as signed by the release signer -SignerIdentity names, never a rehearsal; nothing is deployed from a
   release that does not verify. -Apply requires -SignerIdentity; a plan run without it may read a rehearsal release;
2. with -Public, the app's address rules are read before anything in Azure changes, and any rule, or a read that fails,
   stops the deployment, having changed nothing: a -Public deployment admits everyone (the template empties the rules as it
   deploys, before the candidate is probed), so it is for the open site only, and the opening is -Open's (the review of
   #989: on the private copy the rules were removed, and a failed probe deactivated only the candidate). A plan run says so;
3. the release image is copied into the registry out of the release by its manifest digest (`oras cp`); the app is
   deployed by that digest, never by a tag;
4. `deploy/main.bicep` is deployed: the image by digest as a new revision with the `candidate` label and no traffic
   while -LiveRevision keeps 100 per cent. Until the owner opens the site, every deployment is of the private copy:
   ingress admits only the -AllowedCidr ranges (the operator's address first; several, comma-separated; each /24 to
   /32), and the script refuses a deployment that names none; after the opening, every deployment says -Public;
5. who ingress admits is read back from the app, and must be exactly those ranges (or everyone, with -Public); if it is
   not, the ranges are set again and read back once more, and the script stops if they still do not hold;
6. the zero-traffic probe runs against the candidate's own URL (`web/scripts/deploy-probe.mjs --origin`), over HTTP
   only, unless -BrowserProbe asks for the browser probes too (`--browser`: the journey's real-mount steps through a
   browser, by pointer; Chrome or Edge must be installed, on a machine where a browser may run). No browser runs by
   default: none may run on the driver's machine (the owner's order of 2026-10-16), and -SkipBrowserProbe, the old way
   to say so, is still accepted and changes nothing. Without -BrowserProbe both probes run with the browser trap loaded
   first (`web/scripts/browser-trap.mjs`), so nothing they run can find or start a browser, and only the HTTP probes
   run: the release read back; `GET /` sending a reader who names no language to `/fr/`; the home page the release
   image's own (as built, or as its renderer renders the candidate's coverage answer) with the CSP
   (`frame-ancestors 'none'`), HSTS, `Referrer-Policy: no-referrer` and `nosniff`; the 404 page; the card at its stable
   route; coverage from the release's corpus; the clock; an accented title; readiness and no synthetic preview; V2's
   routes. Privacy is not observed on a remote revision either way: only a release's own image run watches the
   server's output and /tmp, and only when the release was probed (its report's `privacy`; a release made with
   `--no-probe` was not). The page steps (each page held to its answer, its hydration, console, paint and security
   headers, every printed citation verified) are then walked nowhere over the real mount: CI's journeys walk the
   fixture mounts only, and a release rehearsed with `image-rehearsal.mjs --no-browser` walks none. A browser walk of
   the private candidate from a machine where a browser is allowed (-BrowserProbe there) is the step left before the
   opening (STATUS-WEB.md);
7. if the probe fails, the candidate revision is deactivated (the removal step) and the script exits non-zero.
Promotion (moving traffic to the candidate) is printed for the owner and never run.

-Remove deactivates one revision of the app and exits: the removal step on its own.

-Open is the owner's opening, on the opening day and never otherwise: it first asks the site's ten legal pages
(`web/scripts/deploy-probe.mjs --opening`, the plan's item 7.1) and stops, removing nothing, unless each answers, sets no
cookie and says no placeholder (a publisher's detail the owner fills in `web/scripts/legal-publisher.mjs`); the check fails
closed, so pages it cannot read keep the site private. Then it removes every address rule, reads back that ingress admits
everyone, and only then says the site is open; if it cannot list the rules, or the read-back still finds one, it stops and
says so. From then on every deployment says -Public, and a -Public deployment's probe holds the candidate's legal pages to
the same rule (`--opening`, handed to the probe as one argument: the review of #989), so a release with an unfilled detail
fails its probe, is deactivated and is never offered for promotion. A -Public deployment before the opening is refused
(step 2). (The plan's item 0.5: the first version restricted ingress on a first deployment only, so a second deployment
would have opened the private copy to everyone.)

.EXAMPLE
pwsh -File deploy/deploy.ps1 -Subscription <id> -ResourceGroup rg-lex-v3 -EnvironmentId <managed environment id> `
  -Registry crlex.azurecr.io -IdentityResourceId <user-assigned identity id> -SigningPublicKey release-signing.pem `
  -SignerIdentity <the release signer's identity> -Release C:\releases\v3-... -LiveRevision ca-lex-v3--v3-previous `
  -AllowedCidr 203.0.113.7/32,198.51.100.0/24 -Apply
#>
[CmdletBinding(DefaultParameterSetName = 'Deploy')]
param(
    [Parameter(Mandatory)] [string] $Subscription,
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [string] $AppName = 'ca-lex-v3',

    [Parameter(Mandatory, ParameterSetName = 'Deploy')] [string] $EnvironmentId,
    [Parameter(Mandatory, ParameterSetName = 'Deploy')] [string] $Registry,
    [Parameter(ParameterSetName = 'Deploy')] [string] $Repository = 'lex-v3',
    [Parameter(Mandatory, ParameterSetName = 'Deploy')] [string] $IdentityResourceId,
    [Parameter(Mandatory, ParameterSetName = 'Deploy')] [string] $SigningPublicKey,
    [Parameter(ParameterSetName = 'Deploy')] [string] $SignerIdentity = '',
    [Parameter(Mandatory, ParameterSetName = 'Deploy')] [string] $Release,
    [Parameter(ParameterSetName = 'Deploy')] [string] $LiveRevision = '',
    [Parameter(ParameterSetName = 'Deploy')] [string[]] $AllowedCidr = @(),
    [Parameter(ParameterSetName = 'Deploy')] [switch] $Public,
    [Parameter(ParameterSetName = 'Deploy')] [switch] $Apply,
    [Parameter(ParameterSetName = 'Deploy')] [switch] $BrowserProbe,
    [Parameter(ParameterSetName = 'Deploy')] [switch] $SkipBrowserProbe,
    [Parameter(ParameterSetName = 'Deploy')] [ValidateSet('kept-out', 'indexed', IgnoreCase = $false)] [string] $SearchEngines = 'kept-out',

    [Parameter(Mandatory, ParameterSetName = 'Remove')] [switch] $Remove,
    [Parameter(Mandatory, ParameterSetName = 'Remove')] [string] $Revision,

    [Parameter(Mandatory, ParameterSetName = 'Open')] [switch] $Open
)

$ErrorActionPreference = 'Stop'
$checkout = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'private-copy.ps1')
$AllowedCidr = Get-AllowedCidrs $AllowedCidr

function Step([string] $what) { Write-Host "== $what" }

# Without -BrowserProbe the probes run with the browser trap loaded first, so nothing they run can find or start a browser;
# the opening's check of the legal pages runs under it too. The trap's file URL by the constructor, never a cast: on Linux a
# cast reads `/home/...` as a relative URI, whose AbsoluteUri throws, and PowerShell turns a getter's throw into $null, so
# `--import` took the script's path instead (CI on #979). An empty URL stops the deployment here.
$trapPath = [System.IO.Path]::GetFullPath((Join-Path $checkout 'web/scripts/browser-trap.mjs'))
$trapUrl = if ($IsWindows) { [System.Uri]::new($trapPath).AbsoluteUri } else { [System.Uri]::new("file://$trapPath").AbsoluteUri }
if (-not $trapUrl -or -not $trapUrl.StartsWith('file:///')) { throw "The browser trap's file URL could not be formed ($trapUrl); nothing is deployed." }
# Every argument list below is `@(if (...) { ... })`, an array whatever the branch: PowerShell unrolls an array an `if` returns,
# so `= if (...) { @('--opening') }` was the string '--opening', and splatting a string hands it over one character at a time
# (`- - o p e n i n g`): the probe never read --opening (the review of #989).
$nodeTrap = @(if (-not $BrowserProbe) { '--import', $trapUrl })

function Run([string] $display, [scriptblock] $command) {
    # Every command is shown before it runs; without -Apply it is only shown. No command line carries a secret.
    Write-Host "  > $display"
    if ($Apply -or $Remove -or $Open) {
        & $command
        if ($LASTEXITCODE -ne 0) { throw "failed ($LASTEXITCODE): $display" }
    }
}

if ($Remove) {
    Step "remove: deactivate revision $Revision of $AppName"
    Run "az account set --subscription $Subscription" { az account set --subscription $Subscription }
    Run "az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $Revision" {
        az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $Revision
    }
    exit 0
}

if ($Open) {
    # The owner's opening: every address rule removed, so ingress admits everyone. Never part of a deployment.
    Step "open: $AppName admits everyone"
    Run "az account set --subscription $Subscription" { az account set --subscription $Subscription }
    # The legal pages first (the plan's item 7.1): the site opens only once each of the ten it serves answers, sets no cookie
    # and says no placeholder. The check fails closed: an address that cannot be read, or pages that cannot be, keep it private.
    Step "check the legal pages $AppName serves before it admits everyone"
    $fqdn = az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.fqdn" -o tsv
    if ($LASTEXITCODE -ne 0 -or "$fqdn" -notmatch '^[a-z0-9.-]+$') { throw "The address of $AppName could not be read, so its legal pages could not be checked; nothing was removed, and the site stays private." }
    Write-Host "  > node web/scripts/deploy-probe.mjs --opening --origin https://$fqdn"
    & node @nodeTrap (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --opening --origin "https://$fqdn"
    if ($LASTEXITCODE -ne 0) { throw "The legal pages $AppName serves are not ready to publish (above); nothing was removed, and the site stays private. The owner fills web/scripts/legal-publisher.mjs, and a release built from it is deployed, before the opening." }
    $listed = az containerapp ingress access-restriction list -g $ResourceGroup -n $AppName --query "[].name" -o tsv
    if ($LASTEXITCODE -ne 0) { throw "The address rules of $AppName could not be listed; nothing was removed, and the site stays private." }
    foreach ($rule in @($listed) | Where-Object { $_ }) {
        Run "az containerapp ingress access-restriction remove -g $ResourceGroup -n $AppName --rule-name $rule" {
            az containerapp ingress access-restriction remove -g $ResourceGroup -n $AppName --rule-name $rule
        }
    }
    # Said open only once read back so (review of #960: a failed listing removed nothing and still said open).
    $read = az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.ipSecurityRestrictions" -o json
    if ($LASTEXITCODE -ne 0) { throw "Who ingress admits could not be read back after the opening: check it before saying the site is open." }
    $failure = Get-AdmittedFailure ($read -join "`n") @() $true
    if ($failure) { throw "The opening did not take: $failure." }
    Write-Host "  The site is open. From now on every deployment says -Public, so none restricts it again."
    exit 0
}

if ($Registry -notmatch '^[a-z0-9]+\.azurecr\.io$') { throw "-Registry must be an Azure Container Registry login server, such as crlex.azurecr.io." }
# Until the opening the app is the private copy, on every deployment, not only the first.
if ($Public -and $AllowedCidr.Count -gt 0) {
    throw "-Public admits everyone and names no address range; -AllowedCidr is for the private copy."
}
if (-not $Public -and $AllowedCidr.Count -eq 0) {
    throw "Until the opening every deployment names the address ranges the private copy admits (-AllowedCidr, the operator's address first, such as 203.0.113.7/32). After the owner's opening (-Open), every deployment says -Public."
}
foreach ($cidr in $AllowedCidr) {
    $failure = Get-AllowedCidrFailure $cidr
    if ($failure) { throw "-AllowedCidr $failure." }
}
if ($Apply -and [string]::IsNullOrWhiteSpace($SignerIdentity)) {
    throw "-Apply deploys a release signed by the release signer: name its identity with -SignerIdentity. A rehearsal release is for plan runs only."
}
$signerProbe = @(if (-not [string]::IsNullOrWhiteSpace($SignerIdentity)) { '--signer', $SignerIdentity })
# The browser probes are the operator's to ask for (-BrowserProbe), on a machine where a browser may run; none runs by
# default (the owner's order of 2026-10-16, after headless Chrome runs locked the owner's account; the review of #979: an
# -Apply that forgot -SkipBrowserProbe started one). -SkipBrowserProbe, the old way to say none, is kept and changes nothing.
if ($BrowserProbe -and $SkipBrowserProbe) { throw "-BrowserProbe asks for the browser probes and -SkipBrowserProbe skips them: name one." }
# `$browserArgs`, not `$browserProbe`: PowerShell's names ignore case, and that would be the -BrowserProbe switch itself.
$browserArgs = @(if ($BrowserProbe) { '--browser' })
# Once the site is open (-Public), the candidate's legal pages are held to the opening's rule: none may say a placeholder.
$openingArgs = @(if ($Public) { '--opening' })

# 1. The release, read back under the signing identity's key, before anything touches Azure.
Step "verify the release under the signing identity's public key"
$releasePath = (Resolve-Path $Release).Path
$keyPath = (Resolve-Path $SigningPublicKey).Path
& node @nodeTrap (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --release $releasePath --public-key $keyPath @signerProbe
if ($LASTEXITCODE -ne 0) { throw "The release does not verify under the signing identity's key; nothing is deployed." }

$manifest = Get-Content (Join-Path $releasePath 'release-manifest.json') -Raw | ConvertFrom-Json
$digest = $manifest.image.manifest_digest
if ($digest -notmatch '^sha256:[0-9a-f]{64}$') { throw "The release manifest names no image digest." }
$image = "$Registry/$Repository@$digest"
$suffix = ($manifest.version.ToLowerInvariant() -replace '[^a-z0-9-]', '-')
if ($suffix.Length -gt 40) { $suffix = $suffix.Substring($suffix.Length - 40).TrimStart('-') }
$archive = Join-Path $releasePath 'lex-v3-image.oci.tar'
Write-Host "  release $($manifest.version): image $digest, corpus $($manifest.corpus.sha256)"

# 2. The owner's subscription; and with -Public, that the site is already open. A -Public deployment admits everyone: the
# template's openToPublic empties the address rules as it deploys, before the candidate is probed, and a failed probe
# deactivates only the candidate. On the still-private app the rules would be gone and the live revision open to everyone
# (the review of #989). The opening is -Open's alone, after its check of the legal pages. So the app's rules are read before
# anything in Azure changes: any rule, or a read that fails, stops the deployment here, having changed nothing.
Step "use the subscription $Subscription"
Run "az account set --subscription $Subscription" { az account set --subscription $Subscription }
if ($Public) {
    Step "check that $AppName is open: a -Public deployment is for the open site only"
    if ($Apply) {
        $restrictions = az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.ipSecurityRestrictions" -o json
        if ($LASTEXITCODE -ne 0) { throw "Whether $AppName is open could not be read; nothing was deployed. A -Public deployment is for the open site only." }
        # Any rule at all, whatever it names: "null", "[]" or nothing is the open site's answer.
        $text = $restrictions -join "`n"
        try { $rules = @(if (-not [string]::IsNullOrWhiteSpace($text)) { $text | ConvertFrom-Json | Where-Object { $_ } }) }
        catch { throw "Whether $AppName is open could not be read; nothing was deployed ($($_.Exception.Message))." }
        if ($rules.Count -gt 0) {
            $named = ($rules | ForEach-Object { "$($_.name) $($_.ipAddressRange)".Trim() }) -join ', '
            throw "The site is private: open it with deploy.ps1 -Open first ($($rules.Count) address rule(s): $named). Nothing was deployed: a -Public deployment would remove the address rules before its candidate is probed."
        }
        Write-Host "  $AppName admits everyone: the site is open"
    }
    else {
        Write-Host "  > az containerapp show -g $ResourceGroup -n $AppName --query properties.configuration.ingress.ipSecurityRestrictions -o json: any address rule stops a -Public deployment here, before anything in Azure changes (the site is private: open it with deploy.ps1 -Open first)"
    }
}

# 3. The image into the registry: read out of the release by its manifest digest, tagged with the release version as a
# handle, and deployed below by the digest alone. The registry access token (short-lived, from the owner's session)
# goes on standard input to `oras login`, which keeps it in a registry config in a fresh private directory that only
# `oras cp` and the digest check read; the directory is removed when the copy ends, whatever happens. The token is never
# on a command line, never printed, and never left on disk (the custody probe's runbook did the same).
Step "copy the image into $Registry"
$registryName = $Registry.Split('.')[0]
$tag = ($manifest.version -replace '[^A-Za-z0-9_.-]', '-')
$session = Join-Path ([System.IO.Path]::GetTempPath()) "lex-v3-oras-$([guid]::NewGuid().ToString('N'))"
$registryConfig = Join-Path $session 'registry-config.json'
try {
    if ($Apply) { New-Item -ItemType Directory -Path $session | Out-Null }
    Run "az acr login --name $registryName --expose-token --query accessToken -o tsv | oras login $Registry --username 00000000-0000-0000-0000-000000000000 --password-stdin --registry-config <private session config>" {
        az acr login --name $registryName --expose-token --query accessToken -o tsv |
            oras login $Registry --username 00000000-0000-0000-0000-000000000000 --password-stdin --registry-config $registryConfig
    }
    Run "oras cp --from-oci-layout <release image>@$digest $Registry/${Repository}:$tag --to-registry-config <private session config>" {
        oras cp --from-oci-layout "${archive}@$digest" "$Registry/${Repository}:$tag" --to-registry-config $registryConfig
    }
    Run "oras manifest fetch $image --descriptor --registry-config <private session config>" {
        oras manifest fetch $image --descriptor --registry-config $registryConfig
    }
}
finally {
    if (Test-Path $session) { Remove-Item -Recurse -Force $session -Confirm:$false }
}

# 4. The candidate revision, with no traffic while a live revision serves, admitting only the named ranges until the
# opening. The parameters go in a file, since the ranges are an array; it holds no secret.
Step "deploy the candidate revision $suffix"
# A domain already bound to the app (deploy/bind-domain.ps1) is read back and passed on, so a redeployment keeps it: the
# template would otherwise set the app's custom domains to none. A plan run asks Azure nothing, and says so.
$customDomains = @()
if ($Apply) {
    # A read that fails must stop the deployment, never pass "none" on: the template would unbind the domain (the review
    # of #988). Only a first deployment, with no app yet, has nothing to read.
    $bound = az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.customDomains" -o json
    if ($LASTEXITCODE -ne 0) {
        if (-not [string]::IsNullOrEmpty($LiveRevision)) {
            throw "The custom domains bound to $AppName could not be read; nothing is deployed, so none is unbound."
        }
        Write-Host "  a first deployment: $AppName holds no custom domain yet"
    }
    elseif ($bound) {
        $customDomains = @($bound | ConvertFrom-Json | Where-Object { $_ } | ForEach-Object {
                [ordered]@{ name = $_.name; bindingType = $_.bindingType; certificateId = $_.certificateId } })
    }
    Write-Host "  custom domains kept: $(if ($customDomains.Count) { ($customDomains | ForEach-Object { $_.name }) -join ', ' } else { 'none' })"
}
else {
    Write-Host "  > the custom domains bound to $AppName are read back and kept (a plan run asks Azure nothing)"
}
$values = [ordered]@{
    environmentId = $EnvironmentId; appName = $AppName; image = $image; registryServer = $Registry
    identityResourceId = $IdentityResourceId; revisionSuffix = $suffix; liveRevision = $LiveRevision
    allowedSourceCidrs = @($AllowedCidr); openToPublic = [bool]$Public; searchEngines = $SearchEngines
    customDomains = @($customDomains)
}
$document = [ordered]@{
    '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
    contentVersion = '1.0.0.0'
    parameters = [ordered]@{}
}
foreach ($name in $values.Keys) { $document.parameters[$name] = @{ value = $values[$name] } }
$parametersFile = Join-Path ([System.IO.Path]::GetTempPath()) "lex-v3-deploy-$([guid]::NewGuid().ToString('N')).parameters.json"
$shown = ($values.Keys | ForEach-Object {
        $value = $values[$_]
        $text = if ($_ -eq 'customDomains') { '[' + (($value | ForEach-Object { $_.name }) -join ',') + ']' } elseif ($value -is [array]) { '[' + ($value -join ',') + ']' } else { $value }
        "$_=$text"
    }) -join ' '
try {
    $document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $parametersFile -Encoding utf8NoBOM
    Run "az deployment group what-if -g $ResourceGroup -f deploy/main.bicep -p <parameters: $shown>" {
        az deployment group what-if -g $ResourceGroup -f (Join-Path $checkout 'deploy/main.bicep') -p "@$parametersFile"
    }
    try {
        Run "az deployment group create -g $ResourceGroup -f deploy/main.bicep -p <parameters: $shown>" {
            az deployment group create -g $ResourceGroup -f (Join-Path $checkout 'deploy/main.bicep') -p "@$parametersFile" -o none
        }
        if ($Apply -and $customDomains.Count) {
            # Read back: every domain bound before the deployment is still bound after it.
            $after = @(az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.customDomains[].name" -o json | ConvertFrom-Json)
            $lost = @($customDomains | Where-Object { $after -notcontains $_.name } | ForEach-Object { $_.name })
            if ($lost.Count) { throw "The deployment unbound $($lost -join ', '); run deploy/bind-domain.ps1 -Apply to bind it again." }
            Write-Host "  custom domains still bound: $($after -join ', ')"
        }
    }
    catch {
        # A candidate that never becomes ready (its readiness route answers 503 while it holds no law) fails the deployment
        # itself, at Azure's progress deadline, before the probe can run: it is deactivated here as a failed probe's is, so
        # no revision without the law stays active and billed (review of #963).
        $failedCandidate = "$AppName--$suffix"
        $active = az containerapp revision show -g $ResourceGroup -n $AppName --revision $failedCandidate --query properties.active -o tsv 2>$null
        if ($LASTEXITCODE -eq 0 -and $active -eq 'true') {
            Step "the deployment failed: deactivate the candidate $failedCandidate"
            az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $failedCandidate
        }
        throw "The candidate revision did not deploy ($($_.Exception.Message)); a candidate that never becomes ready holds no law (GET /health/ready answers 503). It was deactivated if it was active, and no traffic moved to it."
    }
}
finally {
    Remove-Item -LiteralPath $parametersFile -ErrorAction SilentlyContinue
}

# 5. Who ingress admits, read back from the app: exactly the named ranges until the opening, everyone after it.
function AdmittedFailure {
    $read = az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.ipSecurityRestrictions" -o json
    if ($LASTEXITCODE -ne 0) { return "who ingress admits could not be read back" }
    return Get-AdmittedFailure ($read -join "`n") $AllowedCidr ([bool]$Public)
}
if ($Apply) {
    Step "read back who ingress admits"
    $failure = AdmittedFailure
    if ($failure -and -not $Public) {
        Write-Host "  ${failure}: setting the private copy's ranges again"
        $index = 0
        foreach ($cidr in $AllowedCidr) {
            $index += 1
            az containerapp ingress access-restriction set -g $ResourceGroup -n $AppName --rule-name "private-copy-$index" --ip-address $cidr --action Allow -o none
        }
        $failure = AdmittedFailure
    }
    if ($failure) { throw "Who ingress admits is not what this deployment names: $failure. Nothing more is done; fix it before anything else." }
    if ($Public) { Write-Host "  ingress admits everyone (-Public)" } else { Write-Host "  ingress admits only $($AllowedCidr -join ', ')" }
}

# 6. The zero-traffic probe against the candidate's own URL.
Step "probe the candidate"
if (-not $Apply) {
    Write-Host "  > wait until $AppName--$suffix is provisioned, active, running and healthy"
    Write-Host "  > node $(($nodeTrap + @('web/scripts/deploy-probe.mjs')) -join ' ') --origin https://<$AppName--$suffix's own address> --release $releasePath --public-key $keyPath $($signerProbe -join ' ') $($browserArgs -join ' ') $($openingArgs -join ' ') --promotion-check"
    Write-Host "  (exit 3: the candidate answers as its release holds and promotion refuses its data's age; no promotion command would be printed)"
    if (-not $BrowserProbe) { Write-Host "  (no browser probe: only the HTTP probes would run, under the browser trap, and no page of the candidate would be walked in a browser; -BrowserProbe walks them, where a browser may run)" }
    Write-Host "== planned only: run again with -Apply to deploy (the owner's decision)"
    exit 0
}
$candidate = "$AppName--$suffix"
# The probe asks the candidate revision at its own address, never through the candidate label: the label follows the
# latest *ready* revision, so while the new revision pulled its image and verified its indexes (about four minutes for the
# population) the label still named the live one, and on 2026-10-18 the probe held the live revision to this release.
# The release card cannot tell two revisions apart in general (a later release over the same corpus and gates answers
# the same bytes), so only the revision's own address proves which revision answered (the review of #985). The probe
# waits until the revision is provisioned, active, running and healthy; a failed or inactive one ends the wait at once.
$readyBy = (Get-Date).AddMinutes(20)
while ($true) {
    $state = az containerapp revision show -g $ResourceGroup -n $AppName --revision $candidate --query "{running:properties.runningState,health:properties.healthState,provisioning:properties.provisioningState,active:properties.active,fqdn:properties.fqdn}" -o json | ConvertFrom-Json
    $failed = "$($state.provisioning)" -eq 'Failed' -or "$($state.running)" -eq 'Failed'
    if ($failed -or ($null -ne $state -and $state.active -eq $false)) {
        Step "the candidate cannot become ready: deactivate the candidate $candidate"
        az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $candidate
        $why = if ($failed) { "it failed (provisioning $($state.provisioning), running $($state.running))" } else { 'it is not active: activate it first if it should be probed' }
        throw "The candidate $candidate cannot become ready: $why. No traffic moved."
    }
    if ($state.health -eq 'Healthy' -and "$($state.running)".StartsWith('Running') -and -not [string]::IsNullOrEmpty($state.fqdn)) {
        Write-Host "  $candidate is $($state.running) and $($state.health), at its own address $($state.fqdn)"
        break
    }
    if ((Get-Date) -gt $readyBy) {
        Step "the candidate did not become ready: deactivate the candidate $candidate"
        az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $candidate
        throw "The candidate $candidate was not ready within 20 minutes ($($state.running), $($state.health)); it was deactivated and no traffic moved."
    }
    Write-Host "  waiting: $candidate is $($state.provisioning), $($state.running), $($state.health)"
    Start-Sleep -Seconds 15
}
$origin = "https://$($state.fqdn)"
if (-not $BrowserProbe) { Write-Host "  no browser probe (no -BrowserProbe): only the HTTP probes run, under the browser trap, and no page of the candidate is walked in a browser" }
& node @nodeTrap (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --origin $origin --release $releasePath --public-key $keyPath @signerProbe @browserArgs @openingArgs --promotion-check
# Exit 3: the candidate answers as its release holds, and promotion refuses its data's age (R87; O11's maximum, which every
# answer states): a publisher channel's newest completed collection is past it, or not stated. The candidate stays, with no
# traffic, and no promotion command is printed.
$dataAgeRefused = $LASTEXITCODE -eq 3
if ($LASTEXITCODE -ne 0 -and -not $dataAgeRefused) {
    # 7. The removal step: the candidate never carried traffic (or only the allowed addresses'), and is deactivated.
    Step "the probe failed: deactivate the candidate $candidate"
    az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $candidate
    throw "The candidate failed its probe and was deactivated; no traffic moved."
}

Step "the candidate $candidate answers as the release holds"
Write-Host "  Promotion is the owner's decision and is not run here."
if ($dataAgeRefused) {
    Write-Host "  Promotion refuses this release's data: the probe above says which publisher's data is past the maximum data age, or"
    Write-Host "  states no completed collection. No promotion command is printed; a release with newer data is the way forward."
}
elseif ([string]::IsNullOrEmpty($LiveRevision)) {
    Write-Host "  A first deployment already routes to the candidate."
}
else {
    Write-Host "  To move all traffic from $LiveRevision to the candidate:"
    Write-Host "  > az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $candidate=100"
}
Write-Host "  To remove it instead: pwsh -File deploy/deploy.ps1 -Subscription $Subscription -ResourceGroup $ResourceGroup -AppName $AppName -Remove -Revision $candidate"
if (-not $Public) {
    Write-Host "  The site stays private: ingress admits only $($AllowedCidr -join ', '). The opening, the owner's, on the opening day:"
    Write-Host "  > pwsh -File deploy/deploy.ps1 -Subscription $Subscription -ResourceGroup $ResourceGroup -AppName $AppName -Open"
}
if (-not [string]::IsNullOrEmpty($LiveRevision) -and -not $dataAgeRefused) {
    # Rollback and forward again are traffic moves, the owner's like promotion: printed, never run.
    # Each active revision runs a billed replica (1 vCPU, 2 GiB: about 23 EUR a month idle), so the budget of about 30 a
    # month holds one: after a promotion the previous revision is deactivated, kept but stopped (the review of #950).
    Write-Host "  After promotion, deactivate $LiveRevision so one replica is billed (it is kept, and can be activated again):"
    Write-Host "  > az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $LiveRevision"
    Write-Host "  To roll back: activate it, wait until it is ready (about a minute to mount), then move the traffic:"
    Write-Host "  > az containerapp revision activate -g $ResourceGroup -n $AppName --revision $LiveRevision"
    Write-Host "  > az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $LiveRevision=100"
    Write-Host "  and forward again to the candidate:"
    Write-Host "  > az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $candidate=100"
}
