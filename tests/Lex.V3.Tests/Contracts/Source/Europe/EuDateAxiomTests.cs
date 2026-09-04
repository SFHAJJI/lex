using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E1 (ledger row <c>SRC-013</c>): a closed vocabulary and typed row shape for EU
/// <c>owl:Axiom</c>-qualified dates, with fixtures hand built directly from the GDPR and Directive
/// 95/46/EC shapes quoted in <c>review/23-research-temporal.md</c> section 3. Contract-only: no
/// live SPARQL call, no adapter.
/// </summary>
[TestClass]
public sealed class EuDateAxiomTests
{
    private const string N = "Lex.V3.Contracts.Source.Europe.";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";

    // --- Fixtures, quoted directly from review/23-research-temporal.md section 3 -------------

    /// <summary>
    /// "for GDPR, resource_legal_date_entry-into-force 2016-05-24 carries annotation:type_of_date
    /// {EV} ('Entry into force') and comment_on_date {DATPUB} +20 {V} {ART} 99".
    /// </summary>
    private static EuDateAxiomRow EntryIntoForce() => new(
        rawLexicalValue: "2016-05-24",
        rdfDatatypeUri: XsdDate,
        precision: EuDatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
        axiomReference: "axiom:32016r0679-entry-into-force-2016-05-24",
        rawQualifierCode: "EV",
        parsedAuthorityLabel: "Entry into force",
        publisherComment: "DATPUB +20 V ART 99");

    /// <summary>"2018-05-25 carries type_of_date {MA} ('Application')". No comment observed.</summary>
    private static EuDateAxiomRow Application() => new(
        rawLexicalValue: "2018-05-25",
        rdfDatatypeUri: XsdDate,
        precision: EuDatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
        axiomReference: "axiom:32016r0679-application-2018-05-25",
        rawQualifierCode: "MA",
        parsedAuthorityLabel: "Application",
        publisherComment: null);

    /// <summary>
    /// "resource_legal_date_deadline 2020-05-25 carries {AU+TARD} ('At the latest') {ART} 97".
    /// </summary>
    private static EuDateAxiomRow Deadline() => new(
        rawLexicalValue: "2020-05-25",
        rdfDatatypeUri: XsdDate,
        precision: EuDatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.DeadlinePredicateUri,
        axiomReference: "axiom:32016r0679-deadline-2020-05-25",
        rawQualifierCode: "AU+TARD",
        parsedAuthorityLabel: "At the latest",
        publisherComment: "ART 97");

    /// <summary>
    /// "resource_legal_date_end-of-validity ... 9999-12-31 sentinel when open". GDPR's own
    /// consolidation is still in force, so this is the open case, with no qualifier example
    /// evidenced anywhere in review/23.
    /// </summary>
    private static EuDateAxiomRow EndOfValidityOpen() => new(
        rawLexicalValue: "9999-12-31",
        rdfDatatypeUri: XsdDate,
        precision: EuDatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.EndOfValidityPredicateUri,
        axiomReference: "axiom:32016r0679-end-of-validity-open",
        rawQualifierCode: null,
        parsedAuthorityLabel: null,
        publisherComment: null);

    /// <summary>"2018-05-24 on Directive 95/46", the closed counterpart review/23 quotes for the same predicate.</summary>
    private static EuDateAxiomRow EndOfValidityClosed() => new(
        rawLexicalValue: "2018-05-24",
        rdfDatatypeUri: XsdDate,
        precision: EuDatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.EndOfValidityPredicateUri,
        axiomReference: "axiom:31995l0046-end-of-validity-2018-05-24",
        rawQualifierCode: null,
        parsedAuthorityLabel: null,
        publisherComment: null);

    // --- (a) qualifier-preserving typed rows ---------------------------------------------------

    [TestMethod]
    public void EachEvidencedGdprShapeRoundTripsEveryFieldIncludingSentinelState()
    {
        var ev = EntryIntoForce();
        Assert.AreEqual("2016-05-24", ev.RawLexicalValue);
        Assert.AreEqual(XsdDate, ev.RdfDatatypeUri);
        Assert.AreEqual(EuDatePrecision.YearMonthDay, ev.Precision);
        Assert.AreEqual(EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri, ev.SourcePredicateUri);
        Assert.AreEqual("axiom:32016r0679-entry-into-force-2016-05-24", ev.AxiomReference);
        Assert.AreEqual("EV", ev.RawQualifierCode);
        Assert.AreEqual("Entry into force", ev.ParsedAuthorityLabel);
        Assert.AreEqual("DATPUB +20 V ART 99", ev.PublisherComment);
        Assert.AreEqual(EuDateAxiomRole.EntryIntoForce, ev.Role);
        Assert.AreEqual(EuDateOpenSentinelState.Closed, ev.OpenSentinelState);

        var ma = Application();
        Assert.AreEqual("2018-05-25", ma.RawLexicalValue);
        Assert.AreEqual(EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri, ma.SourcePredicateUri);
        Assert.AreEqual("MA", ma.RawQualifierCode);
        Assert.AreEqual("Application", ma.ParsedAuthorityLabel);
        Assert.IsNull(ma.PublisherComment);
        Assert.AreEqual(EuDateAxiomRole.Application, ma.Role);
        Assert.AreEqual(EuDateOpenSentinelState.Closed, ma.OpenSentinelState);

        var deadline = Deadline();
        Assert.AreEqual("2020-05-25", deadline.RawLexicalValue);
        Assert.AreEqual(EuDateQualifierVocabulary.DeadlinePredicateUri, deadline.SourcePredicateUri);
        Assert.AreEqual("AU+TARD", deadline.RawQualifierCode);
        Assert.AreEqual("At the latest", deadline.ParsedAuthorityLabel);
        Assert.AreEqual("ART 97", deadline.PublisherComment);
        Assert.AreEqual(EuDateAxiomRole.Deadline, deadline.Role);
        Assert.AreEqual(EuDateOpenSentinelState.Closed, deadline.OpenSentinelState);

        var eovOpen = EndOfValidityOpen();
        Assert.AreEqual("9999-12-31", eovOpen.RawLexicalValue);
        Assert.AreEqual(EuDateQualifierVocabulary.EndOfValidityPredicateUri, eovOpen.SourcePredicateUri);
        Assert.IsNull(eovOpen.RawQualifierCode);
        Assert.IsNull(eovOpen.ParsedAuthorityLabel);
        Assert.AreEqual(EuDateAxiomRole.EndOfValidity, eovOpen.Role);
        Assert.AreEqual(EuDateOpenSentinelState.OpenSentinel, eovOpen.OpenSentinelState);

        var eovClosed = EndOfValidityClosed();
        Assert.AreEqual("2018-05-24", eovClosed.RawLexicalValue);
        Assert.AreEqual(EuDateAxiomRole.EndOfValidity, eovClosed.Role);
        Assert.AreEqual(EuDateOpenSentinelState.Closed, eovClosed.OpenSentinelState);
    }

    [TestMethod]
    public void TheOpenSentinelNeverCollapsesAMalformedNearSentinelIntoClosed()
    {
        // Looks like an attempt at the pinned literal but the trailing text is not a well formed
        // XSD timezone. Must not be silently read as an ordinary (Closed) date either.
        var malformed = new EuDateAxiomRow(
            "9999-12-31X", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EndOfValidityPredicateUri,
            "axiom:malformed-sentinel-suffix", null, null, null);
        Assert.AreEqual(EuDateOpenSentinelState.Unresolved, malformed.OpenSentinelState);

        // The permissive forms PublisherDate elsewhere in this codebase also admits: bare, "Z",
        // and a signed offset, all read as the open sentinel.
        var withZ = new EuDateAxiomRow(
            "9999-12-31Z", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EndOfValidityPredicateUri,
            "axiom:sentinel-with-z", null, null, null);
        Assert.AreEqual(EuDateOpenSentinelState.OpenSentinel, withZ.OpenSentinelState);

        var withOffset = new EuDateAxiomRow(
            "9999-12-31+01:00", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EndOfValidityPredicateUri,
            "axiom:sentinel-with-offset", null, null, null);
        Assert.AreEqual(EuDateOpenSentinelState.OpenSentinel, withOffset.OpenSentinelState);
    }

    // --- (b) role-collapse mutation --------------------------------------------------------------
    //
    // Manually verified red: EuDateQualifierVocabulary.PinnedQualifiers["MA"] was temporarily
    // edited to carry EuDateAxiomRole.EntryIntoForce (collapsing MA onto EV's slot) and this test
    // failed on the very first assertion below with actual=EntryIntoForce, expected!=actual. The
    // edit was then reverted and the suite re-run green before this file was committed.

    [TestMethod]
    public void EntryIntoForceAndApplicationNeverCollapseOntoTheSameRoleOrSlot()
    {
        var ev = EntryIntoForce();
        var ma = Application();

        Assert.AreNotEqual(ev.Role, ma.Role);
        Assert.AreEqual(EuDateAxiomRole.EntryIntoForce, ev.Role);
        Assert.AreEqual(EuDateAxiomRole.Application, ma.Role);

        // Distinct all the way down, not merely at the Role enum: a collapse further along the
        // pipeline than the enum member itself would also surface here.
        Assert.AreNotEqual(ev.RawQualifierCode, ma.RawQualifierCode);
        Assert.AreNotEqual(ev.ParsedAuthorityLabel, ma.ParsedAuthorityLabel);
    }

    // --- (c) date order never supplies a role ----------------------------------------------------

    [TestMethod]
    public void TwoUnqualifiedDatesOnTheSharedPredicateBothStayUnknownRegardlessOfConstructionOrder()
    {
        // Same predicate GDPR's real EV and MA dates share, but with no owl:Axiom qualifier at
        // all. Nothing in EuDateAxiomRow's constructor takes a date's order relative to any other
        // date as input, so there is no channel through which "earlier" or "later" could reach
        // Role even if some future edit tried to add one silently: this proves the current
        // behaviour, and the missing parameter is why a reviewer can trust it stays proven.
        var earlier = new EuDateAxiomRow(
            "2016-05-24", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
            "axiom:unqualified-2016-05-24-a", null, null, null);
        var later = new EuDateAxiomRow(
            "2018-05-25", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
            "axiom:unqualified-2018-05-25-a", null, null, null);

        Assert.AreEqual(EuDateAxiomRole.Unknown, earlier.Role);
        Assert.AreEqual(EuDateAxiomRole.Unknown, later.Role);

        // Constructed in the opposite order: same result, because order was never an input.
        var laterFirst = new EuDateAxiomRow(
            "2018-05-25", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
            "axiom:unqualified-2018-05-25-b", null, null, null);
        var earlierSecond = new EuDateAxiomRow(
            "2016-05-24", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
            "axiom:unqualified-2016-05-24-b", null, null, null);

        Assert.AreEqual(EuDateAxiomRole.Unknown, laterFirst.Role);
        Assert.AreEqual(EuDateAxiomRole.Unknown, earlierSecond.Role);
    }

    [TestMethod]
    public void AnUnrecognizedQualifierTokenStaysTypedUnknownRatherThanBeingGuessed()
    {
        var row = new EuDateAxiomRow(
            "2019-01-01", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.DeadlinePredicateUri,
            "axiom:unrecognized-qualifier", "SOME_FUTURE_TOKEN", "Some future label", null);

        Assert.AreEqual(EuDateAxiomRole.Unknown, row.Role);
        // Raw evidence is still preserved losslessly even though the role could not be resolved.
        Assert.AreEqual("SOME_FUTURE_TOKEN", row.RawQualifierCode);
        Assert.AreEqual("Some future label", row.ParsedAuthorityLabel);
    }

    // --- (d) transposition_deadline requires directive-specific evidence ------------------------

    [TestMethod]
    public void ABareDeadlineWithNoDirectiveLinkageIsInsufficientAndOneWithLinkageIsAccepted()
    {
        var deadline = Deadline();

        var bare = EuTranspositionDeadlineClassification.Classify(deadline, evidence: null);
        Assert.AreEqual(EuTranspositionDeadlineOutcome.TranspositionDeadlineEvidenceInsufficient, bare.Outcome);
        Assert.IsFalse(bare.IsAcceptedTranspositionDeadline);
        Assert.IsNull(bare.Evidence);
        // The underlying row is never silently promoted or altered by the failed attempt: it is
        // still exactly the plain Deadline role it always was.
        Assert.AreEqual(EuDateAxiomRole.Deadline, bare.DerivedFrom.Role);
        Assert.AreSame(deadline, bare.DerivedFrom);

        // A fixture directive identity for the mechanism only; review/23 does not claim GDPR's own
        // deadline transposes a Directive.
        var evidence = new EuDirectiveTranspositionEvidence("31995L0046");
        var linked = EuTranspositionDeadlineClassification.Classify(deadline, evidence);
        Assert.AreEqual(EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline, linked.Outcome);
        Assert.IsTrue(linked.IsAcceptedTranspositionDeadline);
        Assert.IsNotNull(linked.Evidence);
        Assert.AreEqual("31995L0046", linked.Evidence!.DirectiveIdentity);
        Assert.AreSame(deadline, linked.DerivedFrom);
    }

    [TestMethod]
    public void ClassifyingANonDeadlineRoleIsNotApplicableRatherThanInsufficientOrAccepted()
    {
        var ev = EntryIntoForce();
        var result = EuTranspositionDeadlineClassification.Classify(ev, evidence: null);

        Assert.AreEqual(EuTranspositionDeadlineOutcome.NotADeadline, result.Outcome);
        Assert.IsFalse(result.IsAcceptedTranspositionDeadline);
        Assert.IsNull(result.Evidence);
    }

    [TestMethod]
    public void DirectiveEvidenceCannotAccompanyANonDeadlineRow()
    {
        var ev = EntryIntoForce();
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuTranspositionDeadlineClassification.Classify(ev, new EuDirectiveTranspositionEvidence("31995L0046")));
    }

    // --- Constructor invariants beyond the four required scenarios --------------------------------

    [TestMethod]
    public void APinnedQualifierOnTheWrongPredicateIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuDateAxiomRow(
            "2016-05-24", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.DeadlinePredicateUri, // EV is only evidenced on the EV/MA predicate
            "axiom:ev-on-wrong-predicate", "EV", "Entry into force", null));
    }

    [TestMethod]
    public void APinnedQualifierWithAMismatchedLabelIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuDateAxiomRow(
            "2016-05-24", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
            "axiom:ev-with-wrong-label", "EV", "Not the pinned label", null));
    }

    [TestMethod]
    public void EndOfValidityCannotCarryAParsedAuthorityLabel()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuDateAxiomRow(
            "9999-12-31", XsdDate, EuDatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EndOfValidityPredicateUri,
            "axiom:eov-with-invented-label", null, "Invented label", null));
    }

    [TestMethod]
    public void DatatypeAndPrecisionMustAgree()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuDateAxiomRow(
            "2016-05-24", XsdDate, EuDatePrecision.Year, // xsd:date is day precision, not year
            EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
            "axiom:datatype-precision-mismatch", "EV", "Entry into force", null));
    }

    // --- Construction surface --------------------------------------------------------------------

    [TestMethod]
    public void TheRowHasExactlyOneConstructionPath()
    {
        // Two entries, not one: the static readonly PrecisionByDatatype table needs a type
        // initializer, exactly as EuWatermarkWitnessPlan's own static table does
        // (ThePlanHasExactlyOneConstructionPath above it in this codebase's sibling file). The
        // runtime calls .cctor() once, automatically, and it hands out nothing; the one real door
        // is the public instance constructor.
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private static " + N + "EuDateAxiomRow::.cctor() -> " + N + "EuDateAxiomRow",
                "constructor public instance " + N + "EuDateAxiomRow::.ctor(System.String, "
                + "System.String, " + N + "EuDatePrecision, System.String, System.String, "
                + "System.String, System.String, System.String) -> " + N + "EuDateAxiomRow",
            },
            ConstructionSurface.Of(typeof(EuDateAxiomRow)).ToArray());
    }

    [TestMethod]
    public void EveryOtherProducerOfARowInTheAssemblyIsExactlyTheClassificationsOwnHolder()
    {
        // Not empty: EuTranspositionDeadlineClassification.DerivedFrom holds a row (that is the
        // whole point of the derived_from pattern), so its backing field and its property getter
        // are real, expected doors onto an already-constructed row. Neither can mint a new one;
        // both only ever return a row someone else already built and handed to Classify.
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuTranspositionDeadlineClassification::"
                + "<DerivedFrom>k__BackingField -> " + N + "EuDateAxiomRow",
                "property public instance " + N + "EuTranspositionDeadlineClassification::"
                + "DerivedFrom() -> " + N + "EuDateAxiomRow",
            },
            ConstructionSurface.ProducersIn(typeof(EuDateAxiomRow).Assembly, typeof(EuDateAxiomRow), true).ToArray());
    }

    [TestMethod]
    public void TheClassificationHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuTranspositionDeadlineClassification::.ctor("
                + N + "EuTranspositionDeadlineOutcome, " + N + "EuDateAxiomRow, " + N
                + "EuDirectiveTranspositionEvidence) -> " + N + "EuTranspositionDeadlineClassification",
                "method public static " + N + "EuTranspositionDeadlineClassification::Classify(" + N
                + "EuDateAxiomRow, " + N + "EuDirectiveTranspositionEvidence) -> " + N
                + "EuTranspositionDeadlineClassification",
            },
            ConstructionSurface.Of(typeof(EuTranspositionDeadlineClassification)).ToArray());
    }

    [TestMethod]
    public void TheDirectiveEvidenceHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor public instance " + N + "EuDirectiveTranspositionEvidence::.ctor("
                + "System.String) -> " + N + "EuDirectiveTranspositionEvidence",
            },
            ConstructionSurface.Of(typeof(EuDirectiveTranspositionEvidence)).ToArray());
    }
}
