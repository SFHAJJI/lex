using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The never-consolidated frame: an absence claim about a whole population, and the ways a count
/// could be published that nobody actually established.
/// </summary>
/// <remarks>
/// E10 says to treat the 23,370 measurement as audit context and never as a literal acceptance value.
/// The tests that matter here are the ones that fail if a number can be produced from a partial
/// sweep, or if "never consolidated" can be claimed for an act nobody enumerated.
/// </remarks>
[TestClass]
public sealed class LuxembourgNeverConsolidatedFrameTests
{
    private const string Loi = "http://data.legilux.public.lu/resource/authority/legal-type/LOI";
    private const string Rgd = "http://data.legilux.public.lu/resource/authority/legal-type/RGD";

    /// <summary>Something Luxembourg has and this build has no member for.</summary>
    private const string Unknown =
        "http://data.legilux.public.lu/resource/authority/legal-type/ARRETE-MINISTERIEL";

    private const string ActOne = "https://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
    private const string ActTwo = "https://data.legilux.public.lu/eli/etat/leg/rgd/2026/02/02/a2";
    private const string ActThree = "https://data.legilux.public.lu/eli/etat/leg/loi/2026/03/03/a3";

    // ---- The counting rule. ----

    /// <summary>
    /// One unenumerated act refuses the whole count, however many others were confirmed.
    /// </summary>
    /// <remarks>
    /// The property E10's "never a literal acceptance value" demands, made structural. A count over a
    /// partial sweep is indistinguishable, once written down, from a count over a complete one.
    /// </remarks>
    [TestMethod]
    public void OneUnenumeratedActRefusesTheCountHoweverManyOthersAreConfirmed()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActTwo, Rgd), out _));
        Assert.IsTrue(frame.TryAdmit(Unproven(ActThree, Loi), out _));

        Assert.IsFalse(frame.TryCountNeverConsolidated(out var count, out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedCountRefusal.EnumerationIncomplete, refusal);
        Assert.AreEqual(0, count, "a refused count reports nothing, not a partial total.");
    }

    [TestMethod]
    public void ACompleteFrameCountsOnlyTheNeverConsolidated()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _);
        frame.TryAdmit(Consolidated(ActTwo, Rgd), out _);
        frame.TryAdmit(NeverConsolidated(ActThree, Loi), out _);

        Assert.IsTrue(frame.TryCountNeverConsolidated(out var count, out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedCountRefusal.None, refusal);
        Assert.AreEqual(2, count);
    }

    /// <summary>An act excluded by rule is counted as neither, and does not block the count.</summary>
    [TestMethod]
    public void AnActOutsideTheClassManifestBlocksNothingAndCountsAsNothing()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _);
        frame.TryAdmit(OutsideManifest(ActTwo, Unknown), out _);

        Assert.IsTrue(frame.TryCountNeverConsolidated(out var count, out _));
        Assert.AreEqual(1, count, "a deliberate exclusion is not a gap and is not a member.");
    }

    [TestMethod]
    public void AnEmptyFrameReportsNoPopulationRatherThanZero()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();

        Assert.IsFalse(frame.TryCountNeverConsolidated(out var count, out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedCountRefusal.FrameEmpty, refusal);
        Assert.AreEqual(0, count, "zero acts never consolidated and no acts examined differ.");
    }

    // ---- Absence needs evidence; non-absence must not carry it. ----

    [TestMethod]
    public void AnEnumeratedDispositionWithoutCompletionEvidenceIsRejected()
    {
        foreach (var disposition in new[]
        {
            LuxembourgNeverConsolidatedDisposition.EnumeratedAndNeverConsolidated,
            LuxembourgNeverConsolidatedDisposition.EnumeratedAndConsolidated,
        })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new LuxembourgNeverConsolidatedEntry(
                    ActOne, new LuxembourgActClassRef(Loi), disposition, null),
                $"{disposition} claims an enumeration completed and must name the evidence.");
        }
    }

    [TestMethod]
    public void AnUnenumeratedDispositionCarryingCompletionEvidenceIsRejected()
    {
        foreach (var disposition in new[]
        {
            LuxembourgNeverConsolidatedDisposition.EnumerationUnproven,
            LuxembourgNeverConsolidatedDisposition.OutsideClassManifest,
        })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new LuxembourgNeverConsolidatedEntry(
                    ActOne, new LuxembourgActClassRef(Loi), disposition, Evidence()),
                $"{disposition} is not an enumeration outcome and must not look like one.");
        }
    }

    // ---- The publisher owns the class vocabulary. ----

    /// <summary>
    /// A class this build has no member for survives intact. E9's language axis nearly closed at 24
    /// while the publisher emitted 94; an act class is the same kind of thing.
    /// </summary>
    [TestMethod]
    public void AnActClassThisBuildDoesNotRecogniseIsCarriedVerbatim()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Unknown), out _));

        Assert.AreEqual(Unknown, frame.Entries[0].ActClass.PublisherClassIri);
        Assert.IsTrue(frame.TryCountNeverConsolidated(out var count, out _));
        Assert.AreEqual(1, count, "an unrecognised class is still an act, not a refusal.");
    }

    [TestMethod]
    public void ActClassIsNotNormalisedOrCaseFolded()
    {
        var shouted = Loi.ToUpperInvariant();
        Assert.AreNotEqual(Loi, shouted);
        Assert.AreEqual(shouted, new LuxembourgActClassRef(shouted).PublisherClassIri);
    }

    // ---- One act, one answer. ----

    [TestMethod]
    public void ReadmittingOneActWithTheSameDispositionIsIdempotent()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out var disagreed));
        Assert.IsFalse(disagreed);
        Assert.HasCount(1, frame.Entries);
    }

    [TestMethod]
    public void TwoDifferentAnswersForOneActRefuseRatherThanOverwrite()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        var held = NeverConsolidated(ActOne, Loi);
        Assert.IsTrue(frame.TryAdmit(held, out _));

        Assert.IsFalse(frame.TryAdmit(Consolidated(ActOne, Loi), out var disagreed));
        Assert.IsTrue(disagreed);
        Assert.HasCount(1, frame.Entries);
        Assert.AreSame(held, frame.Entries[0], "the frame never replaces an admitted answer.");
    }

    /// <summary>
    /// An act re-presented as unproven after being enumerated is a disagreement, not a downgrade.
    /// </summary>
    [TestMethod]
    public void AnEnumeratedActCannotBeDowngradedToUnproven()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _);

        Assert.IsFalse(frame.TryAdmit(Unproven(ActOne, Loi), out var disagreed));
        Assert.IsTrue(disagreed);
        Assert.IsTrue(frame.TryCountNeverConsolidated(out var count, out _),
            "and the refused downgrade must not have poisoned the count.");
        Assert.AreEqual(1, count);
    }

    /// <summary>
    /// Two act IRIs differing only in case are two acts. The frame keys on the IRI, and IRI paths
    /// are case-sensitive.
    /// </summary>
    /// <remarks>
    /// Found by a mechanical sweep, not by me: flipping the frame's comparer to ignore-case survived
    /// every other test here. Under that flip two distinct acts would collide, the second would be
    /// read as a disagreement with the first, and the population would silently lose one.
    /// </remarks>
    [TestMethod]
    public void TwoActIrisDifferingOnlyByCaseAreTwoActs()
    {
        var shouted = ActOne.Replace("/a1", "/A1", StringComparison.Ordinal);
        Assert.AreNotEqual(ActOne, shouted);

        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));
        Assert.IsTrue(
            frame.TryAdmit(NeverConsolidated(shouted, Loi), out var disagreed),
            "a different IRI is a different act, not a second answer about the first.");
        Assert.IsFalse(disagreed);

        Assert.HasCount(2, frame.Entries);
        Assert.IsTrue(frame.TryCountNeverConsolidated(out var count, out _));
        Assert.AreEqual(2, count);
    }

    /// <summary>An act IRI or class IRI that is blank is refused rather than carried.</summary>
    /// <remarks>
    /// The sweep could not mutate these guards - its regex did not match them - so their absence was
    /// invisible to it. Reported as a no-op rather than a survivor, which is what sent me looking.
    /// </remarks>
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void BlankIdentifiersAreRefused(string blank)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgActClassRef(blank));
        Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgNeverConsolidatedEntry(
                blank,
                new LuxembourgActClassRef(Loi),
                LuxembourgNeverConsolidatedDisposition.EnumerationUnproven,
                null));
    }

    // ---- Surface. ----

    [TestMethod]
    public void TheExposedCollectionCannotBeMutatedByACaller()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _);

        Assert.IsNotInstanceOfType<List<LuxembourgNeverConsolidatedEntry>>(frame.Entries);
        if (frame.Entries is ICollection<LuxembourgNeverConsolidatedEntry> mutable)
        {
            Assert.ThrowsExactly<NotSupportedException>(() => mutable.Clear());
        }
    }

    [TestMethod]
    public void EveryNullArgumentIsACallerContractViolation()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LuxembourgNeverConsolidatedEntry(
                ActOne,
                null!,
                LuxembourgNeverConsolidatedDisposition.EnumerationUnproven,
                null));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LuxembourgNeverConsolidatedFrame().TryAdmit(null!, out _));
    }

    [TestMethod]
    public void AnUndefinedDispositionIsRejected() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new LuxembourgNeverConsolidatedEntry(
                ActOne,
                new LuxembourgActClassRef(Loi),
                (LuxembourgNeverConsolidatedDisposition)47,
                null));

    [TestMethod]
    public void EveryDispositionHasItsExactWireToken() =>
        CollectionAssert.AreEqual(
            new[]
            {
                "\"enumerated_and_never_consolidated\"",
                "\"enumerated_and_consolidated\"",
                "\"enumeration_unproven\"",
                "\"outside_class_manifest\"",
            },
            Enum.GetValues<LuxembourgNeverConsolidatedDisposition>()
                .Select(member => ContractJson.Serialize(member))
                .ToArray());

    [TestMethod]
    public void EveryCountRefusalHasItsExactWireToken() =>
        CollectionAssert.AreEqual(
            new[] { "\"none\"", "\"enumeration_incomplete\"", "\"frame_empty\"" },
            Enum.GetValues<LuxembourgNeverConsolidatedCountRefusal>()
                .Select(member => ContractJson.Serialize(member))
                .ToArray());

    // ---- Fixtures. ----

    private static LuxembourgNeverConsolidatedEntry NeverConsolidated(string act, string actClass) =>
        new(act,
            new LuxembourgActClassRef(actClass),
            LuxembourgNeverConsolidatedDisposition.EnumeratedAndNeverConsolidated,
            Evidence());

    private static LuxembourgNeverConsolidatedEntry Consolidated(string act, string actClass) =>
        new(act,
            new LuxembourgActClassRef(actClass),
            LuxembourgNeverConsolidatedDisposition.EnumeratedAndConsolidated,
            Evidence());

    private static LuxembourgNeverConsolidatedEntry Unproven(string act, string actClass) =>
        new(act,
            new LuxembourgActClassRef(actClass),
            LuxembourgNeverConsolidatedDisposition.EnumerationUnproven,
            null);

    private static LuxembourgNeverConsolidatedEntry OutsideManifest(string act, string actClass) =>
        new(act,
            new LuxembourgActClassRef(actClass),
            LuxembourgNeverConsolidatedDisposition.OutsideClassManifest,
            null);

    private static SourceArtifactRef Evidence() => new(
        "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", new string('a', 64));
}
