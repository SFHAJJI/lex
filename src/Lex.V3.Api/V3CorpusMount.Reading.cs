using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Platform;

namespace Lex.V3.Api;

/// <summary>
/// Reading by article and by page (the plan's item 3.7; the specification of record's R73 and R107): <c>evidence_bundle</c>
/// answers a version (a Luxembourg state or an EU wording) one bounded page at a time, with the index of its pages and, when
/// asked, its table of contents, so a reading of a code's 3,000 articles is never one answer of every article's text.
/// </summary>
/// <remarks>
/// <para>
/// A version is read in its order (Luxembourg: <see cref="CompareLuxembourgArticleIds"/>; the EU: the order of the
/// publisher's provision ids, which the wording digest reads them in) and cut into pages: a page holds at most
/// <see cref="ReadingPageArticles"/> articles and at most <see cref="ReadingPageBytes"/> bytes of quoted text and notes
/// (UTF-8), and an article whose text alone passes that bound is a page of its own. The cut depends on the version alone, so
/// a page's number names the same articles on every asking. With neither <c>page</c> nor <c>article</c> asked the first
/// page is served, so a version that fits one page is answered whole, as before; <c>article</c> serves the page that holds
/// it, <c>page</c> that page, and <c>contents</c> adds every article's position, publisher id, heading and page, no text.
/// </para>
/// <para>
/// Rights are enforced over the whole version before any page is cut, and a version none of whose articles holds text is
/// refused <c>text_not_available</c> as before. An article a served version does not hold is <c>anchor_not_in_version</c>,
/// with the nearest ids it holds; a page past a version's last, or a page and an article asked together, is a malformed
/// request, as a cursor that names nothing is.
/// </para>
/// </remarks>
internal sealed partial class V3CorpusMount
{
    /// <summary>The most articles one page holds.</summary>
    internal const int ReadingPageArticles = 50;

    /// <summary>The most bytes of quoted text and notes (UTF-8) one page holds, unless one article alone passes it.</summary>
    internal const int ReadingPageBytes = 98_304;

    /// <summary>The greatest page number a request may name (the request schema's maximum).</summary>
    internal const int MaximumReadingPage = 100_000;

    internal const string LuxembourgReadingPageRule =
        "a state is read in pages: its articles, quoted or held without text, are taken in the order of their publisher ids (the publisher's " +
        "document order is not held: the numbers in an id compare as numbers, the Latin adverbs bis, ter, quater and the rest in their order, an " +
        "article id that is a roman numeral by its value, then the article identity), and each page holds at most 50 of them and at most 98,304 " +
        "bytes of quoted text and notes (UTF-8), an article whose text alone passes that bound being a page of its own; pages count from 1 and the " +
        "cut depends on the state alone; with neither page nor article asked the first page is served, article serves the page that holds that " +
        "publisher id (anchor_not_in_version when a served state does not hold it), and contents true adds every article's position, publisher " +
        "id, identity and page, with no text; articles, articles_without_text and validity_conflict_count are the page's, and page counts the state's";

    internal const string EuropeReadingPageRule =
        "a wording is read in pages: its provisions, quoted or held without text, are taken in the order of the publisher's provision ids (the " +
        "order the wording digest reads them in), and each page holds at most 50 of them and at most 98,304 bytes of quoted text (UTF-8), a " +
        "provision whose text alone passes that bound being a page of its own; pages count from 1 and the cut depends on the wording alone; with " +
        "neither page nor article asked the first page is served, article serves the page that holds that provision id (anchor_not_in_version " +
        "when a served wording does not hold it), and contents true adds every provision's position, publisher id, identity, heading and page, with no " +
        "text; articles and articles_without_text are the page's, and page counts the wording's";

    /// <summary>What a reading asks of the pages: one page by its number or by an article it holds, and whether the contents.</summary>
    internal sealed record ReadingPageAsked(int? Page, string? Article, bool Contents);

    /// <summary>One article of a version, in the version's order, as its pages are cut: its id, heading, whether quoted, weight.</summary>
    internal sealed record ReadingEntry(string PublisherId, string? Heading, bool Quoted, long Weight, string ArticleIdentitySha256 = "");

    /// <summary>
    /// A version cut into pages: the page each position (from 0) is on (from 1), the pages' first and last positions, and the
    /// page served for the request with its positions.
    /// </summary>
    internal sealed record ReadingPages(
        IReadOnlyList<ReadingEntry> Entries,
        int[] PageOf,
        IReadOnlyList<(int First, int Last)> Bounds,
        int Served)
    {
        internal int Count => Bounds.Count;

        internal (int First, int Last) ServedBounds => Bounds[Served - 1];

        internal bool Holds(int position) => position >= ServedBounds.First && position <= ServedBounds.Last;
    }

    /// <summary>
    /// The reading's page parameters, checked as the request schema checks them (for a caller that reaches the mount without
    /// it): <c>page</c> a whole number from 1 to <see cref="MaximumReadingPage"/>, <c>article</c> a non-blank string, never
    /// both, and <c>contents</c> true or false.
    /// </summary>
    private static ReadingPageAsked ReadingPageAskedOf(JsonElement parameters)
    {
        int? page = null;
        if (parameters.TryGetProperty("page", out var pageValue))
        {
            if (pageValue.ValueKind != JsonValueKind.Number || !pageValue.TryGetInt32(out var number) || number < 1 || number > MaximumReadingPage)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'page' is not a whole page number from 1.");
            }

            page = number;
        }

        string? article = null;
        if (parameters.TryGetProperty("article", out _))
        {
            article = RequiredString(parameters, "article");
        }

        if (page is not null && article is not null)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "A reading asks one page: by its number or by an article it holds, not both.");
        }

        var contents = false;
        if (parameters.TryGetProperty("contents", out var contentsValue))
        {
            if (contentsValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'contents' is not true or false.");
            }

            contents = contentsValue.GetBoolean();
        }

        return new ReadingPageAsked(page, article, contents);
    }

    /// <summary>
    /// A version's entries cut into pages (<see cref="ReadingPageArticles"/>, <see cref="ReadingPageBytes"/>), and the page
    /// served for <paramref name="asked"/>: its number, the page that holds its article, or the first; null with the
    /// article's absence when the version does not hold the article asked. A page past the last is a malformed request.
    /// </summary>
    internal static ReadingPages? CutReadingPages(IReadOnlyList<ReadingEntry> entries, ReadingPageAsked asked, string versionName)
    {
        var pageOf = new int[entries.Count];
        var bounds = new List<(int First, int Last)>();
        var count = 0;
        long bytes = 0;
        for (var position = 0; position < entries.Count; position++)
        {
            var weight = entries[position].Weight;
            if (count > 0 && (count == ReadingPageArticles || bytes + weight > ReadingPageBytes))
            {
                bounds[^1] = (bounds[^1].First, position - 1);
                count = 0;
                bytes = 0;
            }

            if (count == 0)
            {
                bounds.Add((position, position));
            }

            count++;
            bytes += weight;
            pageOf[position] = bounds.Count;
        }

        if (bounds.Count > 0)
        {
            bounds[^1] = (bounds[^1].First, entries.Count - 1);
        }

        var served = 1;
        if (asked.Page is { } page)
        {
            if (page > bounds.Count)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    $"The operation request's 'page' is {page.ToString(CultureInfo.InvariantCulture)}, and {versionName} has " +
                    $"{bounds.Count.ToString(CultureInfo.InvariantCulture)} page{(bounds.Count == 1 ? string.Empty : "s")}.");
            }

            served = page;
        }
        else if (asked.Article is { } article)
        {
            var at = -1;
            for (var position = 0; position < entries.Count && at < 0; position++)
            {
                if (string.Equals(entries[position].PublisherId, article, StringComparison.Ordinal))
                {
                    at = position;
                }
            }

            if (at < 0)
            {
                return null;
            }

            served = pageOf[at];
        }

        return new ReadingPages(entries, pageOf, bounds, served);
    }

    /// <summary>
    /// What a page says of itself and of its version: its number, the version's pages, its first and last positions (from 1),
    /// and how many articles the version holds, quoted and without text (and, for a Luxembourg state, with a date of their own
    /// differing from the state's).
    /// </summary>
    private static object ReadingPageMember(ReadingPages pages, int? validityConflictsInVersion)
    {
        var (first, last) = pages.ServedBounds;
        var quoted = pages.Entries.Count(static entry => entry.Quoted);
        var page = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["number"] = pages.Served,
            ["pages"] = pages.Count,
            ["first_position"] = first + 1,
            ["last_position"] = last + 1,
            ["articles_in_version"] = pages.Entries.Count,
            ["quoted_in_version"] = quoted,
            ["without_text_in_version"] = pages.Entries.Count - quoted,
        };
        if (validityConflictsInVersion is { } conflicts)
        {
            page["validity_conflicts_in_version"] = conflicts;
        }

        return page;
    }

    /// <summary>The index of a version's pages: each page's number, its first and last article (and EU headings), and its count.</summary>
    private static object[] ReadingPagesMember(ReadingPages pages, bool headings) =>
        pages.Bounds.Select((bound, index) =>
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["number"] = index + 1,
                ["first_publisher_id"] = pages.Entries[bound.First].PublisherId,
                ["last_publisher_id"] = pages.Entries[bound.Last].PublisherId,
                ["articles"] = bound.Last - bound.First + 1,
            };
            if (headings)
            {
                row["first_heading"] = pages.Entries[bound.First].Heading;
                row["last_heading"] = pages.Entries[bound.Last].Heading;
            }

            return (object)row;
        }).ToArray();

    /// <summary>A version's table of contents: every article's position, publisher id, identity, (EU) heading, whether quoted, and page.</summary>
    private static object[] ReadingContentsMember(ReadingPages pages, bool headings) =>
        pages.Entries.Select((entry, index) =>
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["position"] = index + 1,
                ["publisher_id"] = entry.PublisherId,
                ["article_identity_sha256"] = entry.ArticleIdentitySha256,
            };
            if (headings)
            {
                row["heading"] = entry.Heading;
            }

            row["text_held"] = entry.Quoted;
            row["page"] = pages.PageOf[index];
            return (object)row;
        }).ToArray();

    /// <summary>
    /// The refusal of an article a served version does not hold: the article asked, the ids nearest it that the version holds
    /// (<see cref="NearestAnchors"/>), and the version.
    /// </summary>
    private V3PlatformOperationOutcome RefuseArticleNotInVersion(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        Lex.V3.Contracts.PublisherId publisher,
        string article,
        IReadOnlyList<string> held,
        string permalink,
        string language)
    {
        using var notInVersion = JsonSerializer.SerializeToDocument(new
        {
            requested_anchor = article,
            nearest_anchors = NearestAnchors(held, article),
            do_not_fall_back_to_full_text_search = true,
            permalink,
            language,
            what_would_answer = AnchorNotInVersionRoutes,
            asserts_absence_of_law = false,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, publisher),
            new V3PlatformOperationRefusal(request, "anchor_not_in_version", notInVersion.RootElement));
    }

    /// <summary>An EU wording's provisions as its pages are cut: each id, heading, whether quoted, and its text's weight.</summary>
    private static ReadingEntry[] EuropeReadingEntries(IReadOnlyList<Lex.V3.Ingest.Europe.EuropeIndexArticleText> articles) =>
        articles.Select(static article => new ReadingEntry(article.PublisherIdentifier, article.Heading, article.Text.Length > 0, ReadingWeight(article.Text), article.ArticleIdentitySha256)).ToArray();

    /// <summary>The bytes an article weighs on a page: its text and its notes' text (UTF-8); none for an article held without text.</summary>
    private static long ReadingWeight(string text, string? tokensJson = null)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        long weight = Encoding.UTF8.GetByteCount(text);
        if (tokensJson is not null)
        {
            foreach (var (_, noteText) in ArticleNoteParts(tokensJson))
            {
                weight += Encoding.UTF8.GetByteCount(noteText);
            }
        }

        return weight;
    }

    /// <summary>
    /// The order of a Luxembourg version's articles, which the index does not hold as the publisher's document order (every
    /// state lists its article identities sorted by digest): the publisher's ids compared part by part, digits as numbers,
    /// a Latin adverb (bis, ter, quater, ...) by its rank, an id that is <c>art_</c> or <c>article_</c> and a roman numeral by
    /// the numeral's value, any other letters as text, a separator before letters and digits, and an id before every id
    /// that extends it. Ties are broken by the caller (the article identity), so the order is total.
    /// </summary>
    internal static int CompareLuxembourgArticleIds(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var leftParts = ArticleIdParts(left);
        var rightParts = ArticleIdParts(right);
        for (var index = 0; index < Math.Min(leftParts.Count, rightParts.Count); index++)
        {
            var compared = CompareArticleIdPart(leftParts[index], rightParts[index]);
            if (compared != 0)
            {
                return compared;
            }
        }

        var byLength = leftParts.Count.CompareTo(rightParts.Count);
        return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
    }

    /// <summary>One part of an article id: a separator (0), a number (1) or letters (2), with its rank for numbers and adverbs.</summary>
    private readonly record struct ArticleIdPart(int Kind, string Text, long Rank);

    /// <summary>
    /// The letters a Luxembourg article id writes after a number, by their rank: <c>er</c> (1er, the first) as 1, then the
    /// Latin adverbs from 2 for bis.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, long> LatinAdverbs = new Dictionary<string, long>(StringComparer.Ordinal)
    {
        ["er"] = 1, ["bis"] = 2, ["ter"] = 3, ["quater"] = 4, ["quinquies"] = 5, ["sexies"] = 6, ["septies"] = 7, ["octies"] = 8,
        ["nonies"] = 9, ["novies"] = 9, ["decies"] = 10, ["undecies"] = 11, ["duodecies"] = 12, ["terdecies"] = 13,
        ["quaterdecies"] = 14, ["quindecies"] = 15, ["sexdecies"] = 16, ["septdecies"] = 17, ["octodecies"] = 18,
        ["novodecies"] = 19, ["vicies"] = 20,
    };

    private static List<ArticleIdPart> ArticleIdParts(string id)
    {
        // An id that is art_ or article_ and a roman numeral (art_iii, article_xiv) is read as the numeral's value.
        foreach (var prefix in new[] { "art_", "article_" })
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal) && RomanValue(id[prefix.Length..]) is { } value)
            {
                return [new(2, prefix[..^1], 0), new(0, "_", 0), new(1, id[prefix.Length..], value)];
            }
        }

        var parts = new List<ArticleIdPart>();
        var at = 0;
        while (at < id.Length)
        {
            var start = at;
            if (char.IsAsciiDigit(id[at]))
            {
                while (at < id.Length && char.IsAsciiDigit(id[at]))
                {
                    at++;
                }

                var digits = id[start..at].TrimStart('0');
                // A number longer than a long holds is ranked by its length, then as text.
                parts.Add(new(1, id[start..at], digits.Length > 18 ? long.MaxValue : digits.Length == 0 ? 0 : long.Parse(digits, CultureInfo.InvariantCulture)));
            }
            else if (char.IsLetter(id[at]))
            {
                while (at < id.Length && char.IsLetter(id[at]))
                {
                    at++;
                }

                var letters = id[start..at];
                parts.Add(new(2, letters, LatinAdverbs.TryGetValue(letters.ToLowerInvariant(), out var rank) ? rank : 0));
            }
            else
            {
                at++;
                parts.Add(new(0, id[start..at], 0));
            }
        }

        return parts;
    }

    private static int CompareArticleIdPart(ArticleIdPart left, ArticleIdPart right)
    {
        if (left.Kind != right.Kind)
        {
            return left.Kind.CompareTo(right.Kind);
        }

        if (left.Kind == 1)
        {
            var byValue = left.Rank.CompareTo(right.Rank);
            return byValue != 0 ? byValue : string.CompareOrdinal(left.Text, right.Text);
        }

        if (left.Kind == 2 && left.Rank > 0 && right.Rank > 0)
        {
            var byRank = left.Rank.CompareTo(right.Rank);
            return byRank != 0 ? byRank : string.CompareOrdinal(left.Text, right.Text);
        }

        if (left.Kind == 2 && (left.Rank > 0) != (right.Rank > 0))
        {
            // A Latin adverb follows the number it is inserted after and comes before any other letters there.
            return left.Rank > 0 ? -1 : 1;
        }

        return string.CompareOrdinal(left.Text, right.Text);
    }

    /// <summary>The value of a lower-case roman numeral written in its usual form (i to mmmcmxcix), or null.</summary>
    private static int? RomanValue(string text)
    {
        if (text.Length == 0 || text.Length > 15 || text.Any(static character => "ivxlcdm".IndexOf(character, StringComparison.Ordinal) < 0))
        {
            return null;
        }

        var value = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var current = RomanDigit(text[index]);
            var next = index + 1 < text.Length ? RomanDigit(text[index + 1]) : 0;
            value += current < next ? -current : current;
        }

        return value is > 0 and < 4000 && string.Equals(RomanText(value), text, StringComparison.Ordinal) ? value : null;
    }

    private static int RomanDigit(char character) => character switch
    {
        'i' => 1,
        'v' => 5,
        'x' => 10,
        'l' => 50,
        'c' => 100,
        'd' => 500,
        _ => 1000,
    };

    private static string RomanText(int value)
    {
        var text = new StringBuilder();
        foreach (var (amount, numeral) in new[]
                 {
                     (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"),
                     (9, "ix"), (5, "v"), (4, "iv"), (1, "i"),
                 })
        {
            while (value >= amount)
            {
                text.Append(numeral);
                value -= amount;
            }
        }

        return text.ToString();
    }
}
