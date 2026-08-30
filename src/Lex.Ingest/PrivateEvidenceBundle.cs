using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.Law;
using Lex.Temporal;

namespace Lex.Ingest;

public sealed record PrivateEvidenceBundleIdentity
{
    public PrivateEvidenceBundleIdentity(
        string runIdentity,
        string codeCommit,
        string publisher,
        int sequence,
        string? previousBundleSha256)
    {
        RunIdentity = IngestRunIdentity.Require(
            runIdentity, "Private evidence run identity");
        CodeCommit = CodeIdentity.RequireFullCommit(
            codeCommit, nameof(codeCommit));
        Publisher = RequirePublisher(publisher);
        if (sequence is < 0 or > SourceRequestIdentity.MaximumOrdinal)
            throw new InvalidDataException(
                "Private evidence sequence is outside its allowed bound.");
        Sequence = sequence;
        if (sequence == 0 && previousBundleSha256 is not null)
            throw new InvalidDataException(
                "The first private evidence bundle cannot name a predecessor.");
        if (sequence > 0 && previousBundleSha256 is null)
            throw new InvalidDataException(
                "A chained private evidence bundle must name its predecessor.");
        PreviousBundleSha256 = previousBundleSha256 is null
            ? null
            : CodeIdentity.RequireSha256(
                previousBundleSha256, nameof(previousBundleSha256));
    }

    public string RunIdentity { get; }
    public string CodeCommit { get; }
    public string Publisher { get; }
    public int Sequence { get; }
    public string? PreviousBundleSha256 { get; }

    private static string RequirePublisher(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 64
            || value[0] is not (>= 'a' and <= 'z')
            || value.Any(character =>
                character is not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-'))
            throw new InvalidDataException(
                "Private evidence publisher must be a bounded lowercase ASCII token.");
        return value;
    }
}

/// <summary>A local handle. It is not durable evidence and cannot open response bytes.</summary>
public sealed record StagedEvidenceRef
{
    public StagedEvidenceRef(
        string requestId, string objectSha256, long byteLength)
    {
        RequestId = CodeIdentity.RequireSha256(requestId, nameof(requestId));
        ObjectSha256 = CodeIdentity.RequireSha256(
            objectSha256, nameof(objectSha256));
        if (byteLength is < 0 or > EvidenceRef.MaximumByteLength)
            throw new InvalidDataException(
                "Staged evidence byte length is outside its allowed bound.");
        ByteLength = byteLength;
    }

    public string RequestId { get; }
    public string ObjectSha256 { get; }
    public long ByteLength { get; }
}

public sealed record PrivateEvidenceRecord(
    SourceRequestIdentity Request,
    BoundedResponseMetadata Response,
    EvidenceRef Evidence);

/// <summary>
/// A locally sealed receipt. Local flushes improve staging crash behavior but do not
/// establish remote durability.
/// </summary>
public sealed record PrivateEvidenceBundleReceipt
{
    public PrivateEvidenceBundleReceipt(
        string stagingRoot,
        PrivateEvidenceBundleIdentity identity,
        string manifestSha256,
        IReadOnlyCollection<EvidenceRef> evidence)
    {
        if (!Path.IsPathFullyQualified(stagingRoot))
            throw new InvalidDataException(
                "Private evidence staging root must be absolute.");
        StagingRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(stagingRoot));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ManifestSha256 = CodeIdentity.RequireSha256(
            manifestSha256, nameof(manifestSha256));
        ArgumentNullException.ThrowIfNull(evidence);
        var ordered = evidence
            .OrderBy(item => item.RequestId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0
            || ordered.Select(item => item.RequestId)
                .Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException(
                "Private evidence receipt must inventory unique verified requests.");
        Evidence = Array.AsReadOnly(ordered);
    }

    public string StagingRoot { get; }
    public PrivateEvidenceBundleIdentity Identity { get; }
    public string ManifestSha256 { get; }
    public IReadOnlyList<EvidenceRef> Evidence { get; }

    /// <summary>
    /// Returns the commit-last marker for the already verified evidence inventory.
    /// The caller remains responsible for uploading and reading this marker back.
    /// </summary>
    public byte[] CreateCommitMarkerBytes()
    {
        var bytes = EvidenceJson.Write(new CommitDocument(
            PrivateEvidenceBundle.CommitMarkerSchema,
            ManifestSha256,
            Identity,
            Evidence));
        if (bytes.Length > PrivateEvidenceBundle.MaximumMarkerBytes)
            throw new InvalidDataException(
                "Private evidence commit marker exceeds its size bound.");
        return bytes;
    }
}

public sealed class VerifiedPrivateEvidenceBundle : IVerifiedResponseSet
{
    private readonly string _root;
    private readonly IReadOnlyDictionary<string, PrivateEvidenceRecord> _byRequest;

    internal VerifiedPrivateEvidenceBundle(
        string root,
        PrivateEvidenceBundleIdentity identity,
        IReadOnlyList<PrivateEvidenceRecord> records)
    {
        _root = root;
        Identity = identity;
        Records = records;
        _byRequest = records.ToDictionary(
            record => record.Request.RequestId, StringComparer.Ordinal);
    }

    public PrivateEvidenceBundleIdentity Identity { get; }
    public IReadOnlyList<PrivateEvidenceRecord> Records { get; }

    public Stream OpenBody(EvidenceRef evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!_byRequest.TryGetValue(evidence.RequestId, out var record)
            || record.Evidence != evidence)
            throw new InvalidDataException(
                "Evidence reference does not belong to this verified response set.");
        return EvidenceFiles.OpenVerifiedObject(_root, evidence);
    }
}

/// <summary>
/// Local capture, sealing, and readback mechanics. This type is deliberately not an
/// IRawResponseSink because CaptureAsync alone makes no remote durability claim.
/// </summary>
public sealed class PrivateEvidenceBundle
{
    public const string ManifestSchema = "lex-private-evidence-bundle/1";
    public const string CommitMarkerSchema = "lex-private-evidence-commit/1";
    public const string ResponseReceiptSchema = "lex-private-evidence-response/1";
    public const string ManifestFileName = "manifest.json";
    public const string CommitMarkerFileName = "commit.json";
    public const string ObjectsDirectoryName = "objects";
    public const string ReceiptsDirectoryName = "receipts";
    public const long MaximumBodyBytes = EvidenceRef.MaximumByteLength;
    private const int MaximumManifestBytes = 16 * 1024 * 1024;
    private const int MaximumResponseReceiptBytes = 64 * 1024;
    internal const int MaximumMarkerBytes = 8 * 1024 * 1024;

    private readonly string _root;
    private readonly string _objectsRoot;
    private readonly string _receiptsRoot;
    private readonly PrivateEvidenceBundleIdentity _identity;
    private readonly List<StagedRecord> _records = [];
    private readonly Dictionary<string, EvidenceRef> _verified =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _sealed;

    private PrivateEvidenceBundle(
        string root, PrivateEvidenceBundleIdentity identity)
    {
        _root = root;
        _objectsRoot = Path.Combine(root, ObjectsDirectoryName);
        _receiptsRoot = Path.Combine(root, ReceiptsDirectoryName);
        _identity = identity;
    }

    public static PrivateEvidenceBundle Create(
        string stagingRoot, PrivateEvidenceBundleIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var root = EvidenceFiles.RequireRoot(
            stagingRoot, "Private evidence staging root");
        if (Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException(
                "Private evidence staging root must be empty and caller-owned.");
        Directory.CreateDirectory(Path.Combine(root, ObjectsDirectoryName));
        Directory.CreateDirectory(Path.Combine(root, ReceiptsDirectoryName));
        return new PrivateEvidenceBundle(root, identity);
    }

    public async Task<StagedEvidenceRef> CaptureAsync(
        SourceRequestIdentity request,
        BoundedResponseMetadata response,
        Stream body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(body);
        if (!body.CanRead)
            throw new InvalidDataException("Response body stream must be readable.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireOpen();
            EvidenceFiles.RequireRoot(_root, "Private evidence staging root");
            EvidenceFiles.RequireEntry(
                _root, _objectsRoot, "Private evidence objects directory");
            EvidenceFiles.RequireEntry(
                _root, _receiptsRoot, "Private evidence receipts directory");
            if (!string.Equals(
                    request.Publisher, _identity.Publisher,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Request publisher does not match the evidence bundle.");
            if (_records.Any(record =>
                    record.Request.RequestId == request.RequestId
                    || record.Request.Ordinal == request.Ordinal))
                throw new InvalidDataException(
                    "Request identity or ordinal is duplicated.");

            var temp = Path.Combine(
                _objectsRoot, $".capture-{Guid.NewGuid():N}.tmp");
            try
            {
                var staged = await WriteBodyAsync(
                    request.RequestId, body, temp, cancellationToken)
                    .ConfigureAwait(false);
                var final = EvidenceFiles.ObjectPath(_root, staged.ObjectSha256);
                if (File.Exists(final))
                {
                    EvidenceFiles.VerifyObject(final, staged);
                    File.Delete(temp);
                }
                else
                {
                    File.Move(temp, final, overwrite: false);
                }
                var record = new StagedRecord(request, response, staged);
                var receiptBytes = EvidenceJson.WriteResponseReceipt(record);
                if (receiptBytes.Length > MaximumResponseReceiptBytes)
                    throw new InvalidDataException(
                        "Private evidence response receipt exceeds its size bound.");
                var receiptPath = EvidenceFiles.ResponseReceiptPath(
                    _root, request.RequestId);
                if (File.Exists(receiptPath))
                {
                    var existing = EvidenceFiles.ReadBounded(
                        _root,
                        receiptPath,
                        MaximumResponseReceiptBytes,
                        "Private evidence response receipt");
                    if (!existing.AsSpan().SequenceEqual(receiptBytes))
                        throw new InvalidDataException(
                            "Existing response receipt does not match the capture.");
                }
                else
                {
                    await WriteAtomicAsync(
                        receiptPath, receiptBytes, cancellationToken)
                        .ConfigureAwait(false);
                }
                _records.Add(record);
                return staged;
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Verifies a separate copy of one staged response. A sidecar can use this transition
    /// after its remote upload and readback; only the returned EvidenceRef may reach parsers.
    /// </summary>
    public EvidenceRef VerifyStagedReadback(
        string readbackRoot, StagedEvidenceRef staged)
    {
        ArgumentNullException.ThrowIfNull(staged);
        _gate.Wait();
        try
        {
            RequireOpen();
            var root = EvidenceFiles.RequireRoot(
                readbackRoot, "Private evidence readback root");
            if (EvidenceFiles.PathsOverlap(root, _root))
                throw new InvalidDataException(
                    "Evidence readback must be separate from staging.");
            var record = _records.SingleOrDefault(
                item => item.Evidence == staged);
            if (record is null)
                throw new InvalidDataException(
                    "Staged evidence does not belong to this bundle.");
            VerifyResponseReceipt(root, record);
            EvidenceFiles.VerifyObject(
                EvidenceFiles.ObjectPath(root, staged.ObjectSha256), staged);
            var verified = new EvidenceRef(
                staged.RequestId, staged.ObjectSha256, staged.ByteLength);
            _verified[verified.RequestId] = verified;
            return verified;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PrivateEvidenceBundleReceipt> SealAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireOpen();
            var ordered = OrderedRecords();
            if (ordered.Length == 0 || _verified.Count != ordered.Length
                || ordered.Any(record =>
                    !_verified.TryGetValue(
                        record.Request.RequestId, out var verified)
                    || !SameEvidence(record.Evidence, verified)))
                throw new InvalidDataException(
                    "Every staged response must pass separate readback before sealing.");

            VerifyLayout(
                _root, ordered, includeManifest: false, includeMarker: false);
            var bytes = EvidenceJson.Write(new ManifestDocument(
                ManifestSchema, _identity, ordered));
            if (bytes.Length > MaximumManifestBytes)
                throw new InvalidDataException(
                    "Private evidence manifest exceeds its size bound.");
            await WriteAtomicAsync(
                Path.Combine(_root, ManifestFileName), bytes, cancellationToken)
                .ConfigureAwait(false);
            VerifyLayout(
                _root, ordered, includeManifest: true, includeMarker: false);
            _sealed = true;
            return new PrivateEvidenceBundleReceipt(
                _root,
                _identity,
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                ordered.Select(record =>
                    _verified[record.Request.RequestId]).ToArray());
        }
        finally
        {
            _gate.Release();
        }
    }

    public static VerifiedPrivateEvidenceBundle VerifyReadback(
        string readbackRoot, PrivateEvidenceBundleReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var root = EvidenceFiles.RequireRoot(
            readbackRoot, "Private evidence readback root");
        if (EvidenceFiles.PathsOverlap(root, receipt.StagingRoot))
            throw new InvalidDataException(
                "Bundle readback must be separate from staging.");

        var manifestBytes = EvidenceFiles.ReadBounded(
            root,
            Path.Combine(root, ManifestFileName),
            MaximumManifestBytes,
            "Private evidence manifest");
        if (!string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(manifestBytes)),
                receipt.ManifestSha256,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "Manifest does not match the sealed receipt.");
        var parsed = EvidenceJson.ParseManifest(manifestBytes);
        if (parsed.Identity != receipt.Identity)
            throw new InvalidDataException(
                "Manifest run, code, publisher, or chain does not match the receipt.");

        var receiptByRequest = receipt.Evidence.ToDictionary(
            item => item.RequestId, StringComparer.Ordinal);
        if (parsed.Records.Count != receiptByRequest.Count
            || parsed.Records.Any(record =>
                !receiptByRequest.TryGetValue(
                    record.Request.RequestId, out var evidence)
                || !SameEvidence(record.Evidence, evidence)))
            throw new InvalidDataException(
                "Manifest evidence inventory does not match the sealed receipt.");

        VerifyLayout(
            root, parsed.Records, includeManifest: true, includeMarker: true);
        var marker = EvidenceFiles.ReadBounded(
            root,
            Path.Combine(root, CommitMarkerFileName),
            MaximumMarkerBytes,
            "Private evidence commit marker");
        if (!marker.AsSpan().SequenceEqual(receipt.CreateCommitMarkerBytes()))
            throw new InvalidDataException(
                "Commit marker does not exactly match the verified inventory.");

        foreach (var record in parsed.Records)
            VerifyResponseReceipt(root, record);
        foreach (var group in parsed.Records.GroupBy(
                     record => record.Evidence.ObjectSha256,
                     StringComparer.Ordinal))
        {
            var first = group.First().Evidence;
            if (group.Any(item => item.Evidence.ByteLength != first.ByteLength))
                throw new InvalidDataException(
                    "One object has conflicting declared lengths.");
            EvidenceFiles.VerifyObject(
                EvidenceFiles.ObjectPath(root, first.ObjectSha256), first);
        }

        var records = parsed.Records.Select(record => new PrivateEvidenceRecord(
            record.Request,
            record.Response,
            receiptByRequest[record.Request.RequestId])).ToArray();
        return new VerifiedPrivateEvidenceBundle(root, parsed.Identity, records);
    }

    private StagedRecord[] OrderedRecords()
    {
        var ordered = _records
            .OrderBy(record => record.Request.Ordinal)
            .ThenBy(record => record.Request.RequestId, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].Request.Ordinal != index)
                throw new InvalidDataException(
                    "Request ordinals must be contiguous from zero.");
        }
        return ordered;
    }

    private void RequireOpen()
    {
        if (_sealed)
            throw new InvalidOperationException("Evidence bundle is already sealed.");
    }

    private static async Task<StagedEvidenceRef> WriteBodyAsync(
        string requestId,
        Stream source,
        string tempPath,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        await using (var destination = new FileStream(tempPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = buffer.Length,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
        }))
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;
                if (length > MaximumBodyBytes - read)
                    throw new InvalidDataException(
                        $"Response body exceeds {MaximumBodyBytes} bytes.");
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(
                    buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                length += read;
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }
        return new StagedEvidenceRef(
            requestId,
            Convert.ToHexStringLower(hash.GetHashAndReset()),
            length);
    }

    private static async Task WriteAtomicAsync(
        string finalPath, byte[] bytes, CancellationToken cancellationToken)
    {
        var temp = Path.Combine(
            Path.GetDirectoryName(finalPath)!,
            $".{Path.GetFileName(finalPath)}-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            }))
            {
                await stream.WriteAsync(bytes, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, finalPath, overwrite: false);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void VerifyLayout(
        string root,
        IReadOnlyCollection<StagedRecord> records,
        bool includeManifest,
        bool includeMarker)
    {
        var expectedRoot = new HashSet<string>(StringComparer.Ordinal)
        {
            "D:" + ObjectsDirectoryName,
            "D:" + ReceiptsDirectoryName,
        };
        if (includeManifest) expectedRoot.Add("F:" + ManifestFileName);
        if (includeMarker) expectedRoot.Add("F:" + CommitMarkerFileName);
        var actualRoot = Directory.EnumerateFileSystemEntries(root)
            .Select(entry => EvidenceFiles.EntryKey(root, entry))
            .ToHashSet(StringComparer.Ordinal);
        if (!actualRoot.SetEquals(expectedRoot))
            throw new InvalidDataException(
                "Evidence bundle root does not have the exact required file set.");

        var objectsRoot = Path.Combine(root, ObjectsDirectoryName);
        var expectedObjects = records
            .Select(record => "F:" + record.Evidence.ObjectSha256 + ".bin")
            .ToHashSet(StringComparer.Ordinal);
        var actualObjects = Directory.EnumerateFileSystemEntries(objectsRoot)
            .Select(entry => EvidenceFiles.EntryKey(root, entry))
            .Select(key => key[..2] + Path.GetFileName(key[2..]))
            .ToHashSet(StringComparer.Ordinal);
        if (!actualObjects.SetEquals(expectedObjects))
            throw new InvalidDataException(
                "Evidence objects do not match the manifest exactly.");

        var receiptsRoot = Path.Combine(root, ReceiptsDirectoryName);
        var expectedReceipts = records
            .Select(record => "F:" + record.Request.RequestId + ".json")
            .ToHashSet(StringComparer.Ordinal);
        var actualReceipts = Directory.EnumerateFileSystemEntries(receiptsRoot)
            .Select(entry => EvidenceFiles.EntryKey(root, entry))
            .Select(key => key[..2] + Path.GetFileName(key[2..]))
            .ToHashSet(StringComparer.Ordinal);
        if (!actualReceipts.SetEquals(expectedReceipts))
            throw new InvalidDataException(
                "Evidence response receipts do not match the manifest exactly.");
    }

    private static bool SameEvidence(
        StagedEvidenceRef staged, EvidenceRef verified) =>
        staged.RequestId == verified.RequestId
        && staged.ObjectSha256 == verified.ObjectSha256
        && staged.ByteLength == verified.ByteLength;

    private static void VerifyResponseReceipt(
        string root, StagedRecord record)
    {
        var expected = EvidenceJson.WriteResponseReceipt(record);
        var actual = EvidenceFiles.ReadBounded(
            root,
            EvidenceFiles.ResponseReceiptPath(
                root, record.Request.RequestId),
            MaximumResponseReceiptBytes,
            "Private evidence response receipt");
        if (!actual.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException(
                "Response receipt does not exactly match its request and evidence.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record StagedRecord(
    SourceRequestIdentity Request,
    BoundedResponseMetadata Response,
    StagedEvidenceRef Evidence);

internal sealed record ManifestDocument(
    string Schema,
    PrivateEvidenceBundleIdentity Identity,
    IReadOnlyList<StagedRecord> Records);

internal sealed record CommitDocument(
    string Schema,
    string ManifestSha256,
    PrivateEvidenceBundleIdentity Identity,
    IReadOnlyList<EvidenceRef> Evidence);

internal sealed record ResponseReceiptDocument(
    string Schema,
    StagedRecord Record);

internal sealed record ParsedManifest(
    PrivateEvidenceBundleIdentity Identity,
    IReadOnlyList<StagedRecord> Records);

internal static class EvidenceJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Write<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        return [.. json, (byte)'\n'];
    }

    public static byte[] WriteResponseReceipt(StagedRecord record) =>
        Write(new ResponseReceiptDocument(
            PrivateEvidenceBundle.ResponseReceiptSchema, record));

    public static ParsedManifest ParseManifest(byte[] bytes)
    {
        ManifestDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ManifestDocument>(bytes, Options)
                ?? throw new InvalidDataException("Evidence manifest is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Evidence manifest is not strict JSON.", error);
        }
        if (document.Identity is null || document.Records is null
            || document.Records.Any(record => record is null
                                              || record.Request is null
                                              || record.Response is null
                                              || record.Evidence is null))
            throw new InvalidDataException("Evidence manifest is incomplete.");
        if (!string.Equals(
                document.Schema,
                PrivateEvidenceBundle.ManifestSchema,
                StringComparison.Ordinal))
            throw new InvalidDataException("Evidence manifest schema is unsupported.");

        var ordered = document.Records
            .OrderBy(record => record.Request.Ordinal)
            .ThenBy(record => record.Request.RequestId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
            throw new InvalidDataException("Evidence manifest has no records.");
        var requests = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Length; index++)
        {
            var record = ordered[index];
            if (record.Request.Ordinal != index
                || !requests.Add(record.Request.RequestId)
                || record.Request.Publisher != document.Identity.Publisher
                || record.Evidence.RequestId != record.Request.RequestId)
                throw new InvalidDataException(
                    "Evidence manifest request identity is inconsistent.");
        }

        var canonical = Write(new ManifestDocument(
            document.Schema, document.Identity, ordered));
        if (!bytes.AsSpan().SequenceEqual(canonical))
            throw new InvalidDataException(
                "Evidence manifest is not in canonical form.");
        return new ParsedManifest(document.Identity, ordered);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false,
            MaxDepth = 16,
        };
        options.Converters.Add(new JsonStringEnumConverter<SourceRequestMethod>(
            JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
        return options;
    }
}

internal static class EvidenceFiles
{
    public static string RequireRoot(string path, string description)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException($"{description} must be absolute.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(full))
            throw new InvalidDataException($"{description} does not exist.");
        ProtectedPath.RequireExisting(full, full, description);
        return full;
    }

    public static string RequireEntry(
        string root, string path, string description) =>
        ProtectedPath.RequireExisting(root, path, description);

    public static string ObjectPath(string root, string sha256) => Path.Combine(
        root,
        PrivateEvidenceBundle.ObjectsDirectoryName,
        CodeIdentity.RequireSha256(
            sha256, "Private evidence object SHA-256") + ".bin");

    public static string ResponseReceiptPath(string root, string requestId) =>
        Path.Combine(
            root,
            PrivateEvidenceBundle.ReceiptsDirectoryName,
            CodeIdentity.RequireSha256(
                requestId, "Private evidence request ID") + ".json");

    public static string EntryKey(string root, string path)
    {
        var verified = RequireEntry(root, path, "Private evidence entry");
        return Directory.Exists(verified)
            ? "D:" + Path.GetRelativePath(root, verified)
            : File.Exists(verified)
                ? "F:" + Path.GetRelativePath(root, verified)
                : throw new InvalidDataException(
                    "Evidence bundle contains an unsupported entry.");
    }

    public static void VerifyObject(string path, StagedEvidenceRef expected)
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(path)!)!;
        using var stream = OpenVerified(
            root, path, expected.ObjectSha256, expected.ByteLength);
    }

    public static Stream OpenVerifiedObject(string root, EvidenceRef expected)
    {
        RequireRoot(root, "Private evidence readback root");
        return OpenVerified(
            root,
            ObjectPath(root, expected.ObjectSha256),
            expected.ObjectSha256,
            expected.ByteLength);
    }

    public static byte[] ReadBounded(
        string root, string path, int maximumBytes, string description)
    {
        var verified = RequireEntry(root, path, description);
        if (!File.Exists(verified) || Directory.Exists(verified))
            throw new InvalidDataException($"{description} is not a regular file.");
        var length = new FileInfo(verified).Length;
        if (length is < 1 || length > maximumBytes)
            throw new InvalidDataException(
                $"{description} length is outside its allowed bound.");
        var bytes = File.ReadAllBytes(verified);
        if (bytes.LongLength != length)
            throw new InvalidDataException($"{description} changed while read.");
        return bytes;
    }

    public static bool PathsOverlap(string left, string right)
    {
        var first = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        var second = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(first, second, comparison)
               || IsWithin(first, second, comparison)
               || IsWithin(second, first, comparison);
    }

    private static bool IsWithin(
        string candidate, string parent, StringComparison comparison)
    {
        var prefix = Path.EndsInDirectorySeparator(parent)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }

    private static FileStream OpenRead(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.Read,
        Options = FileOptions.SequentialScan,
    });

    private static FileStream OpenVerified(
        string root, string path, string sha256, long byteLength)
    {
        var verified = RequireEntry(root, path, "Private evidence object");
        if (!File.Exists(verified) || Directory.Exists(verified))
            throw new InvalidDataException(
                "Private evidence object is not a regular file.");
        var stream = OpenRead(verified);
        try
        {
            if (stream.Length != byteLength
                || Convert.ToHexStringLower(SHA256.HashData(stream)) != sha256)
                throw new InvalidDataException(
                    "Evidence object length or SHA-256 does not match its receipt.");
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
