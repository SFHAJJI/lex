using System.Collections.ObjectModel;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// One token of a <c>reference_to_modified_location</c> value: a structural location code, the
/// authority IRI that code is a member of, and the opaque value that follows it.
/// </summary>
/// <remarks>
/// <para>
/// The wire shape is <c>{CODE|AUTHORITY_IRI}</c> optionally followed by a value, for example
/// <c>{AN|http://publications.europa.eu/resource/authority/fd_370/AN} 1</c>. Both halves are kept.
/// Dropping the IRI, as review/22 section 3's rendering does, leaves a bare code with no list to
/// resolve it against, and two different EUR-Lex lists mint codes that collide as bare strings.
/// </para>
/// <para>
/// <b><see cref="Value"/> is never parsed as a number.</b> The retained fixture carries
/// <c>IA</c> as a trailing value on an <c>AN</c> token, beside <c>1</c> and <c>2</c> on others, so
/// an integer parse fails on real publisher data on the first Roman-numeral annex it meets. The
/// value is validated as a bounded printable string and carried as one.
/// </para>
/// <para>
/// <b><see cref="Value"/> is optional.</b> The canary event
/// <c>lex-event-20260904T175313280Z-e99a2a04ab2e44fb8bc5a5aa66d14451</c> reads a real location
/// ending <c>{PTA|...}</c> with nothing after it, so a token with no trailing value is ordinary
/// publisher data and not a malformed token.
/// </para>
/// </remarks>
public sealed class EuAuthorityQualifiedToken
{
    private EuAuthorityQualifiedToken(string code, string authorityUri, string? value)
    {
        Code = code;
        AuthorityUri = authorityUri;
        Value = value;
    }

    /// <summary>The member code exactly as observed, for example <c>AN</c>, <c>AR</c> or <c>PTA</c>.</summary>
    public string Code { get; }

    /// <summary>
    /// The full member IRI exactly as observed, for example
    /// <c>http://publications.europa.eu/resource/authority/fd_370/AN</c>.
    /// </summary>
    public string AuthorityUri { get; }

    /// <summary>
    /// The value trailing this token, carried verbatim, or <c>null</c> when the publisher wrote
    /// none. Never converted to a number.
    /// </summary>
    public string? Value { get; }

    /// <summary>
    /// Builds a token, refusing an authority outside <paramref name="expectedAuthorityListUri"/>
    /// by naming both what was expected and what arrived.
    /// </summary>
    /// <remarks>
    /// The member IRI must be exactly the list IRI, a slash, and this token's own code. Every one
    /// of the ten authority-qualified values in the retained fixture holds that relation, five
    /// against fd_370 and five against fd_375. A pair whose halves disagree names a code in one
    /// list and a member of another, which is not a fact this type can carry coherently, so it is
    /// refused rather than kept with one half silently winning.
    /// </remarks>
    public static EuAuthorityQualifiedToken Create(
        string code,
        string authorityUri,
        string? value,
        string expectedAuthorityListUri)
    {
        ArgumentNullException.ThrowIfNull(expectedAuthorityListUri);
        if (!IsAdmittedCode(code))
        {
            throw new ArgumentException(
                $"\"{Describe(code)}\" is not an authority code: a code is 1 to 64 printable "
                    + "ASCII characters carrying no space, brace or vertical bar.",
                nameof(code));
        }

        ArgumentNullException.ThrowIfNull(authorityUri);
        var expected = expectedAuthorityListUri + "/" + code;
        if (!string.Equals(authorityUri, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"\"{Describe(authorityUri)}\" is not a member of the pinned authority list "
                    + $"{expectedAuthorityListUri}; the code \"{code}\" requires exactly "
                    + $"\"{expected}\".",
                nameof(authorityUri));
        }

        if (value is not null && !IsAdmittedValue(value))
        {
            throw new ArgumentException(
                $"\"{Describe(value)}\" is not an admitted location value: a value is 1 to 128 "
                    + "printable ASCII characters carrying no brace.",
                nameof(value));
        }

        return new EuAuthorityQualifiedToken(code, authorityUri, value);
    }

    /// <remarks>
    /// <b>The ASCII bound is observed, not specified.</b> Every code in the retained fixture and in
    /// the canary quotations is short uppercase ASCII: <c>AN</c>, <c>AR</c>, <c>PTA</c>, and the
    /// fd_375 members <c>R</c>, <c>J</c> and <c>M</c>. No publisher document has been read stating
    /// the character set these lists may draw on, so this bound describes what has been seen rather
    /// than what is permitted. If a code outside it ever appears it will surface as a refusal
    /// naming the offending value, which is the visible failure, not the silent one; widening it is
    /// then a one-line change against that observation.
    /// </remarks>
    internal static bool IsAdmittedCode(string? code)
    {
        if (code is not { Length: >= 1 and <= 64 })
        {
            return false;
        }

        foreach (var character in code)
        {
            if (character is < '!' or > '~' or '{' or '}' or '|')
            {
                return false;
            }
        }

        return true;
    }

    /// <remarks>
    /// The same observed bound as <see cref="IsAdmittedCode"/>, widened to admit the space and the
    /// punctuation seen in real trailing values (<c>1</c>, <c>2</c>, <c>23</c>, <c>IA</c>, and
    /// review/22's <c>(e)</c>). Also observed rather than specified.
    /// </remarks>
    internal static bool IsAdmittedValue(string? value)
    {
        if (value is not { Length: >= 1 and <= 128 })
        {
            return false;
        }

        foreach (var character in value)
        {
            // Space is admitted inside a value because a value may be more than one word; brace
            // is not, because a brace is this grammar's own token delimiter.
            if (character is (< ' ' or > '~') or '{' or '}')
            {
                return false;
            }
        }

        return value[0] != ' ' && value[^1] != ' ';
    }

    /// <summary>
    /// Renders an offending input for a refusal message without letting a control character or an
    /// unbounded string ride into it.
    /// </summary>
    internal static string Describe(string? value)
    {
        if (value is null)
        {
            return "(null)";
        }

        var clipped = value.Length > 96 ? string.Concat(value.AsSpan(0, 96), "...") : value;
        return string.Create(
            clipped.Length,
            clipped,
            static (destination, source) =>
            {
                for (var index = 0; index < source.Length; index++)
                {
                    var character = source[index];
                    destination[index] = character is >= ' ' and <= '~' ? character : '?';
                }
            });
    }
}

/// <summary>
/// A whole <c>reference_to_modified_location</c> value: its verbatim text and the ordered tokens
/// it decomposes into.
/// </summary>
/// <remarks>
/// <para>
/// Order is the publisher's and is preserved. A location reads outside in, so
/// <c>{AR|...} 23 {PTA|...}</c> is article 23's point, and the same two tokens reversed is a
/// different place in a different act.
/// </para>
/// <para>
/// <b>What the retained fixture proves, and what it does not.</b> All five retained values carry
/// exactly one token. A multi-token value is observed only in the canary event
/// <c>lex-event-20260904T175313280Z-e99a2a04ab2e44fb8bc5a5aa66d14451</c>, which quotes
/// <c>{AR|...fd_370/AR} 23 {PTA|...}</c> and abbreviates the second token's IRI with an ellipsis.
/// So the sequence is modelled as ordered and multi-token because a multi-token value is real,
/// while no retained bytes in this slice exercise more than one token from a complete quotation.
/// The tests say which of the two each fixture is.
/// </para>
/// </remarks>
public sealed class EuStructuralLocation
{
    private EuStructuralLocation(string rawValue, IReadOnlyList<EuAuthorityQualifiedToken> tokens)
    {
        RawValue = rawValue;
        Tokens = tokens;
    }

    /// <summary>The publisher's whole value, byte for byte, including its separating spaces.</summary>
    public string RawValue { get; }

    /// <summary>The tokens in the publisher's own order. Never empty.</summary>
    public IReadOnlyList<EuAuthorityQualifiedToken> Tokens { get; }

    /// <summary>
    /// Parses an authority-qualified token sequence against a caller-named pinned authority list.
    /// </summary>
    /// <remarks>
    /// The verbatim input is retained on <see cref="RawValue"/>, so the one thing this parse
    /// discards, the run of spaces separating a token from the value after it, is still readable
    /// from the whole value. Nothing else is normalised: codes, IRIs and values are sliced out and
    /// stored exactly as they appear.
    /// </remarks>
    public static EuStructuralLocation Parse(string rawValue, string expectedAuthorityListUri)
    {
        ArgumentNullException.ThrowIfNull(rawValue);
        ArgumentNullException.ThrowIfNull(expectedAuthorityListUri);

        var tokens = new List<EuAuthorityQualifiedToken>();
        var index = 0;
        while (index < rawValue.Length)
        {
            if (rawValue[index] == ' ')
            {
                index++;
                continue;
            }

            if (rawValue[index] != '{')
            {
                throw new ArgumentException(
                    $"\"{EuAuthorityQualifiedToken.Describe(rawValue)}\" is not a location value: "
                        + $"a token must open with a brace at offset {index}.",
                    nameof(rawValue));
            }

            var close = rawValue.IndexOf('}', index + 1);
            if (close < 0)
            {
                throw new ArgumentException(
                    $"\"{EuAuthorityQualifiedToken.Describe(rawValue)}\" is not a location value: "
                        + $"the token opening at offset {index} is never closed.",
                    nameof(rawValue));
            }

            var body = rawValue[(index + 1)..close];
            var bar = body.IndexOf('|', StringComparison.Ordinal);
            if (bar < 0)
            {
                throw new ArgumentException(
                    $"\"{EuAuthorityQualifiedToken.Describe(rawValue)}\" is not a location value: "
                        + $"the token at offset {index} carries no authority IRI, so its code "
                        + "cannot be resolved against any list.",
                    nameof(rawValue));
            }

            var code = body[..bar];
            var authorityUri = body[(bar + 1)..];

            // The value runs from the closing brace to the next token or the end of the value.
            var next = rawValue.IndexOf('{', close + 1);
            var valueEnd = next < 0 ? rawValue.Length : next;
            var value = rawValue[(close + 1)..valueEnd].Trim(' ');

            tokens.Add(EuAuthorityQualifiedToken.Create(
                code,
                authorityUri,
                value.Length == 0 ? null : value,
                expectedAuthorityListUri));

            index = valueEnd;
        }

        if (tokens.Count == 0)
        {
            throw new ArgumentException(
                "A location value must carry at least one authority-qualified token.",
                nameof(rawValue));
        }

        return new EuStructuralLocation(rawValue, new ReadOnlyCollection<EuAuthorityQualifiedToken>(tokens));
    }
}
