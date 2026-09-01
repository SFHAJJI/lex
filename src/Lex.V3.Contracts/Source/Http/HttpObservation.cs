using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public enum HttpNoBodyReason
{
    [JsonStringEnumMemberName("semantic_no_entity")]
    SemanticNoEntity = 1,

    [JsonStringEnumMemberName("complete_zero_octet_entity")]
    CompleteZeroOctetEntity = 2,

    [JsonStringEnumMemberName("framing_forbids_body")]
    FramingForbidsBody = 3,
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
[JsonDerivedType(typeof(ResponseCompletionUnprovenObservation), HttpObservationWireKinds.ResponseCompletionUnproven)]
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
        TransferCompletionEvidence transferCompletionEvidence,
        DurableBlobWriteReceipt durableWriteReceipt)
        : base(
            schema,
            observationId,
            request,
            effectiveUri,
            statusCode,
            statusDisposition,
            responseMetadata)
    {
        TransferCompletionEvidence = transferCompletionEvidence
            ?? throw new ArgumentNullException(nameof(transferCompletionEvidence));
        DurableWriteReceipt = durableWriteReceipt
            ?? throw new ArgumentNullException(nameof(durableWriteReceipt));
        var durableBlobRef = DurableWriteReceipt.Reference;
        RequireTransportBlob(
            durableBlobRef,
            TransferCompletionEvidence.TransportByteLength,
            TransferCompletionEvidence.TransportByteSha256,
            nameof(durableWriteReceipt));
        if (TransferCompletionEvidence.AdapterExecutionIdentity != request.AdapterIdentity ||
            !string.Equals(
                TransferCompletionEvidence.ResponseObservationId,
                observationId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Transfer completion must bind this exact adapter execution and response.",
                nameof(transferCompletionEvidence));
        }

        if (responseMetadata.TryGetSingleContentLength(out var retainedContentLength) &&
            retainedContentLength != TransferCompletionEvidence.TransportByteLength)
        {
            throw new ArgumentException(
                "A retained valid Content-Length must equal the completed transport-byte length.",
                nameof(transferCompletionEvidence));
        }

        if (TransferCompletionEvidence is DeclaredContentLengthCompleteEvidence &&
            (responseMetadata.HasTransferEncoding ||
             !responseMetadata.TryGetSingleContentLength(out var declaredLength) ||
             declaredLength != TransferCompletionEvidence.TransportByteLength))
        {
            throw new ArgumentException(
                "Declared-length completion requires one matching Content-Length without Transfer-Encoding.",
                nameof(transferCompletionEvidence));
        }

        if (HttpResponseFraming.IsHeaderTerminatedStatus(statusCode))
        {
            throw new ArgumentException(
                "A header-terminated response cannot be represented as a complete body.",
                nameof(statusCode));
        }

    }

    public TransferCompletionEvidence TransferCompletionEvidence { get; }

    public DurableBlobWriteReceipt DurableWriteReceipt { get; }

    [JsonIgnore]
    public long ReceivedEncodedEntityByteCount => DurableWriteReceipt.Reference.ByteLength;

    [JsonIgnore]
    public string TransportByteSha256 => DurableWriteReceipt.Reference.ContentSha256;

    [JsonIgnore]
    public DurableBlobRef DurableBlobRef => DurableWriteReceipt.Reference;

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
        long admittedEncodedEntityByteLimit,
        SourceRegistryMemberRef terminalFailureReason,
        DurableBlobWriteReceipt? durableWriteReceipt)
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

        if (admittedEncodedEntityByteLimit is <= 0 or > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(admittedEncodedEntityByteLimit));
        }

        if (receivedEncodedEntityByteCount > admittedEncodedEntityByteLimit)
        {
            throw new ArgumentException(
                "A body observation cannot retain more bytes than its admitted limit.",
                nameof(receivedEncodedEntityByteCount));
        }

        if (HttpResponseFraming.IsHeaderTerminatedStatus(statusCode))
        {
            throw new ArgumentException(
                "A header-terminated response cannot be represented as a partial body.",
                nameof(statusCode));
        }

        TerminalFailureReason = terminalFailureReason
            ?? throw new ArgumentNullException(nameof(terminalFailureReason));
        var reason = HttpAcquisitionReasonRegistry.RequirePartial(TerminalFailureReason);
        long? validDeclaredLength = !responseMetadata.HasTransferEncoding &&
            responseMetadata.TryGetSingleContentLength(out var retainedDeclaredLength)
                ? retainedDeclaredLength
                : null;
        if (validDeclaredLength is long declaredLength &&
            declaredLength <= receivedEncodedEntityByteCount)
        {
            throw new ArgumentException(
                "A partial body must contain fewer bytes than its valid declared length.",
                nameof(terminalFailureReason));
        }

        if (receivedEncodedEntityByteCount == admittedEncodedEntityByteLimit &&
            reason != HttpPartialBodyReason.ByteBoundPreventedCompletion)
        {
            throw new ArgumentException(
                "Reaching the admitted byte limit outranks an unobserved terminal cause.",
                nameof(terminalFailureReason));
        }

        switch (reason)
        {
            case HttpPartialBodyReason.DeclaredLengthShortRead:
                if (responseMetadata.HasTransferEncoding ||
                    !responseMetadata.TryGetSingleContentLength(out var shortReadLength) ||
                    shortReadLength <= receivedEncodedEntityByteCount)
                {
                    throw new ArgumentException(
                        "A declared-length short read requires one retained length greater than the received count.",
                        nameof(terminalFailureReason));
                }
                break;

            case HttpPartialBodyReason.ByteBoundPreventedCompletion:
                if (receivedEncodedEntityByteCount != admittedEncodedEntityByteLimit)
                {
                    throw new ArgumentException(
                        "A byte-bound outcome must retain exactly the admitted prefix.",
                        nameof(terminalFailureReason));
                }
                break;
        }
        if (receivedEncodedEntityByteCount == 0)
        {
            if (durableWriteReceipt is not null)
            {
                throw new ArgumentException(
                    "A zero-octet partial transfer carries no durable write receipt.",
                    nameof(durableWriteReceipt));
            }
        }
        else
        {
            if (durableWriteReceipt is null)
            {
                throw new ArgumentException(
                    "A positive partial transfer must retain its durable write receipt.",
                    nameof(durableWriteReceipt));
            }

            ResponseCompleteBodyObservation.RequireTransportBlob(
                durableWriteReceipt.Reference,
                receivedEncodedEntityByteCount,
                durableWriteReceipt.Reference.ContentSha256,
                nameof(durableWriteReceipt));
        }

        ReceivedEncodedEntityByteCount = receivedEncodedEntityByteCount;
        AdmittedEncodedEntityByteLimit = admittedEncodedEntityByteLimit;
        DurableWriteReceipt = durableWriteReceipt;
    }

    public long ReceivedEncodedEntityByteCount { get; }

    public long AdmittedEncodedEntityByteLimit { get; }

    public SourceRegistryMemberRef TerminalFailureReason { get; }

    public DurableBlobWriteReceipt? DurableWriteReceipt { get; }

    [JsonIgnore]
    public string? TransportByteSha256 => DurableWriteReceipt?.Reference.ContentSha256;

    [JsonIgnore]
    public DurableBlobRef? DurableBlobRef => DurableWriteReceipt?.Reference;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ResponseCompletionUnprovenObservation : HttpResponseObservation
{
    [JsonConstructor]
    public ResponseCompletionUnprovenObservation(
        string schema,
        string observationId,
        HttpRequestEvidence request,
        string effectiveUri,
        int statusCode,
        HttpStatusDisposition statusDisposition,
        HttpResponseMetadata responseMetadata,
        long receivedEncodedEntityByteCount,
        long admittedEncodedEntityByteLimit,
        SourceRegistryMemberRef completionUnprovenReason,
        DurableBlobWriteReceipt? durableWriteReceipt)
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

        if (admittedEncodedEntityByteLimit is <= 0 or > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(admittedEncodedEntityByteLimit));
        }

        if (receivedEncodedEntityByteCount > admittedEncodedEntityByteLimit)
        {
            throw new ArgumentException(
                "A body observation cannot retain more bytes than its admitted limit.",
                nameof(receivedEncodedEntityByteCount));
        }

        if (HttpResponseFraming.IsHeaderTerminatedStatus(statusCode))
        {
            throw new ArgumentException(
                "A header-terminated response cannot have unproven body completion.",
                nameof(statusCode));
        }

        CompletionUnprovenReason = completionUnprovenReason
            ?? throw new ArgumentNullException(nameof(completionUnprovenReason));
        var reason = HttpAcquisitionReasonRegistry.RequireCompletionUnproven(
            CompletionUnprovenReason);
        if (receivedEncodedEntityByteCount == admittedEncodedEntityByteLimit)
        {
            throw new ArgumentException(
                "Reaching the admitted byte limit is a bounded incomplete transfer, not an unproven completion.",
                nameof(receivedEncodedEntityByteCount));
        }

        switch (reason)
        {
            case HttpCompletionUnprovenReason.TransferCodingConflict:
                if (!responseMetadata.HasTransferEncoding || !responseMetadata.HasContentLength)
                {
                    throw new ArgumentException(
                        "A transfer-coding conflict requires retained Transfer-Encoding and Content-Length evidence.",
                        nameof(completionUnprovenReason));
                }
                break;

            case HttpCompletionUnprovenReason.MissingCompletionProof:
                if (responseMetadata.HasTransferEncoding && responseMetadata.HasContentLength ||
                    !responseMetadata.HasTransferEncoding &&
                    responseMetadata.TryGetSingleContentLength(out _))
                {
                    throw new ArgumentException(
                        "Missing completion proof requires framing that is neither a valid declared length nor a coding conflict.",
                        nameof(completionUnprovenReason));
                }
                break;
        }

        if (receivedEncodedEntityByteCount == 0)
        {
            if (durableWriteReceipt is not null)
            {
                throw new ArgumentException(
                    "A zero-octet completion-unproven response carries no durable write receipt.",
                    nameof(durableWriteReceipt));
            }
        }
        else
        {
            if (durableWriteReceipt is null)
            {
                throw new ArgumentException(
                    "A positive completion-unproven response must retain its durable write receipt.",
                    nameof(durableWriteReceipt));
            }

            ResponseCompleteBodyObservation.RequireTransportBlob(
                durableWriteReceipt.Reference,
                receivedEncodedEntityByteCount,
                durableWriteReceipt.Reference.ContentSha256,
                nameof(durableWriteReceipt));
        }

        ReceivedEncodedEntityByteCount = receivedEncodedEntityByteCount;
        AdmittedEncodedEntityByteLimit = admittedEncodedEntityByteLimit;
        DurableWriteReceipt = durableWriteReceipt;
    }

    public long ReceivedEncodedEntityByteCount { get; }

    public long AdmittedEncodedEntityByteLimit { get; }

    public SourceRegistryMemberRef CompletionUnprovenReason { get; }

    public DurableBlobWriteReceipt? DurableWriteReceipt { get; }

    [JsonIgnore]
    public string? TransportByteSha256 => DurableWriteReceipt?.Reference.ContentSha256;

    [JsonIgnore]
    public DurableBlobRef? DurableBlobRef => DurableWriteReceipt?.Reference;
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
        if (RepresentationRequestKeyRef != request.RepresentationRequestKeyIdentity)
        {
            throw new ArgumentException(
                "The 304 representation key must equal the current request key.",
                nameof(representationRequestKeyRef));
        }

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

    public AdmittedRevalidation304Observation AdmitAgainst(
        ResponseCompleteBodyObservation predecessor)
    {
        ArgumentNullException.ThrowIfNull(predecessor);
        if (predecessor.StatusCode != 200 ||
            predecessor.StatusDisposition != HttpStatusDisposition.DerivableStatus ||
            predecessor.ResponseMetadata.BlocksDerivation ||
            predecessor.Request.Method != HttpRequestMethod.Get ||
            !string.Equals(
                predecessor.Request.RequestedUri,
                Request.RequestedUri,
                StringComparison.Ordinal) ||
            predecessor.Request.RepresentationRequestKeyIdentity != RepresentationRequestKeyRef ||
            !string.Equals(predecessor.EffectiveUri, EffectiveUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The predecessor must be one derivable HTTP-200 GET representation of the same effective resource.",
                nameof(predecessor));
        }

        var predecessorHeader = PredecessorValidator.ValidatorKind.MemberKey switch
        {
            "etag" => predecessor.ResponseMetadata.Etag,
            "last_modified" => predecessor.ResponseMetadata.LastModified,
            _ => throw new ArgumentException(
                "The predecessor validator kind is not admitted.",
                nameof(predecessor)),
        };
        if (predecessorHeader is not SingleHttpHeader singleHeader ||
            !string.Equals(singleHeader.Value, PredecessorValidator.Value, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The predecessor response must retain the exact single validator that was sent.",
                nameof(predecessor));
        }

        var responseValidatorHeader = PredecessorValidator.ValidatorKind.MemberKey switch
        {
            "etag" => ResponseMetadata.Etag,
            "last_modified" => ResponseMetadata.LastModified,
            _ => throw new ArgumentException(
                "The revalidation validator kind is not admitted.",
                nameof(predecessor)),
        };
        if (responseValidatorHeader is SingleHttpHeader responseValidator &&
            !string.Equals(
                responseValidator.Value,
                PredecessorValidator.Value,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A retained 304 validator must exactly match the predecessor validator.",
                nameof(predecessor));
        }

        if (ResponseMetadata.HasMultipleField ||
            ResponseMetadata.ContentLength is not AbsentHttpHeader &&
            (!ResponseMetadata.TryGetSingleContentLength(out var retainedLength) ||
             retainedLength != predecessor.DurableBlobRef.ByteLength))
        {
            throw new ArgumentException(
                "A 304 may be admitted only with unambiguous metadata and a retained Content-Length matching the predecessor.",
                nameof(predecessor));
        }

        if (PredecessorObservationRef != HttpObservationIdentity.Create(predecessor) ||
            PredecessorBlobRef != predecessor.DurableBlobRef)
        {
            throw new ArgumentException(
                "The predecessor reference and blob must name the exact checked complete observation.",
                nameof(predecessor));
        }

        return new AdmittedRevalidation304Observation(this);
    }
}

/// <summary>
/// A 304 observation that has been checked against the exact complete predecessor it references.
/// Raw deserialization yields only <see cref="Revalidation304Observation"/> and cannot construct
/// this admission token.
/// </summary>
public sealed class AdmittedRevalidation304Observation
{
    internal AdmittedRevalidation304Observation(Revalidation304Observation observation)
    {
        Observation = observation;
    }

    public Revalidation304Observation Observation { get; }
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
        HttpNoBodyReason reason,
        ZeroOctetTransferCompletionEvidence? zeroOctetCompletionEvidence)
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

        var expectedReason = statusCode switch
        {
            >= 100 and <= 199 or 204 => HttpNoBodyReason.FramingForbidsBody,
            205 => HttpNoBodyReason.SemanticNoEntity,
            _ => HttpNoBodyReason.CompleteZeroOctetEntity,
        };
        if (reason != expectedReason)
        {
            throw new ArgumentException(
                "The no-body reason must match header framing, semantic no-entity or a complete zero-octet entity.",
                nameof(reason));
        }

        if (reason == HttpNoBodyReason.CompleteZeroOctetEntity)
        {
            ZeroOctetCompletionEvidence = zeroOctetCompletionEvidence
                ?? throw new ArgumentNullException(nameof(zeroOctetCompletionEvidence));
            if (ZeroOctetCompletionEvidence.AdapterExecutionIdentity != request.AdapterIdentity ||
                !string.Equals(
                    ZeroOctetCompletionEvidence.ResponseObservationId,
                    observationId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Zero-octet completion must bind this exact adapter execution and response.",
                    nameof(zeroOctetCompletionEvidence));
            }

            if (ZeroOctetCompletionEvidence is DeclaredZeroOctetContentLengthCompleteEvidence &&
                (responseMetadata.HasTransferEncoding ||
                 !responseMetadata.TryGetSingleContentLength(out var declaredZeroLength) ||
                 declaredZeroLength != 0))
            {
                throw new ArgumentException(
                    "Declared zero-octet completion requires one exact Content-Length of zero.",
                    nameof(zeroOctetCompletionEvidence));
            }
        }
        else
        {
            if (reason == HttpNoBodyReason.SemanticNoEntity &&
                (responseMetadata.HasTransferEncoding ||
                 !responseMetadata.TryGetSingleContentLength(out var semanticZeroLength) ||
                 semanticZeroLength != 0))
            {
                throw new ArgumentException(
                    "A semantic no-entity response requires one exact Content-Length of zero.",
                    nameof(responseMetadata));
            }

            if (zeroOctetCompletionEvidence is not null)
            {
                throw new ArgumentException(
                    "A framing or semantic no-body response carries no zero-octet transfer proof.",
                    nameof(zeroOctetCompletionEvidence));
            }

            ZeroOctetCompletionEvidence = null;
        }

        if (reason != HttpNoBodyReason.FramingForbidsBody &&
            responseMetadata.ContentLength is not AbsentHttpHeader &&
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

    public ZeroOctetTransferCompletionEvidence? ZeroOctetCompletionEvidence { get; }
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
        _ = HttpAcquisitionReasonRegistry.RequireBeforeHeaders(FailureClass);
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
