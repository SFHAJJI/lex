using System.Security.Cryptography;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest;

/// <summary>
/// An index a build wrote to a file and verified there: its digest and length, its measured capability manifest, and where
/// the file is. The mount build (<see cref="V3FirstMountBuild"/>) builds each index this way, twice, into its own staging
/// directory and compares the two files' SHA-256 (slice S4b): no index is held in memory, the second file is removed at
/// once, and the mount writer moves the first into the mount directory.
/// </summary>
public sealed class V3BuiltIndexFile
{
    internal V3BuiltIndexFile(
        string path,
        SourceArtifactRef indexRef,
        long byteLength,
        SourceArtifactRef capabilityManifestRef,
        ReadOnlyMemory<byte> capabilityManifestBytes,
        V3IndexCapabilityManifest capabilityManifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(indexRef);
        ArgumentNullException.ThrowIfNull(capabilityManifestRef);
        ArgumentNullException.ThrowIfNull(capabilityManifest);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        Path = path;
        IndexRef = indexRef;
        ByteLength = byteLength;
        CapabilityManifestRef = capabilityManifestRef;
        CapabilityManifestBytes = capabilityManifestBytes;
        CapabilityManifest = capabilityManifest;
    }

    /// <summary>The index's identity: its file's SHA-256, as both builds measured it from the file.</summary>
    public SourceArtifactRef IndexRef { get; }

    /// <summary>The index file's length in bytes.</summary>
    public long ByteLength { get; }

    public SourceArtifactRef CapabilityManifestRef { get; }

    /// <summary>The capability manifest's canonical bytes (kilobytes; held, never staged).</summary>
    public ReadOnlyMemory<byte> CapabilityManifestBytes { get; }

    public V3IndexCapabilityManifest CapabilityManifest { get; }

    /// <summary>Where the file is now: the build's staging directory until the writer moves it, then the mount directory.</summary>
    internal string Path { get; set; }

    /// <summary>Whether the file is still in the build's staging directory, so the writer moves it rather than copying it.</summary>
    internal bool Staged { get; set; } = true;

    /// <summary>The SHA-256 of a file, read once in order, and its length: never the file's bytes in memory.</summary>
    internal static (string Sha256, long Length) Sha256OfFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return (Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length);
    }

    /// <summary><see cref="Sha256OfFile"/>, asynchronously.</summary>
    internal static async Task<(string Sha256, long Length)> Sha256OfFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)), stream.Length);
    }

    /// <summary>Whether two builds wrote the same index: the same digest and length, and the same capability manifest, byte for byte.</summary>
    internal static bool Same(V3BuiltIndexFile first, V3BuiltIndexFile second) =>
        first.IndexRef == second.IndexRef &&
        first.ByteLength == second.ByteLength &&
        first.CapabilityManifestRef == second.CapabilityManifestRef &&
        first.CapabilityManifestBytes.Span.SequenceEqual(second.CapabilityManifestBytes.Span);
}
