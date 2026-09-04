using System.Linq;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E1 (ledger row <c>SRC-013</c>): a rework building the EU <c>owl:Axiom</c>-qualified
/// date binding on the already-merged Facts date layer instead of a parallel vocabulary. See the
/// remarks on <see cref="EuDateAxiomBinding"/> for the two ruling events this rework answers.
/// Fixtures are hand built directly from the GDPR and Directive 95/46/EC shapes quoted in
/// <c>review/23-research-temporal.md</c> section 3, section 6 (regulation vs directive) and
/// section 8 (the fd_335 NAL). Every <c>owl:Axiom</c> identity string below
/// (<c>axiom:...</c>) and every <c>obs:...</c> observation id is invented for this fixture, not a
/// real Cellar axiom or custody coordinate; only the dates, predicates, qualifier tokens, labels
/// and CELEX numbers are the publisher's own, as quoted. Contract-only: no live SPARQL call, no
/// adapter.
/// </summary>
[TestClass]
public sealed class EuDateAxiomTests
{
    private const string N = "Lex.V3.Contracts.Source.Europe.";
    private const string Facts = "Lex.V3.Contracts.Facts.";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string XsdGYear = "http://www.w3.org/2001/XMLSchema#gYear";
    private const string XsdGYearMonth = "http://www.w3.org/2001/XMLSchema#gYearMonth";
    private const string Authority = "https://lex.internal.example/authority/eu-date-axiom-binding/v1";

    // --- Fixtures ---------------------------------------------------------------------------

    private static OfficialIdentitySet Gdpr() =>
        new(PublisherId.EuEurLex, new[] { new OfficialIdentifier(FactsIdentifierFamily.Celex, "32016R0679") });

    private static OfficialIdentitySet Directive9546() =>
        new(PublisherId.EuEurLex, new[] { new OfficialIdentifier(FactsIdentifierFamily.Celex, "31995L0046") });

    private static QualifiedAxiom Axiom(string remoteAxiomId, string? typeOfDateToken)
    {
        var qualifiers = typeOfDateToken is null
            ? Array.Empty<AxiomQualifier>()
            : new[]
            {
                new AxiomQualifier(
                    "http://publications.europa.eu/ontology/annotation#type_of_date", typeOfDateToken),
            };
        return new QualifiedAxiom(remoteAxiomId, qualifiers);
    }

    /// <summary>
    /// "for GDPR, resource_legal_date_entry-into-force 2016-05-24 carries annotation:type_of_date
    /// {EV} ('Entry into force') and comment_on_date {DATPUB} +20 {V} {ART} 99".
    /// </summary>
    private static EuDateAxiomBinding EntryIntoForce() => EuDateAxiomBinding.Create(
        work: Gdpr(),
        rawLexicalValue: "2016-05-24",
        datatypeUri: XsdDate,
        precision: DatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
        axiom: Axiom("axiom:32016r0679-entry-into-force-2016-05-24", "EV"),
        rawQualifierCode: "EV",
        qualifierLabel: "Entry into force",
        publisherComment: "DATPUB +20 V ART 99",
        parsedByAuthority: Authority,
        sourceObservationId: "obs:32016r0679-entry-into-force-2016-05-24");

    /// <summary>"2018-05-25 carries type_of_date {MA} ('Application')". No comment observed.</summary>
    private static EuDateAxiomBinding Application() => EuDateAxiomBinding.Create(
        work: Gdpr(),
        rawLexicalValue: "2018-05-25",
        datatypeUri: XsdDate,
        precision: DatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
        axiom: Axiom("axiom:32016r0679-application-2018-05-25", "MA"),
        rawQualifierCode: "MA",
        qualifierLabel: "Application",
        publisherComment: null,
        parsedByAuthority: Authority,
        sourceObservationId: "obs:32016r0679-application-2018-05-25");

    /// <summary>
    /// "resource_legal_date_deadline 2020-05-25 carries {AU+TARD} ('At the latest') {ART} 97" on
    /// GDPR, a Regulation. review/23 section 6: a regulation's deadline is never a transposition
    /// deadline, which is exactly the fixture this lane's promotion tests refuse.
    /// </summary>
    private static EuDateAxiomBinding GdprDeadline() => EuDateAxiomBinding.Create(
        work: Gdpr(),
        rawLexicalValue: "2020-05-25",
        datatypeUri: XsdDate,
        precision: DatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.DeadlinePredicateUri,
        axiom: Axiom("axiom:32016r0679-deadline-2020-05-25", "AU+TARD"),
        rawQualifierCode: "AU+TARD",
        qualifierLabel: "At the latest",
        publisherComment: "ART 97",
        parsedByAuthority: Authority,
        sourceObservationId: "obs:32016r0679-deadline-2020-05-25");

    /// <summary>
    /// A Deadline-role date belonging to Directive 95/46/EC itself, a Directive. review/23 does
    /// not quote a deadline date for this directive; the date, axiom and comment below are
    /// invented for the mechanism test only, built in the same AU+TARD shape review/23 quotes for
    /// GDPR's own deadline.
    /// </summary>
    private static EuDateAxiomBinding Directive9546Deadline() => EuDateAxiomBinding.Create(
        work: Directive9546(),
        rawLexicalValue: "1998-10-24",
        datatypeUri: XsdDate,
        precision: DatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.DeadlinePredicateUri,
        axiom: Axiom("axiom:31995l0046-deadline-1998-10-24", "AU+TARD"),
        rawQualifierCode: "AU+TARD",
        qualifierLabel: "At the latest",
        publisherComment: "ART 32",
        parsedByAuthority: Authority,
        sourceObservationId: "obs:31995l0046-deadline-1998-10-24");

    /// <summary>
    /// "resource_legal_date_end-of-validity ... 9999-12-31 sentinel when open". GDPR's own
    /// consolidation is still in force, so this is the open case, with no qualifier example
    /// evidenced anywhere in review/23.
    /// </summary>
    private static EuDateAxiomBinding EndOfValidityOpen() => EuDateAxiomBinding.Create(
        work: Gdpr(),
        rawLexicalValue: "9999-12-31",
        datatypeUri: XsdDate,
        precision: DatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.EndOfValidityPredicateUri,
        axiom: Axiom("axiom:32016r0679-end-of-validity-open", null),
        rawQualifierCode: null,
        qualifierLabel: null,
        publisherComment: null,
        parsedByAuthority: Authority,
        sourceObservationId: "obs:32016r0679-end-of-validity-open");

    /// <summary>"2018-05-24 on Directive 95/46", the closed counterpart review/23 quotes for the same predicate.</summary>
    private static EuDateAxiomBinding EndOfValidityClosed() => EuDateAxiomBinding.Create(
        work: Directive9546(),
        rawLexicalValue: "2018-05-24",
        datatypeUri: XsdDate,
        precision: DatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.EndOfValidityPredicateUri,
        axiom: Axiom("axiom:31995l0046-end-of-validity-2018-05-24", null),
        rawQualifierCode: null,
        qualifierLabel: null,
        publisherComment: null,
        parsedByAuthority: Authority,
        sourceObservationId: "obs:31995l0046-end-of-validity-2018-05-24");

    /// <summary>
    /// review/23 section 3's property list names <c>resource_legal_date_signature</c> as a bare
    /// CDM property with no owl:Axiom qualifier example, the same evidentiary basis as
    /// end-of-validity. The date value itself is invented for this fixture.
    /// </summary>
    private static EuDateAxiomBinding Signature() => EuDateAxiomBinding.Create(
        work: Gdpr(),
        rawLexicalValue: "2016-04-27",
        datatypeUri: XsdDate,
        precision: DatePrecision.YearMonthDay,
        sourcePredicateUri: EuDateQualifierVocabulary.SignatureDatePredicateUri,
        axiom: Axiom("axiom:32016r0679-signature-2016-04-27", null),
        rawQualifierCode: null,
        qualifierLabel: null,
        publisherComment: null,
        parsedByAuthority: Authority,
        sourceObservationId: "obs:32016r0679-signature-2016-04-27");

    // --- (a) qualifier-preserving typed bindings, all fields through the produced Fact --------

    [TestMethod]
    public void EachEvidencedGdprShapeRoundTripsEveryFieldOnTheProducedFact()
    {
        var ev = EntryIntoForce();
        Assert.AreEqual("2016-05-24", ev.Fact.Date.RawLexicalValue);
        Assert.AreEqual(XsdDate, ev.Fact.Date.DatatypeUri);
        Assert.AreEqual(DatePrecision.YearMonthDay, ev.Fact.Date.Precision);
        Assert.AreEqual(EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri, ev.Fact.SourcePredicateUri);
        Assert.AreEqual("axiom:32016r0679-entry-into-force-2016-05-24", ev.Fact.Axiom.RemoteAxiomId);
        Assert.AreSame(ev.Fact.Axiom, ev.Axiom);
        Assert.AreEqual("EV", ev.RawQualifierCode);
        Assert.AreEqual("EV", ev.Fact.RawQualifier);
        Assert.AreEqual("Entry into force", ev.QualifierLabel);
        Assert.AreEqual("DATPUB +20 V ART 99", ev.PublisherComment);
        Assert.AreEqual(DateSemanticRole.EntryIntoForce, ev.Fact.SemanticRole);
        Assert.AreEqual(TranspositionEvidence.None, ev.Fact.TranspositionEvidence);
        Assert.AreEqual(DateOpenSentinel.NotOpen, ev.Fact.Date.OpenSentinel);
        Assert.AreEqual(Authority, ev.ParsedByAuthority);
        Assert.IsTrue(ev.WorkIdentity.SameIdentity(Gdpr()));

        var ma = Application();
        Assert.AreEqual("2018-05-25", ma.Fact.Date.RawLexicalValue);
        Assert.AreEqual(EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri, ma.Fact.SourcePredicateUri);
        Assert.AreEqual("MA", ma.RawQualifierCode);
        Assert.AreEqual("Application", ma.QualifierLabel);
        Assert.IsNull(ma.PublisherComment);
        Assert.AreEqual(DateSemanticRole.ApplicationDate, ma.Fact.SemanticRole);

        var deadline = GdprDeadline();
        Assert.AreEqual("2020-05-25", deadline.Fact.Date.RawLexicalValue);
        Assert.AreEqual(EuDateQualifierVocabulary.DeadlinePredicateUri, deadline.Fact.SourcePredicateUri);
        Assert.AreEqual("AU+TARD", deadline.RawQualifierCode);
        Assert.AreEqual("At the latest", deadline.QualifierLabel);
        Assert.AreEqual("ART 97", deadline.PublisherComment);
        Assert.AreEqual(DateSemanticRole.PublisherDeadline, deadline.Fact.SemanticRole);
        Assert.AreEqual(TranspositionEvidence.None, deadline.Fact.TranspositionEvidence);

        var eovOpen = EndOfValidityOpen();
        Assert.AreEqual("9999-12-31", eovOpen.Fact.Date.RawLexicalValue);
        Assert.AreEqual(EuDateQualifierVocabulary.EndOfValidityPredicateUri, eovOpen.Fact.SourcePredicateUri);
        Assert.IsNull(eovOpen.RawQualifierCode);
        Assert.IsNull(eovOpen.QualifierLabel);
        Assert.AreEqual(DateSemanticRole.EndOfValidity, eovOpen.Fact.SemanticRole);
        Assert.AreEqual(DateOpenSentinel.OpenEnded, eovOpen.Fact.Date.OpenSentinel);

        var eovClosed = EndOfValidityClosed();
        Assert.AreEqual("2018-05-24", eovClosed.Fact.Date.RawLexicalValue);
        Assert.AreEqual(DateSemanticRole.EndOfValidity, eovClosed.Fact.SemanticRole);
        Assert.AreEqual(DateOpenSentinel.NotOpen, eovClosed.Fact.Date.OpenSentinel);

        var signature = Signature();
        Assert.AreEqual("2016-04-27", signature.Fact.Date.RawLexicalValue);
        Assert.AreEqual(EuDateQualifierVocabulary.SignatureDatePredicateUri, signature.Fact.SourcePredicateUri);
        Assert.IsNull(signature.RawQualifierCode);
        Assert.IsNull(signature.QualifierLabel);
        Assert.AreEqual(DateSemanticRole.SignatureDate, signature.Fact.SemanticRole);
    }

    [TestMethod]
    public void ASchemeIdentityCarriesTheNalResourceProvenanceNotJustTheBareName()
    {
        var ev = EntryIntoForce();
        Assert.AreEqual(EuNalSchemeIdentity.Fd335Name, ev.SchemeIdentity.Name);
        Assert.AreEqual("fd_335", ev.SchemeIdentity.Name);
        Assert.AreEqual(
            "http://publications.europa.eu/resource/authority/fd_335", ev.SchemeIdentity.AuthorityResourceBaseUri);
        Assert.AreSame(EuNalSchemeIdentity.Fd335, ev.SchemeIdentity);
    }

    // --- (b) gYear and gYearMonth precision, success path (fold-in) ---------------------------

    [TestMethod]
    public void AGYearPrecisionDateBuildsThroughPublisherDateUnchanged()
    {
        // A synthetic precision fixture, not an observed CDM value: review/23 quotes only
        // day-precision fd_335 dates. Deliberately unqualified so the role stays
        // RoleNotStatedByPublisher and no invented qualifier example is implied.
        var binding = EuDateAxiomBinding.Create(
            work: Gdpr(),
            rawLexicalValue: "2016",
            datatypeUri: XsdGYear,
            precision: DatePrecision.Year,
            sourcePredicateUri: EuDateQualifierVocabulary.DeadlinePredicateUri,
            axiom: Axiom("axiom:32016r0679-year-precision-fixture", null),
            rawQualifierCode: null,
            qualifierLabel: null,
            publisherComment: null,
            parsedByAuthority: Authority,
            sourceObservationId: "obs:32016r0679-year-precision-fixture");

        Assert.AreEqual("2016", binding.Fact.Date.RawLexicalValue);
        Assert.AreEqual(XsdGYear, binding.Fact.Date.DatatypeUri);
        Assert.AreEqual(DatePrecision.Year, binding.Fact.Date.Precision);
        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, binding.Fact.SemanticRole);
    }

    [TestMethod]
    public void AGYearMonthPrecisionDateBuildsThroughPublisherDateUnchanged()
    {
        var binding = EuDateAxiomBinding.Create(
            work: Gdpr(),
            rawLexicalValue: "2016-05",
            datatypeUri: XsdGYearMonth,
            precision: DatePrecision.YearMonth,
            sourcePredicateUri: EuDateQualifierVocabulary.DeadlinePredicateUri,
            axiom: Axiom("axiom:32016r0679-year-month-precision-fixture", null),
            rawQualifierCode: null,
            qualifierLabel: null,
            publisherComment: null,
            parsedByAuthority: Authority,
            sourceObservationId: "obs:32016r0679-year-month-precision-fixture");

        Assert.AreEqual("2016-05", binding.Fact.Date.RawLexicalValue);
        Assert.AreEqual(XsdGYearMonth, binding.Fact.Date.DatatypeUri);
        Assert.AreEqual(DatePrecision.YearMonth, binding.Fact.Date.Precision);
        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, binding.Fact.SemanticRole);
    }

    // --- (c) role-collapse mutation -------------------------------------------------------------
    //
    // Manually verified red: EuDateQualifierVocabulary.PinnedQualifiers["MA"] was temporarily
    // edited to carry DateSemanticRole.EntryIntoForce (collapsing MA onto EV's slot) and this test
    // failed on the very first assertion below with actual=EntryIntoForce, expected!=actual. The
    // edit was then reverted and the suite re-run green before this file was committed.

    [TestMethod]
    public void EntryIntoForceAndApplicationNeverCollapseOntoTheSameRoleOrSlot()
    {
        var ev = EntryIntoForce();
        var ma = Application();

        Assert.AreNotEqual(ev.Fact.SemanticRole, ma.Fact.SemanticRole);
        Assert.AreEqual(DateSemanticRole.EntryIntoForce, ev.Fact.SemanticRole);
        Assert.AreEqual(DateSemanticRole.ApplicationDate, ma.Fact.SemanticRole);

        // Distinct all the way down, not merely at the enum member.
        Assert.AreNotEqual(ev.RawQualifierCode, ma.RawQualifierCode);
        Assert.AreNotEqual(ev.QualifierLabel, ma.QualifierLabel);
    }

    // --- (d) date order never supplies a role ----------------------------------------------------

    [TestMethod]
    public void TwoUnqualifiedDatesOnTheSharedPredicateBothStayRoleNotStatedRegardlessOfConstructionOrder()
    {
        // Same predicate GDPR's real EV and MA dates share, but with no owl:Axiom qualifier at
        // all. Create takes no parameter carrying a date's position relative to any other date, so
        // there is no channel through which "earlier" or "later" could reach the role.
        EuDateAxiomBinding Unqualified(string rawLexicalValue, string observationSuffix) =>
            EuDateAxiomBinding.Create(
                Gdpr(), rawLexicalValue, XsdDate, DatePrecision.YearMonthDay,
                EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
                Axiom("axiom:unqualified-" + observationSuffix, null),
                null, null, null, Authority, "obs:unqualified-" + observationSuffix);

        var earlier = Unqualified("2016-05-24", "2016-05-24-a");
        var later = Unqualified("2018-05-25", "2018-05-25-a");
        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, earlier.Fact.SemanticRole);
        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, later.Fact.SemanticRole);

        // Constructed in the opposite order: same result, because order was never an input.
        var laterFirst = Unqualified("2018-05-25", "2018-05-25-b");
        var earlierSecond = Unqualified("2016-05-24", "2016-05-24-b");
        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, laterFirst.Fact.SemanticRole);
        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, earlierSecond.Fact.SemanticRole);
    }

    [TestMethod]
    public void AnUnrecognizedQualifierTokenStaysRoleNotStatedRatherThanBeingGuessed()
    {
        var binding = EuDateAxiomBinding.Create(
            Gdpr(), "2019-01-01", XsdDate, DatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.DeadlinePredicateUri,
            Axiom("axiom:unrecognized-qualifier", "SOME_FUTURE_TOKEN"),
            "SOME_FUTURE_TOKEN", "Some future label", null, Authority, "obs:unrecognized-qualifier");

        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, binding.Fact.SemanticRole);
        // Raw evidence is still preserved losslessly even though the role could not be resolved.
        Assert.AreEqual("SOME_FUTURE_TOKEN", binding.RawQualifierCode);
        Assert.AreEqual("Some future label", binding.QualifierLabel);
    }

    // --- Constructor invariants, reused from Facts or newly EU-specific ------------------------

    [TestMethod]
    public void APinnedQualifierOnTheWrongPredicateIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuDateAxiomBinding.Create(
            Gdpr(), "2016-05-24", XsdDate, DatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.DeadlinePredicateUri, // EV is only evidenced on the EV/MA predicate
            Axiom("axiom:ev-on-wrong-predicate", "EV"),
            "EV", "Entry into force", null, Authority, "obs:ev-on-wrong-predicate"));
    }

    [TestMethod]
    public void APinnedQualifierWithAMismatchedLabelIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuDateAxiomBinding.Create(
            Gdpr(), "2016-05-24", XsdDate, DatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
            Axiom("axiom:ev-with-wrong-label", "EV"),
            "EV", "Not the pinned label", null, Authority, "obs:ev-with-wrong-label"));
    }

    [TestMethod]
    public void EndOfValidityCannotCarryAQualifierLabel()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuDateAxiomBinding.Create(
            Gdpr(), "9999-12-31", XsdDate, DatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EndOfValidityPredicateUri,
            Axiom("axiom:eov-with-invented-label", null),
            null, "Invented label", null, Authority, "obs:eov-with-invented-label"));
    }

    [TestMethod]
    public void SignatureCannotCarryAQualifierLabel()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuDateAxiomBinding.Create(
            Gdpr(), "2016-04-27", XsdDate, DatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.SignatureDatePredicateUri,
            Axiom("axiom:signature-with-invented-label", null),
            null, "Invented label", null, Authority, "obs:signature-with-invented-label"));
    }

    [TestMethod]
    public void DatatypeAndPrecisionMustAgree_ReusingPublisherDatesOwnCheck()
    {
        // The message and the check both come from PublisherDate's own constructor now: this
        // lane no longer carries a second copy of the datatype/precision table.
        Assert.ThrowsExactly<ArgumentException>(() => EuDateAxiomBinding.Create(
            Gdpr(), "2016-05-24", XsdDate, DatePrecision.Year, // xsd:date is day precision, not year
            EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
            Axiom("axiom:datatype-precision-mismatch", "EV"),
            "EV", "Entry into force", null, Authority, "obs:datatype-precision-mismatch"));
    }

    [TestMethod]
    public void AMalformedNearSentinelValueIsRefusedAtConstructionRatherThanGivenAThirdState()
    {
        // PublisherDate's own IsValidLexicalValue refuses this outright: there is no separate
        // "malformed but attempted" sentinel state anywhere in this design.
        Assert.ThrowsExactly<ArgumentException>(() => EuDateAxiomBinding.Create(
            Gdpr(), "9999-12-31X", XsdDate, DatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EndOfValidityPredicateUri,
            Axiom("axiom:malformed-sentinel-suffix", null),
            null, null, null, Authority, "obs:malformed-sentinel-suffix"));
    }

    [TestMethod]
    public void ThePermissiveSentinelFormsPublisherDateAdmitsAllReadAsOpenEnded()
    {
        EuDateAxiomBinding WithSuffix(string suffix, string observationSuffix) => EuDateAxiomBinding.Create(
            Gdpr(), "9999-12-31" + suffix, XsdDate, DatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EndOfValidityPredicateUri,
            Axiom("axiom:sentinel-" + observationSuffix, null),
            null, null, null, Authority, "obs:sentinel-" + observationSuffix);

        Assert.AreEqual(DateOpenSentinel.OpenEnded, WithSuffix("", "bare").Fact.Date.OpenSentinel);
        Assert.AreEqual(DateOpenSentinel.OpenEnded, WithSuffix("Z", "z").Fact.Date.OpenSentinel);
        Assert.AreEqual(DateOpenSentinel.OpenEnded, WithSuffix("+01:00", "offset").Fact.Date.OpenSentinel);
    }

    [TestMethod]
    public void AnOpenSentinelValueCannotCarryAPinnedRole_ReusingTheFactsInvariant()
    {
        // Facts.PublisherDateFact's own constructor invariant, exercised here rather than
        // reimplemented: the open sentinel can only carry EndOfValidity or RoleNotStatedByPublisher.
        Assert.ThrowsExactly<ArgumentException>(() => EuDateAxiomBinding.Create(
            Gdpr(), "9999-12-31", XsdDate, DatePrecision.YearMonthDay,
            EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
            Axiom("axiom:sentinel-with-ev", "EV"),
            "EV", "Entry into force", null, Authority, "obs:sentinel-with-ev"));
    }

    // --- Transposition: single role home, work-bound directive evidence, asserted work kind -----

    [TestMethod]
    public void ClassifyingANonDeadlineRoleIsNotApplicableRatherThanInsufficientOrAccepted()
    {
        var ev = EntryIntoForce();
        var result = EuTranspositionDeadlineClassification.Classify(ev, null, Array.Empty<EuWorkKindAssertion>());

        Assert.AreEqual(EuTranspositionDeadlineOutcome.NotADeadline, result.Outcome);
        Assert.IsFalse(result.IsAcceptedTranspositionDeadline);
        Assert.IsNull(result.Evidence);
        Assert.IsNull(result.PromotedFact);
    }

    [TestMethod]
    public void DirectiveEvidenceCannotAccompanyANonDeadlineRow()
    {
        var ev = EntryIntoForce();
        var evidence = new EuDirectiveTranspositionEvidence(
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "31995L0046"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuTranspositionDeadlineClassification.Classify(ev, evidence, Array.Empty<EuWorkKindAssertion>()));
    }

    [TestMethod]
    public void ABareDeadlineWithNoEvidenceAtAllIsInsufficient()
    {
        var deadline = Directive9546Deadline();
        var result = EuTranspositionDeadlineClassification.Classify(
            deadline, evidence: null, Array.Empty<EuWorkKindAssertion>());

        Assert.AreEqual(EuTranspositionDeadlineOutcome.TranspositionDeadlineEvidenceInsufficient, result.Outcome);
        Assert.IsFalse(result.IsAcceptedTranspositionDeadline);
        Assert.IsNull(result.Evidence);
        Assert.IsNull(result.PromotedFact);
        // The underlying binding is never silently promoted or altered by the failed attempt.
        Assert.AreEqual(DateSemanticRole.PublisherDeadline, result.DerivedFrom.Fact.SemanticRole);
        Assert.AreSame(deadline, result.DerivedFrom);
    }

    [TestMethod]
    public void EvidenceNamingADifferentWorkThanTheBindingsOwnWorkIsInsufficient()
    {
        // GDPR's own deadline, but the evidence names Directive 95/46/EC -- a different work.
        // The old E1 head accepted exactly this shape ("a fixture directive identity for the
        // mechanism only; review/23 does not claim GDPR's own deadline transposes a Directive"),
        // which the ruling's second precision specifically closes.
        var gdprDeadline = GdprDeadline();
        var evidence = new EuDirectiveTranspositionEvidence(
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "31995L0046"));
        var assertions = new[] { new EuWorkKindAssertion(Directive9546(), EuWorkKind.Directive) };

        var result = EuTranspositionDeadlineClassification.Classify(gdprDeadline, evidence, assertions);

        Assert.AreEqual(EuTranspositionDeadlineOutcome.TranspositionDeadlineEvidenceInsufficient, result.Outcome);
        Assert.IsNotNull(result.Evidence);
        Assert.IsNull(result.PromotedFact);
    }

    [TestMethod]
    public void EvidenceNamingTheOwnWorkWithNoDirectiveAssertionAtAllIsInsufficient()
    {
        var deadline = Directive9546Deadline();
        var evidence = new EuDirectiveTranspositionEvidence(
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "31995L0046"));

        var result = EuTranspositionDeadlineClassification.Classify(
            deadline, evidence, Array.Empty<EuWorkKindAssertion>());

        Assert.AreEqual(EuTranspositionDeadlineOutcome.TranspositionDeadlineEvidenceInsufficient, result.Outcome);
        Assert.IsNull(result.PromotedFact);
    }

    [TestMethod]
    public void EvidenceNamingTheOwnWorkAssertedAsARegulationRatherThanADirectiveIsInsufficient()
    {
        // review/23 section 6: "GDPR, being a regulation, has no NIM links ... transposition
        // questions only make sense for directives." A Regulation assertion must not satisfy the
        // directive check even when every other condition lines up.
        var deadline = Directive9546Deadline();
        var evidence = new EuDirectiveTranspositionEvidence(
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "31995L0046"));
        var assertions = new[] { new EuWorkKindAssertion(Directive9546(), EuWorkKind.Regulation) };

        var result = EuTranspositionDeadlineClassification.Classify(deadline, evidence, assertions);

        Assert.AreEqual(EuTranspositionDeadlineOutcome.TranspositionDeadlineEvidenceInsufficient, result.Outcome);
        Assert.IsNull(result.PromotedFact);
    }

    [TestMethod]
    public void EvidenceNamingTheOwnWorkAssertedAsADirectiveIsAcceptedAndPromotesThroughDirectiveQualifier()
    {
        var deadline = Directive9546Deadline();
        var evidence = new EuDirectiveTranspositionEvidence(
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "31995L0046"));
        var assertions = new[] { new EuWorkKindAssertion(Directive9546(), EuWorkKind.Directive) };

        var result = EuTranspositionDeadlineClassification.Classify(deadline, evidence, assertions);

        Assert.AreEqual(EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline, result.Outcome);
        Assert.IsTrue(result.IsAcceptedTranspositionDeadline);
        Assert.AreSame(evidence, result.Evidence);
        Assert.AreSame(deadline, result.DerivedFrom);

        Assert.IsNotNull(result.PromotedFact);
        Assert.AreEqual(DateSemanticRole.TranspositionDeadline, result.PromotedFact!.SemanticRole);
        Assert.AreEqual(TranspositionEvidence.DirectiveQualifier, result.PromotedFact.TranspositionEvidence);
        Assert.AreSame(deadline.Fact.Date, result.PromotedFact.Date);
        Assert.AreSame(deadline.Fact.Axiom, result.PromotedFact.Axiom);
        Assert.AreEqual(deadline.Fact.SourceObservationId, result.PromotedFact.SourceObservationId);

        // Single role home: the promotion never mirrors back onto the binding's own fact.
        Assert.AreEqual(DateSemanticRole.PublisherDeadline, deadline.Fact.SemanticRole);
        Assert.AreEqual(TranspositionEvidence.None, deadline.Fact.TranspositionEvidence);
        Assert.AreNotSame(deadline.Fact, result.PromotedFact);
    }

    [TestMethod]
    public void ADirectiveIdentityThatIsNeitherCelexNorEliShapedIsRefusedAtEvidenceConstruction()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuDirectiveTranspositionEvidence(
            new OfficialIdentifier(FactsIdentifierFamily.Ecli, "ECLI:EU:C:2020:559")));
    }

    [TestMethod]
    public void ANullWorkKindAssertionInTheListIsRefused()
    {
        var deadline = Directive9546Deadline();
        var evidence = new EuDirectiveTranspositionEvidence(
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "31995L0046"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuTranspositionDeadlineClassification.Classify(
                deadline, evidence, new EuWorkKindAssertion?[] { null }!));
    }

    // --- Vocabulary pins ------------------------------------------------------------------------

    [TestMethod]
    public void ThePinnedQualifierTableIsExactlyTheThreeReviewEvidencedTokens()
    {
        CollectionAssert.AreEqual(
            new[] { "AU+TARD", "EV", "MA" },
            EuDateQualifierVocabulary.PinnedQualifiers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        Assert.AreEqual(DateSemanticRole.EntryIntoForce, EntryIntoForce().Fact.SemanticRole);
        Assert.AreEqual(DateSemanticRole.ApplicationDate, Application().Fact.SemanticRole);
        Assert.AreEqual(DateSemanticRole.PublisherDeadline, GdprDeadline().Fact.SemanticRole);
    }

    [TestMethod]
    public void EuWorkKindIsExactlyDirectiveAndRegulation()
    {
        CollectionAssert.AreEqual(
            new[] { "Directive", "Regulation" },
            Enum.GetNames<EuWorkKind>().OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public void DependencyGuard_DateSemanticRoleIsExactlyTheReviewedTenMembers()
    {
        // Not this lane's vocabulary to own, but this lane's ComputeRole fallback (predicate
        // derivation, then RoleNotStatedByPublisher) is a closed-world assumption over this exact
        // member set. A member added upstream should prompt someone to revisit that fallback
        // rather than silently landing on RoleNotStatedByPublisher without anyone noticing.
        CollectionAssert.AreEqual(
            new[]
            {
                "ApplicationDate", "DocumentDate", "EndOfValidity", "EntryIntoForce",
                "NotificationDate", "PublicationDate", "PublisherDeadline", "RoleNotStatedByPublisher",
                "SignatureDate", "TranspositionDeadline",
            },
            Enum.GetNames<DateSemanticRole>().OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public void DependencyGuard_TranspositionEvidenceIsExactlyTheReviewedThreeMembers()
    {
        CollectionAssert.AreEqual(
            new[] { "DirectiveQualifier", "NimRecord", "None" },
            Enum.GetNames<TranspositionEvidence>().OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public void TheBindingsPublicPropertySurfaceCarriesNoSecondRoleField()
    {
        // The direct enforcement of the ruling's single-role-home precision: a reflection pin over
        // every public property the binding declares. ConstructionSurface guards construction
        // paths for the guarded Facts/Europe types, but a computed `Role` or `SemanticRole`
        // property returning the DateSemanticRole enum would not "carry" any of those guarded
        // types and so would not appear in those pins at all. This list is the guard that would
        // actually catch it: any new public property fails this exact comparison.
        CollectionAssert.AreEqual(
            new[]
            {
                "Axiom", "Fact", "ParsedByAuthority", "PublisherComment", "QualifierLabel",
                "RawQualifierCode", "SchemeIdentity", "WorkIdentity",
            },
            typeof(EuDateAxiomBinding).GetProperties()
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray());
    }

    // --- Construction surface --------------------------------------------------------------------

    [TestMethod]
    public void TheBindingHasExactlyTwoConstructionPaths()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuDateAxiomBinding::.ctor("
                + Facts + "PublisherDateFact, System.String, " + N + "EuNalSchemeIdentity) -> "
                + N + "EuDateAxiomBinding",
                "method public static " + N + "EuDateAxiomBinding::Create(" + Facts + "OfficialIdentitySet, "
                + "System.String, System.String, " + Facts + "DatePrecision, System.String, "
                + Facts + "QualifiedAxiom, System.String, System.String, System.String, System.String, "
                + "System.String) -> " + N + "EuDateAxiomBinding",
            },
            ConstructionSurface.Of(typeof(EuDateAxiomBinding)).ToArray());
    }

    [TestMethod]
    public void EveryOtherProducerOfABindingInTheAssemblyIsExactlyTheClassificationsOwnHolder()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuTranspositionDeadlineClassification::"
                + "<DerivedFrom>k__BackingField -> " + N + "EuDateAxiomBinding",
                "property public instance " + N + "EuTranspositionDeadlineClassification::"
                + "DerivedFrom() -> " + N + "EuDateAxiomBinding",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuDateAxiomBinding).Assembly, typeof(EuDateAxiomBinding), true).ToArray());
    }

    [TestMethod]
    public void TheSchemeIdentityIsMintedOnlyAsARecordAndItsOnePinnedSingleton()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuNalSchemeIdentity::.ctor(" + N
                + "EuNalSchemeIdentity) -> " + N + "EuNalSchemeIdentity",
                "constructor private instance " + N + "EuNalSchemeIdentity::.ctor(System.String, "
                + "System.String) -> " + N + "EuNalSchemeIdentity",
                "constructor private static " + N + "EuNalSchemeIdentity::.cctor() -> " + N
                + "EuNalSchemeIdentity",
                "field private static " + N + "EuNalSchemeIdentity::<Fd335>k__BackingField -> " + N
                + "EuNalSchemeIdentity",
                "method public instance " + N + "EuNalSchemeIdentity::<Clone>$() -> " + N
                + "EuNalSchemeIdentity",
                "property public static " + N + "EuNalSchemeIdentity::Fd335() -> " + N
                + "EuNalSchemeIdentity",
            },
            ConstructionSurface.Of(typeof(EuNalSchemeIdentity)).ToArray());
    }

    [TestMethod]
    public void EveryOtherProducerOfASchemeIdentityInTheAssemblyIsExactlyTheBindingsOwnHolder()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuDateAxiomBinding::<SchemeIdentity>k__BackingField -> "
                + N + "EuNalSchemeIdentity",
                "property public instance " + N + "EuDateAxiomBinding::SchemeIdentity() -> " + N
                + "EuNalSchemeIdentity",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuNalSchemeIdentity).Assembly, typeof(EuNalSchemeIdentity), true).ToArray());
    }

    [TestMethod]
    public void TheWorkKindAssertionHasExactlyOneRealConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuWorkKindAssertion::.ctor(" + N
                + "EuWorkKindAssertion) -> " + N + "EuWorkKindAssertion",
                "constructor public instance " + N + "EuWorkKindAssertion::.ctor(" + Facts
                + "OfficialIdentitySet, " + N + "EuWorkKind) -> " + N + "EuWorkKindAssertion",
                "method public instance " + N + "EuWorkKindAssertion::<Clone>$() -> " + N
                + "EuWorkKindAssertion",
            },
            ConstructionSurface.Of(typeof(EuWorkKindAssertion)).ToArray());
    }

    [TestMethod]
    public void NoOtherTypeInTheAssemblyHoldsOrProducesAWorkKindAssertion()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuWorkKindAssertion).Assembly, typeof(EuWorkKindAssertion), true).ToArray());
    }

    [TestMethod]
    public void TheDirectiveEvidenceHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor public instance " + N + "EuDirectiveTranspositionEvidence::.ctor(" + Facts
                + "OfficialIdentifier) -> " + N + "EuDirectiveTranspositionEvidence",
            },
            ConstructionSurface.Of(typeof(EuDirectiveTranspositionEvidence)).ToArray());
    }

    [TestMethod]
    public void EveryOtherProducerOfDirectiveEvidenceInTheAssemblyIsExactlyTheClassificationsOwnHolder()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuTranspositionDeadlineClassification::"
                + "<Evidence>k__BackingField -> " + N + "EuDirectiveTranspositionEvidence",
                "property public instance " + N + "EuTranspositionDeadlineClassification::"
                + "Evidence() -> " + N + "EuDirectiveTranspositionEvidence",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuDirectiveTranspositionEvidence).Assembly,
                typeof(EuDirectiveTranspositionEvidence), true).ToArray());
    }

    [TestMethod]
    public void TheClassificationHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuTranspositionDeadlineClassification::.ctor("
                + N + "EuTranspositionDeadlineOutcome, " + N + "EuDateAxiomBinding, " + N
                + "EuDirectiveTranspositionEvidence, " + Facts + "PublisherDateFact) -> " + N
                + "EuTranspositionDeadlineClassification",
                "method public static " + N + "EuTranspositionDeadlineClassification::Classify(" + N
                + "EuDateAxiomBinding, " + N + "EuDirectiveTranspositionEvidence, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuWorkKindAssertion>) -> " + N
                + "EuTranspositionDeadlineClassification",
            },
            ConstructionSurface.Of(typeof(EuTranspositionDeadlineClassification)).ToArray());
    }

    [TestMethod]
    public void NoOtherTypeInTheAssemblyHoldsOrProducesTheClassification()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuTranspositionDeadlineClassification).Assembly,
                typeof(EuTranspositionDeadlineClassification), true).ToArray());
    }
}
