using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Contracts.Source.Corpus;

/// <summary>
/// The construction surface of the six types D1-06a declared plus the two types this fix added
/// (<see cref="CorpusBodyPendingAcquisitionReasonKind"/> and
/// <see cref="CorpusBodyPendingAcquisitionReason"/>): a note carried in the peer reviewer verdict
/// on this slice (event <c>lex-event-20260904T071246618Z-2d4ca939f7144ea5ac3fd4c421091154</c>) was
/// that none of this file's declared types had a reflection pin at all, following the idiom
/// <c>LuxembourgConstructionSurfaceTests</c> and <c>QuarantineConstructionSurfaceTests</c> already
/// establish for other guarded contract types in this codebase.
///
/// <para>
/// Every entry below is transcribed from what <see cref="ConstructionSurface"/> actually reflected
/// against the real committed types (a throwaway <c>Assert.Fail("ACTUAL&gt;&gt;&gt;" + ...)</c>
/// probe, run once and discarded, per this codebase's own print-then-transcribe discipline), not
/// guessed: enum members are static literal fields of the enum type itself, so they appear as
/// <c>field</c> entries; records auto-generate a private copy constructor and a
/// <c>&lt;Clone&gt;$()</c> method that the sweep reports; enums additionally report the two
/// protected base constructors <c>System.Enum</c> and <c>System.ValueType</c> declare.
/// </para>
/// <para>
/// <see cref="VerifiedCorpusRecord"/> is this file's one gate: its constructor is
/// <c>internal</c>, so <see cref="VerifiedCorpusRecord.ParseAndVerify"/> is the only production
/// path, and the <see cref="ConstructionSurface.ProducersIn"/> check below confirms nothing else in
/// <c>Lex.V3.Contracts</c> can hand out an instance. Every other type here is an open wire shape by
/// design (a <see cref="Microsoft.VisualStudio.TestTools.UnitTesting"/>-free JSON contract a caller
/// or <c>ContractJson</c> constructs directly), exactly as <c>CutCompletionClaim</c> and its
/// siblings are documented in <c>CutReleaseGateConstructionSurfaceTests</c> -- pinned anyway, so a
/// second door or an unreviewed new producer is a visible diff rather than silent.
/// </para>
/// </summary>
[TestClass]
public sealed class CorpusRecordConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Corpus.";
    private const string Core = "Lex.V3.Contracts.Source.Core.";
    private const string Custody = "Lex.V3.Contracts.Custody.";
    private const string Scope = "Lex.V3.Contracts.Source.Scope.";

    [TestMethod]
    public void SchemaIdsHasNoConstructionSurface()
    {
        // A static class holding only a const string: no constructor, and no member whose
        // signature carries the guarded type itself.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.Of(typeof(CorpusRecordSchemaIds)).ToArray());
    }

    [TestMethod]
    public void BodyRecordKindIsExactlyItsThreeMembers()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "CorpusBodyRecordKind::Held -> " + N + "CorpusBodyRecordKind",
                "field public static " + N + "CorpusBodyRecordKind::NotHeld -> " + N + "CorpusBodyRecordKind",
                "field public static " + N + "CorpusBodyRecordKind::PendingAcquisition -> "
                    + N + "CorpusBodyRecordKind",
            },
            ConstructionSurface.Of(typeof(CorpusBodyRecordKind)).ToArray(),
            "a fourth body-record shape must be justified in review, not discovered later");
    }

    [TestMethod]
    public void PendingAcquisitionReasonKindIsExactlyItsTwoMembers()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "CorpusBodyPendingAcquisitionReasonKind::AcquisitionRefused -> "
                    + N + "CorpusBodyPendingAcquisitionReasonKind",
                "field public static " + N + "CorpusBodyPendingAcquisitionReasonKind::NotYetAcquired -> "
                    + N + "CorpusBodyPendingAcquisitionReasonKind",
            },
            ConstructionSurface.Of(typeof(CorpusBodyPendingAcquisitionReasonKind)).ToArray());
    }

    /// <summary>
    /// D1-06b closed <see cref="CorpusBodyPendingAcquisitionReason"/>'s own free-form
    /// <c>Refusal</c> string into this vocabulary, originally fourteen members (one per cause named
    /// in <c>Lex.V3.Contracts.Source.Http.HttpAcquisitionReasonRegistry</c>, read, never touched),
    /// widened to twenty-two by D1-06c-EU fix one (SCOPE_RULING
    /// lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93 item 1): three real EU
    /// document-fetch causes plus five reserved for the LU-2 lane's own document-get route.
    /// </summary>
    [TestMethod]
    public void AcquisitionRefusalReasonIsExactlyItsTwentyTwoMembers()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "CorpusAcquisitionRefusalReason::BodyDeadline -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::BodyReadFailure -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::ByteBoundPreventedCompletion -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::CallerCancelledAfterHeaders -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::DeclaredLengthShortRead -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::Gone -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::HeaderDeadline -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::InvalidContentLength -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::MissingCompletionProof -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::NotFound -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::RedirectTargetOriginNotAdmitted -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::RequestedRepresentationNotServed -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::RetryExhausted -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::RevalidationRequestNotAdmitted -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::RobotsDisallowed -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::StatusContentForbidden -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::StatusFramingConflict -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::TransferCodingConflict -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::TransportBeforeHeaders -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::UnexpectedPublisherStatus -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::UnsupportedTransferCoding -> "
                    + N + "CorpusAcquisitionRefusalReason",
                "field public static " + N + "CorpusAcquisitionRefusalReason::WrongAcceptToken -> "
                    + N + "CorpusAcquisitionRefusalReason",
            },
            ConstructionSurface.Of(typeof(CorpusAcquisitionRefusalReason)).ToArray(),
            "a twenty-third refusal cause must be justified in review, not discovered later");
    }

    [TestMethod]
    public void PendingAcquisitionReasonHasExactlyItsOwnPublicConstructorAndFactories()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "CorpusBodyPendingAcquisitionReason::.ctor("
                    + N + "CorpusBodyPendingAcquisitionReason) -> " + N + "CorpusBodyPendingAcquisitionReason",
                "constructor public instance " + N + "CorpusBodyPendingAcquisitionReason::.ctor("
                    + N + "CorpusBodyPendingAcquisitionReasonKind, "
                    + "System.Nullable<" + N + "CorpusAcquisitionRefusalReason>) -> "
                    + N + "CorpusBodyPendingAcquisitionReason",
                "method public instance " + N + "CorpusBodyPendingAcquisitionReason::<Clone>$() -> "
                    + N + "CorpusBodyPendingAcquisitionReason",
                "method public static " + N + "CorpusBodyPendingAcquisitionReason::AcquisitionRefused("
                    + N + "CorpusAcquisitionRefusalReason) -> " + N + "CorpusBodyPendingAcquisitionReason",
                "method public static " + N + "CorpusBodyPendingAcquisitionReason::NotYetAcquired() -> "
                    + N + "CorpusBodyPendingAcquisitionReason",
            },
            ConstructionSurface.Of(typeof(CorpusBodyPendingAcquisitionReason)).ToArray());
    }

    [TestMethod]
    public void BodyRecordHasExactlyItsOwnPublicConstructorAndThreeFactories()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "CorpusBodyRecord::.ctor("
                    + N + "CorpusBodyRecord) -> " + N + "CorpusBodyRecord",
                "constructor public instance " + N + "CorpusBodyRecord::.ctor("
                    + N + "CorpusBodyRecordKind, " + Custody + "DurableBlobWriteReceipt, "
                    + "System.Nullable<" + Custody + "CustodyMembership>, "
                    + "System.Nullable<" + Scope + "ScopeDisposition>, "
                    + N + "CorpusBodyPendingAcquisitionReason) -> " + N + "CorpusBodyRecord",
                "method public instance " + N + "CorpusBodyRecord::<Clone>$() -> " + N + "CorpusBodyRecord",
                "method public static " + N + "CorpusBodyRecord::Held(" + Custody + "DurableBlobWriteReceipt) -> "
                    + N + "CorpusBodyRecord",
                "method public static " + N + "CorpusBodyRecord::NotHeld(" + Scope + "ScopeDisposition) -> "
                    + N + "CorpusBodyRecord",
                "method public static " + N + "CorpusBodyRecord::PendingAcquisition("
                    + N + "CorpusBodyPendingAcquisitionReason) -> " + N + "CorpusBodyRecord",
            },
            ConstructionSurface.Of(typeof(CorpusBodyRecord)).ToArray(),
            "a fourth production path onto the body record must be justified in review");
    }

    /// <summary>
    /// Fix one of the peer reviewer verdict widened this constructor from six parameters to ten
    /// (the object ordinal plus the three added axis dispositions); this pin makes the next such
    /// widening a visible diff rather than a silent shape change.
    /// </summary>
    [TestMethod]
    public void RecordHasExactlyItsOwnPublicConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "CorpusRecord::.ctor(" + N + "CorpusRecord) -> "
                    + N + "CorpusRecord",
                "constructor public instance " + N + "CorpusRecord::.ctor(System.String, "
                    + Core + "SourceObjectRef, System.Int32, " + Scope + "ScopeDisposition, "
                    + Scope + "ScopeDisposition, " + Scope + "ScopeDisposition, "
                    + Scope + "ScopeDisposition, " + N + "CorpusBodyRecord, "
                    + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef) -> " + N + "CorpusRecord",
                "method public instance " + N + "CorpusRecord::<Clone>$() -> " + N + "CorpusRecord",
            },
            ConstructionSurface.Of(typeof(CorpusRecord)).ToArray());
    }

    [TestMethod]
    public void VerifiedRecordHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor internal instance " + N + "VerifiedCorpusRecord::.ctor("
                    + N + "CorpusRecord) -> " + N + "VerifiedCorpusRecord",
                "method public static " + N + "VerifiedCorpusRecord::ParseAndVerify("
                    + Core + "SourceArtifactRef, System.ReadOnlySpan<System.Byte>) -> "
                    + N + "VerifiedCorpusRecord",
            },
            ConstructionSurface.Of(typeof(VerifiedCorpusRecord)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                    typeof(VerifiedCorpusRecord).Assembly, typeof(VerifiedCorpusRecord), true)
                .ToArray(),
            "nothing else in Contracts may hand out a verified corpus record it did not verify");
    }

    [TestMethod]
    public void CanonicalWriterHasNoConstructionSurface()
    {
        // Every public member returns a digest string or writes bytes; none returns any of this
        // file's own types.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.Of(typeof(CorpusRecordCanonicalWriter)).ToArray());
    }

    [TestMethod]
    public void SetSchemaIdsHasNoConstructionSurface()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.Of(typeof(CorpusRecordSetSchemaIds)).ToArray());
    }

    [TestMethod]
    public void RecordSetHasExactlyItsOwnPublicConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "CorpusRecordSet::.ctor("
                    + N + "CorpusRecordSet) -> " + N + "CorpusRecordSet",
                "constructor public instance " + N + "CorpusRecordSet::.ctor(System.String, "
                    + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "CorpusRecord>) -> "
                    + N + "CorpusRecordSet",
                "method public instance " + N + "CorpusRecordSet::<Clone>$() -> " + N + "CorpusRecordSet",
            },
            ConstructionSurface.Of(typeof(CorpusRecordSet)).ToArray());
    }

    [TestMethod]
    public void VerifiedRecordSetHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor internal instance " + N + "VerifiedCorpusRecordSet::.ctor("
                    + N + "CorpusRecordSet) -> " + N + "VerifiedCorpusRecordSet",
                "method public static " + N + "VerifiedCorpusRecordSet::ParseAndVerify("
                    + Core + "SourceArtifactRef, System.ReadOnlySpan<System.Byte>) -> "
                    + N + "VerifiedCorpusRecordSet",
            },
            ConstructionSurface.Of(typeof(VerifiedCorpusRecordSet)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                    typeof(VerifiedCorpusRecordSet).Assembly, typeof(VerifiedCorpusRecordSet), true)
                .ToArray(),
            "nothing else in Contracts may hand out a verified corpus record set it did not verify");
    }

    [TestMethod]
    public void SetCanonicalWriterHasNoConstructionSurface()
    {
        // Every public member returns a digest string or writes bytes; none returns any of this
        // file's own types.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.Of(typeof(CorpusRecordSetCanonicalWriter)).ToArray());
    }
}
