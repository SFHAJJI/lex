using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// Exports the D1 facts schemas from the contracts themselves.
/// </summary>
/// <remarks>
/// The committed <c>schemas/v3-facts/*.schema.json</c> files are the output of this exporter, and
/// a test asserts they are byte-identical to it. That is what makes "schemas and contracts agree
/// exactly" a checked property rather than a promise: a contract change that is not mirrored in
/// the committed schema fails, and so does a hand-edited schema.
/// </remarks>
public static class FactsSchemaExporter
{
    private static readonly ReadOnlyDictionary<string, Type> SchemaTypes =
        new(new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [FactsSchemaIds.PublisherRelation] = typeof(PublisherRelation),
            [FactsSchemaIds.DerivedInverseRelation] = typeof(DerivedInverseRelation),
            [FactsSchemaIds.LocalInboundView] = typeof(LocalInboundView),
            [FactsSchemaIds.RelationFact] = typeof(RelationFact),
            [FactsSchemaIds.PublisherDate] = typeof(PublisherDate),
            [FactsSchemaIds.PublisherDateFact] = typeof(PublisherDateFact),
            [FactsSchemaIds.VocabularyDrift] = typeof(VocabularyDrift),
        });

    private static readonly ReadOnlyDictionary<string, string> SchemaFiles =
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FactsSchemaIds.FactsCommon] = "facts-common.schema.json",
            [FactsSchemaIds.PublisherRelation] = "publisher-relation.schema.json",
            [FactsSchemaIds.DerivedInverseRelation] = "derived-inverse-relation.schema.json",
            [FactsSchemaIds.LocalInboundView] = "local-inbound-view.schema.json",
            [FactsSchemaIds.RelationFact] = "relation-fact.schema.json",
            [FactsSchemaIds.PublisherDate] = "publisher-date.schema.json",
            [FactsSchemaIds.PublisherDateFact] = "publisher-date-fact.schema.json",
            [FactsSchemaIds.VocabularyDrift] = "vocabulary-drift.schema.json",
        });

    /// <summary>The shared value objects, published once as a definitions document.</summary>
    private static readonly ReadOnlyDictionary<string, Type> CommonDefinitionTypes =
        new(new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["transport_byte_reference"] = typeof(TransportByteReference),
            ["source_observation_reference"] = typeof(SourceObservationReference),
            ["official_identity"] = typeof(OfficialIdentity),
            ["axiom_qualifier"] = typeof(AxiomQualifier),
            ["qualified_axiom"] = typeof(QualifiedAxiom),
        });

    public static IReadOnlyList<string> AllSchemaIds { get; } = Array.AsReadOnly(
        new[]
        {
            FactsSchemaIds.FactsCommon,
            FactsSchemaIds.PublisherRelation,
            FactsSchemaIds.DerivedInverseRelation,
            FactsSchemaIds.LocalInboundView,
            FactsSchemaIds.RelationFact,
            FactsSchemaIds.PublisherDate,
            FactsSchemaIds.PublisherDateFact,
            FactsSchemaIds.VocabularyDrift,
        });

    public static string FileNameFor(string schemaId) =>
        SchemaFiles.TryGetValue(schemaId, out var fileName)
            ? fileName
            : throw new ArgumentException("Unknown facts schema identity.", nameof(schemaId));

    public static byte[] ExportUtf8(string schemaId)
    {
        var root = string.Equals(schemaId, FactsSchemaIds.FactsCommon, StringComparison.Ordinal)
            ? CreateCommonDefinitionsNode()
            : CreateSchemaNode(schemaId);

        var outputOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        };
        var json = root.ToJsonString(outputOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalized = json.TrimEnd('\r', '\n') + "\n";
        var byteCount = Encoding.UTF8.GetByteCount(normalized);
        var bytes = GC.AllocateUninitializedArray<byte>(byteCount);
        Encoding.UTF8.GetBytes(normalized, bytes);
        return bytes;
    }

    public static string ComputeSha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static JsonObject CreateSchemaNode(string schemaId)
    {
        if (!SchemaTypes.TryGetValue(schemaId, out var type))
        {
            throw new ArgumentException("Unknown facts schema identity.", nameof(schemaId));
        }

        var root = ExportTypeNode(type);
        root["$id"] = FactsSchemaResourceIds.ForWireSchema(schemaId);
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        return root;
    }

    private static JsonObject CreateCommonDefinitionsNode()
    {
        var defs = new JsonObject();
        foreach (var (name, type) in CommonDefinitionTypes)
        {
            defs[name] = ExportTypeNode(type);
        }

        return new JsonObject
        {
            ["$id"] = FactsSchemaResourceIds.ForWireSchema(FactsSchemaIds.FactsCommon),
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$defs"] = defs,
        };
    }

    private static JsonObject ExportTypeNode(Type type)
    {
        var serializerOptions = ContractJson.CreateSchemaOptions();
        return serializerOptions.GetJsonSchemaAsNode(
            type,
            new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true,
            }) as JsonObject
            ?? throw new InvalidOperationException("The exported schema root must be an object.");
    }
}
