namespace Lex.V3.Artifacts;

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static byte[] Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new FormatException("The value is not unpadded base64url.");
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => throw new FormatException("The base64url length is invalid."),
        };
        var decoded = Convert.FromBase64String(base64);
        if (!string.Equals(Encode(decoded), value, StringComparison.Ordinal))
        {
            throw new FormatException("The base64url value is not canonical.");
        }

        return decoded;
    }
}
