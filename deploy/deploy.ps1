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
   --origin); nothing is deployed from a release that does not verify;
2. the release image is copied into the registry out of the release by its manifest digest (`oras cp`); the app is
   deployed by that digest, never by a tag;
3. `deploy/main.bicep` is deployed: the image by digest as a new revision with the `candidate` label and no traffic
   while -LiveRevision keeps 100 per cent (on a first deployment ingress admits only -ProbeSourceCidr);
4. the zero-traffic probe runs against the candidate's own URL (`web/scripts/deploy-probe.mjs --origin`), the browser
   probes included (`--browser`: the journey's real-mount steps through a browser; Chrome or Edge must be installed),
   unless -SkipBrowserProbe says they are skipped;
5. if the probe fails, the candidate revision is deactivated (the removal step) and the script exits non-zero.
Promotion (moving traffic to the candidate) is printed for the owner and never run.

-Remove deactivates one revision of the app and exits: the removal step on its own.

.EXAMPLE
pwsh -File deploy/deploy.ps1 -Subscription <id> -ResourceGroup rg-lex-v3 -EnvironmentId <managed environment id> `
  -Registry crlex.azurecr.io -IdentityResourceId <user-assigned identity id> -SigningPublicKey release-signing.pem `
  -Release C:\releases\v3-... -LiveRevision ca-lex-v3--v3-previous -Apply
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
    [Parameter(Mandatory, ParameterSetName = 'Deploy')] [string] $Release,
    [Parameter(ParameterSetName = 'Deploy')] [string] $LiveRevision = '',
    [Parameter(ParameterSetName = 'Deploy')] [string] $ProbeSourceCidr = '',
    [Parameter(ParameterSetName = 'Deploy')] [switch] $Apply,
    [Parameter(ParameterSetName = 'Deploy')] [switch] $SkipBrowserProbe,

    [Parameter(Mandatory, ParameterSetName = 'Remove')] [switch] $Remove,
    [Parameter(Mandatory, ParameterSetName = 'Remove')] [string] $Revision
)

$ErrorActionPreference = 'Stop'
$checkout = Split-Path -Parent $PSScriptRoot

function Step([string] $what) { Write-Host "== $what" }

function Run([string] $display, [scriptblock] $command) {
    # Every command is shown before it runs; without -Apply it is only shown. No command line carries a secret.
    Write-Host "  > $display"
    if ($Apply -or $Remove) {
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

if ($Registry -notmatch '^[a-z0-9]+\.azurecr\.io$') { throw "-Registry must be an Azure Container Registry login server, such as crlex.azurecr.io." }
if (-not [string]::IsNullOrEmpty($ProbeSourceCidr) -and -not [string]::IsNullOrEmpty($LiveRevision)) {
    throw "-ProbeSourceCidr applies to a first deployment only; with -LiveRevision the candidate carries no traffic and needs no restriction."
}
if ([string]::IsNullOrEmpty($LiveRevision) -and [string]::IsNullOrEmpty($ProbeSourceCidr)) {
    throw "A first deployment (no -LiveRevision) needs -ProbeSourceCidr, so that only the owner's probe reaches the candidate until promotion."
}

# 1. The release, read back under the signing identity's key, before anything touches Azure.
Step "verify the release under the signing identity's public key"
$releasePath = (Resolve-Path $Release).Path
$keyPath = (Resolve-Path $SigningPublicKey).Path
& node (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --release $releasePath --public-key $keyPath
if ($LASTEXITCODE -ne 0) { throw "The release does not verify under the signing identity's key; nothing is deployed." }

$manifest = Get-Content (Join-Path $releasePath 'release-manifest.json') -Raw | ConvertFrom-Json
$digest = $manifest.image.manifest_digest
if ($digest -notmatch '^sha256:[0-9a-f]{64}$') { throw "The release manifest names no image digest." }
$image = "$Registry/$Repository@$digest"
$suffix = ($manifest.version.ToLowerInvariant() -replace '[^a-z0-9-]', '-')
if ($suffix.Length -gt 40) { $suffix = $suffix.Substring($suffix.Length - 40).TrimStart('-') }
$archive = Join-Path $releasePath 'lex-v3-image.oci.tar'
Write-Host "  release $($manifest.version): image $digest, corpus $($manifest.corpus.sha256)"

# 2. The image into the registry: read out of the release by its manifest digest, tagged with the release version as a
# handle, and deployed below by the digest alone. The registry access token (short-lived, from the owner's session)
# goes on standard input to `oras login`, which keeps it in a registry config in a fresh private directory that only
# `oras cp` and the digest check read; the directory is removed when the copy ends, whatever happens. The token is never
# on a command line, never printed, and never left on disk (the custody probe's runbook did the same).
Step "copy the image into $Registry"
Run "az account set --subscription $Subscription" { az account set --subscription $Subscription }
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

# 3. The candidate revision, with no traffic while a live revision serves.
Step "deploy the candidate revision $suffix"
$parameters = @(
    "environmentId=$EnvironmentId", "appName=$AppName", "image=$image", "registryServer=$Registry",
    "identityResourceId=$IdentityResourceId", "revisionSuffix=$suffix", "liveRevision=$LiveRevision", "probeSourceCidr=$ProbeSourceCidr"
)
Run "az deployment group what-if -g $ResourceGroup -f deploy/main.bicep -p $($parameters -join ' ')" {
    az deployment group what-if -g $ResourceGroup -f (Join-Path $checkout 'deploy/main.bicep') -p @parameters
}
Run "az deployment group create -g $ResourceGroup -f deploy/main.bicep -p $($parameters -join ' ')" {
    az deployment group create -g $ResourceGroup -f (Join-Path $checkout 'deploy/main.bicep') -p @parameters -o none
}

# 4. The zero-traffic probe against the candidate's own URL.
Step "probe the candidate"
$browserProbe = if ($SkipBrowserProbe) { @() } else { @('--browser') }
if (-not $Apply) {
    Write-Host "  > node web/scripts/deploy-probe.mjs --origin https://$AppName---candidate.<environment default domain> --release $releasePath --public-key $keyPath $($browserProbe -join ' ')"
    if ($SkipBrowserProbe) { Write-Host "  (the browser probes are skipped: -SkipBrowserProbe)" }
    Write-Host "== planned only: run again with -Apply to deploy (the owner's decision)"
    exit 0
}
$domain = az containerapp env show --ids $EnvironmentId --query properties.defaultDomain -o tsv
$candidate = az containerapp show -g $ResourceGroup -n $AppName --query properties.latestRevisionName -o tsv
$origin = "https://$AppName---candidate.$domain"
if ($SkipBrowserProbe) { Write-Host "  the browser probes are skipped (-SkipBrowserProbe): only the HTTP probes run" }
& node (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --origin $origin --release $releasePath --public-key $keyPath @browserProbe
if ($LASTEXITCODE -ne 0) {
    # 5. The removal step: the candidate never carried traffic (or only the owner's probe), and is deactivated.
    Step "the probe failed: deactivate the candidate $candidate"
    az containerapp revision deactivate -g $ResourceGroup -n $AppName --revision $candidate
    throw "The candidate failed its probe and was deactivated; no traffic moved."
}

Step "the candidate $candidate answers as the release holds"
Write-Host "  Promotion is the owner's decision and is not run here."
if ([string]::IsNullOrEmpty($LiveRevision)) {
    Write-Host "  A first deployment already routes to the candidate behind the probe-only restriction. To open it to everyone:"
    Write-Host "  > az containerapp ingress access-restriction remove -g $ResourceGroup -n $AppName --rule-name owner-probe-only"
}
else {
    Write-Host "  To move all traffic from $LiveRevision to the candidate:"
    Write-Host "  > az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $candidate=100"
}
Write-Host "  To remove it instead: pwsh -File deploy/deploy.ps1 -Subscription $Subscription -ResourceGroup $ResourceGroup -AppName $AppName -Remove -Revision $candidate"
if (-not [string]::IsNullOrEmpty($LiveRevision)) {
    # Rollback and forward again are traffic moves, the owner's like promotion: printed, never run.
    Write-Host "  After promotion, to roll back to $LiveRevision (kept, since every revision is kept):"
    Write-Host "  > az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $LiveRevision=100"
    Write-Host "  and forward again to the candidate:"
    Write-Host "  > az containerapp ingress traffic set -g $ResourceGroup -n $AppName --revision-weight $candidate=100"
}
