using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Scope;

public static class ScopeSchemaExporter
{
    public const string FileName = "source-scope-manifest.schema.json";

    public static byte[] ExportUtf8()
    {
        var root = ContractJson.CreateSchemaOptions().GetJsonSchemaAsNode(
                typeof(ScopeManifest),
                new JsonSchemaExporterOptions
                {
                    TreatNullObliviousAsNonNullable = true,
                }) as JsonObject
            ?? throw new InvalidOperationException("The scope schema root must be an object.");
        root["$id"] = ScopeManifestSchemaResourceIds.Manifest;
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        ScopeSchemaHardener.Apply(root);

        var json = root.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(json.TrimEnd('\r', '\n') + "\n");
    }
}

internal static class ScopeSchemaHardener
{
    private const string End = "(?![\\s\\S])";

    public static void Apply(JsonObject root)
    {
        SourceCoreSchemaHardener.ApplyValueObject(root);
        var properties = RequiredObject(root, "properties");
        RequiredObject(properties, "schema")["const"] = ScopeManifestSchemaIds.Manifest;
        Harden(root);
        HardenProfile(properties);
        HardenObservedObjects(properties);
        HardenRows(properties);
        HardenAccounting(properties);
        RequireUniqueArray(properties, "ordered_evidence_artifacts");
        RequireUniqueArray(properties, "body_candidate_ordinals");
    }

    private static void HardenProfile(JsonObject rootProperties)
    {
        var profile = RequiredObject(RequiredObject(rootProperties, "profile"), "properties");
        RequireNonemptyUniqueArray(profile, "ordered_members");
        RequireNonemptyUniqueArray(profile, "ordered_selector_member_ordinals");
        RequireNonemptyUniqueArray(profile, "ordered_rules");
    }

    private static void HardenObservedObjects(JsonObject rootProperties)
    {
        var observed = RequiredObject(rootProperties, "observed_objects");
        observed["uniqueItems"] = true;
        var entry = RequiredObject(observed, "items");
        var entryProperties = RequiredObject(entry, "properties");
        var objectRef = RequiredObject(entryProperties, "object_ref");
        var objectProperties = RequiredObject(objectRef, "properties");
        RequiredObject(objectProperties, "schema")["const"] = SourceCoreSchemaIds.SourceObjectRef;
    }

    private static void HardenRows(JsonObject rootProperties)
    {
        var rows = RequiredObject(rootProperties, "rows");
        rows["uniqueItems"] = true;
        var row = RequiredObject(rows, "items");
        var rowProperties = RequiredObject(row, "properties");

        var selectors = RequiredObject(rowProperties, "selectors");
        var selector = RequiredObject(selectors, "items");
        var selectorProperties = RequiredObject(selector, "properties");
        RequiredObject(selectorProperties, "canonical_values")["uniqueItems"] = true;
        selector["oneOf"] = new JsonArray
        {
            SelectorVariant(
                "publisher_value_present",
                "observed_value_set",
                minimumValues: 1,
                maximumValues: null,
                evidenceRequired: true,
                ruleRequired: false,
                causeRequired: false),
            SelectorVariant(
                "publisher_value_absent",
                "complete_observation_absence",
                minimumValues: null,
                maximumValues: 0,
                evidenceRequired: true,
                ruleRequired: false,
                causeRequired: false),
            SelectorVariant(
                "publisher_value_conflict",
                "observed_conflicting_value_set",
                minimumValues: 2,
                maximumValues: null,
                evidenceRequired: true,
                ruleRequired: false,
                causeRequired: true),
            SelectorVariant(
                "selector_not_applicable",
                evidenceKind: null,
                minimumValues: null,
                maximumValues: 0,
                evidenceRequired: false,
                ruleRequired: true,
                causeRequired: false),
        };

        var matched = RequiredObject(rowProperties, "matched_evaluations");
        matched["uniqueItems"] = true;
        var matchedItem = RequiredObject(matched, "items");
        var matchedProperties = RequiredObject(matchedItem, "properties");
        RequiredObject(matchedProperties, "role_member_ordinals")["uniqueItems"] = true;
        RequiredObject(matchedProperties, "capability_member_ordinals")["uniqueItems"] = true;
        matchedItem["allOf"] = OutcomeConstraints();

        var winners = RequiredObject(rowProperties, "axis_winning_rule_ordinals");
        winners["minItems"] = ScopeValidation.AllAxes.Length;
        winners["maxItems"] = ScopeValidation.AllAxes.Length;
        winners["prefixItems"] = new JsonArray(
            ScopeValidation.AllAxes
                .Select(axis => (JsonNode)new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 0,
                    ["$comment"] = $"Fixed {ScopeManifestCanonicalWriter.AxisName(axis)} axis position.",
                })
                .ToArray());
    }

    private static void HardenAccounting(JsonObject rootProperties)
    {
        var accounting = RequiredObject(rootProperties, "accounting");
        var count = ScopeValidation.AllAxes.Length * ScopeValidation.AllDispositions.Length;
        accounting["minItems"] = count;
        accounting["maxItems"] = count;
        accounting["uniqueItems"] = true;
        var item = RequiredObject(accounting, "items");
        var itemProperties = RequiredObject(item, "properties");
        RequiredObject(itemProperties, "object_ordinals")["uniqueItems"] = true;

        var prefix = new JsonArray();
        foreach (var axis in ScopeValidation.AllAxes)
        {
            foreach (var disposition in ScopeValidation.AllDispositions)
            {
                var positionedItem = item.DeepClone().AsObject();
                var positionedProperties = RequiredObject(positionedItem, "properties");
                positionedProperties["axis"] = new JsonObject
                {
                    ["const"] = ScopeManifestCanonicalWriter.AxisName(axis),
                };
                positionedProperties["disposition"] = new JsonObject
                {
                    ["const"] = ScopeManifestCanonicalWriter.DispositionName(disposition),
                };
                prefix.Add(positionedItem);
            }
        }

        accounting["prefixItems"] = prefix;
    }

    private static JsonArray OutcomeConstraints() => new()
    {
        new JsonObject
        {
            ["if"] = PropertyConst("effect", "exact_denial"),
            ["then"] = PropertyNotConst("disposition", "accepted_selected"),
        },
        new JsonObject
        {
            ["if"] = PropertyNotConst("disposition", "accepted_selected"),
            ["then"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["role_member_ordinals"] = new JsonObject { ["maxItems"] = 0 },
                    ["capability_member_ordinals"] = new JsonObject { ["maxItems"] = 0 },
                },
            },
        },
    };

    private static JsonObject SelectorVariant(
        string state,
        string? evidenceKind,
        int? minimumValues,
        int? maximumValues,
        bool evidenceRequired,
        bool ruleRequired,
        bool causeRequired)
    {
        var values = new JsonObject();
        if (minimumValues is not null)
        {
            values["minItems"] = minimumValues.Value;
        }

        if (maximumValues is not null)
        {
            values["maxItems"] = maximumValues.Value;
        }

        return new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["state"] = new JsonObject { ["const"] = state },
                ["canonical_values"] = values,
                ["evidence_kind"] = evidenceKind is null
                    ? new JsonObject { ["type"] = "null" }
                    : new JsonObject { ["const"] = evidenceKind },
                ["evidence_artifact_ordinal"] = NullabilityConstraint(evidenceRequired),
                ["rule_ordinal"] = NullabilityConstraint(ruleRequired),
                ["cause_member_ordinal"] = NullabilityConstraint(causeRequired),
            },
        };
    }

    private static JsonObject NullabilityConstraint(bool required) => required
        ? new JsonObject { ["not"] = new JsonObject { ["type"] = "null" } }
        : new JsonObject { ["type"] = "null" };

    private static JsonObject PropertyConst(string name, string value) => new()
    {
        ["properties"] = new JsonObject
        {
            [name] = new JsonObject { ["const"] = value },
        },
        ["required"] = new JsonArray(name),
    };

    private static JsonObject PropertyNotConst(string name, string value) => new()
    {
        ["properties"] = new JsonObject
        {
            [name] = new JsonObject
            {
                ["not"] = new JsonObject { ["const"] = value },
            },
        },
        ["required"] = new JsonArray(name),
    };

    private static void RequireNonemptyUniqueArray(JsonObject properties, string name)
    {
        var array = RequiredObject(properties, name);
        array["minItems"] = 1;
        array["uniqueItems"] = true;
    }

    private static void RequireUniqueArray(JsonObject properties, string name) =>
        RequiredObject(properties, name)["uniqueItems"] = true;

    private static JsonObject RequiredObject(JsonObject parent, string name) =>
        parent[name] as JsonObject
        ?? throw new InvalidOperationException($"Generated scope schema member {name} is missing.");

    private static void Harden(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    Harden(item);
                }

                break;
            case JsonObject value:
                if (value["properties"] is JsonObject properties)
                {
                    foreach (var (name, propertyNode) in properties)
                    {
                        if (propertyNode is not JsonObject property)
                        {
                            continue;
                        }

                        if (name.EndsWith("sha256", StringComparison.Ordinal))
                        {
                            property["pattern"] = "^[0-9a-f]{64}" + End;
                            property["minLength"] = 64;
                            property["maxLength"] = 64;
                        }
                        else if (name.EndsWith("ordinal", StringComparison.Ordinal))
                        {
                            property["minimum"] = 0;
                        }
                        else if (name.EndsWith("ordinals", StringComparison.Ordinal) &&
                                 property["items"] is JsonObject ordinalItems)
                        {
                            ordinalItems["minimum"] = 0;
                        }
                        else if (string.Equals(
                                     name,
                                     "rule_match_bits_base64_url",
                                     StringComparison.Ordinal))
                        {
                            property["pattern"] = "^[A-Za-z0-9_-]+" + End;
                            property["minLength"] = 1;
                        }
                        else if (string.Equals(name, "canonical_values", StringComparison.Ordinal) &&
                                 property["items"] is JsonObject items)
                        {
                            items["minLength"] = 1;
                            items["maxLength"] = 4096;
                            items["$comment"] =
                                "Length is limited to 4096 Unicode scalar values. The verified " +
                                "reader rejects invalid Unicode scalar sequences.";
                        }
                    }
                }

                foreach (var (_, child) in value)
                {
                    Harden(child);
                }

                break;
        }
    }
}
