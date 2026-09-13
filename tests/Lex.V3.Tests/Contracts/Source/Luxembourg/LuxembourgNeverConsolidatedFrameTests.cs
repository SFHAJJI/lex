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
/// sweep, if "never consolidated" can be claimed for an act nobody enumerated, or if the number
/// cannot say which population it describes.
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

    // ---- The population is the manifest's, not the caller's. ----

    /// <summary>
    /// A nonempty caller-selected subset is not the population, however terminal its dispositions.
    /// </summary>
    /// <remarks>
    /// The defect this repair exists for. The frame used to count whatever it had been handed, so a
    /// single caller-minted never-consolidated entry made the population 1 while the rest of the
    /// LOI/RGD universe was never supplied. "Every entry in this list has a terminal disposition"
    /// and "the complete population was swept" are different claims, and only the second is what a
    /// published number means.
    /// </remarks>
    [TestMethod]
    public void ANonemptySubsetOfTheManifestIsNotThePopulation()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(
            Manifest((ActOne, Loi), (ActTwo, Rgd), (ActThree, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        Assert.IsFalse(frame.TryCountNeverConsolidated(out var count, out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedCountRefusal.ManifestMemberNotDispositioned,
            refusal,
            "two of three members were never dispositioned, and no entry in the frame can show that.");
        Assert.AreEqual(0, count);
    }

    /// <summary>The per-entry completion evidence does not close the subset gap on its own.</summary>
    /// <remarks>
    /// Every entry below carries a structurally valid completion reference, and the count is still
    /// refused: evidence that AN enumeration completed is not evidence that THIS scope was swept.
    /// </remarks>
    [TestMethod]
    public void CompletionEvidenceOnEveryAdmittedEntryStillDoesNotMakeASubsetThePopulation()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(
            Manifest((ActOne, Loi), (ActTwo, Rgd), (ActThree, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));
        Assert.IsTrue(frame.TryAdmit(Consolidated(ActTwo, Rgd), out _));

        Assert.IsFalse(frame.TryCountNeverConsolidated(out _, out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedCountRefusal.ManifestMemberNotDispositioned, refusal);
    }

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
        var frame = new LuxembourgNeverConsolidatedFrame(
            Manifest((ActOne, Loi), (ActTwo, Rgd), (ActThree, Loi)));
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
        var frame = new LuxembourgNeverConsolidatedFrame(
            Manifest((ActOne, Loi), (ActTwo, Rgd), (ActThree, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));
        Assert.IsTrue(frame.TryAdmit(Consolidated(ActTwo, Rgd), out _));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActThree, Loi), out _));

        Assert.IsTrue(frame.TryCountNeverConsolidated(out var count, out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedCountRefusal.None, refusal);
        Assert.AreEqual(2, count);
    }

    /// <summary>An act outside the manifest is counted as neither and does not block the count.</summary>
    [TestMethod]
    public void AnActOutsideTheClassManifestBlocksNothingAndCountsAsNothing()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));
        Assert.IsTrue(frame.TryAdmit(OutsideManifest(ActTwo, Unknown), out _));

        Assert.IsTrue(frame.TryCountNeverConsolidated(out var count, out _));
        Assert.AreEqual(1, count, "a deliberate exclusion is not a gap and is not a member.");
        Assert.HasCount(2, frame.Entries, "and it is still recorded as having been considered.");
    }

    [TestMethod]
    public void AFrameWithNoEntriesReportsNoPopulationRatherThanZero()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));

        Assert.IsFalse(frame.TryCountNeverConsolidated(out var count, out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedCountRefusal.ManifestMemberNotDispositioned, refusal);
        Assert.AreEqual(0, count, "zero acts never consolidated and no acts examined differ.");
    }

    // ---- Membership is derived, never chosen. ----

    /// <summary>
    /// A manifest member cannot be labelled outside the manifest and dropped from the count.
    /// </summary>
    /// <remarks>
    /// Before this, <c>OutsideClassManifest</c> was a freely chosen disposition independent of the
    /// act's own class, so a LOI could be declared outside and quietly excluded from the population
    /// its own manifest covers.
    /// </remarks>
    [TestMethod]
    public void AManifestMemberCannotBeDispositionedOutsideTheManifest()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));

        Assert.IsFalse(frame.TryAdmit(OutsideManifest(ActOne, Loi), out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedAdmitRefusal.MemberDispositionedOutside, refusal);
        Assert.IsEmpty(frame.Entries);
    }

    /// <summary>And an act the manifest does not hold cannot be counted into its population.</summary>
    /// <remarks>
    /// The other direction of the same defect: the suite this replaced deliberately counted an act
    /// of an unrecognised class as part of the never-consolidated population, which meant the number
    /// could not state which population it described.
    /// </remarks>
    [TestMethod]
    public void AnActTheManifestDoesNotHoldCannotBeDispositionedIntoIt()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));

        Assert.IsFalse(frame.TryAdmit(NeverConsolidated(ActTwo, Rgd), out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedAdmitRefusal.NonMemberDispositionedInside, refusal);
        Assert.IsEmpty(frame.Entries);
    }

    /// <summary>An entry whose class contradicts the manifest's own is refused, not accepted.</summary>
    [TestMethod]
    public void AnEntryWhoseClassContradictsTheManifestIsRefused()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));

        Assert.IsFalse(frame.TryAdmit(NeverConsolidated(ActOne, Rgd), out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedAdmitRefusal.ActClassContradictsManifest, refusal);
        Assert.IsEmpty(frame.Entries);
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

    // ---- The publisher owns the class vocabulary; this count owns its own membership. ----

    /// <summary>
    /// A class this build has no member for survives intact. E9's language axis nearly closed at 24
    /// while the publisher emitted 94; an act class is the same kind of thing.
    /// </summary>
    /// <remarks>
    /// The class vocabulary stays open and the manifest admits this one explicitly. Those are
    /// different things: carrying a publisher's word verbatim is not the same as letting a caller
    /// decide which acts a published number covers.
    /// </remarks>
    [TestMethod]
    public void AnActClassThisBuildDoesNotRecogniseIsCarriedVerbatim()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Unknown)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Unknown), out _));

        Assert.AreEqual(Unknown, frame.Entries[0].ActClass.PublisherClassIri);
        Assert.IsTrue(frame.TryCountNeverConsolidated(out var count, out _));
        Assert.AreEqual(1, count, "an unrecognised class is still an act, not a refusal.");
    }

    [TestMethod]
    public void ActClassIsNotNormalisedOrCaseFolded()
    {
        // The last path segment only: an uppercased scheme is not a URI this contract admits at all,
        // so shouting the whole string would test the URI guard rather than case preservation.
        var shouted = Loi.Replace("/LOI", "/Loi", StringComparison.Ordinal);
        Assert.AreNotEqual(Loi, shouted);
        Assert.AreEqual(shouted, new LuxembourgActClassRef(shouted).PublisherClassIri);
    }

    // ---- The documented fields are IRIs. ----

    /// <summary>
    /// A bounded printable identifier is not an IRI, and the public contract says these are IRIs.
    /// </summary>
    /// <remarks>
    /// Both fields used <c>ContractValidation.RequireIdentifier</c>, which admits any bounded
    /// printable ASCII - so "act-1" and "LOI" passed while the documentation promised the publisher's
    /// own IRI carried verbatim. Values the publisher cannot emit made that promise unfalsifiable.
    /// </remarks>
    [TestMethod]
    [DataRow("act-1")]
    [DataRow("LOI")]
    [DataRow("data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1")]
    [DataRow("ftp://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1")]
    [DataRow("http://data.legilux.public.lu/eli?x=1")]
    public void AnIdentifierThatIsNotAPublisherIriIsRefused(string notAnIri)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgActClassRef(notAnIri));
        Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgNeverConsolidatedEntry(
                notAnIri,
                new LuxembourgActClassRef(Loi),
                LuxembourgNeverConsolidatedDisposition.EnumerationUnproven,
                null));
        Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgActClassManifestMember(notAnIri, new LuxembourgActClassRef(Loi)));
    }

    // ---- One act, one answer, compared on every admitted field. ----

    [TestMethod]
    public void ReadmittingOneActWithTheSameEntryIsIdempotent()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.None, refusal);
        Assert.HasCount(1, frame.Entries);
    }

    [TestMethod]
    public void TwoDifferentAnswersForOneActRefuseRatherThanOverwrite()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));
        var held = NeverConsolidated(ActOne, Loi);
        Assert.IsTrue(frame.TryAdmit(held, out _));

        Assert.IsFalse(frame.TryAdmit(Consolidated(ActOne, Loi), out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.DispositionDisagrees, refusal);
        Assert.HasCount(1, frame.Entries);
        Assert.AreSame(held, frame.Entries[0], "the frame never replaces an admitted answer.");
    }

    /// <summary>
    /// Two runs citing different completion evidence for one act are two claims, not a replay.
    /// </summary>
    [TestMethod]
    public void ReadmittingOneActWithDifferentCompletionEvidenceIsADisagreement()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        var other = new LuxembourgNeverConsolidatedEntry(
            ActOne,
            new LuxembourgActClassRef(Loi),
            LuxembourgNeverConsolidatedDisposition.EnumeratedAndNeverConsolidated,
            OtherEvidence());

        Assert.IsFalse(frame.TryAdmit(other, out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedAdmitRefusal.CompletionEvidenceDisagrees, refusal);
        Assert.HasCount(1, frame.Entries);
    }

    /// <summary>
    /// The same act re-presented with a different class is a contradictory fact, not a replay.
    /// </summary>
    /// <remarks>
    /// Reachable only through a manifest that holds both classings, which is why it is built by
    /// hand here: the ordinary path refuses the contradiction against the manifest first, and this
    /// test is about the frame's OWN comparison of what it already holds. Before this, that
    /// comparison read the disposition alone, so LOI-then-RGD returned success with no disagreement
    /// and silently kept LOI - and since class decides membership, that was a material change
    /// reported as a no-op.
    /// </remarks>
    [TestMethod]
    public void ReadmittingOneActWithADifferentClassIsADisagreement()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        // The manifest's own check fires first, which is itself the point: a reclassed act cannot
        // reach the held-entry comparison without contradicting the manifest that scoped it.
        Assert.IsFalse(frame.TryAdmit(NeverConsolidated(ActOne, Rgd), out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedAdmitRefusal.ActClassContradictsManifest, refusal);
        Assert.AreEqual(
            Loi,
            frame.Entries[0].ActClass.PublisherClassIri,
            "and the held class is not quietly replaced.");
    }

    /// <summary>
    /// An act re-presented as unproven after being enumerated is a disagreement, not a downgrade.
    /// </summary>
    [TestMethod]
    public void AnEnumeratedActCannotBeDowngradedToUnproven()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        Assert.IsFalse(frame.TryAdmit(Unproven(ActOne, Loi), out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.DispositionDisagrees, refusal);
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

        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi), (shouted, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));
        Assert.IsTrue(
            frame.TryAdmit(NeverConsolidated(shouted, Loi), out var refusal),
            "a different IRI is a different act, not a second answer about the first.");
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.None, refusal);

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

    // ---- The manifest refuses to be built partial or self-contradictory. ----

    [TestMethod]
    public void AManifestWithNoAdmittedClassIsRefused()
    {
        Assert.IsNull(LuxembourgActClassManifest.TryCreate(
            [],
            [new LuxembourgActClassManifestMember(ActOne, new LuxembourgActClassRef(Loi))],
            Evidence(),
            out var refusal));
        Assert.AreEqual(LuxembourgActClassManifestRefusal.NoAdmittedClass, refusal);
    }

    [TestMethod]
    public void AManifestWithNoMemberIsRefused()
    {
        Assert.IsNull(LuxembourgActClassManifest.TryCreate(
            [new LuxembourgActClassRef(Loi)], [], Evidence(), out var refusal));
        Assert.AreEqual(LuxembourgActClassManifestRefusal.NoMember, refusal);
    }

    /// <summary>A member of a class the manifest does not admit is a contradiction, not a member.</summary>
    [TestMethod]
    public void AMemberWhoseClassIsNotAdmittedIsRefused()
    {
        Assert.IsNull(LuxembourgActClassManifest.TryCreate(
            [new LuxembourgActClassRef(Loi)],
            [new LuxembourgActClassManifestMember(ActTwo, new LuxembourgActClassRef(Rgd))],
            Evidence(),
            out var refusal));
        Assert.AreEqual(LuxembourgActClassManifestRefusal.MemberClassNotAdmitted, refusal);
    }

    [TestMethod]
    public void AnActAppearingTwiceInTheManifestIsRefused()
    {
        Assert.IsNull(LuxembourgActClassManifest.TryCreate(
            [new LuxembourgActClassRef(Loi)],
            [
                new LuxembourgActClassManifestMember(ActOne, new LuxembourgActClassRef(Loi)),
                new LuxembourgActClassManifestMember(ActOne, new LuxembourgActClassRef(Loi)),
            ],
            Evidence(),
            out var refusal));
        Assert.AreEqual(LuxembourgActClassManifestRefusal.DuplicateMember, refusal);
    }

    [TestMethod]
    public void AClassAppearingTwiceInTheAdmittedSetIsRefused()
    {
        Assert.IsNull(LuxembourgActClassManifest.TryCreate(
            [new LuxembourgActClassRef(Loi), new LuxembourgActClassRef(Loi)],
            [new LuxembourgActClassManifestMember(ActOne, new LuxembourgActClassRef(Loi))],
            Evidence(),
            out var refusal));
        Assert.AreEqual(LuxembourgActClassManifestRefusal.DuplicateAdmittedClass, refusal);
    }

    // ---- Surface. ----

    [TestMethod]
    public void TheExposedCollectionsCannotBeMutatedByACaller()
    {
        var frame = new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)));
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        Assert.IsNotInstanceOfType<List<LuxembourgNeverConsolidatedEntry>>(frame.Entries);
        if (frame.Entries is ICollection<LuxembourgNeverConsolidatedEntry> mutable)
        {
            Assert.ThrowsExactly<NotSupportedException>(() => mutable.Clear());
        }

        Assert.IsNotInstanceOfType<List<LuxembourgActClassManifestMember>>(frame.Manifest.Members);
        if (frame.Manifest.Members is ICollection<LuxembourgActClassManifestMember> members)
        {
            Assert.ThrowsExactly<NotSupportedException>(() => members.Clear());
        }

        if (frame.Manifest.AdmittedClasses is ICollection<LuxembourgActClassRef> classes)
        {
            Assert.ThrowsExactly<NotSupportedException>(() => classes.Clear());
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
            () => new LuxembourgNeverConsolidatedFrame(Manifest((ActOne, Loi)))
                .TryAdmit(null!, out _));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LuxembourgNeverConsolidatedFrame(null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LuxembourgActClassManifestMember(ActOne, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgActClassManifest.TryCreate(
            null!,
            [new LuxembourgActClassManifestMember(ActOne, new LuxembourgActClassRef(Loi))],
            Evidence(),
            out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgActClassManifest.TryCreate(
            [new LuxembourgActClassRef(Loi)], null!, Evidence(), out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgActClassManifest.TryCreate(
            [new LuxembourgActClassRef(Loi)],
            [new LuxembourgActClassManifestMember(ActOne, new LuxembourgActClassRef(Loi))],
            null!,
            out _));
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
            new[]
            {
                "\"none\"",
                "\"enumeration_incomplete\"",
                "\"manifest_member_not_dispositioned\"",
            },
            Enum.GetValues<LuxembourgNeverConsolidatedCountRefusal>()
                .Select(member => ContractJson.Serialize(member))
                .ToArray());

    [TestMethod]
    public void EveryAdmitRefusalHasItsExactWireToken() =>
        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"disposition_disagrees\"",
                "\"act_class_disagrees\"",
                "\"completion_evidence_disagrees\"",
                "\"act_class_contradicts_manifest\"",
                "\"member_dispositioned_outside\"",
                "\"non_member_dispositioned_inside\"",
            },
            Enum.GetValues<LuxembourgNeverConsolidatedAdmitRefusal>()
                .Select(member => ContractJson.Serialize(member))
                .ToArray());

    [TestMethod]
    public void EveryManifestRefusalHasItsExactWireToken() =>
        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"no_admitted_class\"",
                "\"no_member\"",
                "\"member_class_not_admitted\"",
                "\"duplicate_member\"",
                "\"duplicate_admitted_class\"",
            },
            Enum.GetValues<LuxembourgActClassManifestRefusal>()
                .Select(member => ContractJson.Serialize(member))
                .ToArray());

    // ---- Fixtures. ----

    /// <summary>
    /// A manifest whose admitted classes are exactly the distinct classes of its members.
    /// </summary>
    /// <remarks>
    /// Convenient for the frame's tests and useless for the manifest's own: a helper that cannot
    /// build a contradictory manifest cannot test that contradictions are refused, so those tests
    /// call <c>TryCreate</c> directly.
    /// </remarks>
    private static LuxembourgActClassManifest Manifest(params (string Act, string Class)[] members)
    {
        var manifest = LuxembourgActClassManifest.TryCreate(
            [.. members
                .Select(static member => member.Class)
                .Distinct(StringComparer.Ordinal)
                .Select(static value => new LuxembourgActClassRef(value))],
            [.. members.Select(static member => new LuxembourgActClassManifestMember(
                member.Act, new LuxembourgActClassRef(member.Class)))],
            Evidence(),
            out var refusal);
        Assert.IsNotNull(manifest, $"the fixture must mint an admitting manifest: {refusal}");
        return manifest!;
    }

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

    private static SourceArtifactRef OtherEvidence() => new(
        "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", new string('b', 64));
}
