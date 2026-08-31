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

    private const string CellarHost = @"^https?://publications\.europa\.eu/resource/";

    /// <summary>A work is /resource/&lt;class&gt;/&lt;id&gt; and nothing deeper.</summary>
    internal const string CellarWorkPattern = CellarHost + "[^/]+/[^/]+$";

    /// <summary>A resource is anything strictly beneath a work.</summary>
    internal const string CellarResourcePattern = CellarHost + "[^/]+/[^/]+/.+$";

    internal const string EcliPattern = "^ECLI:[A-Z]{2}:[^:]+:[0-9]{4}:[0-9A-Z]+$";

    /// <summary>The five admitted CELEX profiles, anchored at both ends.</summary>
    internal const string CelexPattern =
        @"^[0-9]{5}[A-Z]{1,3}([0-9]+(R\([0-9]+\))?(-[0-9]{8})?|/[A-Z0-9]+(/[A-Z0-9]+)*|[0-9]+[A-Z]{3}_[0-9A-Z]+)$";

    /// <summary>
    /// The only invariants left to the reader alone: equality between two distant instance
    /// locations, which draft 2020-12 cannot express without extensions.
    /// </summary>
    /// <remarks>
    /// Candidate 2 listed ten. Codex was right that half of them are expressible with
    /// <c>if</c>/<c>then</c>, <c>oneOf</c> and <c>const</c>, and that a parity test asserting the
    /// current schema accepts a violation proves only today's permissiveness, never that the
    /// schema language could not reject it. Those five are now encoded below and removed from
    /// this list.
    /// </remarks>
    internal static IReadOnlyList<string> ReaderOnlyInvariants { get; } = Array.AsReadOnly(
        new[]
        {
            "derived inverse source equals the forward assertion target",
            "derived inverse target equals the forward assertion source",
            "derived inverse inverted predicate equals the forward assertion predicate",
            "authorizing axiom maps this forward predicate to this inverse predicate",
            "inbound view contributors all target the view target",
            "ecli state agrees with the target identity set",
            "lexical value is a real calendar date at its declared precision",
            "ecli_missing rests on a complete identifier enumeration",
        });

    /// <summary>
    /// Every contract recognised by the exact set of property names its shape carries.
    /// </summary>
    /// <remarks>
    /// The generator inlines nested contracts, so a relation fact carries a whole publisher
    /// relation inside it with no marker saying which contract it is. Keying the hardener on
    /// property spelling alone left every nested contract version unpinned, so a document with a
    /// wrong nested identity validated while the reader refused it. Matching the structural
    /// signature pins the nested one exactly as the root.
    /// </remarks>
    private static readonly (string SchemaId, string[] Properties)[] ContractSignatures =
    {
        (FactsSchemaIds.PublisherRelation,
            new[] { "schema", "source", "target", "predicate_uri", "observation", "qualified_axioms" }),
        (FactsSchemaIds.DerivedInverseRelation,
            new[] { "schema", "source", "target", "predicate_uri", "inverse_of_predicate_uri",
                    "authorizing_axiom", "derived_from" }),
        (FactsSchemaIds.LocalInboundView,
            new[] { "schema", "target", "predicate_uri", "scope_is_complete",
                    "scope_descriptor_sha256", "contributing_assertions" }),
        (FactsSchemaIds.PublisherDate,
            new[] { "schema", "raw_lexical_value", "datatype_uri", "precision", "open_sentinel" }),
    };

    internal static void Apply(string schemaId, JsonObject root)
    {
        PinConst(root, "schema", schemaId);
        HardenTree(root);
        EncodeInvariants(schemaId, root);
    }

    internal static void HardenValueObject(string name, JsonObject node)
    {
        HardenTree(node);
        if (string.Equals(name, "official_identifier", StringComparison.Ordinal))
        {
            EncodeIdentifierFamilyShapes(node);
        }
    }

    /// <summary>Each family constrains its own raw value, so the tag cannot stand alone.</summary>
    private static void EncodeIdentifierFamilyShapes(JsonObject node)
    {
        var arms = new JsonArray
        {
            // Both Cellar arms used the same unanchored prefix, so the schema could not tell the
            // two WEMI levels apart and a resource URI validated as a work. They are now anchored
            // at both ends and differ by depth, exactly as the reader does.
            IfThen(
                Props(("family", Const("cellar_work_uri"))),
                Props(("raw_value", new JsonObject { ["pattern"] = CellarWorkPattern }))),
            IfThen(
                Props(("family", Const("cellar_resource_uri"))),
                Props(("raw_value", new JsonObject { ["pattern"] = CellarResourcePattern }))),
            IfThen(
                Props(("family", Const("ecli"))),
                Props(("raw_value", new JsonObject { ["pattern"] = EcliPattern }))),
            // An unanchored CELEX prefix accepted any trailing suffix at all. This anchors the
            // whole value across the five admitted profiles.
            IfThen(
                Props(("family", Const("celex"))),
                Props(("raw_value", new JsonObject { ["pattern"] = CelexPattern }))),
        };
        node["allOf"] = arms;
    }

    /// <summary>
    /// Walk every node: apply the grammar a property name implies, and pin any nested contract
    /// recognised by its structural signature.
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

                    PinNestedContract(o, properties);
                }

                foreach (var (_, value) in o)
                {
                    HardenTree(value);
                }

                return;
        }
    }

    private static void PinNestedContract(JsonObject node, JsonObject properties)
    {
        var names = properties.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);

        // The identifier value object is inlined into every relation schema exactly as the
        // contracts are, so its per-family value shapes have to be applied by signature too. The
        // first version applied them only to the definitions document, and a non-URI tagged
        // `cellar_work_uri` validated inside a publisher relation. Same inlining, same lesson.
        if (names.Count == 2 && names.Contains("family") && names.Contains("raw_value"))
        {
            EncodeIdentifierFamilyShapes(node);
            return;
        }

        foreach (var (schemaId, signature) in ContractSignatures)
        {
            if (names.Count == signature.Length && signature.All(names.Contains))
            {
                PinConst(node, "schema", schemaId);
                EncodeInvariants(schemaId, node);
                return;
            }
        }
    }

    private static void HardenProperty(string name, JsonObject property)
    {
        if (name.EndsWith("_sha256", StringComparison.Ordinal))
        {
            property["pattern"] = Sha256Pattern;
            return;
        }

        // A Cellar URI family value must be a URI, which is what a probe smuggled past by
        // tagging a non-URI string as `cellar_work_uri`.
        if (string.Equals(name, "datatype_uri", StringComparison.Ordinal))
        {
            var array = new JsonArray();
            foreach (var value in PublisherDate.PrecisionByDatatype.Keys)
            {
                array.Add(value);
            }

            property["enum"] = array;
            return;
        }

        if (name.EndsWith("_uri", StringComparison.Ordinal) ||
            string.Equals(name, "parsed_by_authority", StringComparison.Ordinal))
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

            // `uniqueItems` compares whole objects, so it never stopped the same family appearing
            // twice with different raw values, which is the contradiction the reader refuses. One
            // `contains` arm per family, capped at one, expresses it exactly.
            var caps = new JsonArray();
            foreach (var family in System.Enum.GetValues<FactsIdentifierFamily>())
            {
                var wire = ClosedVocabulary.WireNames<FactsIdentifierFamily>()[(int)family];
                caps.Add(new JsonObject
                {
                    ["contains"] = new JsonObject
                    {
                        ["properties"] = new JsonObject { ["family"] = Const(wire) },
                        ["required"] = new JsonArray("family"),
                    },
                    // `contains` requires at least one match by default, so without this every
                    // identity set failed the arm for every family it does not carry. The cap is
                    // the assertion; the floor must be zero.
                    ["minContains"] = 0,
                    ["maxContains"] = 1,
                });
            }

            property["allOf"] = caps;
            return;
        }

        // A Cellar family value must be a Cellar URI. `raw_value` never ends in `_uri`, so the
        // URI branch above never saw it and a non-URI tagged `cellar_work_uri` validated.
        if (string.Equals(name, "family", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(name, "observed_at", StringComparison.Ordinal))
        {
            // UTC only. The reader enforces a zero offset; the schema enforces the two lexical
            // forms a zero offset actually takes. `System.Text.Json` writes `+00:00` rather than
            // `Z` for `DateTimeOffset.Zero`, so a bare `Z$` pattern rejected every document this
            // contract produces, which a valid-document parity case caught immediately.
            property["format"] = "date-time";
            property["pattern"] = @"(Z|\+00:00)$";
        }
    }

    /// <summary>The conditional invariants, encoded rather than deferred to the reader.</summary>
    private static void EncodeInvariants(string schemaId, JsonObject root)
    {
        var all = new JsonArray();

        switch (schemaId)
        {
            case FactsSchemaIds.PublisherDate:
                // datatype pins precision, in both directions, one arm per datatype.
                foreach (var (datatype, precision) in PublisherDate.PrecisionByDatatype)
                {
                    var wire = ClosedVocabulary.WireNames<DatePrecision>()[(int)precision];
                    all.Add(IfThen(
                        Props(("datatype_uri", Const(datatype))),
                        Props(("precision", Const(wire)))));
                    all.Add(IfThen(
                        Props(("precision", Const(wire))),
                        Props(("datatype_uri", Const(datatype)))));
                }

                // the open-end sentinel binds to its exact lexical value, in both directions.
                // The sentinel is a date VALUE in any lexical form its datatype admits, so the
                // schema matches the same set the reader does. A const here made
                // `9999-12-31Z` the sentinel to the reader and an ordinary date to the schema.
                var sentinelShape = new JsonObject
                {
                    ["properties"] = new JsonObject
                    {
                        ["raw_lexical_value"] = new JsonObject
                        {
                            ["pattern"] = PublisherDate.OpenEndedLexicalPattern,
                        },
                        ["datatype_uri"] = Const(PublisherDate.Date),
                    },
                    ["required"] = new JsonArray("raw_lexical_value", "datatype_uri"),
                };
                all.Add(IfThen(Props(("open_sentinel", Const("open_ended"))), sentinelShape));
                all.Add(IfThen(sentinelShape, Props(("open_sentinel", Const("open_ended")))));
                break;

            case FactsSchemaIds.PublisherDateFact:
                // an open end may only carry the two roles that can mean it.
                all.Add(IfThen(
                    Props(("date", Props(("open_sentinel", Const("open_ended"))))),
                    Props(("semantic_role", Enum("end_of_validity", "role_not_stated_by_publisher")))));
                // the deadline biconditional, both ways and exactly.
                all.Add(IfThen(
                    Props(("semantic_role", Const("transposition_deadline"))),
                    Props(("transposition_evidence",
                        Enum("directive_qualifier", "nim_record")))));
                all.Add(IfThen(
                    Props(("transposition_evidence", Enum("directive_qualifier", "nim_record"))),
                    Props(("semantic_role", Const("transposition_deadline")))));
                all.Add(IfThen(
                    Props(("semantic_role", Const("publisher_deadline"))),
                    Props(("transposition_evidence", Const("none")))));
                // the parsing authority scheme, which the reader requires and the schema did not.
                Pattern(root, "parsed_by_authority", "^https://");
                break;

            case FactsSchemaIds.RelationFact:
                // exactly one payload, and the declared kind names which one.
                root["oneOf"] = new JsonArray(
                    RequireOnly("publisher_asserted", "ontology_authorized_inverse", "local_inbound_view"),
                    RequireOnly("ontology_authorized_inverse", "publisher_asserted", "local_inbound_view"),
                    RequireOnly("local_inbound_view", "publisher_asserted", "ontology_authorized_inverse"));
                foreach (var (kind, payload) in new[]
                         {
                             ("publisher_asserted", "publisher_asserted"),
                             ("ontology_authorized_inverse", "ontology_authorized_inverse"),
                             ("local_inbound_view", "local_inbound_view"),
                         })
                {
                    all.Add(IfThen(
                        Props(("kind", Const(kind))),
                        new JsonObject
                        {
                            ["required"] = new JsonArray(payload),
                            ["properties"] = new JsonObject
                            {
                                [payload] = new JsonObject
                                {
                                    ["not"] = new JsonObject { ["type"] = "null" },
                                },
                            },
                        }));
                }

                break;

            case FactsSchemaIds.VocabularyDrift:
                MinItems(root, "admitted_terms", 1);
                Unique(root, "admitted_terms");
                // each vocabulary pins its exact admitted array, in declaration order.
                foreach (var kind in System.Enum.GetValues<VocabularyKind>())
                {
                    var wire = ClosedVocabulary.WireNames<VocabularyKind>()[(int)kind];
                    var terms = new JsonArray();
                    foreach (var term in ClosedVocabulary.AdmittedTermsFor(kind))
                    {
                        terms.Add(term);
                    }

                    all.Add(IfThen(
                        Props(("vocabulary", Const(wire))),
                        Props(("admitted_terms", new JsonObject { ["const"] = terms }))));
                }

                break;
        }

        if (all.Count > 0)
        {
            root["allOf"] = all;
        }
    }

    /// <summary>
    /// Build one conditional arm, cloning both halves.
    /// </summary>
    /// <remarks>
    /// A `JsonNode` may have only one parent, so reusing a shape across two arms throws. The
    /// sentinel shape is deliberately used twice, once per direction, which is exactly the case
    /// that needs the clone.
    /// </remarks>
    private static JsonObject IfThen(JsonObject condition, JsonObject consequence) =>
        new()
        {
            ["if"] = condition.DeepClone(),
            ["then"] = consequence.DeepClone(),
        };

    private static JsonObject Props(params (string Name, JsonNode Shape)[] members)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, shape) in members)
        {
            properties[name] = shape;
            required.Add(name);
        }

        return new JsonObject { ["properties"] = properties, ["required"] = required };
    }

    private static JsonObject Const(string value) => new() { ["const"] = value };

    private static JsonObject Enum(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return new JsonObject { ["enum"] = array };
    }

    private static JsonObject RequireOnly(string present, params string[] absent)
    {
        var forbidden = new JsonObject();
        foreach (var name in absent)
        {
            forbidden[name] = new JsonObject { ["type"] = "null" };
        }

        forbidden[present] = new JsonObject { ["not"] = new JsonObject { ["type"] = "null" } };
        return new JsonObject
        {
            ["required"] = new JsonArray(present),
            ["properties"] = forbidden,
        };
    }

    private static JsonObject? Property(JsonObject root, string name) =>
        root["properties"] is JsonObject properties && properties[name] is JsonObject property
            ? property
            : null;

    private static void Pattern(JsonObject root, string name, string pattern)
    {
        if (Property(root, name) is { } property)
        {
            property["pattern"] = pattern;
        }
    }

    private static void PinConst(JsonObject root, string name, string value)
    {
        if (Property(root, name) is { } property)
        {
            property["const"] = value;
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
}
