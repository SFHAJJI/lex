using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Stage 2 item E6, live half, slices three and four: delivered case-law rows becoming E6 bindings.
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

    /// <summary>A sector-6 CELEX. Sector 6 is case law, which is what makes this prove a case.</summary>
    private const string CaseCelex = "62019CJ0311";

    /// <summary>A well-formed CELEX in sector 3, which is a regulation and not a case.</summary>
    private const string NonCaseCelex = "32016R0679";

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

    /// <summary>
    /// One delivered row, with every term and every marker chosen independently so a test can put
    /// them in disagreement on purpose.
    /// </summary>
    private static RepeatedEnumerationRow Row(
        RepeatedEnumerationRdfTerm ecli,
        string ecliKind,
        RepeatedEnumerationRdfTerm? celex = null,
        string celexKind = EuCaseLawDiscoveryPlan.CelexNotAskedKind,
        string predicate = EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
        string act = Act)
    {
        var celexTerm = celex ?? Unbound();
        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            Iri(CaseWork), Iri(predicate), Iri(act),
            ecli, Literal(ecliKind),
            celexTerm, Literal(celexKind),
            Literal("1"),
            Literal(CaseWork), Literal(predicate), Literal(act),
            Literal(ecli.Value ?? ""), Literal(celexTerm.Value ?? ""),
        };
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    /// <summary>The shape the plan delivers for a case whose ECLI the publisher holds.</summary>
    private static RepeatedEnumerationRow EcliRow(
        string predicate = EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
        string act = Act) =>
        Row(Literal(Ecli), "literal", predicate: predicate, act: act);

    /// <summary>The shape the plan delivers for a case with no ECLI but a CELEX.</summary>
    private static RepeatedEnumerationRow CelexRow(string celex = CaseCelex) =>
        Row(Unbound(), EuCaseLawDiscoveryPlan.UnboundEcliKind,
            celex: Literal(celex), celexKind: "literal");

    /// <summary>The shape the plan delivers for a case with neither identity.</summary>
    private static RepeatedEnumerationRow NeitherRow() =>
        Row(Unbound(), EuCaseLawDiscoveryPlan.UnboundEcliKind,
            celex: Unbound(), celexKind: EuCaseLawDiscoveryPlan.UnboundCelexKind);

    private static Dictionary<string, TargetBodyScope> Scopes(
        TargetBodyScope scope = TargetBodyScope.BodyInScopeHeld) =>
        new(StringComparer.Ordinal) { [Act] = scope };

    /// <summary>A delivered row becomes a binding carrying the case, the act and the predicate.</summary>
    [TestMethod]
    public void ADeliveredRowBecomesAnE6BindingWithItsOwnTerms()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [EcliRow()], Profile(), Scopes(), Evidence);

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
        Assert.AreEqual(EcliState.EcliPresent, relation.Binding.CaseEcliState);
    }

    /// <summary>
    /// A case whose publisher record carries no ECLI is kept under its CELEX and typed, never dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the clause the head before it failed. Issue #415's authority reads "a Cellar case
    /// relation without ECLI stays under its Cellar or CELEX identity, typed ecli_missing, never
    /// dropped, ECLI never invented", and slice three refused the whole production for exactly this
    /// row — taking the act's other links with it.
    /// </para>
    /// <para>
    /// <see cref="EcliState.EcliNotInThisSet"/> requires the target to be a case, and
    /// <c>OfficialIdentifier.ProvesCase()</c> accepts a well-formed ECLI or a <b>sector-6</b> CELEX.
    /// So the plan now asks for the CELEX on the branch where the ECLI is absent, and that answer is
    /// what makes this state reachable at all. No ECLI is invented: the identity set carries the
    /// CELEX the publisher actually delivered, and the state says the set has no ECLI in it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AnEcliAbsentCaseIsCarriedUnderItsCelexAndTypedNotInThisSet()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [CelexRow()], Profile(), Scopes(), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.Relations!);

        var relation = result.Relations![0];
        Assert.AreEqual(
            EcliState.EcliNotInThisSet, relation.Binding.CaseEcliState,
            "the edge is kept and typed rather than dropped.");
        Assert.IsNull(
            relation.Binding.CaseEcli(),
            "no ECLI is invented for a case whose record has none.");
        Assert.IsEmpty(
            result.UnrepresentableForEuWork(Act),
            "a case with a CELEX is representable, so nothing is excluded here.");
    }

    /// <summary>
    /// A case with neither identity is kept as a typed exclusion, not by sinking the act's links.
    /// </summary>
    /// <remarks>
    /// The row is well formed and the citation is real; what is missing is any identity
    /// <c>ProvesCase()</c> accepts, and inventing one is forbidden. Refusing the whole production
    /// would lose the act's other links over a row the publisher delivered honestly, so the citation
    /// is recorded as an exclusion and the good links still arrive. The two are separate readers so a
    /// caller cannot read the links as the whole answer by accident.
    /// </remarks>
    [TestMethod]
    public void ACaseWithNeitherIdentityIsExcludedByNameWithoutSinkingTheActsOtherLinks()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [EcliRow(), NeitherRow()], Profile(), Scopes(), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.ForEuWork(Act), "the good link still arrives.");

        var excluded = result.UnrepresentableForEuWork(Act);
        Assert.HasCount(1, excluded);
        Assert.AreEqual(CaseWork, excluded[0].CaseWorkUri);
        Assert.AreEqual(
            EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
            excluded[0].PredicateUri,
            "the citation that could not be carried is named, not merely counted.");
    }

    /// <summary>
    /// A well-formed CELEX that is not case law is an exclusion, decided by the contract's own rule.
    /// </summary>
    /// <remarks>
    /// <c>32016R0679</c> is the GDPR: a real identity for something that is not a case. The producer
    /// asks <c>OfficialIdentifier.ProvesCase()</c> rather than restating "sector 6", so the sector
    /// rule has exactly one owner. Carrying this as a case would assert that a regulation is a
    /// judgment.
    /// </remarks>
    [TestMethod]
    public void ACelexThatDoesNotProveCaseLawIsExcludedRatherThanCarriedAsACase()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [CelexRow(NonCaseCelex)], Profile(), Scopes(), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsEmpty(result.ForEuWork(Act));
        Assert.HasCount(1, result.UnrepresentableForEuWork(Act));
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
            [Row(Literal(Ecli), EuCaseLawDiscoveryPlan.UnboundEcliKind,
                celex: Unbound(), celexKind: EuCaseLawDiscoveryPlan.UnboundCelexKind)],
            Profile(), Scopes(), Evidence);
        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, boundTermUnboundMarker.Refusal);
        StringAssert.Contains(boundTermUnboundMarker.Detail!, "disagree");

        // An absent ECLI whose marker claims a literal was delivered.
        var unboundTermLiteralMarker = EuCaseLawLinkProducer.DecodeRows(
            [Row(Unbound(), "literal")], Profile(), Scopes(), Evidence);
        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, unboundTermLiteralMarker.Refusal);
        StringAssert.Contains(unboundTermLiteralMarker.Detail!, "disagree");

        // The CELEX half of the same rule: a bound CELEX whose own marker claims none was delivered.
        var celexTermMarkerClash = EuCaseLawLinkProducer.DecodeRows(
            [Row(Unbound(), EuCaseLawDiscoveryPlan.UnboundEcliKind,
                celex: Literal(CaseCelex), celexKind: EuCaseLawDiscoveryPlan.UnboundCelexKind)],
            Profile(), Scopes(), Evidence);
        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, celexTermMarkerClash.Refusal);
        StringAssert.Contains(celexTermMarkerClash.Detail!, "disagree");
    }

    /// <summary>
    /// A row answering both identity questions, or neither, is not a delivery this plan can produce.
    /// </summary>
    /// <remarks>
    /// The plan asks for the CELEX only on the branch where the ECLI is absent, so exactly one of the
    /// two questions is answered per row and <c>case_celex_kind</c> says which. A row carrying an ECLI
    /// and a real CELEX answer did not come from this query, and reading it as though it did would
    /// mean trusting a shape nothing produced.
    /// </remarks>
    [TestMethod]
    public void ARowThatAnswersBothIdentityQuestionsOrNeitherIsRefused()
    {
        // The detail is asserted, not just the refusal kind. Every guard in this producer refuses
        // with RowNotAdmitted, so asserting only the kind would pass no matter which guard fired —
        // and I found by mutation that deleting this coherence check entirely left both assertions
        // green, because each row then tripped a different guard on its way out.
        var both = EuCaseLawLinkProducer.DecodeRows(
            [Row(Literal(Ecli), "literal", celex: Literal(CaseCelex), celexKind: "literal")],
            Profile(), Scopes(), Evidence);
        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, both.Refusal);
        StringAssert.Contains(both.Detail!, "both questions or neither");

        var neither = EuCaseLawLinkProducer.DecodeRows(
            [Row(Unbound(), EuCaseLawDiscoveryPlan.UnboundEcliKind,
                celexKind: EuCaseLawDiscoveryPlan.CelexNotAskedKind)],
            Profile(), Scopes(), Evidence);
        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, neither.Refusal);
        StringAssert.Contains(neither.Detail!, "both questions or neither");
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
            [EcliRow()], Profile(),
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
            [EcliRow(predicate: "http://publications.europa.eu/ontology/cdm#work_cites_nothing")],
            Profile(), Scopes(), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, result.Refusal);
    }

    /// <summary>One malformed row refuses the whole production; the good rows are not kept.</summary>
    /// <remarks>
    /// A malformed row means the delivery itself cannot be trusted, so admitting the rest would hand
    /// back a link set that looks complete and is not. This seat shipped that exact shape on the E8
    /// procedure-event contract and had it found in review. It is the opposite decision from the
    /// unrepresentable-but-well-formed row above, and the difference is whether the delivery can be
    /// believed at all.
    /// </remarks>
    [TestMethod]
    public void OneMalformedRowRefusesTheWholeProductionRatherThanFilteringIt()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [
                EcliRow(),
                Row(Unbound(), "literal"),
                EcliRow(),
            ],
            Profile(), Scopes(), Evidence);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, result.Refusal);
        Assert.IsNull(result.Relations, "two good rows must not be delivered as though they were all of them.");
    }

    /// <summary>A refused production cannot be read as an act with no case law.</summary>
    /// <remarks>
    /// The distinction this guards is between "no judgment cites this act" and "we failed to find
    /// out", which are different answers to a user's question and must not collapse. Both readers
    /// throw, because a caller that fell back to the exclusion list on a refused run would be reading
    /// the same false emptiness by a different route.
    /// </remarks>
    [TestMethod]
    public void ARefusedProductionCannotBeReadAsAnActWithNoCaseLaw()
    {
        var refused = EuCaseLawLinkProducer.DecodeRows(
            [EcliRow()], Profile(),
            new Dictionary<string, TargetBodyScope>(StringComparer.Ordinal), Evidence);

        Assert.ThrowsExactly<InvalidOperationException>(() => refused.ForEuWork(Act));
        Assert.ThrowsExactly<InvalidOperationException>(() => refused.UnrepresentableForEuWork(Act));
    }

    /// <summary>A delivered production answers per act, and only for that act.</summary>
    [TestMethod]
    public void ADeliveredProductionAnswersForTheActAskedAbout()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [EcliRow()], Profile(), Scopes(), Evidence);

        Assert.HasCount(1, result.ForEuWork(Act));
        Assert.IsEmpty(result.ForEuWork(
            "http://publications.europa.eu/resource/cellar/99999999-9999-4999-8999-999999999999"));
    }
}
