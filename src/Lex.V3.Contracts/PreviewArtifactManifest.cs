using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewEnvironment
{
    [JsonConstructor]
    public PreviewEnvironment(string @class, string binding)
    {
        if (!string.Equals(@class, "preview", StringComparison.Ordinal))
        {
            throw new ArgumentException("Preview environment class must be preview.", nameof(@class));
        }

        Class = @class;
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        if (binding.Length > 2_048 || binding.Any(static character => character is < ' ' or > '~'))
        {
            throw new ArgumentException(
                "Preview environment binding must be bounded printable ASCII.",
                nameof(binding));
        }

        Binding = binding;
    }

    public string Class { get; }

    public string Binding { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewIssuer
{
    [JsonConstructor]
    public PreviewIssuer(string role, string issuerId, string keyId)
    {
        if (!string.Equals(role, "preview_attestor", StringComparison.Ordinal))
        {
            throw new ArgumentException("Preview issuer role must be preview_attestor.", nameof(role));
        }

        Role = role;
        IssuerId = ContractValidation.RequireIdentifier(issuerId, nameof(issuerId));
        KeyId = ContractValidation.RequireIdentifier(keyId, nameof(keyId));
    }

    public string Role { get; }

    public string IssuerId { get; }

    public string KeyId { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewTrackedSchemaReference
{
    [JsonConstructor]
    public PreviewTrackedSchemaReference(string schema, string schemaResource, string sha256)
    {
        Schema = ContractValidation.RequireIdentifier(schema, nameof(schema));
        SchemaResource = RequireSchemaResource(schema, schemaResource, nameof(schemaResource));
        Sha256 = ContractValidation.RequireSha256(sha256, nameof(sha256));
    }

    public string Schema { get; }

    public string SchemaResource { get; }

    public string Sha256 { get; }

    private static string RequireSchemaResource(
        string schema,
        string schemaResource,
        string parameterName)
    {
        var expected = V3SchemaResourceIds.ForWireSchema(schema);
        if (!string.Equals(schemaResource, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException("The schema resource does not match its wire identity.", parameterName);
        }

        return schemaResource;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewContractSet
{
    [JsonConstructor]
    public PreviewContractSet(
        PreviewTrackedSchemaReference envelope,
        PreviewTrackedSchemaReference objectSet,
        PreviewTrackedSchemaReference operationCatalog,
        PreviewTrackedSchemaReference refusalRegistry)
    {
        Envelope = RequireSchema(envelope, V3SchemaIds.PreviewEnvelope, nameof(envelope));
        ObjectSet = RequireSchema(objectSet, V3SchemaIds.PreviewObjectSet, nameof(objectSet));
        OperationCatalog = RequireSchema(
            operationCatalog,
            V3SchemaIds.PreviewOperationCatalog,
            nameof(operationCatalog));
        RefusalRegistry = RequireSchema(
            refusalRegistry,
            V3SchemaIds.PreviewRefusalRegistry,
            nameof(refusalRegistry));
    }

    public PreviewTrackedSchemaReference Envelope { get; }

    public PreviewTrackedSchemaReference ObjectSet { get; }

    public PreviewTrackedSchemaReference OperationCatalog { get; }

    public PreviewTrackedSchemaReference RefusalRegistry { get; }

    private static PreviewTrackedSchemaReference RequireSchema(
        PreviewTrackedSchemaReference value,
        string expectedSchema,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!string.Equals(value.Schema, expectedSchema, StringComparison.Ordinal))
        {
            throw new ArgumentException("The contract-set member has the wrong schema.", parameterName);
        }

        return value;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewPayloadDescriptor
{
    [JsonConstructor]
    public PreviewPayloadDescriptor(
        string schema,
        string schemaResource,
        string schemaSha256,
        string sha256,
        long bytes,
        string mediaType)
    {
        if (!string.Equals(schema, V3SchemaIds.PreviewPayload, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview payload schema.", nameof(schema));
        }

        if (bytes is < 0 or > PreviewContractLimits.MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        if (!string.Equals(mediaType, "application/json", StringComparison.Ordinal))
        {
            throw new ArgumentException("Preview payload media type must be application/json.", nameof(mediaType));
        }

        Schema = schema;
        if (!string.Equals(
                schemaResource,
                V3SchemaResourceIds.PreviewPayload,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview payload schema resource.", nameof(schemaResource));
        }

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
public sealed record PreviewAttestation
{
    [JsonConstructor]
    public PreviewAttestation(
        string purpose,
        string algorithm,
        string signatureFormat,
        string signature)
    {
        if (!string.Equals(purpose, "preview_mechanics_only", StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview attestation purpose.", nameof(purpose));
        }

        if (!string.Equals(algorithm, "ECDSA-P256-SHA256", StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview attestation algorithm.", nameof(algorithm));
        }

        if (!string.Equals(signatureFormat, "ieee-p1363", StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview signature format.", nameof(signatureFormat));
        }

        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Length != 86 || signature.Any(static value =>
                !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
        {
            throw new ArgumentException("Preview signature must be unpadded base64url for 64 P1363 bytes.", nameof(signature));
        }

        Purpose = purpose;
        Algorithm = algorithm;
        SignatureFormat = signatureFormat;
        Signature = signature;
    }

    public string Purpose { get; }

    public string Algorithm { get; }

    public string SignatureFormat { get; }

    public string Signature { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewArtifactManifest
{
    [JsonConstructor]
    public PreviewArtifactManifest(
        string schema,
        string schemaResource,
        string schemaSha256,
        string evidenceClass,
        bool synthetic,
        string sourceKind,
        PreviewEnvironment environment,
        PreviewIssuer issuer,
        PreviewContractSet contractSet,
        PreviewPayloadDescriptor payload,
        PreviewAttestation attestation)
    {
        if (!string.Equals(schema, V3SchemaIds.PreviewArtifact, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview artifact schema.", nameof(schema));
        }

        if (!string.Equals(evidenceClass, "synthetic_preview", StringComparison.Ordinal))
        {
            throw new ArgumentException("Preview evidence class must be synthetic_preview.", nameof(evidenceClass));
        }

        if (!synthetic)
        {
            throw new ArgumentException("Preview artifact must carry synthetic=true.", nameof(synthetic));
        }

        if (!string.Equals(sourceKind, "synthetic_test", StringComparison.Ordinal))
        {
            throw new ArgumentException("Preview source kind must be synthetic_test.", nameof(sourceKind));
        }

        Schema = schema;
        if (!string.Equals(
                schemaResource,
                V3SchemaResourceIds.PreviewArtifact,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected preview artifact schema resource.", nameof(schemaResource));
        }

        SchemaResource = schemaResource;
        SchemaSha256 = ContractValidation.RequireSha256(schemaSha256, nameof(schemaSha256));
        EvidenceClass = evidenceClass;
        Synthetic = true;
        SourceKind = sourceKind;
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        Issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        ContractSet = contractSet ?? throw new ArgumentNullException(nameof(contractSet));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Attestation = attestation ?? throw new ArgumentNullException(nameof(attestation));
    }

    public string Schema { get; }

    public string SchemaResource { get; }

    public string SchemaSha256 { get; }

    public string EvidenceClass { get; }

    public bool Synthetic { get; }

    public string SourceKind { get; }

    public PreviewEnvironment Environment { get; }

    public PreviewIssuer Issuer { get; }

    public PreviewContractSet ContractSet { get; }

    public PreviewPayloadDescriptor Payload { get; }

    public PreviewAttestation Attestation { get; }
}
