using System.Linq;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E6 (ledger row <c>REL-005</c>): the EU case-law link contract with the ledger's
/// "ecli_missing" implemented as the Facts layer's provable <see cref="EcliState.EcliNotInThisSet"/>.
/// See the remarks on <see cref="EuCaseLawLinkBinding"/> for the full reasoning this rework answers,
/// including the scope ruling
/// (coordination/EVENTS.md event <c>lex-event-20260904T040310991Z-dc5a156f7293412b9680a24f44182bc5</c>)
/// and R4 (coordination/D1-01-OFFICIAL-SOURCE-BOUNDARY-CANDIDATE-5-2026-08-31.md).
/// </summary>
/// <remarks>
/// Fixtures are hand built directly from review/23-research-temporal.md sections 7 and 11.
/// <see cref="Gdpr"/>, <see cref="SchremsIi"/> (both identifiers, CELEX and ECLI) and
/// <see cref="EarliestJudgment"/>'s CELEX are the publisher's own values exactly as quoted there.
/// <see cref="CitingCase"/> is invented for this fixture set (no specific citing case is named in
/// review/23's <c>work_cites_work</c> evidence) and is disclosed as such; every <c>obs:...</c>
/// observation id is likewise invented custody-coordinate scaffolding, not a real one. Contract
/// only: no live SPARQL call, no adapter, no judgment text.
/// </remarks>
[TestClass]
public sealed class EuCaseLawLinkTests
{
    private const string N = "Lex.V3.Contracts.Source.Europe.";
    private const string Facts = "Lex.V3.Contracts.Facts.";

    // --- Fixtures ---------------------------------------------------------------------------

    /// <summary>GDPR, real CELEX (also E1's own fixture identity: 32016R0679).</summary>
    private static OfficialIdentitySet Gdpr() =>
        new(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Celex, "32016R0679")]);

    /// <summary>
    /// Case C-311/18, "Schrems II", by its real CELEX and real ECLI
    /// (review/23-research-temporal.md section 7 and section 11).
    /// </summary>
    private static OfficialIdentitySet SchremsIi() => new(
        PublisherId.EuEurLex,
        [
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "62018CJ0311"),
            new OfficialIdentifier(FactsIdentifierFamily.Ecli, "ECLI:EU:C:2020:559"),
        ]);

    /// <summary>
    /// The earliest CJEU judgment (review/23-research-temporal.md section 7: "the earliest
    /// judgment 61954CJ0001 is present with ECLI:EU:C:1954:7"), by its real CELEX alone. The
    /// identity set built here deliberately omits the ECLI: it exists (EU:C:1954:7, quoted above)
    /// but is not carried by this particular assertion set, which is exactly the real-world shape
    /// <see cref="EcliState.EcliNotInThisSet"/> exists to describe honestly. This is not a claim
    /// that the publisher has no ECLI for this case; it is the opposite, made deliberately, to
    /// prove the state names the set and not the publisher.
    /// </summary>
    private static OfficialIdentitySet EarliestJudgment() =>
        new(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Celex, "61954CJ0001")]);

    /// <summary>
    /// A citing case-law work. Invented for this fixture set: review/23's <c>work_cites_work</c>
    /// evidence names a citation count (2,257 citations to GDPR) and that some citing items carry
    /// ECLI, but no specific citing case. The CELEX below is not a real judgment's identity.
    /// </summary>
    private static OfficialIdentitySet CitingCase() =>
        new(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Celex, "62026CJ9001")]);

    /// <summary>
    /// "Schrems II case-law_interpretes_resource_legal lists 31995L0046, 32016R0679, 12007P/TXT
    /// and Charter articles" (review/23 section 11). The real predicate, in its real direction:
    /// the case is the source, the interpreted act is the target, so the target can never be a
    /// case and the resulting state can only be EcliNotApplicable.
    /// </summary>
    private static EuCaseLawLinkBinding SchremsIiInterpretsGdpr() => EuCaseLawLinkBinding.Create(
        source: SchremsIi(),
        target: Gdpr(),
        predicateUri: EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
        targetBodyScope: TargetBodyScope.BodyInScopeHeld,
        qualifiedAxioms: [],
        sourceObservationId: "obs:schrems-ii-interpretes-gdpr");

    /// <summary>
    /// A citing case pointing at Schrems II via <c>work_cites_work</c>. Schrems II's identity
    /// set carries its real ECLI, so the target reaches EcliPresent.
    /// </summary>
    private static EuCaseLawLinkBinding CitingCaseCitesSchremsIi() => EuCaseLawLinkBinding.Create(
        source: CitingCase(),
        target: SchremsIi(),
        predicateUri: EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
        targetBodyScope: TargetBodyScope.BodyInScopeNotHeld,
        qualifiedAxioms: [],
        sourceObservationId: "obs:citing-case-cites-schrems-ii");

    /// <summary>
    /// A citing case pointing at the earliest judgment via <c>work_cites_work</c>. The target's
    /// identity set carries no ECLI, so the target reaches EcliNotInThisSet.
    /// </summary>
    private static EuCaseLawLinkBinding CitingCaseCitesEarliestJudgment() => EuCaseLawLinkBinding.Create(
        source: CitingCase(),
        target: EarliestJudgment(),
        predicateUri: EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
        targetBodyScope: TargetBodyScope.BodyInScopeNotHeld,
        qualifiedAxioms: [],
        sourceObservationId: "obs:citing-case-cites-earliest-judgment");

    // --- The three EcliState shapes, each against a real EU identity -------------------------

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
    public void ACaseTargetCarryingTheEcliLiteralReachesEcliPresentWithTheLiteralRetainedVerbatim()
    {
        var binding = CitingCaseCitesSchremsIi();

        Assert.AreEqual(
            EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
            binding.Fact.PublisherAsserted!.PredicateUri);
        Assert.AreEqual(EcliState.EcliPresent, binding.Fact.TargetEcliState);
        Assert.IsTrue(binding.Fact.CarriedTarget().IsCase());
        Assert.AreEqual("ECLI:EU:C:2020:559", binding.Fact.TargetEcli());
    }

    [TestMethod]
    public void ACaseTargetWithNoEcliInTheAssertionSetReachesEcliNotInThisSet()
    {
        var binding = CitingCaseCitesEarliestJudgment();

        Assert.AreEqual(
            EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
            binding.Fact.PublisherAsserted!.PredicateUri);
        Assert.AreEqual(EcliState.EcliNotInThisSet, binding.Fact.TargetEcliState);
        Assert.IsTrue(
            binding.Fact.CarriedTarget().IsCase(),
            "EcliNotInThisSet only applies to a case (RelationFact's own invariant).");
        Assert.IsNull(binding.Fact.TargetEcli());

        // The state names the set, not the publisher: EU:C:1954:7 is this case's real ECLI
        // (review/23 section 7) and it is simply not present in the identity set built here.
        Assert.IsFalse(binding.Fact.CarriedTarget().Has(FactsIdentifierFamily.Ecli));
    }

    [TestMethod]
    public void TheLedgersEcliMissingWordingNeverMintsAWireTokenByThatName()
    {
        // Precision one of the scope ruling: the ledger and R4 say "ecli_missing"; the wire
        // vocabulary never carries that string, and EcliNotInThisSet is the reconciled, provable
        // reading, proven here again against a real EU case rather than only a Luxembourg one.
        Assert.IsFalse(ClosedVocabulary.WireNames<EcliState>().Contains("ecli_missing"));
        Assert.IsTrue(ClosedVocabulary.WireNames<EcliState>().Contains("ecli_not_in_this_set"));

        var fact = CitingCaseCitesEarliestJudgment().Fact;
        Assert.Contains("ecli_not_in_this_set", ContractJson.Serialize(fact));
    }

    // --- The ECLI state is computed, never caller-suppliable -----------------------------------

    [TestMethod]
    public void CreateHasNoParameterThroughWhichAnEcliStateCouldBeSupplied()
    {
        // The direct enforcement of "computed, never a parameter": a reflection pin over Create's
        // own parameter list. Adding an EcliState parameter tomorrow fails this exact comparison.
        var parameters = typeof(EuCaseLawLinkBinding)
            .GetMethod(nameof(EuCaseLawLinkBinding.Create))!
            .GetParameters();

        Assert.IsFalse(parameters.Any(p => p.ParameterType == typeof(EcliState)));
        CollectionAssert.AreEqual(
            new[] { "source", "target", "predicateUri", "targetBodyScope", "qualifiedAxioms", "sourceObservationId" },
            parameters.Select(p => p.Name).ToArray());
    }

    // --- Granularity and judgment-body disposition are fixed, proven across all three shapes ---

    [TestMethod]
    public void EveryShapeCarriesActLevelGranularityAndTheLinkOnlyJudgmentBodyDisposition()
    {
        foreach (var binding in new[]
                 {
                     SchremsIiInterpretsGdpr(), CitingCaseCitesSchremsIi(), CitingCaseCitesEarliestJudgment(),
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
        // resource_legal_amended_by_case-law is a real CDM predicate name (review/23 section 7)
        // deliberately left unpinned: no worked instance exists for it there. Refused all the
        // same as a syntactically valid absolute URI that happens not to be on the pinned list.
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
        foreach (var scope in Enum.GetValues<TargetBodyScope>())
        {
            var binding = EuCaseLawLinkBinding.Create(
                CitingCase(), SchremsIi(), EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri,
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
                + "EuJudgmentBodyDisposition) -> " + N + "EuCaseLawLinkBinding",
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
    public void TheBindingsPublicPropertySurfaceIsExactlyTheseThree()
    {
        CollectionAssert.AreEqual(
            new[] { "Fact", "Granularity", "JudgmentBodyDisposition" },
            typeof(EuCaseLawLinkBinding).GetProperties()
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray());
    }
}
