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
    private const int BodyCapBytes = 4 * 1024 * 1024;   // versions above this keep metadata only

    // Flagship set for increment B. DocumentType is data (§3.5): derived from the
    // publisher's own CELEX typology inside this adapter (R = regulation, L = directive).
    private static readonly string[] Flagships =
    [
        "32016R0679", // GDPR
        "32022R2554", // DORA
        "32024R1689", // AI Act
        "32022L2555", // NIS2
        "32014L0065", // MiFID II
        "32013R0575", // CRR
        "32015L2366", // PSD2
        "32019R2088", // SFDR
    ];

    // Common names in universal professional use — adapter-provided display aliases,
    // never a substitute for the publisher's own title (kept verbatim in Title).
    private static readonly Dictionary<string, string> CommonNames = new(StringComparer.Ordinal)
    {
        ["32016R0679"] = "GDPR",
        ["32022R2554"] = "DORA",
        ["32024R1689"] = "AI Act",
        ["32022L2555"] = "NIS2",
        ["32014L0065"] = "MiFID II",
        ["32013R0575"] = "CRR",
        ["32015L2366"] = "PSD2",
        ["32019R2088"] = "SFDR",
    };

    private static readonly HttpClient Http = CreateClient();
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private readonly Dictionary<string, List<VersionRecord>> _byWork = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkRef> _works = new(StringComparer.Ordinal);
    private bool _loaded;
    private readonly SemaphoreSlim _initLock = new(1, 1);

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
        Languages: ["en"],
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
        // Verbatim consolidated XHTML from the official channel; capped by size.
        // Cellar's 303s redirect https->http, which HttpClient refuses to follow
        // automatically — follow manually, upgrading each hop back to https.
        await PaceAsync(ct);
        var celex = version.Raw.GetValueOrDefault("celex");
        if (celex is null) return null;
        var url = $"https://publications.europa.eu/resource/celex/{celex}";
        HttpResponseMessage? resp = null;
        for (var hop = 0; hop < 6; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("application/xhtml+xml");
            req.Headers.Accept.ParseAdd("text/html");
            req.Headers.AcceptLanguage.ParseAdd(expression.Language);
            resp?.Dispose();
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
            {
                var next = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(url), loc).ToString();
                url = next.StartsWith("http://", StringComparison.Ordinal) ? "https://" + next["http://".Length..] : next;
                continue;
            }
            break;
        }
        if (resp is null || !resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"  [eurlex] {celex}: body fetch failed ({(int?)resp?.StatusCode})");
            resp?.Dispose();
            return null;
        }
        using var _ = resp;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, read);
            if (ms.Length > BodyCapBytes)
            {
                Console.Error.WriteLine($"  [eurlex] {celex}: body exceeds {BodyCapBytes / 1024 / 1024} MB cap — metadata only");
                return null;
            }
        }
        Console.Error.WriteLine($"  [eurlex] {celex}: body {ms.Length / 1024} KB");
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
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
        await PaceAsync(ct);
        var celex = version.Raw.GetValueOrDefault("celex");
        if (celex is null) return null;
        var url = $"https://publications.europa.eu/resource/celex/{celex}";
        HttpResponseMessage? resp = null;
        for (var hop = 0; hop < 6; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // Cellar's negotiation parser rejects "application/zip; mtype=fmx4" (500) — the
            // exact spaceless form must go on the wire, and reading the typed Accept view
            // afterwards would re-normalize it, so: raw header, never inspected.
            req.Headers.TryAddWithoutValidation("Accept", "application/zip;mtype=fmx4");
            req.Headers.AcceptLanguage.ParseAdd(expression.Language);
            resp?.Dispose();
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
            {
                var next = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(url), loc).ToString();
                url = next.StartsWith("http://", StringComparison.Ordinal) ? "https://" + next["http://".Length..] : next;
                continue;
            }
            break;
        }
        if (resp is null || !resp.IsSuccessStatusCode ||
            resp.Content.Headers.ContentType?.MediaType is not "application/zip")
        {
            resp?.Dispose();
            return null;   // no fmx4 manifestation upstream (Cellar answers with a non-zip error body)
        }
        using var _ = resp;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, read);
            if (ms.Length > BodyCapBytes)
            {
                Console.Error.WriteLine($"  [eurlex] {celex}: fmx4 exceeds {BodyCapBytes / 1024 / 1024} MB cap — skipped");
                return null;
            }
        }

        ms.Position = 0;
        var members = new List<ManifestationMember>();
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry.Name);   // no traversal, flat member names only
                if (name.Length == 0) continue;
                await using var es = entry.Open();
                using var ems = new MemoryStream();
                await es.CopyToAsync(ems, ct);
                members.Add(new ManifestationMember(name, ems.ToArray()));
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
        return new ManifestationFetch("fmx4", members, url);
    }

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
            foreach (var baseCelex in Flagships)
            {
                var consolidatedPrefix = "0" + baseCelex[1..];
                var rows = await SelectAsync(Cdm + $$"""
                    SELECT ?celex ?date ?title WHERE {
                      ?s cdm:resource_legal_id_celex ?celex ; cdm:act_consolidated_date ?date .
                      FILTER(STRSTARTS(STR(?celex), "{{consolidatedPrefix}}"))
                      OPTIONAL { ?e cdm:expression_belongs_to_work ?s ;
                                 cdm:expression_uses_language <http://publications.europa.eu/resource/authority/language/ENG> ;
                                 cdm:expression_title ?title }
                    } ORDER BY ?celex
                    """, ct);

                if (rows.Count == 0) { Console.Error.WriteLine($"  [eurlex] {baseCelex}: no consolidated versions found"); continue; }

                // Consolidated expressions usually carry no title in Cellar — fall back to the
                // base act's official EN title so search matches natural-language names.
                var baseTitleRows = await SelectAsync(Cdm + $$"""
                    SELECT ?title WHERE {
                      ?w cdm:resource_legal_id_celex ?c .
                      FILTER(STR(?c) = "{{baseCelex}}")
                      ?e cdm:expression_belongs_to_work ?w ;
                         cdm:expression_uses_language <http://publications.europa.eu/resource/authority/language/ENG> ;
                         cdm:expression_title ?title .
                    } LIMIT 1
                    """, ct);
                var baseTitle = baseTitleRows.FirstOrDefault()?.GetValueOrDefault("title");
                var commonName = CommonNames.GetValueOrDefault(baseCelex);

                var workUri = $"http://publications.europa.eu/resource/celex/{baseCelex}";
                var slug = baseCelex.ToLowerInvariant();
                var typeCode = baseCelex.Contains('R') && baseCelex[5] == 'R' ? "REG" : baseCelex[5] == 'L' ? "DIR" : "REG";

                // Distinct versions sorted by consolidation date; valid_to = next valid_from - 1 (publisher-dated sequence).
                var versions = rows
                    .GroupBy(r => r["celex"], StringComparer.Ordinal)
                    .Select(g => (Celex: g.Key, Date: DateOnly.Parse(g.First()["date"]), Title: g.First().GetValueOrDefault("title")))
                    .OrderBy(v => v.Date).ThenBy(v => v.Celex, StringComparer.Ordinal)
                    .ToList();

                var list = new List<VersionRecord>();
                for (var i = 0; i < versions.Count; i++)
                {
                    var (celex, date, title) = versions[i];
                    DateOnly? validTo = i + 1 < versions.Count ? versions[i + 1].Date.AddDays(-1) : null;
                    var sourceUri = $"https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:{celex}";
                    var effTitle = title ?? baseTitle;
                    list.Add(new VersionRecord(
                        Id: new Identifier($"http://publications.europa.eu/resource/celex/{celex}"),
                        WorkId: new Identifier(workUri),
                        TypeCode: typeCode,
                        ValidFrom: date,
                        ValidTo: validTo,
                        ValidTimeSource: "publisher",
                        InForceStatus: null,
                        PublicationDate: date,
                        Expressions:
                        [
                            new ExpressionRecord("en", date, validTo, "publisher",
                                Title: effTitle, TitleShort: DisplayTitle(commonName, effTitle, celex), SourceUri: sourceUri)
                        ],
                        Relations: [new RelationRecord("consolidates", new Identifier(workUri))],
                        Raw: new Dictionary<string, string> { ["celex"] = celex }));
                }

                _byWork[workUri] = list;
                _works[workUri] = new WorkRef(new Identifier(workUri), slug, typeCode,
                    DisplayTitle(commonName, versions[^1].Title ?? baseTitle, baseCelex));
                Console.Error.WriteLine($"  [eurlex] {baseCelex}: {list.Count} consolidated versions");
            }
            _loaded = true;
        }
        finally { _initLock.Release(); }
    }

    private static string? ShortTitle(string? title)
    {
        if (title is null) return null;
        var cut = title.IndexOf(" of the European Parliament", StringComparison.Ordinal);
        return cut > 0 ? title[..cut] : title.Length > 90 ? title[..90] + "…" : title;
    }

    private static string DisplayTitle(string? commonName, string? title, string fallback)
    {
        var s = ShortTitle(title);
        return commonName is null ? s ?? fallback : s is null ? commonName : $"{commonName} — {s}";
    }

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
