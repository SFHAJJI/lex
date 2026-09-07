using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// E5 (#414), REL-003: the two-source LU-to-EU transposition bridge keeps its publishers apart,
/// answers regulations rather than falling silent, and never lets its own join pass as a
/// publisher's assertion.
/// </summary>
[TestClass]
public sealed class EuTranspositionBridgeTests
{
    private const string Gdpr = "http://publications.europa.eu/resource/celar/32016R0679";
    private const string Directive = "http://publications.europa.eu/resource/celar/31995L0046";
    private const string LuMeasure = "http://data.legilux.public.lu/eli/etat/leg/loi/2018/08/01";

    private static SourceArtifactRef Ev(string label) =>
        new("urn:uuid:00000000-0000-4000-8000-00000000e5" + label, new string('a', 64));

    /// <summary>A completion-evidence ref, distinct from any side's evidence.</summary>
    private static SourceArtifactRef EvC(string label) =>
        new("urn:uuid:00000000-0000-4000-8000-00000000c5" + label, new string('b', 64));

    private static EuTranspositionSide Side(EuTranspositionAssertedBy by, string label = "01") =>
        new(by, LuMeasure, Ev(label));

    /// <summary>A source that asserted a measure, its bounded acquisition complete.</summary>
    private static EuTranspositionSourceAcquisition Asserted(
        EuTranspositionAssertedBy by, string label = "01") =>
        new(by, EuRelationAcquisitionState.Complete, Side(by, label), EvC(label));

    /// <summary>A source whose completed acquisition found nothing: a proven absence.</summary>
    private static EuTranspositionSourceAcquisition ProvenAbsent(
        EuTranspositionAssertedBy by, string label = "02") =>
        new(by, EuRelationAcquisitionState.Complete, null, EvC(label));

    /// <summary>A source never queried: an open question, not an absence.</summary>
    private static EuTranspositionSourceAcquisition Unacquired(EuTranspositionAssertedBy by) =>
        new(by, EuRelationAcquisitionState.Unacquired, null, null);

    // ---- The separation, which is what REL-003 is for. ----

    /// <summary>
    /// Neither column may hold the other publisher's assertion.
    /// </summary>
    /// <remarks>
    /// This is the collapse the whole contract exists to prevent. Legilux says what Luxembourg
    /// enacted; NIM says what a Member State notified as implementing a directive. They answer
    /// different questions, so a bridge that let one column carry the other's assertion would
    /// publish an agreement neither publisher made.
    /// </remarks>
    [TestMethod]
    public void NeitherColumnMayHoldTheOtherPublishersAssertion()
    {
        var wrongInLegilux = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.Transposable,
            Asserted(EuTranspositionAssertedBy.Nim), Asserted(EuTranspositionAssertedBy.Nim), null));
        Assert.AreEqual("legilux", wrongInLegilux.ParamName);

        var wrongInNim = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.Transposable,
            Asserted(EuTranspositionAssertedBy.Legilux), Asserted(EuTranspositionAssertedBy.Legilux), null));
        Assert.AreEqual("nim", wrongInNim.ParamName);

        // And a publisher's own acquisition cannot hold the other publisher's assertion either.
        var crossed = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionSourceAcquisition(
            EuTranspositionAssertedBy.Legilux,
            EuRelationAcquisitionState.Complete,
            Side(EuTranspositionAssertedBy.Nim),
            Ev("09")));
        Assert.AreEqual("side", crossed.ParamName);
    }

    /// <summary>
    /// Both publishers' sides are carried side by side, each keeping its own asserted_by.
    /// </summary>
    [TestMethod]
    public void BothPublishersSidesAreCarriedWithoutMerging()
    {
        var bridge = new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.Transposable,
            Asserted(EuTranspositionAssertedBy.Legilux, "02"),
            Asserted(EuTranspositionAssertedBy.Nim, "03"),
            new EuNormalisedEliJoin(LuMeasure, Ev("04")));

        Assert.AreEqual(EuTranspositionAssertedBy.Legilux, bridge.Legilux.Side!.AssertedBy);
        Assert.AreEqual(EuTranspositionAssertedBy.Nim, bridge.Nim.Side!.AssertedBy);
        Assert.AreNotSame(bridge.Legilux.Side.EvidenceRef, bridge.Nim.Side.EvidenceRef);
    }

    // ---- The typed answer for regulations. ----

    /// <summary>
    /// A regulation answers not-transposable rather than falling silent.
    /// </summary>
    /// <remarks>
    /// review/23 section 6: "GDPR, being a regulation, has no NIM links ... so 'transposition'
    /// questions only make sense for directives." A caller reading an empty bridge cannot tell "we
    /// did not look" from "this question does not apply"; only the typed answer distinguishes them.
    /// </remarks>
    [TestMethod]
    public void ARegulationAnswersNotTransposableRatherThanFallingSilent()
    {
        var bridge = new EuTranspositionBridge(
            Gdpr, EuWorkKind.Regulation, EuTransposability.NotTransposable,
            ProvenAbsent(EuTranspositionAssertedBy.Legilux),
            ProvenAbsent(EuTranspositionAssertedBy.Nim), null);

        Assert.AreEqual(EuTransposability.NotTransposable, bridge.Transposability);
        Assert.AreEqual(EuTransposability.NotTransposable,
            EuTranspositionBridge.TransposabilityFor(EuWorkKind.Regulation));
        Assert.AreEqual(EuTransposability.Transposable,
            EuTranspositionBridge.TransposabilityFor(EuWorkKind.Directive));
    }

    /// <summary>
    /// Transposability is read from the work kind, never chosen by the caller.
    /// </summary>
    /// <remarks>
    /// The same rule <c>EuSelectionDisposition</c> applies to a selector's policy and
    /// <c>LuxembourgAssertionFactDisposition</c> to a fact's kind: authority comes from the
    /// accepted reading rather than from whoever is constructing. Without it a caller could mark a
    /// regulation transposable and then attach transposition evidence to it.
    /// </remarks>
    [TestMethod]
    public void TransposabilityIsReadFromTheWorkKindNotChosen()
    {
        var claimedTransposable = Assert.ThrowsExactly<ArgumentException>(
            () => new EuTranspositionBridge(
                Gdpr, EuWorkKind.Regulation, EuTransposability.Transposable,
                Unacquired(EuTranspositionAssertedBy.Legilux),
                Unacquired(EuTranspositionAssertedBy.Nim), null));
        Assert.AreEqual("transposability", claimedTransposable.ParamName);

        Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.NotTransposable,
            Unacquired(EuTranspositionAssertedBy.Legilux),
            Unacquired(EuTranspositionAssertedBy.Nim), null));
    }

    /// <summary>
    /// A not-transposable work carries no side, rather than storing evidence that contradicts it.
    /// </summary>
    [TestMethod]
    public void ANotTransposableWorkCarriesNoTranspositionSide()
    {
        foreach (var (legilux, nim) in new (EuTranspositionSourceAcquisition, EuTranspositionSourceAcquisition)[]
                 {
                     (Asserted(EuTranspositionAssertedBy.Legilux, "05"),
                         ProvenAbsent(EuTranspositionAssertedBy.Nim)),
                     (ProvenAbsent(EuTranspositionAssertedBy.Legilux),
                         Asserted(EuTranspositionAssertedBy.Nim, "06")),
                 })
        {
            var error = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionBridge(
                Gdpr, EuWorkKind.Regulation, EuTransposability.NotTransposable, legilux, nim, null));
            Assert.AreEqual("transposability", error.ParamName);
        }
    }

    // ---- The join is ours, and says so. ----

    /// <summary>
    /// The normalised ELI join is always derived and carries no publisher.
    /// </summary>
    /// <remarks>
    /// S2-A02: a derived view never becomes a publisher claim. The join exists because the two
    /// publishers spell one national measure differently, so it is Lex's reading rather than
    /// anyone's assertion. <c>IsDerived</c> is computed rather than supplied for the same reason
    /// <c>EuLegislationSummary.Licence</c> is: a caller who could set it could set it wrong.
    /// </remarks>
    [TestMethod]
    public void TheNormalisedEliJoinIsAlwaysDerivedAndNamesNoPublisher()
    {
        var join = new EuNormalisedEliJoin(LuMeasure, Ev("07"));

        Assert.IsTrue(join.IsDerived);
        Assert.IsNull(
            typeof(EuNormalisedEliJoin).GetProperty(nameof(EuTranspositionSide.AssertedBy)),
            "the join must carry no publisher; a derived reading is nobody's assertion.");
        Assert.IsFalse(
            typeof(EuNormalisedEliJoin).GetConstructors().Single().GetParameters()
                .Any(parameter => parameter.Name == "isDerived"),
            "IsDerived must be computed, not supplied, so no caller can set it wrong.");
    }

    // ---- FINDING 1: an absence is proven or it is an open question, never ambiguous. ----

    /// <summary>
    /// A completed acquisition retains its completion evidence, and only a completed one may.
    /// </summary>
    /// <remarks>
    /// Decision 64: a genuinely empty set is admissible only when that family's bounded enumeration
    /// completed and its completion evidence is retained. Without the first rule "complete" is a
    /// claim rather than a fact; without the second, evidence of completion could sit on an
    /// acquisition that never completed, which is the same lie told the other way round.
    /// </remarks>
    [TestMethod]
    public void CompletionEvidenceBelongsToACompletedAcquisitionAndOnlyToOne()
    {
        var missing = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionSourceAcquisition(
            EuTranspositionAssertedBy.Legilux, EuRelationAcquisitionState.Complete, null, null));
        Assert.AreEqual("completionEvidenceRef", missing.ParamName);

        foreach (var state in new[]
                 {
                     EuRelationAcquisitionState.Unacquired,
                     EuRelationAcquisitionState.Incomplete,
                     EuRelationAcquisitionState.Uncertain,
                 })
        {
            var unearned = Assert.ThrowsExactly<ArgumentException>(
                () => new EuTranspositionSourceAcquisition(
                    EuTranspositionAssertedBy.Nim, state, null, Ev("08")));
            Assert.AreEqual("completionEvidenceRef", unearned.ParamName,
                $"{state} must not carry completion evidence.");
        }
    }

    /// <summary>
    /// Silence from a source that was never queried is not the same fact as a proven absence.
    /// </summary>
    /// <remarks>
    /// THE REVIEWER'S FINDING, and it was mine to have caught. The first version of this contract
    /// accepted a transposable directive with both sides null and no state at all, so "we never
    /// asked Legilux" and "we asked, the enumeration completed, and it asserts nothing" were the
    /// same value. I had verified exactly this rule on the LU relation vocabulary (#411) and the LU
    /// assertion vocabulary (#412) days earlier and still shipped a contract that broke it.
    /// </remarks>
    [TestMethod]
    public void AnUnqueriedSourceIsDistinguishableFromAProvenAbsence()
    {
        var neverAsked = new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.Transposable,
            Unacquired(EuTranspositionAssertedBy.Legilux),
            Unacquired(EuTranspositionAssertedBy.Nim), null);

        var asked = new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.Transposable,
            ProvenAbsent(EuTranspositionAssertedBy.Legilux),
            ProvenAbsent(EuTranspositionAssertedBy.Nim), null);

        Assert.IsNull(neverAsked.Legilux.Side);
        Assert.IsNull(asked.Legilux.Side);
        Assert.IsFalse(neverAsked.Legilux.ProvesAbsence, "an unqueried source proves nothing.");
        Assert.IsTrue(asked.Legilux.ProvesAbsence, "a completed acquisition with no side is a real negative.");
        Assert.AreNotEqual(neverAsked.Legilux.Acquisition, asked.Legilux.Acquisition);
    }

    // ---- FINDING 2: the join invariant is enforced, not merely documented. ----

    /// <summary>
    /// A two-source join needs two sources.
    /// </summary>
    /// <remarks>
    /// The join's own remarks always said it is present only when both publishers named a measure.
    /// The first version documented that and did not check it — the declared-but-unenforced shape I
    /// have twice reported in other people's code this session.
    /// </remarks>
    [TestMethod]
    public void ADerivedJoinNeedsBothPublishersToHaveNamedAMeasure()
    {
        var onlyLegilux = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.Transposable,
            Asserted(EuTranspositionAssertedBy.Legilux),
            ProvenAbsent(EuTranspositionAssertedBy.Nim),
            new EuNormalisedEliJoin(LuMeasure, Ev("05"))));
        Assert.AreEqual("normalisedEliJoin", onlyLegilux.ParamName);
        StringAssert.Contains(onlyLegilux.Message, "one side");

        var neither = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.Transposable,
            ProvenAbsent(EuTranspositionAssertedBy.Legilux),
            ProvenAbsent(EuTranspositionAssertedBy.Nim),
            new EuNormalisedEliJoin(LuMeasure, Ev("06"))));
        Assert.AreEqual("normalisedEliJoin", neither.ParamName);
        StringAssert.Contains(neither.Message, "neither side");
    }

    /// <summary>
    /// A not-transposable work carries no join, and is refused for the reason that is actually
    /// true of it.
    /// </summary>
    /// <remarks>
    /// I first wrote a separate arm refusing a join on a not-transposable work, and a test for it.
    /// Disabling that arm left the whole suite green: a not-transposable work is already forbidden
    /// a side, so it arrives here with both sides null and the two-source rule refuses it first.
    /// The arm was unreachable and the test passed for the wrong reason. Both are corrected — the
    /// arm is gone, and this asserts the refusal that can actually happen.
    /// </remarks>
    [TestMethod]
    public void ANotTransposableWorkCarriesNoNormalisedEliJoin()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionBridge(
            Gdpr, EuWorkKind.Regulation, EuTransposability.NotTransposable,
            ProvenAbsent(EuTranspositionAssertedBy.Legilux),
            ProvenAbsent(EuTranspositionAssertedBy.Nim),
            new EuNormalisedEliJoin(LuMeasure, Ev("07"))));
        Assert.AreEqual("normalisedEliJoin", error.ParamName);
        StringAssert.Contains(error.Message, "neither side",
            "the refusal must name the true reason: a regulation has no sides to join.");
    }
}
