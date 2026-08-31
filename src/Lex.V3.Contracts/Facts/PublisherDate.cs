using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// A date exactly as the publisher served it: the lexical value, its datatype, and the precision
/// actually present in that value.
/// </summary>
/// <remarks>
/// <para>
/// The lexical form is kept verbatim. Parsing a publisher date into a calendar type and keeping
/// only the result throws away the distinction between <c>2019</c> and <c>2019-01-01</c>, which
/// then reappears downstream as a confident day-precision claim the publisher never made.
/// </para>
/// <para>
/// All four of datatype, lexical grammar, precision and calendar validity are bound to each
/// other. Candidate 1 checked only that the lexical shape matched the declared precision, so
/// <c>2019-02-30</c> at <c>xsd:date</c> was accepted, and so was a year-precision value carrying
/// the <c>xsd:date</c> datatype. A date that cannot exist is not a weaker fact, it is a wrong one.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublisherDate
{
    public const string Identity = FactsSchemaIds.PublisherDate;

    /// <summary>The EUR-Lex open-end date value.</summary>
    /// <remarks>
    /// The sentinel is this <b>date value</b> at <c>xsd:date</c>, in any lexical form the datatype
    /// admits, which is the bare value or the value with a valid timezone. Candidate 3 said "the
    /// exact lexical value" in its constant and its schema while the runtime compared the date
    /// part, so `9999-12-31Z` was the sentinel to the reader and an ordinary date to the schema.
    /// That mixed rule was neither of the two truths available, so this states the wider one and
    /// the schema now matches it with a pattern rather than a const.
    /// </remarks>
    public const string OpenEndedLexicalValue = "9999-12-31";

    /// <summary>The pattern matching every admitted lexical form of the sentinel.</summary>
    public const string OpenEndedLexicalPattern = @"^9999-12-31(Z|[+-](0[0-9]|1[0-3]):[0-5][0-9]|[+-]14:00)?$";

    public const string GYear = "http://www.w3.org/2001/XMLSchema#gYear";
    public const string GYearMonth = "http://www.w3.org/2001/XMLSchema#gYearMonth";
    public const string Date = "http://www.w3.org/2001/XMLSchema#date";

    /// <summary>
    /// The closed datatype set, each bound to the one precision its lexical space can express.
    /// </summary>
    public static ReadOnlyDictionary<string, DatePrecision> PrecisionByDatatype { get; } =
        new(new Dictionary<string, DatePrecision>(StringComparer.Ordinal)
        {
            [GYear] = DatePrecision.Year,
            [GYearMonth] = DatePrecision.YearMonth,
            [Date] = DatePrecision.YearMonthDay,
        });

    [JsonConstructor]
    public PublisherDate(
        string schema,
        string rawLexicalValue,
        string datatypeUri,
        DatePrecision precision,
        DateOpenSentinel openSentinel)
    {
        if (!string.Equals(schema, Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The publisher date schema must be version 1.", nameof(schema));
        }

        FactsValidation.RequireDefined(precision, nameof(precision));
        FactsValidation.RequireDefined(openSentinel, nameof(openSentinel));

        if (!FactsValidation.IsOpaqueIdentity(rawLexicalValue))
        {
            throw new ArgumentException(
                "A publisher date must keep its raw lexical value.",
                nameof(rawLexicalValue));
        }

        if (!PrecisionByDatatype.TryGetValue(datatypeUri ?? "", out var datatypePrecision))
        {
            throw new ArgumentException(
                "A publisher date must carry one of the three accepted XSD date datatypes.",
                nameof(datatypeUri));
        }

        if (datatypePrecision != precision)
        {
            throw new ArgumentException(
                $"{datatypeUri} expresses {datatypePrecision} precision, not {precision}.",
                nameof(precision));
        }

        if (!IsValidLexicalValue(rawLexicalValue, precision))
        {
            throw new ArgumentException(
                "The lexical value is not a real calendar date at the declared precision.",
                nameof(rawLexicalValue));
        }

        // The sentinel binds in BOTH directions. Candidate 2 enforced only one: an arbitrary date
        // could not claim the open end, but the open end could be declared an ordinary date, which
        // silently turns "validity does not end" into "validity ended in the year 9999" and is the
        // more damaging of the two readings.
        var isSentinelValue =
            string.Equals(DatePart(rawLexicalValue), OpenEndedLexicalValue, StringComparison.Ordinal) &&
            string.Equals(datatypeUri, Date, StringComparison.Ordinal);
        if (openSentinel == DateOpenSentinel.OpenEnded && !isSentinelValue)
        {
            throw new ArgumentException(
                $"Only {OpenEndedLexicalValue} at {Date} is the open-end sentinel.",
                nameof(openSentinel));
        }

        if (isSentinelValue && openSentinel != DateOpenSentinel.OpenEnded)
        {
            throw new ArgumentException(
                $"{OpenEndedLexicalValue} at {Date} is the open-end sentinel and cannot be {openSentinel}.",
                nameof(openSentinel));
        }

        Schema = schema;
        RawLexicalValue = rawLexicalValue;
        DatatypeUri = datatypeUri!;
        Precision = precision;
        OpenSentinel = openSentinel;
    }

    public string Schema { get; }

    public string RawLexicalValue { get; }

    public string DatatypeUri { get; }

    public DatePrecision Precision { get; }

    /// <summary>
    /// Whether this value is the publishers' open-end sentinel. Recorded rather than resolved:
    /// 9999-12-31 is a statement that validity is open, not a date in the year 9999.
    /// </summary>
    public DateOpenSentinel OpenSentinel { get; }

    /// <summary>
    /// The date part of a lexical value, with any XSD timezone suffix removed.
    /// </summary>
    /// <remarks>
    /// `xsd:date`, `xsd:gYear` and `xsd:gYearMonth` all admit an optional trailing timezone: `Z`
    /// or a signed `hh:mm` offset. Candidate 2 required exactly ten characters for a date, so it
    /// refused `2020-02-29Z`, a value inside the lexical space of the very datatype it claimed to
    /// carry. Refusing a publisher value the datatype admits is a loss dressed as strictness.
    /// </remarks>
    public static string DatePart(string value)
    {
        if (value is null or { Length: 0 })
        {
            return value ?? "";
        }

        if (value[^1] == 'Z')
        {
            return value[..^1];
        }

        // A signed offset is exactly six trailing characters, +hh:mm or -hh:mm.
        if (value.Length >= 7 && value[^6] is '+' or '-' && value[^3] == ':')
        {
            return value[..^6];
        }

        return value;
    }

    /// <summary>Whether a trailing timezone, if present, is well formed.</summary>
    private static bool HasValidTimezone(string value)
    {
        var date = DatePart(value);
        if (date.Length == value.Length)
        {
            return true;
        }

        var zone = value[date.Length..];
        if (zone == "Z")
        {
            return true;
        }

        if (zone.Length != 6 || zone[0] is not ('+' or '-') || zone[3] != ':')
        {
            return false;
        }

        for (var index = 1; index < 6; index++)
        {
            if (index == 3)
            {
                continue;
            }

            if (zone[index] is < '0' or > '9')
            {
                return false;
            }
        }

        var hours = int.Parse(zone.Substring(1, 2));
        var minutes = int.Parse(zone.Substring(4, 2));
        return hours <= 14 && minutes <= 59 && (hours < 14 || minutes == 0);
    }

    /// <summary>
    /// Whether a lexical value is a real calendar date at exactly the given precision.
    /// </summary>
    public static bool IsValidLexicalValue(string rawValue, DatePrecision precision)
    {
        if (rawValue is null || !HasValidTimezone(rawValue))
        {
            return false;
        }

        var value = DatePart(rawValue);
        if (value.Length == 0)
        {
            return false;
        }

        static bool Digits(string text, int start, int length)
        {
            for (var index = start; index < start + length; index++)
            {
                if (text[index] is < '0' or > '9')
                {
                    return false;
                }
            }

            return true;
        }

        switch (precision)
        {
            case DatePrecision.Year:
                return value.Length == 4 && Digits(value, 0, 4) && int.Parse(value) >= 1;

            case DatePrecision.YearMonth:
            {
                if (value.Length != 7 || !Digits(value, 0, 4) || value[4] != '-' || !Digits(value, 5, 2))
                {
                    return false;
                }

                var year = int.Parse(value[..4]);
                var month = int.Parse(value.Substring(5, 2));
                return year >= 1 && month is >= 1 and <= 12;
            }

            case DatePrecision.YearMonthDay:
            {
                if (value.Length != 10 || !Digits(value, 0, 4) || value[4] != '-' ||
                    !Digits(value, 5, 2) || value[7] != '-' || !Digits(value, 8, 2))
                {
                    return false;
                }

                var year = int.Parse(value[..4]);
                var month = int.Parse(value.Substring(5, 2));
                var day = int.Parse(value.Substring(8, 2));
                if (year < 1 || year > 9999 || month is < 1 or > 12)
                {
                    return false;
                }

                // A real calendar day, so 2019-02-30 and 2019-04-31 are refused and leap years
                // are honoured.
                return day >= 1 && day <= DateTime.DaysInMonth(year, month);
            }

            default:
                return false;
        }
    }
}
