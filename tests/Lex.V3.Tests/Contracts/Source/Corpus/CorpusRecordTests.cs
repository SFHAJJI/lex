using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Tests.Contracts.Source.Corpus;

/// <summary>
/// D1-06a: the corpus/6 record contract, fixtures only. Covers precision one (direct reuse of
/// <see cref="ScopeDisposition"/> and <see cref="SourceObjectRef"/>, no corpus-specific
/// vocabulary), precision two (the wire form: schema id, canonical writer, an independently
/// pinned digest, and the <see cref="VerifiedCorpusRecord.ParseAndVerify"/> reader door), precision
/// three (the closed "no body held" reason set, one fixture per reason, plus a held-body fixture
/// with a real floored receipt), and the peer reviewer verdict's three required fixes (event
/// <c>lex-event-20260904T071246618Z-2d4ca939f7144ea5ac3fd4c421091154</c>): the record now carries
/// all four of the manifest's axis dispositions plus the row ordinal (fix one), a body's not-held
/// reason must agree with the carried body-axis disposition (fix two), and an accepted body pending
/// acquisition is a modelled, typed state (fix three).
/// </summary>
[TestClass]
public sealed class CorpusRecordTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The exact digest for <see cref="NotHeldFixture"/>'s canonical bytes, pinned as a literal.
    /// Derived independently of this codebase's own hashing: the fixture's canonical JSON was
    /// printed once (a throwaway probe, discarded, not part of this change), then its
    /// domain-prefixed SHA-256 (the fixed ASCII domain <c>"lex-v3-source-corpus-record/6\n"</c>
    /// followed by the exact printed bytes, trailing newline included) was computed a second time
    /// through .NET's raw <c>System.Security.Cryptography.SHA256</c> API from a separate
    /// PowerShell process, never by calling <see cref="CorpusRecordCanonicalWriter"/> and
    /// asserting its own answer equals itself. Both computations agreed on this value; this test
    /// pins that agreement so a future change to the writer's field order, encoding, or domain
    /// prefix fails here rather than silently shipping a different wire byte sequence under the
    /// same schema id.
    /// </summary>
    private const string NotHeldFixtureDigest =
        "189e3d84bbdc5bd50d0f29942b96a022ac86a8410aaee1b22e750694e290244e";

    /// <summary>
    /// The exact digest for <see cref="HeldFixture"/>'s canonical bytes (the floored receipt),
    /// derived the same independent way as <see cref="NotHeldFixtureDigest"/>.
    /// </summary>
    private const string HeldFixtureDigest =
        "a2c6fdd6afb82474e72109fe858daee39257bbb0193e1b79cc51ce42be3d2fcc";

    /// <summary>
    /// The exact digest for <see cref="PendingAcquisitionFixture"/>'s canonical bytes, derived the
    /// same independent way as <see cref="NotHeldFixtureDigest"/>.
    /// </summary>
    private const string PendingAcquisitionFixtureDigest =
        "de8f3463fd7cf774764192f3d66bb21a8189b320957cfcd8660d2afd9541bd0e";

    [TestMethod]
    public void RecordDisposesReusesScopeDispositionAndSourceObjectRefDirectly()
    {
        // Precision one, pinned structurally rather than only by example: a future change that
        // wrapped any of these fields in a corpus-specific type would fail here even if every other
        // test in this file still passed against the wrapper's own equivalent shape.
        var recordType = typeof(CorpusRecord);
        Assert.AreEqual(
            typeof(SourceObjectRef),
            recordType.GetProperty(nameof(CorpusRecord.ObjectRef))!.PropertyType);
        Assert.AreEqual(
            typeof(int),
            recordType.GetProperty(nameof(CorpusRecord.ObjectOrdinal))!.PropertyType);
        Assert.AreEqual(
            typeof(ScopeDisposition),
            recordType.GetProperty(nameof(CorpusRecord.RecordDisposition))!.PropertyType);
        Assert.AreEqual(
            typeof(ScopeDisposition),
            recordType.GetProperty(nameof(CorpusRecord.BodyDisposition))!.PropertyType);
        Assert.AreEqual(
            typeof(ScopeDisposition),
            recordType.GetProperty(nameof(CorpusRecord.RelationDisposition))!.PropertyType);
        Assert.AreEqual(
            typeof(ScopeDisposition),
            recordType.GetProperty(nameof(CorpusRecord.SupportingDocumentDisposition))!.PropertyType);

        var bodyType = typeof(CorpusBodyRecord);
        var notHeldReasonType = bodyType.GetProperty(nameof(CorpusBodyRecord.NotHeldReason))!.PropertyType;
        Assert.AreEqual(typeof(ScopeDisposition?), notHeldReasonType);
    }

    [TestMethod]
    public void ConstructorRequiresItsOwnSchema()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecord(
            "wrong-schema/1",
            ObjectRef(),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            ManifestRef(),
            RunIdentity()));
    }

    [TestMethod]
    public void ConstructorRejectsNullFields()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            null!,
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            ManifestRef(),
            RunIdentity()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            null!,
            ManifestRef(),
            RunIdentity()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            null!,
            RunIdentity()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            ManifestRef(),
            null!));
    }

    [TestMethod]
    public void ConstructorRejectsANegativeObjectOrdinal()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            -1,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            ManifestRef(),
            RunIdentity()));
    }

    /// <summary>
    /// Fix two of the reviewer verdict: a body's not-held reason must be exactly the carried
    /// <see cref="CorpusRecord.BodyDisposition"/>, never an independently supplied value that
    /// disagrees with it. This is the defect named in the verdict verbatim: "a record can say Point
    /// where the manifest's body axis says TypedQuarantine."
    /// </summary>
    [TestMethod]
    public void ConstructorRefusesANotHeldBodyWhoseReasonDisagreesWithTheCarriedBodyDisposition()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.Point),
            ManifestRef(),
            RunIdentity()));
        StringAssert.Contains(
            exception.Message,
            "must carry a not-held body whose reason is exactly that disposition");
        Assert.AreEqual("body", exception.ParamName);
    }

    [TestMethod]
    public void ConstructorRefusesAHeldBodyWhenTheBodyAxisIsNotAcceptedSelected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.Point,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.Held(FlooredReceipt()),
            ManifestRef(),
            RunIdentity()));
    }

    [TestMethod]
    public void ConstructorRefusesANotHeldBodyWhenTheBodyAxisIsAcceptedSelected()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.Point),
            ManifestRef(),
            RunIdentity()));
        StringAssert.Contains(
            exception.Message,
            "accepted_selected cannot carry a not-held body");
    }

    [TestMethod]
    public void ConstructorAcceptsAPendingAcquisitionBodyWhenTheBodyAxisIsAcceptedSelected()
    {
        var record = new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.PendingAcquisition(CorpusBodyPendingAcquisitionReason.NotYetAcquired()),
            ManifestRef(),
            RunIdentity());

        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, record.Body.Kind);
    }

    /// <summary>
    /// Precision three: one fixture per member of the closed "no body held" reason set, built
    /// straight from what the two merged adapters actually classify a body's exclusion as today
    /// (<c>EuScopeProfile.ReduceBody</c>'s four contributions each resolve to one of these three;
    /// the shared <c>scope/1</c> body axis both publishers write into admits no fourth value).
    /// <see cref="ScopeDisposition.AcceptedSelected"/> is refused: it is the one value under which
    /// a body might actually be held, never a "why not" reason.
    /// </summary>
    [TestMethod]
    public void NotHeldReasonsAreExactlyTheManifestsThreeNonAcceptedBodyDispositions()
    {
        var quarantine = CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine);
        Assert.AreEqual(CorpusBodyRecordKind.NotHeld, quarantine.Kind);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, quarantine.NotHeldReason);
        Assert.IsNull(quarantine.Receipt);
        Assert.IsNull(quarantine.Floor);
        Assert.IsNull(quarantine.PendingAcquisitionReason);

        var point = CorpusBodyRecord.NotHeld(ScopeDisposition.Point);
        Assert.AreEqual(ScopeDisposition.Point, point.NotHeldReason);

        var neverIngest = CorpusBodyRecord.NotHeld(ScopeDisposition.NeverIngest);
        Assert.AreEqual(ScopeDisposition.NeverIngest, neverIngest.NotHeldReason);

        Assert.ThrowsExactly<ArgumentException>(
            () => CorpusBodyRecord.NotHeld(ScopeDisposition.AcceptedSelected));
    }

    [TestMethod]
    public void NotHeldRejectsAReceiptOrAFloorOrAPendingAcquisitionReason()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.NotHeld, FlooredReceipt(), null, ScopeDisposition.Point, null));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.NotHeld, null, CustodyMembership.Floored, ScopeDisposition.Point, null));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.NotHeld,
            null,
            null,
            ScopeDisposition.Point,
            CorpusBodyPendingAcquisitionReason.NotYetAcquired()));
    }

    [TestMethod]
    public void HeldRejectsAMissingReceiptOrANotHeldReasonOrAPendingAcquisitionReason()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.Held, null, CustodyMembership.Floored, null, null));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.Held, FlooredReceipt(), CustodyMembership.Floored,
            ScopeDisposition.Point, null));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.Held,
            FlooredReceipt(),
            CustodyMembership.Floored,
            null,
            CorpusBodyPendingAcquisitionReason.NotYetAcquired()));
    }

    /// <summary>
    /// Fix three of the reviewer verdict: the state every accepted object passes through in D1-06b
    /// whenever the fetch has not happened or was refused, distinguished by a typed reason.
    /// </summary>
    [TestMethod]
    public void PendingAcquisitionReasonIsAnExactVariant()
    {
        var notYetAcquired = CorpusBodyPendingAcquisitionReason.NotYetAcquired();
        Assert.AreEqual(CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired, notYetAcquired.Kind);
        Assert.IsNull(notYetAcquired.Refusal);

        var refused = CorpusBodyPendingAcquisitionReason.AcquisitionRefused("dns_lookup_failed");
        Assert.AreEqual(CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused, refused.Kind);
        Assert.AreEqual("dns_lookup_failed", refused.Refusal);

        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyPendingAcquisitionReason(
            CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired, "unexpected"));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyPendingAcquisitionReason(
            CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused, null));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyPendingAcquisitionReason(
            CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused, "   "));
    }

    [TestMethod]
    public void PendingAcquisitionBodyRejectsAReceiptOrAFloorOrANotHeldReasonAndRequiresItsOwnReason()
    {
        var reason = CorpusBodyPendingAcquisitionReason.NotYetAcquired();
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.PendingAcquisition, FlooredReceipt(), null, null, reason));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.PendingAcquisition, null, CustodyMembership.Floored, null, reason));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.PendingAcquisition, null, null, ScopeDisposition.Point, reason));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.PendingAcquisition, null, null, null, null));
    }

    [TestMethod]
    public void PendingAcquisitionFactoryBuildsBothReasonShapes()
    {
        var notYetAcquired = CorpusBodyRecord.PendingAcquisition(
            CorpusBodyPendingAcquisitionReason.NotYetAcquired());
        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, notYetAcquired.Kind);
        Assert.IsNull(notYetAcquired.Receipt);
        Assert.IsNull(notYetAcquired.Floor);
        Assert.IsNull(notYetAcquired.NotHeldReason);
        Assert.AreEqual(
            CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired,
            notYetAcquired.PendingAcquisitionReason!.Kind);

        var refused = CorpusBodyRecord.PendingAcquisition(
            CorpusBodyPendingAcquisitionReason.AcquisitionRefused("connection_reset"));
        Assert.AreEqual(
            CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused,
            refused.PendingAcquisitionReason!.Kind);
        Assert.AreEqual("connection_reset", refused.PendingAcquisitionReason!.Refusal);

        Assert.ThrowsExactly<ArgumentNullException>(
            () => CorpusBodyRecord.PendingAcquisition(null!));
    }

    /// <summary>
    /// The held-body fixture: a real floored receipt (Decision 71's <c>LockedTime</c> meeting the
    /// ninety-day nightly floor), proving the held shape is constructible and that its own floor is
    /// derived from the receipt, never independently asserted.
    /// </summary>
    [TestMethod]
    public void HeldBodyDerivesItsFloorFromItsOwnReceiptRatherThanAcceptingAnAssertedOne()
    {
        var receipt = FlooredReceipt();
        var held = CorpusBodyRecord.Held(receipt);

        Assert.AreEqual(CorpusBodyRecordKind.Held, held.Kind);
        Assert.AreSame(receipt, held.Receipt);
        Assert.AreEqual(CustodyMembership.Floored, held.Floor);
        Assert.IsNull(held.NotHeldReason);
        Assert.IsNull(held.PendingAcquisitionReason);

        // A caller-asserted floor that disagrees with what the receipt itself proves is refused,
        // not silently accepted: the floor is recomputed by CustodyMembershipClassifier, never
        // trusted from the caller. This is also why CustodyMembership.ReadOnce can never reach a
        // held body record here: the classifier itself never answers that value for a real
        // receipt (Decision 71; CustodyMembershipClassifier's own remarks), and this constructor
        // accepts only the value the classifier actually computed.
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.Held, receipt, CustodyMembership.RetainedUnenforced, null, null));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.Held, receipt, CustodyMembership.ReadOnce, null, null));
    }

    /// <summary>
    /// The other reachable floor: a receipt whose store enforces no protection classifies as
    /// retained-but-unenforced (Decision 71's second member), proving the held shape says which of
    /// the three a receipt actually is rather than always reporting the same answer.
    /// </summary>
    [TestMethod]
    public void HeldBodyCanAlsoCarryARetainedUnenforcedReceipt()
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, new string('9', 64), 4, CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            policyKey: null,
            CustodyProtection.NotEnforced,
            ObservedAt,
            protectedUntil: null);
        var receipt = new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);

        var held = CorpusBodyRecord.Held(receipt);
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, held.Floor);
    }

    [TestMethod]
    public void CanonicalWriterReproducesTheIndependentlyDerivedDigestForTheNotHeldFixture()
    {
        var record = NotHeldFixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordCanonicalWriter.Write(buffer, record);
        Assert.AreEqual(NotHeldFixtureDigest, digest);
    }

    [TestMethod]
    public void CanonicalWriterReproducesTheIndependentlyDerivedDigestForTheHeldFixture()
    {
        var record = HeldFixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordCanonicalWriter.Write(buffer, record);
        Assert.AreEqual(HeldFixtureDigest, digest);
    }

    [TestMethod]
    public void CanonicalWriterReproducesTheIndependentlyDerivedDigestForThePendingAcquisitionFixture()
    {
        var record = PendingAcquisitionFixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordCanonicalWriter.Write(buffer, record);
        Assert.AreEqual(PendingAcquisitionFixtureDigest, digest);
    }

    /// <summary>
    /// A note carried in the peer reviewer verdict: the wire string literals are not pinned as
    /// literal assertions anywhere, only exercised through round trips, so a future accidental
    /// rename of one would only be caught if the corresponding round-trip test happened to compare
    /// the renamed value against itself. This test parses the raw canonical bytes and asserts the
    /// exact literal string this codebase's wire format actually uses for each disposition, each
    /// body kind, each floor and each pending-acquisition reason kind it can produce.
    /// </summary>
    [TestMethod]
    public void CanonicalWireStringsAreExactlyThePinnedLiterals()
    {
        var notHeldNode = CanonicalNode(NotHeldFixture());
        Assert.AreEqual("lex-v3-source-corpus-record/6", notHeldNode["schema"]!.GetValue<string>());
        Assert.AreEqual("accepted_selected", notHeldNode["record_disposition"]!.GetValue<string>());
        Assert.AreEqual("typed_quarantine", notHeldNode["body_disposition"]!.GetValue<string>());
        Assert.AreEqual("point", notHeldNode["relation_disposition"]!.GetValue<string>());
        Assert.AreEqual(
            "never_ingest", notHeldNode["supporting_document_disposition"]!.GetValue<string>());
        var notHeldBody = notHeldNode["body"]!.AsObject();
        Assert.AreEqual("not_held", notHeldBody["kind"]!.GetValue<string>());
        Assert.AreEqual("typed_quarantine", notHeldBody["not_held_reason"]!.GetValue<string>());
        Assert.IsNull(notHeldBody["pending_acquisition_reason"]);

        var heldNode = CanonicalNode(HeldFixture());
        var heldBody = heldNode["body"]!.AsObject();
        Assert.AreEqual("held", heldBody["kind"]!.GetValue<string>());
        Assert.AreEqual("floored", heldBody["floor"]!.GetValue<string>());
        Assert.AreEqual(
            "typed_quarantine",
            heldNode["supporting_document_disposition"]!.GetValue<string>());

        var pendingNode = CanonicalNode(PendingAcquisitionFixture());
        var pendingBody = pendingNode["body"]!.AsObject();
        Assert.AreEqual("pending_acquisition", pendingBody["kind"]!.GetValue<string>());
        var pendingReason = pendingBody["pending_acquisition_reason"]!.AsObject();
        Assert.AreEqual("not_yet_acquired", pendingReason["kind"]!.GetValue<string>());
        Assert.IsNull(pendingReason["refusal"]);
        Assert.AreEqual(
            "never_ingest", pendingNode["relation_disposition"]!.GetValue<string>());
        Assert.AreEqual(
            "point", pendingNode["supporting_document_disposition"]!.GetValue<string>());

        var refusedRecord = new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            3,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.PendingAcquisition(
                CorpusBodyPendingAcquisitionReason.AcquisitionRefused("connection_reset")),
            ManifestRef(),
            RunIdentity());
        var refusedNode = CanonicalNode(refusedRecord);
        var refusedReason = refusedNode["body"]!.AsObject()["pending_acquisition_reason"]!.AsObject();
        Assert.AreEqual("acquisition_refused", refusedReason["kind"]!.GetValue<string>());
        Assert.AreEqual("connection_reset", refusedReason["refusal"]!.GetValue<string>());

        var retainedUnenforcedReceipt = new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, new string('9', 64), 4, CustodyClass.NightlyFloor90d),
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                new DurableBlobRef(
                    CustodySchemaIds.DurableBlobRef, new string('9', 64), 4, CustodyClass.NightlyFloor90d),
                CustodyVerificationProfile.FileSystemUnenforced1,
                policyKey: null,
                CustodyProtection.NotEnforced,
                ObservedAt,
                protectedUntil: null));
        var retainedRecord = new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            4,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.Held(retainedUnenforcedReceipt),
            ManifestRef(),
            RunIdentity());
        var retainedBody = CanonicalNode(retainedRecord)["body"]!.AsObject();
        Assert.AreEqual("retained_unenforced", retainedBody["floor"]!.GetValue<string>());
    }

    [TestMethod]
    public void WriteRejectsNullArgumentsAndAnUnwritableDestination()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => CorpusRecordCanonicalWriter.Write(null!, NotHeldFixture()));
        using var buffer = new MemoryStream();
        Assert.ThrowsExactly<ArgumentNullException>(
            () => CorpusRecordCanonicalWriter.Write(buffer, null!));

        using var readOnly = new MemoryStream(Array.Empty<byte>(), writable: false);
        Assert.ThrowsExactly<ArgumentException>(
            () => CorpusRecordCanonicalWriter.Write(readOnly, NotHeldFixture()));
    }

    [TestMethod]
    public void ParseAndVerifyRoundTripsTheNotHeldFixtureAndExposesItsReason()
    {
        var record = NotHeldFixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordCanonicalWriter.Write(buffer, record);
        var bytes = buffer.ToArray();
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:55555555-5555-4555-8555-555555555555", digest);

        var verified = VerifiedCorpusRecord.ParseAndVerify(artifactRef, bytes);

        Assert.AreEqual(record.ObjectOrdinal, verified.Record.ObjectOrdinal);
        Assert.AreEqual(record.BodyDisposition, verified.Record.BodyDisposition);
        Assert.AreEqual(record.RelationDisposition, verified.Record.RelationDisposition);
        Assert.AreEqual(
            record.SupportingDocumentDisposition, verified.Record.SupportingDocumentDisposition);
        Assert.AreEqual(CorpusBodyRecordKind.NotHeld, verified.Record.Body.Kind);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, verified.Record.Body.NotHeldReason);
        Assert.AreEqual(record.ObjectRef.CanonicalKey, verified.Record.ObjectRef.CanonicalKey);
        Assert.AreEqual(record.ManifestRef, verified.Record.ManifestRef);
        Assert.AreEqual(record.RunIdentity, verified.Record.RunIdentity);
    }

    [TestMethod]
    public void ParseAndVerifyRoundTripsTheHeldFixtureAndExposesItsFloor()
    {
        var record = HeldFixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordCanonicalWriter.Write(buffer, record);
        var bytes = buffer.ToArray();
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:66666666-6666-4666-8666-666666666666", digest);

        var verified = VerifiedCorpusRecord.ParseAndVerify(artifactRef, bytes);

        Assert.AreEqual(CorpusBodyRecordKind.Held, verified.Record.Body.Kind);
        Assert.AreEqual(CustodyMembership.Floored, verified.Record.Body.Floor);
        Assert.AreEqual(
            record.Body.Receipt!.Reference.ContentSha256,
            verified.Record.Body.Receipt!.Reference.ContentSha256);
    }

    [TestMethod]
    public void ParseAndVerifyRoundTripsThePendingAcquisitionFixtureAndExposesItsReason()
    {
        var record = PendingAcquisitionFixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordCanonicalWriter.Write(buffer, record);
        var bytes = buffer.ToArray();
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", digest);

        var verified = VerifiedCorpusRecord.ParseAndVerify(artifactRef, bytes);

        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, verified.Record.Body.Kind);
        Assert.IsNull(verified.Record.Body.Receipt);
        Assert.IsNull(verified.Record.Body.Floor);
        Assert.AreEqual(
            CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired,
            verified.Record.Body.PendingAcquisitionReason!.Kind);
    }

    [TestMethod]
    public void ParseAndVerifyRefusesBytesThatDoNotMatchTheArtifactReference()
    {
        var record = NotHeldFixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordCanonicalWriter.Write(buffer, record);
        var bytes = buffer.ToArray();

        var wrongDigest = new string(digest[0] == '0' ? '1' : '0', 64);
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:77777777-7777-4777-8777-777777777777", wrongDigest);

        Assert.ThrowsExactly<ArgumentException>(
            () => VerifiedCorpusRecord.ParseAndVerify(artifactRef, bytes));
    }

    [TestMethod]
    public void ParseAndVerifyRefusesTamperedBytesEvenWhenTheArtifactRefIsTheOriginalDigest()
    {
        var record = NotHeldFixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordCanonicalWriter.Write(buffer, record);
        var bytes = buffer.ToArray();
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:88888888-8888-4888-8888-888888888888", digest);

        var tampered = (byte[])bytes.Clone();
        tampered[10] ^= 0xFF;

        Assert.ThrowsExactly<ArgumentException>(
            () => VerifiedCorpusRecord.ParseAndVerify(artifactRef, tampered));
    }

    /// <summary>
    /// A note carried in the peer reviewer verdict: the final <c>SequenceEqual</c> guard in
    /// <see cref="VerifiedCorpusRecord.ParseAndVerify"/> (the canonical re-encode check, distinct
    /// from the digest check <see cref="ParseAndVerifyRefusesTamperedBytesEvenWhenTheArtifactRefIsTheOriginalDigest"/>
    /// exercises) had no test that actually drove it to its failure branch. Reordering a valid
    /// canonical document's top-level members produces bytes that still parse to the identical
    /// record (JSON object member order carries no meaning) and, recomputed for those exact
    /// reordered bytes, still match their own artifact reference -- so the digest check upstream
    /// passes, and only the canonical re-encode check can catch the drift.
    /// </summary>
    [TestMethod]
    public void ParseAndVerifyRefusesValidJsonThatIsNotCanonicallyOrdered()
    {
        var record = NotHeldFixture();
        using var buffer = new MemoryStream();
        CorpusRecordCanonicalWriter.Write(buffer, record);
        var canonicalBytes = buffer.ToArray();

        var node = JsonNode.Parse(Encoding.UTF8.GetString(canonicalBytes))!.AsObject();
        var reordered = new JsonObject();
        foreach (var pair in node.Reverse())
        {
            reordered.Add(pair.Key, pair.Value?.DeepClone());
        }

        var mutatedBytes = Encoding.UTF8.GetBytes(reordered.ToJsonString() + "\n");
        Assert.IsFalse(
            mutatedBytes.SequenceEqual(canonicalBytes),
            "the probe must actually reorder bytes, or this test proves nothing");

        var digest = CorpusRecordCanonicalWriter.ComputeRecordSha256(mutatedBytes);
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:99999999-9999-4999-8999-999999999999", digest);

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => VerifiedCorpusRecord.ParseAndVerify(artifactRef, mutatedBytes));
        StringAssert.Contains(
            exception.Message,
            "The corpus record is not its exact canonical typed representation.");
        Assert.AreEqual("canonicalBytes", exception.ParamName);
    }

    [TestMethod]
    public void ContractJsonRoundTripsARecordThroughReflectionBasedSerialization()
    {
        // Independent of the hand-rolled canonical writer: this proves the type's own JSON
        // constructor and property shape survive the general-purpose ContractJson path D1-06b
        // will also rely on for anything that is not the pinned canonical form.
        var record = HeldFixture();
        var json = ContractJson.Serialize(record);
        var roundTripped = ContractJson.Deserialize<CorpusRecord>(json);

        Assert.AreEqual(record.Schema, roundTripped.Schema);
        Assert.AreEqual(record.ObjectOrdinal, roundTripped.ObjectOrdinal);
        Assert.AreEqual(record.RecordDisposition, roundTripped.RecordDisposition);
        Assert.AreEqual(record.BodyDisposition, roundTripped.BodyDisposition);
        Assert.AreEqual(record.RelationDisposition, roundTripped.RelationDisposition);
        Assert.AreEqual(
            record.SupportingDocumentDisposition, roundTripped.SupportingDocumentDisposition);
        Assert.AreEqual(record.Body.Kind, roundTripped.Body.Kind);
        Assert.AreEqual(record.Body.Floor, roundTripped.Body.Floor);
        Assert.AreEqual(
            record.Body.Receipt!.PolicyEvidence.Protection,
            roundTripped.Body.Receipt!.PolicyEvidence.Protection);
    }

    [TestMethod]
    public void ContractJsonRoundTripsThePendingAcquisitionFixture()
    {
        var record = PendingAcquisitionFixture();
        var json = ContractJson.Serialize(record);
        var roundTripped = ContractJson.Deserialize<CorpusRecord>(json);

        Assert.AreEqual(record.Body.Kind, roundTripped.Body.Kind);
        Assert.AreEqual(
            record.Body.PendingAcquisitionReason!.Kind,
            roundTripped.Body.PendingAcquisitionReason!.Kind);
    }

    [TestMethod]
    public void ContractJsonRejectsAnUnmappedMember()
    {
        var record = NotHeldFixture();
        var json = ContractJson.Serialize(record);
        var withExtraMember = json[..^1] + ",\"unexpected_field\":true}";

        Assert.ThrowsExactly<System.Text.Json.JsonException>(
            () => ContractJson.Deserialize<CorpusRecord>(withExtraMember));
    }

    private static JsonObject CanonicalNode(CorpusRecord record)
    {
        using var buffer = new MemoryStream();
        CorpusRecordCanonicalWriter.Write(buffer, record);
        return JsonNode.Parse(Encoding.UTF8.GetString(buffer.ToArray()))!.AsObject();
    }

    private static CorpusRecord NotHeldFixture() => new(
        CorpusRecordSchemaIds.Record,
        ObjectRef(),
        0,
        ScopeDisposition.AcceptedSelected,
        ScopeDisposition.TypedQuarantine,
        ScopeDisposition.Point,
        ScopeDisposition.NeverIngest,
        CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
        ManifestRef(),
        RunIdentity());

    private static CorpusRecord HeldFixture() => new(
        CorpusRecordSchemaIds.Record,
        ObjectRef(),
        1,
        ScopeDisposition.AcceptedSelected,
        ScopeDisposition.AcceptedSelected,
        ScopeDisposition.AcceptedSelected,
        ScopeDisposition.TypedQuarantine,
        CorpusBodyRecord.Held(FlooredReceipt()),
        ManifestRef(),
        RunIdentity());

    private static CorpusRecord PendingAcquisitionFixture() => new(
        CorpusRecordSchemaIds.Record,
        ObjectRef(),
        2,
        ScopeDisposition.AcceptedSelected,
        ScopeDisposition.AcceptedSelected,
        ScopeDisposition.NeverIngest,
        ScopeDisposition.Point,
        CorpusBodyRecord.PendingAcquisition(CorpusBodyPendingAcquisitionReason.NotYetAcquired()),
        ManifestRef(),
        RunIdentity());

    private static SourceObjectRef ObjectRef() => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        new SourceRegistryMemberRef(
            new SourceArtifactRef(
                "urn:uuid:11111111-1111-4111-8111-111111111111", new string('a', 64)),
            "eu_consolidation_root"),
        "https://publications.europa.eu/resource/celex/32019L0001",
        "eu-consolidation-root:example",
        CanonicalKeySha256("eu-consolidation-root:example"),
        new SourceArtifactRef(
            "urn:uuid:22222222-2222-4222-8222-222222222222", new string('b', 64)),
        null);

    private static SourceArtifactRef ManifestRef() => new(
        "urn:uuid:33333333-3333-4333-8333-333333333333", new string('c', 64));

    private static SourceArtifactRef RunIdentity() => new(
        "urn:uuid:44444444-4444-4444-8444-444444444444", new string('d', 64));

    private static DurableBlobWriteReceipt FlooredReceipt()
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, new string('e', 64), 4096, CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.ImmutableObject1,
            Guid.Parse("00000000-0000-0000-0000-000000000050"),
            CustodyProtection.LockedTime,
            ObservedAt,
            ObservedAt.AddDays(91));
        return new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
    }

    private static string CanonicalKeySha256(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
