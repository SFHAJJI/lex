using System.Runtime.CompilerServices;
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
public sealed class EurLexAdapter : ISourceAdapter
{
    private const string Sparql = "https://publications.europa.eu/webapi/rdf/sparql";
    private const string Cdm = "PREFIX cdm: <http://publications.europa.eu/ontology/cdm#>\n";
    private const string Owl = "PREFIX owl: <http://www.w3.org/2002/07/owl#>\n";
    private const string Skos = "PREFIX skos: <http://www.w3.org/2004/02/skos/core#>\n";
    private const string ShortTitlePredicate =
        "http://publications.europa.eu/ontology/cdm#expression_title_short";
    // Primary XHTML is the searchable legal wording. Large annex-heavy acts legitimately exceed
    // 4 MiB (Regulation 1791/2006 is about 8.7 MiB), so give it a separate offline-ingest budget.
    // Optional Formex archives stay more tightly bounded and are guarded again after expansion.
    private const int BodyCapBytes = 32 * 1024 * 1024;
    private const int FormexArchiveCapBytes = 32 * 1024 * 1024;
    private const long FormexMemberCapBytes = 64 * 1024 * 1024;
    private const long FormexExpandedCapBytes = 128 * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private readonly Dictionary<string, List<VersionRecord>> _byWork = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkRef> _works = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _selectionReasons = new(StringComparer.Ordinal);
    private readonly EurLexScopeConfig _scope;
    private readonly int _wave;
    private bool _loaded;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public EurLexAdapter(string? scopePath = null, int? wave = null)
    {
        _scope = EurLexScopeConfig.Load(scopePath ?? Environment.GetEnvironmentVariable("LEX_EU_SCOPE"));
        _wave = wave ?? _scope.ApprovedWave;
        if (_wave is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(wave), "EU scope wave must be between 1 and 4.");
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lex/0.1");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/SFHAJJI/lex)");
        return c;
    }

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

    public async Task<string?> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
    {
        var celex = version.Raw.GetValueOrDefault("celex");
        if (celex is null) return null;

        // Cellar is canonical, but a small number of language-specific corrigenda return 404
        // there while the official EUR-Lex expression URI serves the XHTML. Retry that URI only
        // when it remains on an EU institutional host; no third-party fallback can become evidence.
        var urls = new List<string> { CellarResourceUrl(celex) };
        if (OfficialEuUri(expression.SourceUri) is { } expressionUri &&
            !string.Equals(expressionUri, urls[0], StringComparison.OrdinalIgnoreCase))
            urls.Add(expressionUri);

        FetchResult? last = null;
        for (var attempt = 0; attempt < urls.Count; attempt++)
        {
            await PaceAsync(ct);
            last = await FetchBytes(urls[attempt], expression.Language,
                "application/xhtml+xml, text/html", BodyCapBytes, ct);
            if (last.Bytes is null) continue;

            var fallback = attempt == 0 ? "" : " (official EUR-Lex fallback)";
            Console.Error.WriteLine($"  [eurlex] {celex}: body {last.Bytes.Length / 1024} KB{fallback}");
            return System.Text.Encoding.UTF8.GetString(last.Bytes);
        }

        if (last?.LimitExceeded == true)
            Console.Error.WriteLine($"  [eurlex] {celex}: official body exceeds {BodyCapBytes / 1024 / 1024} MB cap — metadata only");
        else
            Console.Error.WriteLine($"  [eurlex] {celex}: official body unavailable after {urls.Count} endpoint(s) (last status {(int?)last?.StatusCode})");
        return null;
    }

    private static async Task<FetchResult> FetchBytes(
        string initialUrl,
        string language,
        string accept,
        int capBytes,
        CancellationToken ct)
    {
        var url = initialUrl;
        HttpResponseMessage? resp = null;
        for (var hop = 0; hop < 6; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept", accept);
            req.Headers.AcceptLanguage.ParseAdd(language);
            resp?.Dispose();
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
            {
                var next = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(url), loc).ToString();
                url = next.StartsWith("http://", StringComparison.Ordinal) ? "https://" + next["http://".Length..] : next;
                if (OfficialEuUri(url) is null)
                {
                    resp.Dispose();
                    return new FetchResult(null, null, false, url);
                }
                continue;
            }
            break;
        }
        if (resp is null || !resp.IsSuccessStatusCode)
        {
            var status = resp?.StatusCode;
            resp?.Dispose();
            return new FetchResult(null, status, false, url);
        }
        using var _ = resp;
        if (resp.Content.Headers.ContentLength is { } contentLength && contentLength > capBytes)
            return new FetchResult(null, resp.StatusCode, true, url);

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var bounded = await ReadBounded(stream, capBytes, ct);
        return new FetchResult(bounded.Bytes, resp.StatusCode, bounded.LimitExceeded, url,
            resp.Content.Headers.ContentType?.MediaType);
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
    public async Task<ManifestationFetch?> FetchAltManifestation(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
    {
        // Formex identity checks require an official consolidated CONSLEG expression.
        // Original and unconsolidated acts do not contain INFO.CONSLEG, so requesting
        // their optional Formex representation can never produce an accepted result.
        if (version.Raw.TryGetValue("consolidation_status", out var status) && status != "published")
            return null;

        var celex = version.Raw.GetValueOrDefault("celex");
        if (celex is null) return null;
        await PaceAsync(ct);
        // Cellar's negotiation parser requires the exact spaceless mtype parameter.
        var fetched = await FetchBytes(CellarResourceUrl(celex), expression.Language,
            "application/zip;mtype=fmx4", FormexArchiveCapBytes, ct);
        if (fetched.Bytes is null || fetched.MediaType is not "application/zip")
        {
            if (fetched.LimitExceeded)
                Console.Error.WriteLine($"  [eurlex] {celex}: optional fmx4 exceeds {FormexArchiveCapBytes / 1024 / 1024} MB archive cap — skipped");
            return null;
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
                    return null;
                }
                await using var es = entry.Open();
                var member = await ReadBounded(es, checked((int)Math.Min(FormexMemberCapBytes,
                    FormexExpandedCapBytes - expandedBytes)), ct);
                if (member.Bytes is null)
                {
                    Console.Error.WriteLine($"  [eurlex] {celex}: optional fmx4 member exceeds safe limit — skipped");
                    return null;
                }
                expandedBytes += member.Bytes.LongLength;
                members.Add(new ManifestationMember(name, member.Bytes));
            }
        }
        catch (InvalidDataException)
        {
            Console.Error.WriteLine($"  [eurlex] {celex}: fmx4 response is not a readable zip — skipped");
            return null;
        }
        if (members.Count == 0) return null;

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
            return null;
        }

        Console.Error.WriteLine($"  [eurlex] {celex}: fmx4 {members.Count} member(s), {members.Sum(x => x.Bytes.LongLength) / 1024} KB");
        return new ManifestationFetch("fmx4", members, fetched.SourceUri);
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
        string? MediaType = null);

    private async Task PaceAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow - _lastRequest;
        var pause = TimeSpan.FromMilliseconds(1500);
        if (since < pause) await Task.Delay(pause - since, ct);
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
                    .Select(g => new ConsolidatedState(
                        g.Key,
                        ParseDate(g.First()["date"]),
                        g.Where(r => r.ContainsKey("lang"))
                            .GroupBy(r => r["lang"], StringComparer.Ordinal)
                            .ToDictionary(x => x.Key, x => x.First().GetValueOrDefault("title"), StringComparer.Ordinal)))
                    .OrderBy(v => v.Date).ThenBy(v => v.Celex, StringComparer.Ordinal)
                    .ToList();

                var list = new List<VersionRecord>();
                for (var i = 0; i < versions.Count; i++)
                {
                    var state = versions[i];
                    var celex = state.Celex;
                    var date = state.Date;
                    DateOnly? validTo = i + 1 < versions.Count ? versions[i + 1].Date.AddDays(-1) : null;
                    var expressions = _scope.Languages.Select(lang =>
                    {
                        var title = state.Titles.GetValueOrDefault(lang) ?? titles.GetValueOrDefault(lang) ?? baseTitle;
                        var sourceUri = $"https://eur-lex.europa.eu/legal-content/{lang.ToUpperInvariant()}/TXT/?uri=CELEX:{Uri.EscapeDataString(celex)}";
                        return new ExpressionRecord(lang, date, validTo, "publisher", title,
                            OfficialDisplayTitle(title, celex), sourceUri);
                    }).ToArray();
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
                        Raw: ScopeRaw(celex, typeCode, bindingStatus, "published", reasons),
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
                            var expressions = _scope.Languages.Select(lang =>
                            {
                                var title = titles.GetValueOrDefault(lang) ?? baseTitle;
                                var sourceUri = $"https://eur-lex.europa.eu/legal-content/{lang.ToUpperInvariant()}/TXT/?uri=CELEX:{Uri.EscapeDataString(baseCelex)}";
                                return new ExpressionRecord(lang, originalDate, originalValidTo, "publisher", title,
                                    OfficialDisplayTitle(title, baseCelex), sourceUri);
                            }).ToArray();
                            list.Insert(0, new VersionRecord(
                                new Identifier(workUri), new Identifier(workUri), typeCode, originalDate, originalValidTo,
                                "publisher", baseTitleRows.First().GetValueOrDefault("inforce"), originalDate,
                                expressions, [], ScopeRaw(baseCelex, typeCode, bindingStatus,
                                    "original_official_expression", reasons), publisherMetadata,
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
        foreach (var chunk in celexNumbers.Chunk(100))
        {
            var values = string.Join(' ', chunk.Select(c =>
                $"(\"{c}\" <{CelexAliasUri(c)}>)"));
            var rows = await SelectAsync(Cdm + Owl + $$"""
                SELECT ?base ?celex ?date ?lang ?title WHERE {
                  VALUES (?base ?alias) { {{values}} }
                  ?baseWork owl:sameAs ?alias .
                  ?s cdm:act_consolidated_based_on_resource_legal ?baseWork ;
                     cdm:resource_legal_id_celex ?celex ; cdm:act_consolidated_date ?date .
                  ?e cdm:expression_belongs_to_work ?s ; cdm:expression_uses_language ?langUri .
                  VALUES ?langUri {
                    <http://publications.europa.eu/resource/authority/language/ENG>
                    <http://publications.europa.eu/resource/authority/language/FRA>
                  }
                  BIND(IF(STRENDS(STR(?langUri), "/FRA"), "fr", "en") AS ?lang)
                  OPTIONAL { ?e cdm:expression_title ?title }
                } ORDER BY ?base ?celex
                """, ct);
            foreach (var row in rows)
            {
                var baseCelex = row["base"];
                if (!result.TryGetValue(baseCelex, out var list)) result[baseCelex] = list = [];
                list.Add(row);
            }
        }
        return result;
    }

    private async Task<Dictionary<string, List<Dictionary<string, string>>>> LoadWorkMetadataAsync(
        IEnumerable<string> celexNumbers, CancellationToken ct)
    {
        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var chunk in celexNumbers.Chunk(100))
        {
            var values = string.Join(' ', chunk.Select(c =>
                $"(\"{c}\" <{CelexAliasUri(c)}>)"));
            var rows = await SelectAsync(Cdm + Owl + $$"""
                SELECT ?base ?lang ?title ?title_short ?date ?inforce ?rtype ?is_amending ?is_correcting WHERE {
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
                """, ct);
            foreach (var row in rows)
            {
                var baseCelex = row["base"];
                if (!result.TryGetValue(baseCelex, out var list)) result[baseCelex] = list = [];
                list.Add(row);
            }
        }
        return result;
    }

    private async Task<Dictionary<string, List<Dictionary<string, string>>>> LoadPublisherMetadataAsync(
        IEnumerable<string> celexNumbers, CancellationToken ct)
    {
        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var chunk in celexNumbers.Chunk(100))
        {
            var values = string.Join(' ', chunk.Select(c =>
                $"(\"{c}\" <{CelexAliasUri(c)}>)"));
            var rows = await SelectAsync(Cdm + Owl + Skos + $$"""
                SELECT DISTINCT ?base ?kind ?identifier ?lang ?label WHERE {
                  VALUES (?base ?alias) { {{values}} }
                  ?w owl:sameAs ?alias .
                  {
                    ?w cdm:work_is_about_concept_eurovoc ?identifier .
                    BIND("eurovoc" AS ?kind)
                  } UNION {
                    ?w cdm:resource_legal_is_about_concept_directory-code ?identifier .
                    BIND("directory" AS ?kind)
                  }
                  OPTIONAL {
                    ?identifier skos:prefLabel ?label .
                    FILTER(LANG(?label) IN ("en", "fr"))
                  }
                  BIND(IF(BOUND(?label), LANG(?label), "") AS ?lang)
                } ORDER BY ?base ?kind ?identifier ?lang ?label
                """, ct);
            foreach (var row in rows)
            {
                var baseCelex = row["base"];
                if (!result.TryGetValue(baseCelex, out var list)) result[baseCelex] = list = [];
                list.Add(row);
            }
        }
        return result;
    }

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
                var values = string.Join(' ', chunk.Select(c =>
                    $"(\"{c}\" <{CelexAliasUri(c)}>)"));
                var predicateValues = string.Join(' ', predicates.Select(p => $"cdm:{p}"));
                var rows = await SelectAsync(Cdm + Owl + $$"""
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
                      FILTER(
                        ?predicate != cdm:resource_legal_based_on_resource_legal
                        || ?direction = "outbound"
                        || (!STRSTARTS(STR(?seedCelex), "1")
                            && REGEX(STR(?relatedCelex), "^3[0-9]{4}[RL]")))
                    }
                    """, ct);
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

    private bool IsAllowedCelex(string celex, int wave)
    {
        if (celex.Length < 5) return false;
        if (wave >= 4 && celex[0] == '6') return true;
        if (celex[0] is not ('1' or '3')) return false;
        return _scope.LegalForms.Contains(LegalForm(celex, null), StringComparer.Ordinal);
    }

    private static DateOnly ParseDate(string value) => DateOnly.Parse(value[..10]);

    public static string NormalizeWorkSlug(string celex) =>
        celex.ToLowerInvariant().Replace('/', '-');

    public static string CellarResourceUrl(string celex) =>
        $"https://publications.europa.eu/resource/celex/{Uri.EscapeDataString(celex)}";

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
            if (string.IsNullOrWhiteSpace(language)) continue;
            values.Add(new PublisherMetadataRecord(
                "publisher_short_title",
                ShortTitlePredicate,
                language,
                row["title_short"],
                $"https://eur-lex.europa.eu/legal-content/{language.ToUpperInvariant()}/TXT/?uri=CELEX:{Uri.EscapeDataString(baseCelex)}"));
        }
        foreach (var row in subjectRows)
        {
            var kind = row.GetValueOrDefault("kind");
            var identifier = row.GetValueOrDefault("identifier");
            if (kind is not ("eurovoc" or "directory") || string.IsNullOrWhiteSpace(identifier))
                continue;
            var language = row.GetValueOrDefault("lang");
            values.Add(new PublisherMetadataRecord(
                kind,
                identifier,
                string.IsNullOrWhiteSpace(language) ? null : language,
                row.GetValueOrDefault("label"),
                identifier));
        }
        return values.Distinct()
            .OrderBy(value => value.Kind, StringComparer.Ordinal)
            .ThenBy(value => value.Identifier, StringComparer.Ordinal)
            .ThenBy(value => value.Language, StringComparer.Ordinal)
            .ThenBy(value => value.Label, StringComparer.Ordinal)
            .ToArray();
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

    private static Dictionary<string, string> ScopeRaw(
        string celex, string legalForm, string bindingStatus, string consolidationStatus,
        IEnumerable<string> reasons)
    {
        var reasonList = reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var domains = reasonList.Where(r => r.StartsWith("domain:", StringComparison.Ordinal))
            .Select(r => r.Split(':')[1]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["celex"] = celex,
            ["legal_form"] = legalForm,
            ["hierarchy"] = celex.StartsWith('1') ? "primary_eu_law" : "secondary_eu_law",
            ["binding_status"] = bindingStatus,
            ["consolidation_status"] = consolidationStatus,
            ["domains"] = string.Join(',', domains),
            ["scope_reasons"] = string.Join(',', reasonList),
        };
    }

    private sealed record ConsolidatedState(string Celex, DateOnly Date, IReadOnlyDictionary<string, string?> Titles);

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
        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("query", query)]);
        using var req = new HttpRequestMessage(HttpMethod.Post, Sparql) { Content = content };
        req.Headers.Accept.ParseAdd("application/sparql-results+json");
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var rows = new List<Dictionary<string, string>>();
        foreach (var b in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in b.EnumerateObject()) row[p.Name] = p.Value.GetProperty("value").GetString() ?? "";
            rows.Add(row);
        }
        return rows;
    }
}
