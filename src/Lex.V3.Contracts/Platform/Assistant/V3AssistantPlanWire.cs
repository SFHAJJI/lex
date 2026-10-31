using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// The plan as a model writes it under strict structured outputs (<c>lex-v3-assistant-plan-wire/1</c>), generated from the
/// plan grammar (docs/ANSWER-DOSSIER-1.md §5), and its one fixed mapping to the grammar.
/// </summary>
/// <remarks>
/// Strict structured outputs require every property of an object to be required, so an object whose members are optional
/// cannot be said. The wire form therefore writes a step's arguments as an array of <c>{name, source}</c>, and everything else
/// as the grammar does. <see cref="ToPlanJson"/> turns that array into the grammar's object and changes nothing else: it never
/// drops, adds, renames or repairs a value. A name written twice becomes a member written twice, which the validator refuses;
/// a shape the mapping does not recognise is handed to the validator unchanged, which refuses it.
/// </remarks>
public static class V3AssistantPlanWire
{
    public const string SchemaName = "lex_v3_assistant_plan";

    /// <summary>The argument names any operation of the grammar takes.</summary>
    private static readonly string[] ArgumentNames = ["query", "language", "date", "identifier", "article", "anchor", "date_from", "date_to"];

    private static readonly string[] FollowUpOperations = ["dossier", "timeline", "as_of", "evidence_bundle", "article_history"];

    /// <summary>The JSON schema the model's output is held to (strict: every property required, no other property).</summary>
    public static JsonObject Schema()
    {
        JsonObject Closed(params (string Name, JsonNode Schema)[] properties)
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
                ["required"] = new JsonArray(properties.Select(static property => (JsonNode)property.Name).ToArray()),
            };
        }

        JsonObject Enum(IEnumerable<string> values) => new()
        {
            ["type"] = "string",
            ["enum"] = new JsonArray(values.Select(static value => (JsonNode)value).ToArray()),
        };

        JsonObject Integers() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } };

        var steps = new[] { "s1", "s2", "s3", "s4" };
        var parts = new[] { "p1", "p2" };
        var verdicts = System.Enum.GetValues<V3AssistantVerdict>().Select(static verdict => ContractWire.NameOf(verdict));
        var argument = Closed(
            ("name", Enum(ArgumentNames)),
            ("source", new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    Closed(("span", Integers())),
                    Closed(("ref", new JsonObject { ["type"] = "string" })),
                    Closed(("result", Enum(steps)), ("pick", Enum(["sole_work", "sole_article"]))),
                    Closed(("choice", Enum(V3AnswerTemplates.LanguageCodes))),
                    Closed(("today", new JsonObject { ["type"] = "boolean" }))),
            }));
        var arguments = new JsonObject { ["type"] = "array", ["items"] = argument };

        return Closed(
            ("schema", Enum([V3AssistantPlanValidator.Schema])),
            ("parts", new JsonObject
            {
                ["type"] = "array",
                ["items"] = Closed(("part_id", Enum(parts)), ("span", Integers()), ("proposed_verdict", Enum(verdicts))),
            }),
            ("steps", new JsonObject
            {
                ["type"] = "array",
                ["items"] = Closed(
                    ("step_id", Enum(steps)),
                    ("part_id", Enum(parts)),
                    ("operation_id", Enum(V3AssistantPlanValidator.AllowedOperations)),
                    ("arguments", arguments.DeepClone())),
            }),
            ("follow_ups", new JsonObject
            {
                ["type"] = "array",
                ["items"] = Closed(
                    ("step_id", Enum(["f1", "f2"])),
                    ("after", Enum(steps)),
                    ("condition", Enum(["on_single_work", "on_single_article"])),
                    ("operation_id", Enum(FollowUpOperations)),
                    ("arguments", arguments.DeepClone())),
            }));
    }

    /// <summary>
    /// The model's output in the grammar's form: each step's and follow-up's <c>arguments</c> array of <c>{name, source}</c>
    /// written as one object member per entry, in the order given, and nothing else changed. An output that is not that shape
    /// is returned as it came, for the validator to refuse.
    /// </summary>
    public static string ToPlanJson(string wireJson)
    {
        ArgumentNullException.ThrowIfNull(wireJson);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(wireJson, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException)
        {
            return wireJson;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Mappable(root, "steps") || !Mappable(root, "follow_ups"))
            {
                return wireJson;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var member in root.EnumerateObject())
                {
                    if (member.NameEquals("steps") || member.NameEquals("follow_ups"))
                    {
                        writer.WriteStartArray(member.Name);
                        foreach (var step in member.Value.EnumerateArray())
                        {
                            writer.WriteStartObject();
                            foreach (var field in step.EnumerateObject())
                            {
                                if (!field.NameEquals("arguments"))
                                {
                                    field.WriteTo(writer);
                                    continue;
                                }

                                // A name written twice stays twice: the writer does not merge members, the validator refuses.
                                writer.WriteStartObject("arguments");
                                foreach (var entry in field.Value.EnumerateArray())
                                {
                                    writer.WritePropertyName(entry.GetProperty("name").GetString()!);
                                    entry.GetProperty("source").WriteTo(writer);
                                }

                                writer.WriteEndObject();
                            }

                            writer.WriteEndObject();
                        }

                        writer.WriteEndArray();
                    }
                    else
                    {
                        member.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    /// <summary>Whether a list's every item has an <c>arguments</c> array of objects holding exactly a string name and a source.</summary>
    private static bool Mappable(JsonElement root, string list) =>
        root.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().All(static item =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Array &&
            arguments.EnumerateArray().All(static entry =>
                entry.ValueKind == JsonValueKind.Object &&
                entry.EnumerateObject().Count() == 2 &&
                entry.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                entry.TryGetProperty("source", out _)));
}
