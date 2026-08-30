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
        Binding = ContractValidation.RequireIdentifier(binding, nameof(binding));
        if (binding.Length > 2_048)
        {
            throw new ArgumentException("Preview environment binding is too long.", nameof(binding));
        }
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
public sealed record PreviewContractSet
{
    [JsonConstructor]
    public PreviewContractSet(
        ContractReference envelope,
        ContractReference objectSet,
        ContractReference operationCatalog,
        ContractReference refusalRegistry)
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

    public ContractReference Envelope { get; }

    public ContractReference ObjectSet { get; }

    public ContractReference OperationCatalog { get; }

    public ContractReference RefusalRegistry { get; }

    private static ContractReference RequireSchema(
        ContractReference value,
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
    public PreviewPayloadDescriptor(string schema, string sha256, long bytes, string mediaType)
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
        Sha256 = ContractValidation.RequireSha256(sha256, nameof(sha256));
        Bytes = bytes;
        MediaType = mediaType;
    }

    public string Schema { get; }

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

    public string EvidenceClass { get; }

    public bool Synthetic { get; }

    public string SourceKind { get; }

    public PreviewEnvironment Environment { get; }

    public PreviewIssuer Issuer { get; }

    public PreviewContractSet ContractSet { get; }

    public PreviewPayloadDescriptor Payload { get; }

    public PreviewAttestation Attestation { get; }
}
