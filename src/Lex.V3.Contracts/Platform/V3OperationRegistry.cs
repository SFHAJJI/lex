using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform;

public sealed class V3OperationDefinition
{
    public V3OperationDefinition(
        string operationId,
        string requestSchema,
        string resultSchema,
        string refusalSchema,
        IEnumerable<string> resultObjectTypes)
    {
        OperationId = RequireToken(operationId, nameof(operationId));
        RequestSchema = RequireSchema(requestSchema, $"lex-v3-{operationId}-request/1", nameof(requestSchema));
        ResultSchema = RequireSchema(resultSchema, $"lex-v3-{operationId}-result/1", nameof(resultSchema));
        RefusalSchema = RequireSchema(refusalSchema, V3OperationRegistry.RefusalSchema, nameof(refusalSchema));

        var objectTypes = resultObjectTypes?.Select(value => RequireToken(value, nameof(resultObjectTypes)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray() ?? throw new ArgumentNullException(nameof(resultObjectTypes));
        if (objectTypes.Length == 0)
        {
            throw new ArgumentException("An operation must declare at least one result object type.", nameof(resultObjectTypes));
        }

        ResultObjectTypes = Array.AsReadOnly(objectTypes);
    }

    public string OperationId { get; }

    public string RequestSchema { get; }

    public string ResultSchema { get; }

    public string RefusalSchema { get; }

    public ReadOnlyCollection<string> ResultObjectTypes { get; }

    private static string RequireSchema(string value, string expected, string parameterName)
    {
        if (!string.Equals(value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Expected schema '{expected}'.", parameterName);
        }

        return value;
    }

    private static string RequireToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("A lowercase wire token is required.", parameterName);
        }

        if (!string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Wire tokens must be lowercase.", parameterName);
        }

        return value;
    }
}

public sealed class V3OperationRegistry
{
    public const string Schema = "lex-v3-operation-registry/1";
    public const string Version = "v3";
    public const string RefusalSchema = "lex-v3-refusal/1";

    private static readonly string[] RequiredRefusalCodes =
    [
        "advice_boundary",
        "ambiguous_identifier",
        "ambiguous_version",
        "anchor_not_in_version",
        "derivation_refused",
        "format_not_available",
        "identifier_unknown",
        "language_not_available",
        "no_corpus_mounted",
        "no_version_for_date",
        "not_transposable",
        "out_of_corpus_scope",
        "pinned_digest_mismatch",
        "profiles_differ",
        "rate_limited",
        "retrieval_mode_unavailable",
        "snapshot_unknown",
        "text_not_available",
        "text_withheld",
        "upstream_unreachable",
    ];

    private readonly IReadOnlyDictionary<string, V3OperationDefinition> _byId;
    private readonly HashSet<string> _refusalCodes;
    private readonly byte[] _canonicalUtf8;

    public V3OperationRegistry(
        string schema,
        string version,
        IEnumerable<V3OperationDefinition> operations,
        IEnumerable<string> refusalCodes)
    {
        if (!string.Equals(schema, Schema, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unknown operation-registry schema.", nameof(schema));
        }

        if (!string.Equals(version, Version, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unknown V3 platform version.", nameof(version));
        }

        var entries = operations?.OrderBy(entry => entry.OperationId, StringComparer.Ordinal).ToArray()
            ?? throw new ArgumentNullException(nameof(operations));
        if (entries.Length != entries.Select(entry => entry.OperationId).Distinct(StringComparer.Ordinal).Count())
        {
            throw new ArgumentException("Operation identifiers must be unique.", nameof(operations));
        }

        var expectedOperations = V3ContractVocabulary.OperationIds.Order(StringComparer.Ordinal).ToArray();
        if (!entries.Select(entry => entry.OperationId).SequenceEqual(expectedOperations, StringComparer.Ordinal))
        {
            throw new ArgumentException("The production registry must contain the complete reviewed operation set.", nameof(operations));
        }

        var refusals = refusalCodes?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            ?? throw new ArgumentNullException(nameof(refusalCodes));
        if (!refusals.SequenceEqual(RequiredRefusalCodes, StringComparer.Ordinal))
        {
            throw new ArgumentException("The production registry must contain the complete reviewed refusal set.", nameof(refusalCodes));
        }

        SchemaId = schema;
        PlatformVersion = version;
        Operations = Array.AsReadOnly(entries);
        RefusalCodes = Array.AsReadOnly(refusals);
        _byId = entries.ToDictionary(entry => entry.OperationId, StringComparer.Ordinal);
        _refusalCodes = new HashSet<string>(refusals, StringComparer.Ordinal);
        _canonicalUtf8 = WriteCanonicalUtf8(entries, refusals);
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(_canonicalUtf8));
    }

    public string SchemaId { get; }

    public string PlatformVersion { get; }

    public ReadOnlyCollection<V3OperationDefinition> Operations { get; }

    public ReadOnlyCollection<string> RefusalCodes { get; }

    public byte[] CanonicalUtf8 => _canonicalUtf8.ToArray();

    public string Sha256 { get; }

    public static V3OperationRegistry Reviewed { get; } = new(
        Schema,
        Version,
        CreateReviewedOperations(),
        RequiredRefusalCodes);

    public V3OperationDefinition Operation(string operationId)
    {
        if (!_byId.TryGetValue(operationId, out var operation))
        {
            throw new ArgumentException("Unknown V3 operation.", nameof(operationId));
        }

        return operation;
    }

    public bool DeclaresRefusal(string refusalCode) => _refusalCodes.Contains(refusalCode);

    private static IEnumerable<V3OperationDefinition> CreateReviewedOperations()
    {
        var objectTypes = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["resolve"] = ["work_resolution"],
            ["search"] = ["quote", "work_record"],
            ["browse"] = ["classification", "work_record"],
            ["concepts"] = ["classification"],
            ["dossier"] = ["work_record"],
            ["manifestation"] = ["manifestation"],
            ["as_of"] = ["quote", "version_state"],
            ["as_observed"] = ["quote", "version_state"],
            ["knowable_on"] = ["quote", "version_state"],
            ["timeline"] = ["timeline"],
            ["article_history"] = ["provision_history"],
            ["diff"] = ["diff"],
            ["status_on"] = ["version_state"],
            ["in_force_on"] = ["version_state"],
            ["changes_in_period"] = ["change_list"],
            ["relations"] = ["relation_edge"],
            ["classification"] = ["classification"],
            ["cited_by"] = ["relation_edge"],
            ["citation"] = ["relation_edge"],
            ["transposition"] = ["relation_edge"],
            ["provenance"] = ["provenance_chain"],
            ["verify"] = ["verification"],
            ["evidence_bundle"] = ["evidence_bundle"],
            ["events"] = ["event"],
            ["answer_drift"] = ["answer_drift"],
            ["coverage"] = ["coverage_report"],
            ["ask"] = ["answer_dossier", "handoff_card"],
        };

        foreach (var operationId in V3ContractVocabulary.OperationIds)
        {
            yield return new V3OperationDefinition(
                operationId,
                $"lex-v3-{operationId}-request/1",
                $"lex-v3-{operationId}-result/1",
                RefusalSchema,
                objectTypes[operationId]);
        }
    }

    private static byte[] WriteCanonicalUtf8(
        IReadOnlyList<V3OperationDefinition> operations,
        IReadOnlyList<string> refusalCodes)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("version", Version);
            writer.WriteStartArray("operations");
            foreach (var operation in operations)
            {
                writer.WriteStartObject();
                writer.WriteString("operation_id", operation.OperationId);
                writer.WriteString("request_schema", operation.RequestSchema);
                writer.WriteString("result_schema", operation.ResultSchema);
                writer.WriteString("refusal_schema", operation.RefusalSchema);
                writer.WriteStartArray("result_object_types");
                foreach (var objectType in operation.ResultObjectTypes)
                {
                    writer.WriteStringValue(objectType);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("refusal_codes");
            foreach (var refusalCode in refusalCodes)
            {
                writer.WriteStringValue(refusalCode);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
