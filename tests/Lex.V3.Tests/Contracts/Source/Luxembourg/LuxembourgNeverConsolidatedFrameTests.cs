using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The never-consolidated recorder: one disposition per act, each agreeing with the enumeration
/// cited for it, and no population claim at all.
/// </summary>
/// <remarks>
/// An earlier head of this contract produced a population count from a caller-supplied class
/// manifest bound to a proof only by cardinality. Review established that any proven delivery of N
/// unrelated rows authorised any N caller-chosen act identities, so the count was not evidence-bound
/// and the manifest was the wrapper it travelled in. Both were removed rather than relocated. The
/// tests that matter here are the ones that fail if a population claim returns, or if one act can
/// carry two answers.
/// </remarks>
[TestClass]
public sealed class LuxembourgNeverConsolidatedFrameTests
{
    private const string Loi = "http://data.legilux.public.lu/resource/authority/legal-type/LOI";
    private const string Rgd = "http://data.legilux.public.lu/resource/authority/legal-type/RGD";

    /// <summary>Something Luxembourg has and this build has no member for.</summary>
    private const string Unknown =
        "http://data.legilux.public.lu/resource/authority/legal-type/ARRETE-MINISTERIEL";

    private const string ActOne = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo";
    private const string ActTwo = "http://data.legilux.public.lu/eli/etat/leg/rgd/2026/02/02/a2/jo";

    // ---- The population claim is gone, and must not come back. ----

    /// <summary>
    /// The recorder exposes nothing that returns a population, and the surface is pinned whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NAME FILTER WOULD NOT HOLD THIS. Forbidding members whose names contain "Count" would pass
    /// on one called <c>Total</c> or <c>Population</c>, and this is precisely the claim review found
    /// unevidenced, so the guard against its return has to be the whole member surface rather than a
    /// taboo list. Adding any member at all fails this test and makes the author say what it is.
    /// </para>
    /// <para>
    /// Pinned from what reflection reports, printed rather than hand-written.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheRecorderExposesNoPopulationCountAndItsSurfaceIsPinnedWhole()
    {
        var surface = typeof(LuxembourgNeverConsolidatedFrame)
            .GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Select(member => $"{member.MemberType} {member}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "Constructor Void .ctor()",
                "Method Boolean TryAdmit(Lex.V3.Contracts.Source.Luxembourg."
                    + "LuxembourgNeverConsolidatedEntry, Lex.V3.Contracts.Source.Luxembourg."
                    + "LuxembourgNeverConsolidatedAdmitRefusal ByRef)",
                "Method System.Collections.Generic.IReadOnlyList`1[Lex.V3.Contracts.Source.Luxembourg."
                    + "LuxembourgNeverConsolidatedEntry] get_Entries()",
                "Property System.Collections.Generic.IReadOnlyList`1[Lex.V3.Contracts.Source."
                    + "Luxembourg.LuxembourgNeverConsolidatedEntry] Entries",
            },
            surface,
            "the recorder records and exposes what it holds. A member that returned a population "
            + "would be the claim review found unevidenced, arriving again.");
    }

    /// <summary>No class-manifest type survives to carry the retired population scope.</summary>
    /// <remarks>
    /// The manifest was the wrapper the substitution travelled in: caller-supplied members bound to
    /// a proof by cardinality alone. Deleting the count while leaving the manifest would have left
    /// the same unevidenced scope one call away.
    /// </remarks>
    [TestMethod]
    public void NoClassManifestTypeSurvivesInTheContractAssembly()
    {
        var retired = typeof(LuxembourgNeverConsolidatedFrame).Assembly
            .GetTypes()
            .Select(type => type.FullName ?? type.Name)
            .Where(name =>
                name.Contains("ActClassManifest", StringComparison.Ordinal)
                || name.Contains("NeverConsolidatedCountRefusal", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(retired);
    }

    // ---- Absence needs evidence; non-absence must not carry it. ----

    [TestMethod]
    public void AnEnumeratedDispositionWithoutItsProofIsRejected()
    {
        foreach (var disposition in new[]
        {
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredRows,
        })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new LuxembourgNeverConsolidatedEntry(
                    ActOne, new LuxembourgActClassRef(Loi), disposition, null),
                $"{disposition} claims an enumeration completed and must name the proof.");
        }
    }

    [TestMethod]
    public void AnUnenumeratedDispositionCarryingAProofIsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgNeverConsolidatedEntry(
                ActOne,
                new LuxembourgActClassRef(Loi),
                LuxembourgNeverConsolidatedDisposition.NoEnumerationCited,
                Proof(ActOne, 0)),
            "'nobody enumerated this' must not carry evidence suggesting somebody had.");

    /// <summary>
    /// A disposition that contradicts its own proof is refused at construction, not recorded.
    /// </summary>
    /// <remarks>
    /// The proof decides which of the two cited-enumeration members this is, and only that: it says
    /// how many rows came back. A caller naming the member that contradicts its own proof's row
    /// count is contradicting itself inside one argument list.
    /// </remarks>
    [TestMethod]
    public void ADispositionThatContradictsItsOwnProofIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgNeverConsolidatedEntry(
                ActOne,
                new LuxembourgActClassRef(Loi),
                LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
                Proof(ActOne, 2)),
            "delivered no rows, beside a proof that delivered two.");

        Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgNeverConsolidatedEntry(
                ActOne,
                new LuxembourgActClassRef(Loi),
                LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredRows,
                Proof(ActOne, 0)),
            "delivered rows, beside a proof that delivered none.");
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
                LuxembourgNeverConsolidatedDisposition.NoEnumerationCited,
                null));
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
                LuxembourgNeverConsolidatedDisposition.NoEnumerationCited,
                null));
    }

    // ---- One act, one answer, compared on every admitted field. ----

    [TestMethod]
    public void ReadmittingOneActWithTheSameEntryIsIdempotent()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.None, refusal);
        Assert.HasCount(1, frame.Entries);
    }

    [TestMethod]
    public void TwoDifferentAnswersForOneActRefuseRatherThanOverwrite()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        var held = NeverConsolidated(ActOne, Loi);
        Assert.IsTrue(frame.TryAdmit(held, out _));

        Assert.IsFalse(frame.TryAdmit(Consolidated(ActOne, Loi), out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.DispositionDisagrees, refusal);
        Assert.HasCount(1, frame.Entries);
        Assert.AreSame(held, frame.Entries[0], "the frame never replaces an admitted answer.");
    }

    /// <summary>
    /// The same act re-presented with a different class is a contradictory fact, not a replay.
    /// </summary>
    /// <remarks>
    /// An earlier head compared the disposition alone, so LOI-then-RGD returned success with no
    /// disagreement and silently kept LOI.
    /// </remarks>
    [TestMethod]
    public void ReadmittingOneActWithADifferentClassIsADisagreement()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        Assert.IsFalse(frame.TryAdmit(NeverConsolidated(ActOne, Rgd), out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.ActClassDisagrees, refusal);
        Assert.AreEqual(
            Loi,
            frame.Entries[0].ActClass.PublisherClassIri,
            "and the held class is not quietly replaced.");
    }

    /// <summary>
    /// Two runs citing different enumerations for one act are two claims, not a replay.
    /// </summary>
    [TestMethod]
    public void ReadmittingOneActCitingADifferentEnumerationIsADisagreement()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        // A DIFFERENT RUN over the same rows, not a second object describing the same one.
        var other = new LuxembourgNeverConsolidatedEntry(
            ActOne,
            new LuxembourgActClassRef(Loi),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            Proof(ActOne, 0, runIdentitySeed: 931));

        Assert.IsFalse(frame.TryAdmit(other, out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedAdmitRefusal.CompletionEvidenceDisagrees, refusal);
        Assert.HasCount(1, frame.Entries);
    }

    /// <summary>
    /// Two proofs of ONE enumeration are one claim, so re-presenting an act with its own evidence
    /// freshly minted is still idempotent.
    /// </summary>
    /// <remarks>
    /// <see cref="AbsenceFamilyEnumerationProof"/> is a class with no value equality, so comparing
    /// the objects would make an honest replay a refusal.
    /// </remarks>
    [TestMethod]
    public void TwoProofsOfOneEnumerationAreOneClaim()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        var first = NeverConsolidated(ActOne, Loi);
        var second = NeverConsolidated(ActOne, Loi);

        Assert.AreNotSame(
            first.EnumerationCompletionProof,
            second.EnumerationCompletionProof,
            "the two entries must hold different proof OBJECTS for this to test anything.");

        Assert.IsTrue(frame.TryAdmit(first, out _));
        Assert.IsTrue(frame.TryAdmit(second, out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.None, refusal);
        Assert.HasCount(1, frame.Entries);
    }

    /// <summary>
    /// Two proofs of one delivery differing only in retention class are two claims, not one.
    /// </summary>
    /// <remarks>
    /// Found in review, not by me. The comparison read family, run, row count and key digest and
    /// omitted <c>RetainedFloor</c>, so a proof retained under the weaker custody class replayed as
    /// identical to a floored one and the stronger was silently kept. <c>Floored</c> and
    /// <c>RetainedUnenforced</c> are different guarantees about whether the retained bytes survive,
    /// so a record that treats them as interchangeable is asserting a durability it was not given.
    /// </remarks>
    [TestMethod]
    public void AProofRetainedUnderAWeakerCustodyClassIsADifferentClaim()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        var floored = new LuxembourgNeverConsolidatedEntry(
            ActOne,
            new LuxembourgActClassRef(Loi),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            Proof(ActOne, 0, floor: CustodyMembership.Floored));
        var unenforced = new LuxembourgNeverConsolidatedEntry(
            ActOne,
            new LuxembourgActClassRef(Loi),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            Proof(ActOne, 0, floor: CustodyMembership.RetainedUnenforced));

        // The premise: the two proofs differ in EXACTLY this one field, or the test proves nothing
        // about the field it names.
        Assert.AreEqual(
            floored.EnumerationCompletionProof!.CanonicalKeyDigest,
            unenforced.EnumerationCompletionProof!.CanonicalKeyDigest);
        Assert.AreEqual(
            floored.EnumerationCompletionProof.AcquisitionRunRef,
            unenforced.EnumerationCompletionProof.AcquisitionRunRef);
        Assert.AreNotEqual(
            floored.EnumerationCompletionProof.RetainedFloor,
            unenforced.EnumerationCompletionProof.RetainedFloor);

        Assert.IsTrue(frame.TryAdmit(floored, out _));
        Assert.IsFalse(frame.TryAdmit(unenforced, out var refusal));
        Assert.AreEqual(
            LuxembourgNeverConsolidatedAdmitRefusal.CompletionEvidenceDisagrees, refusal);
        Assert.AreEqual(
            CustodyMembership.Floored,
            frame.Entries[0].EnumerationCompletionProof!.RetainedFloor,
            "and the stronger claim is not quietly replaced by the weaker one.");
    }

    /// <summary>
    /// Every field a proof publishes takes part in deciding whether two proofs are one claim.
    /// </summary>
    /// <remarks>
    /// The omission review found was a LIST going stale, not logic going wrong: four of the proof's
    /// seven public fields were compared. A behavioural test per field would still say nothing about
    /// a field nobody has added yet, so this pins the surface against the comparison's own source.
    /// </remarks>
    [TestMethod]
    public void EveryProofFieldParticipatesInTheClaimComparison()
    {
        var fields = typeof(AbsenceFamilyEnumerationProof)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "AcquisitionRunRef",
                "CanonicalKeyDigest",
                "DeliveredRowCount",
                "FamilyKey",
                "InterpretationProfileRef",
                "RetainedFloor",
                "SourceProfileRef",
            },
            fields,
            "a proof that grew a field must also be compared on it, or two different claims "
            + "become one replay.");

        var comparison = MethodBody(
            ReadSource("src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgNeverConsolidatedFrame.cs"),
            "private static bool SameEnumeration(");

        foreach (var field in fields)
        {
            StringAssert.Contains(
                comparison,
                "left." + field,
                $"SameEnumeration does not read {field}.");
        }
    }

    /// <summary>One method's brace-matched body, so a mention elsewhere cannot satisfy the pin.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, $"'{signature}' is not in the source read.");

        var open = source.IndexOf('{', start);
        Assert.IsGreaterThanOrEqualTo(0, open, "the method has no body.");

        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[open..index];
            }
        }

        Assert.Fail("the method body is unterminated.");
        return string.Empty;
    }

    private static string ReadSource(string repositoryRelativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("Checkout root not found.");
        return File.ReadAllText(
            Path.Combine(root, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// An act re-presented as unproven after being enumerated is a disagreement, not a downgrade.
    /// </summary>
    [TestMethod]
    public void AnEnumeratedActCannotBeDowngradedToUnproven()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

        Assert.IsFalse(frame.TryAdmit(Unproven(ActOne, Loi), out var refusal));
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.DispositionDisagrees, refusal);
        Assert.HasCount(1, frame.Entries);
    }

    /// <summary>
    /// Two act IRIs differing only in case are two acts. The frame keys on the IRI, and IRI paths
    /// are case-sensitive.
    /// </summary>
    /// <remarks>
    /// Found by a mechanical sweep, not by me: flipping the frame's comparer to ignore-case survived
    /// every other test here. Under that flip two distinct acts would collide and the second would be
    /// read as a disagreement with the first.
    /// </remarks>
    [TestMethod]
    public void TwoActIrisDifferingOnlyByCaseAreTwoActs()
    {
        var shouted = ActOne.Replace("/a1", "/A1", StringComparison.Ordinal);
        Assert.AreNotEqual(ActOne, shouted);

        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));
        Assert.IsTrue(
            frame.TryAdmit(NeverConsolidated(shouted, Loi), out var refusal),
            "a different IRI is a different act, not a second answer about the first.");
        Assert.AreEqual(LuxembourgNeverConsolidatedAdmitRefusal.None, refusal);

        Assert.HasCount(2, frame.Entries);
    }

    // ---- Surface. ----

    [TestMethod]
    public void TheExposedCollectionCannotBeMutatedByACaller()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        Assert.IsTrue(frame.TryAdmit(NeverConsolidated(ActOne, Loi), out _));

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
                LuxembourgNeverConsolidatedDisposition.NoEnumerationCited,
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
                "\"cited_enumeration_delivered_no_rows\"",
                "\"cited_enumeration_delivered_rows\"",
                "\"no_enumeration_cited\"",
            },
            Enum.GetValues<LuxembourgNeverConsolidatedDisposition>()
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
            },
            Enum.GetValues<LuxembourgNeverConsolidatedAdmitRefusal>()
                .Select(member => ContractJson.Serialize(member))
                .ToArray());

    // ---- Slice 3: the proof must be THIS act's own enumeration. ----

    /// <summary>
    /// A cited enumeration whose proof is keyed to another act refuses: the family key is not this
    /// act's per-act consolidation key.
    /// </summary>
    /// <remarks>
    /// The #584 defect, closed. Before slice 3 a zero-row proof of an unrelated family satisfied
    /// every construction check; now the proof's <c>FamilyKey</c> must equal
    /// <c>PartitionKeyFor</c> of this entry's act, and another act's key is refused by name.
    /// </remarks>
    [TestMethod]
    public void ACitedEnumerationWhoseProofNamesAnotherActRefuses()
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgNeverConsolidatedEntry(
            ActOne,
            new LuxembourgActClassRef(Loi),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            Proof(ActTwo, 0)));
        Assert.AreEqual("enumerationCompletionProof", thrown.ParamName);
        StringAssert.Contains(
            thrown.Message, "not this act's per-act consolidation key",
            "refused by the family-key binding, not by the row-count guard that shares its ParamName.");
    }

    /// <summary>The generic inventory family key - the pre-slice-1 default - is refused.</summary>
    /// <remarks>
    /// <c>laws</c> is the family key the never-consolidated frame's own earlier fixtures minted
    /// under, and #584 established such a key paired with an act proves nothing. It is not this act's
    /// per-act key, so it refuses.
    /// </remarks>
    [TestMethod]
    public void ACitedEnumerationWhoseProofCarriesTheGenericFamilyRefuses()
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgNeverConsolidatedEntry(
            ActOne,
            new LuxembourgActClassRef(Loi),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            ProofKeyed("laws", 0)));
        Assert.AreEqual("enumerationCompletionProof", thrown.ParamName);
        StringAssert.Contains(
            thrown.Message, "not this act's per-act consolidation key",
            "refused by the family-key binding, not by the row-count guard that shares its ParamName.");
    }

    /// <summary>
    /// A cited enumeration for an act spelled a way the per-act family cannot key refuses at the act,
    /// not merely at the family key.
    /// </summary>
    /// <remarks>
    /// The act must be the one admitted spelling <c>PartitionKeyFor</c> digests (slice 1's condition
    /// 1). An https spelling of the same act is a publisher URI the frame's general act field admits,
    /// but a cited enumeration cannot be keyed under it, so it refuses with the act as the offender.
    /// </remarks>
    [TestMethod]
    public void ACitedEnumerationForANonAdmittedActSpellingRefuses()
    {
        const string HttpsSpelling = "https://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo";
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgNeverConsolidatedEntry(
            HttpsSpelling,
            new LuxembourgActClassRef(Loi),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            Proof(ActOne, 0)));
        Assert.AreEqual("publisherActIri", thrown.ParamName);
    }

    /// <summary>A proof keyed to this act's own per-act key is admitted, and the entry carries it.</summary>
    [TestMethod]
    public void ACitedEnumerationKeyedToThisActsPerActKeyIsAdmitted()
    {
        var entry = new LuxembourgNeverConsolidatedEntry(
            ActOne,
            new LuxembourgActClassRef(Loi),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            Proof(ActOne, 0));

        Assert.AreEqual(
            LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(ActOne),
            entry.EnumerationCompletionProof!.FamilyKey,
            "the admitted proof is bound to this act's per-act consolidation key.");
    }

    // ---- Fixtures. ----

    private static LuxembourgNeverConsolidatedEntry NeverConsolidated(string act, string actClass) =>
        new(act,
            new LuxembourgActClassRef(actClass),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            Proof(act, 0));

    private static LuxembourgNeverConsolidatedEntry Consolidated(string act, string actClass) =>
        new(act,
            new LuxembourgActClassRef(actClass),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredRows,
            Proof(act, 2));

    private static LuxembourgNeverConsolidatedEntry Unproven(string act, string actClass) =>
        new(act,
            new LuxembourgActClassRef(actClass),
            LuxembourgNeverConsolidatedDisposition.NoEnumerationCited,
            null);

    /// <summary>
    /// A real <see cref="AbsenceFamilyEnumerationProof"/> over a delivery of exactly
    /// <paramref name="rows"/> rows.
    /// </summary>
    /// <remarks>
    /// These used to be hand-written <see cref="SourceArtifactRef"/> literals, which is exactly the
    /// defect review found: a structurally valid reference that no enumeration stands behind. A proof
    /// can only be minted from an <see cref="EnumerationDeliveryComparison"/> whose two independent
    /// passes agreed, so the fixture now pays the same price a caller does.
    /// </remarks>
    private static AbsenceFamilyEnumerationProof Proof(
        string act,
        int rows,
        int runIdentitySeed = 930,
        CustodyMembership floor = CustodyMembership.Floored) =>
        ProofKeyed(
            LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(act), rows, runIdentitySeed, floor);

    /// <summary>
    /// A real proof over a delivery whose partition - and so the proof's <c>FamilyKey</c> - is
    /// exactly <paramref name="familyKey"/>. Slice 3 binds a cited-enumeration entry to
    /// <c>PartitionKeyFor(act)</c>, so an honest fixture mints its proof under that key; the hostile
    /// cases mint under another key to prove the binding refuses.
    /// </summary>
    private static AbsenceFamilyEnumerationProof ProofKeyed(
        string familyKey,
        int rows,
        int runIdentitySeed = 930,
        CustodyMembership floor = CustodyMembership.Floored)
    {
        var fixture = rows == 0
            ? new RepeatedEnumerationDeliveryProofTests.Fixture(
                terminalPagePolicy:
                    RepeatedEnumerationTerminalPagePolicy.EmptySuccessorAfterShortPage,
                expectedCount: 0,
                rawRows: EmptyRows,
                runIdentitySeed: runIdentitySeed,
                partitionKey: familyKey)
            : new RepeatedEnumerationDeliveryProofTests.Fixture(
                expectedCount: rows, runIdentitySeed: runIdentitySeed, partitionKey: familyKey);

        var cursors = rows == 0
            ? "ignored"
            : string.Join(',', Enumerable.Range(0, rows).Select(static index =>
                ((char)('a' + index)).ToString()));

        var delivery = fixture.Create(cursors, cursors);
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            familyKey, delivery, floor, out var refusal);
        Assert.IsNotNull(proof, $"the fixture must mint an admitting proof: {refusal}");
        return proof!;
    }

    private const string EmptyRows =
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[]}}";
}
