using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;

namespace Lex.V3.Artifacts;

internal static class AdmissionHeaderReader
{
    private static readonly HashSet<string> RootMembers = Set(
        "schema",
        "schema_resource",
        "schema_sha256",
        "evidence_class",
        "synthetic",
        "source_kind",
        "environment",
        "issuer",
        "contract_set",
        "payload",
        "attestation");

    private static readonly HashSet<string> EnvironmentMembers = Set("class", "binding");
    private static readonly HashSet<string> IssuerMembers = Set("role", "issuer_id", "key_id");
    private static readonly HashSet<string> ContractSetMembers = Set(
        "envelope",
        "object_set",
        "operation_catalog",
        "refusal_registry");
    private static readonly HashSet<string> ContractReferenceMembers = Set(
        "schema",
        "schema_resource",
        "sha256");
    private static readonly HashSet<string> PayloadMembers = Set(
        "schema",
        "schema_resource",
        "schema_sha256",
        "sha256",
        "bytes",
        "media_type");
    private static readonly HashSet<string> AttestationMembers = Set(
        "purpose",
        "algorithm",
        "signature_format",
        "signature");

    public static HeaderReadResult Read(ReadOnlyMemory<byte> bytes)
    {
        var scanFailure = Scan(bytes.Span);
        if (scanFailure is not null)
        {
            return HeaderReadResult.Rejected(scanFailure);
        }

        try
        {
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowDuplicateProperties = false,
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = PreviewContractLimits.MaximumManifestDepth,
                });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return HeaderReadResult.Rejected(Failure(ArtifactAdmissionFailureCode.MalformedHeader));
            }

            var unknown = FindUnknown(root, RootMembers);
            if (unknown is not null)
            {
                return HeaderReadResult.Rejected(Failure(ArtifactAdmissionFailureCode.UnknownMember));
            }

            var environment = RequiredObject(root, "environment");
            var issuer = RequiredObject(root, "issuer");
            var contractSet = RequiredObject(root, "contract_set");
            var payload = RequiredObject(root, "payload");
            var attestation = RequiredObject(root, "attestation");

            if (FindUnknown(environment, EnvironmentMembers) is not null ||
                FindUnknown(issuer, IssuerMembers) is not null ||
                FindUnknown(contractSet, ContractSetMembers) is not null ||
                FindUnknown(payload, PayloadMembers) is not null ||
                FindUnknown(attestation, AttestationMembers) is not null)
            {
                return HeaderReadResult.Rejected(Failure(ArtifactAdmissionFailureCode.UnknownMember));
            }

            foreach (var contractName in ContractSetMembers)
            {
                var contract = RequiredObject(contractSet, contractName);
                if (FindUnknown(contract, ContractReferenceMembers) is not null)
                {
                    return HeaderReadResult.Rejected(Failure(ArtifactAdmissionFailureCode.UnknownMember));
                }

                _ = RequiredString(contract, "schema");
                _ = RequiredString(contract, "schema_resource");
                RequireSha256(RequiredString(contract, "sha256"));
            }

            var payloadBytes = RequiredInt64(payload, "bytes");
            if (payloadBytes is < 0 or > PreviewContractLimits.MaximumPayloadBytes)
            {
                return HeaderReadResult.Rejected(Failure(ArtifactAdmissionFailureCode.MalformedHeader));
            }

            RequireSha256(RequiredString(payload, "sha256"));
            var payloadSchemaSha256 = RequiredString(payload, "schema_sha256");
            RequireSha256(payloadSchemaSha256);

            var header = new AdmissionHeader(
                RequiredString(root, "schema"),
                RequiredString(root, "schema_resource"),
                RequiredSha256(root, "schema_sha256"),
                RequiredString(root, "evidence_class"),
                RequiredBoolean(root, "synthetic"),
                RequiredString(root, "source_kind"),
                RequiredString(environment, "class"),
                RequiredString(environment, "binding"),
                RequiredString(issuer, "role"),
                RequiredString(issuer, "issuer_id"),
                RequiredString(issuer, "key_id"),
                RequiredString(payload, "schema"),
                RequiredString(payload, "schema_resource"),
                payloadSchemaSha256,
                payloadBytes,
                RequiredString(payload, "media_type"),
                RequiredString(attestation, "purpose"),
                RequiredString(attestation, "algorithm"),
                RequiredString(attestation, "signature_format"),
                RequiredString(attestation, "signature"));

            return HeaderReadResult.Accepted(header);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return HeaderReadResult.Rejected(Failure(ArtifactAdmissionFailureCode.MalformedHeader));
        }
    }

    private static ArtifactAdmissionFailure? Scan(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var reader = new Utf8JsonReader(
                bytes,
                new JsonReaderOptions
                {
                    AllowMultipleValues = false,
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = PreviewContractLimits.MaximumManifestDepth,
                });
            var frames = new Stack<HashSet<string>>();
            var propertyCount = 0;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        frames.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        if (frames.Count == 0)
                        {
                            return Failure(ArtifactAdmissionFailureCode.MalformedHeader);
                        }

                        frames.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        {
                            if (frames.Count == 0 || ++propertyCount > PreviewContractLimits.MaximumManifestProperties)
                            {
                                return Failure(ArtifactAdmissionFailureCode.MalformedHeader);
                            }

                            var name = reader.GetString()!;
                            if (Encoding.UTF8.GetByteCount(name) > PreviewContractLimits.MaximumManifestPropertyNameBytes)
                            {
                                return Failure(ArtifactAdmissionFailureCode.MalformedHeader);
                            }

                            if (!frames.Peek().Add(name))
                            {
                                return Failure(ArtifactAdmissionFailureCode.DuplicateMember);
                            }

                            break;
                        }
                    case JsonTokenType.String:
                        if (Encoding.UTF8.GetByteCount(reader.GetString()!) >
                            PreviewContractLimits.MaximumManifestStringBytes)
                        {
                            return Failure(ArtifactAdmissionFailureCode.MalformedHeader);
                        }

                        break;
                    case JsonTokenType.StartArray:
                    case JsonTokenType.EndArray:
                    case JsonTokenType.Null:
                        return Failure(ArtifactAdmissionFailureCode.MalformedHeader);
                }
            }

            return frames.Count == 0 ? null : Failure(ArtifactAdmissionFailureCode.MalformedHeader);
        }
        catch (JsonException)
        {
            return Failure(ArtifactAdmissionFailureCode.MalformedHeader);
        }
    }

    private static string? FindUnknown(JsonElement value, HashSet<string> expected)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!expected.Contains(property.Name))
            {
                return property.Name;
            }

            seen.Add(property.Name);
        }

        return seen.SetEquals(expected) ? null : throw new JsonException("A required member is missing.");
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw new JsonException($"{name} must be an object.");
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{name} must be a string.");
        }

        var result = value.GetString()!;
        if (result.Length == 0 || result.Any(static character => character is < ' ' or > '~'))
        {
            throw new JsonException($"{name} must be non-empty printable ASCII.");
        }

        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new JsonException($"{name} must be a Boolean."),
        };
    }

    private static long RequiredInt64(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : throw new JsonException($"{name} must be an integer.");
    }

    private static void RequireSha256(string value)
    {
        if (value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new JsonException("SHA-256 values must be lowercase hexadecimal.");
        }
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        RequireSha256(value);
        return value;
    }

    private static ArtifactAdmissionFailure Failure(ArtifactAdmissionFailureCode code) =>
        new(code, "admission_manifest");

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
}

internal sealed record AdmissionHeader(
    string Schema,
    string SchemaResource,
    string SchemaSha256,
    string EvidenceClass,
    bool Synthetic,
    string SourceKind,
    string EnvironmentClass,
    string EnvironmentBinding,
    string IssuerRole,
    string IssuerId,
    string KeyId,
    string PayloadSchema,
    string PayloadSchemaResource,
    string PayloadSchemaSha256,
    long PayloadBytes,
    string MediaType,
    string AttestationPurpose,
    string Algorithm,
    string SignatureFormat,
    string Signature);

internal sealed record HeaderReadResult(
    AdmissionHeader? Header,
    ArtifactAdmissionFailure? Failure)
{
    public static HeaderReadResult Accepted(AdmissionHeader header) => new(header, null);

    public static HeaderReadResult Rejected(ArtifactAdmissionFailure failure) => new(null, failure);
}
