using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lex.V3.Api;

/// <summary>
/// What the site says to search engines (the specification of record's R67; the plan's item 3.6), as the live server says
/// it (<c>web/scripts/search-engines.mjs</c>; a test holds the two to one table,
/// <c>tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json</c>, its <c>searchEngines</c> member).
/// </summary>
/// <remarks>
/// <para>
/// One release-level switch decides it, <c>LEX_V3_SEARCH_ENGINES</c>, read when the server starts: <c>indexed</c> opens the
/// site, and anything else, its absence included, keeps it out, the default while the owner has not answered O27 (the plan:
/// "If unanswered: the pages open but are kept out of search engines"). Kept out, <c>/robots.txt</c> disallows everything,
/// every response of this root carries <c>X-Robots-Tag: noindex</c>, every page names a robots <c>noindex</c>, and
/// <c>/sitemap.xml</c> is an address this root does not hold. Indexed, robots.txt allows the pages and disallows the API, MCP,
/// the health routes, search and export, and names the sitemap; a page whose kept-out marker this root may remove
/// (<see cref="IndexablePage"/>) is served without it and without the header, and a redirect or a move without the header (it
/// is no page: the page it leads to says what it says).
/// </para>
/// <para>
/// Names (R92, the owner's O28): a law's page is opened only for a work the release's indexing record lists
/// (<see cref="ReadIndexingRecord"/>), the outcome of R92's measurement of which classes of acts name private individuals
/// (the plan's item 1.11). No release carries one yet: with the switch on, no law's page is opened and the sitemap lists none.
/// </para>
/// </remarks>
internal sealed partial class V3WebRoot
{
    /// <summary>The release-level switch's variable.</summary>
    internal const string SearchEnginesVariable = "LEX_V3_SEARCH_ENGINES";

    /// <summary>The switch's one value that opens the site to search engines.</summary>
    internal const string Indexed = "indexed";

    /// <summary>The switch's state for any other value, its absence included.</summary>
    internal const string KeptOut = "kept-out";

    /// <summary>The robots meta element of a page no search engine is ever asked to index, as the pages' renderer writes it.</summary>
    internal const string NeverIndexedMeta = "<meta name=\"robots\" content=\"noindex\"/>";

    /// <summary>The robots meta element of a page the switch opens, which this root removes when it may (<see cref="ServedPage"/>).</summary>
    internal const string KeptOutMeta = "<meta name=\"robots\" content=\"noindex\" data-search-engines=\"kept-out\"/>";

    /// <summary>The header every response of this root carries but a page the switch opened.</summary>
    internal const string XRobotsTag = "noindex";

    internal const string RobotsPath = "/robots.txt";

    internal const string SitemapPath = "/sitemap.xml";

    internal const string RobotsContentType = "text/plain; charset=utf-8";

    internal const string SitemapContentType = "application/xml; charset=utf-8";

    /// <summary>The file the API reads the release's indexing record from, beside its corpus mount.</summary>
    internal const string IndexingRecordFile = "indexing-record.json";

    internal const string IndexingRecordSchema = "lex-v3-indexing-record/1";

    /// <summary>A sitemap file holds at most this many addresses; a record that would list more is refused.</summary>
    internal const int SitemapMaximumUrls = 50_000;

    /// <summary>The site's origin, which robots.txt and the sitemap name whole.</summary>
    internal const string SiteOrigin = "https://law.soufien.lu";

    /// <summary>robots.txt with the site kept out: every address, for every crawler.</summary>
    internal const string RobotsKeptOut = "User-agent: *\nDisallow: /\n";

    /// <summary>The pages the switch opens that stand at no law's address, under each language: its home page and Trust and Coverage.</summary>
    private static readonly IReadOnlyList<string> OpenedPages = ["", "coverage.html"];

    /// <summary>
    /// robots.txt with the site indexed (R67: "robots.txt allows dossiers and reading pages, and disallows /api, /mcp, search
    /// results and exports"): the API, the MCP endpoint, the health routes, and each language's search and export pages. A
    /// property, not a field: a field's initializer here could run before <see cref="InterfaceLanguages"/>'s, in another file.
    /// </summary>
    internal static string RobotsIndexed => string.Join(
        "\n",
        new[] { "User-agent: *", "Disallow: /api/", "Disallow: /mcp", "Disallow: /health/" }
            .Concat(InterfaceLanguages.SelectMany(static language => new[] { $"Disallow: /{language}/search.html", $"Disallow: /{language}/export.html" }))
            .Concat(["", $"Sitemap: {SiteOrigin}{SitemapPath}", ""]));

    /// <summary>The switch's state for a value of its variable: <c>indexed</c> only when it says so exactly.</summary>
    internal static string SearchEnginesState(string? value) => string.Equals(value, Indexed, StringComparison.Ordinal) ? Indexed : KeptOut;

    /// <summary>robots.txt in a state of the switch.</summary>
    internal static string RobotsTxt(string state) => string.Equals(state, Indexed, StringComparison.Ordinal) ? RobotsIndexed : RobotsKeptOut;

    /// <summary>The release's indexing record: the build of the index it read, and each work it lists with its text languages.</summary>
    internal sealed record IndexingRecord(string? BuiltAt, IReadOnlyList<IndexedWork> Works);

    /// <summary>A work R92's measurement found to name no private individual: its dossier's address and its readable addresses' languages.</summary>
    internal sealed record IndexedWork(string Address, IReadOnlyList<string> Languages);

    private static readonly Regex TextLanguage = new("^[a-z]{3}\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex UtcInstant = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// The release's indexing record read from its JSON text, as <c>readIndexingRecord</c> in
    /// <c>web/scripts/search-engines.mjs</c> reads it, or null when it is none (nothing is then opened): the schema
    /// <c>lex-v3-indexing-record/1</c>; <c>built_at</c> an instant in UTC (<c>yyyy-MM-ddTHH:mm:ssZ</c>) or null; <c>works</c>,
    /// each a dossier's address with no interface language (never a consolidated version's CELEX) and at least one text
    /// language, in order and each once, the works in their addresses' order, each once; and no more addresses in the sitemap
    /// than a sitemap file holds.
    /// </summary>
    internal static IndexingRecord? ReadIndexingRecord(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            // A byte-order mark is no part of the record, as the live server's reader drops it.
            using var document = JsonDocument.Parse(text.StartsWith('\uFEFF') ? text[1..] : text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.String ||
                !string.Equals(schema.GetString(), IndexingRecordSchema, StringComparison.Ordinal))
            {
                return null;
            }

            string? builtAt = null;
            if (root.TryGetProperty("built_at", out var built) && built.ValueKind != JsonValueKind.Null)
            {
                if (built.ValueKind != JsonValueKind.String || !IsUtcInstant(built.GetString()!))
                {
                    return null;
                }

                builtAt = built.GetString();
            }

            if (!root.TryGetProperty("works", out var listed) || listed.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var works = new List<IndexedWork>();
            foreach (var work in listed.EnumerateArray())
            {
                if (work.ValueKind != JsonValueKind.Object || !work.TryGetProperty("address", out var addressValue) || addressValue.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var address = addressValue.GetString()!;
                var match = SiteMatch(address);
                if (match is null || match.Groups["locale"].Success || match.Groups["pin"].Success || match.Groups["current"].Success ||
                    (match.Groups["celex"].Success && ConsolidatedCelex.IsMatch(match.Groups["celex"].Value)) ||
                    (works.Count > 0 && string.CompareOrdinal(works[^1].Address, address) >= 0))
                {
                    return null;
                }

                if (!work.TryGetProperty("languages", out var languagesValue) || languagesValue.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var languages = new List<string>();
                foreach (var language in languagesValue.EnumerateArray())
                {
                    if (language.ValueKind != JsonValueKind.String || !TextLanguage.IsMatch(language.GetString()!) ||
                        (languages.Count > 0 && string.CompareOrdinal(languages[^1], language.GetString()) >= 0))
                    {
                        return null;
                    }

                    languages.Add(language.GetString()!);
                }

                if (languages.Count == 0)
                {
                    return null;
                }

                works.Add(new IndexedWork(address, languages.AsReadOnly()));
            }

            var urls = InterfaceLanguages.Count * (OpenedPages.Count + works.Sum(static work => 1 + work.Languages.Count));
            return urls > SitemapMaximumUrls ? null : new IndexingRecord(builtAt, works.AsReadOnly());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The release's indexing record in <paramref name="file"/>, or null when there is none or it is none.</summary>
    internal static IndexingRecord? ReadIndexingRecordFile(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        try
        {
            return File.Exists(file) ? ReadIndexingRecord(File.ReadAllText(file, Encoding.UTF8)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A real instant in UTC, written <c>yyyy-MM-ddTHH:mm:ssZ</c>, as <c>isUtcInstant</c> in <c>web/scripts/temporal.mjs</c> says it.</summary>
    private static bool IsUtcInstant(string value) =>
        UtcInstant.IsMatch(value) &&
        DateTime.TryParseExact(value, "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _);

    /// <summary>A law's address's work, as the indexing record names it (<c>/lu-legilux/{work}</c>, <c>/eu-eurlex/{celex}</c>), or null.</summary>
    internal static string? WorkAddressOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var match = SiteMatch(path);
        if (match is null)
        {
            return null;
        }

        return match.Groups["work"].Success ? $"/lu-legilux/{match.Groups["work"].Value}" : $"/eu-eurlex/{match.Groups["celex"].Value}";
    }

    /// <summary>
    /// Whether a page at <paramref name="path"/> may be served without its kept-out marker, as <c>indexablePage</c> says it: with
    /// the switch on, a page at no law's address, and a law's page whose work the indexing record lists (R92).
    /// </summary>
    internal static bool IndexablePage(string state, string path, IndexingRecord? record)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!string.Equals(state, Indexed, StringComparison.Ordinal))
        {
            return false;
        }

        if (SiteMatch(path) is null)
        {
            return true;
        }

        var work = WorkAddressOf(path);
        return record is not null && record.Works.Any(listed => string.Equals(listed.Address, work, StringComparison.Ordinal));
    }

    /// <summary>
    /// A page as served in the switch's state, as <c>servedPage</c> says it: the kept-out marker removed once when the page
    /// may be indexed (<see cref="IndexablePage"/>) and carries it, and whether it was (<c>Opened</c>); else the page as it is.
    /// </summary>
    internal static (string Html, bool Opened) ServedPage(string html, string state, string path, IndexingRecord? record)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (!IndexablePage(state, path, record))
        {
            return (html, false);
        }

        var at = html.IndexOf(KeptOutMeta, StringComparison.Ordinal);
        return at < 0 ? (html, false) : (string.Concat(html.AsSpan(0, at), html.AsSpan(at + KeptOutMeta.Length)), true);
    }

    private static string EscapeXml(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '&' or '<' or '>' or '"' or '\'')
            {
                builder.Append("&#").Append(((int)character).ToString(CultureInfo.InvariantCulture)).Append(';');
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>One page's sitemap entries: the page in each interface language, each with every language's alternate and <paramref name="xDefault"/>.</summary>
    private static IEnumerable<string> SitemapEntries(Func<string, string> addressIn, string xDefault, string? lastmod)
    {
        var links = string.Concat(InterfaceLanguages.Select(language => (language, $"{SiteOrigin}{addressIn(language)}"))
            .Append(("x-default", $"{SiteOrigin}{xDefault}"))
            .Select(static link => $"<xhtml:link rel=\"alternate\" hreflang=\"{link.Item1}\" href=\"{EscapeXml(link.Item2)}\"/>"));
        return InterfaceLanguages.Select(language =>
            $"<url><loc>{EscapeXml($"{SiteOrigin}{addressIn(language)}")}</loc>{(lastmod is null ? string.Empty : $"<lastmod>{lastmod}</lastmod>")}{links}</url>");
    }

    /// <summary>
    /// The sitemap with the site indexed, as <c>sitemapXml</c> writes it (R67: "A sitemap lists dossiers and current reading
    /// pages, with lastmod from the build or acquisition time, never valid_from"): each language's home page and Trust and
    /// Coverage, then each listed work's dossier and its readable address in each listed text language, never a permalink;
    /// <c>lastmod</c> the record's <c>built_at</c>, left out with none.
    /// </summary>
    internal static string SitemapXml(IndexingRecord? record)
    {
        var lastmod = record?.BuiltAt;
        var lines = new List<string>
        {
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">",
        };
        lines.AddRange(SitemapEntries(static language => $"/{language}/", "/", lastmod));
        lines.AddRange(SitemapEntries(static language => $"/{language}/coverage.html", $"/{InterfaceLanguages[0]}/coverage.html", lastmod));
        foreach (var work in record?.Works ?? [])
        {
            lines.AddRange(SitemapEntries(language => $"/{language}{work.Address}", work.Address, lastmod));
            foreach (var language in work.Languages)
            {
                var act = $"{work.Address}/{language}";
                lines.AddRange(SitemapEntries(locale => $"/{locale}{act}", act, lastmod));
            }
        }

        lines.Add("</urlset>");
        lines.Add(string.Empty);
        return string.Join("\n", lines);
    }
}
