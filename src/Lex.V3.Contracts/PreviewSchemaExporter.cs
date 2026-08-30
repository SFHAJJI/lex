using System.Collections.ObjectModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Lex.V3.Contracts;

public static class PreviewSchemaExporter
{
    private static readonly ReadOnlyDictionary<string, Type> SchemaTypes =
        new(new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [V3SchemaIds.PreviewArtifact] = typeof(PreviewArtifactManifest),
            [V3SchemaIds.PreviewPayload] = typeof(PreviewPayload),
            [V3SchemaIds.PreviewEnvelope] = typeof(PreviewEnvelope),
            [V3SchemaIds.PreviewObjectSet] = typeof(PreviewObjectSet),
            [V3SchemaIds.PreviewOperationCatalog] = typeof(PreviewOperationCatalog),
            [V3SchemaIds.PreviewRefusalRegistry] = typeof(PreviewRefusalRegistry),
        });

    private static readonly ReadOnlyDictionary<string, string> SchemaFiles =
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [V3SchemaIds.PreviewArtifact] = "preview-artifact.schema.json",
            [V3SchemaIds.PreviewPayload] = "preview-payload.schema.json",
            [V3SchemaIds.PreviewEnvelope] = "preview-envelope.schema.json",
            [V3SchemaIds.PreviewObjectSet] = "preview-object-set.schema.json",
            [V3SchemaIds.PreviewOperationCatalog] = "preview-operation-catalog.schema.json",
            [V3SchemaIds.PreviewRefusalRegistry] = "preview-refusal-registry.schema.json",
        });

    public static byte[] ExportUtf8(string schemaId)
    {
        if (!SchemaTypes.TryGetValue(schemaId, out var type))
        {
            throw new ArgumentException("Unknown preview schema identity.", nameof(schemaId));
        }

        var serializerOptions = ContractJson.CreateSchemaOptions();
        var root = serializerOptions.GetJsonSchemaAsNode(
            type,
            new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true,
            }) as JsonObject
            ?? throw new InvalidOperationException("The exported schema root must be an object.");

        root["$id"] = schemaId;
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";

        var outputOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        };
        var json = root.ToJsonString(outputOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(json.TrimEnd('\r', '\n') + "\n");
    }

    public static string FileNameFor(string schemaId) =>
        SchemaFiles.TryGetValue(schemaId, out var fileName)
            ? fileName
            : throw new ArgumentException("Unknown preview schema identity.", nameof(schemaId));
}
