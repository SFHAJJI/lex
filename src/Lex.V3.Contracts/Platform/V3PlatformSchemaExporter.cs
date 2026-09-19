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
            ["code"] = new JsonObject { ["type"] = "string" },
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
        "article_history" => Parameters(
            ["identifier", "anchor"],
            ("identifier", NonBlankString()),
            ("anchor", NonBlankString()),
            ("language", NonBlankString())),
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
    /// A civil date in the publisher's own <c>yyyy-MM-dd</c> spelling. Calendar validity is the
    /// operation's check, answered as the same request-schema rejection.
    /// </summary>
    private static JsonObject CivilDate() => new()
    {
        ["type"] = "string",
        ["pattern"] = "^[0-9]{4}-[0-9]{2}-[0-9]{2}$",
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
