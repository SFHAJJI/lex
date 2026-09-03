using Lex.V3.Contracts.Custody;

namespace Lex.V3.Artifacts;

/// <summary>
/// A create-only content-addressed local store that explicitly proves no retention enforcement.
/// </summary>
/// <remarks>
/// This adapter exercises the production ordering contract in source builds. It is not production
/// retention evidence. A complete, flushed temporary file is atomically published before the
/// content address is read back. An existing address is accepted only after exact-byte readback.
/// </remarks>
public sealed class FileSystemCustodyStore : ICustodyStore
{
    private readonly string _root;
    private readonly TimeProvider _time;
    private readonly Action? _beforePublish;

    public FileSystemCustodyStore(string root, TimeProvider? time = null)
        : this(root, time, beforePublish: null)
    {
    }

    internal FileSystemCustodyStore(
        string root,
        TimeProvider? time,
        Action? beforePublish)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _time = time ?? TimeProvider.System;
        _beforePublish = beforePublish;
    }

    public async Task<DurableBlobWriteReceipt> CreateAsync(
        ReadOnlyMemory<byte> bytes,
        CustodyClass custodyClass,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCreate(bytes, custodyClass);

        // Direct callers can still own and mutate the backing array while an asynchronous write is
        // suspended. The store therefore publishes only this bounded private copy.
        var frozen = bytes.ToArray();
        var digest = CustodyDigest.Of(frozen, cancellationToken);
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, digest, frozen.LongLength, custodyClass);
        var directory = Path.Combine(_root, ClassSegment(custodyClass));
        EnsureLaneDirectory(directory, create: true);
        var path = Path.Combine(directory, digest);
        RejectOccupiedNonFileOrReparsePoint(path);
        var pending = Path.Combine(directory, $"{digest}.{Guid.NewGuid():N}.partial");

        try
        {
            await using (var stream = new FileStream(
                             pending,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(frozen, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            _beforePublish?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(pending, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // A concurrent or prior exact create won. The single verified read below decides
                // whether that object is an idempotent success or an integrity incident.
            }
        }
        finally
        {
            if (File.Exists(pending))
            {
                File.Delete(pending);
            }
        }

        _ = await ReadAsync(reference, cancellationToken).ConfigureAwait(false);
        var observedAt = _time.GetUtcNow().ToUniversalTime();
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            null,
            CustodyProtection.NotEnforced,
            observedAt,
            null);

        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            policy);
    }

    public async Task<ReadOnlyMemory<byte>> ReadAsync(
        DurableBlobRef reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(
            _root,
            ClassSegment(reference.CustodyClass),
            reference.ContentSha256);

        try
        {
            EnsureLaneDirectory(Path.GetDirectoryName(path)!, create: false);
            RejectOccupiedNonFileOrReparsePoint(path);
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (stream.Length != reference.ByteLength)
            {
                throw new CustodyIntegrityException(
                    "The retained object length differs from its durable reference.");
            }

            var bytes = GC.AllocateUninitializedArray<byte>(checked((int)reference.ByteLength));
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (await HasAnotherByteAsync(stream, cancellationToken).ConfigureAwait(false)
                || !string.Equals(
                    CustodyDigest.Of(bytes, cancellationToken),
                    reference.ContentSha256,
                    StringComparison.Ordinal))
            {
                throw new CustodyIntegrityException(
                    "The retained object bytes differ from their durable reference.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return bytes;
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CustodyIntegrityException(
                "A promised retained object is missing.", exception);
        }
        catch (EndOfStreamException exception)
        {
            throw new CustodyIntegrityException(
                "The retained object ended before its promised length.", exception);
        }
    }

    public async Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
        string contentSha256,
        CancellationToken cancellationToken)
    {
        if (!CustodyDigest.IsLowercaseSha256(contentSha256))
        {
            throw new ArgumentException(
                "A content-addressed reopen requires one lowercase SHA-256.",
                nameof(contentSha256));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlyMemory<byte>? selected = null;
        foreach (var custodyClass in Enum.GetValues<CustodyClass>())
        {
            var directory = Path.Combine(_root, ClassSegment(custodyClass));
            var path = Path.Combine(directory, contentSha256);
            if (!Directory.Exists(directory) || !File.Exists(path))
            {
                continue;
            }

            EnsureLaneDirectory(directory, create: false);
            RejectOccupiedNonFileOrReparsePoint(path);
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length > CustodyBounds.MaxObjectBytes)
                {
                    throw new CustodyIntegrityException(
                        "The content-addressed artifact exceeds the custody bound.");
                }

                var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
                try
                {
                    await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
                catch (EndOfStreamException exception)
                {
                    throw new CustodyIntegrityException(
                        "The content-addressed artifact ended during readback.", exception);
                }

                if (await HasAnotherByteAsync(stream, cancellationToken).ConfigureAwait(false) ||
                    !string.Equals(
                        CustodyDigest.Of(bytes, cancellationToken),
                        contentSha256,
                        StringComparison.Ordinal))
                {
                    throw new CustodyIntegrityException(
                        "The retained artifact bytes differ from their content address.");
                }

                // Every lane holding the digest is read and verified against it above, so two
                // lanes can only both pass if their bytes are identical; a cross-lane byte
                // comparison here could fire only on a SHA-256 collision and is not kept.
                selected ??= bytes;
            }
            catch (Exception exception)
                when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                throw new CustodyIntegrityException(
                    "An enumerated content-addressed artifact disappeared before readback.",
                    exception);
            }
        }

        return selected
            ?? throw new CustodyIntegrityException(
                "The content-addressed artifact is not retained by this store.");
    }

    private static async Task<bool> HasAnotherByteAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var sentinel = new byte[1];
        return await stream.ReadAsync(sentinel, cancellationToken).ConfigureAwait(false) != 0;
    }

    private static void ValidateCreate(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass)
    {
        if (bytes.Length > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytes),
                "A body above the admitted bound is refused before the filesystem is touched.");
        }

        if (!Enum.IsDefined(custodyClass))
        {
            throw new ArgumentOutOfRangeException(
                nameof(custodyClass), custodyClass, "Unknown custody class.");
        }
    }

    private void EnsureLaneDirectory(string directory, bool create)
    {
        if (create)
        {
            Directory.CreateDirectory(_root);
        }

        RejectReparseComponents(_root);
        if (create)
        {
            Directory.CreateDirectory(directory);
        }

        RejectReparseComponents(directory);
    }

    private static void RejectReparseComponents(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new CustodyIntegrityException("The custody path has no filesystem root.");
        var relative = Path.GetRelativePath(root, fullPath);
        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception)
                when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                throw new CustodyIntegrityException(
                    "A custody path component is missing.", exception);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new CustodyIntegrityException(
                    "A custody path traverses a symbolic link or reparse point.");
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new CustodyIntegrityException(
                    "A non-directory object occupies a custody path component.");
            }
        }
    }

    private static void RejectOccupiedNonFileOrReparsePoint(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new CustodyIntegrityException(
                "A directory occupies a durable content address.");
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new CustodyIntegrityException(
                "A symbolic link or reparse point occupies a durable content address.");
        }
    }

    private static string ClassSegment(CustodyClass custodyClass) => custodyClass switch
    {
        CustodyClass.NightlyFloor90d => "nightly-floor-90d",
        CustodyClass.LegalHoldEvidence => "legal-hold-evidence",
        _ => throw new ArgumentOutOfRangeException(
            nameof(custodyClass), custodyClass, "Unknown custody class."),
    };
}
