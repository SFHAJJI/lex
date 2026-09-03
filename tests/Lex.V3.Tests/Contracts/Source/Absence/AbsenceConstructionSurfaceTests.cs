using Lex.V3.Contracts.Source.Absence;
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

    [TestMethod]
    public void ASubjectHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceSubject::.ctor("
                + Core + "SourceAuthority, " + Core + "SourceRegistryMemberRef, System.String, "
                + Core + "SourceObjectKeyRef) -> " + N + "AbsenceSubject",
                "method public static " + N + "AbsenceSubject::TryCreate("
                + Core + "SourceAuthority, " + Core + "SourceRegistryMemberRef, System.String, "
                + Core + "SourceObjectKeyRef, out " + N + "AbsenceSubjectRefusal&) -> "
                + N + "AbsenceSubject",
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
                + "out " + N + "AbsenceComparisonPolicyRefusal&) -> " + N + "AbsenceComparisonPolicy",
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
                + "out " + N + "AbsenceHistoryGenerationIdRefusal&) -> " + N + "AbsenceHistoryGenerationId",
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
                + N + "AbsenceFamilyObservation",
            },
            ConstructionSurface.Of(typeof(AbsenceFamilyObservation)).ToArray());
    }

    [TestMethod]
    public void ACutHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceCut::.ctor(System.String, "
                + N + "AbsenceRunCompletion, " + N + "AbsenceApplicableSet, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyObservation>, "
                + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                + "System.Collections.Generic.HashSet<System.String>) -> " + N + "AbsenceCut",
                "method public static " + N + "AbsenceCut::TryCreate(System.String, "
                + N + "AbsenceRunCompletion, " + N + "AbsenceApplicableSet, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "AbsenceFamilyObservation>, "
                + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "out " + N + "AbsenceCutRefusal&) -> " + N + "AbsenceCut",
            },
            ConstructionSurface.Of(typeof(AbsenceCut)).ToArray());
    }

    [TestMethod]
    public void AGenerationIsMintedOnlyByTheLedger()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "AbsenceHistoryLedger+Generation::.ctor("
                + N + "AbsenceSubject, " + N + "AbsenceHistoryGenerationId, "
                + N + "AbsenceHistoryGenerationId, System.Int32, " + N + "AbsenceComparisonPolicy, "
                + N + "AbsenceGenerationOpeningEventKind, System.String, "
                + N + "AbsenceHistoryGenerationCause) -> " + N + "AbsenceHistoryLedger+Generation",
                "method internal static " + N + "AbsenceHistoryLedger+Generation::Open("
                + N + "AbsenceSubject, " + N + "AbsenceHistoryLedger+Generation, System.Int32, "
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
                + N + "AbsenceHistoryLedger+Generation",
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
                + "System.String, out " + N + "AbsenceLedgerRefusal&) -> " + N + "AbsenceHistoryLedger",
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
                + N + "AbsenceReplacementCoordinateProfile",
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
                + N + "AbsenceReplacementClassification",
            },
            ConstructionSurface.Of(typeof(AbsenceReplacementClassification)).ToArray());
    }
}
