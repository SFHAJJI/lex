using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

public static class PreviewSchemaGraph
{
    public static ReadOnlyCollection<string> SchemaIds { get; } = Array.AsReadOnly(
        new[]
        {
            V3SchemaIds.PreviewArtifact,
            V3SchemaIds.PreviewPayload,
            V3SchemaIds.PreviewEnvelope,
            V3SchemaIds.PreviewObjectSet,
            V3SchemaIds.PreviewOperationCatalog,
            V3SchemaIds.PreviewRefusalRegistry,
        });

    public static ReadOnlyCollection<string> ContractSetSchemaIds { get; } = Array.AsReadOnly(
        new[]
        {
            V3SchemaIds.PreviewEnvelope,
            V3SchemaIds.PreviewObjectSet,
            V3SchemaIds.PreviewOperationCatalog,
            V3SchemaIds.PreviewRefusalRegistry,
        });
}

[JsonConverter(typeof(JsonStringEnumConverter<PreviewCapabilityState>))]
public enum PreviewCapabilityState
{
    [JsonStringEnumMemberName("preview_mechanics_only")]
    MechanicsOnly,
}

[JsonConverter(typeof(JsonStringEnumConverter<PreviewProvisionality>))]
public enum PreviewProvisionality
{
    [JsonStringEnumMemberName("all")]
    All,
}

[JsonConverter(typeof(JsonStringEnumConverter<PreviewSourceKind>))]
public enum PreviewSourceKind
{
    [JsonStringEnumMemberName("synthetic_test")]
    SyntheticTest,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewSourceContext
{
    [JsonConstructor]
    public PreviewSourceContext(PreviewSourceKind sourceKind)
    {
        if (sourceKind != PreviewSourceKind.SyntheticTest)
        {
            throw new ArgumentException("Preview source context must be synthetic_test.", nameof(sourceKind));
        }

        SourceKind = sourceKind;
    }

    public PreviewSourceKind SourceKind { get; }

    public static PreviewSourceContext SyntheticTest { get; } = new(PreviewSourceKind.SyntheticTest);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewOperationReference
{
    [JsonConstructor]
    public PreviewOperationReference(string operationId, string catalogId, string catalogSha256)
    {
        if (!V3ContractVocabulary.OperationIds.Contains(operationId, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unknown V3 operation identifier.", nameof(operationId));
        }

        OperationId = operationId;
        CatalogId = ContractValidation.RequireIdentifier(catalogId, nameof(catalogId));
        CatalogSha256 = ContractValidation.RequireSha256(catalogSha256, nameof(catalogSha256));
    }

    public string OperationId { get; }

    public string CatalogId { get; }

    public string CatalogSha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewSnapshotReference
{
    [JsonConstructor]
    public PreviewSnapshotReference(string snapshotId, string snapshotSha256)
    {
        SnapshotId = ContractValidation.RequireIdentifier(snapshotId, nameof(snapshotId));
        SnapshotSha256 = ContractValidation.RequireSha256(snapshotSha256, nameof(snapshotSha256));
    }

    public string SnapshotId { get; }

    public string SnapshotSha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewArtifactReference
{
    [JsonConstructor]
    public PreviewArtifactReference(
        string artifactId,
        string manifestSha256,
        string payloadSha256)
    {
        ArtifactId = ContractValidation.RequireIdentifier(artifactId, nameof(artifactId));
        ManifestSha256 = ContractValidation.RequireSha256(manifestSha256, nameof(manifestSha256));
        PayloadSha256 = ContractValidation.RequireSha256(payloadSha256, nameof(payloadSha256));
    }

    public string ArtifactId { get; }

    public string ManifestSha256 { get; }

    public string PayloadSha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ComponentIdentity
{
    [JsonConstructor]
    public ComponentIdentity(string componentId, string sourceSha256)
    {
        ComponentId = ContractValidation.RequireIdentifier(componentId, nameof(componentId));
        SourceSha256 = ContractValidation.RequireSha256(sourceSha256, nameof(sourceSha256));
    }

    public string ComponentId { get; }

    public string SourceSha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewFreshness
{
    [JsonConstructor]
    public PreviewFreshness(DateTimeOffset observedAt)
    {
        if (observedAt == default)
        {
            throw new ArgumentException("Preview observation time is required.", nameof(observedAt));
        }

        ObservedAt = observedAt.ToUniversalTime();
    }

    public DateTimeOffset ObservedAt { get; }

    public string UpstreamHealth => "not_applicable_synthetic";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewEnvelopeContext
{
    [JsonConstructor]
    public PreviewEnvelopeContext(
        string requestRef,
        PreviewOperationReference operation,
        ContractReference refusalRegistry,
        PreviewSnapshotReference snapshot,
        PreviewArtifactReference artifact,
        string indexFormat,
        ComponentIdentity runtime,
        ComponentIdentity builder,
        PreviewCapabilityState capabilities,
        PreviewFreshness freshness,
        string jurisdiction,
        PreviewProvisionality provisionality,
        PreviewSourceContext source)
    {
        RequestRef = RequireRequestReference(requestRef);
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        RefusalRegistry = refusalRegistry ?? throw new ArgumentNullException(nameof(refusalRegistry));
        if (!string.Equals(refusalRegistry.Schema, V3SchemaIds.PreviewRefusalRegistry, StringComparison.Ordinal))
        {
            throw new ArgumentException("The envelope must bind the preview refusal registry.", nameof(refusalRegistry));
        }

        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        IndexFormat = ContractValidation.RequireIdentifier(indexFormat, nameof(indexFormat));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Builder = builder ?? throw new ArgumentNullException(nameof(builder));
        if (string.Equals(runtime.ComponentId, builder.ComponentId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Runtime and builder identities must be distinct.", nameof(builder));
        }

        if (capabilities != PreviewCapabilityState.MechanicsOnly)
        {
            throw new ArgumentException("Preview cannot assert production legal capabilities.", nameof(capabilities));
        }

        Capabilities = capabilities;
        Freshness = freshness ?? throw new ArgumentNullException(nameof(freshness));
        Jurisdiction = ContractValidation.RequireIdentifier(jurisdiction, nameof(jurisdiction));
        Provisionality = provisionality;
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public string RequestRef { get; }

    public PreviewOperationReference Operation { get; }

    public ContractReference RefusalRegistry { get; }

    public PreviewSnapshotReference Snapshot { get; }

    public PreviewArtifactReference Artifact { get; }

    public string IndexFormat { get; }

    public ComponentIdentity Runtime { get; }

    public ComponentIdentity Builder { get; }

    public PreviewCapabilityState Capabilities { get; }

    public PreviewFreshness Freshness { get; }

    public string Jurisdiction { get; }

    public PreviewProvisionality Provisionality { get; }

    public PreviewSourceContext Source { get; }

    private static string RequireRequestReference(string value)
    {
        value = ContractValidation.RequireIdentifier(value, nameof(value));
        if (!value.StartsWith("req_", StringComparison.Ordinal) || value.Length is < 20 or > 128)
        {
            throw new ArgumentException("Request references must be opaque req_ identifiers.", nameof(value));
        }

        return value;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewObjectSetReference
{
    [JsonConstructor]
    public PreviewObjectSetReference(string objectSetId, string objectSetSha256)
    {
        ObjectSetId = ContractValidation.RequireIdentifier(objectSetId, nameof(objectSetId));
        ObjectSetSha256 = ContractValidation.RequireSha256(objectSetSha256, nameof(objectSetSha256));
    }

    public string ObjectSetId { get; }

    public string ObjectSetSha256 { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "branch")]
[JsonDerivedType(typeof(PreviewSuccessEnvelope), "success")]
[JsonDerivedType(typeof(PreviewRefusalEnvelope), "refusal")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record PreviewEnvelope
{
    protected PreviewEnvelope(
        string schema,
        string objectType,
        string status,
        PreviewEnvelopeContext context)
    {
        if (!string.Equals(schema, V3SchemaIds.PreviewEnvelope, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview envelope schema.", nameof(schema));
        }

        if (!string.Equals(objectType, "envelope", StringComparison.Ordinal))
        {
            throw new ArgumentException("Preview envelope object_type must be envelope.", nameof(objectType));
        }

        Schema = schema;
        ObjectType = objectType;
        Status = ContractValidation.RequireIdentifier(status, nameof(status));
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public string Schema { get; }

    public string ObjectType { get; }

    public string Status { get; }

    public PreviewEnvelopeContext Context { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewSuccessEnvelope : PreviewEnvelope
{
    [JsonConstructor]
    public PreviewSuccessEnvelope(
        string schema,
        string objectType,
        string status,
        PreviewEnvelopeContext context,
        PreviewObjectSetReference result)
        : base(schema, objectType, status, context)
    {
        if (!string.Equals(status, "ok", StringComparison.Ordinal))
        {
            throw new ArgumentException("A success envelope status must be ok.", nameof(status));
        }

        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public PreviewObjectSetReference Result { get; }

    public static PreviewSuccessEnvelope Create(
        PreviewEnvelopeContext context,
        PreviewObjectSetReference result) =>
        new(V3SchemaIds.PreviewEnvelope, "envelope", "ok", context, result);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewRefusalEnvelope : PreviewEnvelope
{
    [JsonConstructor]
    public PreviewRefusalEnvelope(
        string schema,
        string objectType,
        string status,
        PreviewEnvelopeContext context,
        IdentifierUnknownRefusal refusal)
        : base(schema, objectType, status, context)
    {
        Refusal = refusal ?? throw new ArgumentNullException(nameof(refusal));
        if (!string.Equals(status, "identifier_unknown", StringComparison.Ordinal) ||
            refusal.Code != RefusalCode.IdentifierUnknown)
        {
            throw new ArgumentException("Refusal status and payload code must match.", nameof(status));
        }
    }

    public IdentifierUnknownRefusal Refusal { get; }

    public static PreviewRefusalEnvelope Create(
        PreviewEnvelopeContext context,
        IdentifierUnknownRefusal refusal) =>
        new(V3SchemaIds.PreviewEnvelope, "envelope", "identifier_unknown", context, refusal);
}
