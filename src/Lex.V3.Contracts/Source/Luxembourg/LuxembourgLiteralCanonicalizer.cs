using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Lex.V3.Contracts.Source.Luxembourg;

public enum LuxembourgLiteralDisposition
{
    Accepted = 1,
    TypedQuarantine = 2,
}

public enum LuxembourgLiteralReason
{
    AcceptedXsdStringIdentity = 1,
    AcceptedRdfLangStringIdentity = 2,
    AcceptedXsdDateCanonical = 3,
    TypedQuarantineUnsupportedDatatype = 4,
    TypedQuarantineContextDependentDatatype = 5,
    TypedQuarantineIllTyped = 6,
}

public sealed record LuxembourgLiteralCanonicalization
{
    internal LuxembourgLiteralCanonicalization(
        string rawLexicalValue,
        string rawDatatypeIriOrEmpty,
        string rawLanguageTagOrEmpty,
        string datatypeIri,
        string languageTag,
        string? canonicalSelectorLexicalValue,
        LuxembourgLiteralDisposition disposition,
        LuxembourgLiteralReason reason)
    {
        RawLexicalValue = rawLexicalValue;
        RawDatatypeIriOrEmpty = rawDatatypeIriOrEmpty;
        RawLanguageTagOrEmpty = rawLanguageTagOrEmpty;
        DatatypeIri = datatypeIri;
        LanguageTag = languageTag;
        CanonicalSelectorLexicalValue = canonicalSelectorLexicalValue;
        Disposition = disposition;
        Reason = reason;
    }

    public string RawLexicalValue { get; }

    public string RawDatatypeIriOrEmpty { get; }

    public string RawLanguageTagOrEmpty { get; }

    public string DatatypeIri { get; }

    public string LanguageTag { get; }

    /// <summary>
    /// The admitted canonical selector value. It is null for every quarantined term, whose source
    /// spelling remains available only through <see cref="RawLexicalValue"/>.
    /// </summary>
    public string? CanonicalSelectorLexicalValue { get; }

    public LuxembourgLiteralDisposition Disposition { get; }

    public LuxembourgLiteralReason Reason { get; }

    public string ReasonCode => Reason switch
    {
        LuxembourgLiteralReason.AcceptedXsdStringIdentity =>
            "accepted_xsd_string_identity",
        LuxembourgLiteralReason.AcceptedRdfLangStringIdentity =>
            "accepted_rdf_lang_string_identity",
        LuxembourgLiteralReason.AcceptedXsdDateCanonical =>
            "accepted_xsd_date_canonical",
        LuxembourgLiteralReason.TypedQuarantineUnsupportedDatatype =>
            "typed_quarantine_unsupported_datatype",
        LuxembourgLiteralReason.TypedQuarantineContextDependentDatatype =>
            "typed_quarantine_context_dependent_datatype",
        LuxembourgLiteralReason.TypedQuarantineIllTyped =>
            "typed_quarantine_ill_typed",
        _ => throw new InvalidOperationException("The literal reason is outside the closed set."),
    };
}

/// <summary>
/// Converts an observed RDF literal into a manifest selector without discarding its source term.
/// </summary>
public static class LuxembourgLiteralCanonicalizer
{
    public const string RdfLangString =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString";
    public const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    public const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    private const string XsdQName = "http://www.w3.org/2001/XMLSchema#QName";
    private const string XsdNotation = "http://www.w3.org/2001/XMLSchema#NOTATION";
    private const string RdfXmlLiteral =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#XMLLiteral";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly ReadOnlyCollection<string> SupportedDatatypes =
        Array.AsReadOnly(new[] { RdfLangString, XsdDate, XsdString });

    private static readonly HashSet<string> ContextDependentDatatypes = new(
        new[] { RdfXmlLiteral, XsdNotation, XsdQName },
        StringComparer.Ordinal);

    private static readonly HashSet<string> GrandfatheredLanguageTags = new(
        new[]
        {
            "art-lojban", "cel-gaulish", "en-gb-oed", "i-ami", "i-bnn", "i-default",
            "i-enochian", "i-hak", "i-klingon", "i-lux", "i-mingo", "i-navajo",
            "i-pwn", "i-tao", "i-tay", "i-tsu", "no-bok", "no-nyn", "sgn-be-fr",
            "sgn-be-nl", "sgn-ch-de", "zh-guoyu", "zh-hakka", "zh-min", "zh-min-nan",
            "zh-xiang",
        },
        StringComparer.Ordinal);

    public static IReadOnlyList<string> SupportedDatatypeIris => SupportedDatatypes;

    public static LuxembourgLiteralCanonicalization Canonicalize(
        string rawLexicalValue,
        string rawDatatypeIriOrEmpty,
        string rawLanguageTagOrEmpty)
    {
        ArgumentNullException.ThrowIfNull(rawLexicalValue);
        ArgumentNullException.ThrowIfNull(rawDatatypeIriOrEmpty);
        ArgumentNullException.ThrowIfNull(rawLanguageTagOrEmpty);

        var datatypeIri = rawDatatypeIriOrEmpty.Length == 0
            ? rawLanguageTagOrEmpty.Length == 0 ? XsdString : RdfLangString
            : rawDatatypeIriOrEmpty;

        if (!IsScalarString(rawLexicalValue) ||
            rawLexicalValue.Contains('\0', StringComparison.Ordinal) ||
            !IsScalarString(rawDatatypeIriOrEmpty) ||
            !IsScalarString(rawLanguageTagOrEmpty) ||
            !IsAbsoluteIri(datatypeIri))
        {
            return Quarantine(
                rawLexicalValue,
                rawDatatypeIriOrEmpty,
                rawLanguageTagOrEmpty,
                datatypeIri,
                LuxembourgLiteralReason.TypedQuarantineIllTyped);
        }

        var hasLanguage = rawLanguageTagOrEmpty.Length != 0;
        if (hasLanguage != string.Equals(datatypeIri, RdfLangString, StringComparison.Ordinal) ||
            (hasLanguage && !IsLanguageTag(rawLanguageTagOrEmpty)))
        {
            return Quarantine(
                rawLexicalValue,
                rawDatatypeIriOrEmpty,
                rawLanguageTagOrEmpty,
                datatypeIri,
                LuxembourgLiteralReason.TypedQuarantineIllTyped);
        }

        if (string.Equals(datatypeIri, XsdString, StringComparison.Ordinal))
        {
            return Accepted(
                rawLexicalValue,
                rawDatatypeIriOrEmpty,
                rawLanguageTagOrEmpty,
                datatypeIri,
                string.Empty,
                rawLexicalValue,
                LuxembourgLiteralReason.AcceptedXsdStringIdentity);
        }

        if (string.Equals(datatypeIri, RdfLangString, StringComparison.Ordinal))
        {
            return Accepted(
                rawLexicalValue,
                rawDatatypeIriOrEmpty,
                rawLanguageTagOrEmpty,
                datatypeIri,
                rawLanguageTagOrEmpty.ToLowerInvariant(),
                rawLexicalValue,
                LuxembourgLiteralReason.AcceptedRdfLangStringIdentity);
        }

        if (string.Equals(datatypeIri, XsdDate, StringComparison.Ordinal))
        {
            return TryCanonicalizeDate(rawLexicalValue, out var canonicalDate)
                ? Accepted(
                    rawLexicalValue,
                    rawDatatypeIriOrEmpty,
                    rawLanguageTagOrEmpty,
                    datatypeIri,
                    string.Empty,
                    canonicalDate,
                    LuxembourgLiteralReason.AcceptedXsdDateCanonical)
                : Quarantine(
                    rawLexicalValue,
                    rawDatatypeIriOrEmpty,
                    rawLanguageTagOrEmpty,
                    datatypeIri,
                    LuxembourgLiteralReason.TypedQuarantineIllTyped);
        }

        return Quarantine(
            rawLexicalValue,
            rawDatatypeIriOrEmpty,
            rawLanguageTagOrEmpty,
            datatypeIri,
            ContextDependentDatatypes.Contains(datatypeIri)
                ? LuxembourgLiteralReason.TypedQuarantineContextDependentDatatype
                : LuxembourgLiteralReason.TypedQuarantineUnsupportedDatatype);
    }

    private static LuxembourgLiteralCanonicalization Accepted(
        string rawLexicalValue,
        string rawDatatypeIriOrEmpty,
        string rawLanguageTagOrEmpty,
        string datatypeIri,
        string languageTag,
        string canonicalLexicalValue,
        LuxembourgLiteralReason reason) => new(
            rawLexicalValue,
            rawDatatypeIriOrEmpty,
            rawLanguageTagOrEmpty,
            datatypeIri,
            languageTag,
            canonicalLexicalValue,
            LuxembourgLiteralDisposition.Accepted,
            reason);

    private static LuxembourgLiteralCanonicalization Quarantine(
        string rawLexicalValue,
        string rawDatatypeIriOrEmpty,
        string rawLanguageTagOrEmpty,
        string datatypeIri,
        LuxembourgLiteralReason reason) => new(
            rawLexicalValue,
            rawDatatypeIriOrEmpty,
            rawLanguageTagOrEmpty,
            datatypeIri,
            rawLanguageTagOrEmpty,
            null,
            LuxembourgLiteralDisposition.TypedQuarantine,
            reason);

    private static bool IsScalarString(string value)
    {
        try
        {
            _ = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsAbsoluteIri(string value) =>
        value.Length != 0 &&
        !value.Any(char.IsWhiteSpace) &&
        Uri.TryCreate(value, UriKind.Absolute, out _);

    private static bool IsLanguageTag(string value)
    {
        if (GrandfatheredLanguageTags.Contains(value.ToLowerInvariant()))
        {
            return true;
        }

        var subtags = value.Split('-', StringSplitOptions.None);
        if (subtags.Length == 0 || subtags.Any(static part => part.Length == 0))
        {
            return false;
        }

        var index = 0;
        if (string.Equals(subtags[0], "x", StringComparison.OrdinalIgnoreCase))
        {
            return subtags.Length > 1 &&
                subtags.Skip(1).All(static part => IsAlphaNumeric(part, 1, 8));
        }

        var primary = subtags[index++];
        if (!IsAsciiLetters(primary, 2, 8))
        {
            return false;
        }

        if (primary.Length is 2 or 3)
        {
            var extlangCount = 0;
            while (index < subtags.Length &&
                   extlangCount < 3 &&
                   IsAsciiLetters(subtags[index], 3, 3))
            {
                index++;
                extlangCount++;
            }
        }

        if (index < subtags.Length && IsAsciiLetters(subtags[index], 4, 4))
        {
            index++;
        }

        if (index < subtags.Length &&
            (IsAsciiLetters(subtags[index], 2, 2) || IsAsciiDigits(subtags[index], 3, 3)))
        {
            index++;
        }

        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (index < subtags.Length && IsVariant(subtags[index]))
        {
            if (!variants.Add(subtags[index]))
            {
                return false;
            }

            index++;
        }

        var extensionSingletons = new HashSet<char>();
        while (index < subtags.Length && IsExtensionSingleton(subtags[index]))
        {
            var singleton = char.ToLowerInvariant(subtags[index][0]);
            if (!extensionSingletons.Add(singleton))
            {
                return false;
            }

            index++;
            var firstPayload = index;
            while (index < subtags.Length && IsAlphaNumeric(subtags[index], 2, 8))
            {
                index++;
            }

            if (index == firstPayload)
            {
                return false;
            }
        }

        if (index < subtags.Length &&
            string.Equals(subtags[index], "x", StringComparison.OrdinalIgnoreCase))
        {
            index++;
            var firstPrivate = index;
            while (index < subtags.Length && IsAlphaNumeric(subtags[index], 1, 8))
            {
                index++;
            }

            if (index == firstPrivate)
            {
                return false;
            }
        }

        return index == subtags.Length;
    }

    private static bool IsVariant(string value) =>
        IsAlphaNumeric(value, 5, 8) ||
        value.Length == 4 && value[0] is >= '0' and <= '9' && IsAlphaNumeric(value, 4, 4);

    private static bool IsExtensionSingleton(string value) =>
        value.Length == 1 &&
        value[0] is not ('x' or 'X') &&
        IsAsciiAlphaNumeric(value[0]);

    private static bool IsAsciiLetters(string value, int minimum, int maximum) =>
        value.Length >= minimum && value.Length <= maximum &&
        value.All(static character =>
            character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));

    private static bool IsAsciiDigits(string value, int minimum, int maximum) =>
        value.Length >= minimum && value.Length <= maximum &&
        value.All(static character => character is >= '0' and <= '9');

    private static bool IsAlphaNumeric(string value, int minimum, int maximum) =>
        value.Length >= minimum && value.Length <= maximum && value.All(IsAsciiAlphaNumeric);

    private static bool IsAsciiAlphaNumeric(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');

    private static bool TryCanonicalizeDate(string value, out string canonical)
    {
        canonical = string.Empty;
        var negativeYear = value.StartsWith("-", StringComparison.Ordinal);
        var yearStart = negativeYear ? 1 : 0;
        var yearEnd = value.IndexOf('-', yearStart);
        if (yearEnd < yearStart + 4)
        {
            return false;
        }

        var yearText = value.AsSpan(yearStart, yearEnd - yearStart);
        if (!AllDigits(yearText) || (yearText.Length > 4 && yearText[0] == '0') ||
            !BigInteger.TryParse(
                yearText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var year))
        {
            return false;
        }

        if (negativeYear)
        {
            year = -year;
        }

        var monthStart = yearEnd + 1;
        var dayStart = monthStart + 3;
        var timezoneStart = dayStart + 2;
        if (value.Length < timezoneStart ||
            monthStart + 2 >= value.Length ||
            value[monthStart + 2] != '-' ||
            !TryTwoDigits(value.AsSpan(monthStart, 2), out var month) ||
            !TryTwoDigits(value.AsSpan(dayStart, 2), out var day) ||
            month is < 1 or > 12 ||
            day < 1 ||
            day > DaysInMonth(year, month))
        {
            return false;
        }

        var timezone = value.AsSpan(timezoneStart);
        int? offsetMinutes;
        if (timezone.Length == 0)
        {
            offsetMinutes = null;
        }
        else if (timezone.SequenceEqual("Z"))
        {
            offsetMinutes = 0;
        }
        else if (timezone.Length == 6 &&
                 timezone[0] is '+' or '-' &&
                 timezone[3] == ':' &&
                 TryTwoDigits(timezone.Slice(1, 2), out var hours) &&
                 TryTwoDigits(timezone.Slice(4, 2), out var minutes) &&
                 hours <= 14 && minutes <= 59 && (hours < 14 || minutes == 0))
        {
            offsetMinutes = (hours * 60 + minutes) * (timezone[0] == '-' ? -1 : 1);
        }
        else
        {
            return false;
        }

        if (offsetMinutes > 12 * 60)
        {
            AddDay(ref year, ref month, ref day, -1);
            offsetMinutes -= 24 * 60;
        }
        else if (offsetMinutes <= -12 * 60)
        {
            AddDay(ref year, ref month, ref day, 1);
            offsetMinutes += 24 * 60;
        }

        canonical = CanonicalYear(year) +
            $"-{month:00}-{day:00}" +
            CanonicalTimezone(offsetMinutes);
        return true;
    }

    private static bool AllDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryTwoDigits(ReadOnlySpan<char> value, out int result)
    {
        result = 0;
        if (value.Length != 2 || !AllDigits(value))
        {
            return false;
        }

        result = (value[0] - '0') * 10 + value[1] - '0';
        return true;
    }

    private static int DaysInMonth(BigInteger year, int month) => month switch
    {
        2 => IsLeapYear(year) ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    private static bool IsLeapYear(BigInteger year) =>
        year % 400 == 0 || (year % 4 == 0 && year % 100 != 0);

    private static void AddDay(ref BigInteger year, ref int month, ref int day, int delta)
    {
        if (delta < 0)
        {
            if (day > 1)
            {
                day--;
                return;
            }

            if (month == 1)
            {
                year--;
                month = 12;
            }
            else
            {
                month--;
            }

            day = DaysInMonth(year, month);
            return;
        }

        if (day < DaysInMonth(year, month))
        {
            day++;
            return;
        }

        day = 1;
        if (month == 12)
        {
            year++;
            month = 1;
        }
        else
        {
            month++;
        }
    }

    private static string CanonicalYear(BigInteger year)
    {
        var magnitude = BigInteger.Abs(year).ToString(CultureInfo.InvariantCulture);
        if (magnitude.Length < 4)
        {
            magnitude = magnitude.PadLeft(4, '0');
        }

        return year < 0 ? "-" + magnitude : magnitude;
    }

    private static string CanonicalTimezone(int? offsetMinutes)
    {
        if (offsetMinutes is null)
        {
            return string.Empty;
        }

        if (offsetMinutes == 0)
        {
            return "Z";
        }

        var magnitude = Math.Abs(offsetMinutes.Value);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(offsetMinutes < 0 ? '-' : '+')}{magnitude / 60:00}:{magnitude % 60:00}");
    }
}
