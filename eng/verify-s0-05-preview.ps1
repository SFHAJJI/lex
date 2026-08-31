[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][string]$PublicGraphRoot,
    [Parameter(Mandatory)][ValidatePattern('^sha256:[0-9a-f]{64}$')][string]$ExpectedBaseImageDigest,
    [Parameter(Mandatory)][string]$BaseIndexPath,
    [Parameter(Mandatory)][string]$BaseManifestPath,
    [Parameter(Mandatory)][string]$ResultPath,
    [string]$DockerArchivePath,
    [ValidatePattern('^[a-z0-9][a-z0-9._/-]*:[A-Za-z0-9_][A-Za-z0-9._-]*$')][string]$DockerImageReference,
    [ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedPrivateKeyCanarySha256
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$MaxArchiveBytes = 1GB
$MaxDockerArchiveBytes = 2GB
$MaxOuterEntries = 4096
$MaxOuterMemberBytes = 1GB
$MaxMetadataBytes = 1MB
$MaxLayers = 64
$MaxExpandedLayerBytes = 1GB
$MaxExpandedImageBytes = 2GB
$MaxLayerEntries = 100000
$MaxLayerFiles = 80000
$MaxLayerFileBytes = 512MB
$MaxPublicGraphEntries = 128
$MaxPublicGraphFileBytes = 2MB
$MaxPublicGraphBytes = 8MB

Add-Type -TypeDefinition @'
using System;

public static class LexV3OciByteScanner
{
    public static int Find(byte[] data, byte[] needle)
    {
        return data.AsSpan().IndexOf(needle);
    }

    public static bool ContainsPrivateKeyDer(byte[] data)
    {
        for (int i = 0; i < data.Length - 8; i++)
        {
            if (data[i] == 0x30 && IsPrivateKeyAt(data, i)) return true;
        }
        return false;
    }

    private static bool IsPrivateKeyAt(byte[] data, int start)
    {
        int position = start;
        int outerEnd;
        if (!ReadTag(data, ref position, 0x30, data.Length, out outerEnd)) return false;
        if (outerEnd - start > 32768) return false;

        int integerEnd;
        if (!ReadTag(data, ref position, 0x02, outerEnd, out integerEnd)) return false;
        if (integerEnd - position != 1 || (data[position] != 0 && data[position] != 1)) return false;
        int version = data[position];
        position = integerEnd;
        if (position >= outerEnd) return false;

        if (data[position] == 0x30)
        {
            int algorithmEnd;
            if (!ReadTag(data, ref position, 0x30, outerEnd, out algorithmEnd)) return false;
            bool hasOid = position < algorithmEnd && data[position] == 0x06;
            position = algorithmEnd;
            int privateKeyEnd;
            return hasOid && ReadTag(data, ref position, 0x04, outerEnd, out privateKeyEnd) && privateKeyEnd > position;
        }

        if (data[position] == 0x04)
        {
            int privateKeyEnd;
            return version == 1 && ReadTag(data, ref position, 0x04, outerEnd, out privateKeyEnd) && privateKeyEnd - position >= 16;
        }

        if (data[position] == 0x02)
        {
            int integerCount = 1;
            while (position < outerEnd && data[position] == 0x02)
            {
                int nextEnd;
                if (!ReadTag(data, ref position, 0x02, outerEnd, out nextEnd)) return false;
                position = nextEnd;
                integerCount++;
            }
            return integerCount >= 9;
        }

        return false;
    }

    private static bool ReadTag(byte[] data, ref int position, byte expectedTag, int limit, out int contentEnd)
    {
        contentEnd = 0;
        if (position >= limit || data[position++] != expectedTag) return false;
        int length;
        if (!ReadLength(data, ref position, limit, out length)) return false;
        if (length < 0 || position > limit - length) return false;
        contentEnd = position + length;
        return true;
    }

    private static bool ReadLength(byte[] data, ref int position, int limit, out int length)
    {
        length = 0;
        if (position >= limit) return false;
        byte first = data[position++];
        if ((first & 0x80) == 0)
        {
            length = first;
            return true;
        }

        int count = first & 0x7f;
        if (count == 0 || count > 4 || position > limit - count) return false;
        if (data[position] == 0) return false;
        for (int i = 0; i < count; i++)
        {
            if (length > (int.MaxValue >> 8)) return false;
            length = (length << 8) | data[position++];
        }
        return length >= 128;
    }
}
'@

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) { throw $Message }
}

function Get-SafeArchivePath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][bool]$IsDirectory
    )

    Assert-True ($Path.Length -gt 0) 'Archive entry path is empty.'
    Assert-True (-not $Path.Contains([char]0)) 'Archive entry path contains NUL.'
    Assert-True (-not $Path.Contains([char]0xfffd) -and -not $Path.ToCharArray().Where({ [int]$_ -lt 32 -or [int]$_ -eq 127 })) 'Archive entry path contains an invalid character.'
    Assert-True (-not $Path.Contains('\')) 'Archive entry path contains a backslash.'
    Assert-True (-not $Path.StartsWith('/', [StringComparison]::Ordinal)) 'Archive entry path is absolute.'
    Assert-True (-not ($Path -cmatch '^[A-Za-z]:')) 'Archive entry path is drive-qualified.'
    Assert-True ([Text.Encoding]::UTF8.GetByteCount($Path) -le 4096) 'Archive entry path is too long.'

    $parts = [Collections.Generic.List[string]]::new()
    foreach ($part in $Path.Split('/')) {
        if ($part.Length -eq 0 -and $IsDirectory -and $parts.Count -gt 0) { continue }
        if ($part.Length -eq 0 -or $part -ceq '.') { continue }
        Assert-True ($part -cne '..') 'Archive entry path contains a parent segment.'
        Assert-True ([Text.Encoding]::UTF8.GetByteCount($part) -le 255) 'Archive entry path segment is too long.'
        Assert-True (-not $part.Contains([char]0)) 'Archive entry path segment contains NUL.'
        $parts.Add($part.Normalize([Text.NormalizationForm]::FormC))
    }

    Assert-True ($parts.Count -gt 0) 'Archive entry path normalizes to empty.'
    return [string]::Join('/', $parts)
}

function Assert-SafeLinkTarget {
    param(
        [Parameter(Mandatory)][string]$EntryPath,
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][bool]$HardLink
    )

    Assert-True ($Target.Length -gt 0) 'Archive link target is empty.'
    Assert-True (-not $Target.Contains([char]0)) 'Archive link target contains NUL.'
    Assert-True (-not $Target.Contains([char]0xfffd) -and -not $Target.ToCharArray().Where({ [int]$_ -lt 32 -or [int]$_ -eq 127 })) 'Archive link target contains an invalid character.'
    Assert-True (-not $Target.Contains('\')) 'Archive link target contains a backslash.'
    Assert-True (-not ($Target -cmatch '^[A-Za-z]:')) 'Archive link target is drive-qualified.'

    $segments = [Collections.Generic.List[string]]::new()
    if (-not $HardLink -and -not $Target.StartsWith('/', [StringComparison]::Ordinal)) {
        $parent = [IO.Path]::GetDirectoryName($EntryPath.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if ($parent) {
            foreach ($segment in $parent.Replace([IO.Path]::DirectorySeparatorChar, '/').Split('/')) {
                if ($segment) { $segments.Add($segment) }
            }
        }
    }

    foreach ($segment in $Target.TrimStart('/').Split('/')) {
        if ($segment.Length -eq 0 -or $segment -ceq '.') { continue }
        if ($segment -ceq '..') {
            Assert-True ($segments.Count -gt 0) 'Archive link target escapes the image root.'
            $segments.RemoveAt($segments.Count - 1)
            continue
        }
        $segments.Add($segment)
    }
    Assert-True ($segments.Count -gt 0) 'Archive link target resolves to the image root.'
}

function New-ForbiddenNeedles {
    param([string]$CanaryDigest)

    $texts = @(
        '-----BEGIN PRIVATE KEY-----',
        '-----BEGIN ENCRYPTED PRIVATE KEY-----',
        '-----BEGIN RSA PRIVATE KEY-----',
        '-----BEGIN EC PRIVATE KEY-----',
        '-----BEGIN DSA PRIVATE KEY-----',
        '-----BEGIN OPENSSH PRIVATE KEY-----',
        "openssh-key-v1$([char]0)",
        'Lex.V3.Preview.dll',
        'Lex.V3.Preview',
        'SyntheticPreviewBuilder',
        'SyntheticSqliteIndex',
        'src/Lex.V3.Preview',
        'lex-index/2',
        'corpus/5',
        'canon/1',
        'lex-artifacts/1',
        'Lex.Ingest',
        'Lex.Index',
        'Lex.Mcp',
        'Lex.Web',
        'signing-key.pem'
    )
    $needles = [Collections.Generic.List[byte[]]]::new()
    foreach ($text in $texts) { $needles.Add([Text.Encoding]::UTF8.GetBytes($text)) }
    if ($CanaryDigest) {
        $needles.Add([Convert]::FromHexString($CanaryDigest))
        $needles.Add([Text.Encoding]::ASCII.GetBytes($CanaryDigest.ToLowerInvariant()))
        $needles.Add([Text.Encoding]::ASCII.GetBytes($CanaryDigest.ToUpperInvariant()))
    }
    return $needles.ToArray()
}

function Read-AndScanStream {
    param(
        [Parameter(Mandatory)][IO.Stream]$Stream,
        [Parameter(Mandatory)][long]$MaximumBytes,
        [Parameter(Mandatory)][byte[][]]$ForbiddenNeedles,
        [Parameter(Mandatory)][string]$Subject,
        [switch]$CopyTo,
        [string]$DestinationPath
    )

    $destination = $null
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        if ($CopyTo) {
            Assert-True (-not [string]::IsNullOrWhiteSpace($DestinationPath)) 'A private spool destination is required.'
            $destination = [IO.FileStream]::new($DestinationPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 65536, [IO.FileOptions]::SequentialScan)
        }
        $buffer = [byte[]]::new(65536)
        $carry = [byte[]]::new(0)
        [long]$total = 0
        while (($read = $Stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            Assert-True ($total -le $MaximumBytes - $read) "$Subject exceeds its byte ceiling."
            $total += $read
            $hash.AppendData($buffer, 0, $read)
            if ($destination) { $destination.Write($buffer, 0, $read) }

            $window = [byte[]]::new($carry.Length + $read)
            if ($carry.Length) { [Array]::Copy($carry, 0, $window, 0, $carry.Length) }
            [Array]::Copy($buffer, 0, $window, $carry.Length, $read)
            foreach ($needle in $ForbiddenNeedles) {
                Assert-True ([LexV3OciByteScanner]::Find($window, $needle) -lt 0) "$Subject contains forbidden material."
            }
            Assert-True (-not [LexV3OciByteScanner]::ContainsPrivateKeyDer($window)) "$Subject contains a private-key structure."

            $carryLength = [Math]::Min(32768, $window.Length)
            $carry = [byte[]]::new($carryLength)
            [Array]::Copy($window, $window.Length - $carryLength, $carry, 0, $carryLength)
        }
        if ($destination) {
            $destination.Flush($true)
            $destination.Dispose()
            $destination = $null
        }
        return [pscustomobject]@{
            Bytes = $total
            Sha256 = [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
        }
    }
    finally {
        if ($destination) { $destination.Dispose() }
        $hash.Dispose()
    }
}

function Assert-NoDuplicateJsonProperties {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            Assert-True ($names.Add($property.Name)) 'JSON contains a duplicate property.'
            Assert-NoDuplicateJsonProperties -Element $property.Value
        }
    }
    elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-NoDuplicateJsonProperties -Element $item }
    }
}

function Read-JsonDocument {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][long]$MaximumBytes
    )
    $info = Get-Item -LiteralPath $Path
    Assert-True ($info.Length -le $MaximumBytes) 'OCI JSON metadata exceeds its byte ceiling.'
    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [Text.Json.JsonCommentHandling]::Disallow
    $options.MaxDepth = 64
    $jsonStream = [IO.MemoryStream]::new([IO.File]::ReadAllBytes($Path), $false)
    try {
        $document = [Text.Json.JsonDocument]::Parse($jsonStream, $options)
    }
    finally { $jsonStream.Dispose() }
    Assert-NoDuplicateJsonProperties -Element $document.RootElement
    return $document
}

function Get-RequiredJsonProperty {
    param(
        [Parameter(Mandatory)][Text.Json.JsonElement]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][Text.Json.JsonValueKind]$Kind
    )
    Assert-True ($Object.ValueKind -eq [Text.Json.JsonValueKind]::Object) 'Expected a JSON object.'
    $value = $Object.GetProperty($Name)
    Assert-True ($value.ValueKind -eq $Kind) "JSON property '$Name' has the wrong type."
    return $value
}

function Assert-OptionalUtcCreated {
    param(
        [Parameter(Mandatory)][Text.Json.JsonElement]$Object,
        [Parameter(Mandatory)][string]$Subject
    )

    if (-not (Test-JsonProperty -Object $Object -Name 'created')) { return }
    $element = $Object.GetProperty('created')
    Assert-True ($element.ValueKind -eq [Text.Json.JsonValueKind]::String) "$Subject created field is not a string."
    $value = $element.GetString()
    Assert-True ($value.Length -le 64 -and $value -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,9})?Z$') "$Subject created field is not bounded UTC RFC 3339."
    [DateTimeOffset]$parsed = [DateTimeOffset]::MinValue
    $valid = [DateTimeOffset]::TryParse(
        $value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$parsed)
    Assert-True ($valid -and $parsed.Offset -eq [TimeSpan]::Zero) "$Subject created field is not a valid UTC instant."
}

function Test-JsonProperty {
    param(
        [Parameter(Mandatory)][Text.Json.JsonElement]$Object,
        [Parameter(Mandatory)][string]$Name
    )
    foreach ($property in $Object.EnumerateObject()) {
        if ($property.Name -ceq $Name) { return $true }
    }
    return $false
}

function Read-TrustedBase {
    param(
        [Parameter(Mandatory)][string]$IndexPath,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][string]$ExpectedDigest
    )

    $resolvedIndex = (Resolve-Path -LiteralPath $IndexPath).Path
    $resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
    $indexInfo = Get-Item -LiteralPath $resolvedIndex
    $manifestInfo = Get-Item -LiteralPath $resolvedManifest
    Assert-True (-not $indexInfo.PSIsContainer -and $indexInfo.Length -gt 0 -and $indexInfo.Length -le $MaxMetadataBytes) 'Trusted base index is outside its byte bound.'
    Assert-True (-not $manifestInfo.PSIsContainer -and $manifestInfo.Length -gt 0 -and $manifestInfo.Length -le $MaxMetadataBytes) 'Trusted base manifest is outside its byte bound.'

    $indexBytes = [IO.File]::ReadAllBytes($resolvedIndex)
    $actualIndexDigest = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($indexBytes)).ToLowerInvariant()
    Assert-True ($actualIndexDigest -ceq $ExpectedDigest) 'Trusted base index digest does not match the pinned base image.'
    $indexStream = [IO.MemoryStream]::new($indexBytes, $false)
    try {
        $options = [Text.Json.JsonDocumentOptions]::new()
        $options.MaxDepth = 64
        $indexDocument = [Text.Json.JsonDocument]::Parse($indexStream, $options)
    }
    finally { $indexStream.Dispose() }
    try {
        Assert-NoDuplicateJsonProperties -Element $indexDocument.RootElement
        $indexRoot = $indexDocument.RootElement
        Assert-True ((Get-RequiredJsonProperty -Object $indexRoot -Name 'schemaVersion' -Kind Number).GetInt32() -eq 2) 'Trusted base index schema version is not accepted.'
        $indexMediaType = (Get-RequiredJsonProperty -Object $indexRoot -Name 'mediaType' -Kind String).GetString()
        Assert-True ($indexMediaType -ceq 'application/vnd.oci.image.index.v1+json' -or $indexMediaType -ceq 'application/vnd.docker.distribution.manifest.list.v2+json') 'Trusted base index media type is not accepted.'
        $baseDescriptors = Get-RequiredJsonProperty -Object $indexRoot -Name 'manifests' -Kind Array
        Assert-True ($baseDescriptors.GetArrayLength() -gt 0 -and $baseDescriptors.GetArrayLength() -le 256) 'Trusted base index manifest count is outside its bound.'
        $selected = [Collections.Generic.List[Text.Json.JsonElement]]::new()
        foreach ($descriptor in $baseDescriptors.EnumerateArray()) {
            $platform = Get-RequiredJsonProperty -Object $descriptor -Name 'platform' -Kind Object
            $architecture = (Get-RequiredJsonProperty -Object $platform -Name 'architecture' -Kind String).GetString()
            $operatingSystem = (Get-RequiredJsonProperty -Object $platform -Name 'os' -Kind String).GetString()
            if ($architecture -ceq 'amd64' -and $operatingSystem -ceq 'linux') { $selected.Add($descriptor) }
        }
        Assert-True ($selected.Count -eq 1) 'Trusted base index must select exactly one linux/amd64 manifest.'
        $selectedDescriptor = $selected[0]
        $selectedMediaType = (Get-RequiredJsonProperty -Object $selectedDescriptor -Name 'mediaType' -Kind String).GetString()
        Assert-True ($selectedMediaType -ceq 'application/vnd.oci.image.manifest.v1+json' -or $selectedMediaType -ceq 'application/vnd.docker.distribution.manifest.v2+json') 'Trusted base child manifest media type is not accepted.'
        $selectedDigest = (Get-RequiredJsonProperty -Object $selectedDescriptor -Name 'digest' -Kind String).GetString()
        Assert-True ($selectedDigest -cmatch '^sha256:[0-9a-f]{64}$') 'Trusted base child digest is malformed.'
        $selectedSizeElement = Get-RequiredJsonProperty -Object $selectedDescriptor -Name 'size' -Kind Number
        [long]$selectedSize = 0
        Assert-True ($selectedSizeElement.TryGetInt64([ref]$selectedSize) -and $selectedSize -gt 0 -and $selectedSize -le $MaxMetadataBytes) 'Trusted base child size is outside its bound.'
    }
    finally { $indexDocument.Dispose() }

    $manifestBytes = [IO.File]::ReadAllBytes($resolvedManifest)
    Assert-True ($manifestBytes.Length -eq $selectedSize) 'Trusted base child size does not match the index descriptor.'
    $actualManifestDigest = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes)).ToLowerInvariant()
    Assert-True ($actualManifestDigest -ceq $selectedDigest) 'Trusted base child digest does not match the index descriptor.'
    $manifestStream = [IO.MemoryStream]::new($manifestBytes, $false)
    try {
        $options = [Text.Json.JsonDocumentOptions]::new()
        $options.MaxDepth = 64
        $manifestDocument = [Text.Json.JsonDocument]::Parse($manifestStream, $options)
    }
    finally { $manifestStream.Dispose() }
    try {
        Assert-NoDuplicateJsonProperties -Element $manifestDocument.RootElement
        $manifestRoot = $manifestDocument.RootElement
        Assert-True ((Get-RequiredJsonProperty -Object $manifestRoot -Name 'schemaVersion' -Kind Number).GetInt32() -eq 2) 'Trusted base manifest schema version is not accepted.'
        if (Test-JsonProperty -Object $manifestRoot -Name 'mediaType') {
            Assert-True ((Get-RequiredJsonProperty -Object $manifestRoot -Name 'mediaType' -Kind String).GetString() -ceq $selectedMediaType) 'Trusted base manifest media type does not match its descriptor.'
        }
        $config = Get-RequiredJsonProperty -Object $manifestRoot -Name 'config' -Kind Object
        Assert-True ((Get-RequiredJsonProperty -Object $config -Name 'mediaType' -Kind String).GetString() -ceq 'application/vnd.oci.image.config.v1+json' -or (Get-RequiredJsonProperty -Object $config -Name 'mediaType' -Kind String).GetString() -ceq 'application/vnd.docker.container.image.v1+json') 'Trusted base config media type is not accepted.'
        Assert-True ((Get-RequiredJsonProperty -Object $config -Name 'digest' -Kind String).GetString() -cmatch '^sha256:[0-9a-f]{64}$') 'Trusted base config digest is malformed.'
        $configSizeElement = Get-RequiredJsonProperty -Object $config -Name 'size' -Kind Number
        [long]$configSize = 0
        Assert-True ($configSizeElement.TryGetInt64([ref]$configSize) -and $configSize -gt 0 -and $configSize -le $MaxMetadataBytes) 'Trusted base config size is outside its bound.'

        $layerElements = Get-RequiredJsonProperty -Object $manifestRoot -Name 'layers' -Kind Array
        Assert-True ($layerElements.GetArrayLength() -gt 0 -and $layerElements.GetArrayLength() -lt $MaxLayers) 'Trusted base layer count is outside its bound.'
        $layers = [Collections.Generic.List[object]]::new()
        foreach ($layer in $layerElements.EnumerateArray()) {
            $mediaType = (Get-RequiredJsonProperty -Object $layer -Name 'mediaType' -Kind String).GetString()
            Assert-True ($mediaType -ceq 'application/vnd.oci.image.layer.v1.tar' -or $mediaType -ceq 'application/vnd.oci.image.layer.v1.tar+gzip' -or $mediaType -ceq 'application/vnd.docker.image.rootfs.diff.tar' -or $mediaType -ceq 'application/vnd.docker.image.rootfs.diff.tar.gzip') 'Trusted base layer media type is not accepted.'
            $digest = (Get-RequiredJsonProperty -Object $layer -Name 'digest' -Kind String).GetString()
            Assert-True ($digest -cmatch '^sha256:[0-9a-f]{64}$') 'Trusted base layer digest is malformed.'
            $sizeElement = Get-RequiredJsonProperty -Object $layer -Name 'size' -Kind Number
            [long]$size = 0
            Assert-True ($sizeElement.TryGetInt64([ref]$size) -and $size -gt 0 -and $size -le $MaxOuterMemberBytes) 'Trusted base layer size is outside its bound.'
            $layers.Add([pscustomobject]@{ MediaType = $mediaType; Digest = $digest; Size = $size })
        }
        return [pscustomobject]@{ IndexDigest = $actualIndexDigest; ManifestDigest = $actualManifestDigest; Layers = $layers }
    }
    finally { $manifestDocument.Dispose() }
}

function Assert-Descriptor {
    param(
        [Parameter(Mandatory)][Text.Json.JsonElement]$Descriptor,
        [Parameter(Mandatory)][string]$ExpectedMediaType,
        [Parameter(Mandatory)][Collections.Generic.Dictionary[string,object]]$Files,
        [Parameter(Mandatory)][long]$MaximumBytes
    )

    $mediaType = (Get-RequiredJsonProperty -Object $Descriptor -Name 'mediaType' -Kind String).GetString()
    Assert-True ($mediaType -ceq $ExpectedMediaType) 'OCI descriptor media type is not accepted.'
    $digest = (Get-RequiredJsonProperty -Object $Descriptor -Name 'digest' -Kind String).GetString()
    Assert-True ($digest -cmatch '^sha256:[0-9a-f]{64}$') 'OCI descriptor digest is malformed.'
    $sizeElement = Get-RequiredJsonProperty -Object $Descriptor -Name 'size' -Kind Number
    [long]$size = 0
    Assert-True ($sizeElement.TryGetInt64([ref]$size)) 'OCI descriptor size is not an integer.'
    Assert-True ($size -ge 0 -and $size -le $MaximumBytes) 'OCI descriptor size is outside its bound.'
    $path = 'blobs/sha256/' + $digest.Substring(7)
    Assert-True ($Files.ContainsKey($path)) 'OCI descriptor names a missing blob.'
    $file = $Files[$path]
    Assert-True ($file.Size -eq $size) 'OCI descriptor size does not match its blob.'
    Assert-True ($file.Sha256 -ceq $digest.Substring(7)) 'OCI descriptor digest does not match its blob.'
    return [pscustomobject]@{ Digest = $digest; Path = $path; File = $file; MediaType = $mediaType; Size = $size }
}

function Read-OuterArchive {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$SpoolRoot,
        [Parameter(Mandatory)][byte[][]]$ForbiddenNeedles
    )

    $files = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [long]$scannedBytes = 0
    [int]$entryCount = 0
    $input = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read, 65536, [IO.FileOptions]::SequentialScan)
    $reader = [System.Formats.Tar.TarReader]::new($input, $false)
    try {
        while (($entry = $reader.GetNextEntry()) -ne $null) {
            $entryCount++
            Assert-True ($entryCount -le $MaxOuterEntries) 'OCI archive contains too many entries.'
            $isDirectory = $entry.EntryType -eq [System.Formats.Tar.TarEntryType]::Directory
            $normalized = Get-SafeArchivePath -Path $entry.Name -IsDirectory $isDirectory
            Assert-True ($seen.Add($normalized)) 'OCI archive contains a duplicate normalized path.'
            Assert-True (-not ([IO.Path]::GetFileName($normalized).StartsWith('.wh.', [StringComparison]::Ordinal))) 'OCI archive contains an unexpected whiteout.'

            if ($isDirectory) { continue }
            Assert-True ($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::RegularFile) 'OCI archive contains a link or special node.'
            Assert-True ($entry.Length -ge 0 -and $entry.Length -le $MaxOuterMemberBytes) 'OCI archive member is outside its byte bound.'
            Assert-True ($normalized -ceq 'oci-layout' -or $normalized -ceq 'index.json' -or $normalized -cmatch '^blobs/sha256/[0-9a-f]{64}$') 'OCI archive contains an unexpected file.'
            $spoolPath = Join-Path $SpoolRoot ('outer-' + $entryCount.ToString('D4') + '.bin')
            $scan = Read-AndScanStream -Stream $entry.DataStream -MaximumBytes $entry.Length -ForbiddenNeedles $ForbiddenNeedles -Subject "OCI member $normalized" -CopyTo -DestinationPath $spoolPath
            Assert-True ($scan.Bytes -eq $entry.Length) 'OCI archive member length is inconsistent.'
            Assert-True ($scannedBytes -le $MaxArchiveBytes - $scan.Bytes) 'OCI archive member total exceeds its byte ceiling.'
            $scannedBytes += $scan.Bytes
            $files.Add($normalized, [pscustomobject]@{ Path = $spoolPath; Size = $scan.Bytes; Sha256 = $scan.Sha256 })
        }
    }
    finally {
        $reader.Dispose()
        $input.Dispose()
    }
    Assert-True ($files.ContainsKey('oci-layout') -and $files.ContainsKey('index.json')) 'OCI archive is missing required layout metadata.'
    return [pscustomobject]@{ Files = $files; Bytes = $scannedBytes; Entries = $entryCount }
}

function Expand-LayerToPrivateSpool {
    param(
        [Parameter(Mandatory)][object]$Layer,
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][byte[][]]$ForbiddenNeedles
    )

    $input = [IO.FileStream]::new($Layer.File.Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $content = $input
    $gzip = $null
    try {
        if ($Layer.MediaType -ceq 'application/vnd.oci.image.layer.v1.tar+gzip' -or $Layer.MediaType -ceq 'application/vnd.docker.image.rootfs.diff.tar.gzip') {
            $gzip = [IO.Compression.GZipStream]::new($input, [IO.Compression.CompressionMode]::Decompress, $false)
            $content = $gzip
        }
        elseif ($Layer.MediaType -cne 'application/vnd.oci.image.layer.v1.tar' -and $Layer.MediaType -cne 'application/vnd.docker.image.rootfs.diff.tar') {
            throw 'OCI layer compression is unsupported and fails closed.'
        }
        return Read-AndScanStream -Stream $content -MaximumBytes $MaxExpandedLayerBytes -ForbiddenNeedles $ForbiddenNeedles -Subject 'expanded OCI layer' -CopyTo -DestinationPath $DestinationPath
    }
    finally {
        if ($gzip) { $gzip.Dispose() }
        else { $input.Dispose() }
    }
}

function Scan-LayerTar {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][byte[][]]$ForbiddenNeedles,
        [Parameter(Mandatory)][Collections.Generic.Dictionary[string,object]]$ExpectedGraphFiles,
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.HashSet[string]]$FoundGraphFiles,
        [Parameter(Mandatory)][bool]$IsApplicationLayer,
        [Parameter(Mandatory)][bool]$AllowBaseWhiteout
    )

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [int]$entryCount = 0
    [int]$fileCount = 0
    [long]$bytes = 0
    $nonOwnerWriteMask = [int][IO.UnixFileMode]::GroupWrite -bor [int][IO.UnixFileMode]::OtherWrite
    $input = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read, 65536, [IO.FileOptions]::SequentialScan)
    $reader = [System.Formats.Tar.TarReader]::new($input, $false)
    try {
        while (($entry = $reader.GetNextEntry()) -ne $null) {
            $entryCount++
            Assert-True ($entryCount -le $MaxLayerEntries) 'OCI layer contains too many entries.'
            $isDirectory = $entry.EntryType -eq [System.Formats.Tar.TarEntryType]::Directory
            $normalized = Get-SafeArchivePath -Path $entry.Name -IsDirectory $isDirectory
            Assert-True ($seen.Add($normalized)) 'OCI layer contains a duplicate normalized path.'
            if ($IsApplicationLayer) {
                Assert-True ($entry.Uid -eq 0 -and $entry.Gid -eq 0) 'OCI application layer entries must be owned by root.'
                Assert-True (([int]$entry.Mode -band $nonOwnerWriteMask) -eq 0) 'OCI application layer entries cannot be group- or world-writable.'
            }
            $isWhiteout = [IO.Path]::GetFileName($normalized).StartsWith('.wh.', [StringComparison]::Ordinal)
            if ($isWhiteout) {
                Assert-True ($AllowBaseWhiteout -and -not $IsApplicationLayer) 'OCI application layer contains an unexpected whiteout.'
                if ($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::CharacterDevice) {
                    Assert-True ($entry.DeviceMajor -eq 0 -and $entry.DeviceMinor -eq 0) 'OCI base whiteout device is malformed.'
                    continue
                }
                Assert-True ($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::RegularFile -and $entry.Length -eq 0) 'OCI base whiteout entry is malformed.'
                continue
            }

            if ($isDirectory) { continue }
            if ($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::SymbolicLink) {
                Assert-SafeLinkTarget -EntryPath $normalized -Target $entry.LinkName -HardLink $false
                continue
            }
            if ($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::HardLink) {
                Assert-SafeLinkTarget -EntryPath $normalized -Target $entry.LinkName -HardLink $true
                continue
            }
            Assert-True ($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::RegularFile) 'OCI layer contains a device or special node.'
            $fileCount++
            Assert-True ($fileCount -le $MaxLayerFiles) 'OCI layer contains too many files.'
            Assert-True ($entry.Length -ge 0 -and $entry.Length -le $MaxLayerFileBytes) 'OCI layer file is outside its byte bound.'
            $scan = Read-AndScanStream -Stream $entry.DataStream -MaximumBytes $entry.Length -ForbiddenNeedles $ForbiddenNeedles -Subject "OCI layer file $normalized"
            Assert-True ($scan.Bytes -eq $entry.Length) 'OCI layer file length is inconsistent.'
            Assert-True ($bytes -le $MaxExpandedLayerBytes - $scan.Bytes) 'OCI layer file total exceeds its byte ceiling.'
            $bytes += $scan.Bytes

            $graphPrefix = 'app/preview-graph/'
            if ($normalized.StartsWith($graphPrefix, [StringComparison]::Ordinal)) {
                Assert-True ($IsApplicationLayer) 'Public graph material appears outside the final application layer.'
                $relativeGraphPath = $normalized.Substring($graphPrefix.Length)
                Assert-True ($ExpectedGraphFiles.ContainsKey($relativeGraphPath)) 'OCI application layer contains an unreviewed public graph file.'
                $expectedGraphFile = $ExpectedGraphFiles[$relativeGraphPath]
                Assert-True ($expectedGraphFile.Size -eq $scan.Bytes -and $expectedGraphFile.Sha256 -ceq $scan.Sha256) 'OCI application layer public graph bytes do not match the reviewed graph.'
                Assert-True ($FoundGraphFiles.Add($relativeGraphPath)) 'OCI application layer repeats a public graph file.'
            }
        }
    }
    finally {
        $reader.Dispose()
        $input.Dispose()
    }
    return [pscustomobject]@{ Entries = $entryCount; Files = $fileCount; Bytes = $bytes }
}

function Scan-PublicGraph {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][byte[][]]$ForbiddenNeedles
    )

    $rootPath = [IO.Path]::GetFullPath($Root)
    Assert-True ([IO.Directory]::Exists($rootPath)) 'Public graph root does not exist.'
    $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $pending.Push([IO.DirectoryInfo]::new($rootPath))
    $graphFiles = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    [int]$entries = 0
    [int]$files = 0
    [long]$bytes = 0
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in $directory.EnumerateFileSystemInfos()) {
            $entries++
            Assert-True ($entries -le $MaxPublicGraphEntries) 'Public graph contains too many entries.'
            Assert-True (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Public graph contains a link or reparse point.'
            if ($item -is [IO.DirectoryInfo]) {
                $pending.Push($item)
                continue
            }
            Assert-True ($item -is [IO.FileInfo]) 'Public graph contains an unsupported entry.'
            $files++
            Assert-True ($item.Length -le $MaxPublicGraphFileBytes) 'Public graph file exceeds its byte ceiling.'
            Assert-True ($bytes -le $MaxPublicGraphBytes - $item.Length) 'Public graph exceeds its cumulative byte ceiling.'
            $relativePath = [IO.Path]::GetRelativePath($rootPath, $item.FullName).Replace('\', '/')
            $normalized = Get-SafeArchivePath -Path $relativePath -IsDirectory $false
            Assert-True (-not ([IO.Path]::GetFileName($normalized).StartsWith('.wh.', [StringComparison]::Ordinal))) 'Public graph contains a whiteout-shaped file.'
            Assert-True (-not $graphFiles.ContainsKey($normalized)) 'Public graph contains a duplicate normalized path.'
            $stream = $item.OpenRead()
            try {
                $scan = Read-AndScanStream -Stream $stream -MaximumBytes $item.Length -ForbiddenNeedles $ForbiddenNeedles -Subject "public graph file $($item.Name)"
                Assert-True ($scan.Bytes -eq $item.Length) 'Public graph file length is inconsistent.'
                $bytes += $scan.Bytes
                $graphFiles.Add($normalized, [pscustomobject]@{ Size = $scan.Bytes; Sha256 = $scan.Sha256 })
            }
            finally { $stream.Dispose() }
        }
    }
    Assert-True ($files -gt 0) 'Public graph is empty.'
    return [pscustomobject]@{ Entries = $entries; Files = $files; Bytes = $bytes; GraphFiles = $graphFiles }
}

function Add-DockerArchiveFile {
    param(
        [Parameter(Mandatory)][System.Formats.Tar.TarWriter]$Writer,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Path
    )

    $entry = [System.Formats.Tar.UstarTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $Name)
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read, 65536, [IO.FileOptions]::SequentialScan)
    try {
        $entry.DataStream = $stream
        $entry.ModificationTime = [DateTimeOffset]::UnixEpoch
        $entry.Uid = 0
        $entry.Gid = 0
        $entry.UserName = ''
        $entry.GroupName = ''
        $Writer.WriteEntry($entry)
    }
    finally { $stream.Dispose() }
}

function Write-DockerArchive {
    param(
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][string]$ImageReference,
        [Parameter(Mandatory)][object]$ConfigDescriptor,
        [Parameter(Mandatory)][Collections.Generic.List[object]]$ExpandedLayers,
        [Parameter(Mandatory)][string]$SpoolRoot
    )

    $configName = $ConfigDescriptor.Digest.Substring(7) + '.json'
    $layerNames = @($ExpandedLayers | ForEach-Object { $_.Name })
    $manifestObject = [ordered]@{
        Config = $configName
        RepoTags = @($ImageReference)
        Layers = $layerNames
    }
    $manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes('[' + ($manifestObject | ConvertTo-Json -Depth 4 -Compress) + ']')
    $manifestPath = Join-Path $SpoolRoot 'docker-manifest.json'
    [IO.File]::WriteAllBytes($manifestPath, $manifestBytes)

    $partialPath = $DestinationPath + '.partial.' + [Guid]::NewGuid().ToString('N')
    try {
        $output = [IO.FileStream]::new($partialPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $writer = [System.Formats.Tar.TarWriter]::new($output, [System.Formats.Tar.TarEntryFormat]::Ustar, $true)
        try {
            Add-DockerArchiveFile -Writer $writer -Name $configName -Path $ConfigDescriptor.File.Path
            foreach ($layer in $ExpandedLayers) {
                Add-DockerArchiveFile -Writer $writer -Name $layer.Name -Path $layer.Path
            }
            Add-DockerArchiveFile -Writer $writer -Name 'manifest.json' -Path $manifestPath
        }
        finally {
            $writer.Dispose()
            $output.Flush($true)
            $output.Dispose()
        }

        $archiveInfo = Get-Item -LiteralPath $partialPath
        Assert-True ($archiveInfo.Length -gt 0 -and $archiveInfo.Length -le $MaxDockerArchiveBytes) 'Docker runtime archive is outside its byte bound.'
        $hashStream = [IO.File]::OpenRead($partialPath)
        try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hashStream)).ToLowerInvariant() }
        finally { $hashStream.Dispose() }
        [IO.File]::Move($partialPath, $DestinationPath, $false)
        return [pscustomobject]@{ Sha256 = $digest; Bytes = $archiveInfo.Length }
    }
    finally {
        if ([IO.File]::Exists($partialPath)) { [IO.File]::Delete($partialPath) }
    }
}

$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
$archiveInfo = Get-Item -LiteralPath $resolvedArchive
Assert-True (-not $archiveInfo.PSIsContainer) 'OCI archive path is not a file.'
Assert-True ($archiveInfo.Length -gt 0 -and $archiveInfo.Length -le $MaxArchiveBytes) 'OCI archive is outside its byte bound.'
$resolvedGraph = (Resolve-Path -LiteralPath $PublicGraphRoot).Path
$resolvedBaseIndex = (Resolve-Path -LiteralPath $BaseIndexPath).Path
$resolvedBaseManifest = (Resolve-Path -LiteralPath $BaseManifestPath).Path
$resolvedResult = [IO.Path]::GetFullPath($ResultPath)
Assert-True (-not [string]::Equals($resolvedArchive, $resolvedResult, [StringComparison]::OrdinalIgnoreCase)) 'Result path aliases the OCI archive.'
$graphPrefix = $resolvedGraph.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
Assert-True (-not $resolvedResult.StartsWith($graphPrefix, [StringComparison]::OrdinalIgnoreCase)) 'Result path must remain outside the public graph.'
if ([IO.File]::Exists($resolvedResult)) { [IO.File]::Delete($resolvedResult) }
$resultDirectory = [IO.Path]::GetDirectoryName($resolvedResult)
Assert-True ([IO.Directory]::Exists($resultDirectory)) 'Result directory does not exist.'
$writeDockerArchive = -not [string]::IsNullOrWhiteSpace($DockerArchivePath)
Assert-True ($writeDockerArchive -eq (-not [string]::IsNullOrWhiteSpace($DockerImageReference))) 'Docker archive path and image reference must be supplied together.'
$resolvedDockerArchive = $null
if ($writeDockerArchive) {
    $resolvedDockerArchive = [IO.Path]::GetFullPath($DockerArchivePath)
    Assert-True (-not [string]::Equals($resolvedDockerArchive, $resolvedArchive, [StringComparison]::OrdinalIgnoreCase)) 'Docker runtime archive aliases the OCI archive.'
    Assert-True (-not [string]::Equals($resolvedDockerArchive, $resolvedBaseIndex, [StringComparison]::OrdinalIgnoreCase)) 'Docker runtime archive aliases the trusted base index.'
    Assert-True (-not [string]::Equals($resolvedDockerArchive, $resolvedBaseManifest, [StringComparison]::OrdinalIgnoreCase)) 'Docker runtime archive aliases the trusted base manifest.'
    Assert-True (-not [string]::Equals($resolvedDockerArchive, $resolvedResult, [StringComparison]::OrdinalIgnoreCase)) 'Docker runtime archive aliases the result.'
    Assert-True (-not $resolvedDockerArchive.StartsWith($graphPrefix, [StringComparison]::OrdinalIgnoreCase)) 'Docker runtime archive must remain outside the public graph.'
    Assert-True ([IO.Directory]::Exists([IO.Path]::GetDirectoryName($resolvedDockerArchive))) 'Docker runtime archive directory does not exist.'
    if ([IO.File]::Exists($resolvedDockerArchive)) { [IO.File]::Delete($resolvedDockerArchive) }
}

$spoolRoot = Join-Path ([IO.Path]::GetTempPath()) ('lex-v3-s0-05-oci-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($spoolRoot) | Out-Null
try {
    $forbiddenNeedles = New-ForbiddenNeedles -CanaryDigest $ExpectedPrivateKeyCanarySha256
    $trustedBase = Read-TrustedBase -IndexPath $BaseIndexPath -ManifestPath $BaseManifestPath -ExpectedDigest $ExpectedBaseImageDigest
    $archiveSpool = Join-Path $spoolRoot 'input-archive.tar'
    $archiveInput = [IO.FileStream]::new($resolvedArchive, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read, 65536, [IO.FileOptions]::SequentialScan)
    try {
        $archiveCopy = Read-AndScanStream -Stream $archiveInput -MaximumBytes $MaxArchiveBytes -ForbiddenNeedles $forbiddenNeedles -Subject 'OCI archive' -CopyTo -DestinationPath $archiveSpool
    }
    finally { $archiveInput.Dispose() }
    Assert-True ($archiveCopy.Bytes -eq $archiveInfo.Length) 'OCI archive changed while it was being read.'
    $outer = Read-OuterArchive -Path $archiveSpool -SpoolRoot $spoolRoot -ForbiddenNeedles $forbiddenNeedles
    $publicGraph = Scan-PublicGraph -Root $resolvedGraph -ForbiddenNeedles $forbiddenNeedles

    $layoutDocument = Read-JsonDocument -Path $outer.Files['oci-layout'].Path -MaximumBytes 4096
    try {
        $layoutVersion = (Get-RequiredJsonProperty -Object $layoutDocument.RootElement -Name 'imageLayoutVersion' -Kind String).GetString()
        Assert-True ($layoutVersion -ceq '1.0.0') 'OCI layout version is not accepted.'
    }
    finally { $layoutDocument.Dispose() }

    $indexDocument = Read-JsonDocument -Path $outer.Files['index.json'].Path -MaximumBytes $MaxMetadataBytes
    try {
        $indexRoot = $indexDocument.RootElement
        Assert-True ((Get-RequiredJsonProperty -Object $indexRoot -Name 'schemaVersion' -Kind Number).GetInt32() -eq 2) 'OCI index schema version is not accepted.'
        $manifests = Get-RequiredJsonProperty -Object $indexRoot -Name 'manifests' -Kind Array
        Assert-True ($manifests.GetArrayLength() -eq 1) 'OCI index must contain exactly one manifest.'
        $manifestDescriptor = Assert-Descriptor -Descriptor $manifests[0] -ExpectedMediaType 'application/vnd.oci.image.manifest.v1+json' -Files $outer.Files -MaximumBytes $MaxMetadataBytes
    }
    finally { $indexDocument.Dispose() }

    $manifestDocument = Read-JsonDocument -Path $manifestDescriptor.File.Path -MaximumBytes $MaxMetadataBytes
    try {
        $manifestRoot = $manifestDocument.RootElement
        Assert-True ((Get-RequiredJsonProperty -Object $manifestRoot -Name 'schemaVersion' -Kind Number).GetInt32() -eq 2) 'OCI manifest schema version is not accepted.'
        $manifestMediaType = (Get-RequiredJsonProperty -Object $manifestRoot -Name 'mediaType' -Kind String).GetString()
        Assert-True ($manifestMediaType -ceq 'application/vnd.oci.image.manifest.v1+json') 'OCI manifest media type is not accepted.'
        $configDescriptor = Assert-Descriptor -Descriptor (Get-RequiredJsonProperty -Object $manifestRoot -Name 'config' -Kind Object) -ExpectedMediaType 'application/vnd.oci.image.config.v1+json' -Files $outer.Files -MaximumBytes $MaxMetadataBytes
        $layerElements = Get-RequiredJsonProperty -Object $manifestRoot -Name 'layers' -Kind Array
        Assert-True ($layerElements.GetArrayLength() -gt 0 -and $layerElements.GetArrayLength() -le $MaxLayers) 'OCI layer count is outside its bound.'
        $layers = [Collections.Generic.List[object]]::new()
        $layerDigests = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($layerElement in $layerElements.EnumerateArray()) {
            $mediaType = (Get-RequiredJsonProperty -Object $layerElement -Name 'mediaType' -Kind String).GetString()
            Assert-True ($mediaType -ceq 'application/vnd.oci.image.layer.v1.tar' -or $mediaType -ceq 'application/vnd.oci.image.layer.v1.tar+gzip' -or $mediaType -ceq 'application/vnd.docker.image.rootfs.diff.tar' -or $mediaType -ceq 'application/vnd.docker.image.rootfs.diff.tar.gzip') 'OCI layer media type is not accepted.'
            $layer = Assert-Descriptor -Descriptor $layerElement -ExpectedMediaType $mediaType -Files $outer.Files -MaximumBytes $MaxOuterMemberBytes
            Assert-True ($layerDigests.Add($layer.Digest)) 'OCI manifest repeats a layer digest.'
            $layers.Add($layer)
        }
        Assert-True ($layers.Count -gt $trustedBase.Layers.Count) 'OCI image does not contain an application layer after the trusted base prefix.'
        for ($baseIndex = 0; $baseIndex -lt $trustedBase.Layers.Count; $baseIndex++) {
            $expectedBaseLayer = $trustedBase.Layers[$baseIndex]
            $actualBaseLayer = $layers[$baseIndex]
            Assert-True ($actualBaseLayer.Digest -ceq $expectedBaseLayer.Digest -and $actualBaseLayer.Size -eq $expectedBaseLayer.Size -and $actualBaseLayer.MediaType -ceq $expectedBaseLayer.MediaType) 'OCI image base layer prefix does not match the trusted base manifest.'
        }
    }
    finally { $manifestDocument.Dispose() }

    $referenced = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $null = $referenced.Add($manifestDescriptor.Path)
    $null = $referenced.Add($configDescriptor.Path)
    foreach ($layer in $layers) { $null = $referenced.Add($layer.Path) }
    foreach ($path in $outer.Files.Keys) {
        if ($path.StartsWith('blobs/sha256/', [StringComparison]::Ordinal)) {
            Assert-True ($referenced.Contains($path)) 'OCI archive contains an unreferenced blob.'
        }
    }

    $configDocument = Read-JsonDocument -Path $configDescriptor.File.Path -MaximumBytes $MaxMetadataBytes
    try {
        $configRoot = $configDocument.RootElement
        Assert-OptionalUtcCreated -Object $configRoot -Subject 'OCI config'
        Assert-True ((Get-RequiredJsonProperty -Object $configRoot -Name 'architecture' -Kind String).GetString() -ceq 'amd64') 'OCI architecture is not linux-x64.'
        Assert-True ((Get-RequiredJsonProperty -Object $configRoot -Name 'os' -Kind String).GetString() -ceq 'linux') 'OCI operating system is not Linux.'
        $runtimeConfig = Get-RequiredJsonProperty -Object $configRoot -Name 'config' -Kind Object
        Assert-True ((Get-RequiredJsonProperty -Object $runtimeConfig -Name 'User' -Kind String).GetString() -ceq '1654') 'OCI runtime user is not UID 1654.'

        $environment = Get-RequiredJsonProperty -Object $runtimeConfig -Name 'Env' -Kind Array
        Assert-True ($environment.GetArrayLength() -le 64) 'OCI environment has too many entries.'
        $environmentNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($environmentEntry in $environment.EnumerateArray()) {
            Assert-True ($environmentEntry.ValueKind -eq [Text.Json.JsonValueKind]::String) 'OCI environment entry is not a string.'
            $text = $environmentEntry.GetString()
            Assert-True ($text.Length -le 4096 -and $text.Contains('=')) 'OCI environment entry is malformed.'
            $name = $text.Substring(0, $text.IndexOf('='))
            Assert-True ($name -cmatch '^[A-Za-z_][A-Za-z0-9_]*$') 'OCI environment name is malformed.'
            Assert-True ($environmentNames.Add($name)) 'OCI environment repeats a name.'
            Assert-True (-not ($name -imatch '(^|_)(SECRET|TOKEN|PASSWORD|PASSWD|PWD|API_?KEY|PRIVATE_?KEY|CREDENTIAL|CONNECTION_?STRING)(_|$)')) 'OCI environment contains a secret-bearing name.'
        }

        $labels = Get-RequiredJsonProperty -Object $runtimeConfig -Name 'Labels' -Kind Object
        $expectedLabels = [ordered]@{
            'org.opencontainers.image.authors' = 'Lex.V3.Api'
            'org.opencontainers.image.version' = '1.0.0'
            'org.opencontainers.image.base.name' = "mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@$ExpectedBaseImageDigest"
            'net.dot.runtime.majorminor' = '10.0'
            'net.dot.sdk.version' = '10.0.400'
            'org.opencontainers.image.base.digest' = $trustedBase.ManifestDigest
        }
        $actualLabelCount = @($labels.EnumerateObject()).Count
        Assert-True ($actualLabelCount -eq $expectedLabels.Count) 'OCI config label set is not the closed reviewed set.'
        foreach ($expectedLabel in $expectedLabels.GetEnumerator()) {
            Assert-True (Test-JsonProperty -Object $labels -Name $expectedLabel.Key) "OCI config is missing label '$($expectedLabel.Key)'."
            $actualLabel = $labels.GetProperty($expectedLabel.Key)
            Assert-True ($actualLabel.ValueKind -eq [Text.Json.JsonValueKind]::String) "OCI label '$($expectedLabel.Key)' is not a string."
            Assert-True ($actualLabel.GetString() -ceq $expectedLabel.Value) "OCI label '$($expectedLabel.Key)' does not match the reviewed value."
        }

        $rootFileSystem = Get-RequiredJsonProperty -Object $configRoot -Name 'rootfs' -Kind Object
        Assert-True ((Get-RequiredJsonProperty -Object $rootFileSystem -Name 'type' -Kind String).GetString() -ceq 'layers') 'OCI rootfs type is not accepted.'
        $diffIds = Get-RequiredJsonProperty -Object $rootFileSystem -Name 'diff_ids' -Kind Array
        Assert-True ($diffIds.GetArrayLength() -eq $layers.Count) 'OCI diff-id count does not match its layers.'
        $history = Get-RequiredJsonProperty -Object $configRoot -Name 'history' -Kind Array
        Assert-True ($history.GetArrayLength() -le 256) 'OCI history exceeds its count bound.'
        $historyLayerIndex = 0
        foreach ($historyEntry in $history.EnumerateArray()) {
            Assert-True ($historyEntry.ValueKind -eq [Text.Json.JsonValueKind]::Object) 'OCI history entry is not an object.'
            $emptyLayer = $false
            Assert-OptionalUtcCreated -Object $historyEntry -Subject 'OCI history'
            if (Test-JsonProperty -Object $historyEntry -Name 'empty_layer') {
                $emptyLayerElement = $historyEntry.GetProperty('empty_layer')
                Assert-True ($emptyLayerElement.ValueKind -eq [Text.Json.JsonValueKind]::True -or $emptyLayerElement.ValueKind -eq [Text.Json.JsonValueKind]::False) 'OCI history empty_layer flag is not Boolean.'
                $emptyLayer = $emptyLayerElement.GetBoolean()
            }
            if (-not $emptyLayer) { $historyLayerIndex++ }
        }
        Assert-True ($historyLayerIndex -eq $layers.Count) 'OCI history does not reconcile with the layer count.'

        [long]$expandedBytes = 0
        [long]$layerContentBytes = 0
        [int]$layerFileCount = 0
        $expandedLayers = [Collections.Generic.List[object]]::new()
        $foundGraphFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        for ($index = 0; $index -lt $layers.Count; $index++) {
            $layerSpool = Join-Path $spoolRoot ('layer-' + $index.ToString('D3') + '.tar')
            $expanded = Expand-LayerToPrivateSpool -Layer $layers[$index] -DestinationPath $layerSpool -ForbiddenNeedles $forbiddenNeedles
            $expandedLayers.Add([pscustomobject]@{ Name = $layers[$index].Digest.Substring(7) + '/layer.tar'; Path = $layerSpool })
            Assert-True ($expandedBytes -le $MaxExpandedImageBytes - $expanded.Bytes) 'Expanded OCI image exceeds its byte ceiling.'
            $expandedBytes += $expanded.Bytes
            $diffId = $diffIds[$index]
            Assert-True ($diffId.ValueKind -eq [Text.Json.JsonValueKind]::String -and $diffId.GetString() -ceq ('sha256:' + $expanded.Sha256)) 'OCI layer diff-id does not match the expanded layer.'
            $layerScan = Scan-LayerTar -Path $layerSpool -ForbiddenNeedles $forbiddenNeedles -ExpectedGraphFiles $publicGraph.GraphFiles -FoundGraphFiles $foundGraphFiles -IsApplicationLayer ($index -eq $layers.Count - 1) -AllowBaseWhiteout ($index -lt $trustedBase.Layers.Count)
            $layerContentBytes += $layerScan.Bytes
            $layerFileCount += $layerScan.Files
        }
        Assert-True ($foundGraphFiles.Count -eq $publicGraph.GraphFiles.Count) 'OCI application layer is missing reviewed public graph files.'
    }
    finally { $configDocument.Dispose() }

    $dockerArchive = $null
    if ($writeDockerArchive) {
        $dockerArchive = Write-DockerArchive -DestinationPath $resolvedDockerArchive -ImageReference $DockerImageReference -ConfigDescriptor $configDescriptor -ExpandedLayers $expandedLayers -SpoolRoot $spoolRoot
    }

    $archiveDigest = $archiveCopy.Sha256
    [long]$scannedBytes = $outer.Bytes + $expandedBytes + $layerContentBytes + $publicGraph.Bytes
    [long]$scannedFiles = $outer.Files.Count + $layerFileCount + $publicGraph.Files
    $result = [ordered]@{
        schema = 'lex-v3-s0-05-oci-verification/1'
        archive_sha256 = $archiveDigest
        manifest_digest = $manifestDescriptor.Digest
        config_digest = $configDescriptor.Digest
        base_image_digest = $ExpectedBaseImageDigest
        base_manifest_digest = $trustedBase.ManifestDigest
        layer_count = $layers.Count
        scanned_bytes = $scannedBytes
        scanned_files = $scannedFiles
        checks = [ordered]@{
            archive_bounds = $true
            normalized_paths_unique = $true
            links_and_nodes_safe = $true
            descriptor_integrity = $true
            trusted_base_prefix = $true
            manifest_config_history_inspected = $true
            created_fields_well_formed = $true
            environment_and_labels_safe = $true
            all_layers_scanned = $true
            public_graph_scanned = $true
            private_material_absent = $true
            legacy_and_production_canaries_absent = $true
        }
    }
    if ($writeDockerArchive) { $result['docker_archive_sha256'] = $dockerArchive.Sha256 }
    $json = ($result | ConvertTo-Json -Depth 5 -Compress) + "`n"
    $partialResult = $resolvedResult + '.partial.' + [Guid]::NewGuid().ToString('N')
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
        $stream = [IO.FileStream]::new($partialResult, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally { $stream.Dispose() }
        [IO.File]::Move($partialResult, $resolvedResult, $true)
    }
    finally {
        if ([IO.File]::Exists($partialResult)) { [IO.File]::Delete($partialResult) }
    }
    Write-Host "S0-05 OCI archive and public graph verified across $scannedFiles files."
}
finally {
    $resolvedSpool = [IO.Path]::GetFullPath($spoolRoot)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    Assert-True ($resolvedSpool.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolvedSpool).StartsWith('lex-v3-s0-05-oci-', [StringComparison]::Ordinal)) 'Refusing to remove an unexpected spool directory.'
    if ([IO.Directory]::Exists($resolvedSpool)) { Remove-Item -LiteralPath $resolvedSpool -Recurse -Force }
}
