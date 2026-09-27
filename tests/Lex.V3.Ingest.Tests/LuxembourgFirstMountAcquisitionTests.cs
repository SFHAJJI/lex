using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Luxembourg half of the first mount, driven end to end by real code on one scripted
/// transport that answers by what each request asks rather than by call order: the four
/// vocabulary partitions (answered with the publisher's required vocabulary, so the profile opens
/// from observation), the act's S, A and G families over its ELI range, the Gazette PDF fetch, and
/// the Akoma Ntoso producers. The produced run then builds a corpus through the envelope helper and
/// <see cref="LexCorpus6Builder"/>.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgFirstMountAcquisitionTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string Types = "http://data.legilux.public.lu/resource/authority/resource-type/";
    private const string Formats = "http://data.legilux.public.lu/resource/authority/user-format/";
    private const string Parent = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
    private const string Act = Parent + "/jo";
    private const string Consolidation = Parent + "/consolide/20260201";
    private const string Expression = Consolidation + "/fr";
    private const string Manifestation = Expression + "/pdf";
    private const string Item = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/consolide/20260201/fr/pdf/consolide.pdf";
    private static readonly string CcBy = VerifiedLuxembourgSourceProfile.AdmittingLicence;

    private static readonly (string Subject, string Predicate, string Value)[] Assertions =
    [
        (Consolidation, RdfType, Jolux + "Consolidation"),
        (Consolidation, Jolux + "typeDocument", Types + "LOI"),
        (Consolidation, Jolux + "isMemberOf", Parent),
        (Consolidation, Jolux + "isRealizedBy", Expression),
        (Expression, RdfType, Jolux + "Expression"),
        (Expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
        (Expression, Jolux + "isEmbodiedBy", Manifestation),
        (Manifestation, RdfType, Jolux + "Manifestation"),
        (Manifestation, Jolux + "userFormat", Formats + "pdf"),
        (Manifestation, Jolux + "isExemplifiedBy", Item),
        (Manifestation, Jolux + "license", CcBy),
        (Act, RdfType, Jolux + "Act"),
        (Act, Jolux + "typeDocument", Types + "LOI"),
        (Act, Jolux + "isMemberOf", Parent),
    ];

    internal static readonly LuxembourgActRange ActRange = new("act-2026", Parent, Parent + "0");

    [TestMethod]
    public async Task OneActIsAcquiredEndToEndFromAnObservedVocabularyAndBuildsACorpus()
    {
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var handler = new LuxembourgFamilyHandler(PdfBytes());
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);

        var result = await Acquisition(store, handler).RunAsync(
            ActRange, renderers, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        var run = result.Run!;
        Assert.IsNull(run.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, run.Completion);
        Assert.IsTrue(run.CorpusRecordSet!.Set.Records.Any(static record => record.Body.Kind == CorpusBodyRecordKind.Held),
            "the act's consolidated PDF must be a held corpus body.");

        // The profile opened from what the transport served, and its evidence is retained.
        var profile = result.Profile!;
        Assert.AreEqual(result.VocabularyEvidenceRef, profile.Snapshot.ObservationRef);
        _ = await CustodyRestore.ReadByDigestCheckedAsync(store, result.VocabularyEvidenceRef!.Sha256, CancellationToken.None);
        Assert.AreEqual(
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary.Count,
            profile.ObservedIriVocabulary.Intersect(VerifiedLuxembourgSourceProfile.RequiredIriVocabulary).Count(),
            "every required value was observed, none was manufactured.");

        // The Akoma Ntoso producers ran over the held bodies (a PDF act yields no articles).
        Assert.IsNotNull(result.AknInventory);
        Assert.IsNotNull(result.AknLegalContent);

        // Traffic: four vocabulary families and three act families, each with its own robots
        // bootstrap, then the one Gazette PDF GET.
        CollectionAssert.AreEquivalent(new[] { "P", "T", "C", "O", "S", "A", "G" }, handler.FamiliesSeen.ToArray());
        Assert.AreEqual(1, handler.DocumentRequests.Count, string.Join(" | ", handler.DocumentRequests));

        // Renderer sources are the checkout's two Luxembourg renderer files.
        var checkout = CheckoutRoot();
        CollectionAssert.AreEqual(
            LuxembourgRendererSources.RendererFiles.Select(path => Sha256(File.ReadAllBytes(Path.Combine(checkout, path)))).ToArray(),
            new[] { renderers.Query.Reference.Sha256, renderers.DocumentFetch.Reference.Sha256 });

        // And the run builds a corpus through the same chain the mount will use.
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: run,
            luxembourgStore: store);
        var built = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        Assert.IsTrue(built.VerifiedSet.Set.Members.Count > 0);
    }

    [TestMethod]
    public async Task ARefusedVocabularyPartitionIsATypedRefusalBeforeAnyActTraffic()
    {
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var handler = new LuxembourgFamilyHandler(PdfBytes(), sparqlRobots: "User-agent: Lex\nDisallow: /\n");
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);

        var result = await Acquisition(store, handler).RunAsync(
            ActRange, renderers, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(LuxembourgFirstMountAcquisitionRefusal.VocabularyRefused, result.Refusal);
        StringAssert.Contains(result.Detail, "vocabulary family P");
        Assert.IsNull(result.Run);
        Assert.AreEqual(0, handler.FamiliesSeen.Count, "no query is sent past a refused robots bootstrap.");
        Assert.AreEqual(0, handler.DocumentRequests.Count);
    }

    /// <summary>
    /// Review finding on this slice: the executor's refusal for a class the ceiling cannot pay for
    /// carries the count it learned, so that a run that was too small still says how big the class
    /// is. The composition root keeps that count in its detail; the next run is sized from it.
    /// </summary>
    [TestMethod]
    public async Task AVocabularyPartitionTheCeilingCannotPayForNamesItsCountInTheRefusal()
    {
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var handler = new LuxembourgFamilyHandler(PdfBytes());
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);

        var result = await Acquisition(store, handler).RunAsync(
            ActRange, renderers, WireRequestBudget.OfWireRequests(3), CancellationToken.None);

        Assert.AreEqual(LuxembourgFirstMountAcquisitionRefusal.VocabularyRefused, result.Refusal);
        StringAssert.Contains(result.Detail, "vocabulary family P");
        StringAssert.Contains(result.Detail, nameof(LuxembourgEnumerationRefusal.WireBudgetExhausted));
        var predicates = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary
            .Where(static value => value.Kind is LuxembourgVocabularyKind.AssertionPredicate or LuxembourgVocabularyKind.RelationPredicate)
            .Select(static value => value.FullIri).Distinct(StringComparer.Ordinal).Count();
        StringAssert.Contains(result.Detail, $"count={predicates}");
        CollectionAssert.AreEquivalent(new[] { "P" }, handler.FamiliesSeen.ToArray());
        Assert.IsNull(result.Run);
    }

    [TestMethod]
    public async Task AVocabularyMissingARequiredValueRefusesTheProfileAndNamesTheValue()
    {
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var handler = new LuxembourgFamilyHandler(PdfBytes(), omitRequiredPredicate: Jolux + "cites");
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);

        var result = await Acquisition(store, handler).RunAsync(
            ActRange, renderers, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(LuxembourgFirstMountAcquisitionRefusal.ProfileRefused, result.Refusal);
        StringAssert.Contains(result.Detail, nameof(LuxembourgProfileResolutionFailureCode.IncompleteVocabulary));
        StringAssert.Contains(result.Detail, "cites");
        Assert.IsNotNull(result.VocabularyEvidenceRef, "the observation is retained even when the profile refuses.");
        Assert.IsNull(result.Run);
        CollectionAssert.AreEquivalent(new[] { "P", "T", "C", "O" }, handler.FamiliesSeen.ToArray());
    }

    [TestMethod]
    public async Task RendererSourcesRefuseACheckoutWithoutTheRendererFiles()
    {
        var empty = Path.Combine(Path.GetTempPath(), "lex-v3-no-lu-checkout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => LuxembourgRendererSources.FromCheckoutAsync(
                new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(), empty, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    // ---- Shared plumbing. ----

    private static LuxembourgFirstMountAcquisition Acquisition(ICustodyStore store, HttpMessageHandler handler) =>
        new(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);

    internal static byte[] PdfBytes() => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "LuDocumentFetch", "lu-pdf-consolidated-2020-04-08-a265.bin"));

    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new AssertFailedException("Checkout root not found above the test binaries.");
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Answers each request by what it asks. Robots on either Legilux host; a SPARQL POST is
    /// classified into its set by the shape of its query text (each set's template is distinct:
    /// see <c>LuxembourgQueryPlan</c>), a count query gets the row count, a first page gets the
    /// rows and a continuation page (its cursor VALUES bind <c>has_cursor</c> true) gets the empty
    /// page that ends the pass; a filestore GET gets the PDF. Nothing depends on call order.
    /// </summary>
    internal sealed class LuxembourgFamilyHandler(
        byte[] pdfBytes,
        string? sparqlRobots = null,
        string? omitRequiredPredicate = null) : HttpMessageHandler
    {
        // The page query binds VALUES (?has_cursor ?last_key_1 ...) { (1 "last key" ...) } on a
        // continuation and (0 "" ...) on a first page (LuxembourgQueryPlan, has_cursor:uint).
        private static readonly Regex ContinuationCursor = new(@"\(\s*1\s+""", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private readonly List<string> _families = [];
        private readonly List<string> _documents = [];

        internal IReadOnlyList<string> FamiliesSeen
        {
            get
            {
                lock (_families)
                {
                    return _families.Distinct(StringComparer.Ordinal).ToArray();
                }
            }
        }

        internal IReadOnlyList<string> DocumentRequests
        {
            get
            {
                lock (_documents)
                {
                    return _documents.ToArray();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/robots.txt")
            {
                var robots = uri.Host == "data.legilux.public.lu" && sparqlRobots is not null
                    ? sparqlRobots
                    : "User-agent: *\nAllow: /\n";
                return Text(request, robots);
            }

            if (request.Method == HttpMethod.Get)
            {
                lock (_documents)
                {
                    _documents.Add(uri.AbsoluteUri);
                }

                var expected = new Uri(Item.Replace("http://data.legilux.public.lu/", "https://legilux.public.lu/", StringComparison.Ordinal)).AbsoluteUri;
                return uri.AbsoluteUri == expected
                    ? Binary(request, pdfBytes, "application/pdf")
                    : Binary(request, [], "text/plain", HttpStatusCode.NotFound);
            }

            // The Luxembourg channel posts its query form-encoded (query=<percent-encoded SPARQL>).
            var raw = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var body = raw.StartsWith("query=", StringComparison.Ordinal)
                ? WebUtility.UrlDecode(raw["query=".Length..])
                : raw;
            var family = Classify(body);
            lock (_families)
            {
                _families.Add(family);
            }

            // A and G project subject, predicate and object beside the keys; every other set
            // projects the keys only. A count is the number of rows the first page carries.
            var continuation = ContinuationCursor.IsMatch(body);
            var count = body.Contains("COUNT(*)", StringComparison.Ordinal);
            var rows = RowsFor(family);
            var page = family switch
            {
                "A" when count => LuxembourgAcquisitionTestFixture.CountJson(Assertions.Length),
                "A" => AssertionRows(continuation ? [] : Assertions),
                "G" when count => LuxembourgAcquisitionTestFixture.CountJson(0),
                "G" => RelationRows(),
                _ when count => LuxembourgAcquisitionTestFixture.CountJson(rows.Count),
                _ when continuation => LuxembourgAcquisitionTestFixture.EmptyRowsJson(),
                _ => LuxembourgAcquisitionTestFixture.RowsJson(rows.ToArray()),
            };
            return LuxembourgAcquisitionTestFixture.JsonResponse(request, page);
        }

        private static string Classify(string body)
        {
            if (body.Contains("?subject a ?type", StringComparison.Ordinal)) return "T";
            if (body.Contains("isIRI(?subject) && isIRI(?object)", StringComparison.Ordinal)) return "G";
            if (body.Contains("AS ?object_kind", StringComparison.Ordinal)) return "A";
            if (body.Contains("BIND(STR(?concept) AS ?key_1)", StringComparison.Ordinal)) return "C";
            if (body.Contains("BIND(STR(?object) AS ?key_1)", StringComparison.Ordinal)) return "O";
            if (body.Contains("BIND(STR(?predicate) AS ?key_1)", StringComparison.Ordinal)) return "P";
            if (body.Contains("BIND(STR(?subject) AS ?key_1)", StringComparison.Ordinal)) return "S";
            throw new AssertFailedException("Unclassified Luxembourg query: " + body[..Math.Min(body.Length, 3000)]);
        }

        /// <summary>The key_1 values each family lists: the publisher's required vocabulary per kind, the act's subjects, no relations.</summary>
        private IReadOnlyList<string> RowsFor(string family)
        {
            var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;
            IEnumerable<string> values = family switch
            {
                "P" => required.Where(static value => value.Kind is LuxembourgVocabularyKind.AssertionPredicate or LuxembourgVocabularyKind.RelationPredicate)
                    .Select(static value => value.FullIri).Where(iri => iri != omitRequiredPredicate),
                "T" => required.Where(static value => value.Kind == LuxembourgVocabularyKind.ResourceClass).Select(static value => value.FullIri),
                // C lists SKOS concepts under the publisher's authority roots, the Legilux licence
                // root among them; O is enumerated over the Creative Commons range only.
                "C" => required.Where(static value => value.Kind is LuxembourgVocabularyKind.TypeDocument or LuxembourgVocabularyKind.UserFormat
                        or LuxembourgVocabularyKind.LegalValue or LuxembourgVocabularyKind.Language
                        || (value.Kind == LuxembourgVocabularyKind.Licence && value.FullIri.StartsWith("http://data.legilux.public.lu/", StringComparison.Ordinal)))
                    .Select(static value => value.FullIri),
                "O" => required.Where(static value => value.Kind == LuxembourgVocabularyKind.Licence
                        && value.FullIri.StartsWith("http://creativecommons.org/", StringComparison.Ordinal))
                    .Select(static value => value.FullIri),
                "S" => [Consolidation, Expression, Manifestation, Act],
                "A" => Assertions.Select(static assertion => assertion.Subject),
                "G" => [],
                _ => throw new AssertFailedException(family),
            };
            return values.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        }

        private static string AssertionRows((string Subject, string Predicate, string Value)[] rows)
        {
            var variables = new[]
            {
                "subject", "predicate", "object", "object_kind", "datatype_iri", "language_tag",
                "key_1", "key_2", "key_3", "key_4", "key_5", "key_6",
            };
            var bindings = rows.OrderBy(static row => row.Subject, StringComparer.Ordinal)
                .ThenBy(static row => row.Predicate, StringComparer.Ordinal).ThenBy(static row => row.Value, StringComparer.Ordinal)
                .Select(row =>
                {
                    var values = new[] { row.Subject, row.Predicate, row.Value, "iri", "", "", row.Subject, row.Predicate, "iri", row.Value, "", "" };
                    return variables.Select((name, index) => (name, term: new { type = index < 3 ? "uri" : "literal", value = values[index] }))
                        .ToDictionary(static field => field.name, static field => field.term);
                });
            return JsonSerializer.Serialize(new
            {
                head = new { link = Array.Empty<string>(), vars = variables },
                results = new { distinct = false, ordered = true, bindings },
            });
        }

        /// <summary>The relation family's empty page: G projects subject, predicate and object beside the six keys and this act relates to nothing.</summary>
        private static string RelationRows() => JsonSerializer.Serialize(new
        {
            head = new
            {
                link = Array.Empty<string>(),
                vars = new[] { "subject", "predicate", "object", "key_1", "key_2", "key_3", "key_4", "key_5", "key_6" },
            },
            results = new { distinct = false, ordered = true, bindings = Array.Empty<object>() },
        });

        private static HttpResponseMessage Text(HttpRequestMessage request, string body) =>
            Binary(request, Encoding.UTF8.GetBytes(body), "text/plain");

        private static HttpResponseMessage Binary(HttpRequestMessage request, byte[] bytes, string mediaType, HttpStatusCode status = HttpStatusCode.OK)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.TryAddWithoutValidation("Content-Type", mediaType);
            content.Headers.ContentLength = bytes.Length;
            return new HttpResponseMessage(status) { Version = HttpVersion.Version11, RequestMessage = request, Content = content };
        }
    }
}
