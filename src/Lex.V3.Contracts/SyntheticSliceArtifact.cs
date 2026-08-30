using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceControlDescriptor
{
    [JsonConstructor]
    public SyntheticSliceControlDescriptor(
        string schema,
        string schemaResource,
        string schemaSha256,
        string sha256,
        long bytes,
        string mediaType)
    {
        if (!string.Equals(schema, V3SchemaIds.SyntheticSliceControl, StringComparison.Ordinal) ||
            !string.Equals(schemaResource, V3SchemaResourceIds.SyntheticSliceControl, StringComparison.Ordinal) ||
            !string.Equals(mediaType, "application/json", StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic control descriptor identity is fixed.");
        }

        if (bytes is <= 0 or > SyntheticSliceContractLimits.MaximumControlBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        Schema = schema;
        SchemaResource = schemaResource;
        SchemaSha256 = ContractValidation.RequireSha256(schemaSha256, nameof(schemaSha256));
        Sha256 = ContractValidation.RequireSha256(sha256, nameof(sha256));
        Bytes = bytes;
        MediaType = mediaType;
    }

    public string Schema { get; }

    public string SchemaResource { get; }

    public string SchemaSha256 { get; }

    public string Sha256 { get; }

    public long Bytes { get; }

    public string MediaType { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceArtifactManifest
{
    [JsonConstructor]
    public SyntheticSliceArtifactManifest(
        string schema,
        string schemaResource,
        string schemaSha256,
        string evidenceClass,
        bool synthetic,
        string sourceKind,
        PreviewEnvironment environment,
        PreviewIssuer issuer,
        SyntheticSliceSchemaTable schemaTable,
        SyntheticSliceControlDescriptor control,
        PreviewAttestation attestation)
    {
        if (!string.Equals(schema, V3SchemaIds.SyntheticSliceArtifact, StringComparison.Ordinal) ||
            !string.Equals(schemaResource, V3SchemaResourceIds.SyntheticSliceArtifact, StringComparison.Ordinal) ||
            !string.Equals(evidenceClass, "synthetic_preview", StringComparison.Ordinal) ||
            !synthetic ||
            !string.Equals(sourceKind, "synthetic_test", StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic artifact markers and schema identity are fixed.");
        }

        SchemaSha256 = ContractValidation.RequireSha256(schemaSha256, nameof(schemaSha256));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        Issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        SchemaTable = schemaTable ?? throw new ArgumentNullException(nameof(schemaTable));
        Control = control ?? throw new ArgumentNullException(nameof(control));
        Attestation = attestation ?? throw new ArgumentNullException(nameof(attestation));

        if (!string.Equals(SchemaTable.Members[0].Sha256, SchemaSha256, StringComparison.Ordinal) ||
            !string.Equals(SchemaTable.Members[1].Sha256, Control.SchemaSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The manifest must bind its own and the control schema digests.");
        }

        Schema = schema;
        SchemaResource = schemaResource;
        EvidenceClass = evidenceClass;
        Synthetic = true;
        SourceKind = sourceKind;
    }

    public string Schema { get; }

    public string SchemaResource { get; }

    public string SchemaSha256 { get; }

    public string EvidenceClass { get; }

    public bool Synthetic { get; }

    public string SourceKind { get; }

    public PreviewEnvironment Environment { get; }

    public PreviewIssuer Issuer { get; }

    public SyntheticSliceSchemaTable SchemaTable { get; }

    public SyntheticSliceControlDescriptor Control { get; }

    public PreviewAttestation Attestation { get; }
}
