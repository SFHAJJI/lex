using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.V3.Contracts.Platform;

public enum V3EnvelopeProjectionKind
{
    Rest = 1,
    Mcp = 2,
}

public static class V3EnvelopeJson
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        Encoder = JavaScriptEncoder.Default,
        WriteIndented = false,
    };

    public static byte[] ProjectRest(V3Envelope envelope, V3OperationRegistry registry) =>
        Project(envelope, registry, V3EnvelopeProjectionKind.Rest);

    public static byte[] ProjectMcp(V3Envelope envelope, V3OperationRegistry registry) =>
        Project(envelope, registry, V3EnvelopeProjectionKind.Mcp);

    public static byte[] Project(
        V3Envelope envelope,
        V3OperationRegistry registry,
        V3EnvelopeProjectionKind projection)
    {
        if (projection is not (V3EnvelopeProjectionKind.Rest or V3EnvelopeProjectionKind.Mcp))
        {
            throw new ArgumentOutOfRangeException(nameof(projection));
        }

        var builder = new V3EnvelopeBuilder(registry);
        builder.VerifyBinding(envelope);
        var root = new JsonObject
        {
            ["schema"] = envelope.Schema,
            ["version"] = envelope.Version,
            ["object_type"] = envelope.ObjectType,
            ["request_ref"] = envelope.RequestRef,
            ["operation_id"] = envelope.OperationId,
            ["registry_schema"] = envelope.RegistrySchema,
            ["registry_sha256"] = envelope.RegistrySha256,
            ["context"] = ContextNode(envelope.Context),
            ["verdict"] = envelope.Verdict,
            ["result"] = envelope.Result is null ? null : new JsonObject
            {
                ["schema"] = envelope.Result.Schema,
                ["object_type"] = envelope.Result.ObjectType,
                ["value"] = ParseNode(envelope.Result.Value),
            },
            ["refusal"] = envelope.Refusal is null ? null : new JsonObject
            {
                ["schema"] = envelope.Refusal.Schema,
                ["code"] = envelope.Refusal.Code,
                ["helpful_payload"] = ParseNode(envelope.Refusal.HelpfulPayload),
            },
        };
        var canonical = Sort(root).ToJsonString(OutputOptions) + "\n";
        return Encoding.UTF8.GetBytes(canonical);
    }

    public static V3Envelope ParseAndVerify(
        ReadOnlySpan<byte> utf8,
        V3OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        using var document = JsonDocument.Parse(
            utf8.ToArray(),
            new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        var root = document.RootElement;
        RequireObjectMembers(root,
            "schema", "version", "object_type", "request_ref", "operation_id",
            "registry_schema", "registry_sha256", "context", "verdict", "result", "refusal");
        Require(root, "schema", V3EnvelopeBuilder.Schema);
        Require(root, "version", V3OperationRegistry.Version);
        Require(root, "object_type", "envelope");
        Require(root, "registry_schema", V3OperationRegistry.Schema);
        Require(root, "registry_sha256", registry.Sha256);

        var requestRef = String(root, "request_ref");
        var operationId = String(root, "operation_id");
        var context = ReadContext(root.GetProperty("context"));
        var verdict = String(root, "verdict");
        var builder = new V3EnvelopeBuilder(registry);
        V3Envelope envelope;
        if (root.GetProperty("refusal").ValueKind == JsonValueKind.Null)
        {
            var result = root.GetProperty("result");
            RequireObjectMembers(result, "schema", "object_type", "value");
            envelope = builder.Success(
                requestRef,
                operationId,
                context,
                verdict,
                String(result, "schema"),
                String(result, "object_type"),
                result.GetProperty("value"));
        }
        else
        {
            if (root.GetProperty("result").ValueKind != JsonValueKind.Null)
            {
                throw new JsonException("An envelope cannot carry result and refusal branches.");
            }

            var refusal = root.GetProperty("refusal");
            RequireObjectMembers(refusal, "schema", "code", "helpful_payload");
            envelope = builder.Refusal(
                requestRef,
                operationId,
                context,
                String(refusal, "schema"),
                String(refusal, "code"),
                refusal.GetProperty("helpful_payload"));
        }

        builder.VerifyBinding(envelope);
        var canonical = ProjectRest(envelope, registry);
        if (!utf8.SequenceEqual(canonical))
        {
            throw new JsonException("The envelope bytes are not the canonical REST/MCP representation.");
        }

        return envelope;
    }

    private static JsonObject ContextNode(V3EnvelopeContext context) => new()
    {
        ["publisher"] = context.Publisher switch
        {
            PublisherId.LuLegilux => "lu-legilux",
            PublisherId.EuEurLex => "eu-eurlex",
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        },
        ["status"] = context.Status,
        ["timeline_semantics"] = context.TimelineSemantics switch
        {
            TimelineSemantics.PublisherApplicability => "publisher_applicability",
            TimelineSemantics.OfficialConsolidationState => "official_consolidation_state",
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        },
        ["snapshot"] = new JsonObject
        {
            ["snapshot_id"] = context.Snapshot.SnapshotId,
            ["snapshot_sha256"] = context.Snapshot.SnapshotSha256,
        },
        ["jurisdiction"] = context.Jurisdiction,
        ["provisional"] = context.Provisional,
        ["freshness"] = new JsonObject
        {
            ["observed_at"] = context.Freshness.ObservedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["upstream_health"] = context.Freshness.UpstreamHealth,
        },
    };

    private static V3EnvelopeContext ReadContext(JsonElement value)
    {
        RequireObjectMembers(value,
            "publisher", "status", "timeline_semantics", "snapshot", "jurisdiction", "provisional", "freshness");
        var snapshot = value.GetProperty("snapshot");
        RequireObjectMembers(snapshot, "snapshot_id", "snapshot_sha256");
        var freshness = value.GetProperty("freshness");
        RequireObjectMembers(freshness, "observed_at", "upstream_health");
        var publisher = String(value, "publisher") switch
        {
            "lu-legilux" => PublisherId.LuLegilux,
            "eu-eurlex" => PublisherId.EuEurLex,
            _ => throw new JsonException("Unknown publisher."),
        };
        var timeline = String(value, "timeline_semantics") switch
        {
            "publisher_applicability" => TimelineSemantics.PublisherApplicability,
            "official_consolidation_state" => TimelineSemantics.OfficialConsolidationState,
            _ => throw new JsonException("Unknown timeline semantics."),
        };
        if (!DateTimeOffset.TryParseExact(
                String(freshness, "observed_at"),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var observedAt))
        {
            throw new JsonException("Invalid freshness timestamp.");
        }

        return new V3EnvelopeContext(
            publisher,
            String(value, "status"),
            timeline,
            new V3SnapshotReference(String(snapshot, "snapshot_id"), String(snapshot, "snapshot_sha256")),
            String(value, "jurisdiction"),
            value.GetProperty("provisional").GetBoolean(),
            new V3Freshness(observedAt, String(freshness, "upstream_health")));
    }

    private static JsonNode Sort(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(static member => member.Key, StringComparer.Ordinal)
            .Select(static member => KeyValuePair.Create(member.Key, member.Value is null ? null : Sort(member.Value)))),
        JsonArray value => new JsonArray(value.Select(static item => item is null ? null : Sort(item)).ToArray()),
        _ => node.DeepClone(),
    };

    private static JsonNode ParseNode(JsonElement value) =>
        JsonNode.Parse(value.GetRawText()) ?? throw new JsonException("A JSON value is required.");

    private static string String(JsonElement value, string propertyName) =>
        value.GetProperty(propertyName).GetString() ?? throw new JsonException($"{propertyName} must be a string.");

    private static void Require(JsonElement value, string propertyName, string expected)
    {
        if (!string.Equals(String(value, propertyName), expected, StringComparison.Ordinal))
        {
            throw new JsonException($"Unexpected {propertyName}.");
        }
    }

    private static void RequireObjectMembers(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new JsonException("The object does not have the exact closed member set.");
        }
    }
}
