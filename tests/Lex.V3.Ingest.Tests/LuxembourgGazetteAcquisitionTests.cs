using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// #419 slice 6c: every Gazette-PDF listing of an as-published act is fetched under the run's wire
/// budget, or reused from the manifest-driven fetch that already retrieved it, and handed to the
/// accepted producer, which types every listing. Offline, through the scripted transport; no
/// publisher traffic.
/// </summary>
/// <remarks>
/// The transport is scripted BY ORDINAL, as the topology regression scripts it: a request this
/// slice did not intend to send lands on an ordinal the script does not name and fails the test,
/// which is how "fetched exactly once" and "not fetched at all" are proved rather than assumed.
/// </remarks>
[TestClass]
public sealed class LuxembourgGazetteAcquisitionTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string Types = "http://data.legilux.public.lu/resource/authority/resource-type/";
    private const string Formats = "http://data.legilux.public.lu/resource/authority/user-format/";
    private const string LegalValues = "http://data.legilux.public.lu/resource/authority/statut-version/";
    private const string Parent = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
    private const string Act = Parent + "/jo";
    private const string Expression = Act + "/fr";
    private const string ManifestationPdfA = Expression + "/pdfa";
    private const string ManifestationPdf = Expression + "/pdf";
    private const string ItemPdfA = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/pdfa/eli-etat-leg-loi-2026-01-01-a1-jo-fr-pdfa.pdf";
    private const string ItemPdf = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/pdf/eli-etat-leg-loi-2026-01-01-a1-jo-fr-pdf.pdf";
    private static readonly string CcBy = VerifiedLuxembourgSourceProfile.AdmittingLicence;
    private static readonly string LicenceScl = VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl;

    private static readonly byte[] PdfABytes = Encoding.ASCII.GetBytes("%PDF-1.7 gazette pdfa body\n%%EOF\n");
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 gazette pdf body\n%%EOF\n");

    /// <summary>
    /// The measured shape: an as-published act with a pdfa and a pdf Gazette listing. The ladder
    /// selects the pdfa for the manifest-driven fetch; the Gazette loop reuses it and fetches the
    /// pdf once; the producer admits both, each bound to the request that fetched it.
    /// </summary>
    [TestMethod]
    public async Task EveryGazetteListingOfAnAsPublishedActIsFetchedOnceAndAdmitted()
    {
        var run = await RunAsync(GazetteAssertions(), pdf: (HttpStatusCode.OK, PdfBytes));

        Assert.IsNull(run.Result.Refusal, $"{run.Result.Refusal?.Code}: {run.Result.Refusal?.Detail}");
        var sets = run.Result.GazetteBodySetsByOrdinal!;
        Assert.HasCount(1, sets, "one as-published act, one set.");
        var (ordinal, set) = sets.Single();
        Assert.AreEqual(Act, set.PublisherActIri);
        Assert.AreEqual(
            run.Result.CorpusRecordSet!.Set.Records.Single(record => record.ObjectRef.PublisherUri == Act).ObjectOrdinal,
            ordinal,
            "keyed by the act's own manifest row ordinal.");
        Assert.HasCount(2, set.Bodies);
        Assert.AreEqual(2, set.AdmittedCount);
        Assert.AreEqual(0, set.GapCount);
        Assert.AreEqual(0, set.RejectedCount);
        var byItem = set.Bodies.ToDictionary(static body => body.Candidate.WemiCandidate.ItemIri, StringComparer.Ordinal);
        Assert.AreEqual(Sha256(PdfABytes), byItem[ItemPdfA].RetainedTransportBytes!.Reference.ContentSha256, "the reused manifest fetch, not a second one.");
        Assert.AreEqual(Sha256(PdfBytes), byItem[ItemPdf].RetainedTransportBytes!.Reference.ContentSha256, "the Gazette loop's own fetch.");
        Assert.IsEmpty(run.Result.GazetteListingFetchRefusalsByOrdinal!);
        Assert.IsEmpty(run.Result.GazetteListingsWithContradictoryLegalValueByOrdinal!);
        Assert.AreEqual(2, run.DocumentRequests, "pdfa once (the ladder's own fetch, reused), pdf once.");
        // The manifest-driven record is untouched by the Gazette loop.
        Assert.AreEqual(CorpusBodyRecordKind.Held,
            run.Result.CorpusRecordSet!.Set.Records.Single(record => record.ObjectRef.PublisherUri == Act).Body.Kind);
    }

    /// <summary>A listing the publisher refuses is typed by the producer and its cause is kept beside the set.</summary>
    [TestMethod]
    public async Task AListingThePublisherRefusesIsAGapWithItsCauseBesideTheSet()
    {
        var run = await RunAsync(GazetteAssertions(), pdf: (HttpStatusCode.NotFound, []));

        Assert.IsNull(run.Result.Refusal, $"{run.Result.Refusal?.Code}: {run.Result.Refusal?.Detail}");
        var (ordinal, set) = run.Result.GazetteBodySetsByOrdinal!.Single();
        Assert.AreEqual(1, set.AdmittedCount);
        Assert.AreEqual(1, set.GapCount);
        var gap = set.Bodies.Single(body => body.Candidate.WemiCandidate.ItemIri == ItemPdf);
        Assert.AreEqual(LuxembourgGazetteBodyOutcome.TypedGap, gap.Outcome);
        Assert.AreEqual(LuxembourgGazetteBodyGapReason.BodyNotRetained, gap.GapReason);
        Assert.AreEqual(
            CorpusAcquisitionRefusalReason.NotFound,
            run.Result.GazetteListingFetchRefusalsByOrdinal![ordinal][ItemPdf],
            "the gap never stands without its cause.");
    }

    /// <summary>
    /// A listing the rules withhold is not fetched: the producer types it from the listing alone,
    /// and the transport script would fail the test on the request this loop must not send.
    /// </summary>
    [TestMethod]
    public async Task AListingTheRulesWithholdIsNotFetched()
    {
        var run = await RunAsync(
            GazetteAssertions(pdfLicence: LicenceScl), pdf: null);

        Assert.IsNull(run.Result.Refusal, $"{run.Result.Refusal?.Code}: {run.Result.Refusal?.Detail}");
        var set = run.Result.GazetteBodySetsByOrdinal!.Values.Single();
        Assert.AreEqual(1, set.AdmittedCount);
        Assert.AreEqual(1, set.RejectedCount);
        Assert.AreEqual(1, run.DocumentRequests, "only the ladder's own fetch went out.");
    }

    /// <summary>
    /// A listing whose legal-value markers contradict each other is not fetched, and is recorded as
    /// the publisher disagreeing with itself rather than as a refusal or a retention.
    /// </summary>
    [TestMethod]
    public async Task AListingWithContradictoryLegalValuesIsNotFetchedAndIsNamed()
    {
        var run = await RunAsync(
            GazetteAssertions(pdfLegalValues: [LegalValues + "officiel", LegalValues + "definitif"]), pdf: null);

        Assert.IsNull(run.Result.Refusal, $"{run.Result.Refusal?.Code}: {run.Result.Refusal?.Detail}");
        var (ordinal, set) = run.Result.GazetteBodySetsByOrdinal!.Single();
        Assert.AreEqual(1, set.AdmittedCount);
        Assert.AreEqual(1, set.GapCount);
        CollectionAssert.AreEqual(
            new[] { ManifestationPdf },
            run.Result.GazetteListingsWithContradictoryLegalValueByOrdinal![ordinal].ToArray());
        Assert.AreEqual(1, run.DocumentRequests);
    }

    /// <summary>A consolidation with a PDF manifestation is not a Gazette act: no set, no extra fetch.</summary>
    [TestMethod]
    public async Task AConsolidationWithAPdfIsNotAGazetteAct()
    {
        const string consolidation = Parent + "/consolide/20260201";
        const string consolidationExpression = consolidation + "/fr";
        const string consolidationManifestation = consolidationExpression + "/pdf";
        const string consolidationItem = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/consolide/20260201/fr/pdf/consolide.pdf";
        (string, string, string)[] assertions =
        [
            (consolidation, RdfType, Jolux + "Consolidation"),
            (consolidation, Jolux + "typeDocument", Types + "LOI"),
            (consolidation, Jolux + "isMemberOf", Parent),
            (consolidation, Jolux + "isRealizedBy", consolidationExpression),
            (consolidationExpression, RdfType, Jolux + "Expression"),
            (consolidationExpression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            (consolidationExpression, Jolux + "isEmbodiedBy", consolidationManifestation),
            (consolidationManifestation, RdfType, Jolux + "Manifestation"),
            (consolidationManifestation, Jolux + "userFormat", Formats + "pdf"),
            (consolidationManifestation, Jolux + "isExemplifiedBy", consolidationItem),
            (consolidationManifestation, Jolux + "license", CcBy),
            // The original act the consolidation proves itself against, with no listings of its own.
            (Act, RdfType, Jolux + "Act"),
            (Act, Jolux + "typeDocument", Types + "LOI"),
            (Act, Jolux + "isMemberOf", Parent),
        ];

        var run = await RunAsync(
            assertions, pdf: null, subjects: [consolidation, consolidationExpression, consolidationManifestation, Act],
            ladderItem: consolidationItem, ladderBody: PdfBytes);

        Assert.IsNull(run.Result.Refusal, $"{run.Result.Refusal?.Code}: {run.Result.Refusal?.Detail}");
        // The consolidation has no set: its PDF is not a Gazette body. The original act it proves
        // itself against is as-published and gets its set, the typed act gap for a realization path
        // the publisher never stated, so the two are told apart rather than both being an absence.
        var set = run.Result.GazetteBodySetsByOrdinal!.Values.Single();
        Assert.AreEqual(Act, set.PublisherActIri, "the original act's set; none for the consolidation.");
        Assert.IsEmpty(set.Bodies);
        Assert.AreEqual(LuxembourgGazetteActGapReason.RealizationPathUnproven, set.ActGap);
        Assert.AreEqual(1, run.DocumentRequests, "the ladder's own fetch, and nothing from the Gazette loop.");
    }

    /// <summary>
    /// A ceiling reached before the Gazette fetch refuses the run, naming the listing. Sixteen
    /// requests pay for the two census passes, the two assertion passes, and the ladder's own
    /// session (robots and the GET); the Gazette session's robots request is the seventeenth.
    /// </summary>
    [TestMethod]
    public async Task ACeilingReachedBeforeAGazetteFetchRefusesTheRunByName()
    {
        var run = await RunAsync(
            GazetteAssertions(), pdf: (HttpStatusCode.OK, PdfBytes), wireBudget: WireRequestBudget.OfWireRequests(16));

        Assert.IsNotNull(run.Result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.DocumentFetchSessionNotStarted, run.Result.Refusal.Code);
        StringAssert.Contains(run.Result.Refusal.Detail, ItemPdf);
        StringAssert.Contains(run.Result.Refusal.Detail, nameof(LuxembourgDocumentGetAttemptRefusal.WireBudgetExhausted));
        Assert.IsNull(run.Result.GazetteBodySetsByOrdinal);
    }

    /// <summary>
    /// Codex's pre-freeze objection on this slice, pinned: the receipt the delivered set carries is
    /// the exact receipt the session retained and the terminal hop names, reopened from custody, and
    /// never a replacement minted by holding the same bytes again. The store here stamps every
    /// receipt with an advancing policy observation, so a second create of identical bytes yields a
    /// receipt with a different digest, as a file-system store with a moving clock does, and the
    /// producer refuses a re-held replacement as a retention it cannot establish.
    /// </summary>
    [TestMethod]
    public async Task TheDeliveredSetCarriesTheReceiptTheRouteBoundNotAReplacement()
    {
        GazetteCustodyStore? store = null;
        var run = await RunAsync(GazetteAssertions(), pdf: (HttpStatusCode.OK, PdfBytes),
            decorate: inner => store = new GazetteCustodyStore(inner) { AdvanceObservationPerCreate = true });

        Assert.IsNull(run.Result.Refusal, $"{run.Result.Refusal?.Code}: {run.Result.Refusal?.Detail}");
        // The fixture self-checks: the ladder's own fetch is held by the session and held again by
        // the manifest-driven loop's record, and those two receipts over identical bytes differ.
        var pdfaReceipts = store!.ReceiptsByContent[Sha256(PdfABytes)];
        Assert.IsTrue(pdfaReceipts.Count >= 2, "the pdfa is held by the session and again by the manifest-driven record.");
        Assert.AreNotEqual(
            DurableBlobWriteReceiptDigest.Of(pdfaReceipts[0]), DurableBlobWriteReceiptDigest.Of(pdfaReceipts[1]),
            "a second create of identical bytes yields a different receipt digest in this store.");
        var byItem = run.Result.GazetteBodySetsByOrdinal!.Values.Single().Bodies
            .ToDictionary(static body => body.Candidate.WemiCandidate.ItemIri, StringComparer.Ordinal);
        Assert.AreEqual(
            DurableBlobWriteReceiptDigest.Of(pdfaReceipts[0]),
            DurableBlobWriteReceiptDigest.Of(byItem[ItemPdfA].RetainedTransportBytes!),
            "the reused listing carries the session's own receipt, the first one minted, not a re-hold.");
        var pdfReceipts = store.ReceiptsByContent[Sha256(PdfBytes)];
        Assert.HasCount(1, pdfReceipts, "the Gazette loop's own fetch is held once, by the session, and never again.");
        Assert.AreEqual(
            DurableBlobWriteReceiptDigest.Of(pdfReceipts[0]),
            DurableBlobWriteReceiptDigest.Of(byItem[ItemPdf].RetainedTransportBytes!));
    }

    /// <summary>A listing the publisher's robots file disallows is not fetched; the gap keeps that cause.</summary>
    [TestMethod]
    public async Task AListingRobotsDisallowsIsAGapWithItsCause()
    {
        var run = await RunAsync(GazetteAssertions(), pdf: (HttpStatusCode.OK, PdfBytes),
            pdfRobots: "User-agent: *\nDisallow: /filestore/\n");

        Assert.IsNull(run.Result.Refusal, $"{run.Result.Refusal?.Code}: {run.Result.Refusal?.Detail}");
        var (ordinal, set) = run.Result.GazetteBodySetsByOrdinal!.Single();
        Assert.AreEqual(1, set.AdmittedCount);
        Assert.AreEqual(1, set.GapCount);
        Assert.AreEqual(
            CorpusAcquisitionRefusalReason.RobotsDisallowed,
            run.Result.GazetteListingFetchRefusalsByOrdinal![ordinal][ItemPdf]);
        Assert.AreEqual(1, run.DocumentRequests, "the ladder's own fetch; the disallowed GET never went out.");
    }

    /// <summary>
    /// A producer refusal is the run's refusal, by name: here the store loses the Gazette body
    /// between the session's hold and the producer's own checked read-back, and the run refuses
    /// naming the producer's code and the listing rather than delivering a set without it.
    /// </summary>
    [TestMethod]
    public async Task AProducerRefusalRefusesTheRunByName()
    {
        // The session reads the body back by reference once when it seals the route's evidence;
        // the producer's checked read is the second, and that is the one the store fails.
        var run = await RunAsync(GazetteAssertions(), pdf: (HttpStatusCode.OK, PdfBytes),
            decorate: inner => new GazetteCustodyStore(inner) { FailReadOfContentSha256 = Sha256(PdfBytes), FailReadOrdinal = 2 });

        Assert.IsNotNull(run.Result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.GazetteBodyNotProduced, run.Result.Refusal.Code);
        StringAssert.Contains(run.Result.Refusal.Detail, nameof(LuxembourgGazetteBodyProductionRefusal.RetainedBytesUnavailable));
        StringAssert.Contains(run.Result.Refusal.Detail, ItemPdf);
        Assert.IsNull(run.Result.GazetteBodySetsByOrdinal);
    }

    /// <summary>
    /// A receipt the terminal hop names that custody cannot give back refuses the run by name: the
    /// loop reopens the session's own receipt rather than minting one, so a store that has lost it
    /// is a whole-run refusal, never a set admitted on a receipt nobody can reopen.
    /// </summary>
    [TestMethod]
    public async Task AReceiptThatWillNotReopenRefusesTheRunByName()
    {
        // The session proves its own retain by reopening the receipt once; the loop's reopen is the
        // second, and that is the one the store fails.
        var run = await RunAsync(GazetteAssertions(), pdf: (HttpStatusCode.OK, PdfBytes),
            decorate: inner => new GazetteCustodyStore(inner) { FailReopenOfReceiptForContent = Sha256(PdfBytes), FailReopenOrdinal = 2 });

        Assert.IsNotNull(run.Result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.GazetteBodyNotProduced, run.Result.Refusal.Code);
        StringAssert.Contains(run.Result.Refusal.Detail, ItemPdf);
        StringAssert.Contains(run.Result.Refusal.Detail, "could not be reopened from custody");
        Assert.IsNull(run.Result.GazetteBodySetsByOrdinal);
    }

    /// <summary>
    /// An as-published act with no Gazette listing at all still gets its set: the producer types the
    /// absence as the act's own gap, so a delivered run never leaves an as-published act unaccounted.
    /// </summary>
    [TestMethod]
    public async Task AnAsPublishedActWithoutAGazetteListingCarriesItsTypedActGap()
    {
        const string manifestationXml = Expression + "/xml";
        const string itemXml = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml/eli-etat-leg-loi-2026-01-01-a1-jo-fr-xml.xml";
        (string, string, string)[] assertions =
        [
            (Act, RdfType, Jolux + "Act"),
            (Act, Jolux + "typeDocument", Types + "LOI"),
            (Act, Jolux + "isMemberOf", Parent),
            (Act, Jolux + "isRealizedBy", Expression),
            (Expression, RdfType, Jolux + "Expression"),
            (Expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            (Expression, Jolux + "isEmbodiedBy", manifestationXml),
            (manifestationXml, RdfType, Jolux + "Manifestation"),
            (manifestationXml, Jolux + "userFormat", Formats + "xml"),
            (manifestationXml, Jolux + "isExemplifiedBy", itemXml),
            (manifestationXml, Jolux + "license", CcBy),
        ];

        var run = await RunAsync(assertions, pdf: null, subjects: [Act, Expression, manifestationXml],
            ladderItem: itemXml, ladderBody: "<akomaNtoso/>"u8.ToArray(), ladderMediaType: "application/xml");

        Assert.IsNull(run.Result.Refusal, $"{run.Result.Refusal?.Code}: {run.Result.Refusal?.Detail}");
        var (ordinal, set) = run.Result.GazetteBodySetsByOrdinal!.Single();
        Assert.AreEqual(Act, set.PublisherActIri);
        Assert.AreEqual(
            run.Result.CorpusRecordSet!.Set.Records.Single(record => record.ObjectRef.PublisherUri == Act).ObjectOrdinal,
            ordinal,
            "keyed by the act's own manifest row ordinal, the key the record set uses for it.");
        Assert.IsEmpty(set.Bodies);
        Assert.AreEqual(LuxembourgGazetteActGapReason.NoGazettePdfCandidate, set.ActGap);
        Assert.AreEqual(1, run.DocumentRequests, "the ladder's own xml fetch, and nothing from the Gazette loop.");
    }

    // ---- Fixtures. ----

    internal static async Task<LuxembourgQueryExecutionResult> CompleteForStage3BodyCompositionAsync() =>
        (await RunAsync(GazetteAssertions(), pdf: (HttpStatusCode.OK, PdfBytes))).Result;

    internal static byte[] GazetteSelectedPdfBytes() => PdfABytes.ToArray();

    internal static async Task<LuxembourgQueryExecutionResult> CompleteWithDistinctReceiptsForStage3BodyCompositionAsync() =>
        (await RunAsync(
            GazetteAssertions(),
            pdf: (HttpStatusCode.OK, PdfBytes),
            decorate: inner => new GazetteCustodyStore(inner) { AdvanceObservationPerCreate = true })).Result;

    internal static async Task<LuxembourgQueryExecutionResult> CompleteXmlForStage3BodyCompositionAsync()
    {
        const string manifestationXml = Expression + "/xml";
        const string itemXml = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml/eli-etat-leg-loi-2026-01-01-a1-jo-fr-xml.xml";
        (string, string, string)[] assertions =
        [
            (Act, RdfType, Jolux + "Act"),
            (Act, Jolux + "typeDocument", Types + "LOI"),
            (Act, Jolux + "isMemberOf", Parent),
            (Act, Jolux + "isRealizedBy", Expression),
            (Expression, RdfType, Jolux + "Expression"),
            (Expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            (Expression, Jolux + "isEmbodiedBy", manifestationXml),
            (manifestationXml, RdfType, Jolux + "Manifestation"),
            (manifestationXml, Jolux + "userFormat", Formats + "xml"),
            (manifestationXml, Jolux + "isExemplifiedBy", itemXml),
            (manifestationXml, Jolux + "license", CcBy),
        ];

        return (await RunAsync(
            assertions,
            pdf: null,
            subjects: [Act, Expression, manifestationXml],
            ladderItem: itemXml,
            ladderBody: "<akomaNtoso/>"u8.ToArray(),
            ladderMediaType: "application/xml")).Result;
    }

    internal static Task<LuxembourgQueryExecutionResult> CompletePublisherPdfForStage3BodyCompositionAsync() =>
        CompletePublisherPdfForStage3BodyCompositionAsync(PdfBytes);

    internal static async Task<LuxembourgQueryExecutionResult> CompletePublisherPdfForStage3BodyCompositionAsync(
        byte[] retainedPdfBytes)
    {
        ArgumentNullException.ThrowIfNull(retainedPdfBytes);
        const string consolidation = Parent + "/consolide/20260201";
        const string expression = consolidation + "/fr";
        const string manifestation = expression + "/pdf";
        const string item = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/consolide/20260201/fr/pdf/consolide.pdf";
        (string, string, string)[] assertions =
        [
            (consolidation, RdfType, Jolux + "Consolidation"),
            (consolidation, Jolux + "typeDocument", Types + "LOI"),
            (consolidation, Jolux + "isMemberOf", Parent),
            (consolidation, Jolux + "isRealizedBy", expression),
            (expression, RdfType, Jolux + "Expression"),
            (expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            (expression, Jolux + "isEmbodiedBy", manifestation),
            (manifestation, RdfType, Jolux + "Manifestation"),
            (manifestation, Jolux + "userFormat", Formats + "pdf"),
            (manifestation, Jolux + "isExemplifiedBy", item),
            (manifestation, Jolux + "license", CcBy),
            (Act, RdfType, Jolux + "Act"),
            (Act, Jolux + "typeDocument", Types + "LOI"),
            (Act, Jolux + "isMemberOf", Parent),
        ];

        return (await RunAsync(
            assertions,
            pdf: null,
            subjects: [consolidation, expression, manifestation, Act],
            ladderItem: item,
            ladderBody: retainedPdfBytes)).Result;
    }

    internal static async Task<LuxembourgQueryExecutionResult>
        CompleteTwoPublisherPdfsForStage3BodyCompositionAsync(byte[] firstBytes, byte[] secondBytes)
    {
        ArgumentNullException.ThrowIfNull(firstBytes);
        ArgumentNullException.ThrowIfNull(secondBytes);
        var first = PublisherPdfAssertions("first");
        var second = PublisherPdfAssertions("second");
        var documents = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [first.Item] = firstBytes,
            [second.Item] = secondBytes,
        };
        return (await RunAsync(
            [.. first.Assertions, .. second.Assertions, (Act, RdfType, Jolux + "Act"),
                (Act, Jolux + "typeDocument", Types + "LOI"), (Act, Jolux + "isMemberOf", Parent)],
            pdf: null,
            subjects: [first.Consolidation, first.Expression, first.Manifestation,
                second.Consolidation, second.Expression, second.Manifestation, Act],
            ladderItem: first.Item,
            ladderBody: firstBytes,
            ladderBodies: documents)).Result;
    }

    private static (string Consolidation, string Expression, string Manifestation, string Item,
        (string, string, string)[] Assertions) PublisherPdfAssertions(string suffix)
    {
        var consolidation = Parent + "/consolide/20260201" + suffix;
        var expression = consolidation + "/fr";
        var manifestation = expression + "/pdf";
        var item = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/consolide/20260201/fr/pdf/consolide"
            + suffix + ".pdf";
        (string, string, string)[] assertions =
        [
            (consolidation, RdfType, Jolux + "Consolidation"),
            (consolidation, Jolux + "typeDocument", Types + "LOI"),
            (consolidation, Jolux + "isMemberOf", Parent),
            (consolidation, Jolux + "isRealizedBy", expression),
            (expression, RdfType, Jolux + "Expression"),
            (expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            (expression, Jolux + "isEmbodiedBy", manifestation),
            (manifestation, RdfType, Jolux + "Manifestation"),
            (manifestation, Jolux + "userFormat", Formats + "pdf"),
            (manifestation, Jolux + "isExemplifiedBy", item),
            (manifestation, Jolux + "license", CcBy),
        ];
        return (consolidation, expression, manifestation, item, assertions);
    }

    private sealed record GazetteRun(LuxembourgQueryExecutionResult Result, int DocumentRequests);

    private static (string, string, string)[] GazetteAssertions(string? pdfLicence = null, string[]? pdfLegalValues = null)
    {
        var assertions = new List<(string, string, string)>
        {
            (Act, RdfType, Jolux + "Act"),
            (Act, Jolux + "typeDocument", Types + "LOI"),
            (Act, Jolux + "isMemberOf", Parent),
            (Act, Jolux + "isRealizedBy", Expression),
            (Expression, RdfType, Jolux + "Expression"),
            (Expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            (Expression, Jolux + "isEmbodiedBy", ManifestationPdfA),
            (Expression, Jolux + "isEmbodiedBy", ManifestationPdf),
            (ManifestationPdfA, RdfType, Jolux + "Manifestation"),
            (ManifestationPdfA, Jolux + "userFormat", Formats + "pdfa"),
            (ManifestationPdfA, Jolux + "isExemplifiedBy", ItemPdfA),
            (ManifestationPdfA, Jolux + "license", CcBy),
            (ManifestationPdf, RdfType, Jolux + "Manifestation"),
            (ManifestationPdf, Jolux + "userFormat", Formats + "pdf"),
            (ManifestationPdf, Jolux + "isExemplifiedBy", ItemPdf),
            (ManifestationPdf, Jolux + "license", pdfLicence ?? CcBy),
        };
        foreach (var legalValue in pdfLegalValues ?? [])
        {
            assertions.Add((ManifestationPdf, Jolux + "legalValue", legalValue));
        }

        return [.. assertions];
    }

    /// <summary>
    /// One full adapter run over the scripted transport: two passes of the census family, robots,
    /// two passes of the assertions family, robots and the ladder's own document fetch, then robots
    /// and the Gazette loop's one fetch. <paramref name="pdf"/> scripts the pdf listing's response,
    /// or is null when this run must not fetch it - in which case any sixteenth request fails the
    /// test.
    /// </summary>
    private static async Task<GazetteRun> RunAsync(
        (string Subject, string Predicate, string Value)[] assertions,
        (HttpStatusCode Status, byte[] Body)? pdf,
        string[]? subjects = null,
        string ladderItem = ItemPdfA,
        byte[]? ladderBody = null,
        WireRequestBudget? wireBudget = null,
        string? pdfRobots = null,
        Func<ICustodyStore, ICustodyStore>? decorate = null,
        string ladderMediaType = "application/pdf",
        IReadOnlyDictionary<string, byte[]>? ladderBodies = null)
    {
        // The census is a cursor-ordered enumeration: subjects in ascending ordinal order, or the
        // executor's own strict cursor check refuses the family as never advancing.
        subjects = (subjects ?? [Act, Expression, ManifestationPdfA, ManifestationPdf])
            .OrderBy(static subject => subject, StringComparer.Ordinal).ToArray();
        ladderBody ??= PdfABytes;
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        store = decorate?.Invoke(store) ?? store;
        var profileReceipt = await store.CreateAsync(
            "synthetic vocabulary observation for the gazette acquisition"u8.ToArray(),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        var profileEvidence = new SourceArtifactRef(NewUrn(), profileReceipt.Reference.ContentSha256);
        var profile = LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            profileEvidence, profileEvidence, VerifiedLuxembourgSourceProfile.RequiredIriVocabulary, []));
        var assertionPage = AssertionRows(assertions);
        var censusPage = LuxembourgAcquisitionTestFixture.RowsJson(subjects);
        var documentRequests = 0;
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.CountJson(subjects.Length)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(request, censusPage),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            7 or 14 => Response(request, "User-agent: *\nAllow: /\n"u8.ToArray(), "text/plain"),
            8 or 11 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.CountJson(assertions.Length)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(request, assertionPage),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(request, AssertionRows([])),
            15 when ladderBodies is null =>
                Document(request, ladderItem, HttpStatusCode.OK, ladderBody, ladderMediaType),
            // Every document GET runs in its own session, and a session bootstraps robots first: the
            // Gazette loop's one fetch is a robots request and then the GET. A robots file that
            // disallows the listing ends that session there, and the GET is then unscripted.
            16 when pdf is not null => Response(request, Encoding.ASCII.GetBytes(pdfRobots ?? "User-agent: *\nAllow: /\n"), "text/plain"),
            17 when pdf is { } scripted && pdfRobots is null => Document(request, ItemPdf, scripted.Status, scripted.Body),
            _ when ladderBodies is not null
                && request.RequestUri?.AbsolutePath == "/robots.txt" =>
                Response(request, "User-agent: *\nAllow: /\n"u8.ToArray(), "text/plain"),
            _ when ladderBodies is not null => DocumentFromMap(request, ladderBodies),
            _ => throw new AssertFailedException($"Unexpected HTTP request {ordinal}: {request.Method} {request.RequestUri}"),
        });
        var executor = new LuxembourgRepeatedEnumerationExecutor(
            store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new LuxembourgQueryExecutionAdapter(store, executor, profile);
        var (censusRequest, censusWitness) = Partition("S", "census");
        var (assertionRequest, assertionWitness) = Partition("A", "assertions");
        var result = await adapter.RunAsync(
            [(censusRequest, censusWitness, null), (assertionRequest, assertionWitness, null)],
            null, "census", "assertions", LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(420),
            wireBudget ?? LuxembourgAcquisitionTestFixture.TestWireBudget(),
            CancellationToken.None);
        return new GazetteRun(result, documentRequests);

        HttpResponseMessage Document(HttpRequestMessage request, string item, HttpStatusCode status, byte[] body, string mediaType = "application/pdf")
        {
            documentRequests++;
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual(
                new Uri(item.Replace("http://data.legilux.public.lu/", "https://legilux.public.lu/", StringComparison.Ordinal)).AbsoluteUri,
                request.RequestUri!.AbsoluteUri);
            return Response(request, body, mediaType, status);
        }

        HttpResponseMessage DocumentFromMap(
            HttpRequestMessage request,
            IReadOnlyDictionary<string, byte[]> bodies)
        {
            var match = bodies.SingleOrDefault(pair => string.Equals(
                new Uri(pair.Key.Replace(
                    "http://data.legilux.public.lu/",
                    "https://legilux.public.lu/",
                    StringComparison.Ordinal)).AbsoluteUri,
                request.RequestUri!.AbsoluteUri,
                StringComparison.Ordinal));
            if (match.Key is null)
            {
                throw new AssertFailedException(
                    $"Unexpected mapped document request: {request.Method} {request.RequestUri}");
            }

            return Document(request, match.Key, HttpStatusCode.OK, match.Value, ladderMediaType);
        }
    }

    private static (LuxembourgPartitionRunRequest, BoundMachineRequest) Partition(string setId, string family)
    {
        var (plan, resourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var renderer = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var range = LuxembourgAcquisitionTestFixture.FullRange(family);
        return (new LuxembourgPartitionRunRequest(plan, resourceId, setId, range, renderer),
            plan.BindCount(resourceId, NewUrn(), NewUrn(), setId, LuxembourgQueryPass.Pass1, range, renderer).Request);
    }

    private static string AssertionRows((string Subject, string Predicate, string Value)[] rows)
    {
        var variables = new[]
        {
            "subject", "predicate", "object", "object_kind", "datatype_iri", "language_tag",
            "key_1", "key_2", "key_3", "key_4", "key_5", "key_6",
        };
        var bindings = rows.OrderBy(row => row.Subject, StringComparer.Ordinal)
            .ThenBy(row => row.Predicate, StringComparer.Ordinal).ThenBy(row => row.Value, StringComparer.Ordinal)
            .Select(row =>
            {
                var values = new[] { row.Subject, row.Predicate, row.Value, "iri", "", "", row.Subject, row.Predicate, "iri", row.Value, "", "" };
                return variables.Select((name, index) => (name, term: new { type = index < 3 ? "uri" : "literal", value = values[index] }))
                    .ToDictionary(field => field.name, field => field.term);
            });
        return JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = variables },
            results = new { distinct = false, ordered = true, bindings },
        });
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, byte[] bytes, string mediaType, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", mediaType);
        content.Headers.ContentLength = bytes.Length;
        return new HttpResponseMessage(status) { RequestMessage = request, Content = content };
    }

    /// <summary>
    /// Decorates the in-memory store for three calibrations: an advancing policy observation, so a
    /// second create of identical bytes yields a receipt with a different digest (as a file-system
    /// store with a moving clock does); one body whose read-back by reference fails after the
    /// session held it, so the producer's own checked read refuses; and one body whose session
    /// receipt the store then cannot give back by digest, so the loop's reopen refuses.
    /// </summary>
    internal sealed class GazetteCustodyStore(ICustodyStore inner) : ICustodyStore
    {
        private DateTimeOffset _observed = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);

        internal bool AdvanceObservationPerCreate { get; init; }

        internal string? FailReadOfContentSha256 { get; init; }

        /// <summary>Which read-back of that body fails, counting from one; the earlier ones succeed.</summary>
        internal int FailReadOrdinal { get; init; } = 1;

        /// <summary>The body whose first receipt, once minted, the store will not give back by digest.</summary>
        internal string? FailReopenOfReceiptForContent { get; init; }

        /// <summary>Which reopen of that receipt fails, counting from one; the earlier ones succeed.</summary>
        internal int FailReopenOrdinal { get; init; } = 1;

        private string? _lostReceiptDigest;

        private int _lostReceiptReopens;

        internal Dictionary<string, List<DurableBlobWriteReceipt>> ReceiptsByContent { get; } = new(StringComparer.Ordinal);

        private readonly Dictionary<string, int> _readsByContent = new(StringComparer.Ordinal);

        public async Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var receipt = await inner.CreateAsync(bytes, custodyClass, cancellationToken);
            if (AdvanceObservationPerCreate)
            {
                _observed = _observed.AddSeconds(1);
                var policy = receipt.PolicyEvidence;
                receipt = new DurableBlobWriteReceipt(receipt.Schema, receipt.Reference, new CustodyPolicyEvidence(
                    policy.Schema, policy.Reference, policy.VerificationProfile, policy.PolicyKey, policy.Protection,
                    _observed, policy.ProtectedUntil is null ? null : _observed.AddDays(91)));
            }

            if (!ReceiptsByContent.TryGetValue(receipt.Reference.ContentSha256, out var receipts))
            {
                ReceiptsByContent[receipt.Reference.ContentSha256] = receipts = [];
            }

            receipts.Add(receipt);
            if (string.Equals(receipt.Reference.ContentSha256, FailReopenOfReceiptForContent, StringComparison.Ordinal))
            {
                _lostReceiptDigest ??= DurableBlobWriteReceiptDigest.Of(receipt);
            }

            return receipt;
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken)
        {
            var reads = _readsByContent[reference.ContentSha256] = _readsByContent.GetValueOrDefault(reference.ContentSha256) + 1;
            return string.Equals(reference.ContentSha256, FailReadOfContentSha256, StringComparison.Ordinal) && reads == FailReadOrdinal
                ? throw new IOException("The fixture store lost this object between the hold and the read-back.")
                : inner.ReadAsync(reference, cancellationToken);
        }

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            string.Equals(contentSha256, _lostReceiptDigest, StringComparison.Ordinal) && ++_lostReceiptReopens == FailReopenOrdinal
                ? throw new FileNotFoundException("The fixture store lost the session's receipt for this body.")
                : inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
