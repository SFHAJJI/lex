using System.Linq;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E6 (ledger row <c>REL-005</c>): the EU case-law link contract with the ledger's
/// "ecli_missing" implemented as the Facts layer's provable <see cref="EcliState.EcliNotInThisSet"/>,
/// read from whichever side of the edge is actually the case-law work.
/// </summary>
/// <remarks>
/// <para>
/// This is a rework of the original E6 head. See the remarks on <see cref="EuCaseLawLinkBinding"/>
/// for the full reasoning, including the design objection
/// (coordination/EVENTS.md event <c>lex-event-20260904T044207644Z-8b9be4b0357f4f798a4489b562d2f1e7</c>),
/// the scope ruling
/// (coordination/EVENTS.md event <c>lex-event-20260904T040310991Z-dc5a156f7293412b9680a24f44182bc5</c>)
/// and R4 (coordination/D1-01-OFFICIAL-SOURCE-BOUNDARY-CANDIDATE-5-2026-08-31.md, line 547).
/// </para>
/// <para>
/// Fixtures are hand built directly from review/23-research-temporal.md. <see cref="Gdpr"/>'s CELEX
/// is quoted throughout the file (e.g. section 3, line 60). <see cref="SchremsIi"/>'s CELEX is
/// quoted at section 5, line 77 ("Case law: 62018CJ0311 has 24 expressions...") and its ECLI at
/// section 2, line 45 ("`ECLI:EU:C:2020:559` as a literal on `cdm:case-law_ecli`"). The
/// <c>case-law_interpretes_resource_legal</c> pairing of Schrems II with the GDPR is quoted at
/// section 7, line 91. <see cref="EarliestJudgment"/>'s CELEX is quoted at section 2, line 41
/// ("the earliest judgment 61954CJ0001 is present with ECLI:EU:C:1954:7"); pairing it with the
/// GDPR under the same real predicate is this file's own scaffolding, disclosed as such where it is
/// used, not a claim that review/23 evidences that specific pairing. Every <c>obs:...</c>
/// observation id is invented custody-coordinate scaffolding. Contract only: no live SPARQL call,
/// no adapter, no judgment text.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuCaseLawLinkTests
{
    private const string N = "Lex.V3.Contracts.Source.Europe.";
    private const string Facts = "Lex.V3.Contracts.Facts.";

    /// <summary>Schrems II's real ECLI (review/23-research-temporal.md section 2, line 45), kept
    /// as one constant so the fixture and the assertions that read it back never drift apart.</summary>
    private const string SchremsIiEcli = "ECLI:EU:C:2020:559";

    // --- Fixtures ---------------------------------------------------------------------------

    /// <summary>GDPR, real CELEX (also E1's own fixture identity: 32016R0679).</summary>
    private static OfficialIdentitySet Gdpr() =>
        new(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Celex, "32016R0679")]);

    /// <summary>
    /// Case C-311/18, "Schrems II", by its real CELEX (review/23-research-temporal.md section 5,
    /// line 77) and real ECLI (section 2, line 45).
    /// </summary>
    private static OfficialIdentitySet SchremsIi() => new(
        PublisherId.EuEurLex,
        [
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "62018CJ0311"),
            new OfficialIdentifier(FactsIdentifierFamily.Ecli, SchremsIiEcli),
        ]);

    /// <summary>
    /// The earliest CJEU judgment (review/23-research-temporal.md section 2, line 41: "the
    /// earliest judgment 61954CJ0001 is present with ECLI:EU:C:1954:7"), by its real CELEX alone.
    /// The identity set built here deliberately omits the ECLI: it exists (EU:C:1954:7, quoted
    /// above) but is not carried by this particular assertion set, which is exactly the
    /// real-world shape <see cref="EcliState.EcliNotInThisSet"/> exists to describe honestly. This
    /// is not a claim that the publisher has no ECLI for this case; it is the opposite, made
    /// deliberately, to prove the state names the set and not the publisher.
    /// </summary>
    private static OfficialIdentitySet EarliestJudgment() =>
        new(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Celex, "61954CJ0001")]);

    /// <summary>
    /// "Schrems II case-law_interpretes_resource_legal lists 31995L0046, 32016R0679, 12007P/TXT
    /// and Charter articles" (review/23 section 7, line 91). The real predicate, in its real,
    /// evidenced direction: the case is the source, the GDPR is the target, so
    /// <see cref="EuCaseLawLinkBinding.CaseSide"/> resolves to
    /// <see cref="EuCaseLawLinkCaseSide.Source"/> and the case's own ECLI
    /// (<see cref="SchremsIiEcli"/>, present in its own identity set) reaches
    /// <see cref="EcliState.EcliPresent"/> on the case side.
    /// </summary>
    private static EuCaseLawLinkBinding SchremsIiInterpretsGdpr() => EuCaseLawLinkBinding.Create(
        source: SchremsIi(),
        target: Gdpr(),
        predicateUri: EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
        targetBodyScope: TargetBodyScope.BodyInScopeHeld,
        qualifiedAxioms: [],
        sourceObservationId: "obs:schrems-ii-interpretes-gdpr");

    /// <summary>
    /// The earliest judgment interpreting the GDPR, under the same real, evidenced predicate as
    /// <see cref="SchremsIiInterpretsGdpr"/>. Pairing <see cref="EarliestJudgment"/> with the GDPR
    /// specifically is this file's own scaffolding (review/23 gives no worked instance connecting
    /// them); what is real is both identities individually and the predicate's real, evidenced
    /// direction. The case's own identity set carries no ECLI, so the case side reaches
    /// <see cref="EcliState.EcliNotInThisSet"/>.
    /// </summary>
    private static EuCaseLawLinkBinding EarliestJudgmentInterpretsGdpr() => EuCaseLawLinkBinding.Create(
        source: EarliestJudgment(),
        target: Gdpr(),
        predicateUri: EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
        targetBodyScope: TargetBodyScope.BodyInScopeHeld,
        qualifiedAxioms: [],
        sourceObservationId: "obs:earliest-judgment-interpretes-gdpr");

    /// <summary>
    /// Synthetic scaffolding only, not from a worked review/23 instance: review/23 evidences
    /// <c>work_cites_work</c> generically (section 7, line 91) but gives no specific example
    /// placing a case at its target, the direction the original E6 head incorrectly treated as
    /// the evidenced one. This fixture exists solely to exercise <see cref="EuCaseLawLinkBinding"/>
    /// on that direction: the consistency check against <see cref="RelationFact.TargetEcliState"/>
    /// when the case is the target, and the judgment-body refusal below. The GDPR is reused as a
    /// non-case source purely for convenience; no claim is made that the GDPR cites Schrems II.
    /// </summary>
    private static EuCaseLawLinkBinding SyntheticWorkCitesWorkWithCaseAtTarget(TargetBodyScope targetBodyScope) =>
        EuCaseLawLinkBinding.Create(
            source: Gdpr(),
            target: SchremsIi(),
            predicateUri: EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
            targetBodyScope: targetBodyScope,
            qualifiedAxioms: [],
            sourceObservationId: "obs:synthetic-case-at-target-" + targetBodyScope);

    /// <summary>
    /// Synthetic scaffolding only, same disclosure as <see cref="SyntheticWorkCitesWorkWithCaseAtTarget"/>,
    /// built to prove the Facts wire vocabulary can express <c>ecli_not_in_this_set</c> (never
    /// <c>ecli_missing</c>) when the case sits at the edge's target and lacks an ECLI in that
    /// identity set: <see cref="RelationFact.TargetEcliState"/> only ever carries that literal
    /// when the case is at the target, since Facts has no source-side ECLI field at all.
    /// </summary>
    private static EuCaseLawLinkBinding SyntheticWorkCitesWorkWithCaseAtTargetLackingEcli() =>
        EuCaseLawLinkBinding.Create(
            source: Gdpr(),
            target: EarliestJudgment(),
            predicateUri: EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
            targetBodyScope: TargetBodyScope.BodyInScopeNotHeld,
            qualifiedAxioms: [],
            sourceObservationId: "obs:synthetic-case-at-target-lacking-ecli");

    // --- The real, evidenced direction: the case is the edge's source -------------------------

    [TestMethod]
    public void ANonCaseTargetOnTheRealInterpretsPredicateReachesEcliNotApplicable()
    {
        var binding = SchremsIiInterpretsGdpr();

        Assert.AreEqual(
            EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
            binding.Fact.PublisherAsserted!.PredicateUri);
        Assert.AreEqual(RelationAssertionKind.PublisherAsserted, binding.Fact.Kind);
        Assert.AreEqual(EcliState.EcliNotApplicable, binding.Fact.TargetEcliState);
        Assert.IsFalse(binding.Fact.CarriedTarget().IsCase());
        Assert.IsTrue(binding.Fact.CarriedTarget().SameIdentity(Gdpr()));
        Assert.IsNull(binding.Fact.TargetEcli());
    }

    [TestMethod]
    public void TheCaseAtTheSourceCarryingTheEcliLiteralReachesEcliPresentOnTheCaseSide()
    {
        var binding = SchremsIiInterpretsGdpr();

        Assert.AreEqual(EuCaseLawLinkCaseSide.Source, binding.CaseSide);
        Assert.AreEqual(EcliState.EcliPresent, binding.CaseEcliState);
        Assert.AreEqual(SchremsIiEcli, binding.CaseEcli());

        // Facts' own field describes the target, which is not the case here, so per
        // RelationFact's own invariant it can only ever be EcliNotApplicable: the case's real
        // ECLI state lives on CaseEcliState, not on Fact.TargetEcliState, in this direction.
        Assert.AreEqual(EcliState.EcliNotApplicable, binding.Fact.TargetEcliState);
    }

    [TestMethod]
    public void TheCaseAtTheSourceWithNoEcliInTheAssertionSetReachesEcliNotInThisSetOnTheCaseSide()
    {
        var binding = EarliestJudgmentInterpretsGdpr();

        Assert.AreEqual(EuCaseLawLinkCaseSide.Source, binding.CaseSide);
        Assert.AreEqual(EcliState.EcliNotInThisSet, binding.CaseEcliState);
        Assert.IsNull(binding.CaseEcli());
        Assert.AreEqual(EcliState.EcliNotApplicable, binding.Fact.TargetEcliState);

        // The state names the set, not the publisher: EU:C:1954:7 is this case's real ECLI
        // (review/23 section 2, line 41) and it is simply not present in the identity set built
        // here.
        Assert.IsFalse(binding.Fact.PublisherAsserted!.Source.Has(FactsIdentifierFamily.Ecli));
    }

    [TestMethod]
    public void TheLedgersEcliMissingWordingNeverMintsAWireTokenByThatName()
    {
        // Precision one of the scope ruling: the ledger and R4 say "ecli_missing"; the wire
        // vocabulary never carries that string, and EcliNotInThisSet is the reconciled, provable
        // reading, proven here again against a real EU case, on the direction review/23 actually
        // evidences.
        Assert.IsFalse(ClosedVocabulary.WireNames<EcliState>().Contains("ecli_missing"));
        Assert.IsTrue(ClosedVocabulary.WireNames<EcliState>().Contains("ecli_not_in_this_set"));

        // Facts has no source-side ECLI field at all, so the wire literal only ever appears on
        // Fact.TargetEcliState, which requires the case to be at the target (synthetic
        // scaffolding, disclosed on the fixture itself). The evidenced direction's own proof, on
        // the case-as-source side, is CaseEcliState, asserted directly in
        // TheCaseAtTheSourceWithNoEcliInTheAssertionSetReachesEcliNotInThisSetOnTheCaseSide.
        var binding = SyntheticWorkCitesWorkWithCaseAtTargetLackingEcli();
        Assert.AreEqual(EcliState.EcliNotInThisSet, binding.CaseEcliState);
        Assert.AreEqual(EcliState.EcliNotInThisSet, binding.Fact.TargetEcliState);
        Assert.Contains("ecli_not_in_this_set", ContractJson.Serialize(binding.Fact));
    }

    // --- Which side is the case, and the case's ECLI state, are computed, never caller-suppliable --

    [TestMethod]
    public void CreateHasNoParameterThroughWhichAnEcliStateOrACaseSideCouldBeSupplied()
    {
        // The direct enforcement of "computed, never a parameter": a reflection pin over Create's
        // own parameter list. Adding an EcliState or a EuCaseLawLinkCaseSide parameter tomorrow
        // fails this exact comparison.
        var parameters = typeof(EuCaseLawLinkBinding)
            .GetMethod(nameof(EuCaseLawLinkBinding.Create))!
            .GetParameters();

        Assert.IsFalse(parameters.Any(p => p.ParameterType == typeof(EcliState)));
        Assert.IsFalse(parameters.Any(p => p.ParameterType == typeof(EuCaseLawLinkCaseSide)));
        CollectionAssert.AreEqual(
            new[] { "source", "target", "predicateUri", "targetBodyScope", "qualifiedAxioms", "sourceObservationId" },
            parameters.Select(p => p.Name).ToArray());
    }

    // --- The case is the edge's target: the direction the original head got wrong ---------------

    [TestMethod]
    public void WhenTheCaseIsTheTargetTheDerivedStateEqualsRelationFactsOwnTargetEcliState()
    {
        var binding = SyntheticWorkCitesWorkWithCaseAtTarget(TargetBodyScope.BodyInScopeNotHeld);

        Assert.AreEqual(EuCaseLawLinkCaseSide.Target, binding.CaseSide);
        Assert.AreEqual(EcliState.EcliPresent, binding.CaseEcliState);
        Assert.AreEqual(SchremsIiEcli, binding.CaseEcli());

        // The consistency check the design objection asked for, fixed: the prior version asserted
        // binding.Fact.TargetEcliState against binding.CaseEcliState, but EuCaseLawLinkBinding.Create
        // sets both from the identical local (factTargetEcliState is literally caseEcliState
        // whenever caseSide is Target), so that comparison could never fail no matter what value
        // the shared computation produced -- it proved agreement between a value and itself, not
        // correctness. The independent expectation instead: SchremsIi's own identity set literally
        // carries SchremsIiEcli (see the fixture above), so RelationFact's own target-ECLI invariant
        // requires exactly EcliPresent here, checked against that literal rather than against
        // CaseEcliState.
        Assert.AreEqual(EcliState.EcliPresent, binding.Fact.TargetEcliState);
        Assert.AreEqual(SchremsIiEcli, binding.Fact.TargetEcli());
    }

    [TestMethod]
    public void ACaseAtTheTargetWithBodyInScopeHeldIsRefusedBecauseJudgmentTextIsAlwaysLinkOnly()
    {
        // The judgment-body disposition is always link-only, and TargetBodyScope always names
        // the target's own body: when the case sits at the target, BodyInScopeHeld would claim
        // the judgment's own body is held, which this contract never does. The original head let
        // this pairing construct without complaint. The message is asserted, not just the
        // exception type, so a mutant that lets a *different* guard (or RelationFact's own,
        // unrelated invariant) coincidentally throw first is not mistaken for this one firing.
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => SyntheticWorkCitesWorkWithCaseAtTarget(TargetBodyScope.BodyInScopeHeld));
        StringAssert.Contains(thrown.Message, "can never pair with TargetBodyScope.BodyInScopeHeld");
    }

    // --- Neither, or both, sides being a case is refused ----------------------------------------

    [TestMethod]
    public void ANeitherSideIsACaseEdgeIsRefusedAsNotACaseLawLink()
    {
        // Two ordinary, non-case identities on the one predicate generic enough to admit them.
        // The message is asserted so this guard is not confused with some other refusal firing
        // for an unrelated reason on the same malformed input.
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => EuCaseLawLinkBinding.Create(
            Gdpr(), Gdpr(), EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
            TargetBodyScope.BodyInScopeHeld, [], "obs:neither-side-is-a-case"));
        StringAssert.Contains(thrown.Message, "this edge is not a case-law link");
    }

    [TestMethod]
    public void ABothSidesBeingACaseEdgeIsRefused()
    {
        // Two real cases on work_cites_work: nothing pinned here has a worked instance of a
        // judgment naming another judgment as its own identity set, and this binding names
        // exactly one case side rather than picking one of two candidates silently. The message
        // is asserted deliberately: an implementation that dropped this explicit refusal and
        // instead arbitrarily named the source (SchremsIi, which carries an ECLI) as the case
        // side would still throw here, but only because RelationFact's own, unrelated invariant
        // then rejects EcliNotApplicable against a target that is itself a case
        // (EarliestJudgment) -- a coincidental refusal for the wrong reason, which a bare
        // exception-type assertion cannot tell apart from this one.
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => EuCaseLawLinkBinding.Create(
            SchremsIi(), EarliestJudgment(), EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
            TargetBodyScope.BodyInScopeNotHeld, [], "obs:both-sides-are-a-case"));
        StringAssert.Contains(thrown.Message, "this binding names exactly one case side");
    }

    // --- Granularity and judgment-body disposition are fixed, proven across every shape ---------

    [TestMethod]
    public void EveryShapeCarriesActLevelGranularityAndTheLinkOnlyJudgmentBodyDisposition()
    {
        foreach (var binding in new[]
                 {
                     SchremsIiInterpretsGdpr(),
                     EarliestJudgmentInterpretsGdpr(),
                     SyntheticWorkCitesWorkWithCaseAtTarget(TargetBodyScope.BodyInScopeNotHeld),
                 })
        {
            Assert.AreEqual(EuCaseLawGranularity.ActLevel, binding.Granularity);
            Assert.AreEqual(
                EuJudgmentBodyDisposition.LinkOnlyNeverHeldOrFetched, binding.JudgmentBodyDisposition);
        }
    }

    [TestMethod]
    public void EuCaseLawGranularityCarriesExactlyOneMember()
    {
        CollectionAssert.AreEqual(
            new[] { "ActLevel" }, Enum.GetNames<EuCaseLawGranularity>());
    }

    [TestMethod]
    public void EuJudgmentBodyDispositionCarriesExactlyOneMember()
    {
        CollectionAssert.AreEqual(
            new[] { "LinkOnlyNeverHeldOrFetched" }, Enum.GetNames<EuJudgmentBodyDisposition>());
    }

    [TestMethod]
    public void EuCaseLawLinkCaseSideCarriesExactlyTwoMembers()
    {
        CollectionAssert.AreEqual(
            new[] { "Source", "Target" }, Enum.GetNames<EuCaseLawLinkCaseSide>());
    }

    // --- Pinned predicate vocabulary is closed --------------------------------------------------

    [TestMethod]
    public void ThePinnedPredicateVocabularyIsExactlyTheTwoReviewEvidencedPredicates()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "http://publications.europa.eu/ontology/cdm#case-law_interpretes_resource_legal",
                "http://publications.europa.eu/ontology/cdm#work_cites_work",
            },
            EuCaseLawPredicateVocabulary.Pinned.ToArray());
    }

    [TestMethod]
    public void AnUnpinnedPredicateIsRefusedEvenWhenItIsARealNamedCdmPredicate()
    {
        // resource_legal_amended_by_case-law is a real CDM predicate name (review/23 section 3,
        // line 54) deliberately left unpinned: no worked instance exists for it there. Refused
        // all the same as a syntactically valid absolute URI that happens not to be on the pinned
        // list.
        Assert.ThrowsExactly<ArgumentException>(() => EuCaseLawLinkBinding.Create(
            SchremsIi(), Gdpr(),
            "http://publications.europa.eu/ontology/cdm#resource_legal_amended_by_case-law",
            TargetBodyScope.BodyInScopeHeld, [], "obs:unpinned-predicate"));
    }

    [TestMethod]
    public void AnArbitraryAbsoluteUriThatIsNotACdmPredicateAtAllIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuCaseLawLinkBinding.Create(
            SchremsIi(), Gdpr(), "https://example.invalid/not-a-cdm-predicate",
            TargetBodyScope.BodyInScopeHeld, [], "obs:not-a-cdm-predicate"));
    }

    [TestMethod]
    public void ANullPredicateIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuCaseLawLinkBinding.Create(
            SchremsIi(), Gdpr(), null!, TargetBodyScope.BodyInScopeHeld, [], "obs:null-predicate"));
    }

    // --- Reused Facts invariants, exercised through this binding rather than reimplemented -----

    [TestMethod]
    public void ANullSourceIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCaseLawLinkBinding.Create(
            null!, Gdpr(), EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
            TargetBodyScope.BodyInScopeHeld, [], "obs:null-source"));
    }

    [TestMethod]
    public void ANullTargetIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCaseLawLinkBinding.Create(
            SchremsIi(), null!, EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
            TargetBodyScope.BodyInScopeHeld, [], "obs:null-target"));
    }

    [TestMethod]
    public void ANullQualifiedAxiomsListIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCaseLawLinkBinding.Create(
            SchremsIi(), Gdpr(), EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
            TargetBodyScope.BodyInScopeHeld, null!, "obs:null-axioms"));
    }

    [TestMethod]
    public void TargetBodyScopeIsCarriedThroughUnchangedForEveryScope()
    {
        // The case sits at the source throughout (SchremsIi interpreting the GDPR), so every
        // TargetBodyScope value stays legitimate: it always names the GDPR's own, ordinary body,
        // never the case's.
        foreach (var scope in Enum.GetValues<TargetBodyScope>())
        {
            var binding = EuCaseLawLinkBinding.Create(
                SchremsIi(), Gdpr(), EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
                scope, [], "obs:body-scope-" + scope);
            Assert.AreEqual(scope, binding.Fact.TargetBodyScope);
        }
    }

    // --- Construction surface --------------------------------------------------------------------

    [TestMethod]
    public void TheBindingHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuCaseLawLinkBinding::.ctor("
                + Facts + "RelationFact, " + N + "EuCaseLawGranularity, " + N
                + "EuJudgmentBodyDisposition, " + N + "EuCaseLawLinkCaseSide, " + Facts
                + "EcliState) -> " + N + "EuCaseLawLinkBinding",
                "method public static " + N + "EuCaseLawLinkBinding::Create(" + Facts
                + "OfficialIdentitySet, " + Facts + "OfficialIdentitySet, System.String, " + Facts
                + "TargetBodyScope, System.Collections.Generic.IReadOnlyList<" + Facts
                + "QualifiedAxiom>, System.String) -> " + N + "EuCaseLawLinkBinding",
            },
            ConstructionSurface.Of(typeof(EuCaseLawLinkBinding)).ToArray());
    }

    [TestMethod]
    public void NoOtherTypeInTheAssemblyHoldsOrProducesABinding()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuCaseLawLinkBinding).Assembly, typeof(EuCaseLawLinkBinding), true).ToArray());
    }

    [TestMethod]
    public void TheBindingsPublicPropertySurfaceIsExactlyTheseFive()
    {
        CollectionAssert.AreEqual(
            new[] { "CaseEcliState", "CaseSide", "Fact", "Granularity", "JudgmentBodyDisposition" },
            typeof(EuCaseLawLinkBinding).GetProperties()
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray());
    }

    [TestMethod]
    public void EuCaseLawGranularityExposesOnlyItsOneNamedValueAndNoOtherProducerExistsInTheAssembly()
    {
        // Transcribed from ConstructionSurface.Of's actual output, per this project's
        // print-then-transcribe technique.
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "EuCaseLawGranularity::ActLevel -> " + N + "EuCaseLawGranularity",
            },
            ConstructionSurface.Of(typeof(EuCaseLawGranularity)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuCaseLawLinkBinding::<Granularity>k__BackingField -> "
                + N + "EuCaseLawGranularity",
                "property public instance " + N + "EuCaseLawLinkBinding::Granularity() -> " + N
                + "EuCaseLawGranularity",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuCaseLawLinkBinding).Assembly, typeof(EuCaseLawGranularity), true).ToArray());
    }

    [TestMethod]
    public void EuJudgmentBodyDispositionExposesOnlyItsOneNamedValueAndNoOtherProducerExistsInTheAssembly()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "EuJudgmentBodyDisposition::LinkOnlyNeverHeldOrFetched -> "
                + N + "EuJudgmentBodyDisposition",
            },
            ConstructionSurface.Of(typeof(EuJudgmentBodyDisposition)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N
                + "EuCaseLawLinkBinding::<JudgmentBodyDisposition>k__BackingField -> " + N
                + "EuJudgmentBodyDisposition",
                "property public instance " + N + "EuCaseLawLinkBinding::JudgmentBodyDisposition() -> "
                + N + "EuJudgmentBodyDisposition",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuCaseLawLinkBinding).Assembly, typeof(EuJudgmentBodyDisposition), true).ToArray());
    }

    [TestMethod]
    public void EuCaseLawLinkCaseSideExposesOnlyItsTwoNamedValuesAndNoOtherProducerExistsInTheAssembly()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "EuCaseLawLinkCaseSide::Source -> " + N + "EuCaseLawLinkCaseSide",
                "field public static " + N + "EuCaseLawLinkCaseSide::Target -> " + N + "EuCaseLawLinkCaseSide",
            },
            ConstructionSurface.Of(typeof(EuCaseLawLinkCaseSide)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuCaseLawLinkBinding::<CaseSide>k__BackingField -> "
                + N + "EuCaseLawLinkCaseSide",
                "property public instance " + N + "EuCaseLawLinkBinding::CaseSide() -> " + N
                + "EuCaseLawLinkCaseSide",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuCaseLawLinkBinding).Assembly, typeof(EuCaseLawLinkCaseSide), true).ToArray());
    }

    [TestMethod]
    public void EuCaseLawPredicateVocabularyHasNoInstanceProducerAnywhereInTheAssembly()
    {
        // A static holder of const strings and a closed set: its own construction surface is
        // whatever the compiler emits for the static field initializer on Pinned, never an
        // instance a caller could construct or receive.
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private static " + N + "EuCaseLawPredicateVocabulary::.cctor() -> "
                + N + "EuCaseLawPredicateVocabulary",
            },
            ConstructionSurface.Of(typeof(EuCaseLawPredicateVocabulary)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuCaseLawLinkBinding).Assembly, typeof(EuCaseLawPredicateVocabulary), true).ToArray());
    }
}
