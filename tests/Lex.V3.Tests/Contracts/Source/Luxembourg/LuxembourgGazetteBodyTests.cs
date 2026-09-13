using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lex.V3.Tests.Contracts.Source.Luxembourg.LuxembourgGazetteBodyFixtures;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// #419 slice 6a: the Gazette channel. Every Gazette-PDF listing of an as-published act gets one
/// typed outcome - admitted only when its bytes are established to have been fetched from the
/// publisher's address and retained, rejected only on the publisher's licenceSCL, typed gaps
/// otherwise - with its dual-channel rights carried unchanged and bound into a byte-stable
/// identity; and an act's set is complete over its join or refused.
/// </summary>
[TestClass]
public sealed class LuxembourgGazetteBodyTests
{
    private const string Other = "http://creativecommons.org/licenses/by-sa/4.0/";
    private const string Another = "http://creativecommons.org/licenses/by-nc/4.0/";

    [TestMethod]
    public void AnAcceptedPdfListingFetchedFromItsAddressAndRetainedIsAdmitted()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdfa"));
        var listing = Listing(join);
        var retention = Retention(listing);

        var body = LuxembourgGazetteBodyDisposition.Create(listing, retention);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.Admitted, body.Outcome);
        Assert.IsNull(body.GapReason);
        Assert.AreEqual("gazette_body_admitted", body.ReasonCode);
        Assert.AreEqual(LuxembourgUserFormatToken.PdfA, body.Format);
        Assert.AreEqual(ActLoi1, body.PublisherActIri);
        Assert.AreEqual(ManifestationOf(ActLoi1, "fr", "pdfa"), body.ManifestationIri);
        Assert.AreEqual(ItemOf(ActLoi1, "fr", "pdfa"), body.ItemIri);
        Assert.AreEqual(Digest('a'), body.TransportByteSha256);
        Assert.AreSame(retention.OfficialAddress, body.OfficialAddress);
        Assert.AreSame(retention.RetainedTransportBytes, body.RetainedTransportBytes);
        Assert.AreEqual(retention.SourceEvidence.RunIdentity, body.SourceEvidenceRunIdentity);
        Assert.IsNotNull(body.SourceObservation);
        Assert.AreEqual(retention.OfficialAddress.FetchUri.AbsoluteUri, body.SourceObservation!.RequestedUri);
        Assert.AreEqual(Digest('a'), body.SourceObservation.TransportByteSha256);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, body.RightsResolution.Disposition);
        Assert.AreEqual(64, body.IdentitySha256.Length);
        Assert.AreEqual(64, body.EpisodeSha256.Length);
    }

    [TestMethod]
    public void AnAcceptedListingWithoutARetentionIsTheTypedGapBodyNotRetained()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));

        var body = LuxembourgGazetteBodyDisposition.Create(listing, null);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.TypedGap, body.Outcome);
        Assert.AreEqual(LuxembourgGazetteBodyGapReason.BodyNotRetained, body.GapReason);
        Assert.AreEqual("gazette_gap_body_not_retained", body.ReasonCode);
        Assert.IsNull(body.RetainedTransportBytes);
        Assert.IsNull(body.OfficialAddress);
        Assert.IsNull(body.SourceObservation);
        Assert.IsNull(body.TransportByteSha256);
    }

    /// <summary>The one rights state that withholds: the publisher's licenceSCL. Never held, so never with bytes.</summary>
    [TestMethod]
    public void APublisherMarkedNotReusableListingIsRejectedAndNeverHeld()
    {
        var listing = Listing(JoinLicenceScl(ActLoi1, Candidate(ActLoi1, "fr", "pdfa")));
        CollectionAssert.AreEqual(
            new[] { LuxembourgBodyBlockerCode.PublisherMarkedNotReusable }, listing.BlockerCodes.ToArray(),
            "the premise: the join withholds on exactly the rights blocker.");

        var body = LuxembourgGazetteBodyDisposition.Create(listing, null);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.Rejected, body.Outcome);
        Assert.IsNull(body.GapReason);
        Assert.AreEqual("gazette_body_publisher_marked_not_reusable", body.ReasonCode);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl, body.RightsResolution.Disposition);
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodyDisposition.Create(listing, Retention(listing)),
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
        var listing = Listing(JoinLicenceScl(ActLoi1, quarantined));
        CollectionAssert.Contains(listing.BlockerCodes.ToArray(), LuxembourgBodyBlockerCode.PublisherMarkedNotReusable,
            "the premise: the publisher's statement is also there, and loses.");

        var body = LuxembourgGazetteBodyDisposition.Create(listing, null);

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.TypedGap, body.Outcome);
        Assert.AreEqual(LuxembourgGazetteBodyGapReason.WemiTupleTypedQuarantine, body.GapReason);
        Assert.AreEqual("gazette_gap_wemi_tuple_typed_quarantine", body.ReasonCode);
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodyDisposition.Create(listing, Retention(listing)));
    }

    [TestMethod]
    public void ATupleReachedFromAnotherRootIsNotThisActsBody()
    {
        var foreign = Candidate(ActLoi1, "fr", "pdf", rootOverride: ActRgd2);
        var listing = Listing(JoinAgreedCcBy(ActLoi1, foreign));

        var body = LuxembourgGazetteBodyDisposition.Create(listing, null);

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
            () => LuxembourgGazetteBodyDisposition.Create(xml, null));
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
        var listing = Listing(JoinSecondChannelCannotReadPdf(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected,
            listing.RightsResolution.Disposition,
            "the premise: the dual-channel resolver records the rejected reading.");
        Assert.AreEqual(LuxembourgBodyCandidateDisposition.AcceptedCandidate, listing.Disposition,
            "the premise: the join withholds on licenceSCL only.");

        var body = LuxembourgGazetteBodyDisposition.Create(listing, Retention(listing, 'b'));

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.Admitted, body.Outcome);
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected,
            body.RightsResolution.Disposition,
            "recorded for the serving gate, unchanged.");
    }

    // ---- identity: what the publisher said; lineage: when it was asked ----

    /// <summary>
    /// S3-A04's first half, measured the way review measured its failure: two INDEPENDENT executions
    /// over one publisher fact - every run-minted reference differs, the act, listing, bytes,
    /// outcome, rights state and licence values do not - address one disposition one way. The
    /// lineage differs, by design.
    /// </summary>
    [TestMethod]
    public void TwoIndependentExecutionsOverOnePublisherFactShareOneIdentity()
    {
        var first = AdmittedFromRun('1');
        var second = AdmittedFromRun('2');

        Assert.AreNotEqual(first.RightsResolution.BoundRunIdentity, second.RightsResolution.BoundRunIdentity, "the premise: two runs.");
        Assert.AreNotEqual(first.SourceEvidenceRunIdentity, second.SourceEvidenceRunIdentity, "the premise: two fetches.");
        Assert.AreEqual(first.RightsResolution.Disposition, second.RightsResolution.Disposition);
        Assert.AreEqual(first.TransportByteSha256, second.TransportByteSha256);

        Assert.AreEqual(first.RightsClaimSha256, second.RightsClaimSha256, "what the publisher said is one fact.");
        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256, "one disposition, addressed one way.");
        Assert.AreNotEqual(first.RightsLineageSha256, second.RightsLineageSha256, "each time it was asked is another.");
        Assert.AreNotEqual(first.EpisodeSha256, second.EpisodeSha256);
    }

    /// <summary>
    /// The rights claim is material to identity: the same act, listing, bytes and admitted outcome
    /// with (a) another rights state, or (b) the same state on other licence values, is another
    /// disposition. The same state on other EVIDENCE is not: that is lineage.
    /// </summary>
    [TestMethod]
    public void ASemanticRightsChangeMovesTheIdentityAndAnEvidenceChangeDoesNot()
    {
        var pdf = Candidate(ActLoi1, "fr", "pdf");
        var agreed = Admit(JoinAgreedCcBy(ActLoi1, pdf));
        var quarantined = Admit(JoinSecondChannelCannotReadPdf(ActLoi1, pdf));
        var multipleA = Admit(JoinAgreedCcByWith(ActLoi1, SparqlEvidence, [Other, CcBy40], [Other, CcBy40], pdf));
        var multipleB = Admit(JoinAgreedCcByWith(ActLoi1, SparqlEvidence, [Another, CcBy40], [Another, CcBy40], pdf));
        var agreedOnOtherEvidence = Admit(JoinAgreedCcByWith(ActLoi1, OtherSparqlEvidence, [CcBy40], [CcBy40], pdf));

        foreach (var body in new[] { agreed, quarantined, multipleA, multipleB, agreedOnOtherEvidence })
        {
            Assert.AreEqual(LuxembourgGazetteBodyOutcome.Admitted, body.Outcome, "the premise: all admitted, same bytes.");
            Assert.AreEqual(agreed.TransportByteSha256, body.TransportByteSha256);
        }

        // (a) another rights state
        Assert.AreNotEqual(agreed.RightsResolution.Disposition, quarantined.RightsResolution.Disposition);
        Assert.AreNotEqual(agreed.IdentitySha256, quarantined.IdentitySha256);

        // (b) the same state - Multiple - on other licence values
        Assert.AreEqual(LuxembourgRightsChannelDisposition.Multiple, multipleA.RightsResolution.Disposition);
        Assert.AreEqual(multipleA.RightsResolution.Disposition, multipleB.RightsResolution.Disposition);
        Assert.AreNotEqual(multipleA.RightsClaimSha256, multipleB.RightsClaimSha256);
        Assert.AreNotEqual(multipleA.IdentitySha256, multipleB.IdentitySha256);

        // the same state on other evidence: lineage, not identity
        Assert.AreEqual(agreed.RightsResolution.Disposition, agreedOnOtherEvidence.RightsResolution.Disposition);
        Assert.AreEqual(agreed.RightsClaimSha256, agreedOnOtherEvidence.RightsClaimSha256);
        Assert.AreEqual(agreed.IdentitySha256, agreedOnOtherEvidence.IdentitySha256);
        Assert.AreNotEqual(agreed.RightsLineageSha256, agreedOnOtherEvidence.RightsLineageSha256);
    }

    /// <summary>
    /// Semantically identical rights observations, delivered in another order, share one identity.
    /// The order that can vary is the channels' observation collections; an observation's own
    /// licence list cannot, because its constructor requires it ordinal-sorted and unique - pinned
    /// here as the premise the claim digest relies on.
    /// </summary>
    [TestMethod]
    public void SemanticallyIdenticalRightsObservationsInAnotherOrderShareOneIdentity()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgRightsChannelObservation(ManifestationOf(ActLoi1, "fr", "pdf"), Run, SparqlEvidence, [CcBy40, Other]),
            "the premise: a licence list is canonical by construction (by-sa sorts before by), so it cannot be a source of order.");

        var pdf = Candidate(ActLoi1, "fr", "pdf");
        var pdfa = Candidate(ActLoi1, "fr", "pdfa");
        LuxembourgBodyJoinResolution Join(params LuxembourgWemiCandidate[] listedAs) =>
            LuxembourgBodyJoin.Resolve(
                ActLoi1,
                Run,
                Topology(pdf, pdfa),
                new LuxembourgSparqlRightsChannelObservations(
                    Run, SparqlEnumeration, listedAs.Select(c => Sparql(c.ManifestationIri, Other, CcBy40)).ToArray()),
                new LuxembourgInFileRightsChannelObservations(
                    Run, InFileEnumeration, listedAs.Select(c => InFileRead(c.ManifestationIri, Other, CcBy40)).ToArray()));

        var first = PdfBody(Join(pdf, pdfa));
        var second = PdfBody(Join(pdfa, pdf));

        Assert.AreEqual(LuxembourgRightsChannelDisposition.Multiple, first.RightsResolution.Disposition,
            "the premise: two licences on a channel is the Multiple state, recorded and not withholding.");
        Assert.AreEqual(first.RightsClaimSha256, second.RightsClaimSha256);
        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);

        static LuxembourgGazetteBodyDisposition PdfBody(LuxembourgBodyJoinResolution join)
        {
            var listing = LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single(static c => c.WemiCandidate.FormatIri == FormatPdf);
            return LuxembourgGazetteBodyDisposition.Create(listing, Retention(listing));
        }
    }

    [TestMethod]
    public void TheIdentityBindsTheBytesAndTheOutcome()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));

        var admitted = LuxembourgGazetteBodyDisposition.Create(listing, Retention(listing, 'a'));
        var again = LuxembourgGazetteBodyDisposition.Create(listing, Retention(listing, 'a'));
        var otherBytes = LuxembourgGazetteBodyDisposition.Create(listing, Retention(listing, 'c'));
        var notRetained = LuxembourgGazetteBodyDisposition.Create(listing, null);

        Assert.AreEqual(admitted.IdentitySha256, again.IdentitySha256, "same claim, same identity.");
        Assert.AreNotEqual(admitted.IdentitySha256, otherBytes.IdentitySha256, "other bytes, other identity.");
        Assert.AreNotEqual(admitted.IdentitySha256, notRetained.IdentitySha256, "other outcome, other identity.");
    }

    // ---- admitted is established, not asserted ----

    [TestMethod]
    public void ARetentionWhoseTerminalHopNamesAnotherReceiptIsRefused()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        var address = AddressOf(listing);
        var request = Request(address.FetchUri.AbsoluteUri);
        // The route names receipt 'a'; the caller hands over receipt 'b' for the same length.
        var route = Route(request, address.FetchUri.AbsoluteUri, Receipt('a'));

        var thrown = Assert.ThrowsExactly<ArgumentException>(() => LuxembourgGazetteBodyDisposition.Create(
            listing, new LuxembourgGazetteBodyRetention(address, request, request, route, Receipt('b'))));

        StringAssert.Contains(thrown.Message, "not the exact receipt bound to the terminal hop");
    }

    /// <summary>
    /// The premise the disposition relies on rather than re-checking: route evidence cannot even be
    /// built with a hop whose receipt names other bytes than the hop transferred. So binding the
    /// exact receipt to the terminal hop binds the bytes too.
    /// </summary>
    [TestMethod]
    public void AHopCannotNameAReceiptForOtherBytesThanItTransferred()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        var address = AddressOf(listing);
        var request = Request(address.FetchUri.AbsoluteUri);

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => Route(request, address.FetchUri.AbsoluteUri, Receipt('a'), transportedSha256: Digest('b')));

        StringAssert.Contains(thrown.Message, "names other bytes");
    }

    [TestMethod]
    public void AnUnrelatedRouteIsRefused()
    {
        var pdf = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        var other = Listing(JoinAgreedCcBy(ActRgd2, Candidate(ActRgd2, "fr", "pdf")));
        var address = AddressOf(pdf);
        var request = Request(address.FetchUri.AbsoluteUri);
        var receipt = Receipt('a');
        // A perfectly good route - for the other act's file.
        var otherAddress = AddressOf(other);
        var otherRequest = Request(otherAddress.FetchUri.AbsoluteUri);
        var unrelated = Route(otherRequest, otherAddress.FetchUri.AbsoluteUri, receipt);

        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgGazetteBodyDisposition.Create(
            pdf, new LuxembourgGazetteBodyRetention(address, request, request, unrelated, receipt)));
    }

    /// <summary>The address must be this listing's: its item, its format, its act - each broken alone refuses.</summary>
    [TestMethod]
    public void AnAddressForAnotherItemFormatOrActIsRefused()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdfa")));
        var wrong = new[]
        {
            (label: "item", address: AddressOf(listing, itemOverride: ItemOf(ActLoi1, "de", "pdfa")), expect: "not this listing's item"),
            (label: "format", address: AddressOf(listing, formatOverride: LuxembourgUserFormatToken.Pdf), expect: "not this listing's PdfA"),
            (label: "act", address: AddressOf(listing, actOverride: ActRgd2), expect: "not this act's"),
        };

        foreach (var (label, address, expect) in wrong)
        {
            // The route is built for the wrong address itself, so only the address-listing binding can catch it.
            var request = Request(address.FetchUri.AbsoluteUri);
            var receipt = Receipt('a');
            var route = Route(request, address.FetchUri.AbsoluteUri, receipt);

            var thrown = Assert.ThrowsExactly<ArgumentException>(() => LuxembourgGazetteBodyDisposition.Create(
                listing, new LuxembourgGazetteBodyRetention(address, request, request, route, receipt)), label);
            StringAssert.Contains(thrown.Message, expect, label);
        }
    }

    [TestMethod]
    public void ANegotiatingRequestIsNotTheAddressesFetch()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        var address = AddressOf(listing);
        var negotiating = Request(address.FetchUri.AbsoluteUri, [new HttpLogicalRequestHeader("accept", GazetteMediaType)]);
        var receipt = Receipt('a');
        var route = Route(negotiating, address.FetchUri.AbsoluteUri, receipt);

        var thrown = Assert.ThrowsExactly<ArgumentException>(() => LuxembourgGazetteBodyDisposition.Create(
            listing, new LuxembourgGazetteBodyRetention(address, negotiating, negotiating, route, receipt)));

        StringAssert.Contains(thrown.Message, "non-negotiating fetch");
    }

    [TestMethod]
    public void ASubstitutedOfficialRequestIsRefused()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        var address = AddressOf(listing);
        var official = Request(address.FetchUri.AbsoluteUri);
        var sentInstead = Request(
            address.FetchUri.AbsoluteUri,
            [new HttpLogicalRequestHeader("user-agent", "lex-v3-tests"), new HttpLogicalRequestHeader("x-extra", "not-the-official")]);
        var receipt = Receipt('a');
        // The hop's digest is of the request actually sent, not the one the caller presents as official.
        var route = Route(sentInstead, address.FetchUri.AbsoluteUri, receipt);

        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgGazetteBodyDisposition.Create(
            listing, new LuxembourgGazetteBodyRetention(address, official, sentInstead, route, receipt)));
    }

    [TestMethod]
    public void AResponseThatIsNotApplicationPdfIsRefused()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        var address = AddressOf(listing);
        var request = Request(address.FetchUri.AbsoluteUri);
        var receipt = Receipt('a');
        var route = Route(request, address.FetchUri.AbsoluteUri, receipt, mediaType: "text/html");

        var thrown = Assert.ThrowsExactly<ArgumentException>(() => LuxembourgGazetteBodyDisposition.Create(
            listing, new LuxembourgGazetteBodyRetention(address, request, request, route, receipt)));

        StringAssert.Contains(thrown.Message, "not application/pdf");
    }

    [TestMethod]
    public void ACompleteResponseWithNoEntityBytesIsNotADerivableBodyTransfer()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        var address = AddressOf(listing);
        var request = Request(address.FetchUri.AbsoluteUri);
        var empty = Receipt('0', 0);
        var route = Route(request, address.FetchUri.AbsoluteUri, empty, length: 0);

        var thrown = Assert.ThrowsExactly<ArgumentException>(() => LuxembourgGazetteBodyDisposition.Create(
            listing, new LuxembourgGazetteBodyRetention(address, request, request, route, empty)));

        StringAssert.Contains(thrown.Message, "not a complete derivable body transfer");
    }

    [TestMethod]
    public void ARedirectRetainsItsStartAndEffectiveAddresses()
    {
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        var address = AddressOf(listing);
        var official = Request(address.FetchUri.AbsoluteUri);
        var terminalUri = address.FetchUri.AbsoluteUri + "?download=1";
        var terminal = Request(terminalUri);
        var receipt = Receipt('a');
        var route = RedirectRoute(official, address.FetchUri.AbsoluteUri, terminal, terminalUri, receipt);

        var body = LuxembourgGazetteBodyDisposition.Create(
            listing, new LuxembourgGazetteBodyRetention(address, official, terminal, route, receipt));

        Assert.AreEqual(LuxembourgGazetteBodyOutcome.Admitted, body.Outcome);
        Assert.AreEqual(address.FetchUri.AbsoluteUri, body.SourceObservation!.RequestedUri);
        Assert.AreEqual(terminalUri, body.SourceObservation.EffectiveUri);
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
            l, l.WemiCandidate.FormatIri == FormatPdfA ? Retention(l) : null)).ToArray();

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
    public void ASetRefusesAMissingDispositionByName()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdfa"), Candidate(ActLoi1, "fr", "pdf"));
        var bodies = Bodies(join);

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(join, [bodies[0]]));

        StringAssert.Contains(thrown.Message, "missing");
        StringAssert.Contains(thrown.Message, bodies[1].ManifestationIri);
    }

    [TestMethod]
    public void ASetRefusesADuplicateDisposition()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdfa"), Candidate(ActLoi1, "fr", "pdf"));
        var bodies = Bodies(join);

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(join, [bodies[0], bodies[1], bodies[0]]));

        StringAssert.Contains(thrown.Message, "Two dispositions name one listing");
    }

    [TestMethod]
    public void ASetRefusesADispositionNamingAnotherAct()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var foreignJoin = JoinAgreedCcBy(ActRgd2, Candidate(ActRgd2, "fr", "pdf"));
        var foreign = Bodies(foreignJoin).Single();

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(join, [Bodies(join).Single(), foreign]));

        StringAssert.Contains(thrown.Message, ActRgd2);
        StringAssert.Contains(thrown.Message, "not this act");
    }

    /// <summary>
    /// The lens's finding: a key-only check let a foreign disposition stand in for a real one at
    /// the right count. A disposition must be minted from this join's own listing - the very object
    /// - so a listing this join does not hold, and a lookalike minted from an equal-but-separate
    /// join, are both refused even when the count is right.
    /// </summary>
    [TestMethod]
    public void ASetRefusesAListingThatIsNotThisJoinsOwnEvenAtTheRightCount()
    {
        var pdfa = Candidate(ActLoi1, "fr", "pdfa");
        var pdf = Candidate(ActLoi1, "fr", "pdf");
        var join = JoinAgreedCcBy(ActLoi1, pdfa, pdf);
        var bodies = Bodies(join);

        // A listing this join does not hold, REPLACING a real one: the count is right.
        var otherJoin = JoinAgreedCcBy(ActLoi1, pdfa, pdf, Candidate(ActLoi1, "de", "pdf"));
        var extra = Bodies(otherJoin).Single(static b => b.ManifestationIri.EndsWith("/de/pdf", StringComparison.Ordinal));
        var swapped = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(join, [bodies[0], extra]));
        StringAssert.Contains(swapped.Message, "not minted from one of this join's own listings");
        StringAssert.Contains(swapped.Message, extra.ManifestationIri);

        // A lookalike: the same act, the same listings, minted from a separate resolution of the
        // same inputs. Equal keys, another object carrying its own disposition and rights.
        var lookalike = Bodies(JoinAgreedCcBy(ActLoi1, pdfa, pdf));
        var separate = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(join, lookalike));
        StringAssert.Contains(separate.Message, "not minted from one of this join's own listings");

        // And the join's own, complete, is accepted.
        Assert.AreEqual(2, LuxembourgGazetteBodySet.Create(join, bodies).Bodies.Count);
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
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgGazetteBodySet.Create(
                join,
                [LuxembourgGazetteBodyDisposition.Create(
                    Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"))), null)]),
            "a disposition for a listing this join does not hold.");
        StringAssert.Contains(thrown.Message, "not minted from one of this join's own listings");
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
        var listing = Listing(JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf")));
        var retention = Retention(listing);
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgGazetteBodyDisposition.Create(null!, null));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgGazetteBodyDisposition.IsGazettePdf(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgGazetteBodyDisposition.RightsClaimSha256Of(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new LuxembourgGazetteBodyRetention(
            null!, retention.OfficialRequest, retention.TerminalRequest, retention.SourceEvidence, retention.RetainedTransportBytes));
        Assert.ThrowsExactly<ArgumentNullException>(() => new LuxembourgGazetteBodyRetention(
            retention.OfficialAddress, retention.OfficialRequest, retention.TerminalRequest, retention.SourceEvidence, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgGazetteBodySet.GazetteCandidatesOf(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgGazetteBodySet.Create(null!, []));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LuxembourgGazetteBodySet.Create(JoinAgreedCcBy(ActLoi1), null!));
    }

    // ---- helpers ----

    private static LuxembourgBodyCandidateResolution Listing(LuxembourgBodyJoinResolution join) =>
        LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single();

    private static LuxembourgGazetteBodyDisposition Admit(LuxembourgBodyJoinResolution join)
    {
        var listing = Listing(join);
        return LuxembourgGazetteBodyDisposition.Create(listing, Retention(listing));
    }

    /// <summary>One admitted pdf body of a1, observed and fetched by the execution <paramref name="seed"/> names.</summary>
    private static LuxembourgGazetteBodyDisposition AdmittedFromRun(char seed)
    {
        var listing = Listing(JoinAgreedCcByFromRun(ActLoi1, seed, ("fr", "pdf")));
        return LuxembourgGazetteBodyDisposition.Create(listing, Retention(listing, routeRun: ArtifactOf(seed, 'e')));
    }

    private static LuxembourgGazetteBodyDisposition[] Bodies(LuxembourgBodyJoinResolution join) =>
        LuxembourgGazetteBodySet.GazetteCandidatesOf(join)
            .Select(static l => LuxembourgGazetteBodyDisposition.Create(l, null))
            .ToArray();
}
