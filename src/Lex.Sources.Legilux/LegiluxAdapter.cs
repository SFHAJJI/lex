using System.Runtime.CompilerServices;
using Lex.Law;

namespace Lex.Sources.Legilux;

/// <summary>
/// Tier A metadata: the publisher supplies validity intervals (jolux:dateApplicability).
/// D44 superseded the original metadata-only D42 design: official manifestation metadata
/// resolves to robots-permitted, CC-BY content files on legilux.public.lu/filestore.
/// Missing XML follows D49's bounded official-PDF ladder; collection PDFs and source-file gaps
/// remain explicit metadata-only states rather than fabricated legal text.
/// Probe results 2026-08-01: Work = jolux:isMemberOf target; DocumentType = the
/// consolidation's own jolux:typeDocument; compilations (CODE/RECUEIL) are Works like any other.
/// </summary>
public sealed class LegiluxAdapter : ISourceAdapter, ISourceBuildInventory
{
    private const string Endpoint = "https://data.legilux.public.lu/sparqlendpoint";
    private const string J = "PREFIX jolux: <http://data.legilux.public.lu/resource/ontology/jolux#>\n";

    // Fixed release bounds sit well above the measured held catalogue (4,656 versions), its
    // manifestations, six current sameAs identities, and at most 57 canonical subjects/work.
    // Subject rows allow both general and specific scheme joins before fail-closed parsing.
    internal const int CatalogueMaximumRows = 20_000;
    internal const int SubjectMaximumRows = 200_000;
    internal const int IdentityMaximumRows = 20_000;
    internal const int ManifestationMaximumRows = 50_000;

    private readonly SparqlClient _sparql;
    private Dictionary<string, List<VersionRecord>>? _byWork;   // work URI -> versions
    private Dictionary<string, WorkRef>? _works;                // work URI -> ref
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public LegiluxAdapter() : this(new SparqlClient(Endpoint)) { }

    internal LegiluxAdapter(SparqlClient sparql) => _sparql = sparql;

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

    // 32 MB, not 8. The old ceiling was refusing four real instruments whose XML the publisher
    // does serve: code/fonction_publique at 9.2 MB, rgd-2005-06-09-n1 at 10.4, and two versions
    // of rgd-2016-04-27-n4 at 30.5 and 30.7. Those are large because of their annexes, not
    // because anything is wrong with them, and refusing publisher XML we can have is a worse
    // failure than parsing a big file. The largest thing Legilux publishes is around 31 MB, so
    // this clears it with headroom and still stops a runaway.
    private const int BodyCapBytes = 32 * 1024 * 1024;
    private static readonly HttpClient BodyHttp = CreateBodyClient();
    private static readonly SourceRetryPolicy RetryPolicy = new(MaximumAttempts: 4);
    private DateTimeOffset _lastBodyFetch = DateTimeOffset.MinValue;
    private Dictionary<string, string>? _xmlFiles;   // "<consolidationUri>|<lang>" -> official file URL
    private Dictionary<string, string>? _pdfFiles;   // same key, the PDF the publisher also lists (D49)

    public SourceBuildInventory GetBuildInventory() =>
        new(_works?.Count ?? 0, [], RetryMaximumAttempts: RetryPolicy.MaximumAttempts);

    private static HttpClient CreateBodyClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(180) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lex/0.1");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/SFHAJJI/lex)");
        return c;
    }

    /// <summary>
    /// Verbatim Akoma Ntoso XML from the official channel: the manifestation file the
    /// publisher's own CC-BY dataset enumerates, served from the robots-permitted main host.
    /// Sequential, paced (D14).
    /// </summary>
    public async Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        if (_xmlFiles is null || !_xmlFiles.TryGetValue($"{version.Id.Value}|{expression.Language}", out var url))
            return new(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The publisher did not enumerate an XML manifestation for this expression.");

        var since = DateTimeOffset.UtcNow - _lastBodyFetch;
        var pause = TimeSpan.FromMilliseconds(1500);
        if (since < pause) await Task.Delay(pause - since, ct);
        _lastBodyFetch = DateTimeOffset.UtcNow;

        var sent = await SourceHttp.SendAsync(BodyHttp,
            () => new HttpRequestMessage(HttpMethod.Get, url), RetryPolicy, ct);
        using var resp = sent.Response;
        if (resp is null || sent.RetryExhausted)
            return new(SourceBodyStatus.RetryExhausted,
                Detail: sent.FailureDetail ?? "The official XML endpoint exhausted the retry policy.",
                Attempts: sent.Attempts);
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"  [legilux] body fetch failed ({(int)resp.StatusCode}): {url}");
            return resp.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => new(SourceBodyStatus.PermanentNotFound,
                    Detail: "The official XML manifestation returned HTTP 404.", Attempts: sent.Attempts),
                System.Net.HttpStatusCode.Gone => new(SourceBodyStatus.Gone,
                    Detail: "The official XML manifestation returned HTTP 410.", Attempts: sent.Attempts),
                System.Net.HttpStatusCode.RequestTimeout
                    or System.Net.HttpStatusCode.TooManyRequests
                    or System.Net.HttpStatusCode.InternalServerError
                    or System.Net.HttpStatusCode.BadGateway
                    or System.Net.HttpStatusCode.ServiceUnavailable
                    or System.Net.HttpStatusCode.GatewayTimeout => new(SourceBodyStatus.RetryExhausted,
                        Detail: $"Retryable publisher response {(int)resp.StatusCode} exhausted the acquisition policy.",
                        Attempts: sent.Attempts),
                _ => new(SourceBodyStatus.ParserFailure,
                    Detail: $"Official XML acquisition failed with HTTP {(int)resp.StatusCode}.",
                    Attempts: sent.Attempts),
            };
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
                return new(SourceBodyStatus.Oversized,
                    Detail: $"Official XML exceeded the {BodyCapBytes}-byte acquisition limit.",
                    Attempts: sent.Attempts);
            }
        }
        return SourceBodyFetch.Retrieved(System.Text.Encoding.UTF8.GetString(ms.ToArray()), sent.Attempts);
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
                """, pageSize: 5000, maximumRows: CatalogueMaximumRows, ct: ct,
                onPage: n => Console.Error.WriteLine($"  [legilux] fetched {n} rows"));

            var subjectRows = await _sparql.SelectPagedAsync(
                LegiluxPublisherMetadata.Query, pageSize: 5000,
                maximumRows: SubjectMaximumRows, ct: ct,
                onPage: n => Console.Error.WriteLine($"  [legilux] fetched {n} subject rows"));
            var subjectsByWork = LegiluxPublisherMetadata.ParseSubjects(subjectRows);
            var identityRows = await _sparql.SelectPagedAsync(
                LegiluxOfficialIdentities.Query, pageSize: 5000,
                maximumRows: IdentityMaximumRows, ct: ct,
                onPage: n => Console.Error.WriteLine($"  [legilux] fetched {n} official identity rows"));
            var identitiesByWork = LegiluxOfficialIdentities.Parse(identityRows);

            var byConsolidation = rows.GroupBy(r => r["c"], StringComparer.Ordinal);
            var byWork = new Dictionary<string, List<VersionRecord>>(StringComparer.Ordinal);
            var works = new Dictionary<string, WorkRef>(StringComparer.Ordinal);

            foreach (var grp in byConsolidation)
            {
                var first = grp.First();
                var workUri = first["work"];
                var typeCode = first.TryGetValue("type", out var t) ? LastSegment(t) : null;
                var validFrom = ParseDate(first["from"]);
                DateOnly? validTo = first.TryGetValue("to", out var to) && to.Length > 0 ? ParseDate(to) : null;
                DateOnly? pubDate = first.TryGetValue("pub", out var pd) && pd.Length > 0 ? ParseDate(pd) : null;
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

                var publisherMetadata = subjectsByWork.GetValueOrDefault(workUri, [])
                    .Concat(identitiesByWork.GetValueOrDefault(workUri, []))
                    .Distinct().OrderBy(value => value.Kind, StringComparer.Ordinal)
                    .ThenBy(value => value.Identifier, StringComparer.Ordinal)
                    .ThenBy(value => value.Language, StringComparer.Ordinal)
                    .ThenBy(value => value.Label, StringComparer.Ordinal).ToArray();
                if (publisherMetadata.Length > 512)
                    throw new InvalidDataException(
                        $"Legilux work {workUri} exceeds 512 publisher metadata records.");

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
                    Raw: new Dictionary<string, string>(),
                    PublisherMetadata: publisherMetadata);

                if (!byWork.TryGetValue(workUri, out var list)) byWork[workUri] = list = [];
                list.Add(version);

                if (!works.ContainsKey(workUri))
                    works[workUri] = new WorkRef(new Identifier(workUri), Slug(workUri), typeCode,
                        expressions.FirstOrDefault()?.TitleShort ?? expressions.FirstOrDefault()?.Title);
            }

            // Prefer a title-bearing hint and the dominant type per work.
            //
            // The hint used to be Expressions.FirstOrDefault(), which is whichever expression the
            // publisher's result set happened to list first. For a work published in several
            // languages that is a coin toss, and the Constitution lost it: its 2023 version exists
            // in German, French and Luxembourgish, German came first, and because the individual
            // expressions carry no title of their own, that one hint became the title of all 39
            // versions back to 1919. A German heading sat over French articles on one of three
            // suggested starting points on the front page.
            //
            // The language chosen is the one the work is MOSTLY published in, counted across its
            // own versions rather than assumed from the country: 37 of the Constitution's 39 are
            // French. Ties break alphabetically so two runs of the same input agree.
            foreach (var (uri, list) in byWork)
            {
                var dominant = list
                    .SelectMany(v => v.Expressions)
                    .GroupBy(e => e.Language, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => g.Key)
                    .FirstOrDefault();

                // Newest version first, dominant language first within it, and skip expressions
                // that carry no title at all rather than letting one absent title win.
                var hint = list
                    .OrderByDescending(v => v.ValidFrom)
                    .SelectMany(v => v.Expressions.OrderByDescending(e => e.Language == dominant))
                    .Select(e => e.TitleShort ?? e.Title)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

                var kind = list.GroupBy(v => v.TypeCode).OrderByDescending(g => g.Count()).First().Key;
                works[uri] = works[uri] with { TitleHint = hint, TypeCode = kind };
            }

            // Manifestation map: the publisher's own dataset enumerates the official XML file
            // per expression (isExemplifiedBy, userFormat xml, dct:license CC-BY-4.0). Fetch
            // host is the site's own manifestation_prefix (robots-permitted main host).
            // Ask for the format too, rather than filtering to xml in the query. The publisher
            // offers XML for 2,892 of its consolidations and PDF only for 1,611 (D49), and the
            // PDF is the only fallback that exists for the rest, so discarding it here was
            // throwing away the answer to "why is there no text".
            var xmlRows = await _sparql.SelectPagedAsync((limit, offset) => J + $$"""
                SELECT ?c ?expr ?fmt ?file WHERE {
                  ?c a jolux:Consolidation ; jolux:isRealizedBy ?expr .
                  ?expr jolux:isEmbodiedBy ?m .
                  ?m jolux:userFormat ?fmt ; jolux:isExemplifiedBy ?file .
                  VALUES ?fmt { <http://data.legilux.public.lu/resource/authority/user-format/xml>
                                <http://data.legilux.public.lu/resource/authority/user-format/pdf> }
                } ORDER BY ?c LIMIT {{limit}} OFFSET {{offset}}
                """, pageSize: 5000, maximumRows: ManifestationMaximumRows, ct: ct,
                onPage: n => Console.Error.WriteLine($"  [legilux] manifestation rows {n}"));
            var xmlFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var pdfFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in xmlRows)
            {
                var lang = LangCode(LastSegment(r["expr"]));
                var url = r["file"]
                    .Replace("http://data.legilux.public.lu/filestore/", "https://legilux.public.lu/filestore/")
                    .Replace("https://data.legilux.public.lu/filestore/", "https://legilux.public.lu/filestore/");
                (LastSegment(r["fmt"]) == "pdf" ? pdfFiles : xmlFiles)[$"{r["c"]}|{lang}"] = url;
            }
            _xmlFiles = xmlFiles;
            _pdfFiles = pdfFiles;

            _byWork = byWork;
            _works = works;
            Console.Error.WriteLine($"  [legilux] {works.Count} works, {byWork.Values.Sum(v => v.Count)} versions, {xmlFiles.Count} xml manifestations");
        }
        finally { _initLock.Release(); }
    }

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(
        value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

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

    /// <summary>
    /// D49: the PDF fallback, on the D48 alternative-manifestation seam.
    ///
    /// Returned ONLY when the publisher lists no XML for this exact version and language, because
    /// a version derived twice would put two texts of the same article in the corpus. The PDF is
    /// publisher bytes like any other evidence and is stored verbatim under its own sha256; what
    /// is lower-confidence is the DERIVATION, and that is carried by the profile id (pdf-lu/1),
    /// not by pretending the bytes are less real than they are.
    ///
    /// Gazette scans are excluded here rather than downstream. When a law was never amended,
    /// Legilux points its consolidation at the whole Mémorial issue the act first appeared in, a
    /// scan containing several unrelated acts. Locating one act inside it needs layout analysis
    /// and is a different profile at a different confidence; feeding it to pdf-lu/1 would produce
    /// confident, wrong articles.
    /// </summary>
    public async Task<SourceManifestationFetch> FetchAltManifestation(
        VersionRecord version, ExpressionRecord expression, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        var key = $"{version.Id.Value}|{expression.Language}";
        if (_xmlFiles is not null && _xmlFiles.ContainsKey(key))
            return new(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The primary XML manifestation already owns this expression.");
        if (_pdfFiles is null || !_pdfFiles.TryGetValue(key, out var url))
            return new(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The publisher did not enumerate an alternative PDF manifestation.");

        // Thematic collections are excluded here, and this is not a nicety. A RECUEIL or
        // CODE_RECUEIL is a shelf, not an instrument: its PDF concatenates every act on the shelf,
        // runs to tens of megabytes, and would be re-fetched for each of the hundreds of dates the
        // shelf is restamped. Learned the hard way: without this the run filled the disk with
        // 638 MB of code-communal before failing.
        if (version.TypeCode is "RECUEIL" or "CODE_RECUEIL")
        {
            Console.Error.WriteLine($"  [legilux] pdf belongs to a thematic collection, not an act; skipped: {version.Id.Value}");
            return new(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The publisher PDF is a thematic collection rather than one legal act.");
        }
        // A gazette issue is fetched, but declared as its own format so the derive step sends it
        // to pdf-memorial-lu/1 rather than to pdf-lu/1. The two are not interchangeable: one reads
        // a document that IS the act, the other has to find the act inside a whole day's journal
        // among unrelated ones, and mixing them would let a confident profile loose on a document
        // it cannot reason about.
        var gazette = url.Contains("/memorial/", StringComparison.Ordinal);

        var since = DateTimeOffset.UtcNow - _lastBodyFetch;
        var pause = TimeSpan.FromMilliseconds(1500);
        if (since < pause) await Task.Delay(pause - since, ct);
        _lastBodyFetch = DateTimeOffset.UtcNow;

        var sent = await SourceHttp.SendAsync(BodyHttp,
            () => new HttpRequestMessage(HttpMethod.Get, url), RetryPolicy, ct);
        using var resp = sent.Response;
        if (resp is null || sent.RetryExhausted)
            return new(SourceBodyStatus.RetryExhausted,
                Detail: sent.FailureDetail ?? "The official PDF endpoint exhausted the retry policy.",
                Attempts: sent.Attempts);
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"  [legilux] pdf fetch failed ({(int)resp.StatusCode}): {url}");
            return resp.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => new(SourceBodyStatus.PermanentNotFound,
                    Detail: "The official PDF manifestation returned HTTP 404.", Attempts: sent.Attempts),
                System.Net.HttpStatusCode.Gone => new(SourceBodyStatus.Gone,
                    Detail: "The official PDF manifestation returned HTTP 410.", Attempts: sent.Attempts),
                _ => new(SourceBodyStatus.ParserFailure,
                    Detail: $"Official PDF acquisition failed with HTTP {(int)resp.StatusCode}.",
                    Attempts: sent.Attempts),
            };
        }
        // A consolidated act is a few MB at most; the whole 1,197-article Code du travail is 2.3.
        // Anything far past that is a compilation the type field failed to mark, and downloading
        // it is how the disk fills. Checked before reading the body, not after.
        const long CapBytes = 25L * 1024 * 1024;
        if (resp.Content.Headers.ContentLength is > CapBytes)
        {
            Console.Error.WriteLine($"  [legilux] pdf exceeds {CapBytes / 1024 / 1024} MB, not a single act; skipped: {url}");
            return new(SourceBodyStatus.Oversized,
                Detail: $"Official PDF exceeded the {CapBytes}-byte acquisition limit.",
                Attempts: sent.Attempts);
        }
        using var ms = new MemoryStream();
        await (await resp.Content.ReadAsStreamAsync(ct)).CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (bytes.LongLength > CapBytes)
        {
            Console.Error.WriteLine($"  [legilux] pdf exceeds the cap once read; skipped: {url}");
            return new(SourceBodyStatus.Oversized,
                Detail: $"Official PDF exceeded the {CapBytes}-byte acquisition limit.",
                Attempts: sent.Attempts);
        }
        if (bytes.Length < 5 || bytes[0] != 0x25 || bytes[1] != 0x50 || bytes[2] != 0x44 || bytes[3] != 0x46)
        {
            Console.Error.WriteLine($"  [legilux] response is not a PDF; discarded: {url}");
            return new(SourceBodyStatus.ParserFailure,
                Detail: "The official PDF response did not have a PDF signature.",
                Attempts: sent.Attempts);
        }
        return SourceManifestationFetch.Retrieved(
            new ManifestationFetch(gazette ? "pdf-memorial" : "pdf",
                [new ManifestationMember(url.Split('/')[^1], bytes)], PublicUrl(url)), sent.Attempts);
    }

    private static string Slug(string workUri)
    {
        // e.g. …/eli/etat/leg/code/procedure_civile  -> code-procedure_civile
        //      …/eli/etat/leg/loi/2001/04/18/n1      -> loi-2001-04-18-n1
        //      …/eli/etat/adm/pa/2020/10/23/b4077    -> pa-2020-10-23-b4077
        //
        // The branch after /eli/ is not always "etat/leg": administrative acts sit under
        // "etat/adm". Matching the literal meant those works kept their whole URL as their
        // identifier, so a publication notice was published as
        // "http_--data.legilux.public.lu-eli-etat-adm-pa-2020-10-23-b4077" in permalinks and in
        // every list. Skip the two segments after /eli/ instead of naming them.
        var idx = workUri.IndexOf("/eli/", StringComparison.Ordinal);
        var tail = workUri;
        if (idx >= 0)
        {
            var rest = workUri[(idx + "/eli/".Length)..].TrimStart('/');
            var cut = 0;
            for (var seg = 0; seg < 2 && cut >= 0; seg++)
            {
                var next = rest.IndexOf('/', cut);
                cut = next < 0 ? -1 : next + 1;
            }
            tail = cut > 0 ? rest[cut..] : rest;
        }
        var slug = tail.Trim('/').Replace('/', '-');
        foreach (var bad in new[] { ':', '?', '#', '&', '=' }) slug = slug.Replace(bad, '_');
        return slug;
    }
}
