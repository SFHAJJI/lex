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
        by == EuTranspositionAssertedBy.Nim
            ? new(by, LuMeasure, Ev(label),
                EuMemberStateDisclaimer.Text, EuMemberStateDisclaimer.SourceUri)
            : new(by, LuMeasure, Ev(label), null, null);

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
    /// anyone's assertion. <c>IsDerived</c> is a computed method rather than supplied, for the reason
    /// <c>EuLegislationSummary.Licence</c> is: a caller who could set it could set it wrong.
    /// </remarks>
    [TestMethod]
    public void TheNormalisedEliJoinIsAlwaysDerivedAndNamesNoPublisher()
    {
        var join = new EuNormalisedEliJoin(LuMeasure, Ev("07"));

        Assert.IsTrue(join.IsDerived());
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
        Assert.IsFalse(neverAsked.Legilux.ProvesAbsence(), "an unqueried source proves nothing.");
        Assert.IsTrue(asked.Legilux.ProvesAbsence(), "a completed acquisition with no side is a real negative.");
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

    // ---- FINDING 1: the Member-State disclaimer, which the Done When clause requires. ----

    /// <summary>
    /// A NIM row carries the Member-State disclaimer verbatim, with its pinned source.
    /// </summary>
    /// <remarks>
    /// THE REVIEWER'S FINDING, and the reason I missed it is worth recording: I read this issue's
    /// body truncated and built against the Authority clause without ever reading the Done When
    /// clause, which names the disclaimer explicitly. The V3 spec's E5 line requires it verbatim
    /// with an archived source.
    /// <para>
    /// It is not decoration. NIM records are what a Member State notified, and the Commission
    /// states plainly that it does not vouch for them. A NIM row without the disclaimer presents a
    /// Member State's notification with the Union publisher's apparent authority behind it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ANimRowCarriesTheMemberStateDisclaimerVerbatim()
    {
        var nim = Side(EuTranspositionAssertedBy.Nim);
        Assert.AreEqual(
            "The member states bear sole responsibility for all information",
            nim.MemberStateDisclaimer);
        Assert.AreEqual(EuMemberStateDisclaimer.SourceUri, nim.MemberStateDisclaimerSourceUri);

        var omitted = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionSide(
            EuTranspositionAssertedBy.Nim, LuMeasure, Ev("10"), null, null));
        Assert.AreEqual("memberStateDisclaimer", omitted.ParamName);

        // Omission and substitution are different failures and say so. Asserting only the parameter
        // name could not tell them apart: with the omission arm removed, a null disclaimer still
        // fails the verbatim check and the test stayed green. The message is what binds this arm.
        StringAssert.Contains(omitted.Message, "vouched for it",
            "an omitted disclaimer must be refused for being absent, not as a paraphrase.");
    }

    /// <summary>A paraphrased disclaimer is refused as firmly as an omitted one.</summary>
    [TestMethod]
    public void AParaphrasedOrMissourcedDisclaimerIsRefused()
    {
        foreach (var (text, source, why) in new (string, string, string)[]
                 {
                     ("The Member States bear sole responsibility for all information",
                         EuMemberStateDisclaimer.SourceUri, "capitalisation differs"),
                     ("Member states are solely responsible for all information",
                         EuMemberStateDisclaimer.SourceUri, "a paraphrase"),
                     (EuMemberStateDisclaimer.Text,
                         "https://eur-lex.europa.eu/collection/n-law/mne.html", "an unarchived source"),
                 })
        {
            var error = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionSide(
                EuTranspositionAssertedBy.Nim, LuMeasure, Ev("11"), text, source));
            Assert.AreEqual("memberStateDisclaimer", error.ParamName, why);
            StringAssert.Contains(error.Message, "verbatim", why);
        }
    }

    /// <summary>The disclaimer does not travel on Legilux's own assertion.</summary>
    /// <remarks>
    /// The disclaimer states who is answerable for a NIM notification. Attaching it to Legilux's
    /// row would put the Commission's caveat on Luxembourg's own publication, which is a different
    /// claim about a different publisher.
    /// </remarks>
    [TestMethod]
    public void TheDisclaimerDoesNotTravelOnALegiluxRow()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionSide(
            EuTranspositionAssertedBy.Legilux, LuMeasure, Ev("12"),
            EuMemberStateDisclaimer.Text, EuMemberStateDisclaimer.SourceUri));
        Assert.AreEqual("memberStateDisclaimer", error.ParamName);
    }

    // ---- FINDING 2: never asked, yet holding an answer. ----

    /// <summary>An unacquired source cannot carry an observed assertion.</summary>
    /// <remarks>
    /// I reused <c>EuRelationAcquisitionState</c> and did not carry its invariant with it.
    /// Unacquired means never asked for; a side beside that state is an observed assertion sitting
    /// next to a claim that no observation happened. <c>EuCellarRelationFamilyObservation</c>
    /// refuses edges in the same state for the same reason, which is what makes this an invariant
    /// of the vocabulary rather than a rule I am inventing here.
    /// </remarks>
    [TestMethod]
    public void AnUnacquiredSourceCannotCarryAnObservedAssertion()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionSourceAcquisition(
            EuTranspositionAssertedBy.Nim,
            EuRelationAcquisitionState.Unacquired,
            Side(EuTranspositionAssertedBy.Nim, "13"),
            null));
        Assert.AreEqual("side", error.ParamName);
    }
}
