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
/// <see cref="Precision"/> is therefore checked against the lexical value rather than declared
/// freely: a value cannot claim day precision unless it carries a day.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublisherDate
{
    public const string Identity = FactsSchemaIds.PublisherDate;

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

        if (!FactsValidation.IsOpaqueIdentity(rawLexicalValue))
        {
            throw new ArgumentException(
                "A publisher date must keep its raw lexical value.",
                nameof(rawLexicalValue));
        }

        if (!FactsValidation.IsAbsoluteUri(datatypeUri))
        {
            throw new ArgumentException(
                "A publisher date must carry its datatype as an absolute URI.",
                nameof(datatypeUri));
        }

        if (DetectPrecision(rawLexicalValue) is not { } detected)
        {
            throw new ArgumentException(
                "A publisher date lexical value must be a year, year-month or year-month-day form.",
                nameof(rawLexicalValue));
        }

        if (detected != precision)
        {
            throw new ArgumentException(
                "The declared precision must be the precision present in the lexical value.",
                nameof(precision));
        }

        Schema = schema;
        RawLexicalValue = rawLexicalValue;
        DatatypeUri = datatypeUri;
        Precision = precision;
        OpenSentinel = openSentinel;
    }

    public string Schema { get; }

    public string RawLexicalValue { get; }

    public string DatatypeUri { get; }

    public DatePrecision Precision { get; }

    /// <summary>
    /// Whether this value is one of the publishers' open-ended sentinels. Recorded rather than
    /// resolved: 9999-12-31 is a statement that validity is open, not a date in the year 9999.
    /// </summary>
    public DateOpenSentinel OpenSentinel { get; }

    private static DatePrecision? DetectPrecision(string value) => value.Length switch
    {
        4 when AllDigits(value, 0, 4) => DatePrecision.Year,
        7 when AllDigits(value, 0, 4) && value[4] == '-' && AllDigits(value, 5, 2) =>
            DatePrecision.YearMonth,
        10 when AllDigits(value, 0, 4) && value[4] == '-' && AllDigits(value, 5, 2) &&
            value[7] == '-' && AllDigits(value, 8, 2) => DatePrecision.YearMonthDay,
        _ => null,
    };

    private static bool AllDigits(string value, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
