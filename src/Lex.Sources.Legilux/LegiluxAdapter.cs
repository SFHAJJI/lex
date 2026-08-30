using System.Runtime.CompilerServices;
using Lex.Law;

namespace Lex.Sources.Legilux;

internal sealed record ManifestationTransport(string Format, string FetchUri);

/// <summary>
/// Tier A metadata: the publisher supplies validity intervals (jolux:dateApplicability).
/// D44 superseded the original metadata-only D42 design: official manifestation metadata
/// resolves to robots-permitted, CC-BY content files on legilux.public.lu/filestore.
/// Missing XML follows D49's bounded official-PDF ladder; collection PDFs and source-file gaps
/// remain explicit metadata-only states rather than fabricated legal text.
/// Probe results 2026-08-01: Work = jolux:isMemberOf target; DocumentType = the
/// consolidation's own jolux:typeDocument; compilations (CODE/RECUEIL) are Works like any other.
/// </summary>
public sealed class LegiluxAdapter : ISourceAdapter, ISourceBuildInventory,
    ILegacyVersionIdentityResolver
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
    internal const int HeldWorkMetadataBatchSize = 8;
    internal const int ManifestationLicenceBatchSize = 32;
    internal const int ManifestationLicenceClaimsMaximum = 2;
    internal const int SubjectRawRowsPerWorkMaximum = 1_024;
    internal const int IdentityRowsPerWorkMaximum = 512;

    private readonly SparqlClient _sparql;
    private Dictionary<string, List<VersionRecord>>? _byWork;   // work URI -> versions
    private Dictionary<string, WorkRef>? _works;                // work URI -> ref
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public LegiluxAdapter() : this(new SparqlClient(Endpoint)) { }

    internal LegiluxAdapter(SparqlClient sparql) => _sparql = sparql;

    public Identifier ResolveLegacyVersionIdentity(LegacyVersionIdentity legacy)
    {
        var work = RequireOfficialEli(legacy.WorkIdentifier, "work_identifier");
        if (legacy.Expressions.Count == 0)
            throw new InvalidDataException(
                "A legacy Legilux version has no expression source identity.");

        var expectedDate = legacy.ValidFrom.ToString(
            "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var workPath = work.AbsolutePath.TrimEnd('/');
        var candidates = legacy.Expressions
            .Select(expression => LegacyConsolidationIdentifier(
                expression.SourceUri, expression.Language, expectedDate,
                workPath))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length != 1)
            throw new InvalidDataException(
                "A legacy Legilux version resolves to more than one publisher identity.");
        return new Identifier(candidates[0]);
    }

    private static string LegacyConsolidationIdentifier(
        string sourceUri, string language, string expectedDate,
        string workPath)
    {
        if (language.Length != 2
            || language.Any(character => character is not (>= 'a' and <= 'z')))
            throw new InvalidDataException(
                "A legacy Legilux expression has an invalid language code.");
        var uri = RequireOfficialEli(sourceUri, "expression source_uri");
        var path = uri.AbsolutePath;
        var languageSuffix = "/" + language;
        if (path.EndsWith(languageSuffix, StringComparison.Ordinal))
            path = path[..^languageSuffix.Length];

        const string legislativePrefix = "/eli/etat/leg/";
        const string administrativePrefix = "/eli/etat/adm/";
        var legislativeWork = workPath.StartsWith(legislativePrefix, StringComparison.Ordinal);
        var administrativeWork = workPath.StartsWith(administrativePrefix, StringComparison.Ordinal);

        if (legislativeWork
            && string.Equals(path, workPath + "/" + expectedDate,
                StringComparison.Ordinal))
            return "http://data.legilux.public.lu" + path;

        if (administrativeWork
            && string.Equals(path, workPath + "/consolide/" + expectedDate,
                StringComparison.Ordinal))
            return "http://data.legilux.public.lu" + path;

        const string codePrefix = legislativePrefix + "code/";
        var codeSuffix = "/" + expectedDate;
        if (legislativeWork
            && path.StartsWith(codePrefix, StringComparison.Ordinal)
            && path.EndsWith(codeSuffix, StringComparison.Ordinal)
            && IsSafeEliSegment(path[codePrefix.Length..^codeSuffix.Length]))
            return "http://data.legilux.public.lu" + path;

        const string recueilPrefix = legislativePrefix + "recueil/";
        var recueilStatePrefix = workPath + "/";
        if (IsSingleSegmentWork(workPath, recueilPrefix)
            && path.StartsWith(recueilStatePrefix, StringComparison.Ordinal)
            && path[recueilStatePrefix.Length..] is var publisherDate
            && publisherDate.Length == 8
            && publisherDate.All(character => character is >= '0' and <= '9'))
            return "http://data.legilux.public.lu" + path;

        var suffix = "/consolide/" + expectedDate;
        if (!legislativeWork
            || !path.StartsWith(legislativePrefix, StringComparison.Ordinal)
            || !path.EndsWith(suffix, StringComparison.Ordinal)
            || path.Length <= legislativePrefix.Length + suffix.Length
            || !path[legislativePrefix.Length..^suffix.Length]
                .Split('/').All(segment => segment.Length > 0))
            throw new InvalidDataException(
                "A legacy Legilux expression source is not its exact "
                + "consolidation path for valid_from.");

        return "http://data.legilux.public.lu" + path;
    }

    private static bool IsSingleSegmentWork(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.Ordinal)
        && IsSafeEliSegment(path[prefix.Length..]);

    private static bool IsSafeEliSegment(string segment) =>
        segment.Length > 0
        && segment.All(character => character is >= 'a' and <= 'z'
            or >= '0' and <= '9' or '_');

    private static bool IsOfficialEliPath(string path)
    {
        var prefix = path.StartsWith("/eli/etat/leg/", StringComparison.Ordinal)
            ? "/eli/etat/leg/"
            : path.StartsWith("/eli/etat/adm/", StringComparison.Ordinal)
                ? "/eli/etat/adm/"
                : null;
        return prefix is not null
            && path[prefix.Length..].Split('/').All(IsSafeEliSegment);
    }

    private static Uri RequireOfficialEli(string value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || (uri.Host != "legilux.public.lu"
                && uri.Host != "data.legilux.public.lu")
            || !IsOfficialEliPath(uri.AbsolutePath)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort)
            throw new InvalidDataException(
                $"A legacy Legilux {field} is not an official ELI URI.");
        return uri;
    }

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
    internal sealed record LegiluxManifestation(
        string Identifier,
        string FileIdentifier,
        string FetchUri,
        LicenceChannelEvidence SparqlLicence);

    private Dictionary<string, LegiluxManifestation>? _xmlFiles;
    private Dictionary<string, LegiluxManifestation>? _pdfFiles;

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
        if (_xmlFiles is null || !_xmlFiles.TryGetValue(
                $"{version.Id.Value}|{expression.Language}", out var manifestation))
            return new(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The publisher did not enumerate an XML manifestation for this expression.");
        var licence = ManifestationLicenceEvidence.AwaitingFile(
            manifestation.Identifier,
            manifestation.FileIdentifier,
            manifestation.SparqlLicence);
        var url = manifestation.FetchUri;

        var since = DateTimeOffset.UtcNow - _lastBodyFetch;
        var pause = TimeSpan.FromMilliseconds(1500);
        if (since < pause) await Task.Delay(pause - since, ct);
        _lastBodyFetch = DateTimeOffset.UtcNow;

        var sent = await SourceHttp.SendAsync(BodyHttp,
            () => new HttpRequestMessage(HttpMethod.Get, url), RetryPolicy, ct);
        using var resp = sent.Response;
        if (resp is null)
            return new(SourceBodyStatus.RetryExhausted,
                Detail: sent.FailureDetail ?? "The official XML endpoint exhausted the retry policy.",
                Attempts: sent.Attempts,
                Licence: licence);
        var effectiveSourceUri = RequireOfficialResponseUri(resp, url);
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"  [legilux] body fetch failed ({(int)resp.StatusCode}): {url}");
            await using var rejectedStream = await resp.Content.ReadAsStreamAsync(ct);
            var rejected = await ReadBoundedBody(rejectedStream, BodyCapBytes, ct);
            var rejectedHttp = new SourceHttpEvidence(
                (int)resp.StatusCode,
                resp.Content.Headers.ContentType?.MediaType,
                resp.Content.Headers.ContentType?.CharSet,
                resp.Headers.ETag?.ToString(),
                resp.Content.Headers.LastModified,
                DateTimeOffset.UtcNow,
                effectiveSourceUri,
                !rejected.LimitExceeded);
            SourceBodyFetch Failure(SourceBodyStatus status, string detail) =>
                new(status, rejected.Bytes, rejectedHttp, detail, sent.Attempts, licence);
            return resp.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => Failure(SourceBodyStatus.PermanentNotFound,
                    "The official XML manifestation returned HTTP 404."),
                System.Net.HttpStatusCode.Gone => Failure(SourceBodyStatus.Gone,
                    "The official XML manifestation returned HTTP 410."),
                System.Net.HttpStatusCode.RequestTimeout
                    or System.Net.HttpStatusCode.TooManyRequests
                    or System.Net.HttpStatusCode.InternalServerError
                    or System.Net.HttpStatusCode.BadGateway
                    or System.Net.HttpStatusCode.ServiceUnavailable
                    or System.Net.HttpStatusCode.GatewayTimeout => Failure(SourceBodyStatus.RetryExhausted,
                        $"Retryable publisher response {(int)resp.StatusCode} exhausted the acquisition policy."),
                _ => Failure(SourceBodyStatus.ParserFailure,
                    $"Official XML acquisition failed with HTTP {(int)resp.StatusCode}."),
            };
        }
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            var remaining = BodyCapBytes - checked((int)ms.Length);
            if (read > remaining)
            {
                if (remaining > 0) ms.Write(buf, 0, remaining);
                Console.Error.WriteLine($"  [legilux] body exceeds {BodyCapBytes / 1024 / 1024} MB cap: {url}");
                var rejectedHttp = new SourceHttpEvidence(
                    (int)resp.StatusCode,
                    resp.Content.Headers.ContentType?.MediaType,
                    resp.Content.Headers.ContentType?.CharSet,
                    resp.Headers.ETag?.ToString(),
                    resp.Content.Headers.LastModified,
                    DateTimeOffset.UtcNow,
                    effectiveSourceUri,
                    BodyComplete: false);
                return new(SourceBodyStatus.Oversized,
                    ms.ToArray(), rejectedHttp,
                    Detail: $"Official XML exceeded the {BodyCapBytes}-byte acquisition limit.",
                    Attempts: sent.Attempts,
                    Licence: licence);
            }
            ms.Write(buf, 0, read);
        }
        var bytes = ms.ToArray();
        var http = new SourceHttpEvidence(
            (int)resp.StatusCode,
            resp.Content.Headers.ContentType?.MediaType,
            resp.Content.Headers.ContentType?.CharSet,
            resp.Headers.ETag?.ToString(),
            resp.Content.Headers.LastModified,
            DateTimeOffset.UtcNow,
            effectiveSourceUri);
        var fileLicence = LegiluxLicenceEvidence.FromAkomaNtoso(
            bytes, manifestation.Identifier);
        return SourceBodyFetch.Retrieved(
            bytes, http, sent.Attempts, licence.WithFile(fileLicence));
    }

    private static string RequireOfficialResponseUri(
        HttpResponseMessage response, string expectedSourceUri) =>
        RequireOfficialResponseUri(
            response.RequestMessage?.RequestUri?.AbsoluteUri, expectedSourceUri);

    internal static string RequireOfficialResponseUri(
        string? value, string expectedSourceUri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !Uri.TryCreate(expectedSourceUri, UriKind.Absolute, out var expected)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "legilux.public.lu",
                StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.Equals(expected))
            throw new InvalidDataException(
                "The publisher response URI does not match the requested official Legilux file.");
        return uri.AbsoluteUri;
    }

    private static async Task<(byte[] Bytes, bool LimitExceeded)> ReadBoundedBody(
        Stream stream, int capBytes, CancellationToken ct)
    {
        using var body = new MemoryStream(Math.Min(capBytes, 1024 * 1024));
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            var remaining = capBytes - checked((int)body.Length);
            if (read > remaining)
            {
                if (remaining > 0) body.Write(buffer, 0, remaining);
                return (body.ToArray(), true);
            }
            body.Write(buffer, 0, read);
        }
        return (body.ToArray(), false);
    }

    internal static string ManifestationQuery(int limit, int offset) => J + $$"""
        SELECT ?c ?expr ?m ?fmt ?file WHERE {
          ?c a jolux:Consolidation ; jolux:isRealizedBy ?expr .
          ?expr jolux:isEmbodiedBy ?m .
          ?m jolux:userFormat ?fmt ; jolux:isExemplifiedBy ?file .
          VALUES ?fmt { <http://data.legilux.public.lu/resource/authority/user-format/xml>
                        <http://data.legilux.public.lu/resource/authority/user-format/pdf> }
        } ORDER BY ?c ?expr ?m ?fmt ?file LIMIT {{limit}} OFFSET {{offset}}
        """;

    internal static string ManifestationLicenceQuery(
        IReadOnlyCollection<string> manifestations)
    {
        var values = ManifestationValues(manifestations);
        var limit = checked(manifestations.Count * ManifestationLicenceClaimsMaximum + 1);
        return J + $$"""
            SELECT ?m ?license WHERE {
              VALUES ?m { {{values}} }
              OPTIONAL { ?m jolux:license ?license }
            } ORDER BY ?m ?license LIMIT {{limit}}
            """;
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

            var heldWorkUris = rows.Select(row => RequiredHeldWork(row))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var subjectRows = await SelectHeldWorkMetadataAsync(
                heldWorkUris, LegiluxPublisherMetadata.Query,
                SubjectRawRowsPerWorkMaximum, SubjectMaximumRows, "subject", ct);
            var subjectsByWork = LegiluxPublisherMetadata.ParseSubjects(subjectRows);
            var identityRows = await SelectHeldWorkMetadataAsync(
                heldWorkUris, LegiluxOfficialIdentities.Query,
                IdentityRowsPerWorkMaximum, IdentityMaximumRows, "official identity", ct);
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
            // per expression (isExemplifiedBy and userFormat). Licence terms are read in
            // separate bounded VALUES batches so OPTIONAL multiplicity cannot truncate this
            // identity enumeration. Fetch
            // host is the site's own manifestation_prefix (robots-permitted main host).
            // Ask for the format too, rather than filtering to xml in the query. The publisher
            // offers XML for 2,892 of its consolidations and PDF only for 1,611 (D49), and the
            // PDF is the only fallback that exists for the rest, so discarding it here was
            // throwing away the answer to "why is there no text".
            var manifestationRows = await _sparql.SelectTermsPagedAsync(
                ManifestationQuery, pageSize: 5000,
                maximumRows: ManifestationMaximumRows, ct: ct,
                onPage: n => Console.Error.WriteLine($"  [legilux] manifestation rows {n}"));
            var manifestationIdentifiers = manifestationRows
                .Select(row => RequiredUriTerm(row, "m").Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var licences = await SelectManifestationLicencesAsync(
                manifestationIdentifiers, ct);
            var manifestationMaps = BuildManifestationMaps(
                manifestationRows, licences);
            _xmlFiles = manifestationMaps.Xml;
            _pdfFiles = manifestationMaps.Pdf;

            _byWork = byWork;
            _works = works;
            Console.Error.WriteLine($"  [legilux] {works.Count} works, {byWork.Values.Sum(v => v.Count)} versions, {manifestationMaps.Xml.Count} xml manifestations");
        }
        finally { _initLock.Release(); }
    }

    private async Task<Dictionary<string, LicenceChannelEvidence>>
        SelectManifestationLicencesAsync(
            IReadOnlyCollection<string> manifestationIdentifiers,
            CancellationToken ct)
    {
        var evidence = new Dictionary<string, LicenceChannelEvidence>(
            StringComparer.Ordinal);
        foreach (var batch in manifestationIdentifiers.Chunk(
                     ManifestationLicenceBatchSize))
        {
            var page = await _sparql.SelectTermsPageAsync(
                ManifestationLicenceQuery(batch), ct);
            var limit = checked(
                batch.Length * ManifestationLicenceClaimsMaximum + 1);
            if (page.Rows.Count >= limit)
                throw new InvalidDataException(
                    $"Legilux licence evidence for {batch.Length} manifestations exceeds {limit - 1} rows.");
            foreach (var item in ParseManifestationLicenceBatch(batch, page))
                if (!evidence.TryAdd(item.Key, item.Value))
                    throw new InvalidDataException(
                        "A Legilux manifestation appeared in more than one licence batch.");
        }
        return evidence;
    }

    internal static Dictionary<string, LicenceChannelEvidence>
        ParseManifestationLicenceBatch(
            IReadOnlyCollection<string> requestedManifestations,
            SparqlSelectPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _ = ManifestationValues(requestedManifestations);
        var requested = requestedManifestations.ToHashSet(StringComparer.Ordinal);
        var evidence = requested.ToDictionary(
            manifestation => manifestation,
            _ => LicenceChannelEvidence.NotObserved,
            StringComparer.Ordinal);
        if (!page.Variables.Contains("m") || !page.Variables.Contains("license"))
            return evidence;

        var rowsByManifestation = new Dictionary<string,
            List<Dictionary<string, SparqlTerm>>>(StringComparer.Ordinal);
        foreach (var row in page.Rows)
        {
            var manifestation = RequiredUriTerm(row, "m").Value;
            if (!requested.Contains(manifestation))
                throw new InvalidDataException(
                    "A Legilux licence row is outside the requested VALUES batch.");
            if (!rowsByManifestation.TryGetValue(manifestation, out var rows))
                rowsByManifestation[manifestation] = rows = [];
            rows.Add(row);
        }

        foreach (var manifestation in requested)
        {
            if (!rowsByManifestation.TryGetValue(manifestation, out var rows))
                continue;
            var terms = rows.Where(row => row.ContainsKey("license"))
                .Select(row => row["license"]).ToArray();
            var unboundRows = rows.Count - terms.Length;
            if (unboundRows == 1 && terms.Length == 0 && rows.Count == 1)
            {
                evidence[manifestation] = LicenceChannelEvidence.Absent;
                continue;
            }
            if (unboundRows > 0
                || terms.Length is < 1 or > ManifestationLicenceClaimsMaximum
                || terms.Distinct().Count() != terms.Length)
            {
                evidence[manifestation] =
                    LegiluxLicenceEvidence.InvalidFromSparqlTerms(terms);
                continue;
            }
            evidence[manifestation] = LegiluxLicenceEvidence.FromSparqlTerms(terms);
        }
        return evidence;
    }

    private static string ManifestationValues(
        IReadOnlyCollection<string> manifestations)
    {
        if (manifestations.Count is < 1 or > ManifestationLicenceBatchSize)
            throw new ArgumentOutOfRangeException(nameof(manifestations));
        if (manifestations.Distinct(StringComparer.Ordinal).Count()
            != manifestations.Count)
            throw new InvalidDataException(
                "A Legilux licence VALUES batch contains duplicate manifestations.");
        return string.Join(' ', manifestations.Select(manifestation =>
        {
            if (!Uri.TryCreate(manifestation, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || !string.Equals(uri.Host, "data.legilux.public.lu",
                    StringComparison.OrdinalIgnoreCase)
                || !uri.IsDefaultPort
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || !string.Equals(uri.AbsoluteUri, manifestation,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "A Legilux manifestation has an invalid official URI.");
            return $"<{manifestation}>";
        }));
    }

    internal static (
        Dictionary<string, LegiluxManifestation> Xml,
        Dictionary<string, LegiluxManifestation> Pdf) BuildManifestationMaps(
            IEnumerable<Dictionary<string, SparqlTerm>> manifestationRows,
            IReadOnlyDictionary<string, LicenceChannelEvidence> licences)
    {
        var xmlFiles = new Dictionary<string, LegiluxManifestation>(
            StringComparer.Ordinal);
        var pdfFiles = new Dictionary<string, LegiluxManifestation>(
            StringComparer.Ordinal);
        foreach (var group in manifestationRows.GroupBy(
                     row => RequiredUriTerm(row, "m").Value,
                     StringComparer.Ordinal))
        {
            var consolidation = SingleUriTerm(group, "c");
            var expression = SingleUriTerm(group, "expr");
            var format = SingleUriTerm(group, "fmt");
            var file = SingleUriTerm(group, "file");
            var transport = OfficialManifestationTransport(format, file);
            var manifestation = new LegiluxManifestation(
                group.Key,
                file.Value,
                transport.FetchUri,
                licences.GetValueOrDefault(
                    group.Key, LicenceChannelEvidence.NotObserved));
            var key =
                $"{consolidation.Value}|{LangCode(LastSegment(expression.Value))}";
            AddExactManifestation(
                transport.Format == "pdf" ? pdfFiles : xmlFiles,
                key,
                manifestation);
        }
        return (xmlFiles, pdfFiles);
    }

    private static SparqlTerm RequiredUriTerm(
        Dictionary<string, SparqlTerm> row, string name)
    {
        if (!row.TryGetValue(name, out var term)
            || !string.Equals(term.Type, "uri", StringComparison.Ordinal)
            || !Uri.TryCreate(term.Value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException(
                $"Legilux manifestation row has no valid URI term for {name}.");
        return term;
    }

    private static SparqlTerm SingleUriTerm(
        IEnumerable<Dictionary<string, SparqlTerm>> rows, string name)
    {
        var terms = rows.Select(row => RequiredUriTerm(row, name))
            .Distinct().ToArray();
        if (terms.Length != 1)
            throw new InvalidDataException(
                $"One Legilux manifestation is bound to multiple {name} values.");
        return terms[0];
    }

    internal static ManifestationTransport OfficialManifestationTransport(
        SparqlTerm format, SparqlTerm file)
    {
        const string formatRoot =
            "http://data.legilux.public.lu/resource/authority/user-format/";
        var formatName = format.Type == "uri" ? format.Value switch
        {
            formatRoot + "xml" => "xml",
            formatRoot + "pdf" => "pdf",
            _ => null,
        } : null;
        if (formatName is null)
            throw new InvalidDataException(
                "Legilux manifestation row has an unsupported format URI.");

        if (!string.Equals(file.Type, "uri", StringComparison.Ordinal)
            || !Uri.TryCreate(file.Value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "data.legilux.public.lu",
                StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.StartsWith("/filestore/", StringComparison.Ordinal)
            || uri.AbsolutePath.Length == "/filestore/".Length)
            throw new InvalidDataException(
                "Legilux manifestation file is not an exact official filestore URI.");

        return new ManifestationTransport(
            formatName, "https://legilux.public.lu" + uri.AbsolutePath);
    }

    private static void AddExactManifestation(
        Dictionary<string, LegiluxManifestation> manifestations,
        string key,
        LegiluxManifestation manifestation)
    {
        if (!manifestations.TryAdd(key, manifestation))
            throw new InvalidDataException(
                $"Legilux expression {key} has multiple manifestations of one format.");
    }

    internal static IEnumerable<string[]> HeldWorkMetadataBatches(IEnumerable<string> workUris) =>
        workUris.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Chunk(HeldWorkMetadataBatchSize);

    internal static string HeldWorkValues(IReadOnlyCollection<string> works)
    {
        if (works.Count is < 1 or > HeldWorkMetadataBatchSize)
            throw new ArgumentOutOfRangeException(nameof(works));
        return string.Join(' ', works.Select(work =>
        {
            if (!Uri.TryCreate(work, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
                throw new InvalidDataException($"Legilux held work has an invalid URI: {work}");
            return $"<{uri.AbsoluteUri}>";
        }));
    }

    private async Task<List<Dictionary<string, string>>> SelectHeldWorkMetadataAsync(
        IReadOnlyCollection<string> heldWorkUris,
        Func<IReadOnlyCollection<string>, string> query,
        int perWorkMaximum,
        int totalMaximum,
        string label,
        CancellationToken ct)
    {
        var all = new List<Dictionary<string, string>>();
        foreach (var batch in HeldWorkMetadataBatches(heldWorkUris))
        {
            var requestedMaximum = checked(batch.Length * perWorkMaximum + 1);
            if (requestedMaximum > SparqlClient.SortedTopMaximum)
                throw new InvalidOperationException("Legilux metadata batch exceeds the Virtuoso sorted-result window.");
            var page = await _sparql.SelectAsync(query(batch), ct);
            if (page.Count >= requestedMaximum)
                throw new InvalidDataException(
                    $"Legilux {label} metadata for {batch.Length} held works exceeds {requestedMaximum - 1} rows.");
            if (page.Count > totalMaximum - all.Count)
                throw new InvalidDataException(
                    $"Legilux {label} metadata exceeds the configured maximum of {totalMaximum} rows.");

            var expected = batch.ToHashSet(StringComparer.Ordinal);
            foreach (var group in page.GroupBy(row => RequiredMetadataWork(row, label), StringComparer.Ordinal))
            {
                if (!expected.Contains(group.Key))
                    throw new InvalidDataException(
                        $"Legilux {label} metadata returned an unrequested work {group.Key}.");
                if (group.Count() > perWorkMaximum)
                    throw new InvalidDataException(
                        $"Legilux {label} metadata for {group.Key} exceeds {perWorkMaximum} rows.");
            }
            all.AddRange(page);
            Console.Error.WriteLine($"  [legilux] fetched {all.Count} {label} rows");
        }
        return all;
    }

    private static string RequiredHeldWork(Dictionary<string, string> row) =>
        row.TryGetValue("work", out var work) && !string.IsNullOrWhiteSpace(work)
            ? work
            : throw new InvalidDataException("Legilux catalogue row is missing work.");

    private static string RequiredMetadataWork(Dictionary<string, string> row, string label) =>
        row.TryGetValue("work", out var work) && !string.IsNullOrWhiteSpace(work)
            ? work
            : throw new InvalidDataException($"Legilux {label} metadata row is missing work.");

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(
        value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // Adapter-internal identifier reading is permitted (F4 exempts adapters).
    private static string LastSegment(string uri) => uri.TrimEnd('/').Split('/')[^1];

    private static string LangCode(string langUri) => LastSegment(langUri) switch
    {
        "FRA" => "fr",
        "DEU" => "de",
        "ENG" => "en",
        "LTZ" => "lb",
        var other => other.ToLowerInvariant()
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
        if (_pdfFiles is null || !_pdfFiles.TryGetValue(key, out var manifestation))
            return new(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The publisher did not enumerate an alternative PDF manifestation.");
        var licence = ManifestationLicenceEvidence.AwaitingFile(
            manifestation.Identifier,
            manifestation.FileIdentifier,
            manifestation.SparqlLicence);
        var url = manifestation.FetchUri;

        // Thematic collections are excluded here, and this is not a nicety. A RECUEIL or
        // CODE_RECUEIL is a shelf, not an instrument: its PDF concatenates every act on the shelf,
        // runs to tens of megabytes, and would be re-fetched for each of the hundreds of dates the
        // shelf is restamped. Learned the hard way: without this the run filled the disk with
        // 638 MB of code-communal before failing.
        if (version.TypeCode is "RECUEIL" or "CODE_RECUEIL")
        {
            Console.Error.WriteLine($"  [legilux] pdf belongs to a thematic collection, not an act; skipped: {version.Id.Value}");
            return new(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The publisher PDF is a thematic collection rather than one legal act.",
                Licence: licence);
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
        if (resp is null)
            return new(SourceBodyStatus.RetryExhausted,
                Detail: sent.FailureDetail ?? "The official PDF endpoint exhausted the retry policy.",
                Attempts: sent.Attempts,
                Licence: licence);
        RequireOfficialResponseUri(resp, url);
        if (sent.RetryExhausted)
            return new(SourceBodyStatus.RetryExhausted,
                Detail: sent.FailureDetail ?? "The official PDF endpoint exhausted the retry policy.",
                Attempts: sent.Attempts,
                Licence: licence);
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"  [legilux] pdf fetch failed ({(int)resp.StatusCode}): {url}");
            return resp.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => new(SourceBodyStatus.PermanentNotFound,
                    Detail: "The official PDF manifestation returned HTTP 404.", Attempts: sent.Attempts,
                    Licence: licence),
                System.Net.HttpStatusCode.Gone => new(SourceBodyStatus.Gone,
                    Detail: "The official PDF manifestation returned HTTP 410.", Attempts: sent.Attempts,
                    Licence: licence),
                _ => new(SourceBodyStatus.ParserFailure,
                    Detail: $"Official PDF acquisition failed with HTTP {(int)resp.StatusCode}.",
                    Attempts: sent.Attempts,
                    Licence: licence),
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
                Attempts: sent.Attempts,
                Licence: licence);
        }
        await using var pdfStream = await resp.Content.ReadAsStreamAsync(ct);
        var bounded = await ReadBoundedBody(pdfStream, checked((int)CapBytes), ct);
        var bytes = bounded.Bytes;
        if (bounded.LimitExceeded)
        {
            Console.Error.WriteLine($"  [legilux] pdf exceeds the cap once read; skipped: {url}");
            return new(SourceBodyStatus.Oversized,
                Detail: $"Official PDF exceeded the {CapBytes}-byte acquisition limit.",
                Attempts: sent.Attempts,
                Licence: licence);
        }
        if (bytes.Length < 5 || bytes[0] != 0x25 || bytes[1] != 0x50 || bytes[2] != 0x44 || bytes[3] != 0x46)
        {
            Console.Error.WriteLine($"  [legilux] response is not a PDF; discarded: {url}");
            return new(SourceBodyStatus.ParserFailure,
                Detail: "The official PDF response did not have a PDF signature.",
                Attempts: sent.Attempts,
                Licence: licence);
        }
        return SourceManifestationFetch.Retrieved(
            new ManifestationFetch(gazette ? "pdf-memorial" : "pdf",
                [new ManifestationMember(url.Split('/')[^1], bytes)], PublicUrl(url)),
            sent.Attempts, licence);
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
