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

    private static MachineQueryRendererSource RendererSource()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("eu-case-law-producer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:2d5b8e14-7f36-4a92-b0c8-49e17d3a6b05",
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))),
            bytes);
    }

    /// <summary>
    /// The whole chain runs: executor, proof, verified rows, links. Nothing is supplied but scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE GUARD THAT MAKES E6 REACHED BY SOMETHING. Every other test in this file calls the
    /// decoder with rows a test built and an evidence reference a test invented, which proves the
    /// decoding and proves nothing about whether the family can be run at all — and it could not be:
    /// <c>RunCaseLawLinksAsync</c> and <c>DecodeRows</c> were called by no code in <c>src/</c>.
    /// </para>
    /// <para>
    /// The cited artifact is required to NAME ITS OWN SCHEMA rather than being compared against the
    /// producer's own report of it. On the sibling E8 family a mutation that cited one request's
    /// HTTP evidence instead of the acquisition run survived exactly that weaker assertion, because
    /// both sides of it came from the same value.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheProducerRunsTheFamilyEndToEndAndCitesTheRunsOwnEvidence()
    {
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["CaseLaw"] = EuAcquisitionTestFixture.ScriptFor(
                "CaseLaw", 1,
                [EuAcquisitionTestFixture.CaseLawRow(
                    CaseWork,
                    EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
                    Act,
                    Ecli)],
                EuAcquisitionTestFixture.CaseLawProjection),
        };

        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuCaseLawLinkProducer(
            store,
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            new EuAcquisitionTestFixture.ClassifyingHandler(scripts));

        var result = await producer.RunAsync(
            new EuCaseLawRunRequest(
                EuCaseLawDiscoveryPlan.Create(),
                [Act],
                "urn:uuid:6c81af29-3d47-4e50-9b12-8f0a5e2c7d63",
                RendererSource()),
            Scopes(),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuCaseLawLinkProductionRefusal.None, result.Refusal,
            $"the family must be runnable end to end: {result.Refusal} {result.Detail}");
        Assert.HasCount(1, result.ForEuWork(Act));
        Assert.IsGreaterThan(0, result.ProductRequestCount);

        var cited = await store.ReadByDigestAsync(
            result.CompletionEvidenceRef!.Sha256, CancellationToken.None);
        StringAssert.StartsWith(
            System.Text.Encoding.UTF8.GetString(cited.Span), "lex-http-acquisition-run/1",
            "the links must cite the acquisition RUN, not one request's HTTP evidence.");
    }

    /// <summary>
    /// The acts this run reports asking about are its OWN batch, never the caller's scope map keys.
    /// </summary>
    /// <remarks>
    /// The two are different sets and the difference is a false absence. A caller supplying scopes
    /// for two acts while the batch asks about one would have <c>ForEuWork</c> answer an evidenced
    /// empty list for the act nobody enumerated — indistinguishable from "no judgment cites it".
    /// </remarks>
    [TestMethod]
    public async Task TheRunReportsItsOwnBatchAsCoverageNotTheCallersScopeMap()
    {
        const string NeverAsked = "http://publications.europa.eu/resource/cellar/11111111-2222-3333-4444-555555555555";

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["CaseLaw"] = EuAcquisitionTestFixture.ScriptFor(
                "CaseLaw", 1,
                [EuAcquisitionTestFixture.CaseLawRow(
                    CaseWork,
                    EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
                    Act,
                    Ecli)],
                EuAcquisitionTestFixture.CaseLawProjection),
        };

        var producer = new EuCaseLawLinkProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            new EuAcquisitionTestFixture.ClassifyingHandler(scripts));

        var scopes = new Dictionary<string, TargetBodyScope>(StringComparer.Ordinal)
        {
            [Act] = TargetBodyScope.BodyInScopeHeld,
            [NeverAsked] = TargetBodyScope.BodyInScopeHeld,
        };

        var result = await producer.RunAsync(
            new EuCaseLawRunRequest(
                EuCaseLawDiscoveryPlan.Create(),
                [Act],
                "urn:uuid:7d92b03a-4e58-4f61-ac23-901b6f3d8e74",
                RendererSource()),
            scopes,
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.ForEuWork(Act));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => result.ForEuWork(NeverAsked),
            "a scope supplied for an act the run never asked about does not make it enumerated.");
    }

    /// <summary>
    /// An act requested non-canonically is still that caller's own act, all the way to coverage.
    /// </summary>
    /// <remarks>
    /// <c>EuPackRootCanonicalForm.TryCanonicalize</c> rewrites <c>https://</c> to <c>http://</c> and
    /// drops one trailing slash, and the publisher is asked about — and answers in — the canonical
    /// form. A run that carried the caller's raw spelling forward would fail its own scope check for
    /// an act it does hold a scope for, and would publish coverage the publisher never answered in.
    /// A mutation doing exactly that survived until this test existed, because every other test here
    /// spells its act canonically already and raw equals canonical there.
    /// </remarks>
    [TestMethod]
    public async Task AnActRequestedNonCanonicallyIsAnsweredUnderItsCanonicalForm()
    {
        const string RequestedHttps =
            "https://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1/";

        Assert.AreEqual(
            Act, EuCaseLawDiscoveryPlan.CanonicalizeBatch([RequestedHttps])[0],
            "the plan rewrites this spelling, so raw and canonical genuinely differ here.");

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["CaseLaw"] = EuAcquisitionTestFixture.ScriptFor(
                "CaseLaw", 1,
                [EuAcquisitionTestFixture.CaseLawRow(
                    CaseWork,
                    EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
                    Act,
                    Ecli)],
                EuAcquisitionTestFixture.CaseLawProjection),
        };

        var producer = new EuCaseLawLinkProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            new EuAcquisitionTestFixture.ClassifyingHandler(scripts));

        var result = await producer.RunAsync(
            new EuCaseLawRunRequest(
                EuCaseLawDiscoveryPlan.Create(),
                [RequestedHttps],
                "urn:uuid:9fb4d25c-6a70-4183-ce45-b23d8f5a0096",
                RendererSource()),
            Scopes(),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(EuCaseLawLinkProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(
            1, result.ForEuWork(Act),
            "coverage is the canonical form the publisher was asked about.");
    }

    /// <summary>
    /// A run the publisher never completed is a typed refusal, never an empty success.
    /// </summary>
    /// <remarks>
    /// Driven by failing every custody write, so the session cannot retain what it fetched and the
    /// executor never reaches a receipt. An empty success here would be the worst answer this family
    /// can give — a proven-empty "no judgment cites this act" for a run that did not happen.
    /// </remarks>
    [TestMethod]
    public async Task ARunThatNeverCompletedIsRefusedRatherThanReportedEmpty()
    {
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["CaseLaw"] = EuAcquisitionTestFixture.ScriptFor(
                "CaseLaw", 1,
                [EuAcquisitionTestFixture.CaseLawRow(
                    CaseWork,
                    EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
                    Act,
                    Ecli)],
                EuAcquisitionTestFixture.CaseLawProjection),
        };

        var producer = new EuCaseLawLinkProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
                failWriteDigest: static (_, _) => true),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            new EuAcquisitionTestFixture.ClassifyingHandler(scripts));

        var result = await producer.RunAsync(
            new EuCaseLawRunRequest(
                EuCaseLawDiscoveryPlan.Create(),
                [Act],
                "urn:uuid:a0c5e36d-7b81-4294-df56-c34e906b1107",
                RendererSource()),
            Scopes(),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreNotEqual(
            EuCaseLawLinkProductionRefusal.None, result.Refusal,
            "a run that never completed cannot report a proven empty result.");
        Assert.IsNull(result.Relations);
        Assert.ThrowsExactly<InvalidOperationException>(() => result.ForEuWork(Act));
    }

    // ONE MUTATION SURVIVES THIS FILE and is recorded rather than left unexplained. Reporting a
    // refused VerifiedRepeatedEnumerationRows.TryOpen as EnumerationProofRefused rather than
    // VerifiedRowsRefused survives because nothing here drives a delivery whose enumeration proof
    // holds while its rows will not reopen. It is a real untested branch, not an argued equivalence,
    // and the sibling E8 producer has the identical gap.

    /// <summary>
    /// An act the run asks about with no supplied scope is refused before the request is sent.
    /// </summary>
    /// <remarks>
    /// Caught at the question rather than at the answer. Such an act could produce no links even if
    /// the publisher answered for it, so a run that proceeded would report a proven empty set for an
    /// act it genuinely enumerated — and would have spent the publisher's budget to do it.
    /// </remarks>
    [TestMethod]
    public async Task AnActAskedAboutWithNoSuppliedScopeIsRefusedBeforeTheRun()
    {
        var producer = new EuCaseLawLinkProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            new EuAcquisitionTestFixture.ClassifyingHandler(
                new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)));

        var result = await producer.RunAsync(
            new EuCaseLawRunRequest(
                EuCaseLawDiscoveryPlan.Create(),
                [Act],
                "urn:uuid:8ea3c14b-5f69-4072-bd34-a12c7e4f9f85",
                RendererSource()),
            new Dictionary<string, TargetBodyScope>(StringComparer.Ordinal),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuCaseLawLinkProductionRefusal.RequestedActBodyScopeNotSupplied, result.Refusal);
        Assert.AreEqual(0, result.ProductRequestCount, "nothing was sent.");
    }

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

    /// <summary>
    /// A delivered production answers for the acts it asked about, and refuses for any other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The earlier version of this test asserted the opposite and was wrong. It filtered the
    /// relations by an act the production had never enumerated, got an empty list, and called that
    /// correct — so the API said "no judgment cites this act" about an act nobody had looked for.
    /// That is the same false absence the refused-run guard prevents by a different route, and my own
    /// test had written it down as intended behaviour.
    /// </para>
    /// <para>
    /// The acts asked about are the keys of the supplied body-scope map, which is already the set the
    /// caller had to name, so nothing new has to be threaded through to know it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AProductionRefusesToAnswerForAnActItNeverAskedAbout()
    {
        var result = EuCaseLawLinkProducer.DecodeRows(
            [EcliRow()], Profile(), Scopes(), Evidence);

        Assert.HasCount(1, result.ForEuWork(Act));
        Assert.IsEmpty(result.UnrepresentableForEuWork(Act));

        const string NeverAsked = "http://publications.europa.eu/resource/cellar/99999999-9999-4999-8999-999999999999";
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => result.ForEuWork(NeverAsked),
            "an empty list here would read as a proven absence for an act nobody enumerated.");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => result.UnrepresentableForEuWork(NeverAsked),
            "the exclusion reader must not become the same false absence by another route.");
    }

    /// <summary>
    /// The marker is checked against the term's whole value space, not against one boolean.
    /// </summary>
    /// <remarks>
    /// The plan's <c>BIND</c> produces four values — <c>iri</c>, <c>literal</c>,
    /// <c>unsupported_blank_node</c> and <c>unbound</c>. An earlier version of this producer asked
    /// only "does the marker say unbound", so a literal ECLI carrying an <c>iri</c> marker agreed
    /// with itself and was admitted: half the marker's value space could not contradict anything,
    /// which made the disagreement check weaker than its own name. Found by attacking this head.
    /// </remarks>
    [TestMethod]
    public void AMarkerNamingTheWrongTermKindIsCaughtEvenWhenBothAgreeSomethingWasDelivered()
    {
        var literalTermIriMarker = EuCaseLawLinkProducer.DecodeRows(
            [Row(Literal(Ecli), "iri")], Profile(), Scopes(), Evidence);
        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, literalTermIriMarker.Refusal);
        StringAssert.Contains(literalTermIriMarker.Detail!, "disagree");

        var literalCelexIriMarker = EuCaseLawLinkProducer.DecodeRows(
            [Row(Unbound(), EuCaseLawDiscoveryPlan.UnboundEcliKind,
                celex: Literal(CaseCelex), celexKind: "iri")],
            Profile(), Scopes(), Evidence);
        Assert.AreEqual(EuCaseLawLinkProductionRefusal.RowNotAdmitted, literalCelexIriMarker.Refusal);
        StringAssert.Contains(literalCelexIriMarker.Detail!, "disagree");
    }
}
