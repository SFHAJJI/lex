using System.Net;
using System.Net.Http;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Formex package population of one complete EU run, produced by real code end to end: the
/// run comes from <see cref="EuAxiomWiringHarness"/> (the adapter over Appendix A's first seed), the
/// manifestation enumerations go through <see cref="EuFormexManifestationEnumerationProducer"/> and
/// the acquisition session on a scripted transport, and the result is closed by the same three
/// gates the envelope relies on. The reference composition
/// (<c>EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope</c>) fabricates enumerations with an
/// internal factory; this producer does not, so what is proven here is that a real run's
/// expressions can be enumerated, closed and reconciled, and that the corpus builder accepts it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EuFormexPackagePopulationProducerTests
{
    private const string FrenchAuthority = "http://publications.europa.eu/resource/authority/language/FRA";

    [TestMethod]
    public async Task EveryExpressionOfTheRunGetsOneTypedOutcomeAndTheReconciliationBindsToTheRun()
    {
        var (run, english, french) = await RunWithTwoExpressionsAsync();
        var handler = new FormexEnumerationHandler(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [english.Identity.PublisherExpressionId] = ["fmx4", "xhtml"],
            [french.Identity.PublisherExpressionId] = ["xhtml"],
        }, [english, french]);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();

        var result = await Producer(store, handler).RunAsync(
            run, RendererSource(), EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        var reconciliation = result.Reconciliation!;
        Assert.AreSame(run, reconciliation.Run, "the envelope checks the run by reference.");
        Assert.AreEqual(run.ObservedExpressionCount, reconciliation.Outcomes.Count);
        Assert.AreEqual(2, reconciliation.Outcomes.Count);

        // Both are publisher facts with proofs: the enumerations were delivered for real.
        Assert.AreEqual(2, result.Enumerations.Count);
        Assert.IsTrue(result.Enumerations.All(static enumeration => enumeration.Delivered));
        Assert.AreEqual(1, result.EligibleExpressionCount);
        Assert.AreEqual(8, result.ProductRequestCount, "two count/page passes per expression, robots excluded.");

        var englishOutcome = reconciliation.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.Refused, englishOutcome.Kind);
        Assert.AreEqual(EuDocumentFetchAttemptRefusal.ObservationNotExecuted, englishOutcome.AcquisitionRefusal);
        Assert.AreEqual(EuFormexPackagePopulationProducer.DeferredAcquisitionDetail, englishOutcome.Detail);

        var frenchOutcome = reconciliation.Outcomes.Single(outcome => outcome.ExpressionIdentity == french.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.NotEligible, frenchOutcome.Kind);
        Assert.IsNull(frenchOutcome.Detail);

        // No package request was sent: every product request is a manifestation enumeration.
        Assert.AreEqual(8, handler.Enumerations.Count);
        Assert.AreEqual(0, handler.OtherRequests.Count, string.Join(" | ", handler.OtherRequests));
        Assert.AreEqual(4, handler.RobotsSends, "one two-hop robots bootstrap per expression session.");
    }

    /// <summary>
    /// The mount path: the produced reconciliation feeds the main-body producer, the Stage 3
    /// envelope, the body composition, the derivation profile envelope and the corpus builder
    /// exactly as the fixture composition does, with a run whose second expression is French and
    /// therefore an unbound alternate language for the one held record.
    /// </summary>
    [TestMethod]
    public async Task TheProducedReconciliationBuildsACorpusThroughTheEnvelopeAndTheBuilder()
    {
        var (run, english, french) = await RunWithTwoExpressionsAsync();
        var handler = new FormexEnumerationHandler(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [english.Identity.PublisherExpressionId] = ["fmx4"],
            [french.Identity.PublisherExpressionId] = ["fmx4"],
        }, [english, french]);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var result = await Producer(store, handler).RunAsync(
            run, RendererSource(), EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.AreEqual(2, result.EligibleExpressionCount);

        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: run,
            formexOverride: result.Reconciliation,
            formexStore: store);

        var built = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        Assert.IsTrue(built.VerifiedSet.Set.Members.Count > 0);
    }

    [TestMethod]
    public async Task ARunThatIsNotCompleteIsRefusedBeforeAnyTraffic()
    {
        var (run, _, _) = await RunWithTwoExpressionsAsync();
        var refusedRun = EuQueryExecutionResult.Refused(
            run.Topology,
            run.FamilyOutcomes,
            new EuQueryExecutionRefusalDetail(
                EuQueryExecutionRefusal.DocumentFetchSessionNotStarted, "test: the run refused"));
        var handler = new FormexEnumerationHandler(new Dictionary<string, string[]>(StringComparer.Ordinal), []);

        var result = await Producer(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), handler).RunAsync(
            refusedRun, RendererSource(), EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuFormexPackagePopulationRefusal.RunNotComplete, result.Refusal);
        StringAssert.Contains(result.Detail, "DocumentFetchSessionNotStarted");
        Assert.AreEqual(0, handler.RobotsSends + handler.Enumerations.Count + handler.OtherRequests.Count);
        Assert.AreEqual(0, result.Enumerations.Count);
    }

    /// <summary>
    /// The eligibility population is all or nothing per family: one enumeration the wire ceiling
    /// stopped refuses the family with that enumeration's own reason, and the enumerations that
    /// were attempted still travel on the result.
    /// </summary>
    [TestMethod]
    public async Task AnEnumerationTheWireCeilingStoppedRefusesTheEligibilityPopulationWithItsReason()
    {
        var (run, english, french) = await RunWithTwoExpressionsAsync();
        var handler = new FormexEnumerationHandler(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [english.Identity.PublisherExpressionId] = ["fmx4"],
            [french.Identity.PublisherExpressionId] = ["fmx4"],
        }, [english, french]);

        // Two requests: the first session's robots bootstrap takes both (301, then 200), so the
        // first count query is the one the ceiling stops.
        var result = await Producer(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), handler).RunAsync(
            run, RendererSource(), EuAcquisitionTestFixture.SourceWitness(),
            WireRequestBudget.OfWireRequests(2), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuFormexPackagePopulationRefusal.EligibilityRefused, result.Refusal);
        StringAssert.Contains(result.Detail, nameof(EuFormexEligibilityPopulationRefusal.ExpressionEnumerationRefused));
        Assert.IsTrue(result.Enumerations.Count >= 1);
        Assert.IsFalse(result.Enumerations[0].Delivered);
        Assert.AreEqual(EuFormexManifestationEnumerationRefusal.EnumerationRefused, result.Enumerations[0].Refusal);
        StringAssert.Contains(result.Enumerations[0].Detail, "WireBudgetExhausted");
        Assert.AreEqual(0, handler.Enumerations.Count, "the ceiling held before the first product request.");
    }

    // ---- Shared plumbing. ----

    /// <summary>
    /// The adapter run over Appendix A's first seed with two expressions that are exact numeric
    /// children of the work (the shape the manifestation enumeration binds): one English, one French.
    /// </summary>
    private static async Task<(EuQueryExecutionResult Run, LanguageScopedExpression English, LanguageScopedExpression French)>
        RunWithTwoExpressionsAsync()
    {
        var root = EuPackRootCanonicalForm.TryCanonicalize(EuAppendixASeedMap.SeedsInCelexOrder[0].WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        var englishIri = root + ".0001";
        var frenchIri = root + ".0002";
        var run = await EuAxiomWiringHarness.RunAsync(
            static seedRoot => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(seedRoot),
            expressionIri: englishIri,
            additionalExpressionIri: frenchIri,
            additionalExpressionLanguageAuthority: FrenchAuthority);
        Assert.IsNull(run.Refusal, $"{run.Refusal?.Code}: {run.Refusal?.Detail}");
        var expressions = run.CorrigendumTripwires!.ProductionsByFamilyKey.Values
            .SelectMany(static production => production.Expressions!.Derivation!.Expressions)
            .ToArray();
        Assert.AreEqual(2, expressions.Length);
        Assert.AreEqual(2, run.ObservedExpressionCount);
        return (
            run,
            expressions.Single(expression => expression.Identity.PublisherExpressionId == englishIri),
            expressions.Single(expression => expression.Identity.PublisherExpressionId == frenchIri));
    }

    private static EuFormexPackagePopulationProducer Producer(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore store, HttpMessageHandler handler) =>
        new(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

    private static Lex.V3.Contracts.Source.Core.MachineQueryRendererSource RendererSource() =>
        EuAcquisitionTestFixture.BuildRendererSource(9821);

    /// <summary>
    /// Answers the SPARQL robots route and, for each manifestation enumeration, the count and page
    /// of the expression the query names, in the count/page/count/page order the two passes send.
    /// Dispatch is by the expression IRI found in the query text, never by call order, so the test
    /// does not depend on which expression the producer enumerates first.
    /// </summary>
    private sealed class FormexEnumerationHandler(
        IReadOnlyDictionary<string, string[]> listedTypesByExpressionIri,
        IReadOnlyList<LanguageScopedExpression> expressions) : HttpMessageHandler
    {
        private readonly Dictionary<string, int> _callsByExpression = new(StringComparer.Ordinal);
        private readonly List<string> _enumerations = [];
        private readonly List<string> _other = [];
        private int _robots;

        internal IReadOnlyList<string> Enumerations
        {
            get
            {
                lock (_enumerations)
                {
                    return _enumerations.ToArray();
                }
            }
        }

        internal IReadOnlyList<string> OtherRequests
        {
            get
            {
                lock (_other)
                {
                    return _other.ToArray();
                }
            }
        }

        internal int RobotsSends => Volatile.Read(ref _robots);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "publications.europa.eu" && uri.AbsolutePath == "/robots.txt")
            {
                Interlocked.Increment(ref _robots);
                return EuAcquisitionTestFixture.BinaryResponse(
                    request, HttpStatusCode.MovedPermanently, [], location: "https://op.europa.eu/robots.txt");
            }

            if (uri.Host == "op.europa.eu" && uri.AbsolutePath == "/robots.txt")
            {
                Interlocked.Increment(ref _robots);
                return EuAcquisitionTestFixture.BinaryResponse(
                    request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n"u8.ToArray(), "text/plain;charset=UTF-8");
            }

            if (request.Method != HttpMethod.Post || request.Content is null)
            {
                lock (_other)
                {
                    _other.Add($"{request.Method} {uri}");
                }

                return EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.NotFound, []);
            }

            var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var expression = expressions.SingleOrDefault(candidate =>
                body.Contains(candidate.Identity.PublisherExpressionId, StringComparison.Ordinal));
            if (expression is null || !body.Contains("manifestation_manifests_expression", StringComparison.Ordinal))
            {
                lock (_other)
                {
                    _other.Add($"POST {uri} :: {body[..Math.Min(body.Length, 120)]}");
                }

                return EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.BadRequest, []);
            }

            var iri = expression.Identity.PublisherExpressionId;
            int call;
            lock (_enumerations)
            {
                call = _callsByExpression.TryGetValue(iri, out var seen) ? seen : 0;
                _callsByExpression[iri] = call + 1;
                _enumerations.Add(iri);
            }

            var rows = listedTypesByExpressionIri[iri]
                .Select(type => EuFormexManifestationEnumerationProducerTests.JsonRow(expression, type))
                .ToArray();
            var projection = EuFormexManifestationDiscoveryPlan.Create().CreateDeliveryProfile().ProjectionVariables;
            return EuAcquisitionTestFixture.JsonResponse(
                request,
                call % 2 == 0
                    ? EuAcquisitionTestFixture.EuCountJson(rows.Length)
                    : EuAcquisitionTestFixture.RowsJson(projection, rows));
        }
    }
}
