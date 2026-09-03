using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

/// <summary>
/// D1-01 Candidate 5 R3 lines 398 to 403: <c>cut_release_gate/1</c>. Every expectation here is a
/// literal beside the assertion, not derived from the code under test.
/// </summary>
[TestClass]
public sealed class CutReleaseGateTests
{
    private static readonly SourceArtifactRef WrongRegistryRef =
        new("urn:uuid:00000000-0000-4000-8000-000000000001", new string('7', 64));

    private static CutCompletionClaim Complete(string id = "completion-1") => new(id, isComplete: true);

    private static CutCompletionClaim Incomplete(string id = "completion-1") => new(id, isComplete: false);

    /// <summary>All twelve families at zero, matching an empty ledger. The uncontested eligible shape.</summary>
    private static List<GlobalBlockerFamilyCountEntry> AllZeroVector() =>
        GlobalBlockerRegistry.Families
            .Select(family => new GlobalBlockerFamilyCountEntry(family, 0, new Dictionary<string, int>()))
            .ToList();

    private static GlobalBlockerCountVector EmptyRecompute() => GlobalBlockerCountVector.Recompute([]);

    [TestMethod]
    public void WireTokensForTheClosedEnumsAreExactlyTheAcceptedTextsSpelling()
    {
        Assert.AreEqual(
            "\"enumeration_evidence_only\"", ContractJson.Serialize(ReleaseClass.EnumerationEvidenceOnly));
        Assert.AreEqual(
            "\"acquisition_or_product\"", ContractJson.Serialize(ReleaseClass.AcquisitionOrProduct));
        Assert.AreEqual(
            "\"cut_release_eligible\"", ContractJson.Serialize(CutReleaseVerdict.CutReleaseEligible));
        Assert.AreEqual(
            "\"cut_release_blocked\"", ContractJson.Serialize(CutReleaseVerdict.CutReleaseBlocked));
    }

    // ---- Positive controls -------------------------------------------------------------------

    [TestMethod]
    public void EnumerationEvidenceOnlyIsEligibleWhenEverythingBinds()
    {
        var gate = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, AllZeroVector(), EmptyRecompute());

        Assert.AreEqual(CutReleaseVerdict.CutReleaseEligible, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.None, gate.Reason);
        Assert.AreEqual(ReleaseClass.EnumerationEvidenceOnly, gate.ReleaseClass);
        Assert.IsNull(gate.AcquisitionCompletion, "enumeration_evidence_only never carries an acquisition claim");
    }

    [TestMethod]
    public void AcquisitionOrProductIsEligibleWhenBothCompletionsAndTheVectorBind()
    {
        var gate = CutReleaseGate.EvaluateAcquisitionOrProduct(
            "cut-1", Complete("enum"), Complete("acq"),
            GlobalBlockerRegistry.RegistryRef, AllZeroVector(), EmptyRecompute());

        Assert.AreEqual(CutReleaseVerdict.CutReleaseEligible, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.None, gate.Reason);
        Assert.AreEqual(ReleaseClass.AcquisitionOrProduct, gate.ReleaseClass);
    }

    // ---- Completion state: false or missing, both release classes -------------------------------

    [TestMethod]
    public void MissingEnumerationCompletionBlocksBothReleaseClasses()
    {
        var enumerationOnly = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", null, GlobalBlockerRegistry.RegistryRef, AllZeroVector(), EmptyRecompute());
        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, enumerationOnly.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.EnumerationCompletionFalseOrMissing, enumerationOnly.Reason);

        var product = CutReleaseGate.EvaluateAcquisitionOrProduct(
            "cut-1", null, Complete(), GlobalBlockerRegistry.RegistryRef, AllZeroVector(), EmptyRecompute());
        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, product.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.EnumerationCompletionFalseOrMissing, product.Reason);
    }

    [TestMethod]
    public void AFalseEnumerationCompletionBlocksBothReleaseClasses()
    {
        var enumerationOnly = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Incomplete(), GlobalBlockerRegistry.RegistryRef, AllZeroVector(), EmptyRecompute());
        Assert.AreEqual(CutReleaseBlockReason.EnumerationCompletionFalseOrMissing, enumerationOnly.Reason);

        var product = CutReleaseGate.EvaluateAcquisitionOrProduct(
            "cut-1", Incomplete(), Complete(), GlobalBlockerRegistry.RegistryRef, AllZeroVector(), EmptyRecompute());
        Assert.AreEqual(CutReleaseBlockReason.EnumerationCompletionFalseOrMissing, product.Reason);
    }

    [TestMethod]
    public void MissingAcquisitionCompletionBlocksOnlyAcquisitionOrProduct()
    {
        var gate = CutReleaseGate.EvaluateAcquisitionOrProduct(
            "cut-1", Complete("enum"), null, GlobalBlockerRegistry.RegistryRef, AllZeroVector(), EmptyRecompute());

        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.AcquisitionCompletionFalseOrMissing, gate.Reason);
    }

    [TestMethod]
    public void AFalseAcquisitionCompletionBlocksAcquisitionOrProduct()
    {
        var gate = CutReleaseGate.EvaluateAcquisitionOrProduct(
            "cut-1", Complete("enum"), Incomplete("acq"),
            GlobalBlockerRegistry.RegistryRef, AllZeroVector(), EmptyRecompute());

        Assert.AreEqual(CutReleaseBlockReason.AcquisitionCompletionFalseOrMissing, gate.Reason);
    }

    // ---- Registry identity -----------------------------------------------------------------------

    [TestMethod]
    public void AWrongRegistryReferenceBlocksWithADigestMismatch()
    {
        var gate = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), WrongRegistryRef, AllZeroVector(), EmptyRecompute());

        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.RegistryDigestMismatch, gate.Reason);
    }

    // ---- Vector shape: missing and duplicate family members --------------------------------------

    [TestMethod]
    public void AVectorMissingAFamilyIsBlocked()
    {
        var vector = AllZeroVector();
        vector.RemoveAll(entry => entry.Family == GlobalBlockerFamily.RobotsPolicyConflict);
        Assert.AreEqual(11, vector.Count);

        var gate = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, vector, EmptyRecompute());

        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.MissingFamily, gate.Reason);
    }

    [TestMethod]
    public void AVectorNamingOneFamilyTwiceIsBlockedAsDuplicateEvenIfAnotherIsAlsoMissing()
    {
        var vector = AllZeroVector();
        // Duplicate ManifestSelectorConflict; RobotsPolicyConflict never appears at all. Thirteen
        // entries, twelve distinct slots represented once, one represented twice: the duplicate
        // check must win here, proving it runs before -- and does not get short-circuited by --
        // the missing-family check.
        vector.Add(new GlobalBlockerFamilyCountEntry(
            GlobalBlockerFamily.ManifestSelectorConflict, 0, new Dictionary<string, int>()));
        vector.RemoveAll(entry => entry.Family == GlobalBlockerFamily.RobotsPolicyConflict);

        var gate = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, vector, EmptyRecompute());

        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.DuplicateFamily, gate.Reason);
    }

    // ---- Evaluation error: a supplied entry does not cohere with itself --------------------------

    [TestMethod]
    public void AnEntryWhoseTotalDisagreesWithItsOwnSubtypeCountsIsAnEvaluationError()
    {
        var vector = AllZeroVector();
        var index = vector.FindIndex(entry => entry.Family == GlobalBlockerFamily.DuplicateClosure);
        // Total says 5, but the only declared subtype carries a count of 1: internally incoherent,
        // regardless of what the recomputed ledger says.
        vector[index] = new GlobalBlockerFamilyCountEntry(
            GlobalBlockerFamily.DuplicateClosure, 5, new Dictionary<string, int> { ["some-subtype"] = 1 });

        var gate = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, vector, EmptyRecompute());

        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.EvaluationError, gate.Reason);
    }

    // ---- Count-ledger mismatch: supplied disagrees with the independent recomputation ------------

    [TestMethod]
    public void ASuppliedTotalThatDisagreesWithTheRecomputedLedgerIsACountLedgerMismatch()
    {
        // The supplied vector claims one duplicate_closure conflict; the independently recomputed
        // ledger (built from a real occurrence list) says there were none. Both are internally
        // coherent -- Total equals the sum of each one's own subtype counts -- so only the
        // cross-check between them can catch this.
        var vector = AllZeroVector();
        var index = vector.FindIndex(entry => entry.Family == GlobalBlockerFamily.DuplicateClosure);
        vector[index] = new GlobalBlockerFamilyCountEntry(
            GlobalBlockerFamily.DuplicateClosure, 1, new Dictionary<string, int> { ["ghost"] = 1 });

        var gate = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, vector, EmptyRecompute());

        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.CountLedgerMismatch, gate.Reason);
    }

    [TestMethod]
    public void ASuppliedSubtypeBreakdownThatDisagreesWithTheRecomputedLedgerIsACountLedgerMismatchEvenWhenTotalsAgree()
    {
        var recomputed = GlobalBlockerCountVector.Recompute(
        [
            new GlobalBlockerOccurrence("duplicate_closure", "real-subtype"),
        ]);

        var vector = AllZeroVector();
        var index = vector.FindIndex(entry => entry.Family == GlobalBlockerFamily.DuplicateClosure);
        // Same total (one), different subtype key: the ledger says "real-subtype", the receipt
        // claims "renamed-subtype". A total-only comparison would have missed this.
        vector[index] = new GlobalBlockerFamilyCountEntry(
            GlobalBlockerFamily.DuplicateClosure, 1, new Dictionary<string, int> { ["renamed-subtype"] = 1 });

        var gate = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, vector, recomputed);

        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.CountLedgerMismatch, gate.Reason);
    }

    // ---- Nonzero blocker count, including an unclassified variant reaching the gate --------------

    [TestMethod]
    public void ANonzeroCountThatMatchesTheLedgerIsStillBlockedAsNonzero()
    {
        var recomputed = GlobalBlockerCountVector.Recompute(
        [
            new GlobalBlockerOccurrence("robots_policy_conflict", "planned-url-not-covered"),
        ]);
        var vector = AllZeroVector();
        var index = vector.FindIndex(entry => entry.Family == GlobalBlockerFamily.RobotsPolicyConflict);
        vector[index] = new GlobalBlockerFamilyCountEntry(
            GlobalBlockerFamily.RobotsPolicyConflict,
            1,
            new Dictionary<string, int> { ["planned-url-not-covered"] = 1 });

        var gate = CutReleaseGate.EvaluateAcquisitionOrProduct(
            "cut-1", Complete("enum"), Complete("acq"),
            GlobalBlockerRegistry.RegistryRef, vector, recomputed);

        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.NonzeroBlockerCount, gate.Reason);
    }

    /// <summary>
    /// R3 line 370's "unclassified variant" is not a separate refusal mechanism; it is a family
    /// like any other, and a nonzero count in it blocks the gate exactly the way a nonzero count in
    /// any named family does. This proves that path end to end: a raw key no bound package would
    /// ever declare classifies to Unclassified, the ledger recomputation reflects it, and when the
    /// supplied receipt faithfully reports that same count the gate refuses to certify the cut.
    /// </summary>
    [TestMethod]
    public void AnUnclassifiedVariantThatReachesTheGateIsRefusedAsANonzeroCountNeverSilentlyPassed()
    {
        var recomputed = GlobalBlockerCountVector.Recompute(
        [
            new GlobalBlockerOccurrence("a_key_no_package_declares", "mystery"),
        ]);
        var vector = AllZeroVector();
        var index = vector.FindIndex(entry => entry.Family == GlobalBlockerFamily.UnclassifiedGlobalBlocker);
        vector[index] = new GlobalBlockerFamilyCountEntry(
            GlobalBlockerFamily.UnclassifiedGlobalBlocker, 1, new Dictionary<string, int> { ["mystery"] = 1 });

        var gate = CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, vector, recomputed);

        Assert.AreEqual(CutReleaseVerdict.CutReleaseBlocked, gate.Verdict);
        Assert.AreEqual(CutReleaseBlockReason.NonzeroBlockerCount, gate.Reason);
    }

    // ---- Precondition guards (not part of the closed reason vocabulary) --------------------------

    [TestMethod]
    public void EvaluateThrowsOnNullOrWhitespaceCutId()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "  ", Complete(), GlobalBlockerRegistry.RegistryRef, AllZeroVector(), EmptyRecompute()));
    }

    [TestMethod]
    public void EvaluateThrowsOnNullRequiredReferences()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), null!, AllZeroVector(), EmptyRecompute()));
        Assert.ThrowsExactly<ArgumentNullException>(() => CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, null!, EmptyRecompute()));
        Assert.ThrowsExactly<ArgumentNullException>(() => CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, AllZeroVector(), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => CutReleaseGate.EvaluateEnumerationEvidenceOnly(
            "cut-1", Complete(), GlobalBlockerRegistry.RegistryRef, [null!], EmptyRecompute()));
    }

    [TestMethod]
    public void AFamilyCountEntryRejectsNegativeTotalsAndNegativeSubtypeCounts()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GlobalBlockerFamilyCountEntry(
            GlobalBlockerFamily.ImplementationError, -1, new Dictionary<string, int>()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GlobalBlockerFamilyCountEntry(
            GlobalBlockerFamily.ImplementationError, 0, new Dictionary<string, int> { ["x"] = -1 }));
    }

    [TestMethod]
    public void ACompletionClaimRejectsAnEmptyIdentity()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CutCompletionClaim("", true));
        Assert.ThrowsExactly<ArgumentException>(() => new CutCompletionClaim("   ", true));
    }
}
