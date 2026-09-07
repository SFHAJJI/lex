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

    private static EuTranspositionSide Side(EuTranspositionAssertedBy by, string label = "01") =>
        new(by, LuMeasure, Ev(label));

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
            Side(EuTranspositionAssertedBy.Nim), null, null));
        Assert.AreEqual("legiluxSide", wrongInLegilux.ParamName);

        var wrongInNim = Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.Transposable,
            null, Side(EuTranspositionAssertedBy.Legilux), null));
        Assert.AreEqual("nimSide", wrongInNim.ParamName);
    }

    /// <summary>
    /// Both publishers' sides are carried side by side, each keeping its own asserted_by.
    /// </summary>
    [TestMethod]
    public void BothPublishersSidesAreCarriedWithoutMerging()
    {
        var bridge = new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.Transposable,
            Side(EuTranspositionAssertedBy.Legilux, "02"),
            Side(EuTranspositionAssertedBy.Nim, "03"),
            new EuNormalisedEliJoin(LuMeasure, Ev("04")));

        Assert.AreEqual(EuTranspositionAssertedBy.Legilux, bridge.LegiluxSide!.AssertedBy);
        Assert.AreEqual(EuTranspositionAssertedBy.Nim, bridge.NimSide!.AssertedBy);
        Assert.AreNotSame(bridge.LegiluxSide.EvidenceRef, bridge.NimSide.EvidenceRef);
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
            Gdpr, EuWorkKind.Regulation, EuTransposability.NotTransposable, null, null, null);

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
                Gdpr, EuWorkKind.Regulation, EuTransposability.Transposable, null, null, null));
        Assert.AreEqual("transposability", claimedTransposable.ParamName);

        Assert.ThrowsExactly<ArgumentException>(() => new EuTranspositionBridge(
            Directive, EuWorkKind.Directive, EuTransposability.NotTransposable, null, null, null));
    }

    /// <summary>
    /// A not-transposable work carries no side, rather than storing evidence that contradicts it.
    /// </summary>
    [TestMethod]
    public void ANotTransposableWorkCarriesNoTranspositionSide()
    {
        foreach (var (legilux, nim) in new (EuTranspositionSide?, EuTranspositionSide?)[]
                 {
                     (Side(EuTranspositionAssertedBy.Legilux, "05"), null),
                     (null, Side(EuTranspositionAssertedBy.Nim, "06")),
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
}
