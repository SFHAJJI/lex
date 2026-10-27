<#
.SYNOPSIS
The owner's go-live for Lex V3, as one command: a verified release deployed as a zero-traffic candidate revision of the
one-server container, probed, and removed again if the probe fails; or, with -Maintenance, the release's maintenance
revision, built the same way, probed and kept inactive until it is switched on.

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
   #989: on the private copy the rules were removed, and a failed probe deactivated only the candidate). Without -Public the
   same rules are read, and none at all (the open site) stops the deployment too: on the open site every deployment says
   -Public, since -AllowedCidr would restrict it again; a read that fails stops a redeployment. A plan run says so;
   then the app's revisions are read, also before anything in Azure changes, and on a redeployment its traffic: -LiveRevision
   must be the revision that holds all of it now, since the template keeps it at 100 per cent and gives the new revision
   none (naming a candidate not yet promoted would move every reader to it, unprobed); traffic no one revision holds, or a
   read that fails, stops it too. The candidate is named
   `<app>--<release>-<digest>`: the release's version, and the first 8 hex digits of a SHA-256 over what makes the
   revision (the image, -SearchEngines, -Public, the -AllowedCidr ranges and deploy/main.bicep), so the same release
   deployed again with another setting is a revision of its own (the incident of 2026-10-27, STATUS-WEB.md). A name the
   app already holds, or -LiveRevision's, stops the deployment there, having changed nothing; a read that fails stops it
   too, unless it is a first deployment (no -LiveRevision). The plan prints the name;
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
7. if the probe fails, the candidate revision is deactivated (the removal step) and the script exits non-zero. Every
   deactivation the deployment makes (a failed deployment, a candidate that fails or is never ready, a failed probe) is
   of a revision this deployment created, one that was not among the revisions read in step 2; if they could not be read,
   nothing is deactivated, and the script says so.
Promotion (moving traffic to the candidate) is printed for the owner and never run.

-Maintenance builds the release's maintenance revision (the specification of record's R60: "a maintenance page shows during
deployments"; R108 pre-authorises the page, R115 makes it the fallback of the first public exposure, the plan's item 10.14
asks for it pre-built). It takes steps 1 to 5 as a deployment does, from the same verified release and image, and deploys
the template with maintenance=true: the revision runs with LEX_V3_MAINTENANCE=on, mounts nothing and serves only the
maintenance page, and arrives as the `maintenance` label with no traffic while -LiveRevision keeps 100 per cent. It is
never a first deployment: without -LiveRevision it is refused before anything changes. Its name's digest covers maintenance
too, so it is never the release's candidate. Step 6 waits until it is ready and probes it at its own address
(`web/scripts/deploy-probe.mjs --maintenance`, under the browser trap; never -BrowserProbe): readiness 204; each page, the
root included, 503 with Retry-After, a page's headers and the release image's own maintenance page, byte for byte; the API
answering the problem service_in_maintenance; no cookie. Then it is deactivated and kept (an inactive revision bills
nothing), or, if the probe fails, deactivated and the script exits non-zero, each through the same guard as a candidate.
Switching it on (activate it, then move all traffic to it) and off (move the traffic back, then deactivate it) are traffic
moves, the owner's as a promotion is: printed, never run. Every deployment's closing lines say how to pre-build its
release's maintenance revision.

-Remove deactivates one revision of the app and exits: the removal step on its own.

-Open is the owner's opening, on the opening day and never otherwise: it first asks the site's ten legal pages
(`web/scripts/deploy-probe.mjs --opening`, the plan's item 7.1) and stops, removing nothing, unless each answers, sets no
cookie and shows no e-mail address, mail or telephone link, placeholder or postcode (the owner's decision of 2026-10-19:
the site names no person and gives no postal address, no e-mail address and no contact); the check fails closed, so pages
it cannot read keep the site private. Then it removes every address rule, reads back that ingress admits everyone, and only
then says the site is open; if it cannot list the rules, or the read-back still finds one, it stops and says so. From then
on every deployment says -Public, and a -Public deployment's probe holds the candidate's legal pages to the same rule
(`--opening`, handed to the probe as one argument: the review of #989), so a release whose legal pages show any of those
fails its probe, is deactivated and is never offered for promotion. A -Public deployment before the opening is refused
(step 2). (The plan's item 0.5: the first version restricted ingress on a first deployment only, so a second deployment
would have opened the private copy to everyone.)

.EXAMPLE
pwsh -File deploy/deploy.ps1 -Subscription <id> -ResourceGroup rg-lex-v3 -EnvironmentId <managed environment id> `
  -Registry crlex.azurecr.io -IdentityResourceId <user-assigned identity id> -SigningPublicKey release-signing.pem `
  -SignerIdentity <the release signer's identity> -Release C:\releases\v3-... -LiveRevision ca-lex-v3--v3-previous `
  -AllowedCidr 203.0.113.7/32,198.51.100.0/24 -Apply

.EXAMPLE
pwsh -File deploy/deploy.ps1 -Subscription <id> -ResourceGroup rg-lex-v3 -EnvironmentId <managed environment id> `
  -Registry crlex.azurecr.io -IdentityResourceId <user-assigned identity id> -SigningPublicKey release-signing.pem `
  -SignerIdentity <the release signer's identity> -Release C:\releases\v3-... -LiveRevision <the revision serving now> `
  -AllowedCidr 203.0.113.7/32,198.51.100.0/24 -Maintenance -Apply
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
    [Parameter(ParameterSetName = 'Deploy')] [switch] $Maintenance,

    [Parameter(Mandatory, ParameterSetName = 'Remove')] [switch] $Remove,
    [Parameter(Mandatory, ParameterSetName = 'Remove')] [string] $Revision,

    [Parameter(Mandatory, ParameterSetName = 'Open')] [switch] $Open
)

$ErrorActionPreference = 'Stop'
$checkout = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'private-copy.ps1')
$AllowedCidr = Get-AllowedCidrs $AllowedCidr

function Step([string] $what) { Write-Host "== $what" }

function Get-TextDigest([string] $Text) {
    [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()
}

# The candidate revision's suffix: the release's version, and the first 8 hex digits of a SHA-256 over the settings that make
# the revision, so each deployment of a release with other settings names a revision of its own, and the same settings the
# same one (refused below, before anything changes, if Azure holds it). A revision is named <app>--<suffix>: the suffix is
# lower-case letters, digits and '-', begins with a letter, ends with a letter or a digit, has no '--' and is at most 64
# characters (the Container Apps documentation, "Revisions"; az refuses '--' too), and the revision's own address, which the
# probe asks, is <revision>.<environment domain>, so the whole name is one DNS label of at most 63 characters. A release's
# version too long for the room left keeps its end (the time and the commit) and the digest; a name that cannot be formed
# stops the deployment before anything runs.
function Get-RevisionSuffix([string] $Version, [string] $App, [System.Collections.IDictionary] $Settings) {
    $text = ($Settings.Keys | ForEach-Object {
            $value = $Settings[$_]
            $shown = if ($value -is [array]) { $value -join ',' } elseif ($value -is [bool]) { "$value".ToLowerInvariant() } else { "$value" }
            "$_=$shown"
        }) -join "`n"
    $settingsDigest = (Get-TextDigest $text).Substring(0, 8)
    $release = (($Version.ToLowerInvariant() -replace '[^a-z0-9-]', '-') -replace '-{2,}', '-').Trim('-')
    $room = 63 - "$App--".Length - "-$settingsDigest".Length
    if ($room -gt 0 -and $release.Length -gt $room) { $release = $release.Substring($release.Length - $room) -replace '^[^a-z]+', '' }
    $suffix = "$release-$settingsDigest"
    if ($room -le 0 -or $suffix -cnotmatch '^[a-z][a-z0-9-]*[a-z0-9]$' -or $suffix.Contains('--') -or $suffix.Length -gt 64 -or "$App--$suffix".Length -gt 63) {
        throw "No revision of $App can be named for this release ($Version): a revision's name is <app>--<suffix>, at most 63 characters, and its suffix is lower-case letters, digits and '-', a letter first. Nothing is deployed."
    }
    return $suffix
}

# The revision that serves all of the app's traffic, from what Azure answers for its ingress traffic and its latest revision
# (`{traffic, latest}` as JSON): the one entry that carries 100 per cent, named by its revision or, for the latest-revision
# entry a first deployment leaves, by the app's latest revision; null when no one revision carries all of it (split traffic,
# none, or an answer that cannot be read).
function Get-ServingRevision([string] $Text) {
    try { $routing = $Text | ConvertFrom-Json } catch { return $null }
    if ($null -eq $routing) { return $null }
    $entries = @($routing.traffic | Where-Object { $_ -and [int]$_.weight -gt 0 })
    if ($entries.Count -ne 1 -or [int]$entries[0].weight -ne 100) { return $null }
    if (-not [string]::IsNullOrEmpty($entries[0].revisionName)) { return [string]$entries[0].revisionName }
    if ($entries[0].latestRevision -eq $true -and -not [string]::IsNullOrEmpty($routing.latest)) { return [string]$routing.latest }
    return $null
}

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
    # and shows no e-mail address, mail or telephone link, placeholder or postcode (the owner's decision of 2026-10-19). The
    # check fails closed: an address that cannot be read, or pages that cannot be, keep it private.
    Step "check the legal pages $AppName serves before it admits everyone"
    $fqdn = az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.fqdn" -o tsv
    if ($LASTEXITCODE -ne 0 -or "$fqdn" -notmatch '^[a-z0-9.-]+$') { throw "The address of $AppName could not be read, so its legal pages could not be checked; nothing was removed, and the site stays private." }
    Write-Host "  > node web/scripts/deploy-probe.mjs --opening --origin https://$fqdn"
    & node @nodeTrap (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --opening --origin "https://$fqdn"
    if ($LASTEXITCODE -ne 0) { throw "The legal pages $AppName serves are not ready to publish (above); nothing was removed, and the site stays private. A release whose legal pages pass the check is deployed before the opening." }
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
# Once the site is open (-Public), the candidate's legal pages are held to the opening's rule: none may show an e-mail
# address, a mail or telephone link, a placeholder or a postcode.
$openingArgs = @(if ($Public) { '--opening' })
# The release's maintenance revision (-Maintenance; R60, R108, R115; the plan's item 10.14) is built beside the revision that
# serves now, -LiveRevision, which the deployment keeps at all the traffic (it must already hold it: checked below, before
# anything in Azure changes): it is never a first deployment (the template's first-deployment rule would send all traffic to
# it), and it is probed over HTTP only, under the browser trap. Both are refused before anything is read.
if ($Maintenance -and [string]::IsNullOrEmpty($LiveRevision)) {
    throw "-Maintenance builds the release's maintenance revision beside the revision serving now: name that revision, the one that holds all the traffic, with -LiveRevision. A maintenance revision is never a first deployment. Nothing was deployed and nothing changed."
}
if ($Maintenance -and $BrowserProbe) { throw "-Maintenance is probed over HTTP only, under the browser trap: leave out -BrowserProbe." }
# What the revision this run makes is called in what it says: the candidate, or the maintenance revision.
$role = if ($Maintenance) { 'maintenance revision' } else { 'candidate' }

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
# Each deployment's revision is its own (the incident of 2026-10-27: the live release, deployed again to change only
# LEX_V3_SEARCH_ENGINES, was given the live revision's own suffix; Azure refused it, "revision with suffix ... already
# exists", and the failure step then deactivated the revision of that name, the live one: the site was down three minutes).
# The digest is of what makes the revision: the image, the settings the template is handed for it, and the template itself.
$revisionSettings = [ordered]@{
    image = $image; searchEngines = $SearchEngines; openToPublic = [bool]$Public; allowedSourceCidrs = @($AllowedCidr)
    template = Get-TextDigest ((Get-Content -Raw -LiteralPath (Join-Path $checkout 'deploy/main.bicep')) -replace "`r`n", "`n")
}
# The maintenance revision's settings say so, so its name is never the release's candidate's; a candidate's are as before.
if ($Maintenance) { $revisionSettings['maintenance'] = $true }
$suffix = Get-RevisionSuffix $manifest.version $AppName $revisionSettings
$candidate = "$AppName--$suffix"
$archive = Join-Path $releasePath 'lex-v3-image.oci.tar'
Write-Host "  release $($manifest.version): image $digest, corpus $($manifest.corpus.sha256)"
Write-Host "  revision ${candidate}: the release's suffix and the digest $($suffix.Substring($suffix.Length - 8)) of this deployment's settings (the image, searchEngines $SearchEngines, openToPublic $([bool]$Public), allowedSourceCidrs [$($AllowedCidr -join ',')], deploy/main.bicep$(if ($Maintenance) { ', maintenance' }))"
if ($Maintenance) { Write-Host "  the release's maintenance revision: LEX_V3_MAINTENANCE=on, no mount, only the maintenance page, no traffic beside $LiveRevision" }

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
else {
    # And the other way round: once the owner has opened the site, a deployment without -Public would put the private copy's
    # address rules back as it deploys, closing the open site to everyone but the named ranges. The rules are read before
    # anything in Azure changes: none at all is the open site, and stops the deployment here. A read that fails stops a
    # redeployment too; only a first deployment, with no app yet, has nothing to read.
    Step "check that $AppName is still the private copy: on the open site every deployment says -Public"
    if ($Apply) {
        $restrictions = az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.ipSecurityRestrictions" -o json
        if ($LASTEXITCODE -ne 0) {
            if (-not [string]::IsNullOrEmpty($LiveRevision)) { throw "Who $AppName admits could not be read; nothing was deployed and nothing changed: a deployment without -Public on the open site would restrict it again." }
            Write-Host "  a first deployment: $AppName holds no address rule yet"
        }
        else {
            $text = $restrictions -join "`n"
            try { $rules = @(if (-not [string]::IsNullOrWhiteSpace($text)) { $text | ConvertFrom-Json | Where-Object { $_ } }) }
            catch { throw "Who $AppName admits could not be read; nothing was deployed and nothing changed ($($_.Exception.Message))." }
            if ($rules.Count -eq 0) { throw "The site is open: every deployment says -Public. Nothing was deployed and nothing changed: a deployment naming -AllowedCidr would restrict the open site to those ranges again." }
            Write-Host "  $AppName admits only its address rules: the private copy"
        }
    }
    else {
        Write-Host "  > az containerapp show -g $ResourceGroup -n $AppName --query properties.configuration.ingress.ipSecurityRestrictions -o json: no address rule at all is the open site, and stops a deployment without -Public here, before anything in Azure changes"
    }
}
# The app's revisions, read before anything in Azure changes, every inactive one too: the deployment never names one that
# exists, and a failure deactivates only a revision this deployment made (the incident of 2026-10-27). A read that fails stops
# a redeployment here; a first deployment, whose app holds none yet, goes on, and a failure of it then deactivates nothing.
Step "read the revisions of ${AppName}: the $role $candidate must be one it does not hold"
$priorRevisions = $null
if ($Apply) {
    $listed = az containerapp revision list -g $ResourceGroup -n $AppName --all --query "[].name" -o tsv
    if ($LASTEXITCODE -eq 0) {
        $priorRevisions = @($listed | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
        Write-Host "  $AppName holds $($priorRevisions.Count) revision(s), active and inactive"
        # A redeployment names the revision that serves, and it must be one of the app's: without it the template's first
        # deployment rule sends all traffic to the unprobed candidate (the review of the incident fix, its note 1).
        if ($priorRevisions.Count -gt 0 -and [string]::IsNullOrEmpty($LiveRevision)) {
            throw "$AppName holds $($priorRevisions.Count) revision(s), so this is a redeployment: name the revision serving now with -LiveRevision. Without it the template sends all traffic to the candidate before its probe. Nothing was deployed and nothing changed."
        }
        if (-not [string]::IsNullOrEmpty($LiveRevision) -and $priorRevisions -notcontains $LiveRevision) {
            throw "-LiveRevision $LiveRevision is not a revision of $AppName (it holds: $($priorRevisions -join ', ')). Nothing was deployed and nothing changed."
        }
    }
    elseif (-not [string]::IsNullOrEmpty($LiveRevision)) {
        throw "The revisions of $AppName could not be read; nothing was deployed: without them a failure could not tell a revision this deployment made from one that serves (the incident of 2026-10-27)."
    }
    else {
        Write-Host "  a first deployment: the revisions of $AppName could not be read (it holds none yet), so should this deployment fail, no revision can be told to be its own, and none is deactivated"
    }
}
else {
    Write-Host "  > az containerapp revision list -g $ResourceGroup -n $AppName --all --query [].name -o tsv: a revision already named $candidate stops the deployment here, before anything in Azure changes"
}
# The revision -LiveRevision names must be the one serving all the traffic now, read from the app before anything in Azure
# changes: the template keeps -LiveRevision at 100 per cent and gives the new revision none, so naming another revision (a
# candidate not yet promoted, as a pre-build line run too early would) moves every reader to it, unprobed. On every
# redeployment, the maintenance revision's included; a read that fails, or traffic that no one revision holds, stops it too.
if (-not [string]::IsNullOrEmpty($LiveRevision)) {
    Step "check that $LiveRevision serves all the traffic of $AppName"
    if ($Apply) {
        $routing = az containerapp show -g $ResourceGroup -n $AppName --query "{traffic: properties.configuration.ingress.traffic, latest: properties.latestRevisionName}" -o json
        if ($LASTEXITCODE -ne 0) { throw "The traffic of $AppName could not be read; nothing was deployed and nothing changed: the deployment keeps -LiveRevision at all the traffic, so it must be the revision that holds it now." }
        $serving = Get-ServingRevision ($routing -join "`n")
        if ($serving -ne $LiveRevision) {
            $which = if ($null -eq $serving) { 'no one revision holds all of it' } else { "$serving holds all of it" }
            throw "-LiveRevision $LiveRevision is not the revision serving all the traffic of $AppName ($which). Nothing was deployed and nothing changed: the template would give $LiveRevision all the traffic. Name the revision that serves now (after a promotion, the promoted one)."
        }
        Write-Host "  $LiveRevision serves all the traffic"
    }
    else {
        Write-Host "  > az containerapp show -g $ResourceGroup -n $AppName --query {traffic: properties.configuration.ingress.traffic, latest: properties.latestRevisionName} -o json: unless $LiveRevision holds all the traffic, the deployment stops here, before anything in Azure changes"
    }
}
$held = @(@($priorRevisions) + @($LiveRevision) | Where-Object { $_ })
if ($held -contains $candidate) {
    $which = if ($candidate -eq $LiveRevision) { 'it is -LiveRevision, the revision serving now' } else { 'an earlier deployment of this release with these same settings' }
    throw "The revision $candidate already exists in $AppName ($which): a deployment makes a revision of its own and never names one that exists. Nothing was deployed and nothing changed. Another setting or a new release names a new revision; one that exists is activated and probed by hand, not deployed again."
}
# Only a revision this deployment made is ever deactivated by it: one whose name was not among the app's revisions read above.
# When they could not be read, none can be told to be its own, and nothing is deactivated: it is said so, and left to -Remove.
$candidateIsOurs = $null -ne $priorRevisions -and $priorRevisions -notcontains $candidate -and $candidate -ne $LiveRevision
function Disable-Candidate([string] $Why, [switch] $IfActive) {
    if (-not $candidateIsOurs) {
        Step "${Why}: the $role $candidate is not deactivated"
        Write-Host "  whether $candidate is this deployment's own is not known (the revisions of $AppName could not be read before it), so nothing is deactivated: check it with az containerapp revision list -g $ResourceGroup -n $AppName --all, and remove it with deploy.ps1 -Remove -Revision $candidate if it is"
        return
    }
    if ($IfActive) {
        $active = az containerapp revision show -g $ResourceGroup -n $AppName --revision $candidate --query properties.active -o tsv 2>$null
        if ($LASTEXITCODE -ne 0 -or $active -ne 'true') { return }
    }
    Step "${Why}: deactivate the $role $candidate"
    az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $candidate
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
Step "deploy the $(if ($Maintenance) { 'maintenance' } else { 'candidate' }) revision $suffix"
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
    customDomains = @($customDomains); maintenance = [bool]$Maintenance
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
        # no revision without the law stays active and billed (review of #963). Only if this deployment made it, and it is
        # active: a deployment Azure refused made none, and the revision of that name is another's (the incident of
        # 2026-10-27, when it was the live one).
        $failure = $_.Exception.Message
        Disable-Candidate 'the deployment failed' -IfActive
        $outcome = if ($candidateIsOurs) { 'It was deactivated if it was active, and no traffic moved to it.' } else { "Nothing was deactivated: whether $candidate is this deployment's own is not known (above); no traffic moved to it." }
        if ($Maintenance) { throw "The maintenance revision did not deploy ($failure); one that never becomes ready holds no maintenance page in each interface language (GET /health/ready answers 503). $outcome" }
        throw "The candidate revision did not deploy ($failure); a candidate that never becomes ready holds no law (GET /health/ready answers 503). $outcome"
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

# Switching the maintenance page on and off moves traffic, the owner's as a promotion is (R108 pre-authorises the page once
# O17 is adopted; the plan's item 10.14): printed, never run.
function Write-MaintenanceSwitch {
    Write-Host "  Switching the maintenance page on or off moves traffic, the owner's as a promotion is (R108 pre-authorises it once O17 is adopted); it is not run here."
    Write-Host "  To switch it on: activate $candidate, wait until it is ready (it mounts nothing: seconds once it has started), then move all traffic to it:"
    Write-Host "  > az containerapp revision activate -g $ResourceGroup -n $AppName --revision $candidate"
    Write-Host "  > az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $candidate=100"
    Write-Host "  To switch it off: move all traffic back to $LiveRevision (or to the revision that is to serve then), then deactivate ${candidate}:"
    Write-Host "  > az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $LiveRevision=100"
    Write-Host "  > az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $candidate"
}

# 6. The zero-traffic probe against the candidate's own URL (the maintenance revision's: against its own).
if ($Maintenance) { Step "probe the maintenance revision" } else { Step "probe the candidate" }
if (-not $Apply -and $Maintenance) {
    Write-Host "  > wait until $candidate is provisioned, active, running and healthy (it mounts nothing, so it is ready once it has started)"
    Write-Host "  > node $(($nodeTrap + @('web/scripts/deploy-probe.mjs')) -join ' ') --maintenance --origin https://<$candidate's own address> --release $releasePath --public-key $keyPath $($signerProbe -join ' ')"
    Write-Host "  (only the maintenance page and its readiness are probed, over HTTP, under the browser trap)"
    Write-Host "  > deactivate $candidate once it passes: it is kept, inactive, for the day it is switched on (an inactive revision bills nothing); one that fails is deactivated too"
    Write-MaintenanceSwitch
    Write-Host "== planned only: run again with -Apply to build the maintenance revision (the owner's decision)"
    exit 0
}
if (-not $Apply) {
    Write-Host "  > wait until $AppName--$suffix is provisioned, active, running and healthy"
    Write-Host "  > node $(($nodeTrap + @('web/scripts/deploy-probe.mjs')) -join ' ') --origin https://<$AppName--$suffix's own address> --release $releasePath --public-key $keyPath $($signerProbe -join ' ') $($browserArgs -join ' ') $($openingArgs -join ' ') --promotion-check --search-engines $SearchEngines"
    Write-Host "  (exit 3: the candidate answers as its release holds and promotion refuses its data's age or its evaluation card; no promotion command would be printed)"
    if (-not $BrowserProbe) { Write-Host "  (no browser probe: only the HTTP probes would run, under the browser trap, and no page of the candidate would be walked in a browser; -BrowserProbe walks them, where a browser may run)" }
    Write-Host "== planned only: run again with -Apply to deploy (the owner's decision)"
    exit 0
}
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
        Disable-Candidate 'the candidate cannot become ready'
        $why = if ($failed) { "it failed (provisioning $($state.provisioning), running $($state.running))" } else { 'it is not active: activate it first if it should be probed' }
        throw "The $role $candidate cannot become ready: $why. No traffic moved."
    }
    if ($state.health -eq 'Healthy' -and "$($state.running)".StartsWith('Running') -and -not [string]::IsNullOrEmpty($state.fqdn)) {
        Write-Host "  $candidate is $($state.running) and $($state.health), at its own address $($state.fqdn)"
        break
    }
    if ((Get-Date) -gt $readyBy) {
        Disable-Candidate 'the candidate did not become ready'
        throw "The $role $candidate was not ready within 20 minutes ($($state.running), $($state.health)); $(if ($candidateIsOurs) { 'it was deactivated' } else { 'it was not deactivated (above)' }) and no traffic moved."
    }
    Write-Host "  waiting: $candidate is $($state.provisioning), $($state.running), $($state.health)"
    Start-Sleep -Seconds 15
}
$origin = "https://$($state.fqdn)"
if ($Maintenance) {
    # The maintenance revision at its own address: its readiness, its pages (the release image's own maintenance page, 503,
    # Retry-After and a page's headers), the API's 503 problem and no cookie, over HTTP only, under the browser trap.
    & node @nodeTrap (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --maintenance --origin $origin --release $releasePath --public-key $keyPath @signerProbe
    if ($LASTEXITCODE -ne 0) {
        Disable-Candidate 'the maintenance probe failed'
        if ($candidateIsOurs) { throw "The maintenance revision failed its probe and was deactivated; no traffic moved." }
        throw "The maintenance revision failed its probe and was not deactivated (above); no traffic moved."
    }
    # Pre-built and kept inactive until it is switched on: an inactive revision bills nothing. Through the same guard as a
    # failed candidate's, so only a revision this run made is ever deactivated.
    Disable-Candidate 'the maintenance revision answers as its release holds, and is kept inactive until it is switched on'
    if ($LASTEXITCODE -ne 0) { throw "The maintenance revision $candidate passed its probe and could not be deactivated: deactivate it with deploy.ps1 -Remove -Revision $candidate, since an active revision is billed. No traffic moved." }
    Step "the maintenance revision $candidate is built, probed and inactive, beside $LiveRevision, which serves all the traffic"
    Write-MaintenanceSwitch
    exit 0
}
if (-not $BrowserProbe) { Write-Host "  no browser probe (no -BrowserProbe): only the HTTP probes run, under the browser trap, and no page of the candidate is walked in a browser" }
& node @nodeTrap (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --origin $origin --release $releasePath --public-key $keyPath @signerProbe @browserArgs @openingArgs --promotion-check --search-engines $SearchEngines
# The probe holds robots.txt and the sitemap to this deployment's -SearchEngines (the probe of 2026-10-27 held an indexed
# candidate to the kept-out robots.txt, and rightly deactivated it).
# Exit 3: the candidate answers as its release holds, and promotion refuses it: its data's age (R87; O11's maximum, which
# every answer states: a publisher channel's newest completed collection is past it, or not stated), or its signed
# evaluation card (R104, O20: a gate that did not pass, a control that missed its shuffle, or a card run over another
# mount). The candidate stays, with no traffic, and no promotion command is printed.
$promotionRefused = $LASTEXITCODE -eq 3
if ($LASTEXITCODE -ne 0 -and -not $promotionRefused) {
    # 7. The removal step: the candidate never carried traffic (or only the allowed addresses'), and is deactivated, if this
    # deployment made it.
    Disable-Candidate 'the probe failed'
    if ($candidateIsOurs) { throw "The candidate failed its probe and was deactivated; no traffic moved." }
    throw "The candidate failed its probe and was not deactivated (above); no traffic moved."
}

Step "the candidate $candidate answers as the release holds"
Write-Host "  Promotion is the owner's decision and is not run here."
if ($promotionRefused) {
    Write-Host "  Promotion refuses this release: the probe above says why (a publisher's data past the maximum data age or with no"
    Write-Host "  completed collection, or a gate of its evaluation card that does not pass). No promotion command is printed; a release"
    Write-Host "  with newer data, or whose card passes, is the way forward."
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
if (-not [string]::IsNullOrEmpty($LiveRevision) -and -not $promotionRefused) {
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
if (-not $promotionRefused) {
    # R60: a deployment that must take the site down shows the maintenance page. Each release's is pre-built beside the
    # revision that serves (-Maintenance), from this command's own parameters, and kept inactive until it is switched on. It
    # is run once this release is promoted, naming the revision that serves then; the line printed names the one serving as
    # it is printed (-LiveRevision, or on a first deployment the candidate, which takes the traffic), and the kit refuses any
    # -LiveRevision that does not hold all the traffic, so run too early or too late it changes nothing.
    $ranges = if ($Public) { '-Public' } else { "-AllowedCidr $($AllowedCidr -join ',')" }
    $servingNow = if ([string]::IsNullOrEmpty($LiveRevision)) { $candidate } else { $LiveRevision }
    Write-Host "  To pre-build this release's maintenance revision (R60: the page shown while a deployment must take the site down): run this after the promotion, with -LiveRevision naming the revision that serves then (the kit refuses one that does not hold all the traffic). The revision serving as this is printed is $($servingNow):"
    Write-Host "  > pwsh -File deploy/deploy.ps1 -Subscription $Subscription -ResourceGroup $ResourceGroup -AppName $AppName -EnvironmentId $EnvironmentId -Registry $Registry -Repository $Repository -IdentityResourceId $IdentityResourceId -SigningPublicKey '$($keyPath -replace "'", "''")' -SignerIdentity '$($SignerIdentity -replace "'", "''")' -Release '$($releasePath -replace "'", "''")' $ranges -SearchEngines $SearchEngines -LiveRevision $servingNow -Maintenance -Apply"
}
