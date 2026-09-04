using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E4 (ledger row <c>REL-002</c>): the asserted relation edge binding, the located
/// amendment axiom, the repeal edge and the derived amendment inverse.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provenance of every literal below.</b> The five located amendment rows are transcribed from
/// the retained fixture <c>amends-located-axioms.json</c> (2968 bytes, sha256
/// <c>d3353e41e9091b202970dae3ef5ec7be063a5b6dc5afcf30c12ada2b4fe01ffd</c>), a real SPARQL SELECT
/// result from the EU endpoint. The cellar URIs, the location values, the role values, the two
/// date spellings, the one absent <c>start</c> and the <c>MS</c> link type are the publisher's own
/// bytes. The repeals row is the GDPR repeals axiom from canary
/// <c>lex-event-20260904T174651520Z-392411cf4e9446e2aa76bd3be3cc2c8a</c> (digest 4701a3361ff09048).
/// The ontology URI and version are from the probe relayed with digest
/// <c>6c918b286291c621944ec20b409ac794b25128f53dd39529fc07c55174f4bba9</c>.
/// </para>
/// <para>
/// <b>What is invented, said plainly.</b> Every <c>axiom:...</c> identity and every <c>obs:...</c>
/// observation id here is made up for the fixture. The retained result is a SELECT and carries no
/// <c>owl:Axiom</c> identifier column and no custody coordinate, so there is nothing real to
/// transcribe for either. The CELEX numbers used to build the GDPR and 1995 directive identity
/// sets in the repeal test come from those acts, not from the retained repeals row. Contract only:
/// no live call anywhere in this file.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuAmendmentRelationTests
{
    private const string Cellar = "http://publications.europa.eu/resource/cellar/";
    private const string Fd370 = "http://publications.europa.eu/resource/authority/fd_370";
    private const string Fd375 = "http://publications.europa.eu/resource/authority/fd_375";

    // Pinned as literals rather than through the vocabulary's own constants: a fixture built from
    // the constant it is later checked against can only fail if production stops assigning that
    // constant, never if the constant's own value silently changes. The amended-by spelling is the
    // one a guard keys on, so this independent transcription is what keeps that guard honest.
    private const string AmendsPredicate =
        "http://publications.europa.eu/ontology/cdm#resource_legal_amends_resource_legal";
    private const string AmendedByPredicate =
        "http://publications.europa.eu/ontology/cdm#resource_legal_amended_by_resource_legal";
    private const string RepealsPredicate =
        "http://publications.europa.eu/ontology/cdm#resource_legal_repeals_resource_legal";
    private const string BasedOnPredicate =
        "http://publications.europa.eu/ontology/cdm#act_consolidated_based_on_resource_legal";
    private const string ConsolidatesPredicate =
        "http://publications.europa.eu/ontology/cdm#act_consolidated_consolidates_resource_legal";
    private const string OntologyUri = "http://publications.europa.eu/ontology/cdm";
    private const string OntologyVersion = "4.17.0";

    private const string LocationAnnotation =
        "http://publications.europa.eu/ontology/annotation#reference_to_modified_location";
    private const string StartAnnotation =
        "http://publications.europa.eu/ontology/annotation#start_of_validity";
    private const string LinkTargetAnnotation =
        "http://publications.europa.eu/ontology/annotation#type_of_link_target";
    private const string RoleAnnotation =
        "http://publications.europa.eu/ontology/annotation#role2";

    /// <summary>The five retained rows, in the order the endpoint returned them.</summary>
    private static (string Source, string Target, string Location, string? Start, string LinkType, string Role)[] RetainedRows() =>
    [
        ("00034b8a-6af2-4207-bc76-d24a10b5125c", "62212f0d-011f-471e-a033-bf56990d4329",
            "{AN|" + Fd370 + "/AN} 1", "2000-02-09", "MS", "{R|" + Fd375 + "/R}"),
        ("00034b8a-6af2-4207-bc76-d24a10b5125c", "62212f0d-011f-471e-a033-bf56990d4329",
            "{AR|" + Fd370 + "/AR} 2", "2000-02-09", "MS", "{R|" + Fd375 + "/R}"),
        // The third row binds no `start` at all. This is the row that proves start_of_validity is
        // optional, which the E4 scope ruling records only for end_of_validity.
        ("00062a99-53f6-4ece-92cc-7cec9efe86d1", "165085c2-125f-4cc7-8b96-cd0a12d4cd0d",
            "{AR|" + Fd370 + "/AR} 2", null, "MS", "{J|" + Fd375 + "/J}"),
        ("00064e3d-e914-42e1-a764-73be0b2ea7c5", "11021bcf-64f0-44fc-9a98-8364df9569a3",
            "{AR|" + Fd370 + "/AR} 1", "2010/02/01", "MS", "{J|" + Fd375 + "/J}"),
        // "IA" is the trailing value that makes an integer parse wrong on real data.
        ("00064e3d-e914-42e1-a764-73be0b2ea7c5", "5eaf68b3-afd7-47b4-b688-429f04995066",
            "{AN|" + Fd370 + "/AN} IA", "2010/01/01", "MS", "{M|" + Fd375 + "/M}"),
    ];

    private static OfficialIdentitySet Work(string uuid) =>
        new(PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, Cellar + uuid)]);

    private static OfficialIdentitySet Celex(string celex) =>
        new(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Celex, celex)]);

    private static EuLocatedAmendmentAxiom Axiom(
        (string Source, string Target, string Location, string? Start, string LinkType, string Role) row,
        string remoteAxiomId) =>
        EuLocatedAmendmentAxiom.Create(
            Work(row.Source),
            Work(row.Target),
            TargetBodyScope.BodyInScopeNotHeld,
            row.Location,
            row.Role,
            row.Start,
            rawEndOfValidity: null,
            row.LinkType,
            remoteAxiomId,
            "obs:invented-" + remoteAxiomId);

    // --- The five retained rows -----------------------------------------------------------------

    /// <summary>
    /// Every retained row round trips: its tokens keep both halves, its role resolves against the
    /// second authority list, and its date keeps the spelling the publisher used.
    /// </summary>
    [TestMethod]
    public void EveryRetainedRowKeepsItsTokensAuthoritiesRoleAndDateSpelling()
    {
        var rows = RetainedRows();
        Assert.HasCount(5, rows);

        var index = 0;
        foreach (var row in rows)
        {
            var axiom = Axiom(row, $"axiom:invented-{index}");

            Assert.HasCount(1, axiom.Location.Tokens);
            Assert.AreEqual(row.Location, axiom.Location.RawValue);

            var token = axiom.Location.Tokens[0];
            Assert.AreEqual(Fd370 + "/" + token.Code, token.AuthorityUri);
            Assert.IsTrue(token.Code is "AN" or "AR", token.Code);

            Assert.AreEqual(Fd375 + "/" + axiom.Role.Code, axiom.Role.AuthorityUri);
            Assert.IsTrue(axiom.Role.Code is "R" or "J" or "M", axiom.Role.Code);
            Assert.IsNull(axiom.Role.Value, "role2 carries a bare code with no trailing value.");

            Assert.AreEqual("MS", axiom.TypeOfLinkTarget);
            Assert.IsNull(axiom.EndOfValidity, "end_of_validity is absent on all five rows.");

            if (row.Start is null)
            {
                Assert.IsNull(axiom.StartOfValidity);
            }
            else
            {
                Assert.AreEqual(row.Start, axiom.StartOfValidity!.RawLexicalValue);
            }

            index++;
        }
    }

    /// <summary>
    /// The trailing value is carried as an opaque string. <c>IA</c> is a real observed value, so a
    /// contract that parsed this to an integer would throw on the fifth retained row.
    /// </summary>
    [TestMethod]
    public void TheTrailingLocationValueIsCarriedAsAStringIncludingTheRomanNumeralOne()
    {
        var values = RetainedRows()
            .Select((row, position) => Axiom(row, $"axiom:invented-{position}").Location.Tokens[0].Value)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "1", "2", "2", "1", "IA" }, values);
        Assert.IsFalse(
            int.TryParse(values[4], out _),
            "\"IA\" must not be parseable as an integer, which is why the value stays a string.");
    }

    /// <summary>
    /// One result set carries both date spellings, and the counts are stated exactly: two
    /// hyphenated, two slash separated, one absent.
    /// </summary>
    /// <remarks>
    /// The E4 re-filed scope check says "three rows give hyphens, two give slashes", which totals
    /// five and implies every row binds a start. The retained bytes say otherwise: two hyphenated,
    /// two slash, one with no <c>start</c> binding. This test transcribes the bytes.
    /// </remarks>
    [TestMethod]
    public void TheSameFiveRowsCarryTwoHyphenatedTwoSlashSeparatedAndOneAbsentStartDate()
    {
        var shapes = RetainedRows()
            .Select((row, position) => Axiom(row, $"axiom:invented-{position}").StartOfValidity?.ObservedShape)
            .ToArray();

        Assert.AreEqual(2, shapes.Count(shape => shape == EuValidityDateShape.HyphenatedIso8601));
        Assert.AreEqual(2, shapes.Count(shape => shape == EuValidityDateShape.SlashSeparated));
        Assert.AreEqual(1, shapes.Count(shape => shape is null));
    }

    /// <summary>
    /// A hyphenated date is a real Facts-layer <see cref="PublisherDate"/>; a slash one has no
    /// spelling in that layer and is honestly null rather than rewritten into one.
    /// </summary>
    [TestMethod]
    public void OnlyTheHyphenatedSpellingTypesThroughTheFactsDateLayerAndNeitherIsNormalised()
    {
        var hyphenated = EuValidityDate.Create("2000-02-09");
        Assert.AreEqual(EuValidityDateShape.HyphenatedIso8601, hyphenated.ObservedShape);
        Assert.IsNotNull(hyphenated.TypedDate);
        Assert.AreEqual("2000-02-09", hyphenated.TypedDate.RawLexicalValue);
        Assert.AreEqual(PublisherDate.Date, hyphenated.TypedDate.DatatypeUri);
        Assert.AreEqual(DatePrecision.YearMonthDay, hyphenated.TypedDate.Precision);
        Assert.AreEqual(DateOpenSentinel.NotOpen, hyphenated.TypedDate.OpenSentinel);

        var slash = EuValidityDate.Create("2010/02/01");
        Assert.AreEqual(EuValidityDateShape.SlashSeparated, slash.ObservedShape);
        Assert.IsNull(slash.TypedDate, "PublisherDate's lexical space has no slash spelling.");

        Assert.AreEqual("2010/02/01", slash.RawLexicalValue);
        Assert.IsFalse(
            PublisherDate.IsValidLexicalValue("2010/02/01", DatePrecision.YearMonthDay),
            "The Facts date layer itself refuses the slash spelling, which is why TypedDate is null.");
    }

    /// <summary>
    /// The hyphenated open end is carried as the Facts layer's own sentinel, not as a date in the
    /// year 9999.
    /// </summary>
    [TestMethod]
    public void TheHyphenatedOpenEndIsCarriedAsTheSentinel()
    {
        var openEnded = EuValidityDate.Create("9999-12-31");

        Assert.AreEqual(EuValidityDateShape.HyphenatedIso8601, openEnded.ObservedShape);
        Assert.IsNotNull(openEnded.TypedDate);
        Assert.AreEqual(DateOpenSentinel.OpenEnded, openEnded.TypedDate.OpenSentinel);
        Assert.AreEqual("9999-12-31", openEnded.RawLexicalValue);
    }

    /// <summary>
    /// The slash-spelled open end is refused by name rather than guessed at in either direction.
    /// </summary>
    /// <remarks>
    /// No slash-spelled sentinel has been observed. Reading it as an ordinary date silently turns
    /// "validity does not end" into "validity ended in the year 9999"; reading it as the sentinel
    /// invents a second sentinel spelling no observation supports. The refusal is the only honest
    /// third option, and this test is what drives it.
    /// </remarks>
    [TestMethod]
    public void TheSlashSpelledOpenEndIsRefusedByName()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => EuValidityDate.Create("9999/12/31"));

        StringAssert.Contains(error.Message, "9999/12/31");
        StringAssert.Contains(error.Message, "no slash-spelled sentinel has been observed");
    }

    /// <summary>The calendar rule is the Facts layer's own, on both spellings.</summary>
    [TestMethod]
    public void AnImpossibleCalendarDateIsRefusedInEitherSpelling()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuValidityDate.Create("2010-02-30"));
        Assert.ThrowsExactly<ArgumentException>(() => EuValidityDate.Create("2010/02/30"));

        // 2000 is a leap year and 1900 is not, which is the Facts layer's rule, not a new one.
        Assert.AreEqual("2000/02/29", EuValidityDate.Create("2000/02/29").RawLexicalValue);
        Assert.ThrowsExactly<ArgumentException>(() => EuValidityDate.Create("1900/02/29"));
    }

    /// <summary>A third date spelling is refused by name rather than normalised into one of the two.</summary>
    [TestMethod]
    public void AThirdDateSpellingIsRefusedByName()
    {
        foreach (var rejected in new[] { "09.02.2000", "2000-02-09T00:00:00", "20000209", "2000-2-9", "2000/02-09" })
        {
            var error = Assert.ThrowsExactly<ArgumentException>(() => EuValidityDate.Create(rejected));
            StringAssert.Contains(error.Message, rejected);
            StringAssert.Contains(error.Message, "two observed validity-date spellings");
        }
    }

    // --- The two authority lists ----------------------------------------------------------------

    /// <summary>
    /// A location token against the role list, or a role against the location list, is refused by
    /// name. Two lists are in play and neither substitutes for the other.
    /// </summary>
    [TestMethod]
    public void AnAuthorityFromTheWrongListIsRefusedByName()
    {
        var locationAgainstRoleList = Assert.ThrowsExactly<ArgumentException>(
            () => EuStructuralLocation.Parse("{AN|" + Fd375 + "/AN} 1", Fd370));
        StringAssert.Contains(locationAgainstRoleList.Message, Fd375 + "/AN");
        StringAssert.Contains(locationAgainstRoleList.Message, Fd370 + "/AN");

        var roleAgainstLocationList = Assert.ThrowsExactly<ArgumentException>(
            () => EuStructuralLocation.Parse("{R|" + Fd370 + "/R}", Fd375));
        StringAssert.Contains(roleAgainstLocationList.Message, Fd370 + "/R");
    }

    /// <summary>An authority list nobody pinned is refused by name, naming both sides.</summary>
    [TestMethod]
    public void AnUnknownAuthorityListIsRefusedByName()
    {
        const string Unknown = "http://publications.europa.eu/resource/authority/fd_999";
        var error = Assert.ThrowsExactly<ArgumentException>(
            () => EuStructuralLocation.Parse("{AN|" + Unknown + "/AN} 1", Fd370));

        StringAssert.Contains(error.Message, Unknown + "/AN");
        StringAssert.Contains(error.Message, "not a member of the pinned authority list");
    }

    /// <summary>
    /// The two halves of a token must agree: a code naming one member with an IRI naming another
    /// is refused rather than kept with one half silently winning.
    /// </summary>
    [TestMethod]
    public void ATokenWhoseCodeAndAuthorityIriDisagreeIsRefused()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(
            () => EuStructuralLocation.Parse("{AN|" + Fd370 + "/AR} 1", Fd370));
        StringAssert.Contains(error.Message, Fd370 + "/AN");
    }

    /// <summary>
    /// A bare token with no inline authority is refused. This is exactly review/22 section 3's
    /// rendering, and the refusal is what records that rendering as incomplete against live data.
    /// </summary>
    [TestMethod]
    public void ReviewTwentyTwosBareLocationRenderingIsRefusedBecauseItCarriesNoAuthority()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(
            () => EuStructuralLocation.Parse("{AR} 54 {PA} 1 {PTA} (e)", Fd370));
        StringAssert.Contains(error.Message, "carries no authority IRI");
    }

    /// <summary>
    /// A multi-token location keeps its order, and a token with no trailing value is ordinary.
    /// </summary>
    /// <remarks>
    /// All five retained rows carry exactly one token, so this shape is grounded in the canary
    /// event <c>lex-event-20260904T175313280Z-e99a2a04ab2e44fb8bc5a5aa66d14451</c> rather than in
    /// the retained fixture. That event quotes a two-token location and elides the second IRI with
    /// an ellipsis, so the second token's IRI here is completed to the form the list requires and
    /// is not a transcription.
    /// </remarks>
    [TestMethod]
    public void AMultiTokenLocationKeepsItsOrderAndItsValuelessFinalToken()
    {
        var location = EuStructuralLocation.Parse(
            "{AR|" + Fd370 + "/AR} 23 {PTA|" + Fd370 + "/PTA}",
            Fd370);

        Assert.HasCount(2, location.Tokens);
        Assert.AreEqual("AR", location.Tokens[0].Code);
        Assert.AreEqual("23", location.Tokens[0].Value);
        Assert.AreEqual("PTA", location.Tokens[1].Code);
        Assert.IsNull(location.Tokens[1].Value, "A final token with no trailing value is real data.");
    }

    /// <summary>
    /// The fd_370 member set is open. <c>PTA</c> is observed and is not among the two codes the
    /// retained rows show, so closing the member set would refuse real data.
    /// </summary>
    [TestMethod]
    public void AMemberCodeOutsideTheRetainedTwoIsAcceptedBecauseTheListIsPinnedNotTheMembers()
    {
        var location = EuStructuralLocation.Parse("{PTA|" + Fd370 + "/PTA} (e)", Fd370);
        Assert.AreEqual("PTA", location.Tokens[0].Code);
        Assert.AreEqual("(e)", location.Tokens[0].Value);
    }

    // --- The binding sits on the Facts layer ----------------------------------------------------

    /// <summary>
    /// The asserted edge is a real <see cref="PublisherRelation"/> inside a real
    /// <see cref="RelationFact"/>, not a parallel re-declaration of one.
    /// </summary>
    [TestMethod]
    public void TheAssertedEdgeIsAPublisherRelationInsideARelationFact()
    {
        var axiom = Axiom(RetainedRows()[0], "axiom:invented-0");
        var fact = axiom.Edge.Fact;

        Assert.AreEqual(RelationFact.Identity, fact.Schema);
        Assert.AreEqual(RelationAssertionKind.PublisherAsserted, fact.Kind);
        Assert.IsNotNull(fact.PublisherAsserted);
        Assert.IsNull(fact.OntologyAuthorizedInverse);
        Assert.IsNull(fact.LocalInboundView);

        var asserted = fact.PublisherAsserted;
        Assert.AreEqual(PublisherRelation.Identity, asserted.Schema);
        Assert.AreEqual(AmendsPredicate, asserted.PredicateUri);
        Assert.AreSame(asserted, axiom.Edge.Asserted);
    }

    /// <summary>
    /// The edge carries the observation that produced it, so a live run can always say where an
    /// edge came from. The first version of E4 could not.
    /// </summary>
    [TestMethod]
    public void TheAssertedEdgeCarriesItsSourceObservationId()
    {
        var axiom = Axiom(RetainedRows()[0], "axiom:invented-0");

        Assert.AreEqual("obs:invented-axiom:invented-0", axiom.Edge.Asserted.SourceObservationId);
    }

    /// <summary>
    /// An EU act is not a case, so the fact's ECLI state is computed as not applicable rather than
    /// accepted from a caller who could get it wrong.
    /// </summary>
    [TestMethod]
    public void TheTargetEcliStateIsComputedFromTheTargetItself()
    {
        var axiom = Axiom(RetainedRows()[0], "axiom:invented-0");

        Assert.AreEqual(EcliState.EcliNotApplicable, axiom.Edge.Fact.TargetEcliState);
        Assert.IsNull(axiom.Edge.Fact.TargetEcli());
    }

    /// <summary>Every declared body scope reaches the fact unchanged.</summary>
    [TestMethod]
    public void EveryTargetBodyScopeIsDrivenAndKept()
    {
        foreach (var scope in Enum.GetValues<TargetBodyScope>())
        {
            var edge = EuRelationEdgeBinding.Create(
                Work("00034b8a-6af2-4207-bc76-d24a10b5125c"),
                Work("62212f0d-011f-471e-a033-bf56990d4329"),
                AmendsPredicate,
                scope,
                [],
                "obs:invented-scope");

            Assert.AreEqual(scope, edge.Fact.TargetBodyScope);
        }

        Assert.HasCount(3, Enum.GetValues<TargetBodyScope>());
    }

    /// <summary>
    /// The predicate set is closed. An unpinned but syntactically valid absolute URI is refused by
    /// name, which the first version of E4 accepted.
    /// </summary>
    [TestMethod]
    public void AnUnpinnedPredicateIsRefusedByName()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => EuRelationEdgeBinding.Create(
            Work("00034b8a-6af2-4207-bc76-d24a10b5125c"),
            Work("62212f0d-011f-471e-a033-bf56990d4329"),
            "http://publications.europa.eu/ontology/cdm#work_cites_work",
            TargetBodyScope.BodyInScopeNotHeld,
            [],
            "obs:invented-unpinned"));

        StringAssert.Contains(error.Message, "work_cites_work");
        StringAssert.Contains(error.Message, "not one of the pinned");
    }

    /// <summary>Exactly the four asserted predicates are accepted, and the inverse is not one.</summary>
    [TestMethod]
    public void ExactlyTheFourAssertedPredicatesAreAccepted()
    {
        foreach (var predicate in new[]
                 {
                     AmendsPredicate, RepealsPredicate, BasedOnPredicate, ConsolidatesPredicate,
                 })
        {
            var edge = EuRelationEdgeBinding.Create(
                Work("00034b8a-6af2-4207-bc76-d24a10b5125c"),
                Work("62212f0d-011f-471e-a033-bf56990d4329"),
                predicate,
                TargetBodyScope.BodyInScopeNotHeld,
                [],
                "obs:invented-pinned");

            Assert.AreEqual(predicate, edge.Asserted.PredicateUri);
        }

        // The inverse predicate returns zero rows store-wide, so it is not an asserted predicate.
        var error = Assert.ThrowsExactly<ArgumentException>(() => EuRelationEdgeBinding.Create(
            Work("62212f0d-011f-471e-a033-bf56990d4329"),
            Work("00034b8a-6af2-4207-bc76-d24a10b5125c"),
            AmendedByPredicate,
            TargetBodyScope.BodyInScopeNotHeld,
            [],
            "obs:invented-inverse"));
        StringAssert.Contains(error.Message, AmendedByPredicate);
        StringAssert.Contains(error.Message, "derived inverse");
    }

    // --- The derived inverse is a Facts DerivedInverseRelation ----------------------------------

    /// <summary>
    /// The inverse is a Facts <see cref="DerivedInverseRelation"/> carrying the publisher's own
    /// <c>owl:inverseOf</c> declaration, with the ontology and version it was observed at.
    /// </summary>
    [TestMethod]
    public void TheDerivedInverseCarriesTheObservedOntologyAxiom()
    {
        var forward = Axiom(RetainedRows()[0], "axiom:invented-0").Edge;
        var inverse = EuDerivedAmendmentInverse.From(forward, "obs:invented-ontology");

        Assert.AreEqual(DerivedInverseRelation.Identity, inverse.Schema);
        Assert.AreEqual(AmendedByPredicate, inverse.PredicateUri);
        Assert.AreEqual(AmendsPredicate, inverse.InverseOfPredicateUri);
        Assert.AreSame(forward.Asserted, inverse.DerivedFrom);

        var axiom = inverse.AuthorizingAxiom;
        Assert.AreEqual(OntologyUri, axiom.OntologyUri);
        Assert.AreEqual(OntologyVersion, axiom.OntologyVersion);
        Assert.AreEqual(AmendsPredicate, axiom.SubjectPredicateUri);
        Assert.AreEqual(AmendedByPredicate, axiom.ObjectPredicateUri);
        Assert.AreEqual("obs:invented-ontology", axiom.SourceObservationId);
        Assert.IsTrue(axiom.Authorizes(AmendsPredicate, AmendedByPredicate));
    }

    /// <summary>The inverse's endpoints are the forward assertion's endpoints, swapped.</summary>
    [TestMethod]
    public void TheDerivedInverseSwapsTheForwardEndpoints()
    {
        var forward = Axiom(RetainedRows()[0], "axiom:invented-0").Edge;
        var inverse = EuDerivedAmendmentInverse.From(forward, "obs:invented-ontology");

        Assert.IsTrue(inverse.Source.SameIdentity(forward.Asserted.Target));
        Assert.IsTrue(inverse.Target.SameIdentity(forward.Asserted.Source));
    }

    /// <summary>
    /// Only the amends predicate has an observed inverse declaration, so nothing else can be
    /// inverted. An unwitnessed inversion is unconstructible, not merely discouraged.
    /// </summary>
    [TestMethod]
    public void OnlyTheAmendmentEdgeCanBeInvertedBecauseOnlyItHasAnObservedAxiom()
    {
        var repeal = EuRelationEdgeBinding.Create(
            Celex("32016R0679"),
            Celex("31995L0046"),
            RepealsPredicate,
            TargetBodyScope.BodyInScopeHeld,
            [],
            "obs:invented-repeal");

        var error = Assert.ThrowsExactly<ArgumentException>(
            () => EuDerivedAmendmentInverse.From(repeal, "obs:invented-ontology"));
        StringAssert.Contains(error.Message, "no observed owl:inverseOf declaration");
    }

    /// <summary>
    /// Admissibility, as ruled: the asserted binding is evidence, and the derived inverse is a
    /// Facts record that implements no marker and so can never enter a bundle typed against it.
    /// </summary>
    [TestMethod]
    public void OnlyTheAssertedBindingIsAdmissibleEvidence()
    {
        Assert.IsTrue(
            typeof(IEuFactsEvidenceCarrier).IsAssignableFrom(typeof(EuRelationEdgeBinding)),
            "A publisher-asserted EU relation edge is evidence.");

        Assert.IsFalse(
            typeof(IEuFactsEvidenceCarrier).IsAssignableFrom(typeof(DerivedInverseRelation)),
            "REL-002 excludes derived edges from evidence bundles.");

        foreach (var type in new[]
                 {
                     typeof(EuLocatedAmendmentAxiom), typeof(EuRepealEdge),
                     typeof(EuConstituentClosure), typeof(EuConstituentStep),
                     typeof(EuStructuralLocation), typeof(EuAuthorityQualifiedToken),
                     typeof(EuValidityDate), typeof(EuDerivedAmendmentInverse),
                 })
        {
            Assert.IsFalse(
                typeof(IEuFactsEvidenceCarrier).IsAssignableFrom(type),
                $"{type.Name} must not be admissible to an evidence bundle.");
        }
    }

    // --- The publisher's raw values survive beside the typed reading ----------------------------

    /// <summary>
    /// The Facts-layer axiom carries the publisher's raw values under the annotation predicates,
    /// and omits a qualifier for an annotation the publisher did not bind. The same axiom reaches
    /// the publisher relation, so the qualifiers travel with the edge rather than beside it.
    /// </summary>
    [TestMethod]
    public void TheFactsAxiomCarriesRawValuesAndOmitsUnboundAnnotations()
    {
        var withStart = Axiom(RetainedRows()[4], "axiom:invented-4");
        CollectionAssert.AreEqual(
            new[] { LocationAnnotation, StartAnnotation, LinkTargetAnnotation, RoleAnnotation },
            withStart.Axiom.Qualifiers.Select(qualifier => qualifier.PredicateUri).ToArray());
        CollectionAssert.AreEqual(
            new[] { "{AN|" + Fd370 + "/AN} IA", "2010/01/01", "MS", "{M|" + Fd375 + "/M}" },
            withStart.Axiom.Qualifiers.Select(qualifier => qualifier.RawValue).ToArray());

        // The same axiom object is the one the publisher relation carries.
        Assert.HasCount(1, withStart.Edge.Asserted.QualifiedAxioms);
        Assert.AreSame(withStart.Axiom, withStart.Edge.Asserted.QualifiedAxioms[0]);

        var withoutStart = Axiom(RetainedRows()[2], "axiom:invented-2");
        Assert.IsFalse(
            withoutStart.Axiom.Qualifiers.Any(qualifier =>
                string.Equals(qualifier.PredicateUri, StartAnnotation, StringComparison.Ordinal)),
            "An unbound annotation contributes no qualifier at all.");
    }

    /// <summary>A role2 value carrying a trailing value is refused; the observed shape is a bare code.</summary>
    [TestMethod]
    public void ARoleCarryingATrailingValueIsRefused()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => EuLocatedAmendmentAxiom.Create(
            Work("00034b8a-6af2-4207-bc76-d24a10b5125c"),
            Work("62212f0d-011f-471e-a033-bf56990d4329"),
            TargetBodyScope.BodyInScopeNotHeld,
            "{AN|" + Fd370 + "/AN} 1",
            "{R|" + Fd375 + "/R} 7",
            "2000-02-09",
            rawEndOfValidity: null,
            "MS",
            "axiom:invented-role",
            "obs:invented-role"));

        StringAssert.Contains(error.Message, "bare code");
    }

    // --- The repeal edge ------------------------------------------------------------------------

    /// <summary>
    /// The GDPR repeals axiom: start 2018-05-25, link type MS, no end, targeting the 1995
    /// directive, on the repeals predicate and with no structural location.
    /// </summary>
    [TestMethod]
    public void TheGdprRepealsAxiomKeepsItsStartLinkTypeAndTarget()
    {
        var repeal = EuRepealEdge.Create(
            Celex("32016R0679"),
            Celex("31995L0046"),
            TargetBodyScope.BodyInScopeHeld,
            "2018-05-25",
            rawEndOfValidity: null,
            "MS",
            "axiom:invented-repeal",
            "obs:invented-repeal");

        Assert.AreEqual(RepealsPredicate, repeal.Edge.Asserted.PredicateUri);
        Assert.AreEqual(RelationAssertionKind.PublisherAsserted, repeal.Edge.Fact.Kind);
        Assert.AreEqual("2018-05-25", repeal.StartOfValidity!.RawLexicalValue);
        Assert.AreEqual(EuValidityDateShape.HyphenatedIso8601, repeal.StartOfValidity.ObservedShape);
        Assert.IsNotNull(repeal.StartOfValidity.TypedDate);
        Assert.IsNull(repeal.EndOfValidity);
        Assert.AreEqual("MS", repeal.TypeOfLinkTarget);

        // The annotation set difference from a located amendment, recorded as observed: a repeal
        // carries type_of_link_target and carries no reference_to_modified_location.
        var predicates = repeal.Axiom.Qualifiers.Select(qualifier => qualifier.PredicateUri).ToArray();
        CollectionAssert.Contains(predicates, LinkTargetAnnotation);
        CollectionAssert.DoesNotContain(predicates, LocationAnnotation);
        CollectionAssert.DoesNotContain(predicates, RoleAnnotation);
    }

    /// <summary>A repeal with no dates at all is constructible, because both are optional.</summary>
    [TestMethod]
    public void ARepealWithNeitherValidityDateIsConstructible()
    {
        var repeal = EuRepealEdge.Create(
            Celex("32016R0679"),
            Celex("31995L0046"),
            TargetBodyScope.BodyOutsideScope,
            rawStartOfValidity: null,
            rawEndOfValidity: null,
            "MS",
            "axiom:invented-repeal-bare",
            "obs:invented-repeal-bare");

        Assert.IsNull(repeal.StartOfValidity);
        Assert.IsNull(repeal.EndOfValidity);
        Assert.HasCount(1, repeal.Axiom.Qualifiers);
    }
}
