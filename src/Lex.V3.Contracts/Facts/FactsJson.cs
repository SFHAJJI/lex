using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// The serialisation boundary for the fact graph, owned by the contract rather than by
/// its callers.
///
/// This exists because a fail-closed guarantee that lives in a test's
/// <see cref="JsonSerializerOptions"/> is not a guarantee at all: the next caller
/// constructs its own options, gets silent tolerance, and the contract's promise
/// evaporates without a single test failing. Every read and write of a fact goes through
/// here, so the strictness is a property of the contract.
///
/// Four things are refused that the platform accepts by default:
/// unmapped members, so a document the schema forbids cannot round-trip;
/// integer enum values, so a member outside the closed vocabulary cannot be smuggled in
/// as a number;
/// duplicate object members, because last-wins would silently discard a publisher
/// statement and look like the publisher never made it;
/// and null in a required position, which <see cref="JsonRequiredAttribute"/> alone
/// permits.
/// </summary>
public static class FactsJson
{
    /// <summary>Maximum nesting accepted from any fact document.</summary>
    public const int MaximumDepth = 64;

    private static readonly JsonSerializerOptions Strict = CreateOptions();

    /// <summary>
    /// The canonical options for the fact graph. Read-only: a caller cannot relax the
    /// strictness by mutating a shared instance.
    /// </summary>
    public static JsonSerializerOptions Options => Strict;

    /// <summary>Serialises a fact value with the contract's own options.</summary>
    public static string Serialize<T>(T value)
        where T : IFactsValue
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        return JsonSerializer.Serialize(value, Strict);
    }

    /// <summary>
    /// Reads a fact value, refusing duplicate members before parsing and validating
    /// invariants after. The value that comes back has already refused null provenance.
    /// </summary>
    public static T Deserialize<T>(string json)
        where T : IFactsValue
    {
        ArgumentNullException.ThrowIfNull(json);
        var utf8 = Encoding.UTF8.GetBytes(json);
        RejectDuplicateMembers(utf8);

        var value = JsonSerializer.Deserialize<T>(utf8, Strict)
            ?? throw new JsonException("A fact document must not be JSON null.");

        value.Validate();
        return value;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = MaximumDepth,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
                new RelationFactConverter(),
            },
        };

        // Read-only from here: a caller cannot relax the strictness by mutating the
        // shared instance, which would silently widen the contract for everyone.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>
    /// Rejects a document that states the same member twice at any level.
    ///
    /// The platform silently keeps the last occurrence. For a contract whose purpose is
    /// losslessness that is the worst possible behaviour: the discarded value leaves no
    /// trace, so the loss is indistinguishable from the publisher never having said it.
    /// </summary>
    private static void RejectDuplicateMembers(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(
            utf8,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth,
            });

        var scopes = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    scopes.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    var name = reader.GetString()!;
                    if (!scopes.Peek().Add(name))
                    {
                        throw new JsonException(
                            $"Duplicate member '{name}'. Keeping the last occurrence would discard a publisher statement without a trace.");
                    }

                    break;
                default:
                    break;
            }
        }
    }
}

/// <summary>
/// Reads and writes the closed <see cref="RelationFact"/> union with an explicit tag.
///
/// The tag is written, never inferred from shape. Shape inference is exactly how a
/// derived edge ends up presented as a publisher assertion: two variants that happen to
/// share members would be read as whichever one the matcher tried first.
/// </summary>
internal sealed class RelationFactConverter : JsonConverter<RelationFact>
{
    /// <summary>The member carrying the closed variant tag.</summary>
    internal const string TagMember = "fact_kind";

    public override RelationFact Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A relation fact must be a JSON object carrying its variant tag.");
        }

        if (!root.TryGetProperty(TagMember, out var tag))
        {
            throw new JsonException(
                $"A relation fact must carry '{TagMember}'. Without it the variant would have to be guessed from shape, which is how a derived edge becomes an official one.");
        }

        if (tag.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"'{TagMember}' must be a string naming a closed variant.");
        }

        var kind = tag.GetString();
        var variant = kind switch
        {
            PublisherRelation.Kind => typeof(PublisherRelation),
            DerivedInverseRelation.Kind => typeof(DerivedInverseRelation),
            LocalInboundView.Kind => typeof(LocalInboundView),
            _ => throw new JsonException(
                $"Unknown {TagMember} '{kind}'. The relation fact union is closed, so an unrecognised variant fails rather than defaulting to a known one."),
        };

        var payload = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            foreach (var member in root.EnumerateObject())
            {
                if (member.NameEquals(TagMember))
                {
                    continue;
                }

                member.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        var fact = (RelationFact?)JsonSerializer.Deserialize(payload.WrittenSpan, variant, options)
            ?? throw new JsonException("A relation fact must not be JSON null.");

        fact.Validate();
        return fact;
    }

    public override void Write(Utf8JsonWriter writer, RelationFact value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();

        var payload = JsonSerializer.SerializeToElement(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString(TagMember, value.Variant);
        foreach (var member in payload.EnumerateObject())
        {
            member.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
