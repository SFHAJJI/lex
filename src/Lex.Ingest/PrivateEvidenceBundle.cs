using System.Security.Cryptography;
using System.Text;
using Lex.Law;
using Lex.Temporal;

namespace Lex.Ingest;

/// <summary>
/// The exact request inventory and identities for one private capture bundle.
/// Changing the corpus baseline, scope, endpoint policy, or any request changes
/// the bundle identity.
/// </summary>
public sealed record PrivateEvidenceAcquisitionPlan
{
    public PrivateEvidenceAcquisitionPlan(
        string runIdentity,
        string codeCommit,
        string publisher,
        string baselineCorpusSha256,
        string enumerationScopeSha256,
        string endpointPolicySha256,
        IReadOnlyCollection<SourceRequestIdentity> requests)
    {
        RunIdentity = IngestRunIdentity.Require(
            runIdentity, "Private evidence run identity");
        CodeCommit = CodeIdentity.RequireFullCommit(
            codeCommit, nameof(codeCommit));
        Publisher = RequirePublisher(publisher);
        BaselineCorpusSha256 = CodeIdentity.RequireSha256(
            baselineCorpusSha256, nameof(baselineCorpusSha256));
        EnumerationScopeSha256 = CodeIdentity.RequireSha256(
            enumerationScopeSha256, nameof(enumerationScopeSha256));
        EndpointPolicySha256 = CodeIdentity.RequireSha256(
            endpointPolicySha256, nameof(endpointPolicySha256));
        ArgumentNullException.ThrowIfNull(requests);
        var ordered = requests
            .OrderBy(request => request.Ordinal)
            .ThenBy(request => request.RequestId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
            throw new InvalidDataException(
                "Private evidence acquisition plan must contain a request.");
        var requestIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Length; index++)
        {
            var request = ordered[index]
                ?? throw new InvalidDataException(
                    "Private evidence acquisition plan contains a null request.");
            if (request.Ordinal != index
                || request.Publisher != Publisher
                || !requestIds.Add(request.RequestId))
                throw new InvalidDataException(
                    "Private evidence requests must be unique, contiguous, and publisher-bound.");
        }
        Requests = Array.AsReadOnly(ordered);
        AcquisitionPlanSha256 = EvidenceJson.HashAcquisitionPlan(ordered);
        BundleId = HashBundleIdentity();
    }

    public string RunIdentity { get; }
    public string CodeCommit { get; }
    public string Publisher { get; }
    public string BaselineCorpusSha256 { get; }
    public string EnumerationScopeSha256 { get; }
    public string EndpointPolicySha256 { get; }
    public string AcquisitionPlanSha256 { get; }
    public string BundleId { get; }
    public IReadOnlyList<SourceRequestIdentity> Requests { get; }

    private string HashBundleIdentity()
    {
        var canonical = string.Join('\n',
            "lex-private-evidence-bundle-id/2",
            RunIdentity,
            CodeCommit,
            Publisher,
            BaselineCorpusSha256,
            EnumerationScopeSha256,
            EndpointPolicySha256,
            AcquisitionPlanSha256);
        return EvidenceJson.Sha256(Encoding.UTF8.GetBytes(canonical));
    }

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

public enum StagedResponseDisposition
{
    Complete = 1,
    Rejected = 2,
}

public enum StagedResponseRejectionReason
{
    BodyTooLarge = 1,
    ResponseIncomplete = 2,
    TransportInterrupted = 3,
}

/// <summary>
/// Local-only response evidence. This hierarchy is intentionally separate from
/// EvidenceRef, so no staged or rejected response can enter a publisher parser.
/// </summary>
public abstract class StagedResponseEvidence
{
    internal StagedResponseEvidence(
        string requestId,
        string objectSha256,
        long byteLength,
        bool bodyComplete)
    {
        RequestId = CodeIdentity.RequireSha256(requestId, nameof(requestId));
        ObjectSha256 = CodeIdentity.RequireSha256(
            objectSha256, nameof(objectSha256));
        if (byteLength is < 0 or > EvidenceRef.MaximumByteLength + 1)
            throw new InvalidDataException(
                "Staged response byte length is outside its allowed bound.");
        ByteLength = byteLength;
        BodyComplete = bodyComplete;
    }

    public string RequestId { get; }
    public string ObjectSha256 { get; }
    public long ByteLength { get; }
    public bool BodyComplete { get; }
    public abstract StagedResponseDisposition Disposition { get; }
}

public sealed class CompleteStagedResponseEvidence : StagedResponseEvidence
{
    internal CompleteStagedResponseEvidence(
        string requestId, string objectSha256, long byteLength)
        : base(requestId, objectSha256, byteLength, bodyComplete: true)
    {
        if (byteLength > EvidenceRef.MaximumByteLength)
            throw new InvalidDataException(
                "Complete staged response exceeds the durable evidence bound.");
    }

    public override StagedResponseDisposition Disposition =>
        StagedResponseDisposition.Complete;
}

public sealed class RejectedStagedResponseEvidence : StagedResponseEvidence
{
    internal RejectedStagedResponseEvidence(
        string requestId,
        string objectSha256,
        long byteLength,
        StagedResponseRejectionReason reason)
        : base(requestId, objectSha256, byteLength, bodyComplete: false)
    {
        if (!Enum.IsDefined(reason))
            throw new InvalidDataException(
                "Staged response rejection reason is unsupported.");
        Reason = reason;
    }

    public StagedResponseRejectionReason Reason { get; }
    public override StagedResponseDisposition Disposition =>
        StagedResponseDisposition.Rejected;
}

public sealed class StagedResponseRecord
{
    internal StagedResponseRecord(
        SourceRequestIdentity request,
        BoundedResponseMetadata response,
        StagedResponseEvidence evidence)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Response = response ?? throw new ArgumentNullException(nameof(response));
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        if (request.RequestId != evidence.RequestId
            || response.BodyComplete != evidence.BodyComplete)
            throw new InvalidDataException(
                "Staged response identity or completion state is inconsistent.");
    }

    public SourceRequestIdentity Request { get; }
    public BoundedResponseMetadata Response { get; }
    public StagedResponseEvidence Evidence { get; }
}

/// <summary>A receipt for a sealed local stage, never a remote durability claim.</summary>
public sealed class PrivateEvidenceStageReceipt
{
    internal PrivateEvidenceStageReceipt(
        string bundleId,
        string manifestSha256,
        IReadOnlyCollection<StagedResponseRecord> records)
    {
        BundleId = CodeIdentity.RequireSha256(bundleId, nameof(bundleId));
        ManifestSha256 = CodeIdentity.RequireSha256(
            manifestSha256, nameof(manifestSha256));
        ArgumentNullException.ThrowIfNull(records);
        Records = Array.AsReadOnly(records
            .OrderBy(record => record.Request.Ordinal)
            .ToArray());
    }

    public string BundleId { get; }
    public string ManifestSha256 { get; }
    public IReadOnlyList<StagedResponseRecord> Records { get; }
}

/// <summary>
/// Crash-recoverable local staging for physical response bytes. The root must be
/// owned exclusively by the evidence sidecar identity. The held owner lock protects
/// cooperating processes; this type does not claim safety against a hostile process
/// running as the same operating-system identity.
/// </summary>
public sealed class PrivateEvidenceBundle : IDisposable
{
    public const string PlanSchema = "lex-private-evidence-plan/2";
    public const string AcquisitionPlanSchema =
        "lex-private-evidence-acquisition-plan/2";
    public const string ResponseReceiptSchema =
        "lex-private-evidence-response/2";
    public const string ManifestSchema = "lex-private-evidence-bundle/2";
    public const string CommitMarkerSchema =
        "lex-private-evidence-stage-commit/2";
    public const string PlanFileName = "plan.json";
    public const string ManifestFileName = "manifest.json";
    public const string CommitMarkerFileName = "commit.json";
    public const string OwnerLockFileName = "owner.lock";
    public const string ObjectsDirectoryName = "objects";
    public const string ReceiptsDirectoryName = "receipts";
    private const int MaximumPlanBytes = 16 * 1024 * 1024;
    private const int MaximumManifestBytes = 16 * 1024 * 1024;
    private const int MaximumResponseReceiptBytes = 64 * 1024;
    private const int MaximumMarkerBytes = 64 * 1024;

    private readonly string _root;
    private readonly string _objectsRoot;
    private readonly string _receiptsRoot;
    private readonly PrivateEvidenceAcquisitionPlan _plan;
    private readonly Dictionary<string, StagedResponseRecord> _records;
    private readonly FileStream _ownerLock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _sealed;
    private bool _disposed;

    private PrivateEvidenceBundle(
        string root,
        PrivateEvidenceAcquisitionPlan plan,
        FileStream ownerLock,
        IReadOnlyCollection<StagedResponseRecord>? records = null,
        bool isSealed = false)
    {
        _root = root;
        _objectsRoot = Path.Combine(root, ObjectsDirectoryName);
        _receiptsRoot = Path.Combine(root, ReceiptsDirectoryName);
        _plan = plan;
        _ownerLock = ownerLock;
        _records = (records ?? [])
            .ToDictionary(record => record.Request.RequestId, StringComparer.Ordinal);
        _sealed = isSealed;
    }

    public PrivateEvidenceAcquisitionPlan Plan => _plan;
    public bool IsSealed => _sealed;
    public IReadOnlyList<StagedResponseRecord> Records => OrderedRecords();

    public static PrivateEvidenceBundle Create(
        string stagingRoot, PrivateEvidenceAcquisitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var root = EvidenceFiles.RequireRoot(
            stagingRoot, "Private evidence staging root");
        if (Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException(
                "Private evidence staging root must be empty and sidecar-owned.");

        FileStream? ownerLock = null;
        try
        {
            ownerLock = EvidenceFiles.CreateOwnerLock(root);
            EvidenceFiles.WriteAtomic(
                Path.Combine(root, PlanFileName), EvidenceJson.WritePlan(plan));
            Directory.CreateDirectory(Path.Combine(root, ObjectsDirectoryName));
            Directory.CreateDirectory(Path.Combine(root, ReceiptsDirectoryName));
            var bundle = new PrivateEvidenceBundle(root, plan, ownerLock);
            bundle.VerifyStage(includeManifest: false, includeCommit: false);
            return bundle;
        }
        catch
        {
            ownerLock?.Dispose();
            throw;
        }
    }

    public static PrivateEvidenceBundle Open(
        string stagingRoot, PrivateEvidenceAcquisitionPlan expectedPlan)
    {
        ArgumentNullException.ThrowIfNull(expectedPlan);
        var root = EvidenceFiles.RequireRoot(
            stagingRoot, "Private evidence staging root");
        FileStream? ownerLock = null;
        try
        {
            ownerLock = EvidenceFiles.OpenOwnerLock(root);
            RecoverInitialCreate(root, expectedPlan);
            EvidenceFiles.CleanupTemporaryFiles(root);
            var planBytes = EvidenceFiles.ReadBounded(
                root,
                Path.Combine(root, PlanFileName),
                MaximumPlanBytes,
                "Private evidence plan");
            var parsedPlan = EvidenceJson.ParsePlan(planBytes);
            if (parsedPlan.BundleId != expectedPlan.BundleId
                || !planBytes.AsSpan().SequenceEqual(
                    EvidenceJson.WritePlan(expectedPlan)))
                throw new InvalidDataException(
                    "Private evidence plan does not match the trusted plan.");

            RecoverInitialDirectories(root);
            var records = LoadReceipts(root, parsedPlan);
            EvidenceFiles.DeleteOrphanObjects(root, records);
            var hasManifest = File.Exists(Path.Combine(root, ManifestFileName));
            var hasCommit = File.Exists(Path.Combine(root, CommitMarkerFileName));
            if (!hasManifest && hasCommit)
                throw new InvalidDataException(
                    "Private evidence commit marker exists without its manifest.");

            var bundle = new PrivateEvidenceBundle(
                root, parsedPlan, ownerLock, records, isSealed: false);
            if (hasManifest)
            {
                bundle.VerifyStage(
                    includeManifest: true, includeCommit: hasCommit);
                if (!hasCommit)
                {
                    bundle.WriteCommitMarker();
                    bundle.VerifyStage(
                        includeManifest: true, includeCommit: true);
                }
                bundle._sealed = true;
            }
            else
            {
                bundle.VerifyStage(includeManifest: false, includeCommit: false);
            }
            return bundle;
        }
        catch
        {
            ownerLock?.Dispose();
            throw;
        }
    }

    public async Task<StagedResponseEvidence> CaptureAsync(
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
            var planned = _plan.Requests.ElementAtOrDefault(request.Ordinal);
            if (planned is null || planned != request)
                throw new InvalidDataException(
                    "Response request is not the exact planned request.");
            if (_records.TryGetValue(request.RequestId, out var recovered))
                return recovered.Evidence;

            var temp = Path.Combine(
                _objectsRoot, $".capture-{Guid.NewGuid():N}.tmp");
            try
            {
                var write = await WriteBodyAsync(
                    request, response.BodyComplete, body, temp, cancellationToken)
                    .ConfigureAwait(false);
                var objectPath = EvidenceFiles.ObjectPath(
                    _root, write.ObjectSha256);
                if (File.Exists(objectPath))
                {
                    EvidenceFiles.VerifyObject(
                        _root, objectPath, write.ObjectSha256, write.ByteLength);
                    File.Delete(temp);
                }
                else
                {
                    File.Move(temp, objectPath, overwrite: false);
                }

                StagedResponseEvidence evidence = write.RejectionReason is null
                    ? new CompleteStagedResponseEvidence(
                        request.RequestId, write.ObjectSha256, write.ByteLength)
                    : new RejectedStagedResponseEvidence(
                        request.RequestId,
                        write.ObjectSha256,
                        write.ByteLength,
                        write.RejectionReason.Value);
                var retainedResponse = evidence.BodyComplete
                    ? response
                    : response.MarkBodyIncomplete();
                var record = new StagedResponseRecord(
                    request, retainedResponse, evidence);
                EvidenceFiles.WriteAtomic(
                    EvidenceFiles.ResponseReceiptPath(_root, request.RequestId),
                    EvidenceJson.WriteReceipt(record));
                _records.Add(request.RequestId, record);
                return evidence;
            }
            catch
            {
                EvidenceFiles.TryDelete(temp);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PrivateEvidenceStageReceipt> SealAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireOpen();
            var ordered = OrderedRecords();
            if (ordered.Length != _plan.Requests.Count
                || ordered.Where((record, index) =>
                    record.Request != _plan.Requests[index]).Any())
                throw new InvalidDataException(
                    "Sealed response inventory must exactly equal the acquisition plan.");

            VerifyStage(includeManifest: false, includeCommit: false);
            var manifestBytes = EvidenceJson.WriteManifest(_plan, ordered);
            EvidenceFiles.WriteAtomic(
                Path.Combine(_root, ManifestFileName), manifestBytes);
            VerifyStage(includeManifest: true, includeCommit: false);
            WriteCommitMarker();
            VerifyStage(includeManifest: true, includeCommit: true);
            _sealed = true;
            return new PrivateEvidenceStageReceipt(
                _plan.BundleId,
                EvidenceJson.Sha256(manifestBytes),
                ordered);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownerLock.Dispose();
        _gate.Dispose();
    }

    private static void RecoverInitialCreate(
        string root, PrivateEvidenceAcquisitionPlan expectedPlan)
    {
        var planPath = Path.Combine(root, PlanFileName);
        if (File.Exists(planPath)) return;
        var entries = Directory.EnumerateFileSystemEntries(root)
            .Select(Path.GetFileName)
            .ToArray();
        if (entries.Any(name => name != OwnerLockFileName
                                && name is not null
                                && !name.StartsWith($".{PlanFileName}-",
                                    StringComparison.Ordinal)))
            throw new InvalidDataException(
                "Incomplete private evidence creation cannot be recovered safely.");
        EvidenceFiles.CleanupTemporaryFiles(root);
        Directory.CreateDirectory(Path.Combine(root, ObjectsDirectoryName));
        Directory.CreateDirectory(Path.Combine(root, ReceiptsDirectoryName));
        EvidenceFiles.WriteAtomic(planPath, EvidenceJson.WritePlan(expectedPlan));
    }

    private static void RecoverInitialDirectories(string root)
    {
        var objects = Path.Combine(root, ObjectsDirectoryName);
        var receipts = Path.Combine(root, ReceiptsDirectoryName);
        if (Directory.Exists(objects) && Directory.Exists(receipts)) return;
        if (File.Exists(Path.Combine(root, ManifestFileName))
            || File.Exists(Path.Combine(root, CommitMarkerFileName)))
            throw new InvalidDataException(
                "A sealed private evidence bundle is missing a required directory.");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            OwnerLockFileName,
            PlanFileName,
            ObjectsDirectoryName,
            ReceiptsDirectoryName,
        };
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (!allowed.Contains(Path.GetFileName(entry)))
                throw new InvalidDataException(
                    "Incomplete private evidence creation has an unexpected entry.");
        }
        foreach (var directory in new[] { objects, receipts })
        {
            if (File.Exists(directory)
                || Directory.Exists(directory)
                && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new InvalidDataException(
                    "Incomplete private evidence creation cannot replace captured state.");
            Directory.CreateDirectory(directory);
        }
    }

    private static IReadOnlyList<StagedResponseRecord> LoadReceipts(
        string root, PrivateEvidenceAcquisitionPlan plan)
    {
        var receiptsRoot = Path.Combine(root, ReceiptsDirectoryName);
        EvidenceFiles.RequireDirectory(root, receiptsRoot,
            "Private evidence receipts directory");
        var planned = plan.Requests.ToDictionary(
            request => request.RequestId, StringComparer.Ordinal);
        var records = new List<StagedResponseRecord>();
        foreach (var path in Directory.EnumerateFileSystemEntries(receiptsRoot))
        {
            var verified = EvidenceFiles.RequireEntry(
                root, path, "Private evidence response receipt");
            if (!File.Exists(verified) || Directory.Exists(verified))
                throw new InvalidDataException(
                    "Private evidence receipts directory contains a non-file entry.");
            var name = Path.GetFileName(verified);
            if (name.Length != 69 || !name.EndsWith(".json", StringComparison.Ordinal)
                || !EvidenceFiles.IsSha256(name[..64]))
                throw new InvalidDataException(
                    "Private evidence response receipt has an invalid name.");
            var bytes = EvidenceFiles.ReadBounded(
                root,
                verified,
                MaximumResponseReceiptBytes,
                "Private evidence response receipt");
            var record = EvidenceJson.ParseReceipt(bytes);
            if (name[..64] != record.Request.RequestId
                || !planned.TryGetValue(
                    record.Request.RequestId, out var plannedRequest)
                || plannedRequest != record.Request
                || records.Any(item =>
                    item.Request.RequestId == record.Request.RequestId))
                throw new InvalidDataException(
                    "Private evidence receipt is not an exact unique plan member.");
            EvidenceFiles.VerifyObject(
                root,
                EvidenceFiles.ObjectPath(root, record.Evidence.ObjectSha256),
                record.Evidence.ObjectSha256,
                record.Evidence.ByteLength);
            records.Add(record);
        }
        return records;
    }

    private void VerifySealedManifest(bool includeCommit)
    {
        var ordered = OrderedRecords();
        if (ordered.Length != _plan.Requests.Count)
            throw new InvalidDataException(
                "Private evidence manifest cannot seal an incomplete plan.");
        var manifestBytes = EvidenceFiles.ReadBounded(
            _root,
            Path.Combine(_root, ManifestFileName),
            MaximumManifestBytes,
            "Private evidence manifest");
        var parsed = EvidenceJson.ParseManifest(manifestBytes);
        if (parsed.BundleId != _plan.BundleId
            || parsed.AcquisitionPlanSha256 != _plan.AcquisitionPlanSha256
            || !EvidenceJson.SameRecords(parsed.Records, ordered))
            throw new InvalidDataException(
                "Private evidence manifest does not match its plan and receipts.");
        if (!includeCommit) return;
        var commitBytes = EvidenceFiles.ReadBounded(
            _root,
            Path.Combine(_root, CommitMarkerFileName),
            MaximumMarkerBytes,
            "Private evidence commit marker");
        var expected = EvidenceJson.WriteCommit(
            _plan, manifestBytes, ordered);
        if (!commitBytes.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException(
                "Private evidence commit marker does not match the final rehash.");
    }

    private void WriteCommitMarker()
    {
        var ordered = OrderedRecords();
        var manifestBytes = EvidenceFiles.ReadBounded(
            _root,
            Path.Combine(_root, ManifestFileName),
            MaximumManifestBytes,
            "Private evidence manifest");
        var parsed = EvidenceJson.ParseManifest(manifestBytes);
        if (parsed.BundleId != _plan.BundleId
            || !EvidenceJson.SameRecords(parsed.Records, ordered))
            throw new InvalidDataException(
                "Private evidence manifest changed before commit.");
        EvidenceFiles.WriteAtomic(
            Path.Combine(_root, CommitMarkerFileName),
            EvidenceJson.WriteCommit(_plan, manifestBytes, ordered));
    }

    private void VerifyStage(bool includeManifest, bool includeCommit)
    {
        var planBytes = EvidenceFiles.ReadBounded(
            _root,
            Path.Combine(_root, PlanFileName),
            MaximumPlanBytes,
            "Private evidence plan");
        if (!planBytes.AsSpan().SequenceEqual(EvidenceJson.WritePlan(_plan)))
            throw new InvalidDataException("Private evidence plan changed.");

        var ordered = OrderedRecords();
        EvidenceFiles.VerifyExactLayout(
            _root, ordered, includeManifest, includeCommit);
        foreach (var record in ordered)
        {
            var receiptBytes = EvidenceFiles.ReadBounded(
                _root,
                EvidenceFiles.ResponseReceiptPath(
                    _root, record.Request.RequestId),
                MaximumResponseReceiptBytes,
                "Private evidence response receipt");
            if (!receiptBytes.AsSpan().SequenceEqual(
                    EvidenceJson.WriteReceipt(record)))
                throw new InvalidDataException(
                    "Private evidence response receipt changed.");
        }
        foreach (var group in ordered.GroupBy(
                     record => record.Evidence.ObjectSha256,
                     StringComparer.Ordinal))
        {
            var first = group.First().Evidence;
            if (group.Any(record =>
                    record.Evidence.ByteLength != first.ByteLength))
                throw new InvalidDataException(
                    "One staged object has conflicting lengths.");
            EvidenceFiles.VerifyObject(
                _root,
                EvidenceFiles.ObjectPath(_root, first.ObjectSha256),
                first.ObjectSha256,
                first.ByteLength);
        }
        if (includeManifest) VerifySealedManifest(includeCommit);
    }

    private StagedResponseRecord[] OrderedRecords() => _records.Values
        .OrderBy(record => record.Request.Ordinal)
        .ThenBy(record => record.Request.RequestId, StringComparer.Ordinal)
        .ToArray();

    private void RequireOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed)
            throw new InvalidOperationException(
                "Private evidence bundle is already sealed.");
    }

    private static async Task<CaptureWriteResult> WriteBodyAsync(
        SourceRequestIdentity request,
        bool declaredBodyComplete,
        Stream source,
        string tempPath,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        var reachedEnd = false;
        var interrupted = false;
        var retainedLimit = checked(request.MaximumResponseBytes + 1);
        await using (var destination = new FileStream(tempPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = buffer.Length,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
        }))
        {
            while (length < retainedLimit)
            {
                var requested = (int)Math.Min(buffer.Length, retainedLimit - length);
                int read;
                try
                {
                    read = await source.ReadAsync(
                            buffer.AsMemory(0, requested), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (IOException)
                {
                    interrupted = true;
                    break;
                }
                if (read == 0)
                {
                    reachedEnd = true;
                    break;
                }
                if (read < 0 || read > requested)
                    throw new InvalidDataException(
                        "Response body stream returned an invalid byte count.");
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(
                        buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                length += read;
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }

        StagedResponseRejectionReason? rejection =
            length == retainedLimit
                ? StagedResponseRejectionReason.BodyTooLarge
                : interrupted
                    ? StagedResponseRejectionReason.TransportInterrupted
                    : !reachedEnd || !declaredBodyComplete
                        ? StagedResponseRejectionReason.ResponseIncomplete
                        : null;
        return new CaptureWriteResult(
            Convert.ToHexStringLower(hash.GetHashAndReset()), length, rejection);
    }
}

internal sealed record CaptureWriteResult(
    string ObjectSha256,
    long ByteLength,
    StagedResponseRejectionReason? RejectionReason);
