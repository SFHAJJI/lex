using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// The licence-page index <c>lex-lu-sparql-licence-pages/1</c> (LU-DATA-DESIGN v2 §1.2 and §2.2, R1 with rights, step 2): every
/// <c>jolux:license</c> row of the acquisition's retained SPARQL assertion pages, by manifestation, with the page's response
/// body digest and the time the acquisition observed the page. The rights batches name only the vocabulary observation, not
/// the page, so a single-channel admission finds the page that carried its licence triple here, and cites and discloses it.
/// </summary>
/// <remarks>
/// It reads the rights evidence index the SPARQL channel names (its deliveries, each with its pages A and B and their
/// observation times), each page's HTTP evidence, and the page's body, all by digest from custody; nothing is fetched. A page
/// is observed live when its observation time is not before <c>liveFrom</c>, the resumption of a resumed acquisition (null:
/// every page is live). A manifestation's page is the latest that holds its row; a manifestation whose pages disagree about
/// its licences has none, so no single-channel admission can rest on it.
/// </remarks>
public sealed class LuxembourgSparqlLicencePageIndex
{
    internal const string Schema = "lex-lu-sparql-licence-pages/1";

    private const string LicencePredicate = "http://data.legilux.public.lu/resource/ontology/jolux#license";

    private readonly Dictionary<string, LuxembourgSparqlLicencePage> _pages;

    private LuxembourgSparqlLicencePageIndex(
        Dictionary<string, LuxembourgSparqlLicencePage> pages, int pageCount, int licenceRows, int disagreeing, byte[] canonical)
    {
        _pages = pages;
        PageCount = pageCount;
        LicenceRows = licenceRows;
        DisagreeingManifestations = disagreeing;
        CanonicalBytes = canonical;
        Sha256 = CustodyDigest.Of(canonical);
    }

    /// <summary>How many retained pages were read.</summary>
    public int PageCount { get; }

    /// <summary>How many <c>jolux:license</c> rows they hold.</summary>
    public int LicenceRows { get; }

    /// <summary>How many manifestations the pages disagree about (and so have no page here).</summary>
    public int DisagreeingManifestations { get; }

    /// <summary>How many manifestations have a page.</summary>
    public int Manifestations => _pages.Count;

    /// <summary>The index's canonical bytes: one row per manifestation, sorted, as the record names it.</summary>
    public ReadOnlyMemory<byte> CanonicalBytes { get; }

    public string Sha256 { get; }

    /// <summary>The latest retained page holding <paramref name="manifestationIri"/>'s licence row, or null.</summary>
    public LuxembourgSparqlLicencePage? PageOf(string manifestationIri) => _pages.GetValueOrDefault(manifestationIri);

    /// <summary>
    /// The index over the rights evidence index <paramref name="rightsIndexSha256"/> (the SPARQL channel's enumeration),
    /// read from <paramref name="store"/> by digest.
    /// </summary>
    internal static async Task<LuxembourgSparqlLicencePageIndex> BuildAsync(
        ICustodyStore store, string rightsIndexSha256, DateTimeOffset? liveFrom, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        var pages = new List<(string HttpEvidenceSha256, DateTimeOffset ObservedAt)>();
        using (var index = JsonDocument.Parse(
                   await CustodyRestore.ReadByDigestCheckedAsync(store, rightsIndexSha256, cancellationToken).ConfigureAwait(false)))
        {
            foreach (var delivery in index.RootElement.GetProperty("deliveries").EnumerateArray())
            {
                var times = delivery.GetProperty("ObservationTimes");
                foreach (var leg in new[] { "PagesA", "PagesB" })
                {
                    if (!delivery.TryGetProperty(leg, out var legPages) || legPages.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var observed = times.GetProperty(leg).EnumerateArray().Select(static time => time.GetDateTimeOffset()).ToArray();
                    var ordered = legPages.GetProperty("Pages").EnumerateArray().ToArray();
                    if (observed.Length != ordered.Length)
                    {
                        throw new CustodyIntegrityException("A retained SPARQL delivery names another number of page times than pages.");
                    }

                    for (var position = 0; position < ordered.Length; position++)
                    {
                        pages.Add((
                            ordered[position].GetProperty("Evidence").GetProperty("HttpEvidenceRef").GetProperty("Sha256").GetString()!,
                            observed[position].ToUniversalTime()));
                    }
                }
            }
        }

        var rows = new Dictionary<string, List<(string Body, DateTimeOffset ObservedAt, SortedSet<string> Licences)>>(StringComparer.Ordinal);
        var licenceRows = 0;
        foreach (var (httpEvidence, observedAt) in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string body;
            using (var evidence = JsonDocument.Parse(
                       await CustodyRestore.ReadByDigestCheckedAsync(store, httpEvidence, cancellationToken).ConfigureAwait(false)))
            {
                var hops = evidence.RootElement.GetProperty("hops").EnumerateArray().ToArray();
                body = hops[^1].GetProperty("readback_sha256").GetString()!;
            }

            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, body, cancellationToken).ConfigureAwait(false);
            var onPage = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var (subject, licence) in LicenceRowsOf(bytes.Span))
            {
                licenceRows++;
                if (!onPage.TryGetValue(subject, out var set))
                {
                    onPage[subject] = set = new SortedSet<string>(StringComparer.Ordinal);
                }

                set.Add(licence);
            }

            foreach (var (subject, licences) in onPage)
            {
                if (!rows.TryGetValue(subject, out var list))
                {
                    rows[subject] = list = [];
                }

                list.Add((body, observedAt, licences));
            }
        }

        var byManifestation = new Dictionary<string, LuxembourgSparqlLicencePage>(StringComparer.Ordinal);
        var disagreeing = 0;
        foreach (var (manifestation, list) in rows)
        {
            if (list.Select(static row => string.Join(" ", row.Licences)).Distinct(StringComparer.Ordinal).Count() != 1)
            {
                disagreeing++;
                continue;
            }

            var latest = list.OrderByDescending(static row => row.ObservedAt).ThenBy(static row => row.Body, StringComparer.Ordinal).First();
            try
            {
                byManifestation[manifestation] = new LuxembourgSparqlLicencePage(
                    manifestation,
                    new SourceArtifactRef(
                        ContentDerivedIdentity.DeriveUuidUrn("lex-lu-sparql-licence-page/1", Encoding.UTF8.GetBytes(latest.Body)), latest.Body),
                    latest.Licences.ToArray(),
                    latest.ObservedAt,
                    liveFrom is not { } from || latest.ObservedAt >= from);
            }
            catch (ArgumentException)
            {
                // A subject that is not one exact resource IRI, or a licence that is not one exact IRI: no page stands for it.
                disagreeing++;
            }
        }

        var canonical = Encoding.UTF8.GetBytes(ContractJson.Serialize(new IndexDocument(
            Schema,
            rightsIndexSha256,
            liveFrom?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture),
            pages.Count,
            licenceRows,
            disagreeing,
            byManifestation.Values
                .OrderBy(static page => page.ManifestationIri, StringComparer.Ordinal)
                .Select(static page => new IndexRow(
                    page.ManifestationIri,
                    page.LicenceIris.ToArray(),
                    page.PageRef.Sha256,
                    page.ObservedAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture),
                    page.ObservedLive))
                .ToArray())));
        return new LuxembourgSparqlLicencePageIndex(byManifestation, pages.Count, licenceRows, disagreeing, canonical);
    }

    /// <summary>The (subject, licence IRI) of every <c>jolux:license</c> binding of a SPARQL JSON results page whose object is an IRI.</summary>
    internal static IEnumerable<(string Subject, string Licence)> LicenceRowsOf(ReadOnlySpan<byte> page)
    {
        var found = new List<(string, string)>();
        var reader = new Utf8JsonReader(page, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
        // Walk to results.bindings, then read each binding object's subject, predicate, object and object_kind values.
        // Depth is the nesting after each token: the root object 1, results 2, the bindings array 3, a binding 4, a term 5.
        var depth = 0;
        var inBindings = false;
        string? key = null;
        string? field = null;
        string? subject = null, predicate = null, value = null, kind = null;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    if (!inBindings && depth == 2 && reader.ValueTextEquals("bindings"))
                    {
                        key = "bindings";
                    }
                    else if (inBindings && depth == 4)
                    {
                        field = reader.GetString();
                    }
                    else if (inBindings && depth == 5 && reader.ValueTextEquals("value"))
                    {
                        key = "value";
                    }

                    break;
                case JsonTokenType.StartArray:
                    depth++;
                    if (key == "bindings" && depth == 3)
                    {
                        inBindings = true;
                        key = null;
                    }

                    break;
                case JsonTokenType.EndArray:
                    depth--;
                    if (inBindings && depth == 2)
                    {
                        inBindings = false;
                    }

                    break;
                case JsonTokenType.StartObject:
                    depth++;
                    if (inBindings && depth == 4)
                    {
                        subject = predicate = value = kind = null;
                    }

                    break;
                case JsonTokenType.EndObject:
                    if (inBindings && depth == 4 && predicate == LicencePredicate && subject is not null && value is not null &&
                        kind == "iri")
                    {
                        found.Add((subject, value));
                    }

                    depth--;
                    break;
                case JsonTokenType.String:
                    if (inBindings && depth == 5 && key == "value")
                    {
                        var text = reader.GetString();
                        switch (field)
                        {
                            case "subject": subject = text; break;
                            case "predicate": predicate = text; break;
                            case "object": value = text; break;
                            case "object_kind": kind = text; break;
                        }

                        key = null;
                    }

                    break;
            }
        }

        return found;
    }

    private sealed record IndexDocument(
        string Schema,
        string RightsEvidenceIndexSha256,
        string? LiveFrom,
        int Pages,
        int LicenceRows,
        int DisagreeingManifestations,
        IndexRow[] Manifestations);

    private sealed record IndexRow(string ManifestationIri, string[] LicenceIris, string PageSha256, string ObservedAt, bool ObservedLive);
}
