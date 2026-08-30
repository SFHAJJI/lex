using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lex.V3.Contracts;

internal static class PreviewDocumentCanonicalizer
{
    public const string Identity = "lex-v3-preview-document-canonical-json/1";

    public static byte[] Canonicalize(PreviewOperationCatalog value) =>
        CanonicalizeSerialized(value);

    public static byte[] Canonicalize(PreviewRefusalRegistry value) =>
        CanonicalizeSerialized(value);

    public static byte[] Canonicalize(PreviewObjectSet value) =>
        CanonicalizeSerialized(value);

    internal static byte[] CanonicalizeJsonForEvidence(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return CanonicalizeUtf8(Encoding.UTF8.GetBytes(json));
    }

    private static byte[] CanonicalizeSerialized<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return CanonicalizeUtf8(Encoding.UTF8.GetBytes(ContractJson.Serialize(value)));
    }

    private static byte[] CanonicalizeUtf8(byte[] serialized)
    {
        using var document = JsonDocument.Parse(
            serialized,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = PreviewContractLimits.MaximumPayloadDepth,
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
                var propertyNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value
                             .EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    if (!propertyNames.Add(property.Name))
                    {
                        throw new JsonException(
                            "Canonical preview objects reject duplicate decoded member names.");
                    }

                    if (!firstProperty)
                    {
                        WriteByte(output, (byte)',');
                    }

                    if (property.Name.Any(static character => character is < ' ' or > '~'))
                    {
                        throw new JsonException("Canonical preview property names must be printable ASCII.");
                    }

                    WriteJsonString(output, property.Name);
                    WriteByte(output, (byte)':');
                    WriteCanonical(output, property.Value);
                    firstProperty = false;
                }

                WriteByte(output, (byte)'}');
                return;
            case JsonValueKind.Array:
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
                throw new JsonException("Canonical preview documents contain only JSON data values.");
        }
    }

    private static void WriteCanonicalInteger(ArrayBufferWriter<byte> output, JsonElement value)
    {
        if (string.Equals(value.GetRawText(), "-0", StringComparison.Ordinal))
        {
            throw new JsonException("Canonical preview documents reject negative zero.");
        }

        if (value.TryGetInt64(out var signed))
        {
            WriteAscii(output, signed.ToString(CultureInfo.InvariantCulture));
            return;
        }

        throw new JsonException("Canonical preview documents permit signed 64-bit integers only.");
    }

    private static void WriteJsonString(ArrayBufferWriter<byte> output, string value)
    {
        WriteByte(output, (byte)'"');
        for (var index = 0; index < value.Length;)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                throw new JsonException("Canonical preview strings must contain valid Unicode scalars.");
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
