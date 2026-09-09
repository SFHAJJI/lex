using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Stage 2 item E6, live half, slice three: delivered case-law rows becoming E6 bindings.
/// </summary>
/// <remarks>
/// This is the slice that makes "real predicates drive link-only judgment text with granularity
/// disclosed" true of delivered rows rather than of a contract nothing reaches. The plan asks the
/// question, the executor runs it, and this reads the answer.
/// </remarks>
[TestClass]
public sealed class EuCaseLawLinkProducerTests
{
    private const string Act = "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string CaseWork = "http://publications.europa.eu/resource/cellar/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Ecli = "ECLI:EU:C:2020:559";

    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:5a1d3c88-4e2f-4b7a-9c61-0d8e2f4a7b93", new string('a', 64));

    private static RepeatedEnumerationInterpretationProfile Profile() =>
        EuCaseLawDiscoveryPlan.Create().CreateDeliveryProfile();

    private static RepeatedEnumerationRdfTerm Iri(string value) =>
        RepeatedEnumerationRdfTerm.Iri(value);

    private static RepeatedEnumerationRdfTerm Literal(string value) =>
        RepeatedEnumerationRdfTerm.Literal(value, null, null);

    private static RepeatedEnumerationRdfTerm Unbound() =>
        RepeatedEnumerationRdfTerm.Unbound();

    /// <summary>One delivered row, with the ECLI term and its marker chosen independently.</summary>
    private static RepeatedEnumerationRow Row(
        RepeatedEnumerationRdfTerm ecli,
        string ecliKind,
        string predicate = EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
        string act = Act)
    {
        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            Iri(CaseWork), Iri(predicate), Iri(act), ecli, Literal(ecliKind),
            Literal("1"), Literal(CaseWork), Literal(predicate), Literal(act), Literal(ecli.Value ?? ""),
        };
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static Dictionary<string, TargetBodyScope> Scopes(TargetBodyScope scope = TargetBodyScope.BodyInScopeHeld) =>
        new(StringComparer.Ordinal) { [Act] = scope };

    /// <summary>A delivered row becomes a binding carrying the case, the act and the predicate.</summary>
    [TestMethod]
    public void ADeliveredRowBecomesAnE6BindingWithItsOwnTerms()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [Row(Literal(Ecli), "literal")], Profile(), Scopes(), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsNotNull(result.Relations);
        Assert.HasCount(1, result.Relations!);

        var relation = result.Relations![0];
        Assert.AreEqual(CaseWork, relation.CaseWorkUri);
        Assert.AreEqual(Act, relation.EuWorkUri);
        Assert.AreEqual(
            EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
            relation.PredicateUri);

        // E6's own fixed answers survive the round trip.
        Assert.AreEqual(EuCaseLawGranularity.ActLevel, relation.Binding.Granularity);
        Assert.AreEqual(
            EuJudgmentBodyDisposition.LinkOnlyNeverHeldOrFetched,
            relation.Binding.JudgmentBodyDisposition);
    }

    /// <summary>
    /// THE MARKER IS NOT THE TERM: a marker disagreeing with its term refuses the row.
    /// </summary>
    /// <remarks>
    /// <c>ecli_kind</c> is a <c>BIND</c> this codebase computes about a row, not the publisher's word
    /// for what the row is. This seat shipped the opposite once — the E1 axiom decoder read a row by
    /// its marker and was found in review — so the term decides here. A marker that contradicts its
    /// term is not merely ignored either: it means the delivery is not what either side believes, so
    /// the row is refused rather than read past on the term alone.
    /// </remarks>
    [TestMethod]
    public void AMarkerThatContradictsItsTermRefusesTheRowRatherThanBeingOverruledSilently()
    {
        // A bound ECLI whose marker claims unbound.
        var boundTermUnboundMarker = EuCaseLawLinkProducer.DecodeRows(
            [Row(Literal(Ecli), EuCaseLawDiscoveryPlan.UnboundEcliKind)], Profile(), Scopes(), Evidence);
        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, boundTermUnboundMarker.Refusal);
        StringAssert.Contains(boundTermUnboundMarker.Detail!, "disagree");

        // An absent ECLI whose marker claims a literal was delivered.
        var unboundTermLiteralMarker = EuCaseLawLinkProducer.DecodeRows(
            [Row(Unbound(), "literal")], Profile(), Scopes(), Evidence);
        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, unboundTermLiteralMarker.Refusal);
        StringAssert.Contains(unboundTermLiteralMarker.Detail!, "disagree");
    }

    /// <summary>
    /// A case with no ECLI is refused, because E6 cannot prove which side is the case without one.
    /// </summary>
    /// <remarks>
    /// This is the honestly-delivered absence the plan asks for explicitly through its
    /// <c>FILTER NOT EXISTS</c> branch — term and marker agree that nothing was delivered. It is
    /// still refused, for a different reason from the disagreement above: <c>ProvesCase()</c> accepts
    /// only a well-formed ECLI or a sector-6 CELEX, and this family projects no CELEX. Recorded
    /// rather than hidden, because it means <c>EcliState.EcliNotInThisSet</c> stays unreachable
    /// through this producer.
    /// </remarks>
    [TestMethod]
    public void AnHonestlyAbsentEcliIsStillRefusedBecauseTheCaseSideCannotBeProven()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [Row(Unbound(), EuCaseLawDiscoveryPlan.UnboundEcliKind)], Profile(), Scopes(), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "prove which side is the case");
    }

    /// <summary>
    /// An act whose body scope the caller did not supply is refused, never defaulted.
    /// </summary>
    /// <remarks>
    /// No case-law row carries a body scope: whether this corpus holds that act's body is a fact
    /// about the corpus. Choosing a value would assert a body-holding fact nobody stated, and
    /// <see cref="TargetBodyScope"/> has three members, so there is no safe default to fall back on.
    /// </remarks>
    [TestMethod]
    public void AnActWithNoSuppliedBodyScopeIsRefusedRatherThanDefaulted()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [Row(Literal(Ecli), "literal")], Profile(),
            new Dictionary<string, TargetBodyScope>(StringComparer.Ordinal), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.TargetBodyScopeNotSupplied, result.Refusal);
        Assert.IsNull(result.Relations);
        StringAssert.Contains(result.Detail!, Act);
    }

    /// <summary>An unpinned predicate is refused, through the vocabulary's own owner.</summary>
    /// <remarks>
    /// The producer does not restate the pinned-set membership test. <c>Create</c> owns that
    /// vocabulary and already refuses by name, so a second copy here would be free to drift from the
    /// one that decides. This proves the refusal still arrives.
    /// </remarks>
    [TestMethod]
    public void AnUnpinnedPredicateIsRefusedByTheVocabularysOwnOwner()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [Row(Literal(Ecli), "literal",
                predicate: "http://publications.europa.eu/ontology/cdm#work_cites_nothing")],
            Profile(), Scopes(), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, result.Refusal);
    }

    /// <summary>One unadmitted row refuses the whole production; the good rows are not kept.</summary>
    /// <remarks>
    /// Admitting the rest would hand back a link set that looks complete and is not, and the dropped
    /// row would leave nothing behind to notice. This seat shipped that exact shape on the E8
    /// procedure-event contract and had it found in review.
    /// </remarks>
    [TestMethod]
    public void OneUnadmittedRowRefusesTheWholeProductionRatherThanFilteringIt()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [
                Row(Literal(Ecli), "literal"),
                Row(Unbound(), EuCaseLawDiscoveryPlan.UnboundEcliKind),
                Row(Literal(Ecli), "literal"),
            ],
            Profile(), Scopes(), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, result.Refusal);
        Assert.IsNull(result.Relations, "two good rows must not be delivered as though they were all of them.");
    }

    /// <summary>A refused production cannot be read as an act with no case law.</summary>
    /// <remarks>
    /// The distinction this guards is between "no judgment cites this act" and "we failed to find
    /// out", which are different answers to a user's question and must not collapse.
    /// </remarks>
    [TestMethod]
    public void ARefusedProductionCannotBeReadAsAnActWithNoCaseLaw()
    {
        var refused = EuCaseLawLinkProducer.DecodeRows(
            [Row(Literal(Ecli), "literal")], Profile(),
            new Dictionary<string, TargetBodyScope>(StringComparer.Ordinal), Evidence);

        Assert.ThrowsExactly<InvalidOperationException>(() => refused.ForEuWork(Act));
    }

    /// <summary>A delivered production answers per act, and only for that act.</summary>
    [TestMethod]
    public void ADeliveredProductionAnswersForTheActAskedAbout()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [Row(Literal(Ecli), "literal")], Profile(), Scopes(), Evidence);

        Assert.HasCount(1, result.ForEuWork(Act));
        Assert.IsEmpty(result.ForEuWork(
            "http://publications.europa.eu/resource/cellar/99999999-9999-4999-8999-999999999999"));
    }
}
