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
            [SourceCoreSchemaIds.MachineQueryPlan] = typeof(MachineQueryPlan),
            [SourceCoreSchemaIds.MachineQueryRenderReceipt] = typeof(MachineQueryRenderReceipt),
            [SourceCoreSchemaIds.MachineRequestEvidence] = typeof(MachineRequestEvidence),
        });

    private static readonly ReadOnlyDictionary<string, string> SchemaFiles =
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SourceCoreSchemaIds.Common] = "source-common.schema.json",
            [SourceCoreSchemaIds.SourceObjectRef] = "source-object-ref.schema.json",
            [SourceCoreSchemaIds.SourceProfileTopology] = "source-profile-topology.schema.json",
            [SourceCoreSchemaIds.MachineQueryPlan] = "machine-query-plan.schema.json",
            [SourceCoreSchemaIds.MachineQueryRenderReceipt] = "machine-query-render-receipt.schema.json",
            [SourceCoreSchemaIds.MachineRequestEvidence] = "machine-request-evidence.schema.json",
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
            SourceCoreSchemaIds.MachineQueryPlan,
            SourceCoreSchemaIds.MachineQueryRenderReceipt,
            SourceCoreSchemaIds.MachineRequestEvidence,
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
    private const string MachineMemberPattern = "^[a-z0-9][a-z0-9._-]{0,127}" + End;
    private const string MediaTypePattern =
        "^[a-z0-9!#$&^_.+-]+/[a-z0-9!#$&^_.+-]+" + End;
    private const string DnsLabelPattern = "[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?";
    private const string NonDefaultPortPattern =
        "(?:[1-9][0-9]{0,3}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|" +
        "655[0-2][0-9]|6553[0-5])";
    private const string MachineTargetOriginAndPathPattern =
        "^(?=[!-~]{1,4096}" + End + ")" +
        "(?!https://[^/?#]+:443/)https://" +
        "(?![^/?#]*@)(?![0-9.]+(?::" + NonDefaultPortPattern + ")?/)" +
        "(?=[a-z0-9.-]{1,253}(?::" + NonDefaultPortPattern + ")?/)" +
        DnsLabelPattern + "(?:\\." + DnsLabelPattern + ")*(?::" + NonDefaultPortPattern + ")?/" +
        "(?![^?#]*(?:\\\\|%(?:25)*(?:2[eEfF]|5[cC])))" +
        "(?!\\.{1,2}(?:/|$)|[^?#]*/\\.{1,2}(?:/|$))[^?#\\s]*" + End;

    public static void Apply(string schemaId, JsonObject root)
    {
        PinConst(root, "schema", schemaId);
        if (string.Equals(schemaId, SourceCoreSchemaIds.MachineQueryRenderReceipt, StringComparison.Ordinal) ||
            string.Equals(schemaId, SourceCoreSchemaIds.MachineRequestEvidence, StringComparison.Ordinal))
        {
            PinConst(root, "query_plan_schema", MachineQueryPlan.SchemaId);
        }

        ApplyValueObject(root);
        ApplyMachineQueryConditions(schemaId, root);
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

                    if (properties.ContainsKey("kind") && properties.ContainsKey("row_limit"))
                    {
                        ApplyResponseCardinalityConditions(value);
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
            case var hashName when hashName.EndsWith("_sha256", StringComparison.Ordinal):
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

            case "target_origin_and_path":
                property["format"] = "uri";
                property["pattern"] = MachineTargetOriginAndPathPattern;
                property["minLength"] = 1;
                property["maxLength"] = 4096;
                break;

            case "request_target_length":
            case "expected_request_target_length":
                property["minimum"] = 1;
                property["maximum"] = 4096;
                break;

            case "expected_request_body_length":
            case "request_body_length":
                property["minimum"] = 0;
                property["maximum"] = MachineQueryValidation.MaximumRequestBodyBytes;
                break;

            case "row_limit":
                property["minimum"] = 1;
                property["maximum"] = MachineQueryValidation.MaximumResponseRowLimit;
                break;

            case "expected_partition_row_count":
                property["minimum"] = 0;
                break;
        }
    }

    private static void ApplyResponseCardinalityConditions(JsonObject root)
    {
        root["allOf"] = new JsonArray(
            EnumCondition("opaque_body", "row_limit", present: false),
            EnumCondition("opaque_body", "expected_partition_row_count", present: false),
            EnumCondition(
                "opaque_body",
                "expected_partition_row_count_evidence_ref",
                present: false),
            EnumCondition(
                "bounded_row_set_page",
                "row_limit",
                present: true,
                minimum: 1,
                maximum: MachineQueryValidation.MaximumResponseRowLimit),
            EnumCondition(
                "bounded_row_set_page",
                "expected_partition_row_count",
                present: true,
                minimum: 0),
            EnumCondition(
                "bounded_row_set_page",
                "expected_partition_row_count_evidence_ref",
                present: true));
    }

    private static JsonObject EnumCondition(
        string discriminator,
        string propertyName,
        bool present,
        int? minimum = null,
        int? maximum = null) => new()
        {
            ["if"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["kind"] = new JsonObject { ["const"] = discriminator },
                },
                ["required"] = new JsonArray("kind"),
            },
            ["then"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    [propertyName] = NullabilityConstraint(
                    present,
                    minimum: minimum,
                    maximum: maximum),
                },
            },
        };

    private static void ApplyMachineQueryConditions(string schemaId, JsonObject root)
    {
        if (string.Equals(schemaId, SourceCoreSchemaIds.MachineQueryPlan, StringComparison.Ordinal) ||
            string.Equals(
                schemaId,
                SourceCoreSchemaIds.MachineQueryRenderReceipt,
                StringComparison.Ordinal))
        {
            var conditions = new JsonArray(
                MethodCondition("GET", bodyPresent: false, includeHeaders: true),
                MethodCondition("POST", bodyPresent: true, includeHeaders: true));
            conditions.Add(RegistryMemberPattern("content_type", MediaTypePattern));
            if (string.Equals(schemaId, SourceCoreSchemaIds.MachineQueryPlan, StringComparison.Ordinal))
            {
                conditions.Add(RegistryMemberPattern("query_family_ref", MachineMemberPattern));
                conditions.Add(RegistryMemberPattern("partition_binding", MachineMemberPattern));
            }

            root["allOf"] = conditions;
        }
        else if (string.Equals(
                     schemaId,
                     SourceCoreSchemaIds.MachineRequestEvidence,
                     StringComparison.Ordinal))
        {
            root["oneOf"] = new JsonArray(
                BodyPair(bodyPresent: false),
                BodyPair(bodyPresent: true));
        }
    }

    private static JsonObject RegistryMemberPattern(string propertyName, string pattern) => new()
    {
        ["properties"] = new JsonObject
        {
            [propertyName] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["member_key"] = new JsonObject { ["pattern"] = pattern },
                },
            },
        },
    };

    private static JsonObject MethodCondition(
        string method,
        bool bodyPresent,
        bool includeHeaders)
    {
        var properties = BodyPairProperties(bodyPresent);
        if (includeHeaders)
        {
            properties["content_type"] = NullabilityConstraint(bodyPresent);
            properties["charset"] = NullabilityConstraint(bodyPresent, allowNullWhenPresent: true);
        }

        return new JsonObject
        {
            ["if"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["method"] = new JsonObject { ["const"] = method },
                },
                ["required"] = new JsonArray("method"),
            },
            ["then"] = new JsonObject { ["properties"] = properties },
        };
    }

    private static JsonObject BodyPair(bool bodyPresent) => new()
    {
        ["properties"] = BodyPairProperties(bodyPresent),
    };

    private static JsonObject BodyPairProperties(bool bodyPresent) => new()
    {
        ["expected_request_body_length"] = NullabilityConstraint(
            bodyPresent,
            minimum: 1,
            maximum: MachineQueryValidation.MaximumRequestBodyBytes),
        ["expected_request_body_sha256"] = NullabilityConstraint(bodyPresent),
        ["request_body_length"] = NullabilityConstraint(
            bodyPresent,
            minimum: 1,
            maximum: MachineQueryValidation.MaximumRequestBodyBytes),
        ["request_body_sha256"] = NullabilityConstraint(bodyPresent),
    };

    private static JsonObject NullabilityConstraint(
        bool present,
        bool allowNullWhenPresent = false,
        int? minimum = null,
        int? maximum = null)
    {
        if (!present || allowNullWhenPresent)
        {
            return allowNullWhenPresent && present
                ? new JsonObject()
                : new JsonObject { ["type"] = "null" };
        }

        var result = new JsonObject
        {
            ["not"] = new JsonObject { ["type"] = "null" },
        };
        if (minimum is not null)
        {
            result["minimum"] = minimum.Value;
        }

        if (maximum is not null)
        {
            result["maximum"] = maximum.Value;
        }

        return result;
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
