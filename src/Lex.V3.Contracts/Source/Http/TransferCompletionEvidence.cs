using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public static class TransferCompletionSchemaIds
{
    public const string TransferCompletionEvidence = "lex-v3-transfer-completion-evidence/1";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(DeclaredContentLengthCompleteEvidence),
    "declared_content_length_complete")]
[JsonDerivedType(
    typeof(Http1TerminalChunkCompleteEvidence),
    "http1_terminal_chunk_complete")]
[JsonDerivedType(
    typeof(Http2EndStreamCompleteEvidence),
    "http2_end_stream_complete")]
[JsonDerivedType(
    typeof(Http3FinCompleteEvidence),
    "http3_fin_complete")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract class TransferCompletionEvidence
{
    private protected TransferCompletionEvidence(
        string schema,
        SourceArtifactRef adapterExecutionIdentity,
        string responseObservationId,
        string transportByteSha256,
        long transportByteLength)
    {
        if (!string.Equals(
                schema,
                TransferCompletionSchemaIds.TransferCompletionEvidence,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Transfer evidence must declare {TransferCompletionSchemaIds.TransferCompletionEvidence}.",
                nameof(schema));
        }

        if (transportByteLength is <= 0 or > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(transportByteLength));
        }

        Schema = schema;
        AdapterExecutionIdentity = adapterExecutionIdentity
            ?? throw new ArgumentNullException(nameof(adapterExecutionIdentity));
        ResponseObservationId = SourceCoreValidation.RequireUuidUrn(
            responseObservationId,
            nameof(responseObservationId));
        TransportByteSha256 = SourceCoreValidation.RequireSha256(
            transportByteSha256,
            nameof(transportByteSha256));
        TransportByteLength = transportByteLength;
    }

    public string Schema { get; }

    public SourceArtifactRef AdapterExecutionIdentity { get; }

    public string ResponseObservationId { get; }

    public string TransportByteSha256 { get; }

    public long TransportByteLength { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DeclaredContentLengthCompleteEvidence : TransferCompletionEvidence
{
    [JsonConstructor]
    public DeclaredContentLengthCompleteEvidence(
        string schema,
        SourceArtifactRef adapterExecutionIdentity,
        string responseObservationId,
        string transportByteSha256,
        long transportByteLength)
        : base(
            schema,
            adapterExecutionIdentity,
            responseObservationId,
            transportByteSha256,
            transportByteLength)
    {
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class Http1TerminalChunkCompleteEvidence : TransferCompletionEvidence
{
    [JsonConstructor]
    public Http1TerminalChunkCompleteEvidence(
        string schema,
        SourceArtifactRef adapterExecutionIdentity,
        string responseObservationId,
        string transportByteSha256,
        long transportByteLength,
        SourceArtifactRef adapterReceiptRef)
        : base(
            schema,
            adapterExecutionIdentity,
            responseObservationId,
            transportByteSha256,
            transportByteLength)
    {
        AdapterReceiptRef = adapterReceiptRef
            ?? throw new ArgumentNullException(nameof(adapterReceiptRef));
    }

    public SourceArtifactRef AdapterReceiptRef { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class Http2EndStreamCompleteEvidence : TransferCompletionEvidence
{
    [JsonConstructor]
    public Http2EndStreamCompleteEvidence(
        string schema,
        SourceArtifactRef adapterExecutionIdentity,
        string responseObservationId,
        string transportByteSha256,
        long transportByteLength,
        SourceArtifactRef adapterReceiptRef)
        : base(
            schema,
            adapterExecutionIdentity,
            responseObservationId,
            transportByteSha256,
            transportByteLength)
    {
        AdapterReceiptRef = adapterReceiptRef
            ?? throw new ArgumentNullException(nameof(adapterReceiptRef));
    }

    public SourceArtifactRef AdapterReceiptRef { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class Http3FinCompleteEvidence : TransferCompletionEvidence
{
    [JsonConstructor]
    public Http3FinCompleteEvidence(
        string schema,
        SourceArtifactRef adapterExecutionIdentity,
        string responseObservationId,
        string transportByteSha256,
        long transportByteLength,
        SourceArtifactRef adapterReceiptRef)
        : base(
            schema,
            adapterExecutionIdentity,
            responseObservationId,
            transportByteSha256,
            transportByteLength)
    {
        AdapterReceiptRef = adapterReceiptRef
            ?? throw new ArgumentNullException(nameof(adapterReceiptRef));
    }

    public SourceArtifactRef AdapterReceiptRef { get; }
}
