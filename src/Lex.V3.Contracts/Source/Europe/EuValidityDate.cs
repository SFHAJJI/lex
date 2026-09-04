using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Which of the two observed spellings a validity date arrived in.
/// </summary>
/// <remarks>
/// This enum exists because the publisher is inconsistent, and it says so rather than hiding it.
/// The five retained located amendment axioms carry both spellings in one result set.
/// </remarks>
public enum EuValidityDateShape
{
    /// <summary>Hyphenated, for example <c>2000-02-09</c>. Two of the five retained rows.</summary>
    HyphenatedIso8601,

    /// <summary>Slash separated, for example <c>2010/02/01</c>. Two of the five retained rows.</summary>
    SlashSeparated,
}

/// <summary>
/// A <c>start_of_validity</c> or <c>end_of_validity</c> annotation value, carried in the spelling
/// the publisher actually used.
/// </summary>
/// <remarks>
/// <para>
/// <b>The publisher uses two date formats on one predicate.</b> The retained fixture
/// <c>amends-located-axioms.json</c> (sha256 <c>d3353e41e9091b20...</c>) returns five rows from one
/// query: two carry <c>2000-02-09</c>, two carry <c>2010/02/01</c> and <c>2010/01/01</c>, and one
/// carries no <c>start_of_validity</c> at all. A strict ISO 8601 parse throws on real data from
/// this predicate, which is the defect this type exists to make impossible.
/// </para>
/// <para>
/// <b>The set of two is closed and nothing is normalised.</b> A third spelling is refused by name,
/// naming the value that arrived. Neither spelling is rewritten into the other:
/// <see cref="RawLexicalValue"/> is the publisher's bytes, and <see cref="ObservedShape"/> records
/// which spelling those bytes are, so a later reader can tell the two apart instead of inheriting
/// a normalisation it cannot see or undo.
/// </para>
/// <para>
/// <b>The Facts date layer is reused, never paralleled.</b> <see cref="PublisherDate"/>'s grammar
/// admits the hyphenated spelling and refuses the slash one, so a hyphenated value is carried as a
/// real <see cref="PublisherDate"/> on <see cref="TypedDate"/> and gets that type's datatype,
/// precision, calendar and open-sentinel rules for free. A slash value has no
/// <see cref="PublisherDate"/> spelling and <see cref="TypedDate"/> is <c>null</c> for it. That
/// null is the honest answer, and it is why this type does not declare a second date vocabulary:
/// the calendar check for a slash value is still
/// <see cref="PublisherDate.IsValidLexicalValue"/>, called on a hyphenated probe built only to ask
/// the question and never stored, so <c>2010/02/30</c> is refused by exactly the rule that refuses
/// <c>2010-02-30</c>.
/// </para>
/// </remarks>
public sealed class EuValidityDate
{
    private EuValidityDate(string rawLexicalValue, EuValidityDateShape observedShape, PublisherDate? typedDate)
    {
        RawLexicalValue = rawLexicalValue;
        ObservedShape = observedShape;
        TypedDate = typedDate;
    }

    /// <summary>The publisher's value, byte for byte, in the spelling it arrived in.</summary>
    public string RawLexicalValue { get; }

    /// <summary>Which of the two closed spellings <see cref="RawLexicalValue"/> is.</summary>
    public EuValidityDateShape ObservedShape { get; }

    /// <summary>
    /// The Facts-layer date, present only for <see cref="EuValidityDateShape.HyphenatedIso8601"/>.
    /// <c>null</c> for a slash value, because <see cref="PublisherDate"/>'s lexical space does not
    /// contain that spelling and inventing one would be the parallel vocabulary this type refuses
    /// to build.
    /// </summary>
    public PublisherDate? TypedDate { get; }

    /// <summary>
    /// Reads a validity annotation value, refusing any spelling outside the observed two by name.
    /// </summary>
    public static EuValidityDate Create(string rawLexicalValue)
    {
        ArgumentNullException.ThrowIfNull(rawLexicalValue);

        var shape = ClassifyOrRefuse(rawLexicalValue);
        var hyphenated = shape == EuValidityDateShape.HyphenatedIso8601
            ? rawLexicalValue
            : string.Create(
                rawLexicalValue.Length,
                rawLexicalValue,
                static (destination, source) =>
                {
                    source.AsSpan().CopyTo(destination);
                    destination[4] = '-';
                    destination[7] = '-';
                });

        if (!PublisherDate.IsValidLexicalValue(hyphenated, DatePrecision.YearMonthDay))
        {
            throw new ArgumentException(
                $"\"{EuAuthorityQualifiedToken.Describe(rawLexicalValue)}\" is not a real calendar "
                    + "date at day precision.",
                nameof(rawLexicalValue));
        }

        var isSentinel = string.Equals(
            hyphenated,
            PublisherDate.OpenEndedLexicalValue,
            StringComparison.Ordinal);

        if (shape == EuValidityDateShape.SlashSeparated && isSentinel)
        {
            // Refused rather than guessed. The open end is a statement that validity does not end,
            // and PublisherDate binds it to exactly one lexical value at xsd:date. No slash-spelled
            // sentinel has been observed, so this type will not decide on its own whether
            // "9999/12/31" means the open end or a date in the year 9999. Reading it as the latter
            // is the more damaging of the two, and reading it as the former invents a second
            // sentinel spelling no observation supports.
            throw new ArgumentException(
                $"\"{rawLexicalValue}\" is the open-end date in a slash spelling. The open-end "
                    + $"sentinel is pinned to \"{PublisherDate.OpenEndedLexicalValue}\" at "
                    + $"{PublisherDate.Date} and no slash-spelled sentinel has been observed, so "
                    + "this value is refused instead of being read as either one.",
                nameof(rawLexicalValue));
        }

        var typedDate = shape == EuValidityDateShape.HyphenatedIso8601
            ? new PublisherDate(
                PublisherDate.Identity,
                rawLexicalValue,
                PublisherDate.Date,
                DatePrecision.YearMonthDay,
                isSentinel ? DateOpenSentinel.OpenEnded : DateOpenSentinel.NotOpen)
            : null;

        return new EuValidityDate(rawLexicalValue, shape, typedDate);
    }

    private static EuValidityDateShape ClassifyOrRefuse(string rawLexicalValue)
    {
        if (rawLexicalValue.Length == 10 &&
            AllDigits(rawLexicalValue, 0, 4) &&
            AllDigits(rawLexicalValue, 5, 2) &&
            AllDigits(rawLexicalValue, 8, 2) &&
            rawLexicalValue[4] == rawLexicalValue[7])
        {
            switch (rawLexicalValue[4])
            {
                case '-':
                    return EuValidityDateShape.HyphenatedIso8601;
                case '/':
                    return EuValidityDateShape.SlashSeparated;
            }
        }

        throw new ArgumentException(
            $"\"{EuAuthorityQualifiedToken.Describe(rawLexicalValue)}\" is not one of the two "
                + "observed validity-date spellings, hyphenated \"YYYY-MM-DD\" or slash separated "
                + "\"YYYY/MM/DD\". The set of two is closed and no value is normalised into it.",
            nameof(rawLexicalValue));
    }

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
