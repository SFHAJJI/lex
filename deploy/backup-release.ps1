<#
.SYNOPSIS
R111: a copy of a signed release's assets, and of the custody pointers it was derived from, in a second container of the
storage account stlexv3soufien, each object checked by SHA-256 against the release's signed manifest.

.DESCRIPTION
The specification of record's R111 asks that custody and every signed release be copied off this PC to private, versioned or
immutable storage, each object checked by digest. Custody already lives in the account's `custody` container (the derive
kit's, OPERATING.md section 4); a release lives only on this PC (C:\lex-v3\releases). This command, which the driver runs
for each release once it is published:
1. reads the release back under the signing identity's public key (`web/scripts/deploy-probe.mjs` without --origin, under
   the browser trap): the manifest's signature and every asset by size and SHA-256; nothing is copied from a release that
   does not verify;
2. holds the account to versioned storage: blob versioning and soft delete for blobs and containers must be on, so a copy can
   be neither overwritten nor deleted for good; -EnableProtection turns them on (365 days of soft delete), once, as the owner's
   money decision of 2026-10-18 allows (storage for the custody's second copy); without it, -Apply refuses an account
   without them before copying anything;
3. copies into the private container -Container (`releases` by default), under `<version>/`: every asset the signed manifest
   lists, the manifest and its signature, each with its SHA-256 in the blob's metadata, in the Cool tier, never over an
   existing blob (one that exists with the same size and digest is kept and said; one that exists with another is a stop);
4. with -CustodyPrefix, writes the custody pointers (`custody-pointers.json`, `lex-v3-custody-pointers/1`): every custody
   object under those prefixes of the `custody` container, by name, size, Content-MD5 and version id, the release's version,
   and the account; the custody itself is not copied again: versioning keeps every version the pointers name;
5. verifies (after -Apply, or alone with -Verify): each object is downloaded, one at a time, into a temporary directory, its
   SHA-256 compared with the signed manifest's (or the pointers file's), and the copy deleted; each custody pointer is listed
   again and must still name an object of the same size, Content-MD5 and version. A failure names the object and stops.
Without -Apply or -Verify it only plans: it reads the release back and prints what it would copy, and changes nothing.
It never prints a secret: the account key is read into this process's environment for the Azure CLI, never written or put on
a command line, and removed when the command ends. It never deletes anything.

.EXAMPLE
pwsh -File deploy/backup-release.ps1 -Release C:\lex-v3\releases\v3-20261026T180319Z-8958ec5fa63f `
  -SigningPublicKey src/Lex.V3.Api/well-known/lex-v3-release.pem -CustodyPrefix run15/,run16/ -Apply

.EXAMPLE
pwsh -File deploy/backup-release.ps1 -Release C:\lex-v3\releases\v3-20261026T180319Z-8958ec5fa63f `
  -SigningPublicKey src/Lex.V3.Api/well-known/lex-v3-release.pem -Verify
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Release,
    [Parameter(Mandatory)] [string] $SigningPublicKey,
    [string] $SignerIdentity = 'lex-v3-release',
    [string] $ResourceGroup = 'rg-lex-v3',
    [string] $Account = 'stlexv3soufien',
    [string] $Container = 'releases',
    [string] $CustodyContainer = 'custody',
    [string[]] $CustodyPrefix = @(),
    [ValidateSet('Hot', 'Cool', 'Cold')] [string] $Tier = 'Cool',
    [long] $KeepFreeBytes = 2GB,
    [switch] $EnableProtection,
    [switch] $Apply,
    [switch] $Verify
)
$ErrorActionPreference = 'Stop'
$checkout = Split-Path -Parent $PSScriptRoot
function Step([string] $what) { Write-Host "== $what" }
function Get-Sha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

if ($Container -cnotmatch '^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$' -or $Container -eq $CustodyContainer) {
    throw "-Container must be a private container name of its own (lower-case letters, digits and single hyphens), never the custody container."
}
foreach ($prefix in $CustodyPrefix) {
    if ([string]::IsNullOrWhiteSpace($prefix) -or $prefix.StartsWith('/') -or $prefix.Contains('..')) { throw "-CustodyPrefix $prefix is not a blob name prefix." }
}

# 1. The release, read back under the signing identity's key, before anything touches Azure.
Step "verify the release under the signing identity's public key"
$releasePath = (Resolve-Path $Release).Path
$keyPath = (Resolve-Path $SigningPublicKey).Path
$trapPath = [System.IO.Path]::GetFullPath((Join-Path $checkout 'web/scripts/browser-trap.mjs'))
$trapUrl = if ($IsWindows) { [System.Uri]::new($trapPath).AbsoluteUri } else { [System.Uri]::new("file://$trapPath").AbsoluteUri }
& node --import $trapUrl (Join-Path $checkout 'web/scripts/deploy-probe.mjs') --release $releasePath --public-key $keyPath --signer $SignerIdentity
if ($LASTEXITCODE -ne 0) { throw "The release does not verify under the signing identity's key; nothing is copied." }

$manifest = Get-Content -LiteralPath (Join-Path $releasePath 'release-manifest.json') -Raw | ConvertFrom-Json
$version = [string]$manifest.version
if ($version -cnotmatch '^v3-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{12}$') { throw "The release's version $version is not a signed release's; nothing is copied." }
# The objects: every asset the signed manifest lists, by its recorded digest, then the manifest and its signature.
$objects = [System.Collections.Generic.List[object]]::new()
foreach ($asset in $manifest.assets) {
    $objects.Add([pscustomobject]@{ Name = [string]$asset.name; Path = Join-Path $releasePath $asset.name; Size = [long]$asset.size; Sha256 = [string]$asset.sha256 })
}
foreach ($name in 'release-manifest.json', 'release-manifest.sig.json') {
    $path = Join-Path $releasePath $name
    $objects.Add([pscustomobject]@{ Name = $name; Path = $path; Size = (Get-Item -LiteralPath $path).Length; Sha256 = Get-Sha256 $path })
}

if (-not $Apply -and -not $Verify) {
    Step "plan: copy $($objects.Count) objects of $version to $Account/$Container/$version/ (nothing is changed without -Apply)"
    foreach ($object in $objects) { Write-Host "  > az storage blob upload --container-name $Container --name $version/$($object.Name) --tier $Tier --metadata sha256=$($object.Sha256) --overwrite false" }
    if ($CustodyPrefix.Count -gt 0) { Write-Host "  and the custody pointers under $($CustodyPrefix -join ', ') of $CustodyContainer, as $version/custody-pointers.json" }
    exit 0
}

# The account key, for the Azure CLI only: in this process's environment, never printed, never on a command line.
$env:AZURE_STORAGE_ACCOUNT = $Account
$env:AZURE_STORAGE_KEY = az storage account keys list -n $Account -g $ResourceGroup --query '[0].value' -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($env:AZURE_STORAGE_KEY)) {
    Remove-Item Env:AZURE_STORAGE_KEY, Env:AZURE_STORAGE_ACCOUNT -ErrorAction SilentlyContinue
    throw "The key of $Account could not be read (az login as an identity that may list it); nothing is copied."
}

function Get-Blob([string] $Name) {
    $text = az storage blob show --container-name $Container --name $Name --query '{size:properties.contentLength, sha256:metadata.sha256}' -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json
}

# The custody objects under the prefixes, each by name, size, Content-MD5 and version id, as text: tab-separated values, since
# ConvertFrom-Json turns a version id (a time stamp) into a date, which no longer prints as the id (`None` for a missing value).
function Get-CustodyListing([string[]] $Prefixes) {
    $listed = [System.Collections.Generic.List[object]]::new()
    foreach ($prefix in $Prefixes) {
        $lines = az storage blob list --container-name $CustodyContainer --prefix $prefix --num-results '*' --include v `
            --query '[?isCurrentVersion != `false`].[name, properties.contentLength, properties.contentSettings.contentMd5, versionId]' -o tsv
        if ($LASTEXITCODE -ne 0) { throw "The custody under $prefix could not be listed." }
        foreach ($line in @($lines)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $parts = "$line".Split("`t")
            if ($parts.Count -ne 4) { throw "The custody listing under $prefix has a line that is not four values." }
            $listed.Add([pscustomobject]@{ name = $parts[0]; size = [long]$parts[1]; md5 = $parts[2]; version = $parts[3] })
        }
    }
    return @($listed | Sort-Object -Property name -CaseSensitive)
}

$scratch = Join-Path ([System.IO.Path]::GetTempPath()) "lex-v3-backup-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    if ($Apply) {
        # 2. Versioned storage: a copy that can be neither overwritten nor deleted for good.
        Step "hold $Account to versioned storage (blob versioning, soft delete for blobs and containers)"
        $protection = az storage account blob-service-properties show -n $Account -g $ResourceGroup `
            --query '{versioning:isVersioningEnabled, blobs:deleteRetentionPolicy.enabled, containers:containerDeleteRetentionPolicy.enabled}' -o json | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $null -eq $protection) { throw "The blob service settings of $Account could not be read; nothing is copied." }
        if (-not ($protection.versioning -eq $true -and $protection.blobs -eq $true -and $protection.containers -eq $true)) {
            if (-not $EnableProtection) {
                throw "$Account keeps no versions or soft-deleted copies (versioning $($protection.versioning), blob soft delete $($protection.blobs), container soft delete $($protection.containers)); R111 asks for versioned storage. Run again with -EnableProtection to turn them on; nothing is copied."
            }
            az storage account blob-service-properties update -n $Account -g $ResourceGroup --enable-versioning true `
                --enable-delete-retention true --delete-retention-days 365 --enable-container-delete-retention true --container-delete-retention-days 365 -o none
            if ($LASTEXITCODE -ne 0) { throw "Versioning and soft delete could not be turned on for $Account; nothing is copied." }
            Write-Host "  turned on: blob versioning, and soft delete for blobs and containers (365 days)"
        }

        # 3. The release's objects, never over an existing blob.
        Step "copy $($objects.Count) objects of $version to $Account/$Container/$version/"
        az storage container create --name $Container --public-access off -o none
        if ($LASTEXITCODE -ne 0) { throw "The container $Container could not be made; nothing is copied." }
        foreach ($object in $objects) {
            $blob = "$version/$($object.Name)"
            $held = Get-Blob $blob
            if ($null -ne $held) {
                if ([long]$held.size -eq $object.Size -and $held.sha256 -ceq $object.Sha256) { Write-Host "  kept: $blob is already copied ($($object.Size) bytes, sha256 $($object.Sha256))"; continue }
                throw "$blob exists in $Container with another content (size $($held.size), sha256 $($held.sha256)); a copy is never overwritten. Stop here."
            }
            az storage blob upload --container-name $Container --name $blob --file $object.Path --tier $Tier `
                --metadata "sha256=$($object.Sha256)" "release=$version" --overwrite false --validate-content --no-progress -o none
            if ($LASTEXITCODE -ne 0) { throw "Copying $blob failed; the objects before it are copied, and a run again keeps them." }
            Write-Host "  copied: $blob ($($object.Size) bytes, sha256 $($object.Sha256))"
        }

        # 4. The custody pointers.
        if ($CustodyPrefix.Count -gt 0) {
            Step "write the custody pointers of $version (under $($CustodyPrefix -join ', ') of $CustodyContainer)"
            $pointers = [ordered]@{
                schema = 'lex-v3-custody-pointers/1'
                release = $version
                account = $Account
                container = $CustodyContainer
                prefixes = @($CustodyPrefix)
                objects = @(Get-CustodyListing $CustodyPrefix | ForEach-Object { [ordered]@{ name = $_.name; size = [long]$_.size; content_md5 = $_.md5; version_id = $_.version } })
            }
            if ($pointers.objects.Count -eq 0) { throw "No custody object is listed under $($CustodyPrefix -join ', '); the pointers would name nothing." }
            $pointersPath = Join-Path $scratch 'custody-pointers.json'
            ($pointers | ConvertTo-Json -Depth 6) + "`n" | Set-Content -LiteralPath $pointersPath -Encoding utf8NoBOM -NoNewline
            $pointerObject = [pscustomobject]@{ Name = 'custody-pointers.json'; Path = $pointersPath; Size = (Get-Item -LiteralPath $pointersPath).Length; Sha256 = Get-Sha256 $pointersPath }
            $blob = "$version/custody-pointers.json"
            $held = Get-Blob $blob
            if ($null -ne $held -and -not ([long]$held.size -eq $pointerObject.Size -and $held.sha256 -ceq $pointerObject.Sha256)) {
                throw "$blob exists with other pointers (sha256 $($held.sha256)); a copy is never overwritten. Stop here."
            }
            if ($null -eq $held) {
                az storage blob upload --container-name $Container --name $blob --file $pointersPath --tier $Tier `
                    --metadata "sha256=$($pointerObject.Sha256)" "release=$version" --overwrite false --validate-content --no-progress -o none
                if ($LASTEXITCODE -ne 0) { throw "Copying $blob failed." }
            }
            Write-Host "  $($pointers.objects.Count) custody objects named, $blob sha256 $($pointerObject.Sha256)"
        }
    }

    # 5. The verification: every object downloaded and hashed, one at a time, and every custody pointer listed again.
    Step "verify the copy of $version in $Account/$Container by SHA-256"
    $verified = 0
    $pointersBlob = Get-Blob "$version/custody-pointers.json"
    $toVerify = @($objects) + @(if ($null -ne $pointersBlob) { [pscustomobject]@{ Name = 'custody-pointers.json'; Path = $null; Size = [long]$pointersBlob.size; Sha256 = [string]$pointersBlob.sha256 } })
    foreach ($object in $toVerify) {
        $blob = "$version/$($object.Name)"
        $held = Get-Blob $blob
        if ($null -eq $held) { throw "$blob is not in ${Container}: the copy is not complete." }
        if ([long]$held.size -ne $object.Size -or $held.sha256 -cne $object.Sha256) { throw "$blob in $Container is $($held.size) bytes with sha256 $($held.sha256) recorded, not the $($object.Size) bytes and $($object.Sha256) the release holds." }
        $free = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($scratch)).AvailableFreeSpace
        if ($free - $object.Size -lt $KeepFreeBytes) { throw "Not enough free disk to download $blob and keep $([math]::Round($KeepFreeBytes / 1GB, 2)) GB ($([math]::Round($free / 1GB, 2)) GB free); it was not verified." }
        $copy = Join-Path $scratch 'verify.bin'
        az storage blob download --container-name $Container --name $blob --file $copy --no-progress -o none
        if ($LASTEXITCODE -ne 0) { throw "$blob could not be downloaded to verify it." }
        $digest = Get-Sha256 $copy
        Remove-Item -LiteralPath $copy
        if ($digest -cne $object.Sha256) { throw "$blob downloaded is sha256 $digest, not the $($object.Sha256) the release holds: the copy is damaged." }
        if ($object.Name -eq 'custody-pointers.json') { continue }
        $verified++
    }
    $pointersChecked = 0
    if ($null -ne $pointersBlob) {
        $pointersCopy = Join-Path $scratch 'custody-pointers.json'
        az storage blob download --container-name $Container --name "$version/custody-pointers.json" --file $pointersCopy --no-progress -o none
        if ($LASTEXITCODE -ne 0) { throw "The custody pointers could not be downloaded." }
        # Read by System.Text.Json, so each version id stays the text the listing gave (ConvertFrom-Json would make it a date).
        $document = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($pointersCopy))
        try {
            $prefixes = @($document.RootElement.GetProperty('prefixes').EnumerateArray() | ForEach-Object { $_.GetString() })
            $now = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
            foreach ($entry in Get-CustodyListing $prefixes) { $now[$entry.name] = $entry }
            foreach ($pointer in $document.RootElement.GetProperty('objects').EnumerateArray()) {
                $name = $pointer.GetProperty('name').GetString()
                $size = $pointer.GetProperty('size').GetInt64()
                $md5 = $pointer.GetProperty('content_md5').GetString()
                $versionId = $pointer.GetProperty('version_id').GetString()
                $entry = if ($now.ContainsKey($name)) { $now[$name] } else { $null }
                if ($null -eq $entry -or $entry.size -ne $size -or $entry.md5 -cne $md5 -or $entry.version -cne $versionId) {
                    throw "The custody object $name is not the one the pointers name (size $size, Content-MD5 $md5, version $versionId)."
                }
                $pointersChecked++
            }
        }
        finally {
            $document.Dispose()
        }
    }
    Write-Host "verified: $verified objects of $version in $Account/$Container by SHA-256$(if ($pointersChecked -gt 0) { ", and $pointersChecked custody pointers" })"
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:AZURE_STORAGE_KEY, Env:AZURE_STORAGE_ACCOUNT -ErrorAction SilentlyContinue
}
