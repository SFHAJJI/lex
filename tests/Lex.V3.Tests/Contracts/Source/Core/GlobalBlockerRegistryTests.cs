using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

/// <summary>
/// D1-01 Candidate 5 R3 lines 355 to 370: <c>cut_global_blocker_registry/1</c>. Every expectation
/// here is a literal beside the assertion, not derived from the code under test, so a wrong
/// registry cannot agree with itself.
/// </summary>
[TestClass]
public sealed class GlobalBlockerRegistryTests
{
    /// <summary>
    /// The exact twelve wire tokens R3 lines 357 to 368 name, in the exact order the text lists
    /// them. This is the direct citation check: if the accepted text's spelling of any family name
    /// ever drifts from this literal list, this test is where that shows up.
    /// </summary>
    [TestMethod]
    public void TheTwelveFamiliesAreExactAndOrderedAsTheAcceptedText()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                GlobalBlockerFamily.ManifestSelectorConflict,
                GlobalBlockerFamily.ManifestBoundaryDrift,
                GlobalBlockerFamily.RootDefinitionConflict,
                GlobalBlockerFamily.DuplicateClosure,
                GlobalBlockerFamily.MissingClosure,
                GlobalBlockerFamily.ClosureReconciliationConflict,
                GlobalBlockerFamily.WitnessReconciliationConflict,
                GlobalBlockerFamily.PagingPartitionOrTruncationConflict,
                GlobalBlockerFamily.RobotsPolicyConflict,
                GlobalBlockerFamily.PositiveFeedReconciliationConflict,
                GlobalBlockerFamily.ImplementationError,
                GlobalBlockerFamily.UnclassifiedGlobalBlocker,
            },
            GlobalBlockerRegistry.Families.ToArray());

        Assert.AreEqual(12, GlobalBlockerRegistry.Families.Count, "R3 line 355 names exactly twelve families.");

        CollectionAssert.AreEqual(
            new[]
            {
                "\"manifest_selector_conflict\"",
                "\"manifest_boundary_drift\"",
                "\"root_definition_conflict\"",
                "\"duplicate_closure\"",
                "\"missing_closure\"",
                "\"closure_reconciliation_conflict\"",
                "\"witness_reconciliation_conflict\"",
                "\"paging_partition_or_truncation_conflict\"",
                "\"robots_policy_conflict\"",
                "\"positive_feed_reconciliation_conflict\"",
                "\"implementation_error\"",
                "\"unclassified_global_blocker\"",
            },
            GlobalBlockerRegistry.Families.Select(ContractJson.Serialize).ToArray());
    }

    [TestMethod]
    public void WireKeyIsTheExactInverseOfClassifyForEveryKnownKey()
    {
        foreach (var family in GlobalBlockerRegistry.Families)
        {
            if (family == GlobalBlockerFamily.UnclassifiedGlobalBlocker)
            {
                continue;
            }

            Assert.AreEqual(family, GlobalBlockerRegistry.Classify(GlobalBlockerRegistry.WireKey(family)));
        }
    }

    [TestMethod]
    public void ClassifyReturnsUnclassifiedForEverythingOutsideTheElevenCanonicalKeys()
    {
        Assert.AreEqual(
            GlobalBlockerFamily.UnclassifiedGlobalBlocker, GlobalBlockerRegistry.Classify(null));
        Assert.AreEqual(
            GlobalBlockerFamily.UnclassifiedGlobalBlocker, GlobalBlockerRegistry.Classify(string.Empty));
        Assert.AreEqual(
            GlobalBlockerFamily.UnclassifiedGlobalBlocker,
            GlobalBlockerRegistry.Classify("totally_unrecognized_variant"));
        // A case variant of a real key is not the real key: classification is exact-match, not
        // fuzzy, so "MANIFEST_SELECTOR_CONFLICT" is not manifest_selector_conflict.
        Assert.AreEqual(
            GlobalBlockerFamily.UnclassifiedGlobalBlocker,
            GlobalBlockerRegistry.Classify("MANIFEST_SELECTOR_CONFLICT"));
        // The literal sentinel string is not a variant a package declares itself as; reporting it
        // verbatim still lands in the same bucket, never a distinct thirteenth family.
        Assert.AreEqual(
            GlobalBlockerFamily.UnclassifiedGlobalBlocker,
            GlobalBlockerRegistry.Classify("unclassified_global_blocker"));
        // Classify is total: it never throws, even on pathological input RequireMemberKey would
        // have rejected had it been routed through GlobalBlockerOccurrence first.
        Assert.AreEqual(
            GlobalBlockerFamily.UnclassifiedGlobalBlocker,
            GlobalBlockerRegistry.Classify(new string('x', 10_000)));
    }

    [TestMethod]
    public void WireKeyRejectsAnUndefinedFamily()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GlobalBlockerRegistry.WireKey((GlobalBlockerFamily)999));
    }

    /// <summary>
    /// Computed once and written down rather than derived, matching this repository's own rule for
    /// pinned content-derived identities (see <c>ContentDerivedIdentityTests</c>): deriving the
    /// expectation the way the code derives it would make this agree with itself under any change
    /// to the derivation.
    /// </summary>
    [TestMethod]
    public void TheRegistryIdentityIsThePinnedTranscribedValue()
    {
        Assert.AreEqual(
            "urn:uuid:0c09528e-8230-8090-abd6-f7a600038b7c", GlobalBlockerRegistry.RegistryRef.ResourceId);
        Assert.AreEqual(
            "fd9fdc5ac34098889af100930333c337fbe259b11709a2ce10fd5e80181403a8",
            GlobalBlockerRegistry.RegistryRef.Sha256);
    }

    [TestMethod]
    public void AnOccurrenceRejectsAnInvalidRawFamilyKeyOrSubtypeKey()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new GlobalBlockerOccurrence("", "subtype"));
        Assert.ThrowsExactly<ArgumentNullException>(() => new GlobalBlockerOccurrence(null!, "subtype"));
        Assert.ThrowsExactly<ArgumentException>(() => new GlobalBlockerOccurrence("family", ""));
        Assert.ThrowsExactly<ArgumentNullException>(() => new GlobalBlockerOccurrence("family", null!));
    }

    [TestMethod]
    public void RecomputeRejectsANullListOrANullOccurrence()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => GlobalBlockerCountVector.Recompute(null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => GlobalBlockerCountVector.Recompute([null!]));
    }

    [TestMethod]
    public void AnEmptyLedgerRecomputesEveryFamilyToZeroAndNoneIsMissing()
    {
        var vector = GlobalBlockerCountVector.Recompute([]);

        Assert.IsTrue(vector.AllZero);
        foreach (var family in GlobalBlockerRegistry.Families)
        {
            Assert.AreEqual(0, vector.Total(family), family.ToString());
            Assert.AreEqual(0, vector.SubtypeCounts(family).Count, family.ToString());
        }
    }

    /// <summary>
    /// The nonzero positive control for every registered family: one occurrence classifying to
    /// exactly that family raises exactly that family's total to one and leaves the other eleven at
    /// zero. This is what proves the registry can actually classify something into each of the
    /// twelve buckets, not merely enumerate an empty vocabulary.
    /// </summary>
    [TestMethod]
    public void EveryFamilyHasAWorkingNonzeroPositiveControl()
    {
        var rawKeyForFamily = new (string RawFamilyKey, GlobalBlockerFamily Expected)[]
        {
            ("manifest_selector_conflict", GlobalBlockerFamily.ManifestSelectorConflict),
            ("manifest_boundary_drift", GlobalBlockerFamily.ManifestBoundaryDrift),
            ("root_definition_conflict", GlobalBlockerFamily.RootDefinitionConflict),
            ("duplicate_closure", GlobalBlockerFamily.DuplicateClosure),
            ("missing_closure", GlobalBlockerFamily.MissingClosure),
            ("closure_reconciliation_conflict", GlobalBlockerFamily.ClosureReconciliationConflict),
            ("witness_reconciliation_conflict", GlobalBlockerFamily.WitnessReconciliationConflict),
            ("paging_partition_or_truncation_conflict", GlobalBlockerFamily.PagingPartitionOrTruncationConflict),
            ("robots_policy_conflict", GlobalBlockerFamily.RobotsPolicyConflict),
            ("positive_feed_reconciliation_conflict", GlobalBlockerFamily.PositiveFeedReconciliationConflict),
            ("implementation_error", GlobalBlockerFamily.ImplementationError),
            ("a_key_no_package_declares", GlobalBlockerFamily.UnclassifiedGlobalBlocker),
        };
        Assert.AreEqual(12, rawKeyForFamily.Length, "one control per registered family");

        foreach (var (rawFamilyKey, expectedFamily) in rawKeyForFamily)
        {
            var vector = GlobalBlockerCountVector.Recompute(
                [new GlobalBlockerOccurrence(rawFamilyKey, "probe-subtype")]);

            Assert.AreEqual(1, vector.Total(expectedFamily), rawFamilyKey);
            var subtypes = vector.SubtypeCounts(expectedFamily);
            Assert.AreEqual(1, subtypes.Count, rawFamilyKey);
            Assert.AreEqual(1, subtypes["probe-subtype"], rawFamilyKey);

            foreach (var otherFamily in GlobalBlockerRegistry.Families.Where(f => f != expectedFamily))
            {
                Assert.AreEqual(0, vector.Total(otherFamily), $"{rawFamilyKey} leaked into {otherFamily}");
            }
        }
    }

    [TestMethod]
    public void SubtypeCountsAccumulateSeparatelyWithinOneFamily()
    {
        var vector = GlobalBlockerCountVector.Recompute(
        [
            new GlobalBlockerOccurrence("manifest_boundary_drift", "unknown-language-authority"),
            new GlobalBlockerOccurrence("manifest_boundary_drift", "unknown-language-authority"),
            new GlobalBlockerOccurrence("manifest_boundary_drift", "unexecutable-root-definition"),
        ]);

        Assert.AreEqual(3, vector.Total(GlobalBlockerFamily.ManifestBoundaryDrift));
        var subtypes = vector.SubtypeCounts(GlobalBlockerFamily.ManifestBoundaryDrift);
        Assert.AreEqual(2, subtypes.Count);
        Assert.AreEqual(2, subtypes["unknown-language-authority"]);
        Assert.AreEqual(1, subtypes["unexecutable-root-definition"]);
        Assert.IsFalse(vector.AllZero);
    }
}
