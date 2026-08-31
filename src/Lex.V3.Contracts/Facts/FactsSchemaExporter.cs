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
/// a test asserts they are byte-identical to it, so a contract change not mirrored in the
/// committed schema fails and so does a hand-edited schema.
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

    private static readonly ReadOnlyDictionary<string, Type> CommonDefinitionTypes =
        new(new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["transport_byte_reference"] = typeof(TransportByteReference),
            ["source_observation_reference"] = typeof(SourceObservationReference),
            ["official_identifier"] = typeof(OfficialIdentifier),
            ["official_identity_set"] = typeof(OfficialIdentitySet),
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
        FactsSchemaHardener.Apply(schemaId, root);
        return root;
    }

    private static JsonObject CreateCommonDefinitionsNode()
    {
        var defs = new JsonObject();
        foreach (var (name, type) in CommonDefinitionTypes)
        {
            var node = ExportTypeNode(type);
            FactsSchemaHardener.HardenValueObject(name, node);
            defs[name] = node;
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

/// <summary>
/// Adds to the exported schemas the constructor invariants JSON Schema can express, and records
/// the ones it cannot.
/// </summary>
/// <remarks>
/// <para>
/// The generator emits shapes and types. It knows nothing about the version constant, the digest
/// grammar, the URI requirement, or the exactly-one-payload rule, so an unhardened schema
/// accepted documents the C# reader refuses. Codex proved it: a document carrying the wrong
/// contract identity validated against the generated schema.
/// </para>
/// <para>
/// Three invariants remain inexpressible in draft 2020-12 without extensions, all of them
/// cross-field equalities: a derived inverse's endpoints against its forward assertion, an
/// inbound view's contributors against its target, and an ECLI state against the target identity
/// set. They are enforced by the reader alone. That divergence is deliberate and is asserted as
/// such in the parity tests, so it is a known gap rather than a surprise.
/// </para>
/// </remarks>
internal static class FactsSchemaHardener
{
    internal const string Sha256Pattern = "^[0-9a-f]{64}$";

    /// <summary>Invariants that the reader enforces and the schema provably cannot.</summary>
    internal static IReadOnlyList<string> ReaderOnlyInvariants { get; } = Array.AsReadOnly(
        new[]
        {
            "derived inverse source equals the forward assertion target",
            "derived inverse target equals the forward assertion source",
            "derived inverse inverted predicate equals the forward assertion predicate",
            "inbound view contributors all target the view target",
            "relation fact declared kind matches the carried payload",
            "ecli state agrees with the target identity set",
            "open sentinel is only the exact 9999-12-31 lexical value",
            "date lexical value is a real calendar date",
            "date precision matches the declared datatype",
            "drift admitted terms are exactly the named vocabulary",
        });

    internal static void Apply(string schemaId, JsonObject root)
    {
        // Every contract pins its own version. Without this the schema accepted any string,
        // including another contract's identity.
        PinConst(root, "schema", schemaId);

        // The generator inlines nested value objects rather than referencing them, so a
        // transport digest appears inside every relation schema as its own copy. Hardening only
        // the definitions document left every one of those copies unconstrained, which is how a
        // non-hex digest still validated inside a publisher relation. The pass is therefore by
        // property name over the whole tree.
        HardenTree(root);

        switch (schemaId)
        {
            case FactsSchemaIds.RelationFact:
                // Exactly one payload, which the generator renders as three nullable members.
                root["oneOf"] = new JsonArray(
                    RequireOnly("publisher_asserted", "ontology_authorized_inverse", "local_inbound_view"),
                    RequireOnly("ontology_authorized_inverse", "publisher_asserted", "local_inbound_view"),
                    RequireOnly("local_inbound_view", "publisher_asserted", "ontology_authorized_inverse"));
                break;

            case FactsSchemaIds.PublisherDate:
                EnumOf(root, "datatype_uri", PublisherDate.PrecisionByDatatype.Keys);
                break;

            case FactsSchemaIds.VocabularyDrift:
                MinItems(root, "admitted_terms", 1);
                Unique(root, "admitted_terms");
                break;
        }
    }

    internal static void HardenValueObject(string name, JsonObject node) => HardenTree(node);

    /// <summary>
    /// Apply the grammar a property name implies, everywhere that name occurs.
    /// </summary>
    private static void HardenTree(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    HardenTree(item);
                }

                return;

            case JsonObject o:
                if (o["properties"] is JsonObject properties)
                {
                    foreach (var (name, value) in properties)
                    {
                        if (value is JsonObject property)
                        {
                            HardenProperty(name, property);
                        }
                    }
                }

                foreach (var (_, value) in o)
                {
                    HardenTree(value);
                }

                return;
        }
    }

    private static void HardenProperty(string name, JsonObject property)
    {
        if (name.EndsWith("_sha256", StringComparison.Ordinal))
        {
            property["pattern"] = Sha256Pattern;
            return;
        }

        // `datatype_uri` carries a closed enum instead, which is stricter than a format.
        if (name.EndsWith("_uri", StringComparison.Ordinal) &&
            !string.Equals(name, "datatype_uri", StringComparison.Ordinal))
        {
            property["format"] = "uri";
            property["minLength"] = 1;
            return;
        }

        if (string.Equals(name, "parsed_by_authority", StringComparison.Ordinal))
        {
            property["format"] = "uri";
            property["minLength"] = 1;
            return;
        }

        if (string.Equals(name, "byte_length", StringComparison.Ordinal))
        {
            property["minimum"] = 0;
            return;
        }

        if (string.Equals(name, "identifiers", StringComparison.Ordinal))
        {
            property["minItems"] = 1;
        }
    }

    private static JsonObject RequireOnly(string present, params string[] absent)
    {
        var forbidden = new JsonObject();
        foreach (var name in absent)
        {
            forbidden[name] = new JsonObject { ["type"] = "null" };
        }

        return new JsonObject
        {
            ["required"] = new JsonArray(present),
            ["properties"] = MergeNotNull(present, forbidden),
        };
    }

    private static JsonObject MergeNotNull(string present, JsonObject forbidden)
    {
        forbidden[present] = new JsonObject { ["not"] = new JsonObject { ["type"] = "null" } };
        return forbidden;
    }

    private static JsonObject? Property(JsonObject root, string name) =>
        root["properties"] is JsonObject properties && properties[name] is JsonObject property
            ? property
            : null;

    private static void PinConst(JsonObject root, string name, string value)
    {
        if (Property(root, name) is { } property)
        {
            property["const"] = value;
        }
    }

    private static void Pattern(JsonObject root, string name, string pattern)
    {
        if (Property(root, name) is { } property)
        {
            property["pattern"] = pattern;
        }
    }

    private static void Uri(JsonObject root, string name)
    {
        if (Property(root, name) is { } property)
        {
            property["format"] = "uri";
            property["minLength"] = 1;
        }
    }

    private static void Minimum(JsonObject root, string name, int minimum)
    {
        if (Property(root, name) is { } property)
        {
            property["minimum"] = minimum;
        }
    }

    private static void MinItems(JsonObject root, string name, int minItems)
    {
        if (Property(root, name) is { } property)
        {
            property["minItems"] = minItems;
        }
    }

    private static void Unique(JsonObject root, string name)
    {
        if (Property(root, name) is { } property)
        {
            property["uniqueItems"] = true;
        }
    }

    private static void EnumOf(JsonObject root, string name, IEnumerable<string> values)
    {
        if (Property(root, name) is { } property)
        {
            var array = new JsonArray();
            foreach (var value in values)
            {
                array.Add(value);
            }

            property["enum"] = array;
        }
    }
}
