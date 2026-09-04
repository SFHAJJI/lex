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
/// D1-06b item 4: the corpus/6 record SET's own contract -- a durable, canonically written envelope
/// around every <see cref="CorpusRecord"/> one run produces, so <c>CorpusRecordSetWriter</c>
/// (Lex.V3.Ingest) custody-writes and reopens a whole run's output as one artifact rather than one
/// artifact per object. Shaped exactly like <see cref="CorpusRecordTests"/>'s own coverage of
/// <see cref="CorpusRecord"/>: schema, internal consistency, an independently pinned digest, and the
/// <see cref="VerifiedCorpusRecordSet.ParseAndVerify"/> reader door.
/// </summary>
[TestClass]
public sealed class CorpusRecordSetTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The exact digest for <see cref="Fixture"/>'s canonical bytes, pinned as a literal. Derived
    /// independently of this codebase's own hashing the same way <c>CorpusRecordTests</c>'s own
    /// fixture digests are: the fixture's canonical JSON was printed once (a throwaway probe,
    /// discarded, not part of this change), then its domain-prefixed SHA-256 (the fixed ASCII domain
    /// <c>"lex-v3-source-corpus-record-set/1\n"</c> followed by the exact printed bytes, trailing
    /// newline included) was computed a second time through .NET's raw
    /// <c>System.Security.Cryptography.SHA256</c> API from a separate PowerShell process, never by
    /// calling <see cref="CorpusRecordSetCanonicalWriter"/> and asserting its own answer equals
    /// itself.
    /// </summary>
    /// <remarks>
    /// Re-pinned by fold-in (b) of the D1-06b corpus/6 record set verdict: <c>ObjectRef</c> below
    /// used to return the identical object regardless of ordinal, so <see cref="Fixture"/>'s own two
    /// records accidentally named the same object. Parametrizing it so each ordinal gets its own
    /// object changed <see cref="Fixture"/>'s canonical bytes, so this digest was recomputed by the
    /// same independent print-then-transcribe method described above, from the new bytes.
    /// </remarks>
    private const string FixtureDigest =
        "482a46a9739368ebaecf1ec5248a01a4f4dc93162f6d682e4d2a54d5e2498ff4";

    [TestMethod]
    public void ConstructorRequiresItsOwnSchema()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecordSet(
            "wrong-schema/1", ManifestRef(), RunIdentity(), Array.Empty<CorpusRecord>()));
    }

    [TestMethod]
    public void ConstructorRejectsNullFields()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, null!, RunIdentity(), Array.Empty<CorpusRecord>()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, ManifestRef(), null!, Array.Empty<CorpusRecord>()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, ManifestRef(), RunIdentity(), null!));
    }

    [TestMethod]
    public void ConstructorAcceptsAnEmptySet()
    {
        // Emptiness is not refused: a manifest that observed zero objects is a legitimate run.
        var set = new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, ManifestRef(), RunIdentity(), Array.Empty<CorpusRecord>());
        Assert.AreEqual(0, set.Records.Count);
    }

    [TestMethod]
    public void ConstructorRejectsARecordWhoseManifestRefDisagrees()
    {
        var wrongManifestRef = new SourceArtifactRef(
            "urn:uuid:eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee", new string('f', 64));
        var record = new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(0),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            wrongManifestRef,
            RunIdentity());

        var exception = Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, ManifestRef(), RunIdentity(), new[] { record }));
        StringAssert.Contains(exception.Message, "the set's own manifest reference");
    }

    [TestMethod]
    public void ConstructorRejectsARecordWhoseRunIdentityDisagrees()
    {
        var wrongRunIdentity = new SourceArtifactRef(
            "urn:uuid:dddddddd-dddd-4ddd-8ddd-dddddddddddd", new string('9', 64));
        var record = new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(0),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            ManifestRef(),
            wrongRunIdentity);

        var exception = Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, ManifestRef(), RunIdentity(), new[] { record }));
        StringAssert.Contains(exception.Message, "the set's own run identity");
    }

    [TestMethod]
    public void ConstructorRejectsOutOfOrderOrDuplicateOrdinals()
    {
        var first = RecordAt(0);
        var second = RecordAt(1);

        var outOfOrder = Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, ManifestRef(), RunIdentity(), new[] { second, first }));
        StringAssert.Contains(outOfOrder.Message, "strictly ordered by object ordinal");

        var duplicate = Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, ManifestRef(), RunIdentity(), new[] { first, first }));
        StringAssert.Contains(duplicate.Message, "strictly ordered by object ordinal");
    }

    /// <summary>
    /// Fold-in (b) of the D1-06b corpus/6 record set verdict: the ordinal check above refuses two
    /// rows at the SAME ordinal, but says nothing about one object appearing twice under two
    /// DIFFERENT ordinals. This builds two genuinely distinct <see cref="CorpusRecord"/> instances
    /// (ordinal 0 and ordinal 1, so the ordinal-uniqueness check does not fire first) that both name
    /// the same <see cref="CorpusRecord.ObjectRef"/>, and requires the constructor's own refusal
    /// message to name the actual cause (the duplicate object), not the ordinal message above.
    /// </summary>
    [TestMethod]
    public void ConstructorRejectsTwoRecordsNamingTheSameObjectRef()
    {
        var sharedObjectRef = ObjectRef(0);
        var first = new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            sharedObjectRef,
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            ManifestRef(),
            RunIdentity());
        var second = new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            sharedObjectRef,
            1,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            ManifestRef(),
            RunIdentity());

        var exception = Assert.ThrowsExactly<ArgumentException>(() => new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, ManifestRef(), RunIdentity(), new[] { first, second }));
        StringAssert.Contains(exception.Message, "cannot name the same object twice");
    }

    [TestMethod]
    public void CanonicalWriterReproducesTheIndependentlyDerivedDigestForTheFixture()
    {
        var set = Fixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordSetCanonicalWriter.Write(buffer, set);
        Assert.AreEqual(FixtureDigest, digest);
    }

    [TestMethod]
    public void WriteRejectsNullArgumentsAndAnUnwritableDestination()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => CorpusRecordSetCanonicalWriter.Write(null!, Fixture()));
        using var buffer = new MemoryStream();
        Assert.ThrowsExactly<ArgumentNullException>(
            () => CorpusRecordSetCanonicalWriter.Write(buffer, null!));

        using var readOnly = new MemoryStream(Array.Empty<byte>(), writable: false);
        Assert.ThrowsExactly<ArgumentException>(
            () => CorpusRecordSetCanonicalWriter.Write(readOnly, Fixture()));
    }

    [TestMethod]
    public void ParseAndVerifyRoundTripsTheFixture()
    {
        var set = Fixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordSetCanonicalWriter.Write(buffer, set);
        var bytes = buffer.ToArray();
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", digest);

        var verified = VerifiedCorpusRecordSet.ParseAndVerify(artifactRef, bytes);

        Assert.AreEqual(set.Records.Count, verified.Set.Records.Count);
        Assert.AreEqual(set.ManifestRef, verified.Set.ManifestRef);
        Assert.AreEqual(set.RunIdentity, verified.Set.RunIdentity);
        for (var index = 0; index < set.Records.Count; index++)
        {
            Assert.AreEqual(
                set.Records[index].ObjectOrdinal, verified.Set.Records[index].ObjectOrdinal);
            Assert.AreEqual(
                set.Records[index].Body.Kind, verified.Set.Records[index].Body.Kind);
        }
    }

    [TestMethod]
    public void ParseAndVerifyRefusesBytesThatDoNotMatchTheArtifactReference()
    {
        var set = Fixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordSetCanonicalWriter.Write(buffer, set);
        var bytes = buffer.ToArray();

        var wrongDigest = new string(digest[0] == '0' ? '1' : '0', 64);
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:bbbbbbb1-bbbb-4bbb-8bbb-bbbbbbbbbbb1", wrongDigest);

        Assert.ThrowsExactly<ArgumentException>(
            () => VerifiedCorpusRecordSet.ParseAndVerify(artifactRef, bytes));
    }

    [TestMethod]
    public void ParseAndVerifyRefusesTamperedBytesEvenWhenTheArtifactRefIsTheOriginalDigest()
    {
        var set = Fixture();
        using var buffer = new MemoryStream();
        var digest = CorpusRecordSetCanonicalWriter.Write(buffer, set);
        var bytes = buffer.ToArray();
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:bbbbbbb2-bbbb-4bbb-8bbb-bbbbbbbbbbb2", digest);

        var tampered = (byte[])bytes.Clone();
        tampered[10] ^= 0xFF;

        Assert.ThrowsExactly<ArgumentException>(
            () => VerifiedCorpusRecordSet.ParseAndVerify(artifactRef, tampered));
    }

    /// <summary>
    /// Mirrors <c>CorpusRecordTests.ParseAndVerifyRefusesValidJsonThatIsNotCanonicallyOrdered</c>:
    /// the final <c>SequenceEqual</c> guard, distinct from the digest check above, needs its own
    /// bytes that still match their own recomputed digest but are not canonically ordered.
    /// </summary>
    [TestMethod]
    public void ParseAndVerifyRefusesValidJsonThatIsNotCanonicallyOrdered()
    {
        var set = Fixture();
        using var buffer = new MemoryStream();
        CorpusRecordSetCanonicalWriter.Write(buffer, set);
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

        var digest = CorpusRecordSetCanonicalWriter.ComputeSetSha256(mutatedBytes);
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:bbbbbbb3-bbbb-4bbb-8bbb-bbbbbbbbbbb3", digest);

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => VerifiedCorpusRecordSet.ParseAndVerify(artifactRef, mutatedBytes));
        StringAssert.Contains(
            exception.Message,
            "The corpus record set is not its exact canonical typed representation.");
        Assert.AreEqual("canonicalBytes", exception.ParamName);
    }

    private static CorpusRecordSet Fixture() => new(
        CorpusRecordSetSchemaIds.Set, ManifestRef(), RunIdentity(), new[] { RecordAt(0), RecordAt(1) });

    private static CorpusRecord RecordAt(int ordinal) => ordinal == 0
        ? new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(0),
            0,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.TypedQuarantine,
            ScopeDisposition.Point,
            ScopeDisposition.NeverIngest,
            CorpusBodyRecord.NotHeld(ScopeDisposition.TypedQuarantine),
            ManifestRef(),
            RunIdentity())
        : new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            ObjectRef(1),
            1,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.Held(FlooredReceipt()),
            ManifestRef(),
            RunIdentity());

    // Fold-in (b) of the D1-06b corpus/6 record set verdict: this used to return the identical
    // SourceObjectRef regardless of ordinal, so Fixture()'s own two records (RecordAt(0) and
    // RecordAt(1)) accidentally named the same object -- invisible before CorpusRecordSet's
    // constructor gained its own duplicate-ObjectRef refusal, since nothing else in Fixture()'s own
    // tests checked object identity across records. Parametrized so every ordinal this test file
    // builds gets its own distinct object.
    private static SourceObjectRef ObjectRef(int ordinal) => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        new SourceRegistryMemberRef(
            new SourceArtifactRef(
                "urn:uuid:11111111-1111-4111-8111-111111111111", new string('a', 64)),
            "eu_consolidation_root"),
        $"https://publications.europa.eu/resource/celex/32019L000{ordinal}",
        $"eu-consolidation-root:example-{ordinal}",
        CanonicalKeySha256($"eu-consolidation-root:example-{ordinal}"),
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
            Guid.Parse("00000000-0000-0000-0000-000000000051"),
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
