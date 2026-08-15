using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Lex.Law;

namespace Lex.Sources.EurLex;

/// <summary>
/// EUR-Lex / Cellar adapter (Tier A). Consolidated versions carry publisher-supplied
/// dates (cdm:act_consolidated_date). Text reuse is permitted with attribution under
/// Commission Decision 2011/833/EU, so this is the text-bearing publisher: bodies are
/// fetched verbatim (XHTML) from the official Cellar dissemination channel
/// (publications.europa.eu — robots.txt: Allow /), sequential and paced.
/// NOTE: consolidated texts carry no legal effect; only OJ acts are authentic (§9.6).
/// </summary>
public sealed class EurLexAdapter : ISourceAdapter, ISourceBuildInventory,
    ILegacyVersionIdentityResolver
{
    private const string Sparql = "https://publications.europa.eu/webapi/rdf/sparql";
    private const string CelexIdentifierPrefix =
        "http://publications.europa.eu/resource/celex/";
    private const string Cdm = "PREFIX cdm: <http://publications.europa.eu/ontology/cdm#>\n";
    private const string Owl = "PREFIX owl: <http://www.w3.org/2002/07/owl#>\n";
    private const string Skos = "PREFIX skos: <http://www.w3.org/2004/02/skos/core#>\n";
    private const string EuroVoc = "PREFIX eurovoc: <http://eurovoc.europa.eu/schema#>\n";
    private const string ShortTitlePredicate =
        "http://publications.europa.eu/ontology/cdm#expression_title_short";
    // Primary XHTML is the searchable legal wording. Large annex-heavy acts legitimately exceed
    // 4 MiB (Regulation 1791/2006 is about 8.7 MiB), so give it a separate offline-ingest budget.
    // Optional Formex archives stay more tightly bounded and are guarded again after expansion.
    private const int BodyCapBytes = 32 * 1024 * 1024;
    private const int FormexArchiveCapBytes = 32 * 1024 * 1024;
    private const long FormexMemberCapBytes = 64 * 1024 * 1024;
    private const long FormexExpandedCapBytes = 128 * 1024 * 1024;
    private const int PortalIdentityMaximumResponses = 2;
    internal const int MetadataWorkBatchSize = 16;
    internal const int MetadataRowsPerWorkMaximum = 512;
    internal const int VirtuosoSortedTopMaximum = 10_000;
    private static readonly SourceRetryPolicy RetryPolicy = new(MaximumAttempts: 4);

    private static readonly HttpClient Http = CreateClient();
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private readonly Dictionary<string, List<VersionRecord>> _byWork = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkRef> _works = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _selectionReasons = new(StringComparer.Ordinal);
    private readonly EurLexScopeConfig _scope;
    private readonly string _scopeSha256;
    private readonly int _wave;
    private int _expectedWorks;
    private readonly List<SourceBuildIssue> _buildIssues = [];
    private bool _loaded;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public EurLexAdapter(string? scopePath = null, int? wave = null)
        : this(scopePath, wave, Http, Task.Delay)
    {
    }

    internal EurLexAdapter(
        string? scopePath,
        int? wave,
        HttpClient http,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        (_scope, _scopeSha256) = EurLexScopeConfig.LoadWithDigest(
            scopePath ?? Environment.GetEnvironmentVariable("LEX_EU_SCOPE"));
        _wave = wave ?? _scope.ApprovedWave;
        if (_wave is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(wave), "EU scope wave must be between 1 and 4.");
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(CreateHandler())
        { Timeout = TimeSpan.FromSeconds(180) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lex/0.1");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/SFHAJJI/lex)");
        return c;
    }

    internal static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        // Every legal expression is a public immutable publisher coordinate. A process-wide
        // session jar lets one expression's portal shell influence later acquisitions.
        UseCookies = false,
    };

    public PublisherDescriptor Describe() => new(
        new Publisher(
            Id: "eu-eurlex",
            Name: "Publications Office of the EU (EUR-Lex / Cellar)",
            Jurisdiction: "EU",
            Homepage: "https://eur-lex.europa.eu",
            Tier: Tier.A,
            Attribution: "© European Union, 1998-2026. Reuse permitted with attribution under Commission Decision 2011/833/EU. Consolidated texts have no legal effect; only acts published in the Official Journal are authentic.",
            SourceTermsUrl: "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html"),
        DocumentTypes: [],
        Languages: _scope.Languages,
        TextIncluded: true,
        TextPublic: true,   // reuse right measured (Decision 2011/833/EU)
        HistoryBegins: "publisher");

    public SourceBuildInventory GetBuildInventory() =>
        new(_expectedWorks, _buildIssues.OrderBy(issue => issue.Work, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal).ToArray(),
            RetryMaximumAttempts: RetryPolicy.MaximumAttempts,
            SourceConfigurationKind: "engineering_scope",
            SourceConfigurationSha256: _scopeSha256);

    public Identifier ResolveLegacyVersionIdentity(LegacyVersionIdentity legacy)
    {
        var baseCelex = LegacyWorkCelex(legacy.WorkIdentifier);
        if (legacy.Expressions.Count == 0)
            throw new InvalidDataException(
                "A EUR-Lex legacy version has no expression source identity.");

        var states = legacy.Expressions
            .Select(LegacyExpressionCelex)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (states.Length != 1)
            throw new InvalidDataException(
                "A EUR-Lex legacy version resolves to more than one CELEX state.");

        var state = states[0];
        var expectedDate = legacy.ValidFrom.ToString(
            "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var expectedConsolidation = "0" + baseCelex[1..] + "-" + expectedDate;
        if (!string.Equals(state, baseCelex, StringComparison.Ordinal)
            && !(baseCelex[0] == '3'
                 && string.Equals(state, expectedConsolidation,
                     StringComparison.Ordinal)))
            throw new InvalidDataException(
                "A EUR-Lex legacy expression is neither the original work CELEX nor its "
                + "exact dated consolidation for valid_from.");

        return new Identifier(CelexIdentifierPrefix + state);
    }

    private static string LegacyWorkCelex(string value)
    {
        if (!value.StartsWith(CelexIdentifierPrefix, StringComparison.Ordinal))
            throw new InvalidDataException(
                "A EUR-Lex legacy work_identifier is not an exact official CELEX resource URI.");
        var celex = value[CelexIdentifierPrefix.Length..];
        if (!System.Text.RegularExpressions.Regex.IsMatch(celex,
                @"^(?:1[0-9]{4}(?:E[0-9]{3}|JN[0-9]{2}/[0-9]{2}|TN[0-9]{2}/[0-9]{2}/A|[A-Z]/TXT)|3[0-9]{4}[RLD][0-9]{4}(?:R\([0-9]{2}\))?)$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException(
                "A EUR-Lex legacy work_identifier does not contain one supported work CELEX.");
        return celex;
    }

    private static string LegacyExpressionCelex(LegacyExpressionIdentity expression)
    {
        if (expression.Language.Length != 2
            || expression.Language.Any(character => character is not (>= 'a' and <= 'z')))
            throw new InvalidDataException(
                "A EUR-Lex legacy expression has an invalid language code.");

        // Official permanent-link and sector-0 CELEX forms:
        // https://eur-lex.europa.eu/content/help/data-reuse/linking.html?locale=en
        // https://eur-lex.europa.eu/content/help/eurlex-content/celex-number.html?locale=en
        var prefix = "https://eur-lex.europa.eu/legal-content/"
                     + expression.Language.ToUpperInvariant()
                     + "/TXT/?uri=CELEX:";
        if (!expression.SourceUri.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException(
                "A EUR-Lex legacy expression source_uri is not its exact official language path.");
        var encodedCelex = expression.SourceUri[prefix.Length..];
        string celex;
        try
        {
            celex = Uri.UnescapeDataString(encodedCelex);
        }
        catch (UriFormatException ex)
        {
            throw new InvalidDataException(
                "A EUR-Lex legacy expression source_uri has an invalid CELEX encoding.", ex);
        }
        if (celex.Length == 0
            || !string.Equals(expression.SourceUri,
                ExpressionSourceUri(expression.Language, celex),
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "A EUR-Lex legacy expression source_uri is not one canonical CELEX link.");
        return celex;
    }

    public async IAsyncEnumerable<WorkRef> EnumerateWorks([EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        foreach (var w in _works.Values.OrderBy(w => w.Slug, StringComparer.Ordinal))
            yield return w;
    }

    public async Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        return _byWork.TryGetValue(work.Id.Value, out var v) ? v : [];
    }

    public async Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
    {
        var celex = version.Raw.GetValueOrDefault("celex");
        if (celex is null)
            return new(SourceBodyStatus.ParserFailure, Detail: "The version has no CELEX identifier.");

        var portalBound = version.Raw.GetValueOrDefault("expression_source")
            == "verified_eurlex_portal";

        // Cellar is canonical, but a small number of language-specific corrigenda return 404
        // there while the official EUR-Lex expression URI serves the XHTML. Retry that URI only
        // when it remains on an EU institutional host; no third-party fallback can become evidence.
        var urls = new List<string>();
        if (portalBound)
        {
            if (OfficialEuUri(expression.SourceUri) is not { } verifiedExpressionUri)
                return new(SourceBodyStatus.ParserFailure,
                    Detail: "The verified EUR-Lex expression URI is not on an official EU host.");
            urls.Add(verifiedExpressionUri);
        }
        else
        {
            urls.Add(CellarResourceUrl(celex));
            if (OfficialEuUri(expression.SourceUri) is { } expressionUri &&
                !string.Equals(expressionUri, urls[0], StringComparison.OrdinalIgnoreCase))
                urls.Add(expressionUri);
        }

        FetchResult? last = null;
        var totalAttempts = 0;
        for (var attempt = 0; attempt < urls.Count; attempt++)
        {
            var remainingAttempts = RetryPolicy.MaximumAttempts - totalAttempts;
            if (remainingAttempts <= 0) break;

            var requiresPortalIdentity = portalBound || attempt > 0;
            if (requiresPortalIdentity)
            {
                var exact = await FetchExactPortalBodyAsync(
                    urls[attempt], celex, expression.Language, remainingAttempts, ct);
                last = exact.Fetch;
                totalAttempts += last.Attempts;
                if (!exact.ExactIdentity && exact.IdentityFailureDetail is not null)
                    return new(SourceBodyStatus.ParserFailure,
                        Detail: exact.IdentityFailureDetail,
                        Attempts: totalAttempts);
            }
            else
            {
                await PaceAsync(ct);
                last = await FetchBytes(urls[attempt], expression.Language,
                    "application/xhtml+xml, text/html", BodyCapBytes,
                    remainingAttempts, ct);
                totalAttempts += last.Attempts;
            }
            if (last.Bytes is null) continue;

            var text = System.Text.Encoding.UTF8.GetString(last.Bytes);

            var fallback = portalBound || attempt > 0
                ? " (official EUR-Lex fallback)"
                : "";
            Console.Error.WriteLine($"  [eurlex] {celex}: body {last.Bytes.Length / 1024} KB{fallback}");
            return SourceBodyFetch.Retrieved(text, totalAttempts);
        }

        if (last?.LimitExceeded == true)
            Console.Error.WriteLine($"  [eurlex] {celex}: official body exceeds {BodyCapBytes / 1024 / 1024} MB cap — metadata only");
        else
            Console.Error.WriteLine($"  [eurlex] {celex}: official body unavailable after {urls.Count} endpoint(s) (last status {(int?)last?.StatusCode})");
        if (last?.LimitExceeded == true)
            return new(SourceBodyStatus.Oversized,
                Detail: $"Official body exceeded the {BodyCapBytes}-byte acquisition limit.",
                Attempts: totalAttempts);
        if (last?.RetryExhausted == true)
            return new(SourceBodyStatus.RetryExhausted,
                Detail: last.FailureDetail ?? "The official endpoint exhausted the retry policy.",
                Attempts: totalAttempts);
        return last?.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => new(SourceBodyStatus.PermanentNotFound,
                Detail: "Every bounded official endpoint returned HTTP 404.", Attempts: totalAttempts),
            System.Net.HttpStatusCode.Gone => new(SourceBodyStatus.Gone,
                Detail: "The official endpoint returned HTTP 410.", Attempts: totalAttempts),
            System.Net.HttpStatusCode.RequestTimeout
                or System.Net.HttpStatusCode.TooManyRequests
                or System.Net.HttpStatusCode.InternalServerError
                or System.Net.HttpStatusCode.BadGateway
                or System.Net.HttpStatusCode.ServiceUnavailable
                or System.Net.HttpStatusCode.GatewayTimeout => new(SourceBodyStatus.RetryExhausted,
                    Detail: $"Retryable publisher response {(int?)last.StatusCode} exhausted the acquisition policy.",
                    Attempts: totalAttempts),
            _ => new(SourceBodyStatus.ParserFailure,
                Detail: $"Official body acquisition failed with HTTP {(int?)last?.StatusCode}.",
                Attempts: totalAttempts),
        };
    }

    private async Task<FetchResult> FetchBytes(
        string initialUrl,
        string language,
        string accept,
        int capBytes,
        int maximumAttempts,
        CancellationToken ct)
    {
        if (maximumAttempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        var url = initialUrl;
        HttpResponseMessage? resp = null;
        var attempts = 0;
        var retryExhausted = false;
        string? failureDetail = null;
        for (var hop = 0; hop < 6; hop++)
        {
            resp?.Dispose();
            var remainingAttempts = maximumAttempts - attempts;
            if (remainingAttempts <= 0)
                return new FetchResult(null, null, false, url, Attempts: attempts,
                    RetryExhausted: true,
                    FailureDetail: "The redirect chain exhausted the bounded acquisition policy.");
            var currentUrl = url;
            var sent = await SourceHttp.SendAsync(_http, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                req.Headers.TryAddWithoutValidation("Accept", accept);
                req.Headers.AcceptLanguage.ParseAdd(language);
                return req;
            }, RetryPolicy with { MaximumAttempts = remainingAttempts }, ct,
                delay: _delay);
            attempts += sent.Attempts;
            retryExhausted = sent.RetryExhausted;
            failureDetail = sent.FailureDetail;
            resp = sent.Response;
            if (resp is null)
                return new FetchResult(null, null, false, url, Attempts: attempts,
                    RetryExhausted: true, FailureDetail: failureDetail);
            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
            {
                var next = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(url), loc).ToString();
                url = next.StartsWith("http://", StringComparison.Ordinal) ? "https://" + next["http://".Length..] : next;
                if (OfficialEuUri(url) is null)
                {
                    resp.Dispose();
                    return new FetchResult(null, null, false, url, Attempts: attempts,
                        FailureDetail: "The publisher redirected outside the official EU host allowlist.");
                }
                continue;
            }
            break;
        }
        if (resp is null || !resp.IsSuccessStatusCode)
        {
            var status = resp?.StatusCode;
            resp?.Dispose();
            return new FetchResult(null, status, false, url, Attempts: attempts,
                RetryExhausted: retryExhausted, FailureDetail: failureDetail);
        }
        using var _ = resp;
        if (resp.Content.Headers.ContentLength is { } contentLength && contentLength > capBytes)
            return new FetchResult(null, resp.StatusCode, true, url, Attempts: attempts);

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var bounded = await ReadBounded(stream, capBytes, ct);
        var contentLanguage = string.Join(",",
            resp.Content.Headers.ContentLanguage.OrderBy(value => value, StringComparer.Ordinal));
        return new FetchResult(bounded.Bytes, resp.StatusCode, bounded.LimitExceeded, url,
            resp.Content.Headers.ContentType?.MediaType,
            string.IsNullOrEmpty(contentLanguage) ? null : contentLanguage,
            Attempts: attempts);
    }

    private async Task<PortalBodyFetch> FetchExactPortalBodyAsync(
        string sourceUri,
        string celex,
        string language,
        int maximumAttempts,
        CancellationToken ct)
    {
        FetchResult? last = null;
        var attempts = 0;
        var identityResponses = 0;
        string? identityFailureDetail = null;
        while (attempts < maximumAttempts
               && identityResponses < PortalIdentityMaximumResponses)
        {
            await PaceAsync(ct);
            var fetched = await FetchBytes(sourceUri, language,
                "application/xhtml+xml, text/html", BodyCapBytes,
                maximumAttempts - attempts, ct);
            attempts += fetched.Attempts;
            last = fetched with { Attempts = attempts };
            if (fetched.Bytes is null)
                return new(last, ExactIdentity: false,
                    IdentityFailureDetail: identityFailureDetail);

            var text = System.Text.Encoding.UTF8.GetString(fetched.Bytes);
            if (IsExactPortalExpression(text, celex, language))
                return new(last, ExactIdentity: true, IdentityFailureDetail: null);

            identityResponses++;
            identityFailureDetail = PortalIdentityFingerprint(
                last, exactIdentity: false);
            Console.Error.WriteLine(
                $"  [eurlex] {celex}: portal identity mismatch; "
                + identityFailureDetail);
        }

        return new(last ?? throw new InvalidOperationException(
            "The portal acquisition loop ended without an attempt."),
            ExactIdentity: false, IdentityFailureDetail: identityFailureDetail);
    }

    private static string PortalIdentityFingerprint(
        FetchResult fetched, bool exactIdentity)
    {
        var endpoint = Uri.TryCreate(fetched.SourceUri, UriKind.Absolute, out var uri)
            ? uri.Host + uri.AbsolutePath
            : "invalid-official-uri";
        var digest = fetched.Bytes is null
            ? "none"
            : Convert.ToHexString(SHA256.HashData(fetched.Bytes)).ToLowerInvariant();
        return $"endpoint={endpoint}; status={(int?)fetched.StatusCode}; "
               + $"content_type={fetched.MediaType ?? "none"}; "
               + $"content_language={fetched.ContentLanguage ?? "none"}; "
               + $"bytes={fetched.Bytes?.Length ?? 0}; sha256={digest}; "
               + $"exact_identity={exactIdentity.ToString().ToLowerInvariant()}";
    }

    internal static async Task<BoundedRead> ReadBounded(Stream stream, int capBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream(Math.Min(capBytes, 1024 * 1024));
        var buf = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            if (ms.Length + read > capBytes)
                return new BoundedRead(null, ms.Length + read, true);
            ms.Write(buf, 0, read);
        }
        return new BoundedRead(ms.ToArray(), ms.Length, false);
    }

    /// <summary>
    /// D48: Formex 4 manifestation (application/zip;mtype=fmx4) — the Publications Office's
    /// structural XML with publisher-minted ARTICLE/PARAG identifiers. The zip container is
    /// rebuilt per request (member timestamps embedded), so only member bytes are evidence.
    /// Identity guard: INFO.CONSLEG START.DATE must equal the requested version's valid_from
    /// (CONSLEG.DATE is the production date and may be later — e.g. GDPR corrigenda 2018).
    /// </summary>
    public async Task<SourceManifestationFetch> FetchAltManifestation(
        VersionRecord version, ExpressionRecord expression, CancellationToken ct)
    {
        // Formex identity checks require an official consolidated CONSLEG expression.
        // Original and unconsolidated acts do not contain INFO.CONSLEG, so requesting
        // their optional Formex representation can never produce an accepted result.
        if (version.Raw.TryGetValue("consolidation_status", out var status) && status != "published")
            return new(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "Formex is not defined for an unconsolidated expression.");

        var celex = version.Raw.GetValueOrDefault("celex");
        if (celex is null)
            return new(SourceBodyStatus.ParserFailure, Detail: "The version has no CELEX identifier.");
        await PaceAsync(ct);
        // Cellar's negotiation parser requires the exact spaceless mtype parameter.
        var fetched = await FetchBytes(CellarResourceUrl(celex), expression.Language,
            "application/zip;mtype=fmx4", FormexArchiveCapBytes,
            RetryPolicy.MaximumAttempts, ct);
        if (fetched.Bytes is null || fetched.MediaType is not "application/zip")
        {
            if (fetched.LimitExceeded)
                Console.Error.WriteLine($"  [eurlex] {celex}: optional fmx4 exceeds {FormexArchiveCapBytes / 1024 / 1024} MB archive cap — skipped");
            if (fetched.LimitExceeded)
                return new(SourceBodyStatus.Oversized,
                    Detail: $"Optional Formex archive exceeded {FormexArchiveCapBytes} bytes.",
                    Attempts: fetched.Attempts);
            if (fetched.RetryExhausted)
                return new(SourceBodyStatus.RetryExhausted, Detail: fetched.FailureDetail,
                    Attempts: fetched.Attempts);
            if (fetched.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(SourceBodyStatus.PermanentNotFound,
                    Detail: "The optional official Formex manifestation returned HTTP 404.",
                    Attempts: fetched.Attempts);
            return new(SourceBodyStatus.ParserFailure,
                Detail: "The optional official manifestation was not a Formex ZIP archive.",
                Attempts: fetched.Attempts);
        }

        using var ms = new MemoryStream(fetched.Bytes, writable: false);
        ms.Position = 0;
        var members = new List<ManifestationMember>();
        long expandedBytes = 0;
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry.Name);   // no traversal, flat member names only
                if (name.Length == 0) continue;
                if (entry.Length > FormexMemberCapBytes ||
                    expandedBytes + entry.Length > FormexExpandedCapBytes)
                {
                    Console.Error.WriteLine($"  [eurlex] {celex}: optional fmx4 exceeds safe expanded-data limit — skipped");
                    return new(SourceBodyStatus.Oversized,
                        Detail: "The expanded Formex archive exceeded its bounded member budget.",
                        Attempts: fetched.Attempts);
                }
                await using var es = entry.Open();
                var member = await ReadBounded(es, checked((int)Math.Min(FormexMemberCapBytes,
                    FormexExpandedCapBytes - expandedBytes)), ct);
                if (member.Bytes is null)
                {
                    Console.Error.WriteLine($"  [eurlex] {celex}: optional fmx4 member exceeds safe limit — skipped");
                    return new(SourceBodyStatus.Oversized,
                        Detail: "A Formex member exceeded its bounded expansion budget.",
                        Attempts: fetched.Attempts);
                }
                expandedBytes += member.Bytes.LongLength;
                members.Add(new ManifestationMember(name, member.Bytes));
            }
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"  [eurlex] {celex}: fmx4 response is not a readable zip — skipped");
            return new(SourceBodyStatus.ParserFailure, Detail: ex.Message, Attempts: fetched.Attempts);
        }
        if (members.Count == 0)
            return new(SourceBodyStatus.ParserFailure,
                Detail: "The Formex archive contained no usable members.", Attempts: fetched.Attempts);

        // Identity guard on the main member (the body carries INFO.CONSLEG).
        var wantDate = version.ValidFrom.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var main = members.FirstOrDefault(m =>
            m.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            !m.Name.EndsWith(".doc.xml", StringComparison.OrdinalIgnoreCase));
        var head = main is null ? "" : System.Text.Encoding.UTF8.GetString(main.Bytes, 0, Math.Min(main.Bytes.Length, 4096));
        var m1 = System.Text.RegularExpressions.Regex.Match(head, "INFO\\.CONSLEG[^>]*START\\.DATE=\"(\\d{8})\"");
        if (main is null || !m1.Success || m1.Groups[1].Value != wantDate)
        {
            Console.Error.WriteLine($"  [eurlex] {celex}: fmx4 identity check failed (START.DATE {(m1.Success ? m1.Groups[1].Value : "absent")} vs {wantDate}) — not stored");
            return new(SourceBodyStatus.ParserFailure,
                Detail: $"Formex identity did not match requested date {wantDate}.",
                Attempts: fetched.Attempts);
        }

        Console.Error.WriteLine($"  [eurlex] {celex}: fmx4 {members.Count} member(s), {members.Sum(x => x.Bytes.LongLength) / 1024} KB");
        return SourceManifestationFetch.Retrieved(
            new ManifestationFetch("fmx4", members, fetched.SourceUri), fetched.Attempts);
    }

    internal static string? OfficialEuUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !(string.Equals(uri.Host, "europa.eu", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".europa.eu", StringComparison.OrdinalIgnoreCase)))
            return null;
        return uri.AbsoluteUri;
    }

    internal sealed record BoundedRead(byte[]? Bytes, long BytesRead, bool LimitExceeded);
    private sealed record FetchResult(
        byte[]? Bytes,
        System.Net.HttpStatusCode? StatusCode,
        bool LimitExceeded,
        string SourceUri,
        string? MediaType = null,
        string? ContentLanguage = null,
        int Attempts = 1,
        bool RetryExhausted = false,
        string? FailureDetail = null);

    private sealed record PortalBodyFetch(
        FetchResult Fetch,
        bool ExactIdentity,
        string? IdentityFailureDetail);

    private async Task PaceAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow - _lastRequest;
        var pause = TimeSpan.FromMilliseconds(1500);
        if (since < pause) await _delay(pause - since, ct);
        _lastRequest = DateTimeOffset.UtcNow;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_loaded) return;
            var selected = await ResolveScopeAsync(ct);
            _expectedWorks = selected.Count;
            var allConsolidations = await LoadConsolidationsAsync(selected.Keys, ct);
            var allMetadata = await LoadWorkMetadataAsync(selected.Keys, ct);
            var allPublisherMetadata = await LoadPublisherMetadataAsync(selected.Keys, ct);
            foreach (var (baseCelex, reasons) in selected.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                _selectionReasons[baseCelex] = reasons;
                var rows = allConsolidations.GetValueOrDefault(baseCelex) ?? [];

                // Consolidated expressions usually carry no title in Cellar — fall back to the
                // base act's official EN title so search matches natural-language names.
                var baseTitleRows = allMetadata.GetValueOrDefault(baseCelex) ?? [];
                if (baseTitleRows.Count == 0)
                {
                    Console.Error.WriteLine($"  [eurlex] {baseCelex}: official work metadata unavailable");
                    _buildIssues.Add(new SourceBuildIssue(
                        "publisher_metadata_unavailable", NormalizeWorkSlug(baseCelex),
                        "Cellar returned no official work metadata for the selected CELEX identifier."));
                    continue;
                }
                var titles = baseTitleRows
                    .Where(r => r.ContainsKey("lang"))
                    .GroupBy(r => r["lang"], StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().GetValueOrDefault("title"), StringComparer.Ordinal);
                var baseTitle = titles.GetValueOrDefault("en") ?? titles.GetValueOrDefault("fr");
                var inForce = baseTitleRows.FirstOrDefault(r => r.ContainsKey("inforce"))
                    ?.GetValueOrDefault("inforce");
                var bindingStatus = NormalizeBindingStatus(inForce);

                var workUri = $"http://publications.europa.eu/resource/celex/{baseCelex}";
                var slug = NormalizeWorkSlug(baseCelex);
                var resourceType = baseTitleRows.FirstOrDefault()?.GetValueOrDefault("rtype");
                var typeCode = LegalForm(baseCelex, resourceType);
                var publisherMetadata = BuildPublisherMetadata(
                    baseCelex, baseTitleRows, allPublisherMetadata.GetValueOrDefault(baseCelex) ?? []);
                var amending = baseTitleRows.Any(row => row.GetValueOrDefault("is_amending") is "1" or "true");
                var correcting = baseTitleRows.Any(row => row.GetValueOrDefault("is_correcting") is "1" or "true");

                // Distinct versions sorted by consolidation date; valid_to = next valid_from - 1 (publisher-dated sequence).
                var versions = rows
                    .GroupBy(r => r["celex"], StringComparer.Ordinal)
                    .Select(g =>
                    {
                        var stateRows = g.ToArray();
                        return new ConsolidatedState(
                            g.Key,
                            ParseDate(stateRows[0]["date"]),
                            stateRows.Where(r => r.ContainsKey("lang"))
                                .GroupBy(r => r["lang"], StringComparer.Ordinal)
                                .ToDictionary(x => x.Key,
                                    x => x.First().GetValueOrDefault("title"),
                                    StringComparer.Ordinal),
                            ConsolidationLanguages(stateRows, _scope.Languages),
                            stateRows.Any(row => row.GetValueOrDefault("expression_source")
                                == "verified_eurlex_portal"));
                    })
                    .OrderBy(v => v.Date).ThenBy(v => v.Celex, StringComparer.Ordinal)
                    .ToList();
                var coordinates = ConsolidatedCoordinates(
                    versions.Select(version => (version.Celex, version.Date)));

                var list = new List<VersionRecord>();
                for (var i = 0; i < versions.Count; i++)
                {
                    var state = versions[i];
                    var celex = state.Celex;
                    var date = state.Date;
                    var validTo = coordinates[i].ValidTo;
                    var expressions = state.Languages.Select(lang =>
                    {
                        var title = ExpressionTitle(lang, state.Titles, titles);
                        var sourceUri = ExpressionSourceUri(lang, celex);
                        return new ExpressionRecord(lang, date, validTo, "publisher", title,
                            OfficialDisplayTitle(title, celex), sourceUri);
                    }).ToArray();
                    var raw = SourceRaw(celex, typeCode, bindingStatus, "published");
                    if (state.PortalBound)
                        raw["expression_source"] = "verified_eurlex_portal";
                    list.Add(new VersionRecord(
                        Id: new Identifier($"http://publications.europa.eu/resource/celex/{celex}"),
                        WorkId: new Identifier(workUri),
                        TypeCode: typeCode,
                        ValidFrom: date,
                        ValidTo: validTo,
                        ValidTimeSource: "publisher",
                        InForceStatus: inForce,
                        PublicationDate: date,
                        Expressions: expressions,
                        Relations: [new RelationRecord("consolidates", new Identifier(workUri))],
                        Raw: raw,
                        PublisherMetadata: publisherMetadata,
                        DocumentRoles: DocumentRoles(
                            resourceType, amending, correcting, consolidated: true)));
                }

                // The original official expression is a real temporal state when the first
                // published consolidation starts later. Keep it for amended works as well as
                // one-version works; otherwise history would begin only after the first
                // amendment. If Cellar already publishes a consolidation on the original date,
                // that state covers the same starting point and no duplicate is added.
                if (_scope.History.IncludeOriginal && (list.Count > 0 || _scope.History.IncludeUnamended))
                {
                    var originalDateText = baseTitleRows.FirstOrDefault(r => r.ContainsKey("date"))?.GetValueOrDefault("date");
                    if (originalDateText is null)
                    {
                        Console.Error.WriteLine($"  [eurlex] {baseCelex}: no publisher date for original expression");
                    }
                    else
                    {
                        var originalDate = ParseDate(originalDateText);
                        var consolidatedDates = list.Select(v => v.ValidFrom).ToArray();
                        if (ShouldIncludeOriginalState(originalDate, consolidatedDates))
                        {
                            var originalValidTo = consolidatedDates.Length == 0
                                ? (DateOnly?)null
                                : consolidatedDates.Min().AddDays(-1);
                            var originalLanguages = ConsolidationLanguages(
                                baseTitleRows, _scope.Languages);
                            var expressions = originalLanguages.Select(lang =>
                            {
                                var title = ExpressionTitle(lang, titles);
                                var sourceUri = ExpressionSourceUri(lang, baseCelex);
                                return new ExpressionRecord(lang, originalDate, originalValidTo, "publisher", title,
                                    OfficialDisplayTitle(title, baseCelex), sourceUri);
                            }).ToArray();
                            list.Insert(0, new VersionRecord(
                                new Identifier(workUri), new Identifier(workUri), typeCode, originalDate, originalValidTo,
                                "publisher", baseTitleRows.First().GetValueOrDefault("inforce"), originalDate,
                                expressions, [], SourceRaw(baseCelex, typeCode, bindingStatus,
                                    "original_official_expression"), publisherMetadata,
                                DocumentRoles(resourceType, amending, correcting, consolidated: false)));
                        }
                    }
                }

                _byWork[workUri] = list;
                _works[workUri] = new WorkRef(new Identifier(workUri), slug, typeCode,
                    OfficialDisplayTitle(baseTitle, baseCelex));
                Console.Error.WriteLine($"  [eurlex] {baseCelex}: {versions.Count} consolidation(s), {list.Count} temporal state(s)");
            }
            _loaded = true;
        }
        finally { _initLock.Release(); }
    }

    private async Task<Dictionary<string, List<Dictionary<string, string>>>> LoadConsolidationsAsync(
        IEnumerable<string> celexNumbers, CancellationToken ct)
    {
        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var chunk in MetadataWorkBatches(celexNumbers))
        {
            var rows = await SelectAsync(ConsolidationsQuery(chunk), ct);
            AddBoundedMetadataRows(result, rows, chunk, "consolidation");
        }
        await VerifyPortalExpressionFallbacksAsync(result, ct);
        return result;
    }

    internal async Task VerifyPortalExpressionFallbacksAsync(
        IReadOnlyDictionary<string, List<Dictionary<string, string>>> rowsByBase,
        CancellationToken ct)
    {
        var missing = rowsByBase.Values.SelectMany(rows => rows)
            .GroupBy(row => row.GetValueOrDefault("celex")
                ?? throw new InvalidDataException(
                    "Publisher consolidation row is missing its CELEX identity."),
                StringComparer.Ordinal)
            .Where(group => group.All(row => !row.ContainsKey("lang")))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        RequirePortalExpressionFallbackBudget(
            missing.Length, _scope.History.MaxVerifiedPortalFallbacks);

        if (missing.Length > 0)
            Console.Error.WriteLine(
                $"  [eurlex] verifying {missing.Length} dated states through the official portal fallback");

        foreach (var state in missing)
        {
            await VerifyPortalExpressionHeadAsync(state.Key, "en", ct);
            foreach (var row in state)
            {
                row["lang"] = "en";
                row["expression_source"] = "verified_eurlex_portal";
            }
        }

        if (missing.Length > 0)
            await VerifyPortalExpressionBodyAsync(missing[0].Key, "en", ct);
    }

    internal static void RequirePortalExpressionFallbackBudget(int count, int maximum)
    {
        if (count < 0 || maximum < 1 || count > maximum)
            throw new InvalidDataException(
                $"Publisher returned {count} dated states without an EN/FR expression; "
                + $"the bounded official-portal fallback permits {maximum}.");
    }

    private async Task VerifyPortalExpressionHeadAsync(
        string celex, string language, CancellationToken ct)
    {
        await PaceAsync(ct);
        var sourceUri = ExpressionSourceUri(language, celex);
        var sent = await SourceHttp.SendAsync(_http, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Head, sourceUri);
            request.Headers.Accept.ParseAdd("text/html");
            request.Headers.AcceptLanguage.ParseAdd(language);
            return request;
        }, RetryPolicy, ct, delay: _delay,
            completion: HttpCompletionOption.ResponseHeadersRead);
        using var response = sent.Response;
        if (sent.RetryExhausted || !IsVerifiedPortalHead(response, language))
            throw new SourceAcquisitionException(new SourceBuildIssue(
                "publisher_expression_unavailable",
                celex,
                sent.FailureDetail
                ?? $"The exact official EUR-Lex {language} expression failed its bounded HEAD verification."),
                sent.Attempts);
    }

    private async Task VerifyPortalExpressionBodyAsync(
        string celex, string language, CancellationToken ct)
    {
        var fetched = await FetchExactPortalBodyAsync(
            ExpressionSourceUri(language, celex), celex, language,
            RetryPolicy.MaximumAttempts, ct);
        if (fetched.Fetch.Bytes is null || !fetched.ExactIdentity)
            throw new SourceAcquisitionException(new SourceBuildIssue(
                "publisher_expression_unavailable",
                celex,
                fetched.IdentityFailureDetail
                ?? fetched.Fetch.FailureDetail
                ?? PortalIdentityFingerprint(fetched.Fetch, fetched.ExactIdentity)),
                fetched.Fetch.Attempts);
        Console.Error.WriteLine(
            $"  [eurlex] {celex}: strict portal GET preflight passed; "
            + PortalIdentityFingerprint(fetched.Fetch, exactIdentity: true));
    }

    internal static bool IsVerifiedPortalHead(
        HttpResponseMessage? response, string language)
    {
        if (response is null || !response.IsSuccessStatusCode
            || !string.Equals(response.Content.Headers.ContentType?.MediaType,
                "text/html", StringComparison.OrdinalIgnoreCase)
            || response.Content.Headers.ContentLength is > BodyCapBytes)
            return false;
        var contentLanguages = response.Content.Headers.ContentLanguage.ToArray();
        return contentLanguages.Length == 1
            && string.Equals(contentLanguages[0], language,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static string ConsolidationsQuery(IReadOnlyCollection<string> celexNumbers)
    {
        var values = MetadataValues(celexNumbers);
        var limit = MetadataQueryLimit(celexNumbers.Count);
        return Cdm + Owl + $$"""
                SELECT DISTINCT ?base ?celex ?date ?lang ?title WHERE {
                  VALUES (?base ?alias) { {{values}} }
                  ?baseWork owl:sameAs ?alias .
                  ?s cdm:act_consolidated_based_on_resource_legal ?baseWork ;
                     cdm:resource_legal_id_celex ?celex ; cdm:act_consolidated_date ?date .
                  OPTIONAL {
                    VALUES ?langUri {
                      <http://publications.europa.eu/resource/authority/language/ENG>
                      <http://publications.europa.eu/resource/authority/language/FRA>
                    }
                    ?e cdm:expression_belongs_to_work ?s ;
                       cdm:expression_uses_language ?langUri .
                    BIND(IF(STRENDS(STR(?langUri), "/FRA"), "fr", "en") AS ?lang)
                    OPTIONAL { ?e cdm:expression_title ?title }
                  }
                } ORDER BY ?base ?celex ?lang
                LIMIT {{limit}}
                """;
    }

    internal static IReadOnlyList<string> ConsolidationLanguages(
        IEnumerable<Dictionary<string, string>> rows,
        IReadOnlyList<string> configuredLanguages)
    {
        var observed = rows
            .Select(row => row.GetValueOrDefault("lang"))
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .ToHashSet(StringComparer.Ordinal);
        var selected = configuredLanguages.Where(observed.Contains).ToArray();
        return selected;
    }

    internal static string? ExpressionTitle(
        string language,
        IReadOnlyDictionary<string, string?> titles,
        IReadOnlyDictionary<string, string?>? fallbackTitles = null)
    {
        if (titles.TryGetValue(language, out var title) && !string.IsNullOrWhiteSpace(title))
            return title;

        return fallbackTitles is not null
            && fallbackTitles.TryGetValue(language, out var fallbackTitle)
            && !string.IsNullOrWhiteSpace(fallbackTitle)
                ? fallbackTitle
                : null;
    }

    private async Task<Dictionary<string, List<Dictionary<string, string>>>> LoadWorkMetadataAsync(
        IEnumerable<string> celexNumbers, CancellationToken ct)
    {
        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var chunk in MetadataWorkBatches(celexNumbers))
        {
            var rows = await SelectAsync(WorkMetadataQuery(chunk), ct);
            RequireBoundedWorkMetadataRows(rows);
            AddBoundedMetadataRows(result, rows, chunk, "work");
        }
        return result;
    }

    internal static string WorkMetadataQuery(IReadOnlyCollection<string> celexNumbers)
    {
        var values = MetadataValues(celexNumbers);
        var limit = MetadataQueryLimit(celexNumbers.Count);
        return Cdm + Owl + $$"""
                SELECT DISTINCT ?base ?lang ?title ?title_short ?date ?inforce ?rtype ?is_amending ?is_correcting WHERE {
                  VALUES (?base ?alias) { {{values}} }
                  ?w owl:sameAs ?alias .
                  OPTIONAL { ?w cdm:work_date_document ?documentDate }
                  OPTIONAL { ?w cdm:date_creation_legacy ?createdDate }
                  BIND(COALESCE(?documentDate, ?createdDate) AS ?date)
                  OPTIONAL { ?w cdm:resource_legal_in-force ?inforce }
                  OPTIONAL { ?w cdm:work_has_resource-type ?rtype }
                  BIND(EXISTS { ?w cdm:resource_legal_amends_resource_legal ?amendedWork } AS ?is_amending)
                  BIND(EXISTS { ?w cdm:resource_legal_corrects_resource_legal ?correctedWork } AS ?is_correcting)
                  ?e cdm:expression_belongs_to_work ?w ; cdm:expression_uses_language ?langUri .
                  OPTIONAL { ?e cdm:expression_title ?title }
                  OPTIONAL { ?e cdm:expression_title_short ?title_short }
                  VALUES ?langUri {
                    <http://publications.europa.eu/resource/authority/language/ENG>
                    <http://publications.europa.eu/resource/authority/language/FRA>
                  }
                  BIND(IF(STRENDS(STR(?langUri), "/FRA"), "fr", "en") AS ?lang)
                } ORDER BY ?base ?lang
                LIMIT {{limit}}
                """;
    }

    internal static void RequireBoundedWorkMetadataRows(
        IReadOnlyCollection<Dictionary<string, string>> rows)
    {
        foreach (var group in rows.GroupBy(row => RequiredBase(row, "work"), StringComparer.Ordinal))
            if (group.Count() > MetadataRowsPerWorkMaximum)
                throw new InvalidDataException(
                    $"Publisher work metadata for {group.Key} exceeds {MetadataRowsPerWorkMaximum} records.");
    }

    private async Task<Dictionary<string, List<Dictionary<string, string>>>> LoadPublisherMetadataAsync(
        IEnumerable<string> celexNumbers, CancellationToken ct)
    {
        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var chunk in MetadataWorkBatches(celexNumbers))
        {
            var rows = await SelectAsync(PublisherMetadataQuery(chunk), ct);
            RequireBoundedPublisherMetadataRows(rows);
            AddBoundedMetadataRows(result, rows, chunk, "publisher");
        }
        return result;
    }

    internal static string PublisherMetadataQuery(IReadOnlyCollection<string> celexNumbers)
    {
        var values = MetadataValues(celexNumbers);
        var limit = MetadataQueryLimit(celexNumbers.Count);
        return Cdm + Owl + Skos + EuroVoc + $$"""
                SELECT DISTINCT ?base ?kind ?identifier ?lang ?label WHERE {
                  VALUES (?base ?alias) { {{values}} }
                  ?w owl:sameAs ?alias .
                  {
                    ?w cdm:resource_legal_is_about_concept_directory-code ?identifier .
                    ?identifier skos:prefLabel ?label .
                    BIND("directory" AS ?kind)
                  } UNION {
                    ?w cdm:work_is_about_concept_eurovoc ?assigned .
                    GRAPH <http://eurovoc.europa.eu/100141> {
                      {
                        BIND(?assigned AS ?identifier)
                        ?assigned skos:prefLabel ?label .
                        BIND("eurovoc" AS ?kind)
                      } UNION {
                        BIND(?assigned AS ?identifier)
                        ?assigned skos:altLabel ?label .
                        BIND("eurovoc_alt_label" AS ?kind)
                      } UNION {
                        ?assigned skos:broader ?identifier .
                        ?identifier skos:prefLabel ?label .
                        BIND("eurovoc_broader" AS ?kind)
                      } UNION {
                        ?assigned skos:inScheme ?identifier .
                        ?identifier a eurovoc:MicroThesaurus ; skos:prefLabel ?label .
                        BIND("eurovoc_subdomain" AS ?kind)
                      } UNION {
                        ?assigned skos:inScheme ?subdomain .
                        ?subdomain a eurovoc:MicroThesaurus ; eurovoc:domain ?identifier .
                        ?identifier skos:prefLabel ?label .
                        BIND("eurovoc_domain" AS ?kind)
                      }
                    }
                  }
                  FILTER(LANG(?label) IN ("en", "fr"))
                  BIND(LANG(?label) AS ?lang)
                } ORDER BY ?base ?kind ?identifier ?lang ?label
                LIMIT {{limit}}
                """;
    }

    internal static IEnumerable<string[]> MetadataWorkBatches(IEnumerable<string> celexNumbers) =>
        celexNumbers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Chunk(MetadataWorkBatchSize);

    private static string MetadataValues(IReadOnlyCollection<string> celexNumbers)
    {
        if (celexNumbers.Count is < 1 or > MetadataWorkBatchSize)
            throw new ArgumentOutOfRangeException(nameof(celexNumbers));
        return string.Join(' ', celexNumbers.Select(celex =>
            $"(\"{celex}\" <{CelexAliasUri(celex)}>)"));
    }

    private static int MetadataQueryLimit(int workCount)
    {
        var limit = checked(workCount * MetadataRowsPerWorkMaximum + 1);
        return limit <= VirtuosoSortedTopMaximum
            ? limit
            : throw new InvalidOperationException("EU metadata batch exceeds the Virtuoso sorted-result window.");
    }

    private static void RequireBoundedPublisherMetadataRows(
        IReadOnlyCollection<Dictionary<string, string>> rows)
    {
        foreach (var group in rows.GroupBy(row => RequiredBase(row, "publisher"), StringComparer.Ordinal))
            if (group.Count() > MetadataRowsPerWorkMaximum)
                throw new InvalidDataException(
                    $"Publisher metadata for {group.Key} exceeds {MetadataRowsPerWorkMaximum} records.");
    }

    private static void AddBoundedMetadataRows(
        Dictionary<string, List<Dictionary<string, string>>> result,
        IReadOnlyCollection<Dictionary<string, string>> rows,
        IReadOnlyCollection<string> requestedCelex,
        string label)
    {
        var batchMaximum = checked(requestedCelex.Count * MetadataRowsPerWorkMaximum);
        if (rows.Count > batchMaximum)
            throw new InvalidDataException(
                $"Publisher {label} metadata for {requestedCelex.Count} EU works exceeds {batchMaximum} records.");
        var requested = requestedCelex.ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var baseCelex = RequiredBase(row, label);
            if (!requested.Contains(baseCelex))
                throw new InvalidDataException(
                    $"Publisher {label} metadata returned unrequested CELEX {baseCelex}.");
            if (!result.TryGetValue(baseCelex, out var list)) result[baseCelex] = list = [];
            if (list.Count >= MetadataRowsPerWorkMaximum)
                throw new InvalidDataException(
                    $"Publisher {label} metadata for {baseCelex} exceeds {MetadataRowsPerWorkMaximum} records.");
            list.Add(row);
        }
    }

    private static string RequiredBase(Dictionary<string, string> row, string label) =>
        row.TryGetValue("base", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Publisher {label} metadata row is missing base CELEX.");

    public async Task<EurLexScopePreview> PreviewScopeAsync(
        string? previousScopePath, DateTimeOffset observedAt, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        IReadOnlySet<string> previous = new HashSet<string>(StringComparer.Ordinal);
        if (previousScopePath is not null)
            previous = (await new EurLexAdapter(previousScopePath, _wave).ResolveScopeAsync(ct)).Keys.ToHashSet(StringComparer.Ordinal);
        var current = _selectionReasons.Keys.ToHashSet(StringComparer.Ordinal);
        var originals = _byWork.Values.SelectMany(v => v)
            .Where(v => v.Raw.GetValueOrDefault("consolidation_status") != "published").Sum(v => v.Expressions.Count);
        var consolidated = _byWork.Values.SelectMany(v => v)
            .Where(v => v.Raw.GetValueOrDefault("consolidation_status") == "published").Sum(v => v.Expressions.Count);
        var languages = _byWork.Values.SelectMany(v => v).SelectMany(v => v.Expressions)
            .GroupBy(e => e.Language, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var reasonCounts = _selectionReasons.Values.SelectMany(v => v)
            .GroupBy(v => v, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var missing = _byWork.Where(kv => kv.Value.All(v => v.Raw.GetValueOrDefault("consolidation_status") != "published"))
            .Select(kv => kv.Value.First().Raw.GetValueOrDefault("celex") ?? kv.Key)
            .Order(StringComparer.Ordinal).ToArray();
        var loadable = _works.Keys.Select(k => k[(k.IndexOf("/celex/", StringComparison.Ordinal) + 7)..])
            .ToHashSet(StringComparer.Ordinal);
        var metadataGaps = current.Except(loadable).Order(StringComparer.Ordinal).ToArray();
        var expressions = originals + consolidated;
        return new EurLexScopePreview(
            "lex-eu-scope-preview/1", _scope.ScopeId, _wave, "engineer-reviewed",
            observedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            current.Except(previous).Order(StringComparer.Ordinal).ToArray(),
            previous.Except(current).Order(StringComparer.Ordinal).ToArray(),
            current.Count, _works.Count, metadataGaps, originals, consolidated, languages, reasonCounts, missing,
            expressions * 256L * 1024L, expressions * 96L * 1024L,
            "planning estimate: 256 KiB download and 96 KiB lexical index per language expression; replace with measured bytes after the dry run");
    }

    private async Task<Dictionary<string, HashSet<string>>> ResolveScopeAsync(CancellationToken ct)
    {
        var selected = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string celex, string reason)
        {
            if (!IsAllowedCelex(celex, _wave)) return;
            if (!selected.TryGetValue(celex, out var reasons))
                selected[celex] = reasons = new HashSet<string>(StringComparer.Ordinal);
            reasons.Add(reason);
        }

        foreach (var domain in _scope.ActiveDomains(_wave))
        {
            foreach (var celex in domain.SeedCelex) Add(celex, $"domain:{domain.Id}:seed");
            foreach (var prefix in domain.DirectoryPrefixes)
            {
                var inForce = _wave < 3
                    ? " ; cdm:resource_legal_in-force \"true\"^^<http://www.w3.org/2001/XMLSchema#boolean>"
                    : "";
                var rows = await SelectAsync(Cdm + $$"""
                    SELECT DISTINCT ?celex WHERE {
                      ?w cdm:resource_legal_id_celex ?celex ;
                         cdm:resource_legal_is_about_concept_directory-code ?directory {{inForce}} .
                      FILTER(STRSTARTS(STRAFTER(STR(?directory), "/dir-eu-legal-act/"), "{{prefix}}"))
                      FILTER(REGEX(STR(?celex), "^3[0-9]{4}[RLD]"))
                    } LIMIT 20001
                    """, ct);
                if (rows.Count > 20000)
                    throw new InvalidOperationException($"Directory selector '{prefix}' exceeds 20,000 works; refine the reviewed scope.");
                foreach (var row in rows) Add(row["celex"], $"domain:{domain.Id}:directory:{prefix}");
            }
            foreach (var concept in domain.Eurovoc)
            {
                var rows = await SelectAsync(Cdm + $$"""
                    SELECT DISTINCT ?celex WHERE {
                      ?w cdm:resource_legal_id_celex ?celex ; cdm:resource_legal_type ?type ;
                         cdm:work_is_about_concept_eurovoc <http://eurovoc.europa.eu/{{concept}}> .
                      FILTER(STR(?type) IN ("R", "L", "D"))
                    } ORDER BY ?celex LIMIT 20001
                    """, ct);
                if (rows.Count > 20000)
                    throw new InvalidOperationException($"EuroVoc selector '{concept}' exceeds 20,000 works; refine the reviewed scope.");
                foreach (var row in rows) Add(row["celex"], $"domain:{domain.Id}:eurovoc:{concept}");
            }
        }

        var predicates = _scope.RelationshipClosure.Predicates.ToList();
        if (_wave >= _scope.RelationshipClosure.IncludeCaseLawFromWave)
            predicates.AddRange(_scope.RelationshipClosure.CaseLawPredicates);
        for (var depth = 0; depth < _scope.RelationshipClosure.MaxDepth; depth++)
        {
            var frontier = selected.Keys.ToArray();
            var before = selected.Count;
            foreach (var chunk in frontier.Chunk(50))
            {
                var rows = await SelectAsync(RelationshipClosureQuery(
                    chunk, predicates, _scope.Languages), ct);
                foreach (var group in rows.GroupBy(r => r["seedCelex"], StringComparer.Ordinal))
                    if (group.Select(r => r["relatedCelex"]).Distinct(StringComparer.Ordinal).Count()
                        > _scope.RelationshipClosure.MaxRelatedPerSeed)
                        throw new InvalidOperationException(
                            $"Relationship closure for '{group.Key}' exceeds the reviewed per-seed limit.");
                foreach (var row in rows)
                {
                    var predicate = row["predicate"].Split('#').Last();
                    Add(row["relatedCelex"], $"relationship:{predicate}");
                    if (selected.TryGetValue(row["seedCelex"], out var parentReasons))
                        foreach (var domain in parentReasons.Where(r => r.StartsWith("domain:", StringComparison.Ordinal))
                                     .Select(r => r.Split(':')[1]).Distinct(StringComparer.Ordinal).ToArray())
                            Add(row["relatedCelex"], $"domain:{domain}:relationship:{predicate}");
                }
            }
            if (selected.Count > _scope.RelationshipClosure.MaxTotalWorks)
                throw new InvalidOperationException("Relationship closure exceeds the reviewed total-work limit.");
            if (selected.Count == before) break;
        }
        return selected;
    }

    internal static string RelationshipClosureQuery(
        IReadOnlyList<string> seedCelex,
        IReadOnlyList<string> predicates,
        IReadOnlyList<string> languages)
    {
        var values = string.Join(' ', seedCelex.Select(c =>
            $"(\"{c}\" <{CelexAliasUri(c)}>)"));
        var predicateValues = string.Join(' ', predicates.Select(p => $"cdm:{p}"));
        var languageValues = string.Join(' ', languages.Select(language => language switch
        {
            "en" => "<http://publications.europa.eu/resource/authority/language/ENG>",
            "fr" => "<http://publications.europa.eu/resource/authority/language/FRA>",
            _ => throw new InvalidDataException($"Unsupported EU scope language '{language}'."),
        }));
        return Cdm + Owl + $$"""
            SELECT DISTINCT ?seedCelex ?relatedCelex ?predicate WHERE {
              VALUES (?seedCelex ?seedAlias) { {{values}} }
              VALUES ?predicate { {{predicateValues}} }
              ?seed owl:sameAs ?seedAlias .
              {
                ?seed ?predicate ?related .
                BIND("outbound" AS ?direction)
              } UNION {
                ?related ?predicate ?seed .
                BIND("inbound" AS ?direction)
              }
              ?related cdm:resource_legal_id_celex ?relatedCelex .
              ?relatedExpression cdm:expression_belongs_to_work ?related ;
                                 cdm:expression_uses_language ?relatedLanguage .
              VALUES ?relatedLanguage { {{languageValues}} }
              FILTER(
                ?predicate != cdm:resource_legal_based_on_resource_legal
                || ?direction = "outbound"
                || (!STRSTARTS(STR(?seedCelex), "1")
                    && REGEX(STR(?relatedCelex), "^3[0-9]{4}[RL]")))
            }
            """;
    }

    private bool IsAllowedCelex(string celex, int wave)
    {
        if (celex.Length < 5) return false;
        if (wave >= 4 && celex[0] == '6') return true;
        if (celex[0] is not ('1' or '3')) return false;
        return _scope.LegalForms.Contains(LegalForm(celex, null), StringComparer.Ordinal);
    }

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(
        value[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static string NormalizeWorkSlug(string celex) =>
        celex.ToLowerInvariant().Replace('/', '-');

    public static string CellarResourceUrl(string celex) =>
        $"https://publications.europa.eu/resource/celex/{Uri.EscapeDataString(celex)}";

    internal static string ExpressionSourceUri(string language, string celex) =>
        $"https://eur-lex.europa.eu/legal-content/{language.ToUpperInvariant()}/TXT/"
        + $"?uri=CELEX:{Uri.EscapeDataString(celex)}";

    internal static bool IsExactPortalExpression(
        string html, string celex, string language) =>
        IsExactPortalExpression(html, celex, language,
            static value => HtmlNonStructuralContent.Replace(value, ""));

    internal static bool IsExactPortalExpression(
        string html, string celex, string language,
        Func<string, string> removeNonStructuralContent)
    {
        try
        {
            return IsExactPortalExpressionCore(
                html, celex, language, removeNonStructuralContent);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsExactPortalExpressionCore(
        string html, string celex, string language,
        Func<string, string> removeNonStructuralContent)
    {
        if (string.IsNullOrWhiteSpace(html)
            || string.IsNullOrWhiteSpace(celex)
            || string.IsNullOrWhiteSpace(language))
            return false;
        var markup = removeNonStructuralContent(html);
        if (markup.Contains("<!--", StringComparison.Ordinal)
            || markup.Contains("-->", StringComparison.Ordinal)
            || HtmlInertTag.IsMatch(markup))
            return false;

        var identityTags = new List<System.Text.RegularExpressions.Match>();
        var contentRoots = new List<System.Text.RegularExpressions.Match>();
        foreach (System.Text.RegularExpressions.Match tag in HtmlOpeningTag.Matches(markup))
        {
            var attributes = tag.Groups["attributes"].Value;
            var properties = HtmlAttributeValues(attributes, "property");
            if (properties.SelectMany(value => value.Split(
                        (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    .Any(value => string.Equals(
                        value, "eli:id_local", StringComparison.Ordinal)))
                identityTags.Add(tag);

            var ids = HtmlAttributeValues(attributes, "id");
            if (ids.Any(value => string.Equals(
                    value, "PP1Contents", StringComparison.OrdinalIgnoreCase)))
                contentRoots.Add(tag);

            if (identityTags.Count > 1 || contentRoots.Count > 1) return false;
        }

        if (identityTags.Count != 1 || contentRoots.Count != 1) return false;
        if (!string.Equals(identityTags[0].Groups["name"].Value,
                "meta", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(contentRoots[0].Groups["name"].Value,
                "div", StringComparison.OrdinalIgnoreCase))
            return false;
        var identityAttributes = identityTags[0].Groups["attributes"].Value;
        var identityProperties = HtmlAttributeValues(identityAttributes, "property");
        var identityContents = HtmlAttributeValues(identityAttributes, "content");
        if (identityProperties.Count != 1 || identityContents.Count != 1
            || !string.Equals(identityContents[0], celex, StringComparison.Ordinal))
            return false;

        var rootAttributes = contentRoots[0].Groups["attributes"].Value;
        if (HtmlAttributeValues(rootAttributes, "id").Count != 1) return false;
        var child = FirstContentElement(markup,
            contentRoots[0].Index + contentRoots[0].Length);
        if (child is null) return false;
        if (!string.Equals(child.Groups["name"].Value,
                "div", StringComparison.OrdinalIgnoreCase))
            return false;
        var selectedLanguages = HtmlAttributeValues(
            child.Groups["attributes"].Value, "lang");
        return selectedLanguages.Count == 1
               && string.Equals(selectedLanguages[0], language,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static readonly System.Text.RegularExpressions.Regex HtmlOpeningTag = new(
        "<(?<name>[A-Za-z][A-Za-z0-9:-]*)\\b(?<attributes>[^<>]*?)>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex HtmlNonStructuralContent = new(
        "<!--.*?-->|<(?<raw>script|style|title|textarea|template|noscript|iframe|noembed|noframes|xmp|listing)"
        + "\\b[^>]*>.*?</\\k<raw>\\s*>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.Singleline
        | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(10));

    private static readonly System.Text.RegularExpressions.Regex HtmlInertTag = new(
        "</?(?:script|style|title|textarea|template|noscript|iframe|noembed|noframes|xmp|listing|plaintext)\\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static IReadOnlyList<string> HtmlAttributeValues(
        string attributes, string attributeName)
    {
        var pattern = $"(?:^|\\s){System.Text.RegularExpressions.Regex.Escape(attributeName)}"
                      + "\\s*=\\s*(?:\\\"(?<double>[^\\\"]*)\\\"|'(?<single>[^']*)'"
                      + "|(?<bare>[^\\s\\\"'=<>`]+))";
        return System.Text.RegularExpressions.Regex.Matches(
                attributes, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Select(match => match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["bare"].Value)
            .Select(value => System.Net.WebUtility.HtmlDecode(value) ?? "")
            .ToArray();
    }

    private static System.Text.RegularExpressions.Match? FirstContentElement(
        string html, int position)
    {
        while (position < html.Length && char.IsWhiteSpace(html[position])) position++;
        if (position >= html.Length || html[position] != '<') return null;
        var child = HtmlOpeningTag.Match(html, position);
        return child.Success && child.Index == position ? child : null;
    }

    public static string CelexAliasUri(string celex) =>
        $"http://publications.europa.eu/resource/celex/{Uri.EscapeDataString(celex)}";

    public static bool ShouldIncludeOriginalState(
        DateOnly originalDate, IEnumerable<DateOnly> consolidatedDates)
    {
        var dates = consolidatedDates.ToArray();
        return dates.Length == 0 || originalDate < dates.Min();
    }

    private static string LegalForm(string celex, string? resourceType)
    {
        if (resourceType is not null) return resourceType.Split('/').Last();
        if (celex.StartsWith('1')) return celex.Contains('P') ? "CHARTER" : "TREATY";
        return celex.Length > 5 ? celex[5] switch
        {
            'R' => "REG",
            'L' => "DIR",
            'D' => "DEC",
            _ => "OTHER",
        } : "OTHER";
    }

    public static string NormalizeBindingStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => "in_force",
        "false" or "0" => "not_in_force",
        _ => "unknown",
    };

    internal static IReadOnlyList<PublisherMetadataRecord> BuildPublisherMetadata(
        string baseCelex,
        IReadOnlyList<Dictionary<string, string>> titleRows,
        IReadOnlyList<Dictionary<string, string>> subjectRows)
    {
        var values = new List<PublisherMetadataRecord>();
        foreach (var row in titleRows.Where(row => row.TryGetValue("title_short", out var value)
                                                   && !string.IsNullOrWhiteSpace(value)))
        {
            var language = row.GetValueOrDefault("lang");
            var label = row["title_short"];
            if (language is not ("en" or "fr") || label.Length > 4096)
                throw new InvalidDataException(
                    "EUR-Lex publisher short title has invalid language or length.");
            values.Add(new PublisherMetadataRecord(
                "publisher_short_title",
                ShortTitlePredicate,
                language,
                label,
                $"https://eur-lex.europa.eu/legal-content/{language.ToUpperInvariant()}/TXT/?uri=CELEX:{Uri.EscapeDataString(baseCelex)}"));
        }
        foreach (var row in subjectRows)
        {
            var kind = row.GetValueOrDefault("kind");
            var identifier = row.GetValueOrDefault("identifier");
            if (kind is not ("eurovoc" or "directory" or "eurovoc_alt_label"
                    or "eurovoc_broader" or "eurovoc_subdomain" or "eurovoc_domain")
                || string.IsNullOrWhiteSpace(identifier)
                || !Uri.TryCreate(identifier, UriKind.Absolute, out var identifierUri)
                || identifierUri.Scheme is not ("http" or "https"))
                throw new InvalidDataException("EUR-Lex publisher metadata has an invalid kind or identifier.");
            var language = row.GetValueOrDefault("lang");
            var label = row.GetValueOrDefault("label");
            if (language is not ("en" or "fr") || string.IsNullOrWhiteSpace(label)
                || label.Length > 4096)
                throw new InvalidDataException(
                    "EUR-Lex publisher metadata has an invalid language or label.");
            values.Add(new PublisherMetadataRecord(
                kind,
                identifier,
                language,
                label,
                identifier));
        }
        var canonical = values.Distinct()
            .OrderBy(value => value.Kind, StringComparer.Ordinal)
            .ThenBy(value => value.Identifier, StringComparer.Ordinal)
            .ThenBy(value => value.Language, StringComparer.Ordinal)
            .ThenBy(value => value.Label, StringComparer.Ordinal)
            .ToArray();
        if (canonical.Length > 512)
            throw new InvalidDataException(
                $"EUR-Lex work {baseCelex} exceeds 512 publisher metadata records.");
        return canonical;
    }

    internal static IReadOnlyList<string> DocumentRoles(
        string? resourceType, bool amending, bool correcting, bool consolidated)
    {
        var code = resourceType?.Split('/').LastOrDefault() ?? "";
        var roles = new HashSet<string>(StringComparer.Ordinal);
        if (code.EndsWith("_DEL", StringComparison.Ordinal)) roles.Add("delegated");
        if (code.EndsWith("_IMPL", StringComparison.Ordinal)) roles.Add("implementing");
        if (code.Contains("CORR", StringComparison.Ordinal)) roles.Add("corrigendum");
        if (amending) roles.Add("amending");
        if (correcting) roles.Add("corrigendum");
        if (consolidated) roles.Add("consolidated");
        return roles.Order(StringComparer.Ordinal).ToArray();
    }

    internal static Dictionary<string, string> SourceRaw(
        string celex, string legalForm, string bindingStatus, string consolidationStatus) =>
        new(StringComparer.Ordinal)
        {
            ["celex"] = celex,
            ["legal_form"] = legalForm,
            ["hierarchy"] = celex.StartsWith('1') ? "primary_eu_law" : "secondary_eu_law",
            ["binding_status"] = bindingStatus,
            ["consolidation_status"] = consolidationStatus,
        };

    private sealed record ConsolidatedState(
        string Celex,
        DateOnly Date,
        IReadOnlyDictionary<string, string?> Titles,
        IReadOnlyList<string> Languages,
        bool PortalBound);

    internal sealed record ConsolidatedCoordinate(string Celex, DateOnly Date, DateOnly? ValidTo);

    internal static IReadOnlyList<ConsolidatedCoordinate> ConsolidatedCoordinates(
        IEnumerable<(string Celex, DateOnly Date)> states)
    {
        var ordered = states.OrderBy(state => state.Date)
            .ThenBy(state => state.Celex, StringComparer.Ordinal).ToArray();
        var result = new List<ConsolidatedCoordinate>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            DateOnly? validTo = null;
            for (var later = index + 1; later < ordered.Length; later++)
                if (ordered[later].Date > ordered[index].Date)
                {
                    validTo = ordered[later].Date.AddDays(-1);
                    break;
                }
            result.Add(new(ordered[index].Celex, ordered[index].Date, validTo));
        }
        return result;
    }

    private static string? ShortTitle(string? title)
    {
        if (title is null) return null;
        var cut = title.IndexOf(" of the European Parliament", StringComparison.Ordinal);
        return cut > 0 ? title[..cut] : title.Length > 90 ? title[..90] + "…" : title;
    }

    internal static string OfficialDisplayTitle(string? title, string fallback) =>
        ShortTitle(title) ?? fallback;

    private async Task<List<Dictionary<string, string>>> SelectAsync(string query, CancellationToken ct)
    {
        await PaceAsync(ct);
        var sent = await SourceHttp.SendAsync(_http, () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Sparql)
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("query", query)]),
            };
            req.Headers.Accept.ParseAdd("application/sparql-results+json");
            return req;
        }, RetryPolicy, ct, delay: _delay,
            completion: HttpCompletionOption.ResponseContentRead);
        using var resp = sent.Response;
        if (resp is null || sent.RetryExhausted || !resp.IsSuccessStatusCode)
            throw new SourceAcquisitionException(new SourceBuildIssue(
                sent.RetryExhausted ? "enumeration_retry_exhausted" : "enumeration_http_failure",
                "eu-eurlex",
                sent.FailureDetail ?? $"The official SPARQL endpoint returned HTTP {(int?)resp?.StatusCode}."),
                sent.Attempts);
        try
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var rows = new List<Dictionary<string, string>>();
            foreach (var b in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
            {
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in b.EnumerateObject())
                    row[p.Name] = p.Value.GetProperty("value").GetString() ?? "";
                rows.Add(row);
            }
            return rows;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new SourceAcquisitionException(new SourceBuildIssue(
                "enumeration_parser_failure", "eu-eurlex", ex.Message), sent.Attempts);
        }
    }
}
