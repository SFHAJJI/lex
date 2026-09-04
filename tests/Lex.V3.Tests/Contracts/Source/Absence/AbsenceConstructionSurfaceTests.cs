using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Absence;

/// <summary>
/// The construction surface of the absence lifecycle types.
///
/// <para>
/// Each of these types exists because something about it must be true before it can be used: a
/// subject must be addressable, a comparison policy must decide every member, an observation must
/// be a fresh family response, a cut must carry real temporal evidence, a generation must have an
/// identity no configuration can reproduce. A second door onto any of them is a way to hold an
/// object none of that was checked for.
/// </para>
/// <para>
/// The two nested types are the reason this file exists rather than a comment. C# gives an
/// enclosing type no access to a nested type's private members, so
/// <c>AbsenceHistoryLedger.Generation.Open</c> and the <c>CutReceipt</c> constructor have to be
/// internal for the ledger to call them, and internal is reachable by every type in this assembly
/// and by anything it befriends. Visibility therefore does not enforce "only the ledger mints
/// these"; this pin does, by making a new producer a line in a diff.
/// </para>
/// </summary>
[TestClass]
public sealed class AbsenceConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Absence.";
    private const string Core = "Lex.V3.Contracts.Source.Core.";
    private const string Custody = "Lex.V3.Contracts.Custody.";
    private const string Lu = "Lex.V3.Contracts.Source.Luxembourg.";

    [TestMethod]
    public void ASubjectHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceSubject::.ctor("
                + Core + "SourceAuthority, " + Core + "SourceRegistryMemberRef, System.String, "
                + Core + "SourceObjectKeyRef?) -> " + N + "AbsenceSubject",
                "method public static " + N + "AbsenceSubject::TryCreate("
                + Core + "SourceAuthority, " + Core + "SourceRegistryMemberRef, System.String, "
                + Core + "SourceObjectKeyRef?, out " + N + "AbsenceSubjectRefusal&) -> "
                + N + "AbsenceSubject?",
            },
            ConstructionSurface.Of(typeof(AbsenceSubject)).ToArray());
    }

    [TestMethod]
    public void AComparisonPolicyHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceComparisonPolicy::.ctor("
                + "System.Collections.Generic.Dictionary<" + N + "AbsenceComparisonPolicyMember, System.String>, "
                + "System.String) -> " + N + "AbsenceComparisonPolicy",
                "method public static " + N + "AbsenceComparisonPolicy::TryCreate("
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceComparisonPolicyDigest>, "
                + "out " + N + "AbsenceComparisonPolicyRefusal&) -> " + N + "AbsenceComparisonPolicy?",
            },
            ConstructionSurface.Of(typeof(AbsenceComparisonPolicy)).ToArray());
    }

    [TestMethod]
    public void AGenerationIdentityHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceHistoryGenerationId::.ctor(System.String) -> "
                + N + "AbsenceHistoryGenerationId",
                "method public static " + N + "AbsenceHistoryGenerationId::TryCreate("
                + N + "AbsenceSubject, System.Int32, System.String, "
                + "out " + N + "AbsenceHistoryGenerationIdRefusal&) -> " + N + "AbsenceHistoryGenerationId?",
            },
            ConstructionSurface.Of(typeof(AbsenceHistoryGenerationId)).ToArray());
    }

    [TestMethod]
    public void AnObservationHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceFamilyObservation::.ctor("
                + "System.String, System.String, System.DateTimeOffset, "
                + N + "AbsenceTimestampPrecision, System.String, System.TimeSpan) -> "
                + N + "AbsenceFamilyObservation",
                "method public static " + N + "AbsenceFamilyObservation::TryCreate("
                + "System.String, System.String, System.DateTimeOffset, "
                + N + "AbsenceTimestampPrecision, System.String, System.TimeSpan, "
                + N + "AbsenceObservationProvenance, out " + N + "AbsenceFamilyObservationRefusal&) -> "
                + N + "AbsenceFamilyObservation?",
            },
            ConstructionSurface.Of(typeof(AbsenceFamilyObservation)).ToArray());
    }

    /// <summary>
    /// A cut has one checked door per completion state and no other. Only
    /// <c>TryCreateComplete</c> reaches <see cref="AbsenceRunCompletion.EnumerationComplete"/>, and
    /// its parameter list is the guarantee: it takes enumeration proofs, and the completion state
    /// is not a parameter of either door. A third factory, or a completion parameter reappearing on
    /// one of these, would put the declared completeness back and is a line in this diff.
    /// </summary>
    [TestMethod]
    public void ACutHasOneCheckedDoorPerCompletionStateAndCompletionIsNeverAParameter()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceCut::.ctor(System.String, "
                + N + "AbsenceRunCompletion, " + N + "AbsenceApplicableSet, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyObservation>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyEnumerationProof>, "
                + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                + "System.Collections.Generic.HashSet<System.String>) -> " + N + "AbsenceCut",
                "method private static " + N + "AbsenceCut::Create(System.String, "
                + N + "AbsenceRunCompletion, " + N + "AbsenceApplicableSet, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyObservation>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyEnumerationProof>, "
                + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "out " + N + "AbsenceCutRefusal&) -> " + N + "AbsenceCut?",
                "method public static " + N + "AbsenceCut::TryCreateComplete(System.String, "
                + N + "AbsenceApplicableSet, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyObservation>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyEnumerationProof>, "
                + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "out " + N + "AbsenceCutRefusal&) -> " + N + "AbsenceCut?",
                "method public static " + N + "AbsenceCut::TryCreatePartial(System.String, "
                + N + "AbsenceApplicableSet, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyObservation>, "
                + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "out " + N + "AbsenceCutRefusal&) -> " + N + "AbsenceCut?",
            },
            ConstructionSurface.Of(typeof(AbsenceCut)).ToArray());
    }

    /// <summary>
    /// A family enumeration proof has exactly one checked door, and its only evidence parameter is
    /// a <c>Source.Core</c> delivery comparison. A second door taking a boolean, a row count or a
    /// digest would be a way to hold a proof nothing verified.
    /// </summary>
    [TestMethod]
    public void AFamilyEnumerationProofHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceFamilyEnumerationProof::.ctor("
                + "System.String, " + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                + Core + "SourceArtifactRef, System.Int64, System.String, "
                + Custody + "CustodyMembership) -> "
                + N + "AbsenceFamilyEnumerationProof",
                "method public static " + N + "AbsenceFamilyEnumerationProof::TryCreate("
                + "System.String, " + Core + "EnumerationDeliveryComparison, "
                + Custody + "CustodyMembership, out "
                + N + "AbsenceFamilyEnumerationProofRefusal&) -> "
                + N + "AbsenceFamilyEnumerationProof?",
            },
            ConstructionSurface.Of(typeof(AbsenceFamilyEnumerationProof)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                // The auto-property's backing field is named rather than filtered out by a
                // substring test. Filtering it was the same defect the neighbouring test removed
                // from this file two tests up, reintroduced for the analogous case: a pin whose
                // whole purpose is that a new holder appears as a visible diff cannot decide what
                // to look at by matching a name. Storage inside the cut is legitimate; saying so
                // by naming it costs one line and keeps the diff honest.
                "field private instance " + N + "AbsenceCut::<EnumerationProofs>k__BackingField -> "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyEnumerationProof>",

                // D1-06c-LU-2: the Luxembourg proof door. It HOLDS a proof rather than minting one,
                // which is why it appears here and why that is admitted. Scope resolution and the
                // body join read observations only through this type, so a caller without a real
                // family proof cannot reach them at all (RULING
                // lex-event-20260904T204900861Z-6b737927d58a409dab05149aa28052e5), and it cannot
                // manufacture a proof: the single checked door pinned above is still the only mint.
                "field private instance " + Lu + "LuxembourgProvenResourceObservations"
                + "::<AssertionFamilyProof>k__BackingField -> " + N + "AbsenceFamilyEnumerationProof",

                // The publisher-neutral delivery receipt's bridge (queue item 19: moved and renamed
                // from Lex.V3.Contracts.Source.Luxembourg.LuxembourgEnumerationDeliveryReceipt), and
                // the second producer this pin was written to catch. It is admitted, not tolerated:
                // it takes the family key and reads the receipt's own verified Delivery, so it can
                // only mint a proof from a comparison this repository's own verifying factory
                // produced. Under RULING
                // lex-event-20260904T215906714Z-6dadaf27829d4a3aa3c355063754ccd6 it also STAMPS the run's
                // custody class onto the proof, which is why the door below carries a
                // CustodyMembership. It used to read a RequireFlooredRun accessor that threw, so no
                // proof existed at all for an unfloored run; durability is now required at the
                // release instead, and AbsenceCutTests
                // .ACompleteCutRefusesAProofHeldWithoutAnEnforcedFloor is that guard.
                "method public instance " + Core + "RepeatedEnumerationDeliveryReceipt"
                + "::TryProveFamilyEnumeration(System.String, out "
                + N + "AbsenceFamilyEnumerationProofRefusal&) -> "
                + N + "AbsenceFamilyEnumerationProof?",
                "property public instance " + N + "AbsenceCut::EnumerationProofs() -> "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyEnumerationProof>",
                "property public instance " + Lu + "LuxembourgProvenResourceObservations"
                + "::AssertionFamilyProof() -> " + N + "AbsenceFamilyEnumerationProof",
            },
            ConstructionSurface.ProducersIn(
                typeof(AbsenceCut).Assembly,
                typeof(AbsenceFamilyEnumerationProof),
                true).ToArray(),
            "a family enumeration proof reached a new holder in Contracts");
    }

    /// <summary>
    /// The load-bearing assumption of the whole binding, asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// <see cref="AbsenceFamilyEnumerationProof"/> is only worth more than a boolean because a
    /// <c>Source.Core</c> delivery comparison cannot be obtained without the verification its
    /// factory performs against resolved retained evidence. If a second producer of one ever
    /// appears, in <c>Source.Core</c> or anywhere else in Contracts, then completeness is declared
    /// again through a longer route and this pin is where that shows up. It is asserted from here,
    /// beside the contract that depends on it, so the dependency is visible from the dependent
    /// side; <c>Source.Core</c> is another lane's file and keeps its own guards.
    /// </remarks>
    [TestMethod]
    public void ADeliveryComparisonIsMintedOnlyByItsOwnVerifyingFactory()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Core + "EnumerationDeliveryComparison::.ctor("
                + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                + Core + "SourceArtifactRef, System.String, "
                + Core + "RepeatedEnumerationThresholdAssessment, "
                + Core + "RepeatedEnumerationEvidenceRefs, " + Core + "EnumerationPageSetRefs, "
                + Core + "RepeatedEnumerationEvidenceRefs, " + Core + "EnumerationPageSetRefs, "
                + Core + "EnumerationObservationTimes, System.Int64, System.Int64, "
                + "System.Collections.Generic.IReadOnlyList<" + Core + "RepeatedEnumerationRow>, "
                + "System.Collections.Generic.IReadOnlyList<" + Core + "RepeatedEnumerationRow>) -> "
                + Core + "EnumerationDeliveryComparison",
                "method public static " + Core + "EnumerationDeliveryComparison::Create("
                + Core + "RepeatedEnumerationInterpretationProfile, " + Core + "SourceArtifactRef, "
                + Core + "RepeatedEnumerationEvidenceRefs, " + Core + "EnumerationPageSetRefs, "
                + Core + "RepeatedEnumerationEvidenceRefs, " + Core + "EnumerationPageSetRefs, "
                + Core + "IRepeatedEnumerationEvidenceResolver) -> "
                + Core + "EnumerationDeliveryComparison",
            },
            ConstructionSurface.Of(typeof(EnumerationDeliveryComparison)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                // Both are the publisher-neutral delivery receipt (queue item 19: moved and
                // renamed from Lex.V3.Contracts.Source.Luxembourg.LuxembourgEnumerationDeliveryReceipt)
                // holding the comparison it was minted from. None of them is a second way to OBTAIN
                // one: the receipt's only door takes a comparison as a parameter, so nothing here
                // can exist without EnumerationDeliveryComparison.Create having already run above.
                // Delivery hands it back asserting nothing. There were three: a RequireFlooredRun
                // accessor handed the same object back only when every custody member was floored,
                // and RULING
                // lex-event-20260904T215906714Z-6dadaf27829d4a3aa3c355063754ccd6 took its last caller
                // away. It was removed rather than left unreferenced, so this pin is down to two.
                "field private instance " + Core + "RepeatedEnumerationDeliveryReceipt"
                + "::<Delivery>k__BackingField -> " + Core + "EnumerationDeliveryComparison",
                "property public instance " + Core + "RepeatedEnumerationDeliveryReceipt"
                + "::Delivery() -> " + Core + "EnumerationDeliveryComparison",
            },
            ConstructionSurface.ProducersIn(
                typeof(AbsenceCut).Assembly, typeof(EnumerationDeliveryComparison), true).ToArray(),
            "something in Contracts now hands out a delivery comparison it did not have to verify");
    }

    [TestMethod]
    public void AGenerationIsMintedOnlyByTheLedger()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceHistoryLedger+Generation::.ctor("
                + N + "AbsenceSubject, " + N + "AbsenceHistoryGenerationId, "
                + N + "AbsenceHistoryGenerationId?, System.Int32, " + N + "AbsenceComparisonPolicy, "
                + N + "AbsenceGenerationOpeningEventKind, System.String, "
                + N + "AbsenceHistoryGenerationCause) -> " + N + "AbsenceHistoryLedger+Generation",
                "method internal static " + N + "AbsenceHistoryLedger+Generation::Open("
                + N + "AbsenceSubject, " + N + "AbsenceHistoryLedger+Generation?, System.Int32, "
                + N + "AbsenceComparisonPolicy, " + N + "AbsenceGenerationOpeningEventKind, "
                + "System.String, " + N + "AbsenceHistoryGenerationCause) -> "
                + N + "AbsenceHistoryLedger+Generation",
            },
            ConstructionSurface.Of(typeof(AbsenceHistoryLedger.Generation)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                // The ledger's own backing list is named here rather than filtered out of the
                // sweep. It was excluded by a Contains test on the field name, which is name
                // filtering inside the pin whose entire point is that a new holder shows up as a
                // visible diff. Storage inside the ledger is legitimate, and saying so by naming
                // it costs one line and keeps the diff honest.
                "field private instance " + N + "AbsenceHistoryLedger::_generations -> "
                + "System.Collections.Generic.List<" + N + "AbsenceHistoryLedger+Generation>",
                "method public instance " + N + "AbsenceHistoryLedger::TryTransitionComparisonPolicy("
                + N + "AbsenceComparisonPolicy, System.String, out " + N + "AbsenceLedgerRefusal&) -> "
                + N + "AbsenceHistoryLedger+Generation?",
                "property public instance " + N + "AbsenceHistoryLedger::CurrentGeneration() -> "
                + N + "AbsenceHistoryLedger+Generation",
                "property public instance " + N + "AbsenceHistoryLedger::Generations() -> "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceHistoryLedger+Generation>",
            },
            ConstructionSurface.ProducersIn(
                typeof(AbsenceHistoryLedger).Assembly,
                typeof(AbsenceHistoryLedger.Generation),
                true).ToArray(),
            "a generation reached a new holder in Contracts");
    }

    [TestMethod]
    public void ALedgerHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceHistoryLedger::.ctor("
                + N + "AbsenceSubject, " + N + "AbsenceApplicableSet, "
                + N + "AbsenceHistoryLedger+Generation) -> " + N + "AbsenceHistoryLedger",
                "method public static " + N + "AbsenceHistoryLedger::TryOpen("
                + N + "AbsenceSubject, " + N + "AbsenceApplicableSet, " + N + "AbsenceComparisonPolicy, "
                + "System.String, out " + N + "AbsenceLedgerRefusal&) -> " + N + "AbsenceHistoryLedger?",
            },
            ConstructionSurface.Of(typeof(AbsenceHistoryLedger)).ToArray());
    }

    [TestMethod]
    public void AReplacementClassificationAndItsProfileHaveOneCheckedDoorEach()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceReplacementCoordinateProfile::.ctor("
                + "System.String, System.Collections.Generic.IReadOnlyList<" + N + "AbsenceCoordinateField>) -> "
                + N + "AbsenceReplacementCoordinateProfile",
                "method public static " + N + "AbsenceReplacementCoordinateProfile::TryCreate("
                + "System.String, System.Collections.Generic.IReadOnlyList<" + N + "AbsenceCoordinateField>, "
                + "out " + N + "AbsenceReplacementCoordinateProfileRefusal&) -> "
                + N + "AbsenceReplacementCoordinateProfile?",
            },
            ConstructionSurface.Of(typeof(AbsenceReplacementCoordinateProfile)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceReplacementClassification::.ctor("
                + N + "AbsenceReplacementCoordinateProfile, System.String, System.String, "
                + "System.Collections.Generic.HashSet<System.String>, "
                + "System.Collections.Generic.HashSet<System.String>, "
                + "System.Collections.Generic.HashSet<System.String>, "
                + "System.Collections.Generic.HashSet<System.String>, "
                + "System.Collections.Generic.HashSet<System.String>, "
                + N + "AbsenceReplacementDisposition) -> " + N + "AbsenceReplacementClassification",
                "method public static " + N + "AbsenceReplacementClassification::TryClassify("
                + N + "AbsenceReplacementCoordinateProfile, System.String, System.String, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "out " + N + "AbsenceReplacementClassificationRefusal&) -> "
                + N + "AbsenceReplacementClassification?",
            },
            ConstructionSurface.Of(typeof(AbsenceReplacementClassification)).ToArray());
    }
}
