using System.Collections.ObjectModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Lex.V3.Contracts.Source.Core;

public static class SourceCoreSchemaExporter
{
    private static readonly ReadOnlyDictionary<string, Type> SchemaTypes =
        new(new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [SourceCoreSchemaIds.SourceObjectRef] = typeof(SourceObjectRef),
            [SourceCoreSchemaIds.SourceProfileTopology] = typeof(SourceProfileTopology),
        });

    private static readonly ReadOnlyDictionary<string, string> SchemaFiles =
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SourceCoreSchemaIds.Common] = "source-common.schema.json",
            [SourceCoreSchemaIds.SourceObjectRef] = "source-object-ref.schema.json",
            [SourceCoreSchemaIds.SourceProfileTopology] = "source-profile-topology.schema.json",
        });

    private static readonly ReadOnlyDictionary<string, Type> CommonDefinitionTypes =
        new(new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["source_artifact_ref"] = typeof(SourceArtifactRef),
            ["source_registry_member_ref"] = typeof(SourceRegistryMemberRef),
            ["source_object_key_ref"] = typeof(SourceObjectKeyRef),
        });

    public static IReadOnlyList<string> AllSchemaIds { get; } = Array.AsReadOnly(
        new[]
        {
            SourceCoreSchemaIds.Common,
            SourceCoreSchemaIds.SourceObjectRef,
            SourceCoreSchemaIds.SourceProfileTopology,
        });

    public static string FileNameFor(string schemaId) =>
        SchemaFiles.TryGetValue(schemaId, out var fileName)
            ? fileName
            : throw new ArgumentException("Unknown source-core schema identity.", nameof(schemaId));

    public static byte[] ExportUtf8(string schemaId)
    {
        var root = string.Equals(schemaId, SourceCoreSchemaIds.Common, StringComparison.Ordinal)
            ? CreateCommonDefinitionsNode()
            : CreateSchemaNode(schemaId);

        var json = root.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(json.TrimEnd('\r', '\n') + "\n");
    }

    private static JsonObject CreateSchemaNode(string schemaId)
    {
        if (!SchemaTypes.TryGetValue(schemaId, out var type))
        {
            throw new ArgumentException("Unknown source-core schema identity.", nameof(schemaId));
        }

        var root = ExportTypeNode(type);
        root["$id"] = SourceCoreSchemaResourceIds.ForWireSchema(schemaId);
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        SourceCoreSchemaHardener.Apply(schemaId, root);
        return root;
    }

    private static JsonObject CreateCommonDefinitionsNode()
    {
        var definitions = new JsonObject();
        foreach (var (name, type) in CommonDefinitionTypes)
        {
            var definition = ExportTypeNode(type);
            SourceCoreSchemaHardener.ApplyValueObject(definition);
            definitions[name] = definition;
        }

        return new JsonObject
        {
            ["$id"] = SourceCoreSchemaResourceIds.Common,
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$defs"] = definitions,
        };
    }

    private static JsonObject ExportTypeNode(Type type) =>
        ContractJson.CreateSchemaOptions().GetJsonSchemaAsNode(
            type,
            new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true,
            }) as JsonObject
        ?? throw new InvalidOperationException("The exported schema root must be an object.");
}

internal static class SourceCoreSchemaHardener
{
    private const string End = "(?![\\s\\S])";
    private const string Sha256Pattern = "^[0-9a-f]{64}" + End;
    private const string UuidUrnPattern =
        "^urn:uuid:(?!00000000-0000-0000-0000-000000000000)" +
        "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}" + End;
    private const string PublisherUriPattern =
        "^(?!.*%(?![0-9A-Fa-f]{2}))https?://(?![^/?#]*@)[^/?#\\s]+(?:/[^?#\\s]*)?" + End;

    public static void Apply(string schemaId, JsonObject root)
    {
        PinConst(root, "schema", schemaId);
        ApplyValueObject(root);
    }

    public static void ApplyValueObject(JsonObject root) => HardenTree(root);

    private static void HardenTree(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    HardenTree(item);
                }

                break;

            case JsonObject value:
                if (value["properties"] is JsonObject properties)
                {
                    foreach (var (name, propertyNode) in properties)
                    {
                        if (propertyNode is JsonObject property)
                        {
                            HardenProperty(name, property);
                        }
                    }
                }

                foreach (var (_, child) in value)
                {
                    HardenTree(child);
                }

                break;
        }
    }

    private static void HardenProperty(string name, JsonObject property)
    {
        switch (name)
        {
            case "resource_id":
                property["pattern"] = UuidUrnPattern;
                property["minLength"] = 45;
                property["maxLength"] = 45;
                break;

            case "sha256":
            case "canonical_key_sha256":
                property["pattern"] = Sha256Pattern;
                property["minLength"] = 64;
                property["maxLength"] = 64;
                break;

            case "member_key":
                property["pattern"] = "^[!-~]{1,256}" + End;
                property["minLength"] = 1;
                property["maxLength"] = 256;
                break;

            case "publisher_uri":
                property["format"] = "uri";
                property["pattern"] = PublisherUriPattern;
                property["minLength"] = 1;
                property["maxLength"] = 4096;
                break;

            case "canonical_key":
                property["minLength"] = 1;
                property["maxLength"] = 4096;
                break;
        }
    }

    private static void PinConst(JsonObject root, string propertyName, string value)
    {
        if (root["properties"] is JsonObject properties &&
            properties[propertyName] is JsonObject property)
        {
            property["const"] = value;
        }
    }
}
