using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.Law;
using Lex.Temporal;

namespace Lex.Ingest;

internal sealed record PlanDocument(
    string Schema,
    string RunIdentity,
    string CodeCommit,
    string Publisher,
    string BaselineCorpusSha256,
    string EnumerationScopeSha256,
    string EndpointPolicySha256,
    string AcquisitionPolicySha256,
    string BundleId);

internal sealed record RequestDocument(
    string RequestId,
    string Publisher,
    string Channel,
    SourceRequestMethod Method,
    string RequestUri,
    string RequestUriSha256,
    string? RequestBodySha256,
    int Ordinal,
    long MaximumResponseBytes,
    int PhysicalAttempt,
    int RedirectHop);

internal sealed record ResponseDocument(
    int StatusCode,
    string? ContentType,
    string? Charset,
    string? EntityTag,
    DateTimeOffset? LastModified,
    DateTimeOffset FetchedAt,
    string EffectiveSourceUri,
    string EffectiveSourceUriSha256,
    bool BodyComplete);

internal sealed record EvidenceDocument(
    StagedResponseDisposition Disposition,
    string RequestId,
    string ObjectSha256,
    long ByteLength,
    StagedResponseRejectionReason? RejectionReason);

internal sealed record RecordDocument(
    RequestDocument Request,
    ResponseDocument Response,
    EvidenceDocument Evidence);

internal sealed record ResponseReceiptDocument(
    string Schema,
    RecordDocument Record);

internal sealed record CaptureIntentDocument(
    string Schema,
    string AttemptSha256,
    string BodyFileName,
    RequestDocument Request,
    ResponseDocument Response);

internal sealed record CaptureOutcomeDocument(
    string Schema,
    EvidenceDocument Evidence);

internal sealed record AttemptChainDocument(
    string Schema,
    string BundleId,
    string? PredecessorAttemptSha256,
    RequestDocument Request);

internal sealed record AttemptStartDocument(
    string Schema,
    string BundleId,
    string? PredecessorAttemptSha256,
    string AttemptSha256,
    RequestDocument Request);

internal sealed record AttemptTerminalPayloadDocument(
    string Schema,
    string BundleId,
    string AttemptSha256,
    PrivateEvidenceAttemptDisposition Disposition,
    string? ResponseReceiptSha256);

internal sealed record AttemptTerminalDocument(
    string Schema,
    string BundleId,
    string AttemptSha256,
    PrivateEvidenceAttemptDisposition Disposition,
    string? ResponseReceiptSha256,
    string TerminalSha256);

internal sealed record AttemptInventoryEntryDocument(
    int Ordinal,
    string AttemptSha256,
    string TerminalSha256,
    PrivateEvidenceAttemptDisposition Disposition,
    string? ResponseReceiptSha256);

internal sealed record AttemptInventoryDocument(
    string Schema,
    string BundleId,
    IReadOnlyList<AttemptInventoryEntryDocument> Attempts);

internal sealed record ManifestDocument(
    string Schema,
    string BundleId,
    string AttemptChainSha256,
    string AttemptInventorySha256,
    IReadOnlyList<RecordDocument> Records);

internal sealed record CommitDocument(
    string Schema,
    string BundleId,
    string ManifestSha256,
    string AttemptChainSha256,
    string AttemptInventorySha256);

internal sealed record ParsedManifest(
    string BundleId,
    string AttemptChainSha256,
    string AttemptInventorySha256,
    IReadOnlyList<StagedResponseRecord> Records);

internal sealed record PendingCaptureIntent(
    string AttemptSha256,
    SourceRequestIdentity Request,
    BoundedResponseMetadata Response);

internal static class EvidenceJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] WritePlan(PrivateEvidenceAcquisitionPlan plan) =>
        Write(new PlanDocument(
            PrivateEvidenceBundle.PlanSchema,
            plan.RunIdentity,
            plan.CodeCommit,
            plan.Publisher,
            plan.BaselineCorpusSha256,
            plan.EnumerationScopeSha256,
            plan.EndpointPolicySha256,
            plan.AcquisitionPolicySha256,
            plan.BundleId));

    public static byte[] WriteReceipt(StagedResponseRecord record) =>
        Write(new ResponseReceiptDocument(
            PrivateEvidenceBundle.ResponseReceiptSchema,
            ToDocument(record)));

    public static byte[] WriteCaptureIntent(
        string attemptSha256,
        SourceRequestIdentity request,
        BoundedResponseMetadata response) => Write(new CaptureIntentDocument(
        PrivateEvidenceBundle.CaptureIntentSchema,
        CodeIdentity.RequireSha256(attemptSha256, nameof(attemptSha256)),
        request.RequestId + ".body",
        ToDocument(request),
        ToDocument(response)));

    public static byte[] WriteCaptureOutcome(StagedResponseEvidence evidence) =>
        Write(new CaptureOutcomeDocument(
            PrivateEvidenceBundle.CaptureOutcomeSchema,
            ToDocument(evidence)));

    public static byte[] WriteManifest(
        PrivateEvidenceAcquisitionPlan plan,
        IReadOnlyList<PrivateEvidenceAttemptRecord> attempts,
        IReadOnlyList<StagedResponseRecord> records) => Write(
        new ManifestDocument(
            PrivateEvidenceBundle.ManifestSchema,
            plan.BundleId,
            HashAttemptChain(plan, attempts),
            HashAttemptInventory(plan, attempts),
            records.Select(ToDocument).ToArray()));

    public static byte[] WriteCommit(
        PrivateEvidenceAcquisitionPlan plan,
        byte[] manifestBytes,
        IReadOnlyList<PrivateEvidenceAttemptRecord> attempts) => Write(
        new CommitDocument(
            PrivateEvidenceBundle.CommitMarkerSchema,
            plan.BundleId,
            Sha256(manifestBytes),
            HashAttemptChain(plan, attempts),
            HashAttemptInventory(plan, attempts)));

    public static string HashAttemptStart(
        PrivateEvidenceAcquisitionPlan plan,
        string? predecessorAttemptSha256,
        SourceRequestIdentity request) => Sha256(Write(
        new AttemptChainDocument(
            PrivateEvidenceBundle.AttemptChainSchema,
            plan.BundleId,
            predecessorAttemptSha256,
            ToDocument(request))));

    public static byte[] WriteAttemptStart(
        PrivateEvidenceAcquisitionPlan plan,
        PrivateEvidenceAttemptState attempt) => Write(
        new AttemptStartDocument(
            PrivateEvidenceBundle.AttemptStartSchema,
            plan.BundleId,
            attempt.PredecessorAttemptSha256,
            attempt.AttemptSha256,
            ToDocument(attempt.Request)));

    public static PrivateEvidenceAttemptState ParseAttemptStart(
        byte[] bytes, PrivateEvidenceAcquisitionPlan plan)
    {
        var document = Deserialize<AttemptStartDocument>(bytes, "attempt start");
        if (document.Schema != PrivateEvidenceBundle.AttemptStartSchema
            || document.BundleId != plan.BundleId
            || document.Request is null)
            throw new InvalidDataException(
                "Private evidence attempt start schema or bundle is invalid.");
        var request = Restore(document.Request);
        var expectedSha256 = HashAttemptStart(
            plan, document.PredecessorAttemptSha256, request);
        var attempt = new PrivateEvidenceAttemptState(
            request,
            document.PredecessorAttemptSha256,
            CodeIdentity.RequireSha256(
                document.AttemptSha256, "Attempt SHA-256"));
        if (attempt.AttemptSha256 != expectedSha256
            || !bytes.AsSpan().SequenceEqual(WriteAttemptStart(plan, attempt)))
            throw new InvalidDataException(
                "Private evidence attempt start is not canonical or self-consistent.");
        return attempt;
    }

    public static PrivateEvidenceAttemptRecord CreateAttemptTerminal(
        PrivateEvidenceAcquisitionPlan plan,
        PrivateEvidenceAttemptState attempt,
        PrivateEvidenceAttemptDisposition disposition,
        string? responseReceiptSha256)
    {
        var payload = new AttemptTerminalPayloadDocument(
            PrivateEvidenceBundle.AttemptTerminalSchema,
            plan.BundleId,
            attempt.AttemptSha256,
            disposition,
            responseReceiptSha256);
        return new PrivateEvidenceAttemptRecord(
            attempt.Request,
            attempt.PredecessorAttemptSha256,
            attempt.AttemptSha256,
            disposition,
            responseReceiptSha256,
            Sha256(Write(payload)));
    }

    public static byte[] WriteAttemptTerminal(
        PrivateEvidenceAcquisitionPlan plan,
        PrivateEvidenceAttemptRecord attempt) => Write(
        new AttemptTerminalDocument(
            PrivateEvidenceBundle.AttemptTerminalSchema,
            plan.BundleId,
            attempt.AttemptSha256,
            attempt.Disposition,
            attempt.ResponseReceiptSha256,
            attempt.TerminalSha256));

    public static PrivateEvidenceAttemptRecord ParseAttemptTerminal(
        byte[] bytes,
        PrivateEvidenceAcquisitionPlan plan,
        PrivateEvidenceAttemptState attempt)
    {
        var document = Deserialize<AttemptTerminalDocument>(
            bytes, "attempt terminal");
        if (document.Schema != PrivateEvidenceBundle.AttemptTerminalSchema
            || document.BundleId != plan.BundleId
            || document.AttemptSha256 != attempt.AttemptSha256)
            throw new InvalidDataException(
                "Private evidence attempt terminal schema or identity is invalid.");
        var expected = CreateAttemptTerminal(
            plan,
            attempt,
            document.Disposition,
            document.ResponseReceiptSha256);
        if (document.TerminalSha256 != expected.TerminalSha256
            || !bytes.AsSpan().SequenceEqual(
                WriteAttemptTerminal(plan, expected)))
            throw new InvalidDataException(
                "Private evidence attempt terminal is not canonical or self-consistent.");
        return expected;
    }

    public static string HashAttemptChain(
        PrivateEvidenceAcquisitionPlan plan,
        IReadOnlyList<PrivateEvidenceAttemptRecord> attempts) => attempts.Count == 0
        ? Sha256(Encoding.UTF8.GetBytes(string.Join('\n',
            PrivateEvidenceBundle.AttemptChainSchema,
            plan.BundleId,
            "empty")))
        : attempts[^1].AttemptSha256;

    public static string HashAttemptInventory(
        PrivateEvidenceAcquisitionPlan plan,
        IReadOnlyList<PrivateEvidenceAttemptRecord> attempts) => Sha256(Write(
        new AttemptInventoryDocument(
            PrivateEvidenceBundle.AttemptInventorySchema,
            plan.BundleId,
            attempts.Select(attempt => new AttemptInventoryEntryDocument(
                attempt.Request.Ordinal,
                attempt.AttemptSha256,
                attempt.TerminalSha256,
                attempt.Disposition,
                attempt.ResponseReceiptSha256)).ToArray())));

    public static string Sha256(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    public static PrivateEvidenceAcquisitionPlan ParsePlan(byte[] bytes)
    {
        var document = Deserialize<PlanDocument>(bytes, "plan");
        if (document.Schema != PrivateEvidenceBundle.PlanSchema)
            throw new InvalidDataException(
                "Private evidence plan schema is invalid.");
        var plan = new PrivateEvidenceAcquisitionPlan(
            document.RunIdentity,
            document.CodeCommit,
            document.Publisher,
            document.BaselineCorpusSha256,
            document.EnumerationScopeSha256,
            document.EndpointPolicySha256,
            document.AcquisitionPolicySha256);
        if (plan.BundleId != document.BundleId
            || !bytes.AsSpan().SequenceEqual(WritePlan(plan)))
            throw new InvalidDataException(
                "Private evidence plan is not canonical or self-consistent.");
        return plan;
    }

    public static StagedResponseRecord ParseReceipt(byte[] bytes)
    {
        var document = Deserialize<ResponseReceiptDocument>(bytes, "receipt");
        if (document.Schema != PrivateEvidenceBundle.ResponseReceiptSchema
            || document.Record is null)
            throw new InvalidDataException(
                "Private evidence response receipt schema is invalid.");
        var record = Restore(document.Record);
        if (!bytes.AsSpan().SequenceEqual(WriteReceipt(record)))
            throw new InvalidDataException(
                "Private evidence response receipt is not canonical.");
        return record;
    }

    public static PendingCaptureIntent ParseCaptureIntent(byte[] bytes)
    {
        var document = Deserialize<CaptureIntentDocument>(bytes, "capture intent");
        if (document.Schema != PrivateEvidenceBundle.CaptureIntentSchema
            || !EvidenceFiles.IsSha256(document.AttemptSha256)
            || document.Request is null
            || document.Response is null)
            throw new InvalidDataException(
                "Private evidence capture intent schema is invalid.");
        var result = new PendingCaptureIntent(
            document.AttemptSha256,
            Restore(document.Request),
            Restore(document.Response));
        if (document.BodyFileName != result.Request.RequestId + ".body")
            throw new InvalidDataException(
                "Private evidence capture intent does not bind its body file.");
        if (!bytes.AsSpan().SequenceEqual(
                WriteCaptureIntent(
                    result.AttemptSha256, result.Request, result.Response)))
            throw new InvalidDataException(
                "Private evidence capture intent is not canonical.");
        return result;
    }

    public static StagedResponseEvidence ParseCaptureOutcome(
        byte[] bytes,
        SourceRequestIdentity request,
        BoundedResponseMetadata response)
    {
        var document = Deserialize<CaptureOutcomeDocument>(
            bytes, "capture outcome");
        if (document.Schema != PrivateEvidenceBundle.CaptureOutcomeSchema
            || document.Evidence is null)
            throw new InvalidDataException(
                "Private evidence capture outcome schema is invalid.");
        var evidence = Restore(
            document.Evidence, request, response, requireRetainedState: false);
        if (!bytes.AsSpan().SequenceEqual(WriteCaptureOutcome(evidence)))
            throw new InvalidDataException(
                "Private evidence capture outcome is not canonical.");
        return evidence;
    }

    public static ParsedManifest ParseManifest(byte[] bytes)
    {
        var document = Deserialize<ManifestDocument>(bytes, "manifest");
        if (document.Schema != PrivateEvidenceBundle.ManifestSchema
            || document.Records is null)
            throw new InvalidDataException(
                "Private evidence manifest schema is invalid.");
        var records = document.Records.Select(Restore)
            .OrderBy(record => record.Request.Ordinal)
            .ToArray();
        var canonical = Write(new ManifestDocument(
            document.Schema,
            document.BundleId,
            document.AttemptChainSha256,
            document.AttemptInventorySha256,
            records.Select(ToDocument).ToArray()));
        if (!bytes.AsSpan().SequenceEqual(canonical))
            throw new InvalidDataException(
                "Private evidence manifest is not canonical.");
        return new ParsedManifest(
            CodeIdentity.RequireSha256(document.BundleId, "Bundle ID"),
            CodeIdentity.RequireSha256(
                document.AttemptChainSha256, "Attempt chain SHA-256"),
            CodeIdentity.RequireSha256(
                document.AttemptInventorySha256,
                "Attempt inventory SHA-256"),
            records);
    }

    public static bool SameRecords(
        IReadOnlyList<StagedResponseRecord> left,
        IReadOnlyList<StagedResponseRecord> right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!WriteReceipt(left[index]).AsSpan()
                    .SequenceEqual(WriteReceipt(right[index])))
                return false;
        }
        return true;
    }

    private static byte[] Write<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        return [.. json, (byte)'\n'];
    }

    private static T Deserialize<T>(byte[] bytes, string description)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Options)
                   ?? throw new InvalidDataException(
                       $"Private evidence {description} is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Private evidence {description} is not strict JSON.", error);
        }
    }

    private static RequestDocument ToDocument(SourceRequestIdentity request) => new(
        request.RequestId,
        request.Publisher,
        request.Channel,
        request.Method,
        request.RequestUri,
        request.RequestUriSha256,
        request.RequestBodySha256,
        request.Ordinal,
        request.MaximumResponseBytes,
        request.PhysicalAttempt,
        request.RedirectHop);

    private static ResponseDocument ToDocument(BoundedResponseMetadata response) => new(
        response.StatusCode,
        response.ContentType,
        response.Charset,
        response.EntityTag,
        response.LastModified,
        response.FetchedAt,
        response.EffectiveSourceUri,
        response.EffectiveSourceUriSha256,
        response.BodyComplete);

    private static EvidenceDocument ToDocument(StagedResponseEvidence evidence) => new(
        evidence.Disposition,
        evidence.RequestId,
        evidence.ObjectSha256,
        evidence.ByteLength,
        evidence is RejectedStagedResponseEvidence rejected
            ? rejected.Reason
            : null);

    private static RecordDocument ToDocument(StagedResponseRecord record) => new(
        ToDocument(record.Request),
        ToDocument(record.Response),
        ToDocument(record.Evidence));

    private static SourceRequestIdentity Restore(RequestDocument document)
    {
        var request = SourceRequestIdentity.RestorePersisted(
            document.Publisher,
            document.Channel,
            document.Method,
            document.RequestUri,
            document.RequestUriSha256,
            document.RequestBodySha256,
            document.Ordinal,
            document.MaximumResponseBytes,
            document.PhysicalAttempt,
            document.RedirectHop);
        if (request.RequestId != document.RequestId)
            throw new InvalidDataException(
                "Persisted source request ID does not match its fields.");
        return request;
    }

    private static StagedResponseRecord Restore(RecordDocument document)
    {
        if (document.Request is null
            || document.Response is null
            || document.Evidence is null)
            throw new InvalidDataException(
                "Private evidence response record is incomplete.");
        var request = Restore(document.Request);
        var response = Restore(document.Response);
        var evidence = Restore(
            document.Evidence, request, response, requireRetainedState: true);
        return new StagedResponseRecord(request, response, evidence);
    }

    private static BoundedResponseMetadata Restore(ResponseDocument document) =>
        BoundedResponseMetadata.RestorePersisted(
            document.StatusCode,
            document.ContentType,
            document.Charset,
            document.EntityTag,
            document.LastModified,
            document.FetchedAt,
            document.EffectiveSourceUri,
            document.EffectiveSourceUriSha256,
            document.BodyComplete);

    private static StagedResponseEvidence Restore(
        EvidenceDocument document,
        SourceRequestIdentity request,
        BoundedResponseMetadata response,
        bool requireRetainedState)
    {
        if (document.RequestId != request.RequestId)
            throw new InvalidDataException(
                "Private evidence outcome request ID is inconsistent.");
        switch (document.Disposition)
        {
            case StagedResponseDisposition.Complete:
                if (document.RejectionReason is not null
                    || !response.BodyComplete
                    || document.ByteLength > request.MaximumResponseBytes)
                    throw new InvalidDataException(
                        "Complete private evidence outcome is inconsistent.");
                return new CompleteStagedResponseEvidence(
                    document.RequestId,
                    document.ObjectSha256,
                    document.ByteLength);
            case StagedResponseDisposition.Rejected:
                if (document.RejectionReason is null
                    || requireRetainedState && response.BodyComplete)
                    throw new InvalidDataException(
                        "Rejected private evidence outcome is inconsistent.");
                if (document.RejectionReason
                        == StagedResponseRejectionReason.BodyTooLarge
                    && document.ByteLength != request.MaximumResponseBytes + 1
                    || document.RejectionReason
                        != StagedResponseRejectionReason.BodyTooLarge
                    && document.ByteLength > request.MaximumResponseBytes)
                    throw new InvalidDataException(
                        "Rejected private evidence length is inconsistent.");
                return new RejectedStagedResponseEvidence(
                    document.RequestId,
                    document.ObjectSha256,
                    document.ByteLength,
                    document.RejectionReason.Value);
            default:
                throw new InvalidDataException(
                    "Private evidence outcome disposition is unsupported.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            WriteIndented = false,
            MaxDepth = 16,
        };
        options.Converters.Add(new JsonStringEnumConverter<SourceRequestMethod>(
            JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.Converters.Add(
            new JsonStringEnumConverter<StagedResponseDisposition>(
                JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.Converters.Add(
            new JsonStringEnumConverter<StagedResponseRejectionReason>(
                JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.Converters.Add(
            new JsonStringEnumConverter<PrivateEvidenceAttemptDisposition>(
                JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }
}
