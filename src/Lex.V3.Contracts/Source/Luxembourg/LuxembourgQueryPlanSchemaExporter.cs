using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public static class LuxembourgQueryPlanSchemaExporter
{
    public const string FileName = "lex-lu-query-plan.schema.json";
    public const string ResourceId = "urn:uuid:65ff531a-8db0-4723-b3d8-2fc9c8e91c10";

    public static byte[] ExportUtf8()
    {
        var root = ContractJson.CreateSchemaOptions().GetJsonSchemaAsNode(
                typeof(LuxembourgQueryPlan),
                new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }) as JsonObject
            ?? throw new InvalidOperationException("The LU query-plan schema root must be an object.");
        root["$id"] = ResourceId;
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        SourceCoreSchemaHardener.ApplyValueObject(root);
        LuxembourgQueryPlanSchemaHardener.Apply(root);
        var json = root.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(json.TrimEnd('\r', '\n') + "\n");
    }
}

internal static class LuxembourgQueryPlanSchemaHardener
{
    public static void Apply(JsonObject root)
    {
        var properties = Object(root, "properties");
        Object(properties, "schema")["const"] = LuxembourgQueryPlan.SchemaId;
        var graph = Object(Object(properties, "dataset_graph_identity"), "properties");
        Object(graph, "endpoint")["const"] = LuxembourgQueryPlan.PublisherEndpoint;
        UniqueNonempty(properties, "scheme_roots");
        UniqueNonempty(properties, "selector_predicates");
        UniqueNonempty(properties, "relation_predicates");
        UniqueNonempty(properties, "set_definitions");
        UniqueNonempty(properties, "query_templates");

        var keyset = Object(Object(properties, "keyset_successor_rule"), "properties");
        Object(keyset, "comparison")["const"] = "strict_greater_than";
        Object(keyset, "order_by")["const"] = "canonical_utf8_tuple_ascending";
        Object(keyset, "component_count")["const"] = 6;
        Object(keyset, "empty_successor_required")["const"] = true;

        var traversal = Object(Object(properties, "page_traversal_rule"), "properties");
        Object(traversal, "successor_after_full_page_required")["const"] = true;
        Object(traversal, "empty_successor_after_short_page_required")["const"] = true;
        Object(traversal, "duplicate_key_rejects_observation")["const"] = true;
        Object(traversal, "non_strict_order_rejects_observation")["const"] = true;
    }

    private static void UniqueNonempty(JsonObject properties, string name)
    {
        var array = Object(properties, name);
        array["minItems"] = 1;
        array["uniqueItems"] = true;
    }

    private static JsonObject Object(JsonObject parent, string name) =>
        parent[name] as JsonObject
        ?? throw new InvalidOperationException($"The generated schema omits {name}.");
}
