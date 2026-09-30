using System.Net;
using System.Net.Http;
using Lex.V3.Contracts.Custody;
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
    public async Task CombinedWorkBatchProducesOneAcquiredOutcomePerOriginalExpression()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var xhtml = await FixtureAsync("new-xhtml-200-body.bin");
        var package = await FixtureAsync("new-fmx4-200-body.bin");
        var run = await EuAxiomWiringHarness.RunTwoSeedAsync(
            (first, second) => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(first, second),
            numericExpressions: true, custodyStore: store,
            documentFetchResponse: request => EuAcquisitionTestFixture.BinaryResponse(
                request, HttpStatusCode.OK,
                [.. xhtml, .. System.Text.Encoding.UTF8.GetBytes($"<!-- fixture work: {request.RequestUri} -->")],
                "application/xhtml+xml;charset=UTF-8"));
        Assert.IsNull(run.Refusal, run.Refusal?.Detail);
        var expressions = run.CorrigendumTripwires!.ProductionsByFamilyKey.Values
            .SelectMany(production => production.Expressions!.Derivation!.Expressions).ToArray();
        Assert.AreEqual(2, expressions.Select(expression => expression.Identity.PublisherWorkId).Distinct().Count());
        var handler = new FormexEnumerationHandler(expressions.ToDictionary(
            expression => expression.Identity.PublisherExpressionId, _ => new[] { "fmx4", "pdfa2a" }), expressions,
            packageResponse: request => request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, package, "application/zip")
                : null);
        var result = await Producer(store, handler).RunAsync(
            run, RendererSource(), RendererSource(), EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.AreEqual(2, result.AcquiredExpressionCount, string.Join("; ", result.Reconciliation!.Outcomes.Select(
            outcome => $"{outcome.Kind}/{outcome.NotAcquiredReason}: {outcome.Detail}")));
        CollectionAssert.AreEquivalent(expressions.Select(expression => expression.Identity).ToArray(),
            result.Reconciliation!.Outcomes.Select(outcome => outcome.ExpressionIdentity).ToArray());
        Assert.AreEqual(2, result.AnnexClassifications.Count);
        foreach (var expression in expressions)
        {
            var binding = result.AnnexClassifications.Single(classification =>
                classification.Binding.FormexBody.PublisherUri == expression.Identity.PublisherExpressionId + ".01").Binding;
            Assert.AreEqual(EuAppendixASeedMap.SeedsInCelexOrder.Single(seed =>
                seed.WorkRoot == expression.Identity.PublisherWorkId).Celex, binding.WorkCelex);
        }
    }

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
            run, RendererSource(), RendererSource(), WorkCelex, EuAcquisitionTestFixture.SourceWitness(),
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
        Assert.AreEqual(1, result.AcquiredExpressionCount);
        Assert.AreEqual(10, result.ProductRequestCount,
            "two count/page passes per expression plus the package's 303 and 200, robots excluded.");

        // The English package was fetched on the manifestation route (303 to {manifestation}/zip,
        // then the real GDPR fmx4 bytes), bound to the run's own identities and inventoried: the
        // GDPR act has no annex, so it is acquired.
        var englishOutcome = reconciliation.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.Acquired, englishOutcome.Kind, englishOutcome.Detail);
        var inventory = englishOutcome.AcquiredInventory!;
        Assert.AreEqual(0, inventory.Members.Count);
        Assert.AreEqual(EuFormexPackageAcquisitionProducer.AnnexInterpretationProfileRef, inventory.InterpretationRuleProfileRef);
        var binding = inventory.TransportBinding;
        Assert.AreEqual(english.Identity.PublisherExpressionId, binding.Expression.PublisherUri);
        Assert.AreEqual(english.Identity.PublisherExpressionId + ".01", binding.FormexBody.PublisherUri);
        Assert.AreEqual(2, binding.ResponseEvidence.Hops.Count);
        Assert.AreEqual(303, binding.ResponseEvidence.Hops[0].Status);
        Assert.AreEqual(200, binding.ResponseEvidence.Hops[1].Status);
        Assert.AreEqual(GdprFmx4Sha256, binding.RetainedZipReceipt.Reference.ContentSha256);
        _ = await CustodyRestore.ReadCheckedAsync(store, binding.RetainedZipReceipt.Reference, CancellationToken.None);

        var frenchOutcome = reconciliation.Outcomes.Single(outcome => outcome.ExpressionIdentity == french.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.NotEligible, frenchOutcome.Kind);
        Assert.IsNull(frenchOutcome.Detail);

        // Every product request is a manifestation enumeration or the one package route.
        Assert.AreEqual(8, handler.Enumerations.Count);
        CollectionAssert.AreEqual(
            new[]
            {
                "https://publications.europa.eu/resource/cellar/" + Key(english) + ".01",
                "https://publications.europa.eu/resource/cellar/" + Key(english) + ".01/zip",
            },
            handler.PackageRequests.ToArray());
        Assert.AreEqual(0, handler.OtherRequests.Count, string.Join(" | ", handler.OtherRequests));
        Assert.AreEqual(6, handler.RobotsSends, "one two-hop robots bootstrap per expression session and one for the package session.");
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
            run, RendererSource(), RendererSource(), WorkCelex, EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.AreEqual(2, result.EligibleExpressionCount);
        Assert.AreEqual(1, result.AcquiredExpressionCount);

        // The French expression is eligible, and the run holds no French body (Decision 89), so the
        // corpus could bind no outcome to it: not acquired, stated as such, and no request sent.
        var frenchOutcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == french.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.NotAcquired, frenchOutcome.Kind);
        Assert.AreEqual(EuFormexPackageNotAcquiredReason.BodyNotHeld, frenchOutcome.NotAcquiredReason);
        Assert.AreEqual(2, handler.PackageRequests.Count, "only the English package route was sent.");
        Assert.IsTrue(handler.PackageRequests.All(uri => uri.Contains(Key(english), StringComparison.Ordinal)));

        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: run,
            formexOverride: result.Reconciliation,
            formexStore: store);

        var built = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");

        // The held EU member now carries the acquired package's main body: admitted, with articles.
        var formexOutcomes = built.VerifiedSet.Set.Members
            .SelectMany(static member => member.Stage3Outcomes)
            .Where(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody)
            .ToArray();
        Assert.AreEqual(1, formexOutcomes.Length, "the produced reconciliation reaches the one held EU member.");
        Assert.AreEqual(LexCorpus6Stage3Disposition.FormexMainBodyAdmitted, formexOutcomes[0].Disposition);
        var mainBody = await new EuFormexMainBodyLegalContentProducer(store).RunAsync(result.Reconciliation, CancellationToken.None);
        var admitted = mainBody.Outcomes.Single(outcome => outcome.Source.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexMainBodyLegalContentDisposition.Admitted, admitted.Disposition, admitted.Detail);
        Assert.AreEqual(99, admitted.Articles.Count, "GDPR has 99 articles.");
    }

    /// <summary>
    /// The office's 303 may point anywhere on its host; the binding admits only the manifestation's
    /// own path. A 200 reached through a hop off that path is a refused route with its status, a
    /// typed outcome the population closes over, never an exception out of the run (review finding
    /// on this pull request: the factory used to reject a 200 and the producer threw).
    /// </summary>
    [TestMethod]
    public async Task ARedirectThatLeavesTheManifestationIsARefusedRouteEvenWhenItEndsInATwoHundred()
    {
        const string elsewhere = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000.0006.01/zip";
        var (result, handler, english, _) = await AcquireEnglishAsync(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                ? null
                : EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.SeeOther, [], location: elsewhere));

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.RouteRefused, outcome.Kind, outcome.Detail);
        Assert.AreEqual(200, outcome.ObservedStatus);
        StringAssert.Contains(outcome.Detail, "does not bind");
        Assert.AreEqual(2, handler.PackageRequests.Count, "the 303 was followed once, to the other manifestation.");
        Assert.AreEqual(10, result.ProductRequestCount);
    }

    [TestMethod]
    public async Task AManifestationTheOfficeDoesNotServeAsAPackageIsUnavailable()
    {
        var (result, handler, english, _) = await AcquireEnglishAsync(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                ? null
                : EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.NotFound, []));

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.Unavailable, outcome.Kind, outcome.Detail);
        Assert.AreEqual(404, outcome.ObservedStatus);
        Assert.AreEqual(1, handler.PackageRequests.Count);
        Assert.AreEqual(9, result.ProductRequestCount);
    }

    [TestMethod]
    public async Task AnOfficeAnswerThatIsNeitherAPackageNorAbsentIsARefusedRouteWithItsStatus()
    {
        var (result, _, english, _) = await AcquireEnglishAsync(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.InternalServerError, "busy"u8.ToArray(), "text/plain")
                : null);

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.RouteRefused, outcome.Kind, outcome.Detail);
        Assert.AreEqual(500, outcome.ObservedStatus);
        StringAssert.Contains(outcome.Detail, "500");
    }

    [TestMethod]
    public async Task ATwoHundredThatIsNotAFormexPackageIsRejectedWithTheInventoryRefusal()
    {
        var (result, _, english, _) = await AcquireEnglishAsync(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, "<html>not a package</html>"u8.ToArray(), "text/html")
                : null);

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.PackageRejected, outcome.Kind, outcome.Detail);
        Assert.AreEqual(200, outcome.ObservedStatus);
        Assert.AreEqual(EuFormexAnnexInventoryRefusal.PackageUnreadable, outcome.PackageRefusal);
    }

    /// <summary>
    /// An annex-bearing package (the real 2026 package: act, annex, descriptor, table of contents)
    /// whose held work body is the GDPR XHTML, which carries no publisher annex convention: fetched,
    /// bound and inventoried with its one annex member, then not acquired with the XHTML reason. No
    /// PDF request is sent; the ZIP stays in custody; the corpus states the typed disposition.
    /// </summary>
    [TestMethod]
    public async Task AnAnnexPackageWhoseHeldBodyHasNoAnnexConventionIsRetainedButNotAcquired()
    {
        var annexPackage = await FixtureAsync("new-fmx4-200-body.bin");
        var (result, handler, english, store) = await AcquireEnglishAsync(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, annexPackage, "application/zip")
                : null);

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.NotAcquired, outcome.Kind, outcome.Detail);
        Assert.AreEqual(EuFormexPackageNotAcquiredReason.AnnexXhtmlNotInventoried, outcome.NotAcquiredReason);
        StringAssert.Contains(outcome.Detail, "1 annex member");
        StringAssert.Contains(outcome.Detail, "PublisherAnnexConventionAbsent");
        Assert.AreEqual(0, handler.PdfRequests.Count, "no PDF is fetched for a body that cannot be inventoried.");
        Assert.AreEqual(0, result.AnnexClassifications.Count);

        // The held member reaches the corpus with the typed disposition, so the corpus states a
        // package that was fetched and not acquired, not a transport refusal; the builder's domain
        // compatibility check admits the three new members (found by the annex survey on PR #751:
        // the check ranged 16 to 24 and Validate() would have thrown inside TryBuild).
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: result.Reconciliation.Run,
            formexOverride: result.Reconciliation,
            formexStore: store);
        var built = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var disposition = built.VerifiedSet.Set.Members
            .SelectMany(static member => member.Stage3Outcomes)
            .Single(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody)
            .Disposition;
        Assert.AreEqual(LexCorpus6Stage3Disposition.FormexMainBodyPackageNotAcquired, disposition);
    }

    /// <summary>
    /// The production shape of an annex-bearing act, on the three real 2026 specimens through the
    /// real session: the held work body is the XHTML the adapter fetched, the package is fetched on
    /// the manifestation route, the work's PDF/A is fetched on the document-fetch route (the office's
    /// 303 to the PDF item, then the 200 labelled with its charset), the three bind as one annex
    /// population and the annex is classified; the expression is acquired with its classification,
    /// and the corpus carries the main body and the annex outcome for the held member.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnAnnexPackageIsAcquiredWithItsAnnexesClassifiedAgainstTheHeldXhtmlAndTheWorkPdf(bool bindCelexFromExpression)
    {
        var annexPackage = await FixtureAsync("new-fmx4-200-body.bin");
        var (result, handler, english, store) = await AcquireEnglishAsync(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                    ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, annexPackage, "application/zip")
                    : null,
            heldXhtml: await FixtureAsync("new-xhtml-200-body.bin"),
            englishTypes: ["fmx4", "pdfa2a"], bindCelexFromExpression: bindCelexFromExpression);

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.Acquired, outcome.Kind, $"{outcome.NotAcquiredReason}: {outcome.Detail}");
        Assert.AreEqual(1, result.AcquiredExpressionCount);
        Assert.AreEqual(1, outcome.AcquiredInventory!.Members.Count);
        Assert.AreEqual("ANNEX", outcome.AcquiredInventory.Members[0].Title);

        var classification = result.AnnexClassifications.Single();
        Assert.AreEqual(outcome.AcquiredInventory.IdentitySha256, classification.Binding.FormexInventoryIdentitySha256);
        Assert.AreEqual(1, classification.Members.Count);
        Assert.AreEqual(WorkCelex, classification.Binding.WorkCelex);
        Assert.AreEqual("EN", classification.Binding.Language);
        Assert.AreEqual(english.Identity.PublisherExpressionId + ".01", classification.Binding.FormexBody.PublisherUri);
        Assert.AreEqual(english.Identity.PublisherExpressionId + ".03", classification.Binding.PdfManifestation.PublisherUri);
        Assert.AreEqual("http://data.europa.eu/eli/reg_impl/2026/1965/oj", classification.Binding.PublisherWorkEli);
        _ = await CustodyRestore.ReadCheckedAsync(store, classification.Binding.PdfReceipt.Reference, CancellationToken.None);

        // The PDF route: the work-level GET with the pdfa2a accept, the 303 to the PDF item, the 200.
        CollectionAssert.AreEqual(
            new[]
            {
                "https://publications.europa.eu/resource/cellar/" + english.Identity.PublisherWorkId[(english.Identity.PublisherWorkId.LastIndexOf('/') + 1)..],
                "https://publications.europa.eu/resource/cellar/" + Key(english) + ".03/DOC_1",
            },
            handler.PdfRequests.ToArray());
        Assert.AreEqual(12, result.ProductRequestCount, "enumerations, the package's two hops and the PDF's two hops.");
        Assert.AreEqual(8, handler.RobotsSends, "four sessions (two enumerations, the package, the PDF) at two robots hops each.");

        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: result.Reconciliation.Run,
            formexOverride: result.Reconciliation,
            formexStore: store,
            formexClassifications: Stage3EvidenceEnvelopeTests.CompleteClassifications(result.Reconciliation, result.AnnexClassifications));
        var built = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var outcomes = built.VerifiedSet.Set.Members.SelectMany(static member => member.Stage3Outcomes).ToArray();
        Assert.AreEqual(LexCorpus6Stage3Disposition.FormexMainBodyAdmitted,
            outcomes.Single(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody).Disposition);
        Assert.Contains(
            LexCorpus6Builder.Stage3Outcome(classification.Members.Single()),
            outcomes.Where(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody).ToArray());
    }

    [TestMethod]
    public async Task AnAnnexPackageWhosePdfTheOfficeDoesNotServeIsRetainedButNotAcquired()
    {
        var annexPackage = await FixtureAsync("new-fmx4-200-body.bin");
        var (result, handler, english, _) = await AcquireEnglishAsync(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                    ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, annexPackage, "application/zip")
                    : request.Headers.Accept.ToString().Contains("application/pdf", StringComparison.Ordinal)
                        ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.NotFound, [])
                        : null,
            heldXhtml: await FixtureAsync("new-xhtml-200-body.bin"),
            englishTypes: ["fmx4", "pdfa2a"]);

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.NotAcquired, outcome.Kind, outcome.Detail);
        Assert.AreEqual(EuFormexPackageNotAcquiredReason.AnnexPdfNotServed, outcome.NotAcquiredReason);
        StringAssert.Contains(outcome.Detail, "404");
        Assert.AreEqual(1, handler.PdfRequests.Count);
        Assert.AreEqual(11, result.ProductRequestCount);
        Assert.AreEqual(0, result.AnnexClassifications.Count);
    }

    /// <summary>
    /// The office's 303 may hand back another expression's PDF (content negotiation at work level);
    /// the manifestation read from the terminal must descend from this expression, or the PDF binds
    /// to nothing here: <c>annex_evidence_not_bound</c>, the ZIP retained.
    /// </summary>
    [TestMethod]
    public async Task APdfServedFromAnotherExpressionDoesNotBindAndThePackageIsNotAcquired()
    {
        var annexPackage = await FixtureAsync("new-fmx4-200-body.bin");
        var (result, handler, english, _) = await AcquireEnglishAsync(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal))
                {
                    return EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, annexPackage, "application/zip");
                }

                if (request.Headers.Accept.ToString().Contains("application/pdf", StringComparison.Ordinal)
                    && !request.RequestUri.AbsolutePath.EndsWith("/DOC_1", StringComparison.Ordinal))
                {
                    var workKey = request.RequestUri.AbsolutePath[(request.RequestUri.AbsolutePath.LastIndexOf('/') + 1)..];
                    return EuAcquisitionTestFixture.BinaryResponse(
                        request, HttpStatusCode.SeeOther, [],
                        location: "http://publications.europa.eu/resource/cellar/" + workKey + ".0002.03/DOC_1");
                }

                return null;
            },
            heldXhtml: await FixtureAsync("new-xhtml-200-body.bin"),
            englishTypes: ["fmx4", "pdfa2a"]);

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.NotAcquired, outcome.Kind, outcome.Detail);
        Assert.AreEqual(EuFormexPackageNotAcquiredReason.AnnexEvidenceNotBound, outcome.NotAcquiredReason);
        StringAssert.Contains(outcome.Detail, "not a manifestation of this expression");
        Assert.AreEqual(2, handler.PdfRequests.Count);
        Assert.AreEqual(0, result.AnnexClassifications.Count);
    }

    /// <summary>
    /// The classifier's widened content-type check still refuses a PDF labelled with a different
    /// <c>type</c> than the accept that was sent (only <c>charset</c> is tolerated): the bound annexes
    /// are not classified and the package is not acquired.
    /// </summary>
    [TestMethod]
    public async Task APdfLabelledWithAnotherTypeIsNotClassifiedAndThePackageIsNotAcquired()
    {
        var annexPackage = await FixtureAsync("new-fmx4-200-body.bin");
        var pdf = await FixtureAsync("new-pdfa2a-200-body.bin");
        var (result, _, english, _) = await AcquireEnglishAsync(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                    ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, annexPackage, "application/zip")
                    : request.RequestUri.AbsolutePath.EndsWith("/DOC_1", StringComparison.Ordinal)
                        ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, pdf, "application/pdf;type=pdfa1a;charset=UTF-8")
                        : null,
            heldXhtml: await FixtureAsync("new-xhtml-200-body.bin"),
            englishTypes: ["fmx4", "pdfa2a"]);

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.NotAcquired, outcome.Kind, outcome.Detail);
        Assert.AreEqual(EuFormexPackageNotAcquiredReason.AnnexBodyNotClassified, outcome.NotAcquiredReason);
        StringAssert.Contains(outcome.Detail, "SourceEvidenceMismatch");
    }

    [TestMethod]
    public async Task AnAnnexPackageWhoseEnumerationListsNoPdfIsRetainedButNotAcquiredWithoutARequest()
    {
        var annexPackage = await FixtureAsync("new-fmx4-200-body.bin");
        var (result, handler, english, _) = await AcquireEnglishAsync(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                    ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, annexPackage, "application/zip")
                    : null,
            heldXhtml: await FixtureAsync("new-xhtml-200-body.bin"));

        var outcome = result.Reconciliation!.Outcomes.Single(outcome => outcome.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexPackageOutcomeKind.NotAcquired, outcome.Kind, outcome.Detail);
        Assert.AreEqual(EuFormexPackageNotAcquiredReason.AnnexPdfNotServed, outcome.NotAcquiredReason);
        StringAssert.Contains(outcome.Detail, "lists no pdf");
        Assert.AreEqual(0, handler.PdfRequests.Count);
    }

    private const string GdprFmx4Sha256 = "4cbf7280014b0bd3d20fc8c1d6a7c08cdcd8aaacab5ee356c07ed7d840994541";
    private static readonly string WorkCelex = EuAxiomWiringHarness.Seed(null).Celex;

    private static string Key(LanguageScopedExpression expression) =>
        expression.Identity.PublisherExpressionId[(expression.Identity.PublisherExpressionId.LastIndexOf("/cellar/", StringComparison.Ordinal) + "/cellar/".Length)..];

    /// <summary>One English eligible expression through the whole producer, with the package route answered by <paramref name="packageResponse"/> (null for the default 303 and GDPR bytes).</summary>
    private static async Task<(EuFormexPackagePopulationResult Result, FormexEnumerationHandler Handler, LanguageScopedExpression English, EuAcquisitionTestFixture.EuInMemoryCustodyStore Store)> AcquireEnglishAsync(
        Func<HttpRequestMessage, HttpResponseMessage?> packageResponse,
        byte[]? heldXhtml = null,
        string[]? englishTypes = null,
        bool bindCelexFromExpression = false)
    {
        // One store for the run and the acquisition, as in production: the annex chain reads the
        // held work body the run retained.
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (run, english, french) = await RunWithTwoExpressionsAsync(heldXhtml, store);
        var handler = new FormexEnumerationHandler(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [english.Identity.PublisherExpressionId] = englishTypes ?? ["fmx4"],
            [french.Identity.PublisherExpressionId] = ["xhtml"],
        }, [english, french], packageResponse);
        var result = bindCelexFromExpression
            ? await Producer(store, handler).RunAsync(
                run, RendererSource(), RendererSource(), EuAcquisitionTestFixture.SourceWitness(),
                EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None)
            : await Producer(store, handler).RunAsync(
                run, RendererSource(), RendererSource(), WorkCelex, EuAcquisitionTestFixture.SourceWitness(),
                EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        return (result, handler, english, store);
    }

    private static Task<byte[]> FixtureAsync(string name) => File.ReadAllBytesAsync(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", name));

    /// <summary>
    /// The harness's default run names its expression by an item-shaped IRI, not the numeric child
    /// of its work, so no manifestation enumeration can be bound for it. Every request is bound
    /// before the first is sent, so the run is refused with the offending expression named and the
    /// publisher sees no request at all.
    /// </summary>
    [TestMethod]
    public async Task AnExpressionThatIsNotTheNumericChildOfItsWorkIsRefusedBeforeAnyTraffic()
    {
        var run = await EuAxiomWiringHarness.RunAsync(
            static seedRoot => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(seedRoot));
        var handler = new FormexEnumerationHandler(new Dictionary<string, string[]>(StringComparer.Ordinal), []);

        var result = await Producer(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), handler).RunAsync(
            run, RendererSource(), RendererSource(), WorkCelex, EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuFormexPackagePopulationRefusal.ExpressionSelectionInvalid, result.Refusal);
        StringAssert.Contains(result.Detail, "/DOC_1");
        Assert.AreEqual(0, handler.RobotsSends + handler.Enumerations.Count + handler.OtherRequests.Count);
        Assert.AreEqual(0, result.Enumerations.Count);
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
            refusedRun, RendererSource(), RendererSource(), WorkCelex, EuAcquisitionTestFixture.SourceWitness(),
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
            run, RendererSource(), RendererSource(), WorkCelex, EuAcquisitionTestFixture.SourceWitness(),
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
        RunWithTwoExpressionsAsync(byte[]? heldXhtml = null, EuAcquisitionTestFixture.EuInMemoryCustodyStore? store = null)
    {
        var root = EuPackRootCanonicalForm.TryCanonicalize(EuAppendixASeedMap.SeedsInCelexOrder[0].WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        var englishIri = root + ".0001";
        var frenchIri = root + ".0002";
        var run = await EuAxiomWiringHarness.RunAsync(
            static seedRoot => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(seedRoot),
            custodyStore: store,
            documentFetchResponse: heldXhtml is null
                ? null
                : request => EuAcquisitionTestFixture.BinaryResponse(
                    request, HttpStatusCode.OK, heldXhtml, "application/xhtml+xml;charset=UTF-8"),
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
        IReadOnlyList<LanguageScopedExpression> expressions,
        Func<HttpRequestMessage, HttpResponseMessage?>? packageResponse = null) : HttpMessageHandler
    {
        private static readonly Lazy<byte[]> GdprPackage = new(() => File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-fmx4-200-body.bin")));

        private static readonly Lazy<byte[]> Pdf = new(() => File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "new-pdfa2a-200-body.bin")));

        private readonly Dictionary<string, int> _callsByExpression = new(StringComparer.Ordinal);
        private readonly List<string> _enumerations = [];
        private readonly List<string> _packages = [];
        private readonly List<string> _pdfs = [];
        private readonly List<string> _other = [];
        private int _robots;

        /// <summary>The PDF route's requests in order: the work GET, then the PDF item GET the 303 named.</summary>
        internal IReadOnlyList<string> PdfRequests
        {
            get
            {
                lock (_pdfs)
                {
                    return _pdfs.ToArray();
                }
            }
        }

        /// <summary>The package route's requests in order: the manifestation GET, then the ZIP GET the 303 named.</summary>
        internal IReadOnlyList<string> PackageRequests
        {
            get
            {
                lock (_packages)
                {
                    return _packages.ToArray();
                }
            }
        }

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

            // The package route: the manifestation GET with the fmx4 accept token answers 303 to the
            // office's own http Location on the same manifestation, and the ZIP GET answers the real
            // GDPR package, unless the test supplies its own answer for either hop.
            if (request.Method == HttpMethod.Get && uri.Host == "publications.europa.eu"
                && request.Headers.Accept.ToString().Contains("application/zip;mtype=fmx4", StringComparison.Ordinal))
            {
                lock (_packages)
                {
                    _packages.Add(uri.AbsoluteUri);
                }

                if (packageResponse?.Invoke(request) is { } supplied)
                {
                    return supplied;
                }

                return uri.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                    ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, GdprPackage.Value, "application/zip")
                    : EuAcquisitionTestFixture.BinaryResponse(
                        request, HttpStatusCode.SeeOther, [], location: "http://publications.europa.eu" + uri.AbsolutePath + "/zip");
            }

            // The PDF route: the work GET with a pdf accept answers 303 to the English expression's
            // PDF item (the office's real shape: {manifestation}/DOC_1), and the item GET answers the
            // real 2026 PDF/A labelled as the office labels it, charset included.
            if (request.Method == HttpMethod.Get && uri.Host == "publications.europa.eu"
                && request.Headers.Accept.ToString().Contains("application/pdf", StringComparison.Ordinal))
            {
                lock (_pdfs)
                {
                    _pdfs.Add(uri.AbsoluteUri);
                }

                if (packageResponse?.Invoke(request) is { } supplied)
                {
                    return supplied;
                }

                if (uri.AbsolutePath.EndsWith("/DOC_1", StringComparison.Ordinal))
                {
                    return EuAcquisitionTestFixture.BinaryResponse(
                        request, HttpStatusCode.OK, Pdf.Value, "application/pdf;type=pdfa2a;charset=UTF-8");
                }

                var english = expressions.Single(candidate =>
                    candidate.OfficialLanguage == "http://publications.europa.eu/resource/authority/language/ENG"
                    && new Uri(candidate.Identity.PublisherWorkId).AbsolutePath == uri.AbsolutePath);
                var englishKey = english.Identity.PublisherExpressionId[
                    (english.Identity.PublisherExpressionId.LastIndexOf("/cellar/", StringComparison.Ordinal) + "/cellar/".Length)..];
                return EuAcquisitionTestFixture.BinaryResponse(
                    request, HttpStatusCode.SeeOther, [],
                    location: "http://publications.europa.eu/resource/cellar/" + englishKey + ".03/DOC_1");
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
