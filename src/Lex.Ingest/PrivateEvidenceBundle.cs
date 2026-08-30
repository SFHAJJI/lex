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
/// Crash-recoverable local staging for physical response bytes. The root is bound to
/// one non-link directory handle, and data reads and mutations are handle-relative.
/// Name enumeration revalidates that file identity. The owner lock coordinates peers;
/// hostile code already running as the same operating-system identity is out of scope.
/// </summary>
public sealed class PrivateEvidenceBundle : IDisposable
{
    public const string PlanSchema = "lex-private-evidence-plan/2";
    public const string AcquisitionPlanSchema =
        "lex-private-evidence-acquisition-plan/2";
    public const string ResponseReceiptSchema =
        "lex-private-evidence-response/2";
    public const string CaptureIntentSchema =
        "lex-private-evidence-capture-intent/1";
    public const string CaptureOutcomeSchema =
        "lex-private-evidence-capture-outcome/1";
    public const string ManifestSchema = "lex-private-evidence-bundle/2";
    public const string CommitMarkerSchema =
        "lex-private-evidence-stage-commit/2";
    public const string PlanFileName = "plan.json";
    public const string ManifestFileName = "manifest.json";
    public const string CommitMarkerFileName = "commit.json";
    public const string OwnerLockFileName = "owner.lock";
    public const string ObjectsDirectoryName = "objects";
    public const string PendingDirectoryName = "pending";
    public const string ReceiptsDirectoryName = "receipts";
    private const int MaximumPlanBytes = 16 * 1024 * 1024;
    private const int MaximumManifestBytes = 16 * 1024 * 1024;
    private const int MaximumResponseReceiptBytes = 64 * 1024;
    private const int MaximumCaptureJournalBytes = 64 * 1024;
    private const int MaximumMarkerBytes = 64 * 1024;

    private readonly string _root;
    private readonly PrivateEvidenceAcquisitionPlan _plan;
    private readonly Dictionary<string, StagedResponseRecord> _records;
    private readonly FileStream _ownerLock;
    private readonly HandleBoundRoot _rootHandle;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _sealed;
    private bool _disposed;

    private PrivateEvidenceBundle(
        string root,
        PrivateEvidenceAcquisitionPlan plan,
        FileStream ownerLock,
        HandleBoundRoot rootHandle,
        IReadOnlyCollection<StagedResponseRecord>? records = null,
        bool isSealed = false)
    {
        _root = root;
        _plan = plan;
        _ownerLock = ownerLock;
        _rootHandle = rootHandle;
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

        FileStream? ownerLock = null;
        HandleBoundRoot? rootHandle = null;
        try
        {
            rootHandle = HandleBoundRename.OpenRoot(root);
            EvidenceFiles.RequireRootIdentity(
                root, rootHandle, "Private evidence staging root");
            if (Directory.EnumerateFileSystemEntries(root).Any())
                throw new InvalidDataException(
                    "Private evidence staging root must be empty and sidecar-owned.");
            ownerLock = EvidenceFiles.CreateOwnerLock(root);
            EvidenceFiles.RequireRootIdentity(
                root, rootHandle, "Private evidence staging root");
            rootHandle.FlushDirectory(".");
            EvidenceFiles.WriteAtomic(
                rootHandle, PlanFileName, EvidenceJson.WritePlan(plan));
            rootHandle.EnsureDirectory(ObjectsDirectoryName);
            rootHandle.EnsureDirectory(PendingDirectoryName);
            rootHandle.EnsureDirectory(ReceiptsDirectoryName);
            var bundle = new PrivateEvidenceBundle(
                root, plan, ownerLock, rootHandle);
            bundle.VerifyStage(includeManifest: false, includeCommit: false);
            return bundle;
        }
        catch
        {
            rootHandle?.Dispose();
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
        HandleBoundRoot? rootHandle = null;
        try
        {
            rootHandle = HandleBoundRename.OpenRoot(root);
            ownerLock = EvidenceFiles.OpenOwnerLock(root);
            EvidenceFiles.RequireRootIdentity(
                root, rootHandle, "Private evidence staging root");
            RecoverInitialCreate(root, rootHandle, expectedPlan);
            var hasManifest = rootHandle.EntryExists(ManifestFileName);
            var hasCommit = rootHandle.EntryExists(CommitMarkerFileName);
            if (!hasManifest && hasCommit)
                throw new InvalidDataException(
                    "Private evidence commit marker exists without its manifest.");
            if (!hasCommit)
                EvidenceFiles.CleanupTemporaryFiles(root, rootHandle);
            var planBytes = EvidenceFiles.ReadBounded(
                rootHandle,
                PlanFileName,
                MaximumPlanBytes,
                "Private evidence plan");
            var parsedPlan = EvidenceJson.ParsePlan(planBytes);
            if (parsedPlan.BundleId != expectedPlan.BundleId
                || !planBytes.AsSpan().SequenceEqual(
                    EvidenceJson.WritePlan(expectedPlan)))
                throw new InvalidDataException(
                    "Private evidence plan does not match the trusted plan.");

            if (!hasCommit)
                RecoverInitialDirectories(root, rootHandle);
            if (!hasManifest)
                RecoverPendingCaptures(root, rootHandle, parsedPlan);
            var records = LoadReceipts(root, rootHandle, parsedPlan);
            if (!hasManifest)
                EvidenceFiles.DeleteOrphanObjects(root, rootHandle, records);

            var bundle = new PrivateEvidenceBundle(
                root,
                parsedPlan,
                ownerLock,
                rootHandle,
                records,
                isSealed: false);
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
            rootHandle?.Dispose();
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
            RefreshRecoveredCaptures();
            if (_records.TryGetValue(request.RequestId, out var recovered))
                return recovered.Evidence;

            EvidenceFiles.WriteAtomic(
                _rootHandle,
                EvidenceFiles.CaptureIntentRelative(request.RequestId),
                EvidenceJson.WriteCaptureIntent(request, response));
            var bodyRelative = EvidenceFiles.CaptureBodyRelative(request.RequestId);
            CaptureWriteResult write;
            await using (var destination = _rootHandle.CreateNewFile(bodyRelative))
            {
                destination.Flush(flushToDisk: true);
                _rootHandle.FlushDirectory(PendingDirectoryName);
                write = await WriteBodyAsync(
                    request,
                    response.BodyComplete,
                    body,
                    destination,
                    cancellationToken)
                    .ConfigureAwait(false);
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
                _rootHandle,
                EvidenceFiles.CaptureOutcomeRelative(request.RequestId),
                EvidenceJson.WriteCaptureOutcome(evidence));
            PublishPendingCapture(_rootHandle, record);
            _records.Add(request.RequestId, record);
            return evidence;
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
            RefreshRecoveredCaptures();
            var ordered = OrderedRecords();
            if (ordered.Length != _plan.Requests.Count
                || ordered.Where((record, index) =>
                    record.Request != _plan.Requests[index]).Any())
                throw new InvalidDataException(
                    "Sealed response inventory must exactly equal the acquisition plan.");

            VerifyStage(includeManifest: false, includeCommit: false);
            var manifestBytes = EvidenceJson.WriteManifest(_plan, ordered);
            EvidenceFiles.WriteAtomic(
                _rootHandle, ManifestFileName, manifestBytes);
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
        _rootHandle.Dispose();
        _ownerLock.Dispose();
        _gate.Dispose();
    }

    private static void RecoverInitialCreate(
        string root,
        HandleBoundRoot rootHandle,
        PrivateEvidenceAcquisitionPlan expectedPlan)
    {
        if (rootHandle.EntryExists(PlanFileName)) return;
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
        var entries = Directory.EnumerateFileSystemEntries(root)
            .Select(Path.GetFileName)
            .ToArray();
        if (entries.Any(name => name != OwnerLockFileName
                                && name != ObjectsDirectoryName
                                && name != PendingDirectoryName
                                && name != ReceiptsDirectoryName
                                && name is not null
                                && !name.StartsWith($".{PlanFileName}-",
                                    StringComparison.Ordinal)))
            throw new InvalidDataException(
                "Incomplete private evidence creation cannot be recovered safely.");
        foreach (var directory in new[]
                 {
                     ObjectsDirectoryName,
                     PendingDirectoryName,
                     ReceiptsDirectoryName,
                 })
        {
            if (rootHandle.EntryExists(directory)
                && (!rootHandle.Exists(directory, expectDirectory: true)
                    || Directory.EnumerateFileSystemEntries(
                        Path.Combine(root, directory)).Any()))
                throw new InvalidDataException(
                    "Incomplete private evidence creation cannot replace captured state.");
        }
        EvidenceFiles.CleanupTemporaryFiles(root, rootHandle);
        EvidenceFiles.WriteAtomic(
            rootHandle, PlanFileName, EvidenceJson.WritePlan(expectedPlan));
    }

    private static void RecoverInitialDirectories(
        string root, HandleBoundRoot rootHandle)
    {
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
        var directories = new[]
        {
            ObjectsDirectoryName,
            PendingDirectoryName,
            ReceiptsDirectoryName,
        };
        if (directories.All(directory =>
                rootHandle.EntryExists(directory)
                && rootHandle.Exists(directory, expectDirectory: true)))
            return;
        if (rootHandle.EntryExists(ManifestFileName)
            || rootHandle.EntryExists(CommitMarkerFileName))
            throw new InvalidDataException(
                "A sealed private evidence bundle is missing a required directory.");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            OwnerLockFileName,
            PlanFileName,
            ObjectsDirectoryName,
            PendingDirectoryName,
            ReceiptsDirectoryName,
        };
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (!allowed.Contains(Path.GetFileName(entry)))
                throw new InvalidDataException(
                    "Incomplete private evidence creation has an unexpected entry.");
        }
        foreach (var directory in directories)
        {
            if (rootHandle.EntryExists(directory))
            {
                if (!rootHandle.Exists(directory, expectDirectory: true)
                    || Directory.EnumerateFileSystemEntries(
                        Path.Combine(root, directory)).Any())
                    throw new InvalidDataException(
                        "Incomplete private evidence creation cannot replace captured state.");
                continue;
            }
            rootHandle.EnsureDirectory(directory);
        }
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
    }

    private void RefreshRecoveredCaptures()
    {
        RecoverPendingCaptures(_root, _rootHandle, _plan);
        foreach (var record in LoadReceipts(_root, _rootHandle, _plan))
        {
            if (_records.TryGetValue(record.Request.RequestId, out var current))
            {
                if (!EvidenceJson.SameRecords([current], [record]))
                    throw new InvalidDataException(
                        "Recovered private evidence conflicts with live state.");
                continue;
            }
            _records.Add(record.Request.RequestId, record);
        }
    }

    private static void RecoverPendingCaptures(
        string root,
        HandleBoundRoot rootHandle,
        PrivateEvidenceAcquisitionPlan plan)
    {
        if (!rootHandle.Exists(PendingDirectoryName, expectDirectory: true))
            throw new InvalidDataException(
                "Private evidence pending directory is missing.");
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
        var requestIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(
                     Path.Combine(root, PendingDirectoryName)))
        {
            var name = Path.GetFileName(path);
            var suffix = name.EndsWith(".intent.json", StringComparison.Ordinal)
                ? ".intent.json"
                : name.EndsWith(".outcome.json", StringComparison.Ordinal)
                    ? ".outcome.json"
                    : name.EndsWith(".body", StringComparison.Ordinal)
                        ? ".body"
                        : null;
            if (suffix is null
                || name.Length != 64 + suffix.Length
                || !EvidenceFiles.IsSha256(name[..64]))
                throw new InvalidDataException(
                    "Private evidence pending entry has an invalid name.");
            var relative = PendingDirectoryName + "/" + name;
            if (!rootHandle.Exists(relative, expectDirectory: false))
                throw new InvalidDataException(
                    "Private evidence pending directory contains a non-file entry.");
            requestIds.Add(name[..64]);
        }
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");

        foreach (var requestId in requestIds.Order(StringComparer.Ordinal))
        {
            if (!rootHandle.EntryExists(
                    EvidenceFiles.CaptureIntentRelative(requestId)))
                throw new InvalidDataException(
                    "Private evidence pending state has no capture intent.");
            _ = RecoverPendingCapture(rootHandle, plan, requestId);
        }
    }

    private static StagedResponseRecord RecoverPendingCapture(
        HandleBoundRoot rootHandle,
        PrivateEvidenceAcquisitionPlan plan,
        string requestId)
    {
        var intent = EvidenceJson.ParseCaptureIntent(EvidenceFiles.ReadBounded(
            rootHandle,
            EvidenceFiles.CaptureIntentRelative(requestId),
            MaximumCaptureJournalBytes,
            "Private evidence capture intent"));
        var planned = plan.Requests.ElementAtOrDefault(intent.Request.Ordinal);
        if (planned is null
            || planned != intent.Request
            || intent.Request.RequestId != requestId)
            throw new InvalidDataException(
                "Private evidence capture intent is not an exact plan member.");

        var bodyRelative = EvidenceFiles.CaptureBodyRelative(requestId);
        var outcomeRelative = EvidenceFiles.CaptureOutcomeRelative(requestId);
        var receiptRelative = EvidenceFiles.ResponseReceiptRelative(requestId);
        var bodyExists = rootHandle.EntryExists(bodyRelative);
        var outcomeExists = rootHandle.EntryExists(outcomeRelative);
        if (rootHandle.EntryExists(receiptRelative))
        {
            var record = EvidenceJson.ParseReceipt(EvidenceFiles.ReadBounded(
                rootHandle,
                receiptRelative,
                MaximumResponseReceiptBytes,
                "Private evidence response receipt"));
            RequireIntentMatchesRecord(intent, record);
            if (outcomeExists)
            {
                var outcome = EvidenceJson.ParseCaptureOutcome(
                    EvidenceFiles.ReadBounded(
                        rootHandle,
                        outcomeRelative,
                        MaximumCaptureJournalBytes,
                        "Private evidence capture outcome"),
                    intent.Request,
                    intent.Response);
                var expected = RecordFromIntent(intent, outcome);
                if (!EvidenceJson.SameRecords([record], [expected]))
                    throw new InvalidDataException(
                        "Private evidence receipt conflicts with its capture outcome.");
            }
            EvidenceFiles.VerifyObject(
                rootHandle,
                EvidenceFiles.ObjectRelative(record.Evidence.ObjectSha256),
                record.Evidence.ObjectSha256,
                record.Evidence.ByteLength);
            if (bodyExists)
            {
                EvidenceFiles.VerifyObject(
                    rootHandle,
                    bodyRelative,
                    record.Evidence.ObjectSha256,
                    record.Evidence.ByteLength);
                rootHandle.DeleteFile(bodyRelative);
            }
            CleanupCaptureJournal(rootHandle, requestId);
            return record;
        }

        StagedResponseEvidence evidence;
        if (outcomeExists)
        {
            evidence = EvidenceJson.ParseCaptureOutcome(
                EvidenceFiles.ReadBounded(
                    rootHandle,
                    outcomeRelative,
                    MaximumCaptureJournalBytes,
                    "Private evidence capture outcome"),
                intent.Request,
                intent.Response);
        }
        else
        {
            if (!bodyExists)
            {
                using (var empty = rootHandle.CreateNewFile(bodyRelative))
                    empty.Flush(flushToDisk: true);
                rootHandle.FlushDirectory(PendingDirectoryName);
            }
            var retained = EvidenceFiles.HashBoundedObject(
                rootHandle,
                bodyRelative,
                checked(intent.Request.MaximumResponseBytes + 1));
            var reason = retained.Length
                         == intent.Request.MaximumResponseBytes + 1
                ? StagedResponseRejectionReason.BodyTooLarge
                : StagedResponseRejectionReason.TransportInterrupted;
            evidence = new RejectedStagedResponseEvidence(
                requestId, retained.Sha256, retained.Length, reason);
            EvidenceFiles.WriteAtomic(
                rootHandle,
                outcomeRelative,
                EvidenceJson.WriteCaptureOutcome(evidence));
        }

        var recovered = RecordFromIntent(intent, evidence);
        PublishPendingCapture(rootHandle, recovered);
        return recovered;
    }

    private static void RequireIntentMatchesRecord(
        PendingCaptureIntent intent, StagedResponseRecord record)
    {
        var expected = RecordFromIntent(intent, record.Evidence);
        if (!EvidenceJson.SameRecords([record], [expected]))
            throw new InvalidDataException(
                "Private evidence receipt conflicts with its capture intent.");
    }

    private static StagedResponseRecord RecordFromIntent(
        PendingCaptureIntent intent, StagedResponseEvidence evidence)
    {
        if (evidence is RejectedStagedResponseEvidence
            {
                Reason: StagedResponseRejectionReason.ResponseIncomplete,
            }
            && intent.Response.BodyComplete)
            throw new InvalidDataException(
                "Response-incomplete evidence conflicts with its capture intent.");
        return new StagedResponseRecord(
            intent.Request,
            evidence.BodyComplete
                ? intent.Response
                : intent.Response.MarkBodyIncomplete(),
            evidence);
    }

    private static void PublishPendingCapture(
        HandleBoundRoot rootHandle, StagedResponseRecord record)
    {
        var requestId = record.Request.RequestId;
        var bodyRelative = EvidenceFiles.CaptureBodyRelative(requestId);
        var objectRelative = EvidenceFiles.ObjectRelative(
            record.Evidence.ObjectSha256);
        if (rootHandle.EntryExists(bodyRelative))
        {
            EvidenceFiles.VerifyObject(
                rootHandle,
                bodyRelative,
                record.Evidence.ObjectSha256,
                record.Evidence.ByteLength);
            if (rootHandle.EntryExists(objectRelative))
            {
                EvidenceFiles.VerifyObject(
                    rootHandle,
                    objectRelative,
                    record.Evidence.ObjectSha256,
                    record.Evidence.ByteLength);
                rootHandle.DeleteFile(bodyRelative);
            }
            else
            {
                rootHandle.Move(bodyRelative, objectRelative, replace: false);
            }
        }
        else if (!rootHandle.EntryExists(objectRelative))
        {
            throw new InvalidDataException(
                "Private evidence capture outcome has no retained body.");
        }
        EvidenceFiles.VerifyObject(
            rootHandle,
            objectRelative,
            record.Evidence.ObjectSha256,
            record.Evidence.ByteLength);
        rootHandle.FlushFile(objectRelative);
        rootHandle.FlushDirectory(ObjectsDirectoryName);

        var receiptRelative = EvidenceFiles.ResponseReceiptRelative(requestId);
        if (rootHandle.EntryExists(receiptRelative))
        {
            var current = EvidenceJson.ParseReceipt(EvidenceFiles.ReadBounded(
                rootHandle,
                receiptRelative,
                MaximumResponseReceiptBytes,
                "Private evidence response receipt"));
            if (!EvidenceJson.SameRecords([current], [record]))
                throw new InvalidDataException(
                    "Private evidence response receipt changed during recovery.");
        }
        else
        {
            EvidenceFiles.WriteAtomic(
                rootHandle,
                receiptRelative,
                EvidenceJson.WriteReceipt(record));
        }
        CleanupCaptureJournal(rootHandle, requestId);
    }

    private static void CleanupCaptureJournal(
        HandleBoundRoot rootHandle, string requestId)
    {
        foreach (var relative in new[]
                 {
                     EvidenceFiles.CaptureOutcomeRelative(requestId),
                     EvidenceFiles.CaptureBodyRelative(requestId),
                     EvidenceFiles.CaptureIntentRelative(requestId),
                 })
        {
            if (rootHandle.EntryExists(relative)) rootHandle.DeleteFile(relative);
        }
    }

    private static IReadOnlyList<StagedResponseRecord> LoadReceipts(
        string root,
        HandleBoundRoot rootHandle,
        PrivateEvidenceAcquisitionPlan plan)
    {
        var receiptsRoot = Path.Combine(root, ReceiptsDirectoryName);
        if (!rootHandle.Exists(ReceiptsDirectoryName, expectDirectory: true))
            throw new InvalidDataException(
                "Private evidence receipts directory is missing.");
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
        var planned = plan.Requests.ToDictionary(
            request => request.RequestId, StringComparer.Ordinal);
        var records = new List<StagedResponseRecord>();
        foreach (var path in Directory.EnumerateFileSystemEntries(receiptsRoot))
        {
            var name = Path.GetFileName(path);
            if (name.Length != 69 || !name.EndsWith(".json", StringComparison.Ordinal)
                || !EvidenceFiles.IsSha256(name[..64]))
                throw new InvalidDataException(
                    "Private evidence response receipt has an invalid name.");
            var relative = ReceiptsDirectoryName + "/" + name;
            if (!rootHandle.Exists(relative, expectDirectory: false))
                throw new InvalidDataException(
                    "Private evidence receipts directory contains a non-file entry.");
            var bytes = EvidenceFiles.ReadBounded(
                rootHandle,
                relative,
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
                rootHandle,
                EvidenceFiles.ObjectRelative(record.Evidence.ObjectSha256),
                record.Evidence.ObjectSha256,
                record.Evidence.ByteLength);
            records.Add(record);
        }
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
        return records;
    }

    private void VerifySealedManifest(bool includeCommit)
    {
        var ordered = OrderedRecords();
        if (ordered.Length != _plan.Requests.Count)
            throw new InvalidDataException(
                "Private evidence manifest cannot seal an incomplete plan.");
        var manifestBytes = EvidenceFiles.ReadBounded(
            _rootHandle,
            ManifestFileName,
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
            _rootHandle,
            CommitMarkerFileName,
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
            _rootHandle,
            ManifestFileName,
            MaximumManifestBytes,
            "Private evidence manifest");
        var parsed = EvidenceJson.ParseManifest(manifestBytes);
        if (parsed.BundleId != _plan.BundleId
            || !EvidenceJson.SameRecords(parsed.Records, ordered))
            throw new InvalidDataException(
                "Private evidence manifest changed before commit.");
        EvidenceFiles.WriteAtomic(
            _rootHandle,
            CommitMarkerFileName,
            EvidenceJson.WriteCommit(_plan, manifestBytes, ordered));
    }

    private void VerifyStage(bool includeManifest, bool includeCommit)
    {
        var planBytes = EvidenceFiles.ReadBounded(
            _rootHandle,
            PlanFileName,
            MaximumPlanBytes,
            "Private evidence plan");
        if (!planBytes.AsSpan().SequenceEqual(EvidenceJson.WritePlan(_plan)))
            throw new InvalidDataException("Private evidence plan changed.");

        var ordered = OrderedRecords();
        EvidenceFiles.VerifyExactLayout(
            _root, _rootHandle, ordered, includeManifest, includeCommit);
        foreach (var record in ordered)
        {
            var receiptBytes = EvidenceFiles.ReadBounded(
                _rootHandle,
                EvidenceFiles.ResponseReceiptRelative(record.Request.RequestId),
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
                _rootHandle,
                EvidenceFiles.ObjectRelative(first.ObjectSha256),
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
        FileStream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        var reachedEnd = false;
        var interrupted = false;
        var retainedLimit = checked(request.MaximumResponseBytes + 1);
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
