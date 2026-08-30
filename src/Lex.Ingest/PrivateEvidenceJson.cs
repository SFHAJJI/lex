using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.Law;
using Lex.Temporal;

namespace Lex.Ingest;

internal sealed record AcquisitionPlanDocument(
    string Schema,
    IReadOnlyList<RequestDocument> Requests);

internal sealed record PlanDocument(
    string Schema,
    string RunIdentity,
    string CodeCommit,
    string Publisher,
    string BaselineCorpusSha256,
    string EnumerationScopeSha256,
    string EndpointPolicySha256,
    string AcquisitionPlanSha256,
    string BundleId,
    IReadOnlyList<RequestDocument> Requests);

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
    string BodyFileName,
    RequestDocument Request,
    ResponseDocument Response);

internal sealed record CaptureOutcomeDocument(
    string Schema,
    EvidenceDocument Evidence);

internal sealed record ManifestDocument(
    string Schema,
    string BundleId,
    string AcquisitionPlanSha256,
    IReadOnlyList<RecordDocument> Records);

internal sealed record CommitDocument(
    string Schema,
    string BundleId,
    string AcquisitionPlanSha256,
    string ManifestSha256,
    string InventorySha256);

internal sealed record ParsedManifest(
    string BundleId,
    string AcquisitionPlanSha256,
    IReadOnlyList<StagedResponseRecord> Records);

internal sealed record PendingCaptureIntent(
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
            plan.AcquisitionPlanSha256,
            plan.BundleId,
            plan.Requests.Select(ToDocument).ToArray()));

    public static byte[] WriteReceipt(StagedResponseRecord record) =>
        Write(new ResponseReceiptDocument(
            PrivateEvidenceBundle.ResponseReceiptSchema,
            ToDocument(record)));

    public static byte[] WriteCaptureIntent(
        SourceRequestIdentity request,
        BoundedResponseMetadata response) => Write(new CaptureIntentDocument(
        PrivateEvidenceBundle.CaptureIntentSchema,
        request.RequestId + ".body",
        ToDocument(request),
        ToDocument(response)));

    public static byte[] WriteCaptureOutcome(StagedResponseEvidence evidence) =>
        Write(new CaptureOutcomeDocument(
            PrivateEvidenceBundle.CaptureOutcomeSchema,
            ToDocument(evidence)));

    public static byte[] WriteManifest(
        PrivateEvidenceAcquisitionPlan plan,
        IReadOnlyList<StagedResponseRecord> records) => Write(
        new ManifestDocument(
            PrivateEvidenceBundle.ManifestSchema,
            plan.BundleId,
            plan.AcquisitionPlanSha256,
            records.Select(ToDocument).ToArray()));

    public static byte[] WriteCommit(
        PrivateEvidenceAcquisitionPlan plan,
        byte[] manifestBytes,
        IReadOnlyList<StagedResponseRecord> records) => Write(
        new CommitDocument(
            PrivateEvidenceBundle.CommitMarkerSchema,
            plan.BundleId,
            plan.AcquisitionPlanSha256,
            Sha256(manifestBytes),
            HashInventory(records)));

    public static string HashAcquisitionPlan(
        IReadOnlyList<SourceRequestIdentity> requests) => Sha256(Write(
        new AcquisitionPlanDocument(
            PrivateEvidenceBundle.AcquisitionPlanSchema,
            requests.Select(ToDocument).ToArray())));

    public static string Sha256(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    public static PrivateEvidenceAcquisitionPlan ParsePlan(byte[] bytes)
    {
        var document = Deserialize<PlanDocument>(bytes, "plan");
        if (document.Requests is null
            || document.Schema != PrivateEvidenceBundle.PlanSchema)
            throw new InvalidDataException(
                "Private evidence plan schema or request inventory is invalid.");
        var requests = document.Requests.Select(Restore).ToArray();
        var plan = new PrivateEvidenceAcquisitionPlan(
            document.RunIdentity,
            document.CodeCommit,
            document.Publisher,
            document.BaselineCorpusSha256,
            document.EnumerationScopeSha256,
            document.EndpointPolicySha256,
            requests);
        if (plan.AcquisitionPlanSha256 != document.AcquisitionPlanSha256
            || plan.BundleId != document.BundleId
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
            || document.Request is null
            || document.Response is null)
            throw new InvalidDataException(
                "Private evidence capture intent schema is invalid.");
        var result = new PendingCaptureIntent(
            Restore(document.Request), Restore(document.Response));
        if (document.BodyFileName != result.Request.RequestId + ".body")
            throw new InvalidDataException(
                "Private evidence capture intent does not bind its body file.");
        if (!bytes.AsSpan().SequenceEqual(
                WriteCaptureIntent(result.Request, result.Response)))
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
            document.AcquisitionPlanSha256,
            records.Select(ToDocument).ToArray()));
        if (!bytes.AsSpan().SequenceEqual(canonical))
            throw new InvalidDataException(
                "Private evidence manifest is not canonical.");
        return new ParsedManifest(
            CodeIdentity.RequireSha256(document.BundleId, "Bundle ID"),
            CodeIdentity.RequireSha256(
                document.AcquisitionPlanSha256, "Acquisition plan SHA-256"),
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

    private static string HashInventory(
        IReadOnlyList<StagedResponseRecord> records)
    {
        var canonical = string.Join('\n', records.Select(record => string.Join(':',
            record.Request.RequestId,
            record.Evidence.Disposition.ToString(),
            record.Evidence.ObjectSha256,
            record.Evidence.ByteLength.ToString(CultureInfo.InvariantCulture))));
        return Sha256(Encoding.UTF8.GetBytes(canonical));
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
        return options;
    }
}
