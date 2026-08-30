using System.Text;
using System.Globalization;

namespace Lex.V3.Artifacts;

public enum SyntheticDerivationFailureCode
{
    InvalidUtf8,
    Utf8BomForbidden,
    NoVisibleContent,
}

public sealed class SyntheticDerivationException : Exception
{
    internal SyntheticDerivationException(SyntheticDerivationFailureCode code)
        : base(code switch
        {
            SyntheticDerivationFailureCode.InvalidUtf8 => "Source bytes are not strict UTF-8.",
            SyntheticDerivationFailureCode.Utf8BomForbidden => "Source bytes cannot begin with a UTF-8 BOM.",
            SyntheticDerivationFailureCode.NoVisibleContent => "Derived text has no visible content.",
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        })
    {
        Code = code;
    }

    public SyntheticDerivationFailureCode Code { get; }
}

public static class SyntheticTextNormalizer
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] Normalize(ReadOnlySpan<byte> source)
    {
        if (source.StartsWith("\uFEFF"u8))
        {
            throw new SyntheticDerivationException(SyntheticDerivationFailureCode.Utf8BomForbidden);
        }

        string decoded;
        try
        {
            decoded = StrictUtf8.GetString(source);
        }
        catch (DecoderFallbackException)
        {
            throw new SyntheticDerivationException(SyntheticDerivationFailureCode.InvalidUtf8);
        }

        var normalized = decoded
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Normalize(NormalizationForm.FormC);
        if (!normalized.EnumerateRunes().Any(IsVisible))
        {
            throw new SyntheticDerivationException(SyntheticDerivationFailureCode.NoVisibleContent);
        }

        return StrictUtf8.GetBytes(normalized);
    }

    private static bool IsVisible(Rune rune)
    {
        if (Rune.IsWhiteSpace(rune))
        {
            return false;
        }

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.Control or
            UnicodeCategory.Format or
            UnicodeCategory.Surrogate or
            UnicodeCategory.PrivateUse or
            UnicodeCategory.OtherNotAssigned => false,
            _ => true,
        };
    }
}
