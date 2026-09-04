using System.Reflection;
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
/// pinned digest, and the <see cref="VerifiedCorpusRecord.ParseAndVerify"/> reader door), and
/// precision three (the closed "no body held" reason set, one fixture per reason, plus a held-body
/// fixture with a real floored receipt).
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
    /// asserting its own answer equals itself. Both computations agreed on
    /// <c>c650c9261ce390006115a26c07032591bbcc3479e4e1e460f66b58157534075f</c>; this test pins that
    /// agreement so a future change to the writer's field order, encoding, or domain prefix fails
    /// here rather than silently shipping a different wire byte sequence under the same schema id.
    /// </summary>
    private const string NotHeldFixtureDigest =
        "c650c9261ce390006115a26c07032591bbcc3479e4e1e460f66b58157534075f";

    /// <summary>
    /// The exact digest for <see cref="HeldFixture"/>'s canonical bytes (the floored receipt),
    /// derived the same independent way as <see cref="NotHeldFixtureDigest"/>.
    /// </summary>
    private const string HeldFixtureDigest =
        "49639de4f406558c10637cb4719ba5d2c69d3aaac7fc1f3de5d9ebaf3e566a61";

    [TestMethod]
    public void RecordDisposesReusesScopeDispositionAndSourceObjectRefDirectly()
    {
        // Precision one, pinned structurally rather than only by example: a future change that
        // wrapped either field in a corpus-specific type would fail here even if every other test
        // in this file still passed against the wrapper's own equivalent shape.
        var recordType = typeof(CorpusRecord);
        Assert.AreEqual(
            typeof(SourceObjectRef),
            recordType.GetProperty(nameof(CorpusRecord.ObjectRef))!.PropertyType);
        Assert.AreEqual(
            typeof(ScopeDisposition),
            recordType.GetProperty(nameof(CorpusRecord.RecordDisposition))!.PropertyType);

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
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.Point),
            ManifestRef(),
            RunIdentity()));
    }

    [TestMethod]
    public void ConstructorRejectsNullFields()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            null!,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.Point),
            ManifestRef(),
            RunIdentity()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            ScopeDisposition.AcceptedSelected,
            null!,
            ManifestRef(),
            RunIdentity()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.Point),
            null!,
            RunIdentity()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(),
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.Point),
            ManifestRef(),
            null!));
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

        var point = CorpusBodyRecord.NotHeld(ScopeDisposition.Point);
        Assert.AreEqual(ScopeDisposition.Point, point.NotHeldReason);

        var neverIngest = CorpusBodyRecord.NotHeld(ScopeDisposition.NeverIngest);
        Assert.AreEqual(ScopeDisposition.NeverIngest, neverIngest.NotHeldReason);

        Assert.ThrowsExactly<ArgumentException>(
            () => CorpusBodyRecord.NotHeld(ScopeDisposition.AcceptedSelected));
    }

    [TestMethod]
    public void NotHeldRejectsAReceiptOrAFloor()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.NotHeld, FlooredReceipt(), null, ScopeDisposition.Point));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.NotHeld, null, CustodyMembership.Floored, ScopeDisposition.Point));
    }

    [TestMethod]
    public void HeldRejectsAMissingReceiptOrANotHeldReason()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.Held, null, CustodyMembership.Floored, null));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.Held, FlooredReceipt(), CustodyMembership.Floored,
            ScopeDisposition.Point));
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

        // A caller-asserted floor that disagrees with what the receipt itself proves is refused,
        // not silently accepted: the floor is recomputed by CustodyMembershipClassifier, never
        // trusted from the caller. This is also why CustodyMembership.ReadOnce can never reach a
        // held body record here: the classifier itself never answers that value for a real
        // receipt (Decision 71; CustodyMembershipClassifier's own remarks), and this constructor
        // accepts only the value the classifier actually computed.
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.Held, receipt, CustodyMembership.RetainedUnenforced, null));
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusBodyRecord(
            CorpusBodyRecordKind.Held, receipt, CustodyMembership.ReadOnce, null));
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
        Assert.AreEqual(record.RecordDisposition, roundTripped.RecordDisposition);
        Assert.AreEqual(record.Body.Kind, roundTripped.Body.Kind);
        Assert.AreEqual(record.Body.Floor, roundTripped.Body.Floor);
        Assert.AreEqual(
            record.Body.Receipt!.PolicyEvidence.Protection,
            roundTripped.Body.Receipt!.PolicyEvidence.Protection);
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

    private static CorpusRecord NotHeldFixture() => new(
        CorpusRecordSchemaIds.Record,
        ObjectRef(),
        ScopeDisposition.AcceptedSelected,
        CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
        ManifestRef(),
        RunIdentity());

    private static CorpusRecord HeldFixture() => new(
        CorpusRecordSchemaIds.Record,
        ObjectRef(),
        ScopeDisposition.AcceptedSelected,
        CorpusBodyRecord.Held(FlooredReceipt()),
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
