using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU half of the first mount, driven end to end by real code on one scripted transport: the
/// adapter run over Appendix A's first seed (the harness's own scripts), the Formex population with
/// its per-expression manifestation enumerations, and the legal-notice GET on eur-lex, all under one
/// wire ceiling and with renderer sources read from this checkout's own renderer files. The produced
/// three inputs then build a corpus through the envelope helper and <see cref="LexCorpus6Builder"/>,
/// with the real notice route in the rights matrix.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EuFirstMountAcquisitionTests
{
    private const string NoticeUri = EuLegalNoticeEvidence.RequestedUri;
    private const string NoticeMediaType = "text/html; charset=UTF-8";
    private static readonly byte[] NoticeBody = Encoding.UTF8.GetBytes(
        "<!DOCTYPE html><html lang=\"en\"><head><title>Legal notice</title></head><body><h1>Legal notice</h1></body></html>\n");

    [TestMethod]
    public async Task OneWorkIsAcquiredEndToEndAndBuildsACorpusWithTheRealNoticeRoute()
    {
        var root = EuAxiomWiringHarness.SeedRoot(null);
        var celex = EuAxiomWiringHarness.Seed(null).Celex;
        var expressionIri = root + ".0001";
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new CompositeHandler(
            EuAxiomWiringHarness.Scripts(
                root, static seedRoot => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(seedRoot),
                expressionIri: expressionIri),
            formexListedTypesByExpressionIri: new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [expressionIri] = ["fmx4", "xhtml"],
            });
        var renderers = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);

        var result = await Acquisition(store, handler).RunAsync(
            celex, renderers, EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        var run = result.Run!;
        Assert.IsNull(run.Refusal);
        Assert.AreEqual(EuQueryExecutionCompletion.AllFamiliesProven, run.Completion);
        Assert.AreEqual(1, run.ObservedExpressionCount);

        // The Formex population is the run's own, and its one expression is eligible and deferred.
        var formex = result.Formex!;
        Assert.AreSame(run, formex.Reconciliation!.Run);
        Assert.AreEqual(1, formex.EligibleExpressionCount);
        Assert.AreEqual(EuFormexPackageOutcomeKind.Refused, formex.Reconciliation.Outcomes.Single().Kind);

        // The notice route names the run's corpus identity, and FromRoute accepts the pair.
        var notice = result.LegalNotice!;
        var corpusRunIdentity = run.CorpusRecordSet!.Set.Records[0].RunIdentity;
        Assert.AreEqual(corpusRunIdentity, notice.Route!.RunIdentity);
        _ = EuLegalNoticeEvidence.FromRoute(notice.Route, notice.TerminalRequest!);

        // Renderer sources are the checkout's own renderer files, held in the run's custody.
        var checkout = CheckoutRoot();
        var expectedDigests = EuRendererSources.RendererFiles
            .Select(path => Sha256(File.ReadAllBytes(Path.Combine(checkout, path))))
            .ToArray();
        var actualDigests = new[]
        {
            renderers.Census, renderers.ObjectFacts, renderers.Witness,
            renderers.DocumentFetch, renderers.FormexManifestation, renderers.LegalNotice,
        }.Select(static source => source.Reference.Sha256).ToArray();
        CollectionAssert.AreEqual(expectedDigests, actualDigests);
        foreach (var digest in expectedDigests)
        {
            _ = await CustodyRestore.ReadByDigestCheckedAsync(store, digest, CancellationToken.None);
        }

        // The publisher saw exactly the traffic the composition describes: adapter families, the
        // Formex enumeration (one expression, two passes), and the eur-lex robots and notice GETs.
        Assert.AreEqual(4, handler.FormexEnumerationRequests);
        CollectionAssert.AreEqual(
            new[] { "https://eur-lex.europa.eu/robots.txt", NoticeUri },
            handler.EurLexRequests.ToArray());
        Assert.IsTrue(handler.AdapterRequests > 0);

        // And the three inputs build a corpus whose rights matrix names the real route.
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: run,
            formexOverride: formex.Reconciliation,
            formexStore: store,
            legalNoticeOverride: (notice.Route, notice.TerminalRequest!));
        var built = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        Assert.AreEqual(
            Sha256(notice.Route.CopyCanonicalBytes()),
            built.VerifiedSet.Set.EuropeRightsMatrix.RoutedEvidenceSha256);
        Assert.AreEqual(Sha256(NoticeBody), built.VerifiedSet.Set.EuropeRightsMatrix.ResponseBodySha256);
    }

    [TestMethod]
    public async Task ACelexThatCannotBeBoundIsRefusedBeforeAnyTraffic()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new CompositeHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            new Dictionary<string, string[]>(StringComparer.Ordinal));
        var renderers = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);

        var result = await Acquisition(store, handler).RunAsync(
            "32099R9999", renderers, EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuFirstMountAcquisitionRefusal.RunRefused, result.Refusal);
        StringAssert.Contains(result.Detail, "Appendix A");
        Assert.IsNull(result.Run);
        Assert.AreEqual(0, handler.AdapterRequests + handler.FormexEnumerationRequests + handler.EurLexRequests.Count);
    }

    [TestMethod]
    public async Task ARefusedLegalNoticeIsATypedRefusalThatStillCarriesTheRunAndTheFormexPopulation()
    {
        var root = EuAxiomWiringHarness.SeedRoot(null);
        var celex = EuAxiomWiringHarness.Seed(null).Celex;
        var expressionIri = root + ".0001";
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new CompositeHandler(
            EuAxiomWiringHarness.Scripts(
                root, static seedRoot => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(seedRoot),
                expressionIri: expressionIri),
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [expressionIri] = ["fmx4"] },
            eurLexRobots: "User-agent: Lex\nDisallow: /\n");
        var renderers = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);

        var result = await Acquisition(store, handler).RunAsync(
            celex, renderers, EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuFirstMountAcquisitionRefusal.LegalNoticeRefused, result.Refusal);
        StringAssert.Contains(result.Detail, nameof(EuLegalNoticeRouteRefusal.RobotsBootstrapRefused));
        Assert.IsNotNull(result.Run);
        Assert.IsTrue(result.Formex!.Delivered);
        Assert.IsNull(result.LegalNotice);
        CollectionAssert.AreEqual(new[] { "https://eur-lex.europa.eu/robots.txt" }, handler.EurLexRequests.ToArray());
    }

    [TestMethod]
    public async Task RendererSourcesRefuseACheckoutWithoutTheRendererFiles()
    {
        var empty = Path.Combine(Path.GetTempPath(), "lex-v3-no-checkout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => EuRendererSources.FromCheckoutAsync(
                new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), empty, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    // ---- Shared plumbing. ----

    private static EuFirstMountAcquisition Acquisition(ICustodyStore store, HttpMessageHandler handler) =>
        new(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

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
    /// One transport for the whole composition: eur-lex requests (robots, then the notice) are
    /// answered here; a SPARQL body that names one of the run's expressions and asks for
    /// manifestations is the Formex enumeration, answered count/page per expression; everything else
    /// is the adapter's own traffic and goes to the harness's classifying handler with its scripts.
    /// </summary>
    internal sealed class CompositeHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _adapter;
        private readonly IReadOnlyDictionary<string, string[]> _formexListedTypesByExpressionIri;
        private readonly byte[] _eurLexRobots;
        private readonly Dictionary<string, int> _formexCallsByExpression = new(StringComparer.Ordinal);
        private readonly List<string> _eurLex = [];
        private int _adapterRequests;
        private int _formexEnumerationRequests;

        internal CompositeHandler(
            IReadOnlyDictionary<string, EuAcquisitionTestFixture.FamilyScript> scripts,
            IReadOnlyDictionary<string, string[]> formexListedTypesByExpressionIri,
            string? eurLexRobots = null)
        {
            _adapter = new HttpMessageInvoker(new EuAcquisitionTestFixture.ClassifyingHandler(scripts));
            _formexListedTypesByExpressionIri = formexListedTypesByExpressionIri;
            _eurLexRobots = eurLexRobots is null
                ? File.ReadAllBytes(Path.Combine(
                    AppContext.BaseDirectory, "Fixtures", "EuLegalNotice", "eur-lex-robots-2026-09-27.txt"))
                : Encoding.UTF8.GetBytes(eurLexRobots);
        }

        internal int AdapterRequests => Volatile.Read(ref _adapterRequests);

        internal int FormexEnumerationRequests => Volatile.Read(ref _formexEnumerationRequests);

        internal IReadOnlyList<string> EurLexRequests
        {
            get
            {
                lock (_eurLex)
                {
                    return _eurLex.ToArray();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "eur-lex.europa.eu")
            {
                lock (_eurLex)
                {
                    _eurLex.Add(uri.AbsoluteUri);
                }

                return uri.AbsolutePath == "/robots.txt"
                    ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, _eurLexRobots)
                    : EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, NoticeBody, NoticeMediaType);
            }

            if (request.Method == HttpMethod.Post && request.Content is not null)
            {
                // Buffer once so the adapter's handler can read the body again.
                var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var expressionIri = _formexListedTypesByExpressionIri.Keys.SingleOrDefault(iri =>
                    body.Contains(iri, StringComparison.Ordinal));
                if (expressionIri is not null && body.Contains("manifestation_manifests_expression", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref _formexEnumerationRequests);
                    int call;
                    lock (_formexCallsByExpression)
                    {
                        call = _formexCallsByExpression.TryGetValue(expressionIri, out var seen) ? seen : 0;
                        _formexCallsByExpression[expressionIri] = call + 1;
                    }

                    var expression = LanguageScopedExpressionFor(expressionIri);
                    var rows = _formexListedTypesByExpressionIri[expressionIri]
                        .Select(type => EuFormexManifestationEnumerationProducerTests.JsonRow(expression, type))
                        .ToArray();
                    var projection = EuFormexManifestationDiscoveryPlan.Create().CreateDeliveryProfile().ProjectionVariables;
                    return EuAcquisitionTestFixture.JsonResponse(
                        request,
                        call % 2 == 0
                            ? EuAcquisitionTestFixture.EuCountJson(rows.Length)
                            : EuAcquisitionTestFixture.RowsJson(projection, rows));
                }

                request.Content = new StringContent(body, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "application/sparql-query");
            }

            Interlocked.Increment(ref _adapterRequests);
            return await _adapter.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Only the identity and language matter to the row JSON; the enumeration producer verifies
        /// the returned rows against the run's own expression by identity, not by this object.
        /// </summary>
        private static LanguageScopedExpression LanguageScopedExpressionFor(string expressionIri)
        {
            var work = expressionIri[..expressionIri.LastIndexOf('.')];
            return LanguageScopedExpression.FromRetainedSource(
                new LanguageScopedExpressionIdentity(work, expressionIri),
                "http://publications.europa.eu/resource/authority/language/ENG",
                null,
                new Lex.V3.Contracts.Source.Core.SourceObjectRef(
                    Lex.V3.Contracts.Source.Core.SourceCoreSchemaIds.SourceObjectRef,
                    Lex.V3.Contracts.Source.Core.SourceAuthority.Cellar,
                    new Lex.V3.Contracts.Source.Core.SourceRegistryMemberRef(
                        new Lex.V3.Contracts.Source.Core.SourceArtifactRef(
                            "urn:uuid:00000000-0000-4000-8000-0000000000b1", new string('b', 64)),
                        "expression"),
                    expressionIri,
                    "eu-language-scoped-expression:" + expressionIri,
                    Sha256(Encoding.UTF8.GetBytes("eu-language-scoped-expression:" + expressionIri)),
                    new Lex.V3.Contracts.Source.Core.SourceArtifactRef(
                        "urn:uuid:00000000-0000-4000-8000-0000000000c1", new string('c', 64)),
                    null),
                LanguageScopedExpressionLineage.FromContributions(
                [
                    new(LanguageScopedExpressionContribution.IdentityAndLanguage, Receipt()),
                ]));
        }

        private static DurableBlobWriteReceipt Receipt()
        {
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, new string('d', 64), 1, CustodyClass.NightlyFloor90d);
            return new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                new CustodyPolicyEvidence(
                    CustodySchemaIds.CustodyPolicyEvidence,
                    reference,
                    CustodyVerificationProfile.FileSystemUnenforced1,
                    policyKey: null,
                    CustodyProtection.NotEnforced,
                    new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero),
                    protectedUntil: null));
        }
    }
}
