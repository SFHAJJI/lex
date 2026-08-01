using System.Runtime.CompilerServices;
using Lex.Law;

namespace Lex.Sources.Legilux;

/// <summary>
/// Tier A metadata: the publisher supplies validity intervals (jolux:dateApplicability).
/// Runs in METADATA-ONLY mode as a standing state (spec D42): no published, robots-compliant
/// body channel exists, so FetchBody always returns null with the corpus recording
/// text.available=false, reason=pending-gate.
/// Probe results 2026-08-01: Work = jolux:isMemberOf target; DocumentType = the
/// consolidation's own jolux:typeDocument; compilations (CODE/RECUEIL) are Works like any other.
/// </summary>
public sealed class LegiluxAdapter : ISourceAdapter
{
    private const string Endpoint = "https://data.legilux.public.lu/sparqlendpoint";
    private const string J = "PREFIX jolux: <http://data.legilux.public.lu/resource/ontology/jolux#>\n";

    private readonly SparqlClient _sparql = new(Endpoint);
    private Dictionary<string, List<VersionRecord>>? _byWork;   // work URI -> versions
    private Dictionary<string, WorkRef>? _works;                // work URI -> ref
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public PublisherDescriptor Describe() => new(
        new Publisher(
            Id: "lu-legilux",
            Name: "Service central de législation (Legilux)",
            Jurisdiction: "LU",
            Homepage: "https://legilux.public.lu",
            Tier: Tier.A,
            Attribution: "Ministère d'État – Service central de législation, Grand-Duché de Luxembourg. Data: Legilux open data (CC-BY), data.public.lu dataset 62c83bfd9794ec8e47b5bc68.",
            SourceTermsUrl: "https://data.public.lu/en/datasets/legilux-journal-officiel-du-grand-duche-de-luxembourg/"),
        DocumentTypes: [],   // discovered from data at ingest time; DocumentType is data, not code (§3.5)
        Languages: ["fr"],
        // R2 RESOLVED 2026-08-01 by the publisher's own statements: the official Casemates
        // docs license "les fichiers de contenu" (content files) AND metadata under CC-BY-4.0
        // incl. commercial reuse, and each manifestation carries a machine-readable
        // dct:license CC-BY-4.0 triple. Text is fetched from the robots-PERMITTED main host
        // (legilux.public.lu/filestore/...), the same host the site itself serves from.
        TextIncluded: true,
        TextPublic: true,
        HistoryBegins: "publisher");

    public async IAsyncEnumerable<WorkRef> EnumerateWorks([EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        foreach (var w in _works!.Values.OrderBy(w => w.Id.Value, StringComparer.Ordinal))
            yield return w;
    }

    public async Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        return _byWork!.TryGetValue(work.Id.Value, out var v)
            ? v.OrderBy(x => x.ValidFrom).ToList()
            : [];
    }

    private const int BodyCapBytes = 8 * 1024 * 1024;
    private static readonly HttpClient BodyHttp = CreateBodyClient();
    private DateTimeOffset _lastBodyFetch = DateTimeOffset.MinValue;
    private Dictionary<string, string>? _xmlFiles;   // "<consolidationUri>|<lang>" -> official file URL

    private static HttpClient CreateBodyClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lex/0.1");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/SFHAJJI/lex)");
        return c;
    }

    /// <summary>
    /// Verbatim Akoma Ntoso XML from the official channel: the manifestation file the
    /// publisher's own CC-BY dataset enumerates, served from the robots-permitted main host.
    /// Sequential, paced (D14).
    /// </summary>
    public async Task<string?> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        if (_xmlFiles is null || !_xmlFiles.TryGetValue($"{version.Id.Value}|{expression.Language}", out var url))
            return null;

        var since = DateTimeOffset.UtcNow - _lastBodyFetch;
        var pause = TimeSpan.FromMilliseconds(1500);
        if (since < pause) await Task.Delay(pause - since, ct);
        _lastBodyFetch = DateTimeOffset.UtcNow;

        using var resp = await BodyHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"  [legilux] body fetch failed ({(int)resp.StatusCode}): {url}");
            return null;
        }
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, read);
            if (ms.Length > BodyCapBytes)
            {
                Console.Error.WriteLine($"  [legilux] body exceeds {BodyCapBytes / 1024 / 1024} MB cap: {url}");
                return null;
            }
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_byWork is not null) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_byWork is not null) return;

            // One paged query fetches the whole versioned corpus (~4.6k consolidations, fr expressions).
            // ~6-10 sequential paced requests total.
            var rows = await _sparql.SelectPagedAsync((limit, offset) => J + $$"""
                SELECT ?c ?work ?type ?from ?to ?status ?pub ?expr ?lang ?title ?titleShort WHERE {
                  ?c a jolux:Consolidation ; jolux:isMemberOf ?work ; jolux:dateApplicability ?from .
                  OPTIONAL { ?c jolux:typeDocument ?type }
                  OPTIONAL { ?c jolux:dateEndApplicability ?to }
                  OPTIONAL { ?c jolux:inForceStatus ?status }
                  OPTIONAL { ?c jolux:publicationDate ?pub }
                  OPTIONAL { ?c jolux:isRealizedBy ?expr .
                             OPTIONAL { ?expr jolux:language ?lang }
                             OPTIONAL { ?expr jolux:title ?title }
                             OPTIONAL { ?expr jolux:titleShort ?titleShort } }
                } ORDER BY ?c ?expr LIMIT {{limit}} OFFSET {{offset}}
                """, pageSize: 5000, ct,
                onPage: n => Console.Error.WriteLine($"  [legilux] fetched {n} rows"));

            var byConsolidation = rows.GroupBy(r => r["c"], StringComparer.Ordinal);
            var byWork = new Dictionary<string, List<VersionRecord>>(StringComparer.Ordinal);
            var works = new Dictionary<string, WorkRef>(StringComparer.Ordinal);

            foreach (var grp in byConsolidation)
            {
                var first = grp.First();
                var workUri = first["work"];
                var typeCode = first.TryGetValue("type", out var t) ? LastSegment(t) : null;
                var validFrom = DateOnly.Parse(first["from"]);
                DateOnly? validTo = first.TryGetValue("to", out var to) && to.Length > 0 ? DateOnly.Parse(to) : null;
                DateOnly? pubDate = first.TryGetValue("pub", out var pd) && pd.Length > 0 ? DateOnly.Parse(pd) : null;
                var status = first.TryGetValue("status", out var st) ? LastSegment(st) : null;

                var expressions = grp
                    .Where(r => r.ContainsKey("expr"))
                    .GroupBy(r => r["expr"], StringComparer.Ordinal)
                    .Select(e =>
                    {
                        var er = e.First();
                        var lang = er.TryGetValue("lang", out var l) ? LangCode(l) : "fr";
                        return new ExpressionRecord(
                            Language: lang,
                            ValidFrom: validFrom,
                            ValidTo: validTo,
                            ValidTimeSource: "publisher",
                            Title: er.GetValueOrDefault("title"),
                            TitleShort: er.GetValueOrDefault("titleShort"),
                            SourceUri: PublicUrl(e.Key));
                    })
                    .DistinctBy(e => e.Language)
                    .ToList();

                if (expressions.Count == 0)
                    expressions = [new ExpressionRecord("fr", validFrom, validTo, "publisher", null, null, PublicUrl(grp.Key))];

                var version = new VersionRecord(
                    Id: new Identifier(grp.Key),
                    WorkId: new Identifier(workUri),
                    TypeCode: typeCode,
                    ValidFrom: validFrom,
                    ValidTo: validTo,
                    ValidTimeSource: "publisher",
                    InForceStatus: status,
                    PublicationDate: pubDate,
                    Expressions: expressions,
                    Relations: [],
                    Raw: new Dictionary<string, string>());

                if (!byWork.TryGetValue(workUri, out var list)) byWork[workUri] = list = [];
                list.Add(version);

                if (!works.ContainsKey(workUri))
                    works[workUri] = new WorkRef(new Identifier(workUri), Slug(workUri), typeCode,
                        expressions.FirstOrDefault()?.TitleShort ?? expressions.FirstOrDefault()?.Title);
            }

            // Prefer a title-bearing hint and the dominant type per work.
            foreach (var (uri, list) in byWork)
            {
                var latest = list.OrderByDescending(v => v.ValidFrom).First();
                var hint = latest.Expressions.FirstOrDefault()?.TitleShort
                           ?? latest.Expressions.FirstOrDefault()?.Title;
                var kind = list.GroupBy(v => v.TypeCode).OrderByDescending(g => g.Count()).First().Key;
                works[uri] = works[uri] with { TitleHint = hint, TypeCode = kind };
            }

            // Manifestation map: the publisher's own dataset enumerates the official XML file
            // per expression (isExemplifiedBy, userFormat xml, dct:license CC-BY-4.0). Fetch
            // host is the site's own manifestation_prefix (robots-permitted main host).
            var xmlRows = await _sparql.SelectPagedAsync((limit, offset) => J + $$"""
                SELECT ?c ?expr ?file WHERE {
                  ?c a jolux:Consolidation ; jolux:isRealizedBy ?expr .
                  ?expr jolux:isEmbodiedBy ?m .
                  ?m jolux:userFormat <http://data.legilux.public.lu/resource/authority/user-format/xml> ;
                     jolux:isExemplifiedBy ?file .
                } ORDER BY ?c LIMIT {{limit}} OFFSET {{offset}}
                """, pageSize: 5000, ct,
                onPage: n => Console.Error.WriteLine($"  [legilux] manifestation rows {n}"));
            var xmlFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in xmlRows)
            {
                var lang = LangCode(LastSegment(r["expr"]));
                xmlFiles[$"{r["c"]}|{lang}"] = r["file"]
                    .Replace("http://data.legilux.public.lu/filestore/", "https://legilux.public.lu/filestore/")
                    .Replace("https://data.legilux.public.lu/filestore/", "https://legilux.public.lu/filestore/");
            }
            _xmlFiles = xmlFiles;

            _byWork = byWork;
            _works = works;
            Console.Error.WriteLine($"  [legilux] {works.Count} works, {byWork.Values.Sum(v => v.Count)} versions, {xmlFiles.Count} xml manifestations");
        }
        finally { _initLock.Release(); }
    }

    // Adapter-internal identifier reading is permitted (F4 exempts adapters).
    private static string LastSegment(string uri) => uri.TrimEnd('/').Split('/')[^1];

    private static string LangCode(string langUri) => LastSegment(langUri) switch
    {
        "FRA" => "fr", "DEU" => "de", "ENG" => "en", "LTZ" => "lb", var other => other.ToLowerInvariant()
    };

    /// <summary>Public, human-facing URL for link-out (the main site, not the data subdomain).</summary>
    private static string PublicUrl(string dataUri) =>
        dataUri.Replace("http://data.legilux.public.lu/", "https://legilux.public.lu/")
               .Replace("https://data.legilux.public.lu/", "https://legilux.public.lu/");

    private static string Slug(string workUri)
    {
        // e.g. …/eli/etat/leg/code/procedure_civile  -> code-procedure_civile
        //      …/eli/etat/leg/loi/2001/04/18/n1      -> loi-2001-04-18-n1
        var idx = workUri.IndexOf("/eli/etat/leg/", StringComparison.Ordinal);
        var tail = idx >= 0 ? workUri[(idx + "/eli/etat/leg/".Length)..] : workUri;
        var slug = tail.Trim('/').Replace('/', '-');
        foreach (var bad in new[] { ':', '?', '#', '&', '=' }) slug = slug.Replace(bad, '_');
        return slug;
    }
}
