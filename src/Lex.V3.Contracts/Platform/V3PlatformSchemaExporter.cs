using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.V3.Contracts.Platform;

public sealed record V3PlatformSchemaDocument(
    string SchemaId,
    string FileName,
    string Sha256,
    ReadOnlyMemory<byte> Utf8);

public static class V3PlatformSchemaExporter
{
    public const string EnvelopeSchemaId = "lex-v3-envelope/1";
    public const string RefusalSchemaId = "lex-v3-refusal/1";

    public static byte[] ExportRequestUtf8(string operationId) =>
        ExportOperation(operationId, "request", []);

    public static byte[] ExportResultUtf8(string operationId, IEnumerable<string> resultObjectTypes) =>
        ExportOperation(operationId, "result", resultObjectTypes);

    public static byte[] ExportEnvelopeUtf8() => ExportEnvelope();

    public static byte[] ExportRefusalUtf8() => ExportRefusal();

    /// <summary>The identity of the schema a refusal code's payload is held to (the plan's item 6.10): <c>lex-v3-{code}-payload/1</c>.</summary>
    public static string RefusalPayloadSchemaId(string refusalCode)
    {
        _ = V3OperationRegistry.ReviewedPayloadFields(refusalCode);
        return $"lex-v3-{refusalCode}-payload/1";
    }

    /// <summary>
    /// The schema a refusal code's payload is held to: an object that carries every field the registry mandates for the code,
    /// each of the type its producers send (<see cref="PayloadFieldSchema"/>), and may carry more, since every producer stands
    /// above the mandate (the refusal census, <c>refusal-payload-samples.json</c>, names the fields beyond it).
    /// </summary>
    public static byte[] ExportRefusalPayloadUtf8(string refusalCode)
    {
        var fields = V3OperationRegistry.ReviewedPayloadFields(refusalCode);
        var properties = new JsonObject();
        foreach (var field in fields)
        {
            properties[field] = PayloadFieldSchema(refusalCode, field);
        }

        return Write(new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = RefusalPayloadSchemaId(refusalCode),
            ["type"] = "object",
            ["additionalProperties"] = true,
            ["properties"] = properties,
            ["required"] = new JsonArray(fields.Select(static field => (JsonNode)field).ToArray()),
        });
    }

    public static string FileNameFor(string schemaId)
    {
        if (schemaId == EnvelopeSchemaId)
        {
            return "envelope.schema.json";
        }

        if (schemaId == RefusalSchemaId)
        {
            return "refusal.schema.json";
        }

        const string prefix = "lex-v3-";
        const string suffix = "/1";
        if (!schemaId.StartsWith(prefix, StringComparison.Ordinal) ||
            !schemaId.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unknown platform schema identity.", nameof(schemaId));
        }

        var stem = schemaId[prefix.Length..^suffix.Length].Replace('_', '-');
        return $"{stem}.schema.json";
    }

    public static IReadOnlyList<V3PlatformSchemaDocument> ExportReviewedDocuments()
    {
        var documents = new List<V3PlatformSchemaDocument>();
        foreach (var operation in V3OperationRegistry.Reviewed.Operations)
        {
            Add(operation.RequestSchema, ExportRequestUtf8(operation.OperationId));
            Add(operation.ResultSchema, ExportResultUtf8(operation.OperationId, operation.ResultObjectTypes));
        }

        Add(RefusalSchemaId, ExportRefusalUtf8());
        foreach (var refusalCode in V3OperationRegistry.Reviewed.RefusalCodes)
        {
            Add(RefusalPayloadSchemaId(refusalCode), ExportRefusalPayloadUtf8(refusalCode));
        }

        Add(EnvelopeSchemaId, ExportEnvelopeUtf8());
        return new ReadOnlyCollection<V3PlatformSchemaDocument>(documents);

        void Add(string schemaId, byte[] bytes) => documents.Add(new(
            schemaId,
            FileNameFor(schemaId),
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bytes));
    }

    internal static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] ExportOperation(
        string operationId,
        string kind,
        IEnumerable<string> resultObjectTypes)
    {
        RequireOperationId(operationId);
        if (kind is not ("request" or "result"))
        {
            throw new ArgumentException("Unknown operation schema kind.", nameof(kind));
        }

        var schemaId = $"lex-v3-{operationId}-{kind}/1";
        var properties = new JsonObject
        {
            ["operation_id"] = new JsonObject { ["const"] = operationId },
        };
        JsonArray required;
        if (kind == "request")
        {
            properties["parameters"] = RequestParameters(operationId);
            required = new("operation_id", "parameters");
        }
        else
        {
            var types = resultObjectTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (types.Length == 0)
            {
                throw new ArgumentException("A result schema needs object types.", nameof(resultObjectTypes));
            }

            properties["object_type"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(types.Select(static value => (JsonNode)value).ToArray()),
            };
            properties["value"] = ClosedObject();
            required = new("operation_id", "object_type", "value");
        }

        return Write(new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = schemaId,
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = required,
        });
    }

    private static byte[] ExportRefusal() => Write(new JsonObject
    {
        ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
        ["$id"] = RefusalSchemaId,
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["schema"] = new JsonObject { ["const"] = RefusalSchemaId },
            // The registry's closed set; each code's payload is held to its own schema, lex-v3-{code}-payload/1.
            ["code"] = OneOf(V3OperationRegistry.ReviewedRefusalCodes),
            ["helpful_payload"] = ClosedObject(),
        },
        ["required"] = new JsonArray("schema", "code", "helpful_payload"),
    });

    private static byte[] ExportEnvelope() => Write(new JsonObject
    {
        ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
        ["$id"] = EnvelopeSchemaId,
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["schema"] = new JsonObject { ["const"] = EnvelopeSchemaId },
            ["version"] = new JsonObject { ["const"] = V3OperationRegistry.Version },
            ["object_type"] = new JsonObject { ["const"] = "envelope" },
            ["request_ref"] = new JsonObject { ["type"] = "string", ["maxLength"] = 128 },
            ["operation_id"] = new JsonObject { ["type"] = "string" },
            ["registry_schema"] = new JsonObject { ["const"] = V3OperationRegistry.Schema },
            ["registry_sha256"] = Hash(),
            ["context"] = new JsonObject { ["type"] = "object" },
            ["verdict"] = new JsonObject { ["type"] = "string" },
            ["result"] = new JsonObject { ["type"] = new JsonArray("object", "null") },
            ["refusal"] = new JsonObject { ["type"] = new JsonArray("object", "null") },
        },
        ["required"] = new JsonArray(
            "schema", "version", "object_type", "request_ref", "operation_id",
            "registry_schema", "registry_sha256", "context", "verdict", "result", "refusal"),
    });

    /// <summary>
    /// The reviewed parameter shape of each operation whose request document says what the operation
    /// requires. An operation absent here still admits any parameters object, which is recorded as
    /// open work: "validated against the reviewed schema document" means nothing for it yet.
    /// </summary>
    private static JsonObject RequestParameters(string operationId) => operationId switch
    {
        "resolve" => Parameters(
            ["identifier"],
            ("identifier", NonBlankString())),
        "as_of" => Parameters(
            ["identifier", "date"],
            ("identifier", NonBlankString()),
            ("date", CivilDate()),
            ("language", NonBlankString())),
        "timeline" => Parameters(
            ["identifier"],
            ("identifier", NonBlankString()),
            ("language", NonBlankString())),
        "dossier" => Parameters(
            ["identifier"],
            ("identifier", NonBlankString()),
            ("language", NonBlankString())),
        "article_history" => Parameters(
            ["identifier", "anchor"],
            ("identifier", NonBlankString()),
            ("anchor", NonBlankString()),
            ("language", NonBlankString())),
        "diff" => Parameters(
            ["identifier", "date_from", "date_to"],
            ("identifier", NonBlankString()),
            ("date_from", CivilDate()),
            ("date_to", CivilDate()),
            ("language", NonBlankString()),
            ("page", PageNumber())),
        "changes_in_period" => Parameters(
            ["date_from", "date_to"],
            ("date_from", CivilDate()),
            ("date_to", CivilDate()),
            ("identifier", NonBlankString()),
            ("language", NonBlankString()),
            ("limit", RowLimit())),
        "in_force_on" => Parameters(
            ["date"],
            ("date", CivilDate()),
            ("identifier", NonBlankString()),
            ("language", NonBlankString()),
            ("limit", RowLimit()),
            ("after_work_key", NonBlankString())),
        "search" => Parameters(
            ["query", "language"],
            ("query", BoundedQuery()),
            ("language", NonBlankString()),
            ("date", CivilDate()),
            ("identifier", NonBlankString()),
            ("mode", NonBlankString()),
            ("limit", RowLimit()),
            ("after", NonBlankString())),
        "coverage" => Parameters(
            [],
            ("language", NonBlankString())),
        "provenance" => Parameters(
            ["identifier", "date"],
            ("identifier", NonBlankString()),
            ("date", CivilDate()),
            ("language", NonBlankString())),
        "cited_by" => Parameters(
            ["identifier"],
            ("identifier", NonBlankString()),
            ("limit", RowLimit()),
            ("after", NonBlankString())),
        "citation" => Parameters(
            ["identifier", "date"],
            ("identifier", NonBlankString()),
            ("date", CivilDate()),
            ("language", NonBlankString()),
            ("anchor", NonBlankString()),
            ("limit", RowLimit()),
            ("after", NonBlankString())),
        "verify" => Parameters(
            ["identifier"],
            ("identifier", NonBlankString()),
            ("language", NonBlankString())),
        "relations" => Parameters(
            ["identifier"],
            ("identifier", NonBlankString()),
            ("date", CivilDate()),
            ("language", NonBlankString()),
            ("direction", NonBlankString()),
            ("limit", RowLimit()),
            ("after", NonBlankString())),
        "evidence_bundle" => Parameters(
            ["identifier", "date"],
            ("identifier", NonBlankString()),
            ("date", CivilDate()),
            ("language", NonBlankString()),
            ("article", NonBlankString()),
            ("page", PageNumber()),
            ("contents", new JsonObject { ["type"] = "boolean" })),
        "classification" => Parameters(
            ["identifier"],
            ("identifier", NonBlankString()),
            ("language", NonBlankString())),
        "manifestation" => Parameters(
            ["identifier"],
            ("identifier", NonBlankString()),
            ("language", NonBlankString()),
            ("format", NonBlankString())),
        "status_on" => Parameters(
            ["identifier", "date"],
            ("identifier", NonBlankString()),
            ("date", CivilDate()),
            ("language", NonBlankString())),
        "browse" => Parameters(
            [],
            ("type", NonBlankString()),
            ("language", NonBlankString()),
            ("limit", RowLimit()),
            ("after", NonBlankString())),
        "ask" => Parameters(
            ["question"],
            ("question", NonBlankString())),
        "events" => Parameters(
            [],
            ("after", EventCursor()),
            ("limit", RowLimit()),
            ("event", OneOf(V3EventRegistry.Mintable))),
        "answer_drift" => Parameters(
            [],
            ("identifier", NonBlankString()),
            ("after", EventCursor()),
            ("limit", RowLimit())),
        "as_observed" => Parameters(
            ["identifier", "date"],
            ("identifier", NonBlankString()),
            ("date", CivilDate()),
            ("language", NonBlankString()),
            ("snapshot", Hash()),
            ("at", NonBlankString())),
        _ => ClosedObject(),
    };

    private static JsonObject Parameters(
        string[] required,
        params (string Name, JsonObject Schema)[] properties)
    {
        var declared = new JsonObject();
        foreach (var (name, schema) in properties)
        {
            declared[name] = schema;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = declared,
            ["required"] = new JsonArray(required.Select(static value => (JsonNode)value).ToArray()),
        };
    }

    private static JsonObject NonBlankString() => new()
    {
        ["type"] = "string",
        ["minLength"] = 1,
        ["pattern"] = "\\S",
    };

    /// <summary>
    /// A search query: non-blank, and no longer than the operation's ceiling, which the mount holds
    /// again for a caller that reaches it without this document.
    /// </summary>
    private static JsonObject BoundedQuery() => new()
    {
        ["type"] = "string",
        ["minLength"] = 1,
        ["maxLength"] = 512,
        ["pattern"] = "\\S",
    };

    /// <summary>
    /// A civil date in the publisher's own <c>yyyy-MM-dd</c> spelling. Calendar validity is the
    /// operation's check, answered as the same request-schema rejection.
    /// </summary>
    private static JsonObject CivilDate() => new()
    {
        ["type"] = "string",
        ["pattern"] = "^[0-9]{4}-[0-9]{2}-[0-9]{2}$",
    };

    /// <summary>How many rows a windowed answer may carry; the operation states its own ceiling.</summary>
    private static JsonObject RowLimit() => new()
    {
        ["type"] = "integer",
        ["minimum"] = 1,
        ["maximum"] = 200,
    };

    /// <summary>
    /// A page of a version's reading, counted from 1 (<c>evidence_bundle</c>), or of a comparison's wording (<c>diff</c>, R57):
    /// the operation refuses a page past the last as an anchor the version does not hold (<c>anchor_not_in_version</c>),
    /// naming the pages.
    /// </summary>
    private static JsonObject PageNumber() => new()
    {
        ["type"] = "integer",
        ["minimum"] = 1,
        ["maximum"] = 100000,
    };

    /// <summary>
    /// A position in an event log: the log's identity (the digest of the index that holds it) and the
    /// last sequence number read, so a cursor from another log is told apart rather than reinterpreted.
    /// </summary>
    private static JsonObject EventCursor() => new()
    {
        ["type"] = "string",
        ["pattern"] = "^[0-9a-f]{64}:(0|[1-9][0-9]{0,17})$",
    };

    /// <summary>
    /// The schema of one field a refusal code's payload must carry: the JSON type every producer sends it as, read from the
    /// producers (a survey of every construction in <c>src/Lex.V3.Api</c>, 2026-10-27) and from the refusal census. A field may be
    /// null only where a producer says "none" with null (the nearest states and the history's beginning of
    /// <c>no_version_for_date</c>). A code no operation produces at launch (the census's <c>not_produced</c>) states the type its
    /// meaning gives, and says so.
    /// </summary>
    private static JsonObject PayloadFieldSchema(string refusalCode, string field) => (refusalCode, field) switch
    {
        ("advice_boundary", "descriptive_maximum") => NotProduced(new JsonObject()),
        ("advice_boundary", "handoff") => NotProduced(new JsonObject()),
        ("ambiguous_identifier", "requested_identifier") => Text(),
        ("ambiguous_identifier", "candidates") => Texts(minItems: 1),
        ("ambiguous_version", "requested_date") => Text(),
        ("ambiguous_version", "candidates") => Texts(minItems: 1),
        ("anchor_not_in_version", "requested_anchor") => Text(),
        ("anchor_not_in_version", "nearest_anchors") => Texts(minItems: 1),
        ("anchor_not_in_version", "do_not_fall_back_to_full_text_search") => new JsonObject { ["const"] = true },
        ("derivation_refused", "derivation") => NotProduced(Text()),
        ("derivation_refused", "reason") => NotProduced(Text()),
        ("format_not_available", "requested_format") => Text(),
        ("format_not_available", "available_formats") => Texts(),
        ("identifier_unknown", "requested_identifier") => Text(),
        ("identifier_unknown", "official_search_actions") => Texts(minItems: 1),
        ("identifier_unknown", "what_would_answer") => new JsonObject
        {
            ["type"] = "array",
            ["minItems"] = 1,
            ["items"] = OneOf(["corrected_identifier", "new_official_observation", "expanded_official_scope"]),
        },
        ("language_not_available", "requested_language") => Text(),
        ("language_not_available", "available_languages") => Texts(),
        ("no_corpus_mounted", "required_corpus") => OneOf(["lu", "eu"]),
        ("no_version_for_date", "requested_date") => Text(),
        ("no_version_for_date", "history_begins") => TextOrNull(),
        ("no_version_for_date", "nearest_earlier") => TextOrNull(),
        ("no_version_for_date", "nearest_later") => TextOrNull(),
        ("not_transposable", "instrument_type") => NotProduced(Text()),
        ("not_transposable", "explanation") => NotProduced(Text()),
        ("out_of_corpus_scope", "requested_identifier") => Text(),
        ("out_of_corpus_scope", "official_source") => Link(),
        ("out_of_corpus_scope", "reason") => Text(),
        ("out_of_corpus_scope", "who_to_ask") => new JsonObject
        {
            // An institution or an official service, never a person (the owner's decision of 2026-10-19).
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["name"] = Text(),
                ["link"] = Link(),
                ["role"] = OneOf(["information"]),
            },
            ["required"] = new JsonArray("name", "link", "role"),
        },
        ("pinned_digest_mismatch", "requested_digest") => Text(),
        ("pinned_digest_mismatch", "current_digest") => Text(),
        ("pinned_digest_mismatch", "stable_coordinate") => Text(),
        ("pinned_digest_mismatch", "current_hash_pinned_url") => Text(),
        ("profiles_differ", "left_profile") => Texts(minItems: 1),
        ("profiles_differ", "right_profile") => Texts(minItems: 1),
        // The per-client limit's refusal (R109, V3ClientLimiter): the whole seconds to wait, as Retry-After says them, and true.
        ("rate_limited", "retry_after") => new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
        ("rate_limited", "retryable") => new JsonObject { ["const"] = true },
        ("retrieval_mode_unavailable", "requested_mode") => Text(),
        ("retrieval_mode_unavailable", "available_modes") => Texts(minItems: 1),
        ("snapshot_unknown", "snapshot_id") => Text(),
        ("snapshot_unknown", "what_would_answer") => Text(),
        ("text_not_available", "official_identity") => Text(),
        ("text_not_available", "official_source") => Text(),
        ("text_not_available", "retained_transport_evidence") => Text(),
        ("text_withheld", "official_identity") => Text(),
        ("text_withheld", "official_link") => Text(),
        ("text_withheld", "content_sha256") => Text(),
        ("upstream_unreachable", "upstream") => NotProduced(Text()),
        ("upstream_unreachable", "retryable") => NotProduced(new JsonObject { ["const"] = true }),
        _ => throw new InvalidOperationException($"The registry's field {field} of {refusalCode} has no payload schema."),
    };

    private static JsonObject Text() => new() { ["type"] = "string" };

    private static JsonObject TextOrNull() => new() { ["type"] = new JsonArray("string", "null") };

    private static JsonObject Texts(int minItems = 0)
    {
        var schema = new JsonObject { ["type"] = "array" };
        if (minItems > 0)
        {
            schema["minItems"] = minItems;
        }

        schema["items"] = Text();
        return schema;
    }

    /// <summary>An official site's address: an absolute https link.</summary>
    private static JsonObject Link() => new() { ["type"] = "string", ["pattern"] = "^https://\\S{1,2048}$" };

    /// <summary>A field of a code no operation produces at launch: its type as the registry means it, said in a comment.</summary>
    private static JsonObject NotProduced(JsonObject schema)
    {
        schema["$comment"] = "no operation produces this refusal at launch (the refusal census's not_produced); the type is the registry's meaning";
        return schema;
    }

    /// <summary>A string that is one of a closed list of values.</summary>
    private static JsonObject OneOf(IEnumerable<string> values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(values.Select(static value => (JsonNode)value).ToArray()),
    };

    private static JsonObject ClosedObject() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = true,
    };

    private static JsonObject Hash() => new()
    {
        ["type"] = "string",
        ["pattern"] = "^[0-9a-f]{64}$",
    };

    private static byte[] Write(JsonObject root)
    {
        var json = root.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        return Encoding.UTF8.GetBytes(json);
    }

    private static void RequireOperationId(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) ||
            operationId.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) ||
            operationId != operationId.ToLowerInvariant())
        {
            throw new ArgumentException("A lowercase operation id is required.", nameof(operationId));
        }
    }
}
