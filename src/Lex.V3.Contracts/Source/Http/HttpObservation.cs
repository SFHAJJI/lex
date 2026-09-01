using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public enum HttpNoBodyReason
{
    [JsonStringEnumMemberName("semantic_no_entity")]
    SemanticNoEntity = 1,

    [JsonStringEnumMemberName("complete_zero_octet_entity")]
    CompleteZeroOctetEntity = 2,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HttpValidatorEvidence
{
    [JsonConstructor]
    public HttpValidatorEvidence(
        SourceRegistryMemberRef validatorKind,
        string requestHeaderName,
        string responseHeaderName,
        string value)
    {
        ValidatorKind = validatorKind ?? throw new ArgumentNullException(nameof(validatorKind));
        var admitted = validatorKind.MemberKey switch
        {
            "etag" => requestHeaderName == "If-None-Match" && responseHeaderName == "ETag",
            "last_modified" =>
                requestHeaderName == "If-Modified-Since" && responseHeaderName == "Last-Modified",
            _ => false,
        };
        if (!admitted)
        {
            throw new ArgumentException(
                "A validator kind must use its exact HTTP request and response header pair.",
                nameof(validatorKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = HttpResponseMetadata.RequireBoundedHeaderValue(value, nameof(value))!;
        RequestHeaderName = requestHeaderName;
        ResponseHeaderName = responseHeaderName;
    }

    public SourceRegistryMemberRef ValidatorKind { get; }

    public string RequestHeaderName { get; }

    public string ResponseHeaderName { get; }

    public string Value { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ResponseCompleteBodyObservation), HttpObservationWireKinds.ResponseCompleteBody)]
[JsonDerivedType(typeof(ResponsePartialBodyObservation), HttpObservationWireKinds.ResponsePartialBody)]
[JsonDerivedType(typeof(Revalidation304Observation), HttpObservationWireKinds.Revalidation304)]
[JsonDerivedType(typeof(ResponseWithoutBodyObservation), HttpObservationWireKinds.ResponseWithoutBody)]
[JsonDerivedType(typeof(TransportFailureBeforeBodyObservation), HttpObservationWireKinds.TransportFailureBeforeBody)]
[JsonDerivedType(typeof(PolicyRejectionObservation), HttpObservationWireKinds.PolicyRejection)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract class HttpObservation
{
    private protected HttpObservation(
        string schema,
        string observationId,
        HttpRequestEvidence request)
    {
        if (!string.Equals(schema, HttpObservationSchemaIds.HttpObservation, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"An HTTP observation must declare {HttpObservationSchemaIds.HttpObservation}.",
                nameof(schema));
        }

        Schema = schema;
        ObservationId = SourceCoreValidation.RequireUuidUrn(observationId, nameof(observationId));
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public string Schema { get; }

    public string ObservationId { get; }

    public HttpRequestEvidence Request { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract class HttpResponseObservation : HttpObservation
{
    private protected HttpResponseObservation(
        string schema,
        string observationId,
        HttpRequestEvidence request,
        string effectiveUri,
        int statusCode,
        HttpStatusDisposition statusDisposition,
        HttpResponseMetadata responseMetadata)
        : base(schema, observationId, request)
    {
        EffectiveUri = HttpRequestEvidence.RequireCanonicalRequestUri(effectiveUri, nameof(effectiveUri));
        var parsed = new Uri(EffectiveUri, UriKind.Absolute);
        _ = new HttpOrigin(parsed.Scheme, parsed.Host, parsed.Port);
        ResponseMetadata = responseMetadata
            ?? throw new ArgumentNullException(nameof(responseMetadata));

        var expectedDisposition = HttpStatusClassifier.Classify(statusCode, ResponseMetadata);
        if (!Enum.IsDefined(statusDisposition) || statusDisposition != expectedDisposition)
        {
            throw new ArgumentException(
                "The retained status disposition must equal the status and Content-Range evidence.",
                nameof(statusDisposition));
        }

        StatusCode = statusCode;
        StatusDisposition = statusDisposition;
    }

    public string EffectiveUri { get; }

    public int StatusCode { get; }

    public HttpStatusDisposition StatusDisposition { get; }

    public HttpResponseMetadata ResponseMetadata { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ResponseCompleteBodyObservation : HttpResponseObservation
{
    [JsonConstructor]
    public ResponseCompleteBodyObservation(
        string schema,
        string observationId,
        HttpRequestEvidence request,
        string effectiveUri,
        int statusCode,
        HttpStatusDisposition statusDisposition,
        HttpResponseMetadata responseMetadata,
        bool transferComplete,
        long receivedEncodedEntityByteCount,
        string transportByteSha256,
        DurableBlobRef durableBlobRef)
        : base(
            schema,
            observationId,
            request,
            effectiveUri,
            statusCode,
            statusDisposition,
            responseMetadata)
    {
        if (!transferComplete)
        {
            throw new ArgumentException("A complete-body observation must declare transfer_complete true.", nameof(transferComplete));
        }

        if (receivedEncodedEntityByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedEncodedEntityByteCount));
        }

        ArgumentNullException.ThrowIfNull(durableBlobRef);
        var digest = SourceCoreValidation.RequireSha256(transportByteSha256, nameof(transportByteSha256));
        RequireTransportBlob(durableBlobRef, receivedEncodedEntityByteCount, digest, nameof(durableBlobRef));
        if (responseMetadata.ContentLength is not AbsentHttpHeader &&
            (!responseMetadata.TryGetSingleContentLength(out var declaredLength) ||
             declaredLength != receivedEncodedEntityByteCount))
        {
            throw new ArgumentException(
                "A declared Content-Length mismatch is partial transport evidence, not a complete body.",
                nameof(responseMetadata));
        }

        if (statusCode is 204 or 205 or 304)
        {
            throw new ArgumentException(
                "A semantic no-body or 304 response cannot be represented as a complete body.",
                nameof(statusCode));
        }

        TransferComplete = transferComplete;
        ReceivedEncodedEntityByteCount = receivedEncodedEntityByteCount;
        TransportByteSha256 = digest;
        DurableBlobRef = durableBlobRef;
    }

    public bool TransferComplete { get; }

    public long ReceivedEncodedEntityByteCount { get; }

    public string TransportByteSha256 { get; }

    public DurableBlobRef DurableBlobRef { get; }

    internal static void RequireTransportBlob(
        DurableBlobRef blob,
        long byteCount,
        string digest,
        string parameterName)
    {
        if (blob.CustodyClass != CustodyClass.NightlyFloor90d ||
            blob.ByteLength != byteCount ||
            !string.Equals(blob.ContentSha256, digest, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Transport bytes must bind one nightly durable blob with the exact count and digest.",
                parameterName);
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ResponsePartialBodyObservation : HttpResponseObservation
{
    [JsonConstructor]
    public ResponsePartialBodyObservation(
        string schema,
        string observationId,
        HttpRequestEvidence request,
        string effectiveUri,
        int statusCode,
        HttpStatusDisposition statusDisposition,
        HttpResponseMetadata responseMetadata,
        long receivedEncodedEntityByteCount,
        SourceRegistryMemberRef terminalFailureReason,
        string? transportByteSha256,
        DurableBlobRef? durableBlobRef)
        : base(
            schema,
            observationId,
            request,
            effectiveUri,
            statusCode,
            statusDisposition,
            responseMetadata)
    {
        if (receivedEncodedEntityByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedEncodedEntityByteCount));
        }

        TerminalFailureReason = terminalFailureReason
            ?? throw new ArgumentNullException(nameof(terminalFailureReason));
        if (receivedEncodedEntityByteCount == 0)
        {
            if (transportByteSha256 is not null || durableBlobRef is not null)
            {
                throw new ArgumentException(
                    "A zero-octet partial transfer carries neither digest nor blob.",
                    nameof(transportByteSha256));
            }
        }
        else
        {
            if (transportByteSha256 is null || durableBlobRef is null)
            {
                throw new ArgumentException(
                    "A positive partial transfer must retain its exact digest and durable blob.",
                    nameof(transportByteSha256));
            }

            var digest = SourceCoreValidation.RequireSha256(transportByteSha256, nameof(transportByteSha256));
            ResponseCompleteBodyObservation.RequireTransportBlob(
                durableBlobRef,
                receivedEncodedEntityByteCount,
                digest,
                nameof(durableBlobRef));
        }

        ReceivedEncodedEntityByteCount = receivedEncodedEntityByteCount;
        TransportByteSha256 = transportByteSha256;
        DurableBlobRef = durableBlobRef;
    }

    public long ReceivedEncodedEntityByteCount { get; }

    public SourceRegistryMemberRef TerminalFailureReason { get; }

    public string? TransportByteSha256 { get; }

    public DurableBlobRef? DurableBlobRef { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class Revalidation304Observation : HttpResponseObservation
{
    [JsonConstructor]
    public Revalidation304Observation(
        string schema,
        string observationId,
        HttpRequestEvidence request,
        string effectiveUri,
        int statusCode,
        HttpStatusDisposition statusDisposition,
        HttpResponseMetadata responseMetadata,
        HttpValidatorEvidence sentValidator,
        HttpValidatorEvidence predecessorValidator,
        SourceArtifactRef predecessorObservationRef,
        SourceArtifactRef representationRequestKeyRef,
        DurableBlobRef predecessorBlobRef)
        : base(
            schema,
            observationId,
            request,
            effectiveUri,
            statusCode,
            statusDisposition,
            responseMetadata)
    {
        if (statusCode != 304 || statusDisposition != HttpStatusDisposition.RevalidationReferenceOnly)
        {
            throw new ArgumentException("A revalidation observation must be exact HTTP 304.", nameof(statusCode));
        }

        if (request.Method != HttpRequestMethod.Get)
        {
            throw new ArgumentException("A 304 predecessor reference is admitted only for GET.", nameof(request));
        }

        if (responseMetadata.HasContentRange)
        {
            throw new ArgumentException("A 304 carries no new entity representation.", nameof(responseMetadata));
        }

        SentValidator = sentValidator ?? throw new ArgumentNullException(nameof(sentValidator));
        PredecessorValidator = predecessorValidator
            ?? throw new ArgumentNullException(nameof(predecessorValidator));
        if (SentValidator != PredecessorValidator)
        {
            throw new ArgumentException(
                "The sent validator must exactly match the stored predecessor validator.",
                nameof(predecessorValidator));
        }

        PredecessorObservationRef = predecessorObservationRef
            ?? throw new ArgumentNullException(nameof(predecessorObservationRef));
        RepresentationRequestKeyRef = representationRequestKeyRef
            ?? throw new ArgumentNullException(nameof(representationRequestKeyRef));
        PredecessorBlobRef = predecessorBlobRef
            ?? throw new ArgumentNullException(nameof(predecessorBlobRef));
        if (PredecessorBlobRef.ByteLength <= 0 ||
            PredecessorBlobRef.CustodyClass != CustodyClass.NightlyFloor90d)
        {
            throw new ArgumentException(
                "A 304 may refer only to a nonempty durable transport blob.",
                nameof(predecessorBlobRef));
        }
    }

    public HttpValidatorEvidence SentValidator { get; }

    public HttpValidatorEvidence PredecessorValidator { get; }

    public SourceArtifactRef PredecessorObservationRef { get; }

    public SourceArtifactRef RepresentationRequestKeyRef { get; }

    public DurableBlobRef PredecessorBlobRef { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ResponseWithoutBodyObservation : HttpResponseObservation
{
    [JsonConstructor]
    public ResponseWithoutBodyObservation(
        string schema,
        string observationId,
        HttpRequestEvidence request,
        string effectiveUri,
        int statusCode,
        HttpStatusDisposition statusDisposition,
        HttpResponseMetadata responseMetadata,
        long receivedEncodedEntityByteCount,
        HttpNoBodyReason reason)
        : base(
            schema,
            observationId,
            request,
            effectiveUri,
            statusCode,
            statusDisposition,
            responseMetadata)
    {
        if (receivedEncodedEntityByteCount != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedEncodedEntityByteCount));
        }

        if (!Enum.IsDefined(reason) || statusCode == 304)
        {
            throw new ArgumentException("A no-body observation must carry one admitted non-304 reason.", nameof(reason));
        }

        var semantic = statusCode is 204 or 205;
        if (semantic != (reason == HttpNoBodyReason.SemanticNoEntity))
        {
            throw new ArgumentException(
                "The no-body reason must distinguish semantic no-entity from a clean zero-octet entity.",
                nameof(reason));
        }

        if (responseMetadata.ContentLength is not AbsentHttpHeader &&
            (!responseMetadata.TryGetSingleContentLength(out var declaredLength) || declaredLength > 0))
        {
            throw new ArgumentException(
                "A positive declared length with zero received octets is partial evidence.",
                nameof(responseMetadata));
        }

        ReceivedEncodedEntityByteCount = receivedEncodedEntityByteCount;
        Reason = reason;
    }

    public long ReceivedEncodedEntityByteCount { get; }

    public HttpNoBodyReason Reason { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TransportFailureBeforeBodyObservation : HttpObservation
{
    [JsonConstructor]
    public TransportFailureBeforeBodyObservation(
        string schema,
        string observationId,
        HttpRequestEvidence request,
        SourceRegistryMemberRef failureClass,
        int elapsedMilliseconds)
        : base(schema, observationId, request)
    {
        FailureClass = failureClass ?? throw new ArgumentNullException(nameof(failureClass));
        if (elapsedMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
        }

        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public SourceRegistryMemberRef FailureClass { get; }

    public int ElapsedMilliseconds { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PolicyRejectionObservation : HttpObservation
{
    [JsonConstructor]
    public PolicyRejectionObservation(
        string schema,
        string observationId,
        HttpRequestEvidence request,
        SourceRegistryMemberRef rejectionReason,
        SourceRegistryMemberRef rejectedStage,
        SourceArtifactRef zeroRequestProofRef)
        : base(schema, observationId, request)
    {
        RejectionReason = rejectionReason ?? throw new ArgumentNullException(nameof(rejectionReason));
        RejectedStage = rejectedStage ?? throw new ArgumentNullException(nameof(rejectedStage));
        ZeroRequestProofRef = zeroRequestProofRef
            ?? throw new ArgumentNullException(nameof(zeroRequestProofRef));
    }

    public SourceRegistryMemberRef RejectionReason { get; }

    public SourceRegistryMemberRef RejectedStage { get; }

    public SourceArtifactRef ZeroRequestProofRef { get; }
}
