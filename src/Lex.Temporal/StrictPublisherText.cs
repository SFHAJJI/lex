using System.Text;

namespace Lex.Temporal;

/// <summary>
/// The only primary publisher-body decoder. Evidence bytes stay unchanged; derivation accepts
/// only strict UTF-8 or its ASCII subset and never manufactures replacement characters.
/// </summary>
public static class StrictPublisherText
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> bytes, string? charset)
    {
        var normalized = charset?.Trim().Trim('"').Replace('_', '-').ToLowerInvariant();
        if (normalized is { Length: > 0 }
            && normalized is not "utf-8" and not "utf8" and not "us-ascii")
            throw new InvalidDataException(
                "Publisher body declared an unsupported character encoding.");
        if (normalized == "us-ascii")
            foreach (var value in bytes)
                if (value > 0x7f)
                    throw new InvalidDataException(
                        "Publisher body violates its US-ASCII declaration.");
        if (bytes.StartsWith(new byte[] { 0xff, 0xfe })
            || bytes.StartsWith(new byte[] { 0xfe, 0xff }))
            throw new InvalidDataException(
                "Publisher body uses an unsupported UTF-16 byte-order mark.");
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            if (normalized == "us-ascii")
                throw new InvalidDataException(
                    "Publisher body has a UTF-8 byte-order mark under US-ASCII.");
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }
        try
        {
            return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException(
                "Publisher body is not valid strict UTF-8.", error);
        }
    }
}
