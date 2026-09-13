using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lex.V3.Tests.Contracts.Source.Luxembourg.LuxembourgGazetteBodyFixtures;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// #419 slice 6a: the Gazette channel. Every Gazette-PDF listing of an as-published act gets one
/// typed outcome - admitted only with retained bytes, rejected only on the publisher's licenceSCL,
/// typed gaps otherwise - with its dual-channel rights carried unchanged; and an act's set is
/// complete over its join or refused.
/// </summary>
[TestClass]
public sealed class LuxembourgGazetteBodyTests
{
    [TestMethod]
    public void AnAcceptedPdfListingWithRetainedBytesIsAdmitted()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdfa"));
        var listing = LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single();

        var body = LuxembourgGazetteBodyDisposition.Create(listing, Receipt('a'), FetchEvidence);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.Admitted, body.Outcome);
        Assert.IsNull(body.GapReason);
        Assert.AreEqual("gazette_body_admitted", body.ReasonCode);
        Assert.AreEqual(LuxembourgUserFormatToken.PdfA, body.Format);
        Assert.AreEqual(ActLoi1, body.PublisherActIri);
        Assert.AreEqual(ManifestationOf(ActLoi1, "fr", "pdfa"), body.ManifestationIri);
        Assert.AreEqual(ItemOf(ActLoi1, "fr", "pdfa"), body.ItemIri);
        Assert.AreEqual(new string('a', 64), body.TransportByteSha256);
        Assert.AreEqual(FetchEvidence, body.FetchEvidenceRef);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, body.RightsResolution.Disposition);
        Assert.AreEqual(64, body.IdentitySha256.Length);
    }

    [TestMethod]
    public void AnAcceptedListingWithoutBytesIsTheTypedGapBodyNotRetained()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var listing = LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single();

        var body = LuxembourgGazetteBodyDisposition.Create(listing, null, null);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.TypedGap, body.Outcome);
        Assert.AreEqual(LuxembourgGazetteBodyGapReason.BodyNotRetained, body.GapReason);
        Assert.AreEqual("gazette_gap_body_not_retained", body.ReasonCode);
        Assert.IsNull(body.RetainedTransportBytes);
        Assert.IsNull(body.TransportByteSha256);
    }

    [TestMethod]
    public void BytesAndTheirEvidenceAreAssertedTogetherOrNotAtAll()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var listing = LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single();

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodyDisposition.Create(listing, Receipt('a'), null));
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodyDisposition.Create(listing, null, FetchEvidence));
    }

    /// <summary>The one rights state that withholds: the publisher's licenceSCL. Never held, so never with bytes.</summary>
    [TestMethod]
    public void APublisherMarkedNotReusableListingIsRejectedAndNeverHeld()
    {
        var join = JoinLicenceScl(ActLoi1, Candidate(ActLoi1, "fr", "pdfa"));
        var listing = LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single();
        CollectionAssert.AreEqual(
            new[] { LuxembourgBodyBlockerCode.PublisherMarkedNotReusable }, listing.BlockerCodes.ToArray(),
            "the premise: the join withholds on exactly the rights blocker.");

        var body = LuxembourgGazetteBodyDisposition.Create(listing, null, null);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.Rejected, body.Outcome);
        Assert.IsNull(body.GapReason);
        Assert.AreEqual("gazette_body_publisher_marked_not_reusable", body.ReasonCode);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl, body.RightsResolution.Disposition);
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodyDisposition.Create(listing, Receipt('a'), FetchEvidence),
            "retaining a withheld body would hold what the rule withholds.");
    }

    /// <summary>Structure outranks the publisher's statement: a quarantined tuple is not this act's body.</summary>
    [TestMethod]
    public void AQuarantinedTupleIsAStructuralGapWhateverThePublisherSaid()
    {
        var quarantined = Candidate(
            ActLoi1, "fr", "pdf",
            disposition: LuxembourgWemiCandidateDisposition.TypedQuarantine,
            blockers: [LuxembourgWemiBlockerCode.ObservationMismatch]);
        var join = JoinLicenceScl(ActLoi1, quarantined);
        var listing = LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single();
        CollectionAssert.Contains(listing.BlockerCodes.ToArray(), LuxembourgBodyBlockerCode.PublisherMarkedNotReusable,
            "the premise: the publisher's statement is also there, and loses.");

        var body = LuxembourgGazetteBodyDisposition.Create(listing, null, null);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.TypedGap, body.Outcome);
        Assert.AreEqual(LuxembourgGazetteBodyGapReason.WemiTupleTypedQuarantine, body.GapReason);
        Assert.AreEqual("gazette_gap_wemi_tuple_typed_quarantine", body.ReasonCode);
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodyDisposition.Create(listing, Receipt('a'), FetchEvidence));
    }

    [TestMethod]
    public void ATupleReachedFromAnotherRootIsNotThisActsBody()
    {
        var foreign = Candidate(ActLoi1, "fr", "pdf", rootOverride: ActRgd2);
        var join = JoinAgreedCcBy(ActLoi1, foreign);
        var listing = LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single();

        var body = LuxembourgGazetteBodyDisposition.Create(listing, null, null);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.TypedGap, body.Outcome);
        Assert.AreEqual(LuxembourgGazetteBodyGapReason.WemiRootMismatch, body.GapReason);
    }

    [TestMethod]
    public void AnXmlListingIsNotAGazetteBody()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "xml"), Candidate(ActLoi1, "fr", "pdf"));
        var xml = join.Candidates.Single(static c => c.WemiCandidate.FormatIri == FormatXml);

        Assert.IsFalse(LuxembourgGazetteBodyDisposition.IsGazettePdf(xml));
        Assert.AreEqual(1, LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Count, "only the pdf listing is a Gazette body.");
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodyDisposition.Create(xml, null, null));
        Assert.AreEqual("candidate", thrown.ParamName);
    }

    /// <summary>
    /// The stated limitation, pinned rather than hidden: today's in-file channel cannot read a PDF,
    /// so the second channel resolves to a typed quarantine. Under the settled rule that state is
    /// recorded on the body and withholds nothing.
    /// </summary>
    [TestMethod]
    public void TheRightsResolutionIsCarriedUnchangedWhenTheSecondChannelCannotReadThePdf()
    {
        var join = JoinSecondChannelCannotReadPdf(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var listing = LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single();
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected,
            listing.RightsResolution.Disposition,
            "the premise: the dual-channel resolver records the rejected reading.");
        Assert.AreEqual(LuxembourgBodyCandidateDisposition.AcceptedCandidate, listing.Disposition,
            "the premise: the join withholds on licenceSCL only.");

        var body = LuxembourgGazetteBodyDisposition.Create(listing, Receipt('b'), FetchEvidence);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.Admitted, body.Outcome);
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected,
            body.RightsResolution.Disposition,
            "recorded for the serving gate, unchanged.");
    }

    [TestMethod]
    public void TheIdentityDigestBindsActManifestationItemBytesAndOutcome()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var listing = LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single();

        var admitted = LuxembourgGazetteBodyDisposition.Create(listing, Receipt('a'), FetchEvidence);
        var again = LuxembourgGazetteBodyDisposition.Create(listing, Receipt('a'), FetchEvidence);
        var otherBytes = LuxembourgGazetteBodyDisposition.Create(listing, Receipt('c'), FetchEvidence);
        var notRetained = LuxembourgGazetteBodyDisposition.Create(listing, null, null);

        Assert.AreEqual(admitted.IdentitySha256, again.IdentitySha256, "same claim, same identity.");
        Assert.AreNotEqual(admitted.IdentitySha256, otherBytes.IdentitySha256, "other bytes, other identity.");
        Assert.AreNotEqual(admitted.IdentitySha256, notRetained.IdentitySha256, "other outcome, other identity.");
    }

    // ---- the set ----

    [TestMethod]
    public void ASetHoldsExactlyOneDispositionPerGazetteListingInCanonicalOrder()
    {
        var pdfa = Candidate(ActLoi1, "fr", "pdfa");
        var pdf = Candidate(ActLoi1, "fr", "pdf");
        var join = JoinAgreedCcBy(ActLoi1, pdf, Candidate(ActLoi1, "fr", "xml"), pdfa);
        var listings = LuxembourgGazetteBodySet.GazetteCandidatesOf(join);
        Assert.AreEqual(2, listings.Count);
        var bodies = listings.Select(l => LuxembourgGazetteBodyDisposition.Create(
            l, l.WemiCandidate.FormatIri == FormatPdfA ? Receipt('a') : null,
            l.WemiCandidate.FormatIri == FormatPdfA ? FetchEvidence : null)).ToArray();

        var forward = LuxembourgGazetteBodySet.Create(join, bodies);
        var reversed = LuxembourgGazetteBodySet.Create(join, bodies.Reverse().ToArray());

        Assert.AreEqual(ActLoi1, forward.PublisherActIri);
        Assert.AreEqual(Run, forward.ObservationRunRef);
        Assert.IsNull(forward.ActGap);
        CollectionAssert.AreEqual(
            new[] { ManifestationOf(ActLoi1, "fr", "pdf"), ManifestationOf(ActLoi1, "fr", "pdfa") },
            forward.Bodies.Select(static b => b.ManifestationIri).ToArray(),
            "ordinal manifestation order: .../pdf before .../pdfa.");
        CollectionAssert.AreEqual(
            forward.Bodies.Select(static b => b.IdentitySha256).ToArray(),
            reversed.Bodies.Select(static b => b.IdentitySha256).ToArray(),
            "two delivery orders, one set.");
        Assert.AreEqual(1, forward.AdmittedCount);
        Assert.AreEqual(0, forward.RejectedCount);
        Assert.AreEqual(1, forward.GapCount);
    }

    [TestMethod]
    public void ASetRefusesAMissingExtraDuplicateOrForeignDisposition()
    {
        var pdfa = Candidate(ActLoi1, "fr", "pdfa");
        var pdf = Candidate(ActLoi1, "fr", "pdf");
        var join = JoinAgreedCcBy(ActLoi1, pdfa, pdf);
        var bodies = LuxembourgGazetteBodySet.GazetteCandidatesOf(join)
            .Select(static l => LuxembourgGazetteBodyDisposition.Create(l, null, null)).ToArray();

        // missing one
        var missing = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(join, [bodies[0]]));
        StringAssert.Contains(missing.Message, "missing");

        // duplicate
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(join, [bodies[0], bodies[1], bodies[0]]));

        // a listing this join does not hold
        var otherJoin = JoinAgreedCcBy(ActLoi1, pdfa, pdf, Candidate(ActLoi1, "de", "pdf"));
        var extra = LuxembourgGazetteBodySet.GazetteCandidatesOf(otherJoin)
            .Select(static l => LuxembourgGazetteBodyDisposition.Create(l, null, null))
            .Single(static b => b.ManifestationIri.EndsWith("/de/pdf", StringComparison.Ordinal));
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(join, [bodies[0], bodies[1], extra]));

        // another act's body
        var foreignJoin = JoinAgreedCcBy(ActRgd2, Candidate(ActRgd2, "fr", "pdf"));
        var foreign = LuxembourgGazetteBodyDisposition.Create(
            LuxembourgGazetteBodySet.GazetteCandidatesOf(foreignJoin).Single(), null, null);
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(join, [bodies[0], bodies[1], foreign]));
        StringAssert.Contains(thrown.Message, ActRgd2);
    }

    [TestMethod]
    public void AnActWithTuplesButNoPdfHasTheActGapNoGazettePdfCandidate()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "xml"));

        var set = LuxembourgGazetteBodySet.Create(join, []);

        Assert.AreEqual(0, set.Bodies.Count);
        Assert.AreEqual(LuxembourgGazetteActGapReason.NoGazettePdfCandidate, set.ActGap);
    }

    [TestMethod]
    public void AnActWhoseRealizationPathIsUnprovenHasThatActGap()
    {
        var join = JoinAgreedCcBy(ActLoi1);
        CollectionAssert.Contains(
            join.RootBlockerCodes.ToArray(), LuxembourgBodyRootBlockerCode.PublisherRealizationPathUnproven,
            "the premise: no tuple at all.");

        var set = LuxembourgGazetteBodySet.Create(join, []);

        Assert.AreEqual(LuxembourgGazetteActGapReason.RealizationPathUnproven, set.ActGap);
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(
                join,
                [LuxembourgGazetteBodyDisposition.Create(
                    LuxembourgGazetteBodySet.GazetteCandidatesOf(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"))).Single(),
                    null, null)]),
            "a disposition for a listing this join does not hold.");
    }

    [TestMethod]
    public void EveryVocabularyMemberHasItsExactWireToken()
    {
        CollectionAssert.AreEqual(
            new[] { "\"admitted\"", "\"rejected\"", "\"typed_gap\"" },
            Enum.GetValues<LuxembourgGazetteBodyOutcome>().Select(static m => ContractJson.Serialize(m)).ToArray());
        CollectionAssert.AreEqual(
            new[] { "\"wemi_tuple_typed_quarantine\"", "\"wemi_root_mismatch\"", "\"body_not_retained\"" },
            Enum.GetValues<LuxembourgGazetteBodyGapReason>().Select(static m => ContractJson.Serialize(m)).ToArray());
        CollectionAssert.AreEqual(
            new[] { "\"realization_path_unproven\"", "\"no_gazette_pdf_candidate\"" },
            Enum.GetValues<LuxembourgGazetteActGapReason>().Select(static m => ContractJson.Serialize(m)).ToArray());
    }

    [TestMethod]
    public void NullsAreCallerContractViolations()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgGazetteBodyDisposition.Create(null!, null, null));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgGazetteBodyDisposition.IsGazettePdf(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgGazetteBodySet.GazetteCandidatesOf(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgGazetteBodySet.Create(null!, []));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LuxembourgGazetteBodySet.Create(JoinAgreedCcBy(ActLoi1), null!));
    }
}
