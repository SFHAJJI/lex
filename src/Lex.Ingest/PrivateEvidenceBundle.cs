using System.Security.Cryptography;
using System.Text;
using Lex.Law;
using Lex.Temporal;

namespace Lex.Ingest;

/// <summary>
/// The stable identity and acquisition policy for one private capture bundle.
/// Physical requests are appended durably at runtime and are not guessed here.
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
        string acquisitionPolicySha256)
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
        AcquisitionPolicySha256 = CodeIdentity.RequireSha256(
            acquisitionPolicySha256, nameof(acquisitionPolicySha256));
        BundleId = HashBundleIdentity();
    }

    public string RunIdentity { get; }
    public string CodeCommit { get; }
    public string Publisher { get; }
    public string BaselineCorpusSha256 { get; }
    public string EnumerationScopeSha256 { get; }
    public string EndpointPolicySha256 { get; }
    public string AcquisitionPolicySha256 { get; }
    public string BundleId { get; }

    private string HashBundleIdentity()
    {
        var canonical = string.Join('\n',
            "lex-private-evidence-bundle-id/3",
            RunIdentity,
            CodeCommit,
            Publisher,
            BaselineCorpusSha256,
            EnumerationScopeSha256,
            EndpointPolicySha256,
            AcquisitionPolicySha256);
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

public enum PrivateEvidenceAttemptDisposition
{
    Response = 1,
    NoResponse = 2,
}

public sealed class PrivateEvidenceAttemptRecord
{
    internal PrivateEvidenceAttemptRecord(
        RecordedSourceRequest request,
        string? predecessorAttemptSha256,
        string attemptSha256,
        PrivateEvidenceAttemptDisposition disposition,
        string? responseReceiptSha256,
        string terminalSha256)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        PredecessorAttemptSha256 = predecessorAttemptSha256 is null
            ? null
            : CodeIdentity.RequireSha256(
                predecessorAttemptSha256, nameof(predecessorAttemptSha256));
        AttemptSha256 = CodeIdentity.RequireSha256(
            attemptSha256, nameof(attemptSha256));
        if (!Enum.IsDefined(disposition))
            throw new InvalidDataException(
                "Private evidence attempt disposition is unsupported.");
        Disposition = disposition;
        if (disposition == PrivateEvidenceAttemptDisposition.Response)
            ResponseReceiptSha256 = CodeIdentity.RequireSha256(
                responseReceiptSha256, nameof(responseReceiptSha256));
        else if (responseReceiptSha256 is not null)
            throw new InvalidDataException(
                "A response-free attempt cannot bind a response receipt.");
        TerminalSha256 = CodeIdentity.RequireSha256(
            terminalSha256, nameof(terminalSha256));
    }

    public RecordedSourceRequest Request { get; }
    public string? PredecessorAttemptSha256 { get; }
    public string AttemptSha256 { get; }
    public PrivateEvidenceAttemptDisposition Disposition { get; }
    public string? ResponseReceiptSha256 { get; }
    public string TerminalSha256 { get; }
}

public sealed class PrivateEvidencePhysicalAttempt
{
    internal PrivateEvidencePhysicalAttempt(
        PrivateEvidenceBundle owner,
        PrivateEvidenceAttemptState state,
        SourceRequestIdentity request)
    {
        Owner = owner;
        State = state;
        Request = request;
    }

    public SourceRequestIdentity Request { get; }
    public string AttemptSha256 => State.AttemptSha256;

    internal PrivateEvidenceBundle Owner { get; }
    internal PrivateEvidenceAttemptState State { get; }
}

internal sealed class PrivateEvidenceAttemptState
{
    public PrivateEvidenceAttemptState(
        RecordedSourceRequest request,
        string? predecessorAttemptSha256,
        string attemptSha256,
        PrivateEvidenceAttemptRecord? terminal = null)
    {
        Request = request;
        PredecessorAttemptSha256 = predecessorAttemptSha256;
        AttemptSha256 = attemptSha256;
        Terminal = terminal;
    }

    public RecordedSourceRequest Request { get; }
    public string? PredecessorAttemptSha256 { get; }
    public string AttemptSha256 { get; }
    public PrivateEvidenceAttemptRecord? Terminal { get; set; }
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
        RecordedSourceRequest request,
        RecordedResponseMetadata response,
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

    public RecordedSourceRequest Request { get; }
    public RecordedResponseMetadata Response { get; }
    public StagedResponseEvidence Evidence { get; }
}

/// <summary>A receipt for a sealed local stage, never a remote durability claim.</summary>
public sealed class PrivateEvidenceStageReceipt
{
    internal PrivateEvidenceStageReceipt(
        string bundleId,
        string manifestSha256,
        string attemptChainSha256,
        string attemptInventorySha256,
        IReadOnlyCollection<PrivateEvidenceAttemptRecord> attempts,
        IReadOnlyCollection<StagedResponseRecord> records)
    {
        BundleId = CodeIdentity.RequireSha256(bundleId, nameof(bundleId));
        ManifestSha256 = CodeIdentity.RequireSha256(
            manifestSha256, nameof(manifestSha256));
        AttemptChainSha256 = CodeIdentity.RequireSha256(
            attemptChainSha256, nameof(attemptChainSha256));
        AttemptInventorySha256 = CodeIdentity.RequireSha256(
            attemptInventorySha256, nameof(attemptInventorySha256));
        ArgumentNullException.ThrowIfNull(attempts);
        Attempts = Array.AsReadOnly(attempts
            .OrderBy(attempt => attempt.Request.Ordinal)
            .ToArray());
        ArgumentNullException.ThrowIfNull(records);
        Records = Array.AsReadOnly(records
            .OrderBy(record => record.Request.Ordinal)
            .ToArray());
    }

    public string BundleId { get; }
    public string ManifestSha256 { get; }
    public string AttemptChainSha256 { get; }
    public string AttemptInventorySha256 { get; }
    public IReadOnlyList<PrivateEvidenceAttemptRecord> Attempts { get; }
    public IReadOnlyList<StagedResponseRecord> Records { get; }
}

/// <summary>
/// Crash-recoverable local staging for physical response bytes. The root is bound to
/// one non-link directory handle, and data reads and mutations are handle-relative.
/// Name enumeration revalidates that file identity. The owner lock coordinates peers;
/// hostile code already running as the same operating-system identity is out of scope.
/// Journal hashes prove internal consistency, not authenticity or external attestation.
/// </summary>
public sealed class PrivateEvidenceBundle : IDisposable
{
    /// <summary>
    /// Acquisition must stop before this many physical attempts and continue in
    /// a new bundle whose trusted run identity is distinct. Attempts never spill
    /// implicitly between bundles. The cap keeps a worst-case canonical manifest
    /// below its readback bound.
    /// </summary>
    public const int MaximumAttemptsPerBundle = 64;
    public const string PlanSchema = "lex-private-evidence-plan/3";
    public const string ResponseReceiptSchema =
        "lex-private-evidence-response/3";
    public const string CaptureIntentSchema =
        "lex-private-evidence-capture-intent/2";
    public const string CaptureOutcomeSchema =
        "lex-private-evidence-capture-outcome/1";
    public const string AttemptStartSchema =
        "lex-private-evidence-attempt-start/1";
    public const string AttemptHeadSchema =
        "lex-private-evidence-attempt-head/1";
    public const string AttemptTerminalSchema =
        "lex-private-evidence-attempt-terminal/1";
    public const string AttemptChainSchema =
        "lex-private-evidence-attempt-chain/1";
    public const string AttemptInventorySchema =
        "lex-private-evidence-attempt-inventory/1";
    public const string ManifestSchema = "lex-private-evidence-bundle/3";
    public const string CommitMarkerSchema =
        "lex-private-evidence-stage-commit/3";
    public const string PlanFileName = "plan.json";
    public const string AttemptHeadFileName = "attempt-head.json";
    public const string ManifestFileName = "manifest.json";
    public const string CommitMarkerFileName = "commit.json";
    public const string OwnerLockFileName = "owner.lock";
    public const string AttemptsDirectoryName = "attempts";
    public const string ObjectsDirectoryName = "objects";
    public const string PendingDirectoryName = "pending";
    public const string ReceiptsDirectoryName = "receipts";
    private const int MaximumPlanBytes = 16 * 1024 * 1024;
    private const int MaximumManifestBytes = 16 * 1024 * 1024;
    private const int MaximumResponseReceiptBytes = 64 * 1024;
    private const int MaximumCaptureJournalBytes = 64 * 1024;
    private const int MaximumAttemptJournalBytes = 64 * 1024;
    private const int MaximumAttemptHeadBytes = 64 * 1024;
    private const int MaximumMarkerBytes = 64 * 1024;

    private readonly string _root;
    private readonly PrivateEvidenceAcquisitionPlan _plan;
    private readonly Dictionary<string, StagedResponseRecord> _records;
    private readonly List<PrivateEvidenceAttemptState> _attempts;
    private readonly HashSet<PrivateEvidencePhysicalAttempt> _liveAttempts = [];
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
        IReadOnlyCollection<PrivateEvidenceAttemptState>? attempts = null,
        IReadOnlyCollection<StagedResponseRecord>? records = null,
        bool isSealed = false)
    {
        _root = root;
        _plan = plan;
        _ownerLock = ownerLock;
        _rootHandle = rootHandle;
        _attempts = (attempts ?? [])
            .OrderBy(attempt => attempt.Request.Ordinal)
            .ToList();
        _records = (records ?? [])
            .ToDictionary(record => record.Request.RequestId, StringComparer.Ordinal);
        _sealed = isSealed;
    }

    public PrivateEvidenceAcquisitionPlan Plan => _plan;
    public bool IsSealed => _sealed;
    public IReadOnlyList<PrivateEvidenceAttemptRecord> Attempts => _attempts
        .Select(attempt => attempt.Terminal)
        .Where(terminal => terminal is not null)
        .Cast<PrivateEvidenceAttemptRecord>()
        .ToArray();
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
            rootHandle.EnsureDirectory(AttemptsDirectoryName);
            rootHandle.EnsureDirectory(ObjectsDirectoryName);
            rootHandle.EnsureDirectory(PendingDirectoryName);
            rootHandle.EnsureDirectory(ReceiptsDirectoryName);
            EvidenceFiles.WriteAtomic(
                rootHandle,
                AttemptHeadFileName,
                EvidenceJson.WriteAttemptHead(plan, 0, null));
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

    /// <summary>
    /// Opens only a fully sealed bundle whose manifest and commit marker verify.
    /// Unsealed state is never normalized or promoted. It remains untouched for
    /// forensic inspection, and acquisition must restart under a distinct identity.
    /// </summary>
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
            var hasManifest = rootHandle.EntryExists(ManifestFileName);
            var hasCommit = rootHandle.EntryExists(CommitMarkerFileName);
            if (!hasManifest || !hasCommit)
                throw new InvalidDataException(
                    "Unsealed private evidence is retained for forensics and cannot be reopened; start a new acquisition with a distinct trusted run identity.");
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

            var attempts = LoadAttemptStarts(root, rootHandle, parsedPlan);
            VerifyAttemptHead(rootHandle, parsedPlan, attempts);
            var records = LoadReceipts(root, rootHandle);
            LoadAttemptTerminals(
                root, rootHandle, parsedPlan, attempts);
            ValidateAttemptResponses(attempts, records);

            var bundle = new PrivateEvidenceBundle(
                root,
                parsedPlan,
                ownerLock,
                rootHandle,
                attempts,
                records,
                isSealed: true);
            bundle.VerifyStage(includeManifest: true, includeCommit: true);
            return bundle;
        }
        catch
        {
            rootHandle?.Dispose();
            ownerLock?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Durably appends the physical request before the caller may send it.
    /// The returned token is bound to this bundle and exactly one terminal result.
    /// </summary>
    public PrivateEvidencePhysicalAttempt BeginAttempt(
        SourceRequestIdentity request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _gate.Wait();
        try
        {
            RequireOpen();
            if (_attempts.Count >= MaximumAttemptsPerBundle)
                throw new InvalidOperationException(
                    "Private evidence attempt cap reached; continue in a new bundle with a distinct trusted run identity.");
            if (request.Publisher != _plan.Publisher)
                throw new InvalidDataException(
                    "A physical attempt must belong to the bundle publisher.");
            var recordedRequest = request.ToRecordedClaim();
            if (recordedRequest.Ordinal != _attempts.Count)
                throw new InvalidDataException(
                    "A physical attempt ordinal must append without a gap.");
            if (_attempts.Any(current =>
                    current.Request.RequestId == recordedRequest.RequestId))
                throw new InvalidDataException(
                    "A physical attempt request identity must be unique.");

            var predecessor = _attempts.LastOrDefault()?.AttemptSha256;
            var state = new PrivateEvidenceAttemptState(
                recordedRequest,
                predecessor,
                EvidenceJson.HashAttemptStart(
                    _plan, predecessor, recordedRequest));
            EvidenceFiles.WriteAtomic(
                _rootHandle,
                EvidenceFiles.AttemptStartRelative(recordedRequest),
                EvidenceJson.WriteAttemptStart(_plan, state));
            EvidenceFiles.WriteAtomic(
                _rootHandle,
                AttemptHeadFileName,
                EvidenceJson.WriteAttemptHead(
                    _plan, _attempts.Count + 1, state.AttemptSha256),
                replace: true);
            _attempts.Add(state);
            var token = new PrivateEvidencePhysicalAttempt(this, state, request);
            _liveAttempts.Add(token);
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Retains the bounded response bytes and terminalizes the attempt as Response.
    /// </summary>
    public async Task<StagedResponseEvidence> CaptureAsync(
        PrivateEvidencePhysicalAttempt attempt,
        BoundedResponseMetadata response,
        Stream body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(body);
        if (!body.CanRead)
            throw new InvalidDataException("Response body stream must be readable.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireOpen();
            RequireLiveAttempt(attempt);
            var request = attempt.Request;
            EvidenceFiles.WriteAtomic(
                _rootHandle,
                EvidenceFiles.CaptureIntentRelative(request.RequestId),
                EvidenceJson.WriteCaptureIntent(
                    attempt.AttemptSha256, request, response));
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
                request.ToRecordedClaim(),
                retainedResponse.ToRecordedClaim(),
                evidence);
            EvidenceFiles.WriteAtomic(
                _rootHandle,
                EvidenceFiles.CaptureOutcomeRelative(request.RequestId),
                EvidenceJson.WriteCaptureOutcome(evidence));
            PublishPendingCapture(_rootHandle, record);
            _records.Add(request.RequestId, record);
            PersistAttemptTerminal(
                attempt.State,
                PrivateEvidenceAttemptDisposition.Response,
                EvidenceJson.Sha256(EvidenceJson.WriteReceipt(record)));
            _liveAttempts.Remove(attempt);
            return evidence;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Terminalizes a live attempt that produced no response.</summary>
    public void RecordNoResponse(PrivateEvidencePhysicalAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        _gate.Wait();
        try
        {
            RequireOpen();
            RequireLiveAttempt(attempt);
            var requestId = attempt.Request.RequestId;
            if (_records.ContainsKey(requestId)
                || _rootHandle.EntryExists(
                    EvidenceFiles.ResponseReceiptRelative(requestId))
                || _rootHandle.EntryExists(
                    EvidenceFiles.CaptureIntentRelative(requestId))
                || _rootHandle.EntryExists(
                    EvidenceFiles.CaptureOutcomeRelative(requestId))
                || _rootHandle.EntryExists(
                    EvidenceFiles.CaptureBodyRelative(requestId)))
                throw new InvalidDataException(
                    "An attempt with response state cannot become no-response.");
            PersistAttemptTerminal(
                attempt.State,
                PrivateEvidenceAttemptDisposition.NoResponse,
                responseReceiptSha256: null);
            _liveAttempts.Remove(attempt);
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
            if (_liveAttempts.Count != 0
                || _attempts.Any(attempt => attempt.Terminal is null))
                throw new InvalidOperationException(
                    "Private evidence cannot seal with a live physical attempt.");
            ValidateAttemptResponses(_attempts, OrderedRecords());
            var attempts = OrderedAttempts();
            var ordered = OrderedRecords();

            VerifyStage(includeManifest: false, includeCommit: false);
            var manifestBytes = EvidenceJson.WriteManifest(
                _plan, attempts, ordered);
            if (manifestBytes.Length > MaximumManifestBytes)
                throw new InvalidDataException(
                    "Private evidence manifest exceeds its readback bound; partition the acquisition before sealing.");
            EvidenceFiles.WriteAtomic(
                _rootHandle, ManifestFileName, manifestBytes);
            VerifyStage(includeManifest: true, includeCommit: false);
            WriteCommitMarker();
            VerifyStage(includeManifest: true, includeCommit: true);
            _sealed = true;
            return new PrivateEvidenceStageReceipt(
                _plan.BundleId,
                EvidenceJson.Sha256(manifestBytes),
                EvidenceJson.HashAttemptChain(_plan, attempts),
                EvidenceJson.HashAttemptInventory(_plan, attempts),
                attempts,
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
                    "Private evidence response receipt changed during publication.");
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

    private static List<PrivateEvidenceAttemptState> LoadAttemptStarts(
        string root,
        HandleBoundRoot rootHandle,
        PrivateEvidenceAcquisitionPlan plan)
    {
        if (!rootHandle.Exists(AttemptsDirectoryName, expectDirectory: true))
            throw new InvalidDataException(
                "Private evidence attempts directory is missing.");
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
        var attemptsRoot = Path.Combine(root, AttemptsDirectoryName);
        var starts = new List<PrivateEvidenceAttemptState>();
        foreach (var path in Directory.EnumerateFileSystemEntries(attemptsRoot))
        {
            var name = Path.GetFileName(path);
            var suffix = name.EndsWith(".start.json", StringComparison.Ordinal)
                ? ".start.json"
                : name.EndsWith(".terminal.json", StringComparison.Ordinal)
                    ? ".terminal.json"
                    : null;
            if (suffix is null
                || name.Length != 71 + suffix.Length
                || name[6] != '-'
                || !int.TryParse(
                    name.AsSpan(0, 6),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var ordinal)
                || !EvidenceFiles.IsSha256(name.Substring(7, 64)))
                throw new InvalidDataException(
                    "Private evidence attempt entry has an invalid name.");
            var relative = AttemptsDirectoryName + "/" + name;
            if (!rootHandle.Exists(relative, expectDirectory: false))
                throw new InvalidDataException(
                    "Private evidence attempts directory contains a non-file entry.");
            if (suffix != ".start.json") continue;

            var attempt = EvidenceJson.ParseAttemptStart(
                EvidenceFiles.ReadBounded(
                    rootHandle,
                    relative,
                    MaximumAttemptJournalBytes,
                    "Private evidence attempt start"),
                plan);
            if (attempt.Request.Ordinal != ordinal
                || attempt.Request.RequestId != name.Substring(7, 64)
                || attempt.Request.Publisher != plan.Publisher)
                throw new InvalidDataException(
                    "Private evidence attempt filename or publisher is inconsistent.");
            starts.Add(attempt);
        }
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");

        var ordered = starts.OrderBy(attempt => attempt.Request.Ordinal).ToList();
        if (ordered.Count > MaximumAttemptsPerBundle)
            throw new InvalidDataException(
                "Private evidence attempt inventory exceeds its bundle cap.");
        var requestIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Count; index++)
        {
            var attempt = ordered[index];
            var predecessor = index == 0
                ? null
                : ordered[index - 1].AttemptSha256;
            if (attempt.Request.Ordinal != index
                || attempt.PredecessorAttemptSha256 != predecessor
                || !requestIds.Add(attempt.Request.RequestId))
                throw new InvalidDataException(
                    "Private evidence attempt chain has a gap, duplicate, or invalid predecessor.");
        }
        return ordered;
    }

    private static void VerifyAttemptHead(
        HandleBoundRoot rootHandle,
        PrivateEvidenceAcquisitionPlan plan,
        IReadOnlyList<PrivateEvidenceAttemptState> attempts)
    {
        if (!rootHandle.EntryExists(AttemptHeadFileName))
            throw new InvalidDataException(
                "Private evidence attempt head is missing.");

        var head = EvidenceJson.ParseAttemptHead(EvidenceFiles.ReadBounded(
            rootHandle,
            AttemptHeadFileName,
            MaximumAttemptHeadBytes,
            "Private evidence attempt head"), plan);
        var expectedHead = head.AttemptCount == 0
            ? null
            : attempts.ElementAtOrDefault(head.AttemptCount - 1)?.AttemptSha256;
        if (head.HeadAttemptSha256 != expectedHead)
            throw new InvalidDataException(
                "Private evidence attempt head does not match its published prefix.");
        if (head.AttemptCount != attempts.Count)
            throw new InvalidDataException(
                "Private evidence attempt head does not match its exact inventory.");
    }

    private static void LoadAttemptTerminals(
        string root,
        HandleBoundRoot rootHandle,
        PrivateEvidenceAcquisitionPlan plan,
        IReadOnlyList<PrivateEvidenceAttemptState> attempts)
    {
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
        var terminalNames = Directory.EnumerateFileSystemEntries(
                Path.Combine(root, AttemptsDirectoryName))
            .Select(path => Path.GetFileName(path)
                ?? throw new InvalidDataException(
                    "Private evidence attempt entry has no filename."))
            .Where(name => name.EndsWith(
                ".terminal.json", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var expectedNames = attempts.Select(attempt =>
                Path.GetFileName(EvidenceFiles.AttemptTerminalRelative(
                    attempt.Request))
                ?? throw new InvalidDataException(
                    "Private evidence attempt terminal has no filename."))
            .ToHashSet(StringComparer.Ordinal);
        if (!terminalNames.SetEquals(expectedNames))
            throw new InvalidDataException(
                "Private evidence attempt terminals do not exactly match their starts.");

        foreach (var attempt in attempts)
        {
            var relative = EvidenceFiles.AttemptTerminalRelative(
                attempt.Request);
            attempt.Terminal = EvidenceJson.ParseAttemptTerminal(
                EvidenceFiles.ReadBounded(
                    rootHandle,
                    relative,
                    MaximumAttemptJournalBytes,
                    "Private evidence attempt terminal"),
                plan,
                attempt);
        }
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
    }

    private void PersistAttemptTerminal(
        PrivateEvidenceAttemptState attempt,
        PrivateEvidenceAttemptDisposition disposition,
        string? responseReceiptSha256) => PersistAttemptTerminal(
        _rootHandle, _plan, attempt, disposition, responseReceiptSha256);

    private static void PersistAttemptTerminal(
        HandleBoundRoot rootHandle,
        PrivateEvidenceAcquisitionPlan plan,
        PrivateEvidenceAttemptState attempt,
        PrivateEvidenceAttemptDisposition disposition,
        string? responseReceiptSha256)
    {
        var expected = EvidenceJson.CreateAttemptTerminal(
            plan, attempt, disposition, responseReceiptSha256);
        var relative = EvidenceFiles.AttemptTerminalRelative(attempt.Request);
        if (rootHandle.EntryExists(relative))
        {
            var current = EvidenceJson.ParseAttemptTerminal(
                EvidenceFiles.ReadBounded(
                    rootHandle,
                    relative,
                    MaximumAttemptJournalBytes,
                    "Private evidence attempt terminal"),
                plan,
                attempt);
            if (current.TerminalSha256 != expected.TerminalSha256)
                throw new InvalidDataException(
                    "A physical attempt already has a different terminal result.");
            attempt.Terminal = current;
            return;
        }
        EvidenceFiles.WriteAtomic(
            rootHandle,
            relative,
            EvidenceJson.WriteAttemptTerminal(plan, expected));
        attempt.Terminal = expected;
    }

    private static void ValidateAttemptResponses(
        IReadOnlyCollection<PrivateEvidenceAttemptState> attempts,
        IReadOnlyCollection<StagedResponseRecord> records)
    {
        var responses = records.ToDictionary(
            record => record.Request.RequestId, StringComparer.Ordinal);
        var responseAttempts = 0;
        foreach (var attempt in attempts)
        {
            var terminal = attempt.Terminal
                ?? throw new InvalidDataException(
                    "Private evidence attempt has no terminal result.");
            if (terminal.Disposition
                == PrivateEvidenceAttemptDisposition.Response)
            {
                responseAttempts++;
                if (!responses.TryGetValue(
                        attempt.Request.RequestId, out var response)
                    || response.Request != attempt.Request
                    || terminal.ResponseReceiptSha256
                    != EvidenceJson.Sha256(EvidenceJson.WriteReceipt(response)))
                    throw new InvalidDataException(
                        "A response attempt does not bind its exact receipt.");
            }
            else if (responses.ContainsKey(attempt.Request.RequestId))
            {
                throw new InvalidDataException(
                    "A response-free attempt cannot retain a response receipt.");
            }
        }
        if (responseAttempts != records.Count)
            throw new InvalidDataException(
                "Private evidence receipts do not exactly match response attempts.");
    }

    private static IReadOnlyList<StagedResponseRecord> LoadReceipts(
        string root,
        HandleBoundRoot rootHandle)
    {
        var receiptsRoot = Path.Combine(root, ReceiptsDirectoryName);
        if (!rootHandle.Exists(ReceiptsDirectoryName, expectDirectory: true))
            throw new InvalidDataException(
                "Private evidence receipts directory is missing.");
        EvidenceFiles.RequireRootIdentity(
            root, rootHandle, "Private evidence staging root");
        var records = new List<StagedResponseRecord>();
        var requestIds = new HashSet<string>(StringComparer.Ordinal);
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
                || !requestIds.Add(record.Request.RequestId))
                throw new InvalidDataException(
                    "Private evidence receipt identity is not unique and exact.");
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
        var attempts = OrderedAttempts();
        var ordered = OrderedRecords();
        ValidateAttemptResponses(_attempts, ordered);
        var manifestBytes = EvidenceFiles.ReadBounded(
            _rootHandle,
            ManifestFileName,
            MaximumManifestBytes,
            "Private evidence manifest");
        var parsed = EvidenceJson.ParseManifest(manifestBytes);
        if (parsed.BundleId != _plan.BundleId
            || parsed.AttemptChainSha256
            != EvidenceJson.HashAttemptChain(_plan, attempts)
            || parsed.AttemptInventorySha256
            != EvidenceJson.HashAttemptInventory(_plan, attempts)
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
            _plan, manifestBytes, attempts);
        if (!commitBytes.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException(
                "Private evidence commit marker does not match the final rehash.");
    }

    private void WriteCommitMarker()
    {
        var attempts = OrderedAttempts();
        var ordered = OrderedRecords();
        var manifestBytes = EvidenceFiles.ReadBounded(
            _rootHandle,
            ManifestFileName,
            MaximumManifestBytes,
            "Private evidence manifest");
        var parsed = EvidenceJson.ParseManifest(manifestBytes);
        if (parsed.BundleId != _plan.BundleId
            || parsed.AttemptChainSha256
            != EvidenceJson.HashAttemptChain(_plan, attempts)
            || parsed.AttemptInventorySha256
            != EvidenceJson.HashAttemptInventory(_plan, attempts)
            || !EvidenceJson.SameRecords(parsed.Records, ordered))
            throw new InvalidDataException(
                "Private evidence manifest changed before commit.");
        EvidenceFiles.WriteAtomic(
            _rootHandle,
            CommitMarkerFileName,
            EvidenceJson.WriteCommit(
                _plan, manifestBytes, attempts));
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
        var headBytes = EvidenceFiles.ReadBounded(
            _rootHandle,
            AttemptHeadFileName,
            MaximumAttemptHeadBytes,
            "Private evidence attempt head");
        if (!headBytes.AsSpan().SequenceEqual(EvidenceJson.WriteAttemptHead(
                _plan,
                _attempts.Count,
                _attempts.LastOrDefault()?.AttemptSha256)))
            throw new InvalidDataException(
                "Private evidence attempt head changed.");

        var ordered = OrderedRecords();
        EvidenceFiles.VerifyExactLayout(
            _root,
            _rootHandle,
            _attempts,
            ordered,
            includeManifest,
            includeCommit);
        foreach (var attempt in _attempts)
        {
            var startBytes = EvidenceFiles.ReadBounded(
                _rootHandle,
                EvidenceFiles.AttemptStartRelative(attempt.Request),
                MaximumAttemptJournalBytes,
                "Private evidence attempt start");
            if (!startBytes.AsSpan().SequenceEqual(
                    EvidenceJson.WriteAttemptStart(_plan, attempt)))
                throw new InvalidDataException(
                    "Private evidence attempt start changed.");
            if (attempt.Terminal is null) continue;
            var terminalBytes = EvidenceFiles.ReadBounded(
                _rootHandle,
                EvidenceFiles.AttemptTerminalRelative(attempt.Request),
                MaximumAttemptJournalBytes,
                "Private evidence attempt terminal");
            if (!terminalBytes.AsSpan().SequenceEqual(
                    EvidenceJson.WriteAttemptTerminal(
                        _plan, attempt.Terminal)))
                throw new InvalidDataException(
                    "Private evidence attempt terminal changed.");
        }
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

    private PrivateEvidenceAttemptRecord[] OrderedAttempts() => _attempts
        .Select(attempt => attempt.Terminal
            ?? throw new InvalidDataException(
                "Private evidence attempt has no terminal result."))
        .ToArray();

    private void RequireLiveAttempt(PrivateEvidencePhysicalAttempt attempt)
    {
        if (!ReferenceEquals(attempt.Owner, this)
            || !_liveAttempts.Contains(attempt)
            || !_attempts.Contains(attempt.State)
            || attempt.State.Terminal is not null)
            throw new InvalidDataException(
                "Physical attempt token is not live for this bundle.");
    }

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
            catch (Exception error) when (error is IOException
                                          or HttpRequestException
                                          or System.Net.Sockets.SocketException
                                          or TimeoutException)
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
