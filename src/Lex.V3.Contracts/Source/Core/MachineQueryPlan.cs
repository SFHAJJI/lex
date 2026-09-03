using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Core;

public enum HttpRequestMethod
{
    [JsonStringEnumMemberName("GET")]
    Get = 1,

    [JsonStringEnumMemberName("POST")]
    Post = 2,
}

public enum MachineQueryCharset
{
    [JsonStringEnumMemberName("utf-8")]
    Utf8 = 1,
}

public enum MachineQueryInputMode
{
    [JsonStringEnumMemberName("renderer_with_ordered_inputs")]
    RendererInputs = 1,
}

public enum MachineQueryParameterKind
{
    [JsonStringEnumMemberName("bounded_integer")]
    BoundedInteger = 1,

    [JsonStringEnumMemberName("publisher_cursor")]
    PublisherCursor = 2,

    [JsonStringEnumMemberName("publisher_literal")]
    PublisherLiteral = 3,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MachineQueryParameter
{
    [JsonConstructor]
    public MachineQueryParameter(
        string name,
        MachineQueryParameterKind kind,
        long? integerValue,
        string? textValue,
        SourceArtifactRef provenanceRef)
    {
        Name = MachineQueryValidation.RequireMachineMemberKey(name, nameof(name));
        Kind = SourceCoreValidation.RequireDefined(kind, nameof(kind));
        IntegerValue = integerValue;
        TextValue = textValue;
        ProvenanceRef = provenanceRef ?? throw new ArgumentNullException(nameof(provenanceRef));

        if (Kind == MachineQueryParameterKind.BoundedInteger &&
            (IntegerValue is null or < 0 || TextValue is not null))
        {
            throw new ArgumentException(
                "A bounded-integer query parameter carries one nonnegative integer.",
                nameof(integerValue));
        }

        if (Kind == MachineQueryParameterKind.PublisherCursor &&
            (IntegerValue is not null ||
             string.IsNullOrEmpty(TextValue) ||
             TextValue.Length > MachineQueryValidation.MaximumParameterTextLength ||
             TextValue.Any(static character => character is < '!' or > '~')))
        {
            throw new ArgumentException(
                "A publisher cursor carries one bounded printable-ASCII value.",
                nameof(textValue));
        }


        if (Kind == MachineQueryParameterKind.PublisherLiteral)
        {
            if (IntegerValue is not null)
            {
                throw new ArgumentException(
                    "A publisher literal carries text and no integer.",
                    nameof(integerValue));
            }

            _ = MachineQueryValidation.RequirePublisherLiteral(TextValue, nameof(textValue));
        }
    }

    public string Name { get; }

    public MachineQueryParameterKind Kind { get; }

    public long? IntegerValue { get; }

    public string? TextValue { get; }

    public SourceArtifactRef ProvenanceRef { get; }
}

public enum MachineResponseCardinalityKind
{
    [JsonStringEnumMemberName("opaque_body")]
    OpaqueBody = 1,

    [JsonStringEnumMemberName("bounded_row_set_page")]
    BoundedRowSetPage = 2,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MachineResponseCardinality
{
    [JsonConstructor]
    public MachineResponseCardinality(
        MachineResponseCardinalityKind kind,
        long? rowLimit,
        long? expectedPartitionRowCount,
        SourceArtifactRef? expectedPartitionRowCountEvidenceRef)
    {
        Kind = SourceCoreValidation.RequireDefined(kind, nameof(kind));
        RowLimit = rowLimit;
        ExpectedPartitionRowCount = expectedPartitionRowCount;
        ExpectedPartitionRowCountEvidenceRef = expectedPartitionRowCountEvidenceRef;

        if (Kind == MachineResponseCardinalityKind.OpaqueBody &&
            (RowLimit is not null ||
             ExpectedPartitionRowCount is not null ||
             ExpectedPartitionRowCountEvidenceRef is not null))
        {
            throw new ArgumentException(
                "An opaque response cannot declare row-set cardinality evidence.",
                nameof(rowLimit));
        }

        if (Kind == MachineResponseCardinalityKind.BoundedRowSetPage)
        {
            if (RowLimit is null)
            {
                throw new ArgumentException(
                    "A bounded row-set page must declare its request-time row limit.",
                    nameof(rowLimit));
            }

            if (RowLimit is <= 0 or > MachineQueryValidation.MaximumResponseRowLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(rowLimit));
            }

            if (ExpectedPartitionRowCount is null)
            {
                throw new ArgumentException(
                    "A bounded row-set page must declare the independent expected partition row count.",
                    nameof(expectedPartitionRowCount));
            }

            if (ExpectedPartitionRowCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedPartitionRowCount));
            }

            if (ExpectedPartitionRowCountEvidenceRef is null)
            {
                throw new ArgumentException(
                    "A bounded row-set page must reference retained independent count evidence.",
                    nameof(expectedPartitionRowCountEvidenceRef));
            }
        }
    }

    public MachineResponseCardinalityKind Kind { get; }

    public long? RowLimit { get; }

    public long? ExpectedPartitionRowCount { get; }

    public SourceArtifactRef? ExpectedPartitionRowCountEvidenceRef { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MachineQueryPlan
{
    public const string SchemaId = SourceCoreSchemaIds.MachineQueryPlan;

    [JsonConstructor]
    public MachineQueryPlan(
        string schema,
        SourceRegistryMemberRef queryFamilyRef,
        SourceArtifactRef rendererProfileRef,
        SourceArtifactRef rendererSourceRef,
        HttpRequestMethod method,
        string targetOriginAndPath,
        long expectedRequestTargetLength,
        string expectedRequestTargetSha256,
        MachineResponseCardinality responseCardinality,
        SourceRegistryMemberRef? contentType,
        MachineQueryCharset? charset,
        MachineQueryInputMode inputMode,
        SourceArtifactRef orderedParameterSet,
        SourceRegistryMemberRef partitionBinding,
        long? expectedRequestBodyLength,
        string? expectedRequestBodySha256)
    {
        if (!string.Equals(schema, SchemaId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"A machine query plan must declare {SchemaId}.", nameof(schema));
        }

        Schema = schema;
        QueryFamilyRef = MachineQueryValidation.RequireMachineRegistryMember(
            queryFamilyRef,
            nameof(queryFamilyRef));
        RendererProfileRef = rendererProfileRef
            ?? throw new ArgumentNullException(nameof(rendererProfileRef));
        RendererSourceRef = rendererSourceRef
            ?? throw new ArgumentNullException(nameof(rendererSourceRef));
        Method = SourceCoreValidation.RequireDefined(method, nameof(method));
        TargetOriginAndPath = MachineQueryValidation.RequireTargetOriginAndPath(
            targetOriginAndPath,
            nameof(targetOriginAndPath));
        if (expectedRequestTargetLength is < 1 or > MachineQueryValidation.MaximumRequestTargetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRequestTargetLength));
        }

        ExpectedRequestTargetLength = expectedRequestTargetLength;
        ExpectedRequestTargetSha256 = SourceCoreValidation.RequireSha256(
            expectedRequestTargetSha256,
            nameof(expectedRequestTargetSha256));
        ResponseCardinality = responseCardinality
            ?? throw new ArgumentNullException(nameof(responseCardinality));
        ContentType = contentType is null
            ? null
            : MachineQueryValidation.RequireMediaTypeRegistryMember(
                contentType,
                nameof(contentType));
        Charset = charset is null
            ? null
            : SourceCoreValidation.RequireDefined(charset.Value, nameof(charset));
        InputMode = SourceCoreValidation.RequireDefined(inputMode, nameof(inputMode));
        OrderedParameterSet = orderedParameterSet
            ?? throw new ArgumentNullException(nameof(orderedParameterSet));
        PartitionBinding = MachineQueryValidation.RequireMachineRegistryMember(
            partitionBinding,
            nameof(partitionBinding));

        if (PartitionBinding.RegistryRef != OrderedParameterSet)
        {
            throw new ArgumentException(
                "A partition binding must name a member of the exact ordered-parameter artifact.",
                nameof(partitionBinding));
        }

        if (expectedRequestBodyLength < 0 ||
            expectedRequestBodyLength > MachineQueryValidation.MaximumRequestBodyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRequestBodyLength));
        }

        ExpectedRequestBodyLength = expectedRequestBodyLength;
        ExpectedRequestBodySha256 = expectedRequestBodySha256 is null
            ? null
            : SourceCoreValidation.RequireSha256(
                expectedRequestBodySha256,
                nameof(expectedRequestBodySha256));

        if (Method == HttpRequestMethod.Get &&
            (ContentType is not null ||
             Charset is not null ||
             ExpectedRequestBodyLength is not null ||
             ExpectedRequestBodySha256 is not null))
        {
            throw new ArgumentException("A GET machine query plan cannot describe entity bytes.");
        }

        if (Method == HttpRequestMethod.Post &&
            (ContentType is null ||
             ExpectedRequestBodyLength is null or 0 ||
             ExpectedRequestBodySha256 is null))
        {
            throw new ArgumentException("A POST machine query plan requires a typed nonempty entity body.");
        }
    }

    public string Schema { get; }

    public SourceRegistryMemberRef QueryFamilyRef { get; }

    public SourceArtifactRef RendererProfileRef { get; }

    public SourceArtifactRef RendererSourceRef { get; }

    public HttpRequestMethod Method { get; }

    public string TargetOriginAndPath { get; }

    public long ExpectedRequestTargetLength { get; }

    public string ExpectedRequestTargetSha256 { get; }

    public MachineResponseCardinality ResponseCardinality { get; }

    public SourceRegistryMemberRef? ContentType { get; }

    public MachineQueryCharset? Charset { get; }

    public MachineQueryInputMode InputMode { get; }

    public SourceArtifactRef OrderedParameterSet { get; }

    public SourceRegistryMemberRef PartitionBinding { get; }

    public long? ExpectedRequestBodyLength { get; }

    public string? ExpectedRequestBodySha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MachineQueryRenderReceipt
{
    public const string SchemaId = SourceCoreSchemaIds.MachineQueryRenderReceipt;

    [JsonConstructor]
    public MachineQueryRenderReceipt(
        string schema,
        SourceArtifactRef queryPlanRef,
        string queryPlanSchema,
        SourceArtifactRef rendererProfileRef,
        SourceArtifactRef rendererSourceRef,
        SourceArtifactRef orderedParameterSetRef,
        SourceRegistryMemberRef? contentType,
        MachineQueryCharset? charset,
        MachineQueryInputMode inputMode,
        HttpRequestMethod method,
        long requestTargetLength,
        string requestTargetSha256,
        long? requestBodyLength,
        string? requestBodySha256)
    {
        if (!string.Equals(schema, SchemaId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A machine query render receipt must declare {SchemaId}.",
                nameof(schema));
        }

        if (!string.Equals(queryPlanSchema, MachineQueryPlan.SchemaId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A machine query render receipt must reference {MachineQueryPlan.SchemaId}.",
                nameof(queryPlanSchema));
        }

        if (requestTargetLength is < 1 or > MachineQueryValidation.MaximumRequestTargetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTargetLength));
        }

        if (requestBodyLength < 0 ||
            requestBodyLength > MachineQueryValidation.MaximumRequestBodyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(requestBodyLength));
        }

        Schema = schema;
        QueryPlanRef = queryPlanRef ?? throw new ArgumentNullException(nameof(queryPlanRef));
        QueryPlanSchema = queryPlanSchema;
        RendererProfileRef = rendererProfileRef
            ?? throw new ArgumentNullException(nameof(rendererProfileRef));
        RendererSourceRef = rendererSourceRef
            ?? throw new ArgumentNullException(nameof(rendererSourceRef));
        OrderedParameterSetRef = orderedParameterSetRef
            ?? throw new ArgumentNullException(nameof(orderedParameterSetRef));
        ContentType = contentType is null
            ? null
            : MachineQueryValidation.RequireMediaTypeRegistryMember(
                contentType,
                nameof(contentType));
        Charset = charset is null
            ? null
            : SourceCoreValidation.RequireDefined(charset.Value, nameof(charset));
        InputMode = SourceCoreValidation.RequireDefined(inputMode, nameof(inputMode));
        Method = SourceCoreValidation.RequireDefined(method, nameof(method));
        RequestTargetLength = requestTargetLength;
        RequestTargetSha256 = SourceCoreValidation.RequireSha256(
            requestTargetSha256,
            nameof(requestTargetSha256));
        RequestBodyLength = requestBodyLength;
        RequestBodySha256 = requestBodySha256 is null
            ? null
            : SourceCoreValidation.RequireSha256(requestBodySha256, nameof(requestBodySha256));

        if (Method == HttpRequestMethod.Get &&
            (ContentType is not null ||
             Charset is not null ||
             RequestBodyLength is not null ||
             RequestBodySha256 is not null))
        {
            throw new ArgumentException("A GET render receipt cannot bind entity bytes.");
        }

        if (Method == HttpRequestMethod.Post &&
            (ContentType is null ||
             RequestBodyLength is null or 0 ||
             RequestBodySha256 is null))
        {
            throw new ArgumentException("A POST render receipt must bind a nonempty entity body.");
        }
    }

    public string Schema { get; }

    public SourceArtifactRef QueryPlanRef { get; }

    public string QueryPlanSchema { get; }

    public SourceArtifactRef RendererProfileRef { get; }

    public SourceArtifactRef RendererSourceRef { get; }

    public SourceArtifactRef OrderedParameterSetRef { get; }

    public SourceRegistryMemberRef? ContentType { get; }

    public MachineQueryCharset? Charset { get; }

    public MachineQueryInputMode InputMode { get; }

    public HttpRequestMethod Method { get; }

    public long RequestTargetLength { get; }

    public string RequestTargetSha256 { get; }

    public long? RequestBodyLength { get; }

    public string? RequestBodySha256 { get; }
}

public sealed class MachineQueryInputArtifact
{
    private readonly byte[] _canonicalBytes;
    private readonly IReadOnlyList<MachineQueryParameter> _orderedParameters;

    private MachineQueryInputArtifact(
        SourceArtifactRef artifactRef,
        SourceRegistryMemberRef queryFamilyRef,
        SourceRegistryMemberRef partitionBinding,
        MachineResponseCardinality responseCardinality,
        MachineQueryParameter[] orderedParameters,
        ReadOnlySpan<byte> canonicalBytes)
    {
        ArtifactRef = artifactRef;
        QueryFamilyRef = queryFamilyRef;
        PartitionBinding = partitionBinding;
        ResponseCardinality = responseCardinality;
        _orderedParameters = Array.AsReadOnly(orderedParameters.ToArray());
        _canonicalBytes = canonicalBytes.ToArray();
    }

    public static MachineQueryInputArtifact Create(
        string resourceId,
        SourceRegistryMemberRef queryFamilyRef,
        string partitionKey,
        MachineResponseCardinality responseCardinality,
        IReadOnlyList<MachineQueryParameter> orderedParameters)
    {
        queryFamilyRef = MachineQueryValidation.RequireMachineRegistryMember(
            queryFamilyRef,
            nameof(queryFamilyRef));
        partitionKey = MachineQueryValidation.RequireMachineMemberKey(
            partitionKey,
            nameof(partitionKey));
        ArgumentNullException.ThrowIfNull(responseCardinality);
        ArgumentNullException.ThrowIfNull(orderedParameters);
        var parameters = orderedParameters.ToArray();
        if (parameters.Length is < 1 or > MachineQueryValidation.MaximumParameterCount ||
            parameters.Any(static parameter => parameter is null) ||
            parameters.Select(static parameter => parameter.Name).Distinct(StringComparer.Ordinal).Count() !=
            parameters.Length)
        {
            throw new ArgumentException(
                "Machine query parameters must be a bounded ordered list with unique names.",
                nameof(orderedParameters));
        }

        var document = new MachineQueryInputDocument(
            MachineQueryInputDocument.SchemaId,
            queryFamilyRef,
            partitionKey,
            responseCardinality,
            parameters);
        var canonicalBytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
        var artifactRef = new SourceArtifactRef(
            resourceId,
            MachineQueryValidation.Sha256(canonicalBytes));
        return new MachineQueryInputArtifact(
            artifactRef,
            queryFamilyRef,
            new SourceRegistryMemberRef(artifactRef, partitionKey),
            responseCardinality,
            parameters,
            canonicalBytes);
    }

    public static MachineQueryInputArtifact ParseAndVerify(
        SourceArtifactRef artifactRef,
        ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        if (canonicalBytes.Length is < 1 or > MachineQueryValidation.MaximumInputArtifactBytes)
        {
            throw new ArgumentException(
                "A retained machine-input artifact must contain bounded canonical bytes.",
                nameof(canonicalBytes));
        }

        if (!string.Equals(
                MachineQueryValidation.Sha256(canonicalBytes),
                artifactRef.Sha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The retained machine-input bytes do not match their artifact reference.",
                nameof(canonicalBytes));
        }

        MachineQueryInputDocument document;
        try
        {
            document = ContractJson.Deserialize<MachineQueryInputDocument>(
                MachineQueryValidation.DecodeStrictUtf8(canonicalBytes, nameof(canonicalBytes)));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The retained machine-input bytes are not one valid typed artifact.",
                nameof(canonicalBytes),
                exception);
        }

        if (!string.Equals(document.Schema, MachineQueryInputDocument.SchemaId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A machine-input artifact must declare {MachineQueryInputDocument.SchemaId}.",
                nameof(canonicalBytes));
        }

        var rebuilt = Create(
            artifactRef.ResourceId,
            document.QueryFamilyRef,
            document.PartitionKey,
            document.ResponseCardinality,
            document.OrderedParameters);
        if (rebuilt.ArtifactRef != artifactRef ||
            !canonicalBytes.SequenceEqual(rebuilt._canonicalBytes))
        {
            throw new ArgumentException(
                "The retained machine-input artifact is not its exact canonical representation.",
                nameof(canonicalBytes));
        }

        return rebuilt;
    }

    public SourceArtifactRef ArtifactRef { get; }

    public SourceRegistryMemberRef QueryFamilyRef { get; }

    public SourceRegistryMemberRef PartitionBinding { get; }

    public MachineResponseCardinality ResponseCardinality { get; }

    public IReadOnlyList<MachineQueryParameter> OrderedParameters => _orderedParameters;

    public byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MachineQueryInputDocument(
    string Schema,
    SourceRegistryMemberRef QueryFamilyRef,
    string PartitionKey,
    MachineResponseCardinality ResponseCardinality,
    IReadOnlyList<MachineQueryParameter> OrderedParameters)
{
    public const string SchemaId = "machine_query_input/1";
}

public interface IMachineQueryRenderer
{
    SourceArtifactRef RendererProfileRef { get; }

    SourceArtifactRef RendererSourceRef { get; }

    /// <summary>
    /// The exact bytes <see cref="RendererProfileRef"/> names, when this renderer can produce
    /// them. Null when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decision 75: a run holds what it depends on rather than inheriting someone else's custody.
    /// The four external artifacts of a send closure are versioned constants of the code that
    /// runs, and the frozen plan already names each by digest, so the digest is the authority and
    /// these bytes are its witness. The binder verifies them against the reference at bind time,
    /// and retention refuses bytes that do not hash to the digest, so supplying them can only
    /// place under our own custody the exact bytes the reference already named.
    /// </para>
    /// <para>
    /// Optional rather than required, and deliberately so. A renderer that returns null keeps the
    /// reopen-by-reference path, which requires the artifact to already be in the store. That
    /// path is scheduled for deletion once every renderer produces its bytes; it exists now only
    /// so one adapter can close while another has not yet.
    /// </para>
    /// </remarks>
    ReadOnlyMemory<byte>? CopyRendererProfileBytes() => null;

    /// <summary>The exact bytes <see cref="RendererSourceRef"/> names, or null.</summary>
    ReadOnlyMemory<byte>? CopyRendererSourceBytes() => null;

    MachineQueryRenderOutput Render(
        MachineQueryPlan plan,
        MachineQueryInputArtifact orderedParameterSet);
}

/// <summary>
/// Sender-owned bridge that makes every artifact used by a machine-query projection prove its
/// content address. Binder-produced bytes are retained before reopening; externally produced
/// renderer and registry artifacts must already be reopenable.
/// </summary>
public interface IMachineQueryArtifactResolver
{
    Task<ReadOnlyMemory<byte>> RetainAndReopenAsync(
        SourceArtifactRef reference,
        ReadOnlyMemory<byte> producerBytes,
        CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> ReopenAsync(
        SourceArtifactRef reference,
        CancellationToken cancellationToken);
}

public sealed class MachineQueryRenderOutput
{
    private readonly byte[] _requestBody;

    public MachineQueryRenderOutput(string requestTarget, ReadOnlySpan<byte> requestBody)
    {
        ArgumentNullException.ThrowIfNull(requestTarget);
        RequestTarget = requestTarget;
        _requestBody = requestBody.ToArray();
    }

    public string RequestTarget { get; }

    public byte[] CopyRequestBody() => _requestBody.ToArray();
}

/// <summary>
/// Opaque live capability minted only by <see cref="MachineQueryBinder"/>. Its public projection is
/// diagnostic data, not provenance. A sender authenticates its non-rendering identity with
/// <see cref="MachineQueryBinder.OpenIdentity"/> and trusts request bytes only after
/// <see cref="MachineQueryBinder.OpenForSendAsync"/> reopens the complete artifact closure.
/// </summary>
public abstract class BoundMachineRequest
{
    private protected BoundMachineRequest()
    {
    }

    public abstract string RequestedUri { get; }

    public abstract MachineQueryRenderReceipt RenderReceipt { get; }

    public abstract byte[] CopyRequestBody();
}

/// <summary>
/// Non-authoritative identity projection opened only from the binder's private concrete
/// capability. It lets an owned transport select a source and reserve a plan item without
/// rendering or accepting the copyable public projection as proof of provenance.
/// </summary>
public sealed class BoundMachineRequestIdentity
{
    internal BoundMachineRequestIdentity(
        string requestedUri,
        MachineQueryRenderReceipt renderReceipt)
    {
        RequestedUri = requestedUri;
        RenderReceipt = renderReceipt;
    }

    public string RequestedUri { get; }

    public MachineQueryRenderReceipt RenderReceipt { get; }

    public SourceArtifactRef QueryPlanRef => RenderReceipt.QueryPlanRef;
}

/// <summary>
/// Reproduced immutable request data. This is not send authority; the transport accepts the opaque
/// <see cref="BoundMachineRequest"/> and performs the open itself immediately before use.
/// </summary>
public sealed class OpenedMachineRequest
{
    private readonly byte[] _requestBody;
    private readonly byte[] _queryPlanCanonicalBytes;
    private readonly byte[] _orderedParameterSetCanonicalBytes;
    private readonly byte[] _renderReceiptCanonicalBytes;

    internal OpenedMachineRequest(
        string requestedUri,
        ReadOnlySpan<byte> requestBody,
        MachineQueryRenderReceipt renderReceipt,
        ReadOnlySpan<byte> queryPlanCanonicalBytes,
        ReadOnlySpan<byte> orderedParameterSetCanonicalBytes,
        SourceArtifactRef? retainedRenderReceiptRef = null,
        byte[]? retainedRenderReceiptCanonicalBytes = null)
    {
        RequestedUri = requestedUri;
        _requestBody = requestBody.ToArray();
        RenderReceipt = renderReceipt;
        _queryPlanCanonicalBytes = queryPlanCanonicalBytes.ToArray();
        _orderedParameterSetCanonicalBytes = orderedParameterSetCanonicalBytes.ToArray();

        var reproducedReceiptBytes = MachineQueryRenderReceiptIdentity.GetCanonicalBytes(renderReceipt);
        if ((retainedRenderReceiptRef is null) != (retainedRenderReceiptCanonicalBytes is null))
        {
            throw new ArgumentException(
                "A retained render receipt requires both its artifact reference and canonical bytes.",
                nameof(retainedRenderReceiptRef));
        }

        if (retainedRenderReceiptRef is null)
        {
            // Decision 77: derived from the receipt's own canonical bytes, never minted fresh.
            // A random name here reached the retained request policy, whose digest is a member of
            // the R3.3 absence key tuple, so the absence key changed at every cut and an absence
            // history could never advance. Nothing failed; absence simply never became provable.
            RenderReceiptRef = MachineQueryRenderReceiptIdentity.Create(
                ContentDerivedIdentity.DeriveUuidUrn(
                    ContentDerivedIdentity.RenderReceiptScope,
                    reproducedReceiptBytes),
                renderReceipt);
            _renderReceiptCanonicalBytes = reproducedReceiptBytes;
        }
        else
        {
            MachineQueryRenderReceiptIdentity.Validate(retainedRenderReceiptRef, renderReceipt);
            if (!reproducedReceiptBytes.AsSpan().SequenceEqual(retainedRenderReceiptCanonicalBytes))
            {
                throw new ArgumentException(
                    "The retained render-receipt bytes differ from the typed receipt.",
                    nameof(retainedRenderReceiptCanonicalBytes));
            }

            RenderReceiptRef = retainedRenderReceiptRef;
            _renderReceiptCanonicalBytes = retainedRenderReceiptCanonicalBytes!.ToArray();
        }
    }

    public string RequestedUri { get; }

    public MachineQueryRenderReceipt RenderReceipt { get; }

    public SourceArtifactRef RenderReceiptRef { get; }

    public SourceArtifactRef QueryPlanRef => RenderReceipt.QueryPlanRef;

    public SourceArtifactRef OrderedParameterSetRef => RenderReceipt.OrderedParameterSetRef;

    public byte[] CopyRequestBody() => _requestBody.ToArray();

    public byte[] CopyQueryPlanCanonicalBytes() => _queryPlanCanonicalBytes.ToArray();

    public byte[] CopyOrderedParameterSetCanonicalBytes() =>
        _orderedParameterSetCanonicalBytes.ToArray();

    public byte[] CopyRenderReceiptCanonicalBytes() => _renderReceiptCanonicalBytes.ToArray();
}

public static class MachineQueryBinder
{
    internal static BoundMachineRequest BindForSend(
        MachineQueryPlan plan,
        SourceArtifactRef queryPlanRef,
        MachineQueryInputArtifact orderedParameterSet,
        IMachineQueryRenderer renderer)
    {
        var reproduced = ValidateAndRender(
            plan,
            queryPlanRef,
            orderedParameterSet,
            renderer);
        // Verified here, at the one place that holds both the reference and the bytes. A caller
        // cannot pair a true reference with other bytes, and retention refuses anything that does
        // not hash to the digest, so the two checks together mean supplying bytes can only place
        // under our custody what the frozen reference already named.
        var profileBytes = RequireNamedBytes(
            renderer.CopyRendererProfileBytes(), renderer.RendererProfileRef, "renderer profile");
        var sourceBytes = RequireNamedBytes(
            renderer.CopyRendererSourceBytes(), renderer.RendererSourceRef, "renderer source");

        return new MintedBoundMachineRequest(
            plan,
            queryPlanRef,
            MachineQueryPlanIdentity.GetCanonicalBytes(plan),
            orderedParameterSet.ArtifactRef,
            orderedParameterSet.CopyCanonicalBytes(),
            renderer,
            renderer.RendererProfileRef,
            renderer.RendererSourceRef,
            reproduced,
            profileBytes,
            sourceBytes);
    }

    /// <summary>
    /// Bytes offered for a reference must hash to it. Absent bytes are permitted and keep the
    /// reopen-by-reference path; wrong bytes are never permitted.
    /// </summary>
    private static ReadOnlyMemory<byte>? RequireNamedBytes(
        ReadOnlyMemory<byte>? offered,
        SourceArtifactRef reference,
        string artifactName)
    {
        if (offered is not { } bytes)
        {
            return null;
        }

        var digest = Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();
        if (!string.Equals(digest, reference.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {artifactName} bytes do not carry the digest their reference names.",
                nameof(reference));
        }

        return bytes;
    }

    /// <summary>
    /// Authenticates the binder-private capability and exposes only its immutable identity. This
    /// does not render, reopen artifacts or grant send authority.
    /// </summary>
    public static BoundMachineRequestIdentity OpenIdentity(BoundMachineRequest capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (capability is not MintedBoundMachineRequest minted)
        {
            throw new ArgumentException(
                "Only the binder's private concrete capability has an identity projection.",
                nameof(capability));
        }

        var receiptBytes = MachineQueryRenderReceiptIdentity.GetCanonicalBytes(minted.RenderReceipt);
        if (!receiptBytes.AsSpan().SequenceEqual(minted.CopyRenderReceiptCanonicalBytes()))
        {
            throw new ArgumentException(
                "The bound render-receipt identity changed after capability minting.",
                nameof(capability));
        }

        MachineQueryRenderReceiptIdentity.Validate(minted.RenderReceiptRef, minted.RenderReceipt);
        return new BoundMachineRequestIdentity(minted.RequestedUri, minted.RenderReceipt);
    }

    public static OpenedMachineRequest OpenForSend(BoundMachineRequest capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (capability is not MintedBoundMachineRequest minted)
        {
            throw new ArgumentException(
                "Only the binder's private concrete capability can be opened for send.",
                nameof(capability));
        }

        var currentPlanBytes = MachineQueryPlanIdentity.GetCanonicalBytes(minted.Plan);
        if (!currentPlanBytes.AsSpan().SequenceEqual(minted.CopyPlanCanonicalBytes()))
        {
            throw new ArgumentException(
                "The bound machine-query plan changed after capability minting.",
                nameof(capability));
        }

        MachineQueryPlanIdentity.Validate(minted.QueryPlanRef, minted.Plan);
        var input = MachineQueryInputArtifact.ParseAndVerify(
            minted.OrderedParameterSetRef,
            minted.CopyInputCanonicalBytes());
        if (minted.Renderer.RendererProfileRef != minted.RendererProfileRef ||
            minted.Renderer.RendererSourceRef != minted.RendererSourceRef)
        {
            throw new ArgumentException(
                "The bound renderer identity changed after capability minting.",
                nameof(capability));
        }

        var reproduced = ValidateAndRender(
            minted.Plan,
            minted.QueryPlanRef,
            input,
            minted.Renderer);
        if (!string.Equals(reproduced.RequestedUri, minted.RequestedUri, StringComparison.Ordinal) ||
            !reproduced.CopyRequestBody().AsSpan().SequenceEqual(minted.CopyFrozenRequestBody()) ||
            reproduced.RenderReceipt != minted.RenderReceipt)
        {
            throw new ArgumentException(
                "The machine request cannot be reproduced exactly from its retained typed inputs.",
                nameof(capability));
        }

        return new OpenedMachineRequest(
            reproduced.RequestedUri,
            reproduced.CopyRequestBody(),
            reproduced.RenderReceipt,
            minted.CopyPlanCanonicalBytes(),
            minted.CopyInputCanonicalBytes(),
            minted.RenderReceiptRef,
            minted.CopyRenderReceiptCanonicalBytes());
    }

    /// <summary>
    /// Reopens the complete transport-owned artifact set before reproducing the request. The
    /// resolver is not authority: every returned byte sequence is independently hashed and every
    /// binder-produced artifact must equal the frozen canonical bytes retained by the capability.
    /// </summary>
    public static async Task<OpenedMachineRequest> OpenForSendAsync(
        BoundMachineRequest capability,
        IMachineQueryArtifactResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(resolver);
        if (capability is not MintedBoundMachineRequest minted)
        {
            throw new ArgumentException(
                "Only the binder's private concrete capability can be opened for send.",
                nameof(capability));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var receiptBytes = await resolver.RetainAndReopenAsync(
                minted.RenderReceiptRef,
                minted.CopyRenderReceiptCanonicalBytes(),
                cancellationToken)
            .ConfigureAwait(false);
        RequireExactArtifact(
            minted.RenderReceiptRef,
            receiptBytes.Span,
            minted.CopyRenderReceiptCanonicalBytes(),
            "render receipt");
        MachineQueryRenderReceiptIdentity.Validate(minted.RenderReceiptRef, minted.RenderReceipt);

        var planBytes = await resolver.RetainAndReopenAsync(
                minted.QueryPlanRef,
                minted.CopyPlanCanonicalBytes(),
                cancellationToken)
            .ConfigureAwait(false);
        RequireExactArtifact(
            minted.QueryPlanRef,
            planBytes.Span,
            minted.CopyPlanCanonicalBytes(),
            "query plan");
        MachineQueryPlanIdentity.Validate(minted.QueryPlanRef, minted.Plan);

        var inputBytes = await resolver.RetainAndReopenAsync(
                minted.OrderedParameterSetRef,
                minted.CopyInputCanonicalBytes(),
                cancellationToken)
            .ConfigureAwait(false);
        RequireExactArtifact(
            minted.OrderedParameterSetRef,
            inputBytes.Span,
            minted.CopyInputCanonicalBytes(),
            "ordered parameter set");
        var input = MachineQueryInputArtifact.ParseAndVerify(
            minted.OrderedParameterSetRef,
            inputBytes.Span);

        foreach (var (reference, artifactName, offeredBytes) in
            ExternalArtifactReferences(minted, input))
        {
            _ = await ReopenExternalAsync(
                    resolver,
                    reference,
                    offeredBytes,
                    cancellationToken,
                    artifactName)
                .ConfigureAwait(false);
        }

        if (minted.Renderer.RendererProfileRef != minted.RendererProfileRef ||
            minted.Renderer.RendererSourceRef != minted.RendererSourceRef)
        {
            throw new ArgumentException(
                "The bound renderer identity changed after capability minting.",
                nameof(capability));
        }

        var reproduced = ValidateAndRender(
            minted.Plan,
            minted.QueryPlanRef,
            input,
            minted.Renderer);
        if (!string.Equals(reproduced.RequestedUri, minted.RequestedUri, StringComparison.Ordinal) ||
            !reproduced.CopyRequestBody().AsSpan().SequenceEqual(minted.CopyFrozenRequestBody()) ||
            reproduced.RenderReceipt != minted.RenderReceipt)
        {
            throw new ArgumentException(
                "The machine request cannot be reproduced exactly from its reopened artifacts.",
                nameof(capability));
        }

        return new OpenedMachineRequest(
            reproduced.RequestedUri,
            reproduced.CopyRequestBody(),
            reproduced.RenderReceipt,
            planBytes.Span,
            inputBytes.Span,
            minted.RenderReceiptRef,
            receiptBytes.ToArray());
    }

    /// <summary>
    /// Reopens one external artifact, retaining it first when this run can produce its bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decision 75: a run holds what it depends on. When the offered bytes are present the
    /// resolver retains them and reopens what it retained, so the run's own custody covers the
    /// artifact and a fresh store is sufficient. The bytes were verified against the reference at
    /// bind time and retention verifies them again, so the digest stays the authority.
    /// </para>
    /// <para>
    /// When they are absent this falls back to reopen by reference, which requires the artifact to
    /// already exist in the store. That fallback is temporary and its deletion is item 1b: it is
    /// what lets one adapter close while another has not yet, and a route relying on it is not
    /// proven against a fresh store. Deleting it is the only thing that stops a temporary path
    /// becoming a permanent one.
    /// </para>
    /// </remarks>
    private static async Task<ReadOnlyMemory<byte>> ReopenExternalAsync(
        IMachineQueryArtifactResolver resolver,
        SourceArtifactRef reference,
        ReadOnlyMemory<byte>? offeredBytes,
        CancellationToken cancellationToken,
        string artifactName)
    {
        var bytes = offeredBytes is { } carried
            ? await resolver.RetainAndReopenAsync(reference, carried, cancellationToken)
                .ConfigureAwait(false)
            : await resolver.ReopenAsync(reference, cancellationToken).ConfigureAwait(false);

        // expectedBytes stays null even when this run carried them. The reference is the
        // authority and the resolver's answer is checked against it; checking the resolver's
        // answer against the same array we just handed it would be a check that cannot fail.
        RequireExactArtifact(reference, bytes.Span, expectedBytes: null, artifactName);
        return bytes.ToArray();
    }

    private static IReadOnlyList<(SourceArtifactRef Reference, string Name, ReadOnlyMemory<byte>? Bytes)>
        ExternalArtifactReferences(
            MintedBoundMachineRequest minted,
            MachineQueryInputArtifact input)
    {
        var ordered =
            new List<(SourceArtifactRef Reference, string Name, ReadOnlyMemory<byte>? Bytes)>();

        // Seeded with the three the binder produced, so "external" means external rather than
        // merely listed elsewhere. A caller can alias them: BindPage and BindCount take the
        // renderer source from the caller while the input's own ref is derived from values that do
        // not depend on it, so binding twice yields two identical opened_artifact lines for one
        // artifact. Nothing is forged by that, and both lines name a true reference, but a distinct
        // count over the policy's opened lines then reads as a property of the code when it is a
        // property of the fixture.
        var seen = new HashSet<SourceArtifactRef>
        {
            minted.RenderReceiptRef,
            minted.QueryPlanRef,
            minted.OrderedParameterSetRef,
        };
        Add(minted.RendererProfileRef, "renderer profile", minted.CopyRendererProfileBytes());
        Add(minted.RendererSourceRef, "renderer source", minted.CopyRendererSourceBytes());
        Add(minted.RenderReceipt.ContentType?.RegistryRef, "content-type registry");
        Add(minted.Plan.QueryFamilyRef.RegistryRef, "query-family registry");
        Add(
            minted.Plan.ResponseCardinality.ExpectedPartitionRowCountEvidenceRef,
            "partition row-count evidence");
        foreach (var parameter in input.OrderedParameters)
        {
            Add(parameter.ProvenanceRef, $"parameter provenance '{parameter.Name}'");
        }

        return ordered;

        void Add(SourceArtifactRef? reference, string name, ReadOnlyMemory<byte>? bytes = null)
        {
            if (reference is not null && seen.Add(reference))
            {
                ordered.Add((reference, name, bytes));
            }
        }
    }

    private static void RequireExactArtifact(
        SourceArtifactRef reference,
        ReadOnlySpan<byte> actualBytes,
        byte[]? expectedBytes,
        string artifactName)
    {
        if (!string.Equals(
                MachineQueryValidation.Sha256(actualBytes),
                reference.Sha256,
                StringComparison.Ordinal) ||
            expectedBytes is { } expected && !actualBytes.SequenceEqual(expected))
        {
            throw new ArgumentException(
                $"The reopened {artifactName} does not match its exact retained identity.",
                nameof(actualBytes));
        }
    }

    public static void VerifyOffline(
        MachineQueryPlan plan,
        SourceArtifactRef queryPlanRef,
        MachineQueryInputArtifact orderedParameterSet,
        MachineQueryRenderReceipt receipt,
        IMachineQueryRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var rerendered = ValidateAndRender(plan, queryPlanRef, orderedParameterSet, renderer);
        if (rerendered.RenderReceipt != receipt)
        {
            throw new ArgumentException(
                "The retained render receipt differs from the offline rerender.",
                nameof(receipt));
        }
    }

    internal static OpenedMachineRequest ReproduceForEvidence(
        MachineQueryPlan plan,
        SourceArtifactRef queryPlanRef,
        MachineQueryInputArtifact orderedParameterSet,
        IMachineQueryRenderer renderer) =>
        ValidateAndRender(plan, queryPlanRef, orderedParameterSet, renderer);

    private static OpenedMachineRequest ValidateAndRender(
        MachineQueryPlan plan,
        SourceArtifactRef queryPlanRef,
        MachineQueryInputArtifact orderedParameterSet,
        IMachineQueryRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(queryPlanRef);
        ArgumentNullException.ThrowIfNull(orderedParameterSet);
        ArgumentNullException.ThrowIfNull(renderer);

        if (orderedParameterSet.ArtifactRef != plan.OrderedParameterSet ||
            orderedParameterSet.QueryFamilyRef != plan.QueryFamilyRef ||
            orderedParameterSet.PartitionBinding != plan.PartitionBinding ||
            orderedParameterSet.ResponseCardinality != plan.ResponseCardinality)
        {
            throw new ArgumentException(
                "The verified machine-input artifact does not match the plan.",
                nameof(orderedParameterSet));
        }

        MachineQueryPlanIdentity.Validate(queryPlanRef, plan);

        if (renderer.RendererProfileRef != plan.RendererProfileRef ||
            renderer.RendererSourceRef != plan.RendererSourceRef)
        {
            throw new ArgumentException("The renderer identity does not match the plan.", nameof(renderer));
        }

        var output = renderer.Render(plan, orderedParameterSet)
            ?? throw new ArgumentException("A machine query renderer returned no output.", nameof(renderer));
        var target = MachineQueryValidation.RequireRenderedRequestTarget(
            output.RequestTarget,
            nameof(output.RequestTarget));
        if (!MachineQueryValidation.IsTargetBoundToPlan(plan, target))
        {
            throw new ArgumentException("The rendered request target is not bound to the plan.", nameof(renderer));
        }

        var body = output.CopyRequestBody();
        if (plan.Charset == MachineQueryCharset.Utf8)
        {
            MachineQueryValidation.RequireStrictUtf8(body, nameof(renderer));
        }

        var bodyLength = body.Length == 0 ? (long?)null : body.LongLength;
        var bodyDigest = body.Length == 0 ? null : MachineQueryValidation.Sha256(body);
        if (bodyLength != plan.ExpectedRequestBodyLength ||
            !string.Equals(bodyDigest, plan.ExpectedRequestBodySha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The rendered request body differs from the plan binding.", nameof(renderer));
        }

        var targetBytes = Encoding.ASCII.GetBytes(new Uri(target).PathAndQuery);
        if (targetBytes.LongLength != plan.ExpectedRequestTargetLength ||
            !string.Equals(
                MachineQueryValidation.Sha256(targetBytes),
                plan.ExpectedRequestTargetSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The rendered request target differs from the plan binding.",
                nameof(renderer));
        }

        var receipt = new MachineQueryRenderReceipt(
            MachineQueryRenderReceipt.SchemaId,
            queryPlanRef,
            MachineQueryPlan.SchemaId,
            plan.RendererProfileRef,
            plan.RendererSourceRef,
            plan.OrderedParameterSet,
            plan.ContentType,
            plan.Charset,
            plan.InputMode,
            plan.Method,
            targetBytes.LongLength,
            MachineQueryValidation.Sha256(targetBytes),
            bodyLength,
            bodyDigest);
        return new OpenedMachineRequest(
            target,
            body,
            receipt,
            MachineQueryPlanIdentity.GetCanonicalBytes(plan),
            orderedParameterSet.CopyCanonicalBytes());
    }

    private sealed class MintedBoundMachineRequest : BoundMachineRequest
    {
        private readonly byte[] _planCanonicalBytes;
        private readonly byte[] _inputCanonicalBytes;
        private readonly byte[] _requestBody;
        private readonly byte[] _renderReceiptCanonicalBytes;

        // Held as the nullable memory itself, never as a byte[] that a conditional or an implicit
        // conversion has to turn back into one. ReadOnlyMemory<byte> has an implicit conversion
        // from byte[] that accepts null and yields an empty memory, so any expression whose best
        // common type lands on byte[] converts "no bytes" into "zero bytes" without a warning.
        // That is not a cosmetic difference here: zero bytes is a present offer, and the send
        // would retain an empty artifact under a true digest rather than fall back to reopening
        // the real one. Both spellings that reintroduce it are pinned by
        // RendererWithoutBytesIsReopenedRatherThanRetained.
        private readonly ReadOnlyMemory<byte>? _rendererProfileBytes;

        private readonly ReadOnlyMemory<byte>? _rendererSourceBytes;

        internal MintedBoundMachineRequest(
            MachineQueryPlan plan,
            SourceArtifactRef queryPlanRef,
            ReadOnlySpan<byte> planCanonicalBytes,
            SourceArtifactRef orderedParameterSetRef,
            ReadOnlySpan<byte> inputCanonicalBytes,
            IMachineQueryRenderer renderer,
            SourceArtifactRef rendererProfileRef,
            SourceArtifactRef rendererSourceRef,
            OpenedMachineRequest reproduced,
            ReadOnlyMemory<byte>? rendererProfileBytes,
            ReadOnlyMemory<byte>? rendererSourceBytes)
        {
            if (rendererProfileBytes is { } profile)
            {
                _rendererProfileBytes = profile.ToArray();
            }

            if (rendererSourceBytes is { } source)
            {
                _rendererSourceBytes = source.ToArray();
            }
            Plan = plan;
            QueryPlanRef = queryPlanRef;
            _planCanonicalBytes = planCanonicalBytes.ToArray();
            OrderedParameterSetRef = orderedParameterSetRef;
            _inputCanonicalBytes = inputCanonicalBytes.ToArray();
            Renderer = renderer;
            RendererProfileRef = rendererProfileRef;
            RendererSourceRef = rendererSourceRef;
            RequestedUri = reproduced.RequestedUri;
            _requestBody = reproduced.CopyRequestBody();
            RenderReceipt = reproduced.RenderReceipt;
            RenderReceiptRef = reproduced.RenderReceiptRef;
            _renderReceiptCanonicalBytes = reproduced.CopyRenderReceiptCanonicalBytes();
        }

        internal MachineQueryPlan Plan { get; }

        internal SourceArtifactRef QueryPlanRef { get; }

        internal SourceArtifactRef OrderedParameterSetRef { get; }

        internal IMachineQueryRenderer Renderer { get; }

        internal SourceArtifactRef RendererProfileRef { get; }

        internal SourceArtifactRef RendererSourceRef { get; }

        /// <summary>
        /// The renderer's own bytes when it produced them, verified at bind time against the
        /// reference that names them. Null keeps the reopen-by-reference path.
        /// </summary>
        internal ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _rendererProfileBytes;

        /// <summary>The renderer source bytes, same contract.</summary>
        internal ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSourceBytes;

        public override string RequestedUri { get; }

        public override MachineQueryRenderReceipt RenderReceipt { get; }

        internal SourceArtifactRef RenderReceiptRef { get; }

        public override byte[] CopyRequestBody() => _requestBody.ToArray();

        internal byte[] CopyPlanCanonicalBytes() => _planCanonicalBytes.ToArray();

        internal byte[] CopyInputCanonicalBytes() => _inputCanonicalBytes.ToArray();

        internal byte[] CopyRenderReceiptCanonicalBytes() =>
            _renderReceiptCanonicalBytes.ToArray();

        internal byte[] CopyFrozenRequestBody() => _requestBody.ToArray();
    }
}

public static class MachineQueryPlanIdentity
{
    public const string CanonicalizationIdentity = "machine-query-plan-canonical-json/1";

    public static SourceArtifactRef Create(string resourceId, MachineQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new SourceArtifactRef(
            resourceId,
            MachineQueryValidation.Sha256(GetCanonicalBytes(plan)));
    }

    public static byte[] GetCanonicalBytes(MachineQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ContractCanonicalizer.Canonicalize(
            plan,
            CanonicalizationIdentity,
            maximumDepth: 64);
    }

    public static void Validate(SourceArtifactRef artifactRef, MachineQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        var expected = Create(artifactRef.ResourceId, plan);
        if (expected != artifactRef)
        {
            throw new ArgumentException(
                "The query-plan artifact reference does not bind the canonical plan bytes.",
                nameof(artifactRef));
        }
    }
}

public static class MachineQueryRenderReceiptIdentity
{
    public const string CanonicalizationIdentity = "machine-query-render-receipt-canonical-json/1";

    public static SourceArtifactRef Create(string resourceId, MachineQueryRenderReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new SourceArtifactRef(
            resourceId,
            MachineQueryValidation.Sha256(GetCanonicalBytes(receipt)));
    }

    public static byte[] GetCanonicalBytes(MachineQueryRenderReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return ContractCanonicalizer.Canonicalize(
            receipt,
            CanonicalizationIdentity,
            maximumDepth: 64);
    }

    public static void Validate(SourceArtifactRef artifactRef, MachineQueryRenderReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        var expected = Create(artifactRef.ResourceId, receipt);
        if (expected != artifactRef)
        {
            throw new ArgumentException(
                "The rerender-receipt artifact reference does not bind the canonical receipt bytes.",
                nameof(artifactRef));
        }
    }
}

internal static class MachineQueryValidation
{
    public const int MaximumInputArtifactBytes = 1_048_576;
    public const int MaximumRequestTargetBytes = 4096;
    public const int MaximumRequestBodyBytes = 1_048_576;
    public const int MaximumResponseRowLimit = 999_999;
    public const int MaximumParameterCount = 64;
    public const int MaximumParameterTextLength = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string RequireTargetOriginAndPath(string value, string parameterName) =>
        RequireCanonicalHttpTarget(value, allowQuery: false, parameterName);

    public static string RequireRenderedRequestTarget(string value, string parameterName) =>
        RequireCanonicalHttpTarget(value, allowQuery: true, parameterName);

    public static bool IsTargetBoundToPlan(MachineQueryPlan plan, string target) =>
        string.Equals(target, plan.TargetOriginAndPath, StringComparison.Ordinal) ||
        (plan.Method == HttpRequestMethod.Get &&
         target.StartsWith(plan.TargetOriginAndPath + "?", StringComparison.Ordinal));

    public static SourceRegistryMemberRef RequireMachineRegistryMember(
        SourceRegistryMemberRef? value,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        _ = RequireMachineMemberKey(value.MemberKey, parameterName);
        return value;
    }

    public static string RequireMachineMemberKey(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 128 ||
            (value[0] is not (>= 'a' and <= 'z') and not (>= '0' and <= '9')) ||
            value.Any(static character =>
                character is not (>= 'a' and <= 'z') and
                not (>= '0' and <= '9') and not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "Machine query registry members must use one bounded lowercase identifier.",
                parameterName);
        }

        return value;
    }

    public static string RequirePublisherLiteral(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A publisher literal must be nonempty control-free text.",
                parameterName);
        }

        try
        {
            if (StrictUtf8.GetByteCount(value) > MaximumParameterTextLength)
            {
                throw new ArgumentException(
                    "A publisher literal exceeds the bounded UTF-8 length.",
                    parameterName);
            }
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "A publisher literal must be valid Unicode text.",
                parameterName,
                exception);
        }

        return value;
    }

    public static SourceRegistryMemberRef RequireMediaTypeRegistryMember(
        SourceRegistryMemberRef? value,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!MediaTypeHeaderValue.TryParse(value.MemberKey, out var parsed) ||
            parsed.Parameters.Count != 0 ||
            !string.Equals(parsed.MediaType, value.MemberKey, StringComparison.Ordinal) ||
            !string.Equals(value.MemberKey, value.MemberKey.ToLowerInvariant(), StringComparison.Ordinal) ||
            !IsConservativeMediaType(value.MemberKey))
        {
            throw new ArgumentException(
                "Machine request content types must be exact lowercase media types without parameters.",
                parameterName);
        }

        return value;
    }

    private static bool IsConservativeMediaType(string value)
    {
        var slash = value.IndexOf('/');
        return slash > 0 &&
               slash == value.LastIndexOf('/') &&
               slash < value.Length - 1 &&
               value.Where(static character => character != '/').All(static character =>
                   character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or
                       '!' or '#' or '$' or '&' or '^' or '_' or '.' or '+' or '-');
    }

    public static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    public static void RequireStrictUtf8(ReadOnlySpan<byte> value, string parameterName)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException(
                "A utf-8 machine request body must contain exact valid UTF-8 bytes.",
                parameterName,
                exception);
        }
    }

    public static string DecodeStrictUtf8(ReadOnlySpan<byte> value, string parameterName)
    {
        try
        {
            return StrictUtf8.GetString(value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException(
                "A retained machine-input artifact must contain exact valid UTF-8 bytes.",
                parameterName,
                exception);
        }
    }

    private static string RequireCanonicalHttpTarget(
        string value,
        bool allowQuery,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumRequestTargetBytes ||
            value.Any(static character => character is < '!' or > '~') ||
            !value.StartsWith("https://", StringComparison.Ordinal) ||
            HasAuthorityUserInfoMarker(value) ||
            HasUnsafePathAlias(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            string.IsNullOrEmpty(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            (!allowQuery && !string.IsNullOrEmpty(parsed.Query)) ||
            parsed.Port == 0 ||
            !IsExactDnsName(parsed.Host) ||
            !string.Equals(value, parsed.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A machine request target must be one exact canonical HTTP target without aliases or side channels.",
                parameterName);
        }

        return value;
    }

    private static bool IsExactDnsName(string host)
    {
        var labels = host.Split('.', StringSplitOptions.None);
        return host.Length <= 253 &&
               string.Equals(host, host.ToLowerInvariant(), StringComparison.Ordinal) &&
               !IPAddress.TryParse(host, out _) &&
               labels.All(static label =>
                   label.Length is > 0 and <= 63 &&
                   label[0] != '-' && label[^1] != '-' &&
                   label.All(static character =>
                       character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'));
    }

    private static bool HasAuthorityUserInfoMarker(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = value.Length;
        }

        return value.AsSpan(authorityStart, authorityEnd - authorityStart).Contains('@');
    }

    private static bool HasUnsafePathAlias(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        var pathStart = value.IndexOf('/', authorityStart);
        if (pathStart < 0)
        {
            return false;
        }

        var queryStart = value.IndexOfAny(['?', '#'], pathStart);
        var path = queryStart < 0 ? value[pathStart..] : value[pathStart..queryStart];
        return path.Contains('\\') || HasEncodedPathAlias(path);
    }

    private static bool HasEncodedPathAlias(string path)
    {
        var candidate = path;
        while (true)
        {
            var decodedAny = false;
            var decoded = new StringBuilder(candidate.Length);
            for (var index = 0; index < candidate.Length; index++)
            {
                if (candidate[index] == '%' &&
                    index + 2 < candidate.Length &&
                    TryDecodeHexByte(candidate[index + 1], candidate[index + 2], out var decodedByte))
                {
                    decodedAny = true;
                    var character = (char)decodedByte;
                    if (character is '.' or '/' or '\\')
                    {
                        return true;
                    }

                    decoded.Append(character);
                    index += 2;
                }
                else
                {
                    decoded.Append(candidate[index]);
                }
            }

            if (!decodedAny)
            {
                return false;
            }

            candidate = decoded.ToString();
        }
    }

    private static bool TryDecodeHexByte(char first, char second, out byte value)
    {
        var high = HexValue(first);
        var low = HexValue(second);
        if (high < 0 || low < 0)
        {
            value = 0;
            return false;
        }

        value = (byte)((high << 4) | low);
        return true;
    }

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1,
    };
}

internal static class SparqlQueryText
{
    internal static string StringLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var character in value)
        {
            _ = character switch
            {
                '\\' => builder.Append("\\\\"),
                '"' => builder.Append("\\\""),
                '\t' => builder.Append("\\t"),
                '\n' => builder.Append("\\n"),
                '\r' => builder.Append("\\r"),
                '\b' => builder.Append("\\b"),
                '\f' => builder.Append("\\f"),
                < ' ' or '\u007f' => builder.Append("\\u").Append(
                    ((int)character).ToString("X4", CultureInfo.InvariantCulture)),
                _ => builder.Append(character),
            };
        }

        return builder.Append('"').ToString();
    }
}
