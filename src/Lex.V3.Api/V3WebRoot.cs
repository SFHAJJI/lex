using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Lex.V3.Api;

/// <summary>
/// The live web pages, served by the API on its own origin (the owner's ruling of 2026-09-30, Decision
/// 95: one server delivers the web bundle and the API, so the pages' <c>connect-src 'self'</c> and the
/// API's absence of CORS stand). The directory is the built live pages (<c>web/dist-live</c>), placed
/// beside the API as <c>v3-web</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only the files the directory holds when the API starts are served, each by its exact path, by GET
/// or HEAD; an interface language's root (<c>/en/</c>, <c>/fr/</c>) is its <c>index.html</c>. Every page
/// stands under its interface language (the specification of record's R61); the root, and an address for
/// law the API mints with no language, are answered with a redirect to the reader's language
/// (<see cref="RedirectFor"/>), and an EU permalink of the shape before R64 with a permanent one to R64's
/// (<see cref="MovedFor"/>). A path the directory does not hold is not claimed, so it reaches the API's
/// routing as before. The set is read once: a file added later is not served, and nothing outside the
/// directory can be named, because a request is matched against the recorded set, never joined onto the
/// directory.
/// </para>
/// <para>
/// Every response carries the headers this server owes a page (ruling 3): the page's own reviewed
/// Content-Security-Policy, read from the English home page (<c>en/index.html</c>) so there is one source
/// of it, with <c>frame-ancestors 'none'</c> added, which only a header can carry;
/// <c>Strict-Transport-Security</c>; <c>Referrer-Policy: no-referrer</c>;
/// <c>X-Content-Type-Options: nosniff</c>; and <c>Cache-Control: no-store</c>, so a page never outlives the
/// corpus it was answered from. A redirect carries them too, and <c>Vary: Accept-Language</c>. So does
/// <c>X-Robots-Tag: noindex</c>, but on a page the release's switch opened (R67, <c>V3WebRoot.SearchEngines.cs</c>); this
/// root also answers <c>/robots.txt</c>, and <c>/sitemap.xml</c> with the site indexed.
/// </para>
/// <para>
/// A law's address under an interface language, and each language's home page (from the coverage answer), are rendered
/// from the API's own answer when the server can (R67: complete HTML, readable without JavaScript): the directory's
/// <c>ssr/</c> holds the pages' server renderer
/// (<see cref="V3PageRenderer"/>), which is run, never served. With no corpus mounted the page renders the refusal every
/// request gets. When there is no answer (a request the API would refuse as malformed) or the render
/// fails, the page is served as built and asks for itself.
/// </para>
/// </remarks>
internal sealed partial class V3WebRoot
{
    internal const string DirectoryName = "v3-web";

    internal const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    internal const string FrameAncestors = "frame-ancestors 'none'";

    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".woff2"] = "font/woff2",
        [".json"] = "application/json; charset=utf-8",
    };

    private readonly IReadOnlyDictionary<string, string> _files;
    private readonly V3PageRenderer? _renderer;
    private readonly IReadOnlyDictionary<string, string> _notFoundPages;
    private readonly IndexingRecord? _indexingRecord;
    private readonly byte[] _robots;
    private readonly IReadOnlyDictionary<string, byte[]>? _sitemap;

    private V3WebRoot(
        IReadOnlyDictionary<string, string> files,
        string contentSecurityPolicy,
        V3PageRenderer? renderer,
        IReadOnlyDictionary<string, string> notFoundPages,
        string searchEngines,
        IndexingRecord? indexingRecord,
        string? lastmod)
    {
        _files = files;
        ContentSecurityPolicy = contentSecurityPolicy;
        _renderer = renderer;
        _notFoundPages = notFoundPages;
        SearchEngines = searchEngines;
        _indexingRecord = indexingRecord;
        // robots.txt and, with the site indexed, the sitemap's files, built once for the root's life (review of #976, F4).
        _robots = Encoding.UTF8.GetBytes(RobotsTxt(searchEngines));
        _sitemap = string.Equals(searchEngines, Indexed, StringComparison.Ordinal)
            ? SitemapFiles(indexingRecord, lastmod).ToDictionary(static file => file.Key, static file => Encoding.UTF8.GetBytes(file.Value), StringComparer.Ordinal)
            : null;
    }

    /// <summary>The release's switch, as this root read it when it was opened: <see cref="Indexed"/> or <see cref="KeptOut"/>.</summary>
    internal string SearchEngines { get; }

    /// <summary>Whether this root holds an indexing record (R92's outcome), which opens the pages of the works it lists.</summary>
    internal bool HoldsIndexingRecord => _indexingRecord is not null;

    /// <summary>Whether the directory held the pages' server renderer.</summary>
    internal bool RendersPages => _renderer is not null;

    /// <summary>The Content-Security-Policy header every response carries: the page's own, and frame-ancestors.</summary>
    internal string ContentSecurityPolicy { get; }

    /// <summary>The paths served, as a request names them.</summary>
    internal IReadOnlyCollection<string> Paths => (IReadOnlyCollection<string>)_files.Keys;

    /// <summary>
    /// The pages in <paramref name="directory"/>, or null when there is none to serve (no directory, or no
    /// English home page, <c>en/index.html</c>). A directory whose home page carries no Content-Security-Policy,
    /// or whose files include one of a type this server does not serve, is refused: a page is never served
    /// without its reviewed policy, and a file is never served as a guessed type. <paramref name="searchEngines"/> is the
    /// release's switch, the value of <c>LEX_V3_SEARCH_ENGINES</c> (<see cref="SearchEnginesState"/>: kept out unless it is
    /// <c>indexed</c>), <paramref name="indexingRecord"/> the release's indexing record with the works the mount holds
    /// (<see cref="SearchEngineFacts"/>), or null (no law's page opened), and <paramref name="lastmod"/> the mounted index's
    /// build time, which the sitemap dates its entries by, or null.
    /// </summary>
    internal static V3WebRoot? Open(string directory, string? searchEngines = null, IndexingRecord? indexingRecord = null, string? lastmod = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var index = Path.Combine(directory, "en", "index.html");
        if (!File.Exists(index))
        {
            return null;
        }

        var root = Path.GetFullPath(directory);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var notFound = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!SafePath.IsMatch(relative))
            {
                throw new InvalidDataException($"The web directory holds '{relative}', a path this server does not serve.");
            }

            if (!ContentTypes.ContainsKey(Path.GetExtension(relative)))
            {
                throw new InvalidDataException($"The web directory holds '{relative}', of a type this server does not serve.");
            }

            // The pages' server renderer is run (V3PageRenderer), never served; the 404 pages answer a page request nothing
            // claims, and the maintenance pages are the deploy kit's: none is served at its own address (V3WebRoot.NotFound.cs).
            if (relative.StartsWith("ssr/", StringComparison.Ordinal) || IsUnservedPage(relative, file, notFound))
            {
                continue;
            }

            files["/" + relative] = file;
        }

        foreach (var language in InterfaceLanguages)
        {
            if (files.TryGetValue($"/{language}/index.html", out var home))
            {
                files[$"/{language}/"] = home;
            }
        }

        var policy = PolicyMeta.Match(File.ReadAllText(index));
        if (!policy.Success || string.IsNullOrWhiteSpace(policy.Groups["policy"].Value))
        {
            throw new InvalidDataException("The web directory's en/index.html carries no Content-Security-Policy, so no page is served.");
        }

        // The attribute is HTML: the page's renderer writes each quote of 'self' as an entity.
        return new V3WebRoot(
            files,
            $"{System.Net.WebUtility.HtmlDecode(policy.Groups["policy"].Value)}; {FrameAncestors}",
            V3PageRenderer.Open(Path.Combine(root, "ssr")),
            notFound,
            SearchEnginesState(searchEngines),
            indexingRecord,
            lastmod);
    }

    /// <summary>
    /// Serves the file <paramref name="rawTarget"/> names, if this root holds it, or, for a law's address under an
    /// interface language (<see cref="PageForSitePath"/>), the page that reads it; answers the root and a law's address
    /// with no language with a redirect to the reader's language (<see cref="RedirectFor"/>); and returns true. Returns
    /// false, touching nothing, for any other path. The query string is not part of the path; a redirect keeps it.
    /// </summary>
    internal async Task<bool> TryServeAsync(
        HttpContext context,
        string rawTarget,
        CancellationToken cancellationToken,
        Func<V3SitePage, CancellationToken, byte[]?>? answer = null,
        Func<string?>? today = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var queryAt = rawTarget.IndexOf('?', StringComparison.Ordinal);
        var path = queryAt < 0 ? rawTarget : rawTarget[..queryAt];
        var response = context.Response;
        var method = context.Request.Method;

        // robots.txt in the switch's state, and the sitemap's index and parts only with the site indexed (R67); kept out, or a
        // part the sitemap has not, is an address this root does not hold.
        byte[]? sitemap = null;
        if (string.Equals(path, RobotsPath, StringComparison.Ordinal) || (_sitemap is not null && _sitemap.TryGetValue(path, out sitemap)))
        {
            if (WritePageHeaders(response, method))
            {
                var text = sitemap ?? _robots;
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = sitemap is null ? RobotsContentType : SitemapContentType;
                response.ContentLength = text.Length;
                if (HttpMethods.IsGet(method))
                {
                    await response.Body.WriteAsync(text, cancellationToken).ConfigureAwait(false);
                }
            }

            return true;
        }

        if (!_files.TryGetValue(path, out var file))
        {
            // A law's address (a permalink or a work's coordinate) under an interface language is served the page that
            // reads it; the page reads its own address (web/scripts/permalinks.mjs). With no language, it and the root are
            // sent to the reader's. Nothing else is claimed.
            if (PageForSitePath(path) is not { } page || !_files.TryGetValue(page, out file))
            {
                var query = queryAt < 0 ? string.Empty : rawTarget[queryAt..];

                // A permalink of the shape before R64 whose address under R64 is computed from it alone has moved there
                // for good, under the interface language it had (MovedFor). The answer is the same for every reader.
                if (MovedFor(path, query) is { } moved && _files.ContainsKey(moved.Page))
                {
                    if (WritePageHeaders(response, method))
                    {
                        ForSearchEnginesRedirect(response);
                        response.Headers.Location = moved.Location;
                        response.StatusCode = StatusCodes.Status301MovedPermanently;
                        response.ContentLength = 0;
                    }

                    return true;
                }

                if (RedirectFor(path, query, context.Request.Headers.AcceptLanguage.ToString()) is not { } redirect ||
                    !_files.ContainsKey(redirect.Language))
                {
                    return false;
                }

                if (WritePageHeaders(response, method))
                {
                    ForSearchEnginesRedirect(response);
                    // The answer depends on the reader's languages, so a cache must not hand it to another reader.
                    response.Headers.Vary = "Accept-Language";
                    response.Headers.Location = redirect.Location;
                    response.StatusCode = StatusCodes.Status302Found;
                    response.ContentLength = 0;
                }

                return true;
            }
        }

        if (!WritePageHeaders(response, method))
        {
            return true;
        }

        // A law's address under a language, rendered from the API's own answer when there is one (R67). A readable address asks
        // for today in Luxembourg (R13), which the caller's clock says; with no clock its page is served as built.
        if (answer is not null && _renderer is not null && SitePageFor(path, today?.Invoke(), queryAt < 0 ? null : rawTarget[queryAt..]) is { } sitePage &&
            answer(sitePage, cancellationToken) is { } envelope &&
            await _renderer.RenderAsync(sitePage.Language, sitePage.Kind, Encoding.UTF8.GetString(envelope), path, cancellationToken).ConfigureAwait(false) is { } html)
        {
            var page = Encoding.UTF8.GetBytes(ForSearchEngines(response, html, path));
            response.StatusCode = AnswersUnknownWork(envelope) ? StatusCodes.Status404NotFound : StatusCodes.Status200OK;
            response.ContentType = ContentTypes[".html"];
            response.ContentLength = page.Length;
            if (HttpMethods.IsGet(method))
            {
                await response.Body.WriteAsync(page, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
        // Kept out, nothing is opened, so a page is never read as text here.
        if (string.Equals(Path.GetExtension(file), ".html", StringComparison.Ordinal) && string.Equals(SearchEngines, Indexed, StringComparison.Ordinal))
        {
            var built = Encoding.UTF8.GetString(bytes);
            var served = ForSearchEngines(response, built, path);
            bytes = ReferenceEquals(served, built) ? bytes : Encoding.UTF8.GetBytes(served);
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = ContentTypes[Path.GetExtension(file)];
        response.ContentLength = bytes.Length;
        if (HttpMethods.IsGet(method))
        {
            await response.Body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// A page as served at <paramref name="path"/> in the switch's state (<see cref="ServedPage"/>): when its kept-out marker
    /// was removed, the page the switch opened, without the <c>X-Robots-Tag</c> every other response carries; else the page
    /// as it is, the same instance.
    /// </summary>
    private string ForSearchEngines(HttpResponse response, string html, string path)
    {
        var (served, opened) = ServedPage(html, SearchEngines, path, _indexingRecord);
        if (!opened)
        {
            return html;
        }

        response.Headers.Remove("X-Robots-Tag");
        return served;
    }

    /// <summary>
    /// A redirect or a move with the site indexed carries no <c>X-Robots-Tag</c>: it is no page, and the page it leads to says
    /// what it says (an <c>x-default</c> alternate is the root's or a law's redirect). Kept out, it keeps the header.
    /// </summary>
    private void ForSearchEnginesRedirect(HttpResponse response)
    {
        if (string.Equals(SearchEngines, Indexed, StringComparison.Ordinal))
        {
            response.Headers.Remove("X-Robots-Tag");
        }
    }

    /// <summary>
    /// Writes the headers every response of this root carries (ruling 3), and returns whether the method may be answered:
    /// for any method but GET or HEAD it answers 405, naming the two, and returns false. <c>X-Robots-Tag: noindex</c> is one
    /// of them (R67): only a page the switch opened goes without it (<see cref="ForSearchEngines"/>).
    /// </summary>
    private bool WritePageHeaders(HttpResponse response, string method)
    {
        var headers = response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.StrictTransportSecurity = StrictTransportSecurity;
        headers["Referrer-Policy"] = "no-referrer";
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "no-store";
        headers["X-Robots-Tag"] = XRobotsTag;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        {
            return true;
        }

        response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        headers.Allow = "GET, HEAD";
        return false;
    }

    // Plain patterns, not source-generated ones: the generator adds types of its own to the assembly, with names
    // derived from this file, which the closed-surface census would then have to pin (review of #788's CI).
    private static readonly Regex SafePath = new(@"^[a-z0-9-]+(/[a-z0-9-]+)*\.[a-z0-9]+$", RegexOptions.CultureInvariant);

    /// <summary>The interface languages, each under its own path (R61); the first is the one a reader with no preference gets.</summary>
    internal static readonly IReadOnlyList<string> InterfaceLanguages = ["fr", "en"];

    /// <summary>
    /// The site's addresses for law, as <c>web/scripts/permalinks.mjs</c> parses them (a test holds the two to one table): a
    /// Luxembourg work (<c>/lu-legilux/{work}</c>) or an EU act (<c>/eu-eurlex/{celex}</c>), served its dossier; the one
    /// permalink shape of the specification of record's R64, a Luxembourg state
    /// (<c>/lu-legilux/{work}/{date}--{sha256}/{lng}</c>) or an EU wording (<c>/eu-eurlex/{celex}/{date}--{sha256}/{lng}</c>),
    /// with the article (or provision) as a last segment when one is named, served its reading; and the readable address of an
    /// act's current version in a language (R67), <c>/lu-legilux/{work}/{lng}</c> or <c>/eu-eurlex/{celex}/{lng}</c>, with an
    /// article (or provision) as a last segment, served its reading as it stands today; each under an interface language
    /// (<c>/en/</c>, <c>/fr/</c>), or with none, as the API mints it, to be sent to the reader's. <c>{lng}</c> is the text's
    /// language; the article is RFC 3986's path characters, anything else percent-encoded, and never one that is <c>.</c> or
    /// <c>..</c> once decoded (<c>%2e</c> or <c>%2E</c> for a dot, as a URL parser reads it; review of #967: this root served
    /// <c>%2E%2E</c> as an article, which the live server and a browser resolve away). A readable address's article is never
    /// a calendar date, with or without a digest after it: <c>/eu-eurlex/{celex}/{lng}/{date}</c> is an EU stable coordinate
    /// and <c>/eu-eurlex/{celex}/{lng}/{date}--{sha256}</c> the EU permalink shape before R64 (<see cref="MovedFor"/>), so
    /// neither shape names an article. A <c>#</c> (a position inside an article) never reaches a server. The end is
    /// <c>\z</c>, not <c>$</c>, which in .NET also matches before a final newline.
    /// </summary>
    private static readonly Regex SitePath = new(
        "^/(?:(?<locale>fr|en)/)?(?<law>lu-legilux/(?<work>[a-z0-9][a-z0-9_-]*)|eu-eurlex/(?<celex>[0-9A-Za-z][0-9A-Za-z()._-]*))" +
        "(?:(?<pin>/(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})--(?<digest>[0-9a-f]{64})/(?<lng>[a-z]{3})" +
        "(?:/(?!(?:\\.|%2[eE]){1,2}\\z)(?:[0-9A-Za-z._~!$&'()*+,;=:@-]|%[0-9A-Fa-f]{2})+)?)" +
        "|(?<current>/(?<currentLng>[a-z]{3})" +
        "(?:/(?![0-9]{4}-[0-9]{2}-[0-9]{2}(?:--[0-9a-f]{64})?\\z)(?!(?:\\.|%2[eE]){1,2}\\z)(?:[0-9A-Za-z._~!$&'()*+,;=:@-]|%[0-9A-Fa-f]{2})+)?))?\\z",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// An EU wording's permalink of the shape before R64, <c>/eu-eurlex/{celex}/{lng}/{date}--{sha256}</c>, under an interface
    /// language or none: it names everything R64's does, so it has moved there (<see cref="MovedFor"/>). A Luxembourg state's
    /// of that time (<c>/lu-legilux/{work}/{date}--{sha256}</c>) names no language, so no address can be computed from it
    /// alone: it is not matched, and is answered as any address this root does not hold.
    /// </summary>
    private static readonly Regex EuropeWordingBeforeR64 = new(
        "^/(?:(?<locale>fr|en)/)?eu-eurlex/(?<celex>[0-9A-Za-z][0-9A-Za-z()._-]*)/(?<lng>[a-z]{3})/(?<pin>[0-9]{4}-[0-9]{2}-[0-9]{2}--[0-9a-f]{64})\\z",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// A consolidated version's CELEX as EUR-Lex writes it, <c>0{yyyy}{type}{number}-{yyyymmdd}</c>: sector 0, the consolidated
    /// act's year, type and number, and the consolidation's date; the act's own CELEX is the same in sector 3.
    /// </summary>
    private static readonly Regex ConsolidatedCelex = new(
        "^0(?<act>[0-9]{4}[A-Z]{1,2}[0-9]{4,}(?:\\([0-9]{2}\\))?)-[0-9]{8}\\z",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// A consolidated version's readable address, <c>/eu-eurlex/0{act}-{yyyymmdd}/{lng}[/{provision}]</c>, under an interface
    /// language or none: it names one wording, not the act today, so it has moved to its act's readable address
    /// (<see cref="MovedFor"/>; review of #972: it quoted another wording under its own address).
    /// </summary>
    private static readonly Regex EuropeConsolidatedReadable = new(
        "^/(?:(?<locale>fr|en)/)?eu-eurlex/0(?<act>[0-9]{4}[A-Z]{1,2}[0-9]{4,}(?:\\([0-9]{2}\\))?)-[0-9]{8}/(?<lng>[a-z]{3})" +
        "(?<article>/(?![0-9]{4}-[0-9]{2}-[0-9]{2}(?:--[0-9a-f]{64})?\\z)(?!(?:\\.|%2[eE]){1,2}\\z)(?:[0-9A-Za-z._~!$&'()*+,;=:@-]|%[0-9A-Fa-f]{2})+)?\\z",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// A site path's match, or null when it is none, or is a consolidated version's readable address, which names no page
    /// here and has moved (<see cref="MovedFor"/>).
    /// </summary>
    private static Match? SiteMatch(string path)
    {
        var match = SitePath.Match(path);
        return !match.Success ||
               (match.Groups["current"].Success && match.Groups["celex"].Success && ConsolidatedCelex.IsMatch(match.Groups["celex"].Value))
            ? null
            : match;
    }

    /// <summary>
    /// The page a site path under an interface language is served by (<c>/en/reading.html</c>, <c>/fr/dossier.html</c>), or
    /// null for any other path, an address for law with no language included (it is redirected).
    /// </summary>
    internal static string? PageForSitePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var match = SiteMatch(path);
        if (match is null || !match.Groups["locale"].Success)
        {
            return null;
        }

        var page = match.Groups["pin"].Success || match.Groups["current"].Success ? "reading.html" : "dossier.html";
        return $"/{match.Groups["locale"].Value}/{page}";
    }

    /// <summary>
    /// What the server renders a law's address under an interface language from (R67), as <c>sitePageFor</c> in
    /// <c>web/scripts/permalinks.mjs</c> says it (a test holds the two to one table): the page, the operation and the request
    /// the page itself would send. A permalink's reading asks by the permalink itself (R64: the version it pins, or why not),
    /// with the date and the text's language it names; a readable address's reading asks by the work (a Luxembourg coordinate
    /// or an EU CELEX), <paramref name="today"/> (Luxembourg's date, R13) and the text's language it names, and with no
    /// <paramref name="today"/> is not rendered; a dossier asks by the work. An interface language's home page
    /// (<c>/en/</c>, <c>/en/index.html</c>) is rendered from <c>coverage</c>, asked with no parameters, as the page as built
    /// asks it once loaded: its coverage line and today's date in Luxembourg (R11, R13; <c>web/scripts/live-home.mjs</c>).
    /// Null for any other path.
    /// </summary>
    /// <remarks>
    /// A reading is read one page at a time (the plan's item 3.7): an address that names an article asks the page that holds
    /// it (<c>article</c>, the segment decoded; a segment that is not UTF-8 once decoded names none), and an address with no
    /// article whose <paramref name="query"/> (with its <c>?</c>) names <c>page=N</c>, N a whole number from 2 written without
    /// leading zeros, asks that page; anything else in the query is ignored, as before. Page 1 is the address with no page.
    /// </remarks>
    internal static V3SitePage? SitePageFor(string path, string? today = null, string? query = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        foreach (var home in InterfaceLanguages)
        {
            if (string.Equals(path, $"/{home}/", StringComparison.Ordinal) || string.Equals(path, $"/{home}/index.html", StringComparison.Ordinal))
            {
                return new V3SitePage(home, "home", "coverage", new Dictionary<string, object>(StringComparer.Ordinal));
            }
        }

        var match = SiteMatch(path);
        if (match is null || !match.Groups["locale"].Success)
        {
            return null;
        }

        var language = match.Groups["locale"].Value;
        var work = match.Groups["work"].Success ? "/lu-legilux/" + match.Groups["work"].Value : match.Groups["celex"].Value;
        if (match.Groups["pin"].Success)
        {
            var date = match.Groups["date"].Value;
            var lng = match.Groups["lng"].Value;
            return new V3SitePage(language, "reading", "evidence_bundle", Paged(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["identifier"] = $"/{match.Groups["law"].Value}/{date}--{match.Groups["digest"].Value}/{lng}",
                ["date"] = date,
                ["language"] = lng,
            }, path, query));
        }

        if (match.Groups["current"].Success)
        {
            if (today is null)
            {
                return null;
            }

            return new V3SitePage(language, "reading", "evidence_bundle", Paged(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["identifier"] = work,
                ["date"] = today,
                ["language"] = match.Groups["currentLng"].Value,
            }, path, query));
        }

        return new V3SitePage(language, "dossier", "dossier", new Dictionary<string, object>(StringComparer.Ordinal) { ["identifier"] = work });
    }

    /// <summary>
    /// A reading's request with the page its address names (the plan's item 3.7), as <c>readingRequestFor</c> in
    /// <c>web/scripts/permalinks.mjs</c> adds it: the article the last segment names, decoded, or else the page the query names.
    /// </summary>
    private static Dictionary<string, object> Paged(Dictionary<string, object> parameters, string path, string? query)
    {
        if (ArticleSegmentOf(path) is { } segment)
        {
            if (DecodeSegment(segment) is { } article)
            {
                parameters["article"] = article;
            }

            return parameters;
        }

        if (PageOfQuery(query) is { } page)
        {
            parameters["page"] = page;
        }

        return parameters;
    }

    /// <summary>The article segment a reading's address names (escaped as the path writes it), or null.</summary>
    private static string? ArticleSegmentOf(string path)
    {
        var match = SiteMatch(path);
        if (match is null)
        {
            return null;
        }

        // The address up to its language: the pin's (/{date}--{digest}/{lng}) or the readable address's (/{lng}).
        var languageGroup = match.Groups["pin"].Success ? match.Groups["lng"] : match.Groups["currentLng"];
        var end = languageGroup.Index + languageGroup.Length;
        return end < path.Length ? path[(end + 1)..] : null;
    }

    /// <summary>
    /// A path segment decoded as a browser's <c>decodeURIComponent</c> decodes it: each <c>%HH</c> a byte, the bytes UTF-8; a
    /// segment whose bytes are not UTF-8 decodes to nothing (null), as there it throws.
    /// </summary>
    private static string? DecodeSegment(string segment)
    {
        var bytes = new List<byte>(segment.Length);
        for (var index = 0; index < segment.Length; index++)
        {
            if (segment[index] == '%' && index + 2 < segment.Length &&
                byte.TryParse(segment.AsSpan(index + 1, 2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                bytes.Add(value);
                index += 2;
                continue;
            }

            bytes.AddRange(Encoding.UTF8.GetBytes(segment[index].ToString()));
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// The page a query names (<c>?page=N</c>, its first <c>page</c> parameter): N a whole number from 2 to 100,000 written
    /// without leading zeros, else none. As <c>pageOfQuery</c> in <c>web/scripts/permalinks.mjs</c>.
    /// </summary>
    private static int? PageOfQuery(string? query)
    {
        if (query is null || query.Length < 2 || query[0] != '?')
        {
            return null;
        }

        foreach (var part in query[1..].Split('&'))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? part : part[..equals];
            if (!string.Equals(name, "page", StringComparison.Ordinal))
            {
                continue;
            }

            var value = equals < 0 ? string.Empty : part[(equals + 1)..];
            if (!PageNumber.IsMatch(value))
            {
                return null;
            }

            var number = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            return number is >= 2 and <= 100_000 ? number : null;
        }

        return null;
    }

    private static readonly Regex PageNumber = new("^[1-9][0-9]{0,5}\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Whether an envelope is the refusal of a work the API does not know: its page is a 404, as an absent page is.</summary>
    private static bool AnswersUnknownWork(byte[] envelope)
    {
        try
        {
            using var document = JsonDocument.Parse(envelope);
            return document.RootElement.TryGetProperty("refusal", out var refusal) &&
                refusal.ValueKind == JsonValueKind.Object &&
                refusal.TryGetProperty("code", out var code) &&
                string.Equals(code.GetString(), "identifier_unknown", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The interface language a reader starts in (R61), as <c>negotiateLanguage</c> in <c>web/scripts/permalinks.mjs</c>
    /// chooses it: of French and English, the one <paramref name="acceptLanguage"/> prefers (the highest weight, the first
    /// listed on a tie; a weight of 0 refuses), else French. Only the primary subtag counts, and only spaces and tabs are
    /// whitespace, as HTTP says (review of #958: .NET's own trim and JavaScript's disagree on U+0085 and U+FEFF).
    /// </summary>
    internal static string NegotiateLanguage(string? acceptLanguage)
    {
        string? best = null;
        var bestWeight = 0.0;
        foreach (var part in (acceptLanguage ?? string.Empty).Split(','))
        {
            var pieces = part.Trim(Whitespace).Split(';');
            var primary = pieces[0].Trim(Whitespace).ToLowerInvariant().Split('-')[0];
            if (!InterfaceLanguages.Contains(primary))
            {
                continue;
            }

            var weight = 1.0;
            foreach (var parameter in pieces.Skip(1))
            {
                var pair = parameter.Trim(Whitespace).Split('=');
                if (string.Equals(pair[0].Trim(Whitespace), "q", StringComparison.OrdinalIgnoreCase))
                {
                    var value = pair.Length > 1 ? pair[1].Trim(Whitespace) : string.Empty;
                    weight = Weight.IsMatch(value) ? double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                }
            }

            if (weight > bestWeight)
            {
                best = primary;
                bestWeight = weight;
            }
        }

        return best ?? InterfaceLanguages[0];
    }

    /// <summary>
    /// Where a path with no interface language is sent, for a reader whose browser says <paramref name="acceptLanguage"/>:
    /// the root (<c>/</c>, <c>/index.html</c>) to that language's home page, a language's root written without its slash to
    /// it with one, and an address for law to the same address under that language; the query (<paramref name="query"/>,
    /// with its <c>?</c>) goes along, from the root too. Null for any other path. <c>Language</c> is the home page the redirect relies on.
    /// </summary>
    internal static (string Location, string Language)? RedirectFor(string path, string query, string? acceptLanguage)
    {
        ArgumentNullException.ThrowIfNull(path);
        var language = NegotiateLanguage(acceptLanguage);
        if (path is "/" or "/index.html")
        {
            return ($"/{language}/{query}", $"/{language}/");
        }

        if (path.Length > 1 && InterfaceLanguages.Contains(path[1..]))
        {
            return ($"{path}/{query}", $"{path}/");
        }

        var match = SiteMatch(path);
        if (match is null || match.Groups["locale"].Success)
        {
            return null;
        }

        return ($"/{language}{path}{query}", $"/{language}/");
    }

    /// <summary>
    /// Where an address has moved for good, as <c>movedFor</c> in <c>web/scripts/permalinks.mjs</c> says it (a test holds the two
    /// to one table): an EU wording's permalink of the shape before R64, <c>/eu-eurlex/{celex}/{lng}/{date}--{sha256}</c>, to
    /// <c>/eu-eurlex/{celex}/{date}--{sha256}/{lng}</c>, and a consolidated version's readable address,
    /// <c>/eu-eurlex/0{act}-{yyyymmdd}/{lng}[/{provision}]</c>, to its act's, <c>/eu-eurlex/3{act}/{lng}[/{provision}]</c> (review
    /// of #972), each under the interface language it stood under, if any, with the query
    /// (<paramref name="query"/>, with its <c>?</c>). Null for any other path. <c>Page</c> is the reading page the move relies
    /// on: the one under that language, or the first interface language's for an address with none.
    /// </summary>
    internal static (string Location, string Page)? MovedFor(string path, string query)
    {
        ArgumentNullException.ThrowIfNull(path);
        var match = EuropeWordingBeforeR64.Match(path);
        if (match.Success)
        {
            var locale = match.Groups["locale"].Success ? match.Groups["locale"].Value : null;
            return (
                $"{(locale is null ? string.Empty : "/" + locale)}/eu-eurlex/{match.Groups["celex"].Value}/{match.Groups["pin"].Value}/{match.Groups["lng"].Value}{query}",
                $"/{locale ?? InterfaceLanguages[0]}/reading.html");
        }

        // A consolidated version's readable address, to its act's (review of #972).
        var consolidated = EuropeConsolidatedReadable.Match(path);
        if (consolidated.Success)
        {
            var locale = consolidated.Groups["locale"].Success ? consolidated.Groups["locale"].Value : null;
            return (
                $"{(locale is null ? string.Empty : "/" + locale)}/eu-eurlex/3{consolidated.Groups["act"].Value}/{consolidated.Groups["lng"].Value}{consolidated.Groups["article"].Value}{query}",
                $"/{locale ?? InterfaceLanguages[0]}/reading.html");
        }

        return null;
    }

    /// <summary>A header value's optional whitespace, as HTTP has it: space and tab.</summary>
    private static readonly char[] Whitespace = [' ', '\t'];

    private static readonly Regex Weight = new(@"^(?:0(?:\.[0-9]{0,3})?|1(?:\.0{0,3})?)\z", RegexOptions.CultureInvariant);

    private static readonly Regex PolicyMeta = new("<meta http-equiv=\"Content-Security-Policy\" content=\"(?<policy>[^\"]*)\"", RegexOptions.CultureInvariant);
}
