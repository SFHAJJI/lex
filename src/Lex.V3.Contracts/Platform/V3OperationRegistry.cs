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
        RequestSchemaSha256 = V3PlatformSchemaExporter.Sha256(
            V3PlatformSchemaExporter.ExportRequestUtf8(OperationId));
        ResultSchemaSha256 = V3PlatformSchemaExporter.Sha256(
            V3PlatformSchemaExporter.ExportResultUtf8(OperationId, ResultObjectTypes));
    }

    public string OperationId { get; }

    public string RequestSchema { get; }

    public string ResultSchema { get; }

    public string RefusalSchema { get; }

    public string RequestSchemaSha256 { get; }

    public string ResultSchemaSha256 { get; }

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

    private static readonly string[] RequiredOperationIds =
    [
        "answer_drift",
        "article_history",
        "as_observed",
        "as_of",
        "ask",
        "browse",
        "changes_in_period",
        "citation",
        "cited_by",
        "classification",
        "concepts",
        "coverage",
        "diff",
        "dossier",
        "events",
        "evidence_bundle",
        "in_force_on",
        "knowable_on",
        "manifestation",
        "provenance",
        "relations",
        "resolve",
        "search",
        "status_on",
        "timeline",
        "transposition",
        "verify",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> MandatoryRefusalPayloadFields =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["advice_boundary"] = ["descriptive_maximum", "handoff"],
            ["ambiguous_identifier"] = ["requested_identifier", "candidates"],
            ["ambiguous_version"] = ["requested_date", "candidates"],
            ["anchor_not_in_version"] =
                ["requested_anchor", "nearest_anchors", "do_not_fall_back_to_full_text_search"],
            ["derivation_refused"] = ["derivation", "reason"],
            ["format_not_available"] = ["requested_format", "available_formats"],
            ["identifier_unknown"] =
                ["requested_identifier", "official_search_actions", "what_would_answer"],
            ["language_not_available"] = ["requested_language", "available_languages"],
            ["no_corpus_mounted"] = ["required_corpus"],
            ["no_version_for_date"] =
                ["requested_date", "history_begins", "nearest_earlier", "nearest_later"],
            ["not_transposable"] = ["instrument_type", "explanation"],
            ["out_of_corpus_scope"] = ["requested_identifier", "official_source"],
            ["pinned_digest_mismatch"] =
                ["requested_digest", "current_digest", "stable_coordinate", "current_hash_pinned_url"],
            ["profiles_differ"] = ["left_profile", "right_profile"],
            ["rate_limited"] = ["retry_after", "retryable"],
            ["retrieval_mode_unavailable"] = ["requested_mode", "available_modes"],
            ["snapshot_unknown"] = ["snapshot_id", "what_would_answer"],
            ["text_not_available"] =
                ["official_identity", "official_source", "retained_transport_evidence"],
            ["text_withheld"] = ["official_identity", "official_link", "content_sha256"],
            ["upstream_unreachable"] = ["upstream", "retryable"],
        };

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

        if (!entries.Select(entry => entry.OperationId).SequenceEqual(RequiredOperationIds, StringComparer.Ordinal))
        {
            throw new ArgumentException("The production registry must contain the complete reviewed operation set.", nameof(operations));
        }

        var refusals = refusalCodes?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            ?? throw new ArgumentNullException(nameof(refusalCodes));
        if (!refusals.SequenceEqual(RequiredRefusalCodes, StringComparer.Ordinal))
        {
            throw new ArgumentException("The production registry must contain the complete reviewed refusal set.", nameof(refusalCodes));
        }

        if (!MandatoryRefusalPayloadFields.Keys.Order(StringComparer.Ordinal)
                .SequenceEqual(RequiredRefusalCodes, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Every refusal must declare exactly one mandatory payload shape.");
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

    public string EnvelopeSchemaSha256 { get; } = V3PlatformSchemaExporter.Sha256(
        V3PlatformSchemaExporter.ExportEnvelopeUtf8());

    public string RefusalSchemaSha256 { get; } = V3PlatformSchemaExporter.Sha256(
        V3PlatformSchemaExporter.ExportRefusalUtf8());

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

    public IReadOnlyList<string> MandatoryPayloadFields(string refusalCode)
    {
        if (!MandatoryRefusalPayloadFields.TryGetValue(refusalCode, out var fields))
        {
            throw new ArgumentException("The refusal code is not declared by the registry.", nameof(refusalCode));
        }

        return Array.AsReadOnly(fields.ToArray());
    }

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

        foreach (var operationId in RequiredOperationIds)
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
            writer.WriteString("envelope_schema", V3PlatformSchemaExporter.EnvelopeSchemaId);
            writer.WriteString(
                "envelope_schema_sha256",
                V3PlatformSchemaExporter.Sha256(V3PlatformSchemaExporter.ExportEnvelopeUtf8()));
            writer.WriteString("refusal_schema", V3PlatformSchemaExporter.RefusalSchemaId);
            writer.WriteString(
                "refusal_schema_sha256",
                V3PlatformSchemaExporter.Sha256(V3PlatformSchemaExporter.ExportRefusalUtf8()));
            writer.WriteStartArray("operations");
            foreach (var operation in operations)
            {
                writer.WriteStartObject();
                writer.WriteString("operation_id", operation.OperationId);
                writer.WriteString("request_schema", operation.RequestSchema);
                writer.WriteString("request_schema_sha256", operation.RequestSchemaSha256);
                writer.WriteString("result_schema", operation.ResultSchema);
                writer.WriteString("result_schema_sha256", operation.ResultSchemaSha256);
                writer.WriteString("refusal_schema", operation.RefusalSchema);
                writer.WriteString(
                    "refusal_schema_sha256",
                    V3PlatformSchemaExporter.Sha256(V3PlatformSchemaExporter.ExportRefusalUtf8()));
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
                writer.WriteStartObject();
                writer.WriteString("code", refusalCode);
                writer.WriteStartArray("mandatory_payload_fields");
                foreach (var field in MandatoryRefusalPayloadFields[refusalCode].Order(StringComparer.Ordinal))
                {
                    writer.WriteStringValue(field);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
