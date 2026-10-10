using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// Canonical bytes and canonical ordering for the fact graph.
///
/// This is deliberately a facts-specific canonicalisation with its own frozen identity,
/// not a reuse of the preview document canonicaliser. Sharing one would make a change
/// made for a preview page silently redefine what a legal fact hashes to, and the
/// resulting digest change would look like the law had changed.
///
/// Two rules matter more than the byte format itself.
///
/// Object members are sorted; array items are never sorted. An array in this graph holds
/// publisher order, and publisher order is an observation. Sorting a qualified-axiom list
/// would report our arrangement as the publisher's.
///
/// Ordering *between* facts, where the publisher supplied no order, is the ordinal byte
/// order of each complete canonical entry, every identity-bearing and provenance-bearing
/// member included. Ordering on a subset of members would let two distinct facts compare
/// equal and one of them would be dropped by whatever sorted them.
/// </summary>
public static class FactsCanonicalizer
{
    /// <summary>The frozen identity of this canonicalisation, prefixed to every output.</summary>
    public const string Identity = "lex-v3-facts-canonical-json/1";

    /// <summary>
    /// Produces the canonical bytes of one fact. A <see cref="RelationFact"/> is
    /// canonicalised through the union so its variant tag is part of its identity: a
    /// publisher assertion and a derived inverse with identical members must not share
    /// canonical bytes.
    /// </summary>
    public static byte[] Canonicalize<T>(T value)
        where T : IFactsValue
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();

        var json = value is RelationFact fact
            ? JsonSerializer.Serialize(fact, typeof(RelationFact), FactsJson.Options)
            : JsonSerializer.Serialize(value, value.GetType(), FactsJson.Options);

        return CanonicalizeUtf8(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Orders facts by the ordinal byte order of their complete canonical entries.
    ///
    /// The sort is stable and never removes a member. Two facts with identical canonical
    /// bytes are two facts: their occurrence identity is part of those bytes, so equal
    /// bytes mean the same occurrence was supplied twice, which is the caller's fact to
    /// keep rather than this method's to tidy away.
    /// </summary>
    public static IReadOnlyList<T> InCanonicalOrder<T>(IEnumerable<T> facts)
        where T : IFactsValue
    {
        ArgumentNullException.ThrowIfNull(facts);

        return facts
            .Select(fact => (Fact: fact, Bytes: Canonicalize(fact)))
            .OrderBy(entry => entry.Bytes, OrdinalByteOrder)
            .Select(entry => entry.Fact)
            .ToList();
    }

    /// <summary>Ordinal byte comparison, shortest-prefix-first, with no culture involved.</summary>
    public static IComparer<byte[]> OrdinalByteOrder { get; } = new ByteOrderComparer();

    private sealed class ByteOrderComparer : IComparer<byte[]>
    {
        public int Compare(byte[]? x, byte[]? y)
        {
            if (x is null || y is null)
            {
                throw new FactsContractViolationException(
                    "Canonical byte ordering received a null entry; a fact without canonical bytes cannot be ordered.");
            }

            return x.AsSpan().SequenceCompareTo(y.AsSpan());
        }
    }

    private static byte[] CanonicalizeUtf8(byte[] serialized)
    {
        using var document = JsonDocument.Parse(
            serialized,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = FactsJson.MaximumDepth,
            });

        var output = new ArrayBufferWriter<byte>();
        WriteAscii(output, Identity);
        WriteByte(output, (byte)'\n');
        WriteCanonical(output, document.RootElement);
        WriteByte(output, (byte)'\n');
        return output.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(ArrayBufferWriter<byte> output, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WriteByte(output, (byte)'{');
                var firstProperty = true;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value
                             .EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    if (!seen.Add(property.Name))
                    {
                        throw new JsonException(
                            $"Canonical fact objects reject the duplicate member '{property.Name}'.");
                    }

                    if (property.Name.Any(static character => character is < ' ' or > '~'))
                    {
                        throw new JsonException("Canonical fact member names must be printable ASCII.");
                    }

                    if (!firstProperty)
                    {
                        WriteByte(output, (byte)',');
                    }

                    WriteJsonString(output, property.Name);
                    WriteByte(output, (byte)':');
                    WriteCanonical(output, property.Value);
                    firstProperty = false;
                }

                WriteByte(output, (byte)'}');
                return;

            case JsonValueKind.Array:
                // Never sorted. The order is the publisher's.
                WriteByte(output, (byte)'[');
                var firstItem = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        WriteByte(output, (byte)',');
                    }

                    WriteCanonical(output, item);
                    firstItem = false;
                }

                WriteByte(output, (byte)']');
                return;

            case JsonValueKind.String:
                WriteJsonString(output, value.GetString()!);
                return;

            case JsonValueKind.Number:
                WriteCanonicalInteger(output, value);
                return;

            case JsonValueKind.True:
                WriteAscii(output, "true");
                return;

            case JsonValueKind.False:
                WriteAscii(output, "false");
                return;

            case JsonValueKind.Null:
                WriteAscii(output, "null");
                return;

            default:
                throw new JsonException("Canonical fact documents contain only JSON data values.");
        }
    }

    private static void WriteCanonicalInteger(ArrayBufferWriter<byte> output, JsonElement value)
    {
        if (string.Equals(value.GetRawText(), "-0", StringComparison.Ordinal))
        {
            throw new JsonException("Canonical fact documents reject negative zero.");
        }

        if (value.TryGetInt64(out var signed))
        {
            WriteAscii(output, signed.ToString(CultureInfo.InvariantCulture));
            return;
        }

        throw new JsonException("Canonical fact documents permit signed 64-bit integers only.");
    }

    private static void WriteJsonString(ArrayBufferWriter<byte> output, string value)
    {
        WriteByte(output, (byte)'"');
        for (var index = 0; index < value.Length;)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                throw new JsonException("Canonical fact strings must contain valid Unicode scalars.");
            }

            index += consumed;
            switch (rune.Value)
            {
                case '"':
                    WriteAscii(output, "\\\"");
                    break;
                case '\\':
                    WriteAscii(output, "\\\\");
                    break;
                case '\b':
                    WriteAscii(output, "\\b");
                    break;
                case '\t':
                    WriteAscii(output, "\\t");
                    break;
                case '\n':
                    WriteAscii(output, "\\n");
                    break;
                case '\f':
                    WriteAscii(output, "\\f");
                    break;
                case '\r':
                    WriteAscii(output, "\\r");
                    break;
                case < 0x20:
                    WriteAscii(output, $"\\u{rune.Value:x4}");
                    break;
                default:
                    var target = output.GetSpan(rune.Utf8SequenceLength);
                    var written = rune.EncodeToUtf8(target);
                    output.Advance(written);
                    break;
            }
        }

        WriteByte(output, (byte)'"');
    }

    private static void WriteAscii(ArrayBufferWriter<byte> output, string value)
    {
        var target = output.GetSpan(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] > 0x7f)
            {
                throw new InvalidOperationException("The canonical ASCII writer received Unicode.");
            }

            target[index] = (byte)value[index];
        }

        output.Advance(value.Length);
    }

    private static void WriteByte(ArrayBufferWriter<byte> output, byte value)
    {
        output.GetSpan(1)[0] = value;
        output.Advance(1);
    }
}
