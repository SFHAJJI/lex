using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The premise behind every scope-reduction admission, retained as its own artifact so it outlives
/// the run that derived it.
/// </summary>
/// <remarks>
/// The expected canonical bytes below are written out by hand rather than produced by the writer
/// under test. A pin rendered from the thing it pins agrees with it by construction, which is the
/// one thing these bytes must not do: they are the wire form a later reader has to accept.
/// </remarks>
[TestClass]
public sealed class LuxembourgObservedObjectIdentitySetTests
{
    private static readonly SourceArtifactRef RunIdentity = new(
        "urn:uuid:11111111-1111-4111-8111-111111111111", new string('1', 64));

    private static readonly SourceArtifactRef ProfileRef = new(
        "urn:uuid:22222222-2222-4222-8222-222222222222", new string('2', 64));

    [TestMethod]
    public void TheCanonicalFormIsExactlyTheseBytes()
    {
        var set = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity, [Observation("https://data.legilux.lu/eli/a"), Observation("https://data.legilux.lu/eli/b")]);

        using var buffer = new MemoryStream();
        var digest = LuxembourgObservedObjectIdentitySetCanonicalWriter.Write(buffer, set);

        var expected =
            "{\"schema\":\"lex-v3-luxembourg-observed-object-identity-set/1\","
            + "\"run_identity\":{\"resource_id\":\"" + RunIdentity.ResourceId + "\","
            + "\"sha256\":\"" + RunIdentity.Sha256 + "\"},"
            + "\"object_ref_sha256\":[\"" + set.ObjectRefSha256Values[0] + "\",\""
            + set.ObjectRefSha256Values[1] + "\"]}\n";

        Assert.AreEqual(expected, Encoding.UTF8.GetString(buffer.ToArray()),
            "the canonical form is the wire a later reader must accept; its field names, order, "
                + "absence of whitespace and trailing newline are all part of it.");
        Assert.AreEqual(
            LuxembourgObservedObjectIdentitySetCanonicalWriter.ComputeSetSha256(
                Encoding.UTF8.GetBytes(expected)),
            digest,
            "Write must return the digest of the bytes it wrote.");
    }

    /// <summary>
    /// A set has no order. Two runs that observed the same objects in different orders state the
    /// same premise and so must produce the same artifact.
    /// </summary>
    [TestMethod]
    public void ObservationOrderDoesNotChangeTheSet()
    {
        var forward = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity, [Observation("https://data.legilux.lu/eli/a"), Observation("https://data.legilux.lu/eli/b")]);
        var reversed = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity, [Observation("https://data.legilux.lu/eli/b"), Observation("https://data.legilux.lu/eli/a")]);

        CollectionAssert.AreEqual(Canonical(forward), Canonical(reversed));
    }

    /// <summary>And no multiplicity: observing one object twice is observing one object.</summary>
    [TestMethod]
    public void ObservingOneObjectTwiceDoesNotChangeTheSet()
    {
        var once = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity, [Observation("https://data.legilux.lu/eli/a")]);
        var twice = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity,
            [Observation("https://data.legilux.lu/eli/a"), Observation("https://data.legilux.lu/eli/a")]);

        Assert.AreEqual(1, twice.ObjectRefSha256Values.Count);
        CollectionAssert.AreEqual(Canonical(once), Canonical(twice));
    }

    /// <summary>
    /// The converse, without which the two tests above would be satisfied by an artifact that
    /// ignored its input entirely: a different observed object must change the bytes.
    /// </summary>
    [TestMethod]
    public void ADifferentObservedObjectChangesTheSet()
    {
        var one = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity, [Observation("https://data.legilux.lu/eli/a")]);
        var other = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity, [Observation("https://data.legilux.lu/eli/c")]);

        CollectionAssert.AreNotEqual(Canonical(one), Canonical(other));
    }

    /// <summary>
    /// And the run binding is real: the same observed objects under a different run identity are a
    /// different artifact, so one run's premise cannot be presented as another's.
    /// </summary>
    [TestMethod]
    public void TheSameObjectsUnderAnotherRunAreADifferentSet()
    {
        var mine = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity, [Observation("https://data.legilux.lu/eli/a")]);
        var theirs = LuxembourgObservedObjectIdentitySet.FromObservations(
            new SourceArtifactRef("urn:uuid:33333333-3333-4333-8333-333333333333", new string('3', 64)),
            [Observation("https://data.legilux.lu/eli/a")]);

        CollectionAssert.AreNotEqual(Canonical(mine), Canonical(theirs));
    }

    /// <summary>
    /// Bytes that parse to the right set but are not the canonical form of it are refused rather
    /// than normalized. Here the array is descending, which no writer of this type emits.
    /// </summary>
    [TestMethod]
    public void BytesThatAreNotSortedAndDistinctAreRefused()
    {
        var set = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity, [Observation("https://data.legilux.lu/eli/a"), Observation("https://data.legilux.lu/eli/b")]);
        var descending =
            "{\"schema\":\"lex-v3-luxembourg-observed-object-identity-set/1\","
            + "\"run_identity\":{\"resource_id\":\"" + RunIdentity.ResourceId + "\","
            + "\"sha256\":\"" + RunIdentity.Sha256 + "\"},"
            + "\"object_ref_sha256\":[\"" + set.ObjectRefSha256Values[1] + "\",\""
            + set.ObjectRefSha256Values[0] + "\"]}\n";
        var bytes = Encoding.UTF8.GetBytes(descending);

        // The reference is the digest of these exact bytes, so the reference check passes and the
        // ordering rule is the only thing left that can refuse them.
        var setRef = new SourceArtifactRef(
            "urn:uuid:44444444-4444-4444-8444-444444444444",
            LuxembourgObservedObjectIdentitySetCanonicalWriter.ComputeSetSha256(bytes));

        var failure = Assert.ThrowsExactly<ArgumentException>(
            () => VerifiedLuxembourgObservedObjectIdentitySet.ParseAndVerify(setRef, bytes));
        StringAssert.Contains(failure.Message, "ordinal-ascending and distinct");
    }

    /// <summary>
    /// Bytes that parse to exactly the right set, in the right order, with no duplicates, and are
    /// still not the canonical form: one space, which no writer of this type emits. Only the round
    /// trip can refuse these. Without this case that guard is unproved -- every other input this
    /// suite offers is either canonical already or refused earlier by the ordering rule, so deleting
    /// the round trip changed nothing and no test noticed.
    /// </summary>
    [TestMethod]
    public void BytesThatParseCorrectlyButAreNotTheCanonicalFormAreRefused()
    {
        var set = LuxembourgObservedObjectIdentitySet.FromObservations(
            RunIdentity, [Observation("https://data.legilux.lu/eli/a"), Observation("https://data.legilux.lu/eli/b")]);
        var spaced =
            "{\"schema\": \"lex-v3-luxembourg-observed-object-identity-set/1\","
            + "\"run_identity\":{\"resource_id\":\"" + RunIdentity.ResourceId + "\","
            + "\"sha256\":\"" + RunIdentity.Sha256 + "\"},"
            + "\"object_ref_sha256\":[\"" + set.ObjectRefSha256Values[0] + "\",\""
            + set.ObjectRefSha256Values[1] + "\"]}\n";
        var bytes = Encoding.UTF8.GetBytes(spaced);

        // The reference is the digest of these exact bytes, so the digest check passes; the array is
        // ascending and distinct, so the ordering rule passes. The round trip is the only thing left.
        var setRef = new SourceArtifactRef(
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            LuxembourgObservedObjectIdentitySetCanonicalWriter.ComputeSetSha256(bytes));

        var failure = Assert.ThrowsExactly<ArgumentException>(
            () => VerifiedLuxembourgObservedObjectIdentitySet.ParseAndVerify(setRef, bytes));
        StringAssert.Contains(failure.Message, "not the canonical form of the set they parse into");
    }

    /// <summary>
    /// A set that is real, well formed, and another run's. The reader refuses it by name rather than
    /// handing back a premise belonging to a different execution. The artifact carries a RunIdentity
    /// precisely so this can be checked, and until the review that produced this test the reader
    /// never looked at it -- so any run's retained set could be presented as the basis of any other.
    /// </summary>
    [TestMethod]
    public async Task AnotherRunsRetainedSetIsRefusedByName()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var written = await new LuxembourgObservedObjectIdentitySetWriter(store).WriteAsync(
            RunIdentity,
            [Observation("https://data.legilux.lu/eli/a"), Observation("https://data.legilux.lu/eli/b")],
            CancellationToken.None);
        Assert.IsNull(written.Refusal, written.Refusal?.Detail);

        var anotherRun = new SourceArtifactRef(
            "urn:uuid:77777777-7777-4777-8777-777777777777", new string('7', 64));

        // The receipt and the reference are this set's own and both check out; the only thing wrong
        // is whose premise it is.
        var read = await new LuxembourgObservedObjectIdentitySetReader(store).ReadAsync(
            written.RetainedSetReceipt!, written.SetRef!, anotherRun, CancellationToken.None);

        Assert.IsNull(read.VerifiedSet);
        Assert.AreEqual(
            LuxembourgObservedObjectIdentitySetReadRefusalKind.RetainedSetIsForAnotherRun,
            read.Refusal!.Kind);

        // And the same bytes under their own run are admitted, so the refusal above is about the
        // run and not about the set being unreadable.
        var honest = await new LuxembourgObservedObjectIdentitySetReader(store).ReadAsync(
            written.RetainedSetReceipt!, written.SetRef!, RunIdentity, CancellationToken.None);
        Assert.IsNull(honest.Refusal, honest.Refusal?.Detail);
        Assert.IsNotNull(honest.VerifiedSet);
    }

    private static byte[] Canonical(LuxembourgObservedObjectIdentitySet set)
    {
        using var buffer = new MemoryStream();
        LuxembourgObservedObjectIdentitySetCanonicalWriter.Write(buffer, set);
        return buffer.ToArray();
    }

    /// <summary>
    /// A complete, well-formed identity set for whichever run is named. Used by the lineage tests to
    /// mint another run's set: the substitution they have to refuse is a REAL artifact belonging to
    /// someone else, not a malformed one that any parse would reject anyway.
    /// </summary>
    internal static (SourceArtifactRef Reference, VerifiedLuxembourgObservedObjectIdentitySet Set)
        BuildFor(SourceArtifactRef runIdentity)
    {
        var set = LuxembourgObservedObjectIdentitySet.FromObservations(
            runIdentity,
            [Observation("https://data.legilux.lu/eli/a"), Observation("https://data.legilux.lu/eli/b")]);
        using var buffer = new MemoryStream();
        var sha256 = LuxembourgObservedObjectIdentitySetCanonicalWriter.Write(buffer, set);
        var reference = new SourceArtifactRef(
            "urn:uuid:66666666-6666-4666-8666-666666666666", sha256);
        return (reference,
            VerifiedLuxembourgObservedObjectIdentitySet.ParseAndVerify(reference, buffer.ToArray()));
    }

    internal static LuxembourgResourceObservation Observation(string subjectUri)
    {
        var objectRef = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(ProfileRef, "legal_resource"),
            subjectUri,
            subjectUri,
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(subjectUri))),
            ProfileRef,
            null);
        return new LuxembourgResourceObservation(
            objectRef,
            ProfileRef,
            [],
            [],
            new LuxembourgSparqlRightsChannelObservations(ProfileRef, ProfileRef, []),
            new LuxembourgInFileRightsChannelObservations(ProfileRef, ProfileRef, []));
    }
}

/// <summary>
/// The same set, but reached the way a later party actually reaches it: out of custody, through the
/// door, using only what the finished run carries.
/// </summary>
[TestClass]
public sealed class LuxembourgRetainedObservedObjectIdentitySetTests
{
    [TestMethod]
    public async Task ARunsRetainedIdentitySetReopensFromTheAddressTheRunItselfCarries()
    {
        var (run, store) = await LuxembourgQueryExecutionAdapterTests
            .RunTwoSubjectDeliveredWithStoreAsync();
        Assert.IsNull(run.Refusal, $"code={run.Refusal?.Code} detail={run.Refusal?.Detail}");
        Assert.IsNotNull(run.ObservedObjectIdentitySetReceipt,
            "a delivered run must name where its own observed identity set was retained.");

        var read = await new LuxembourgObservedObjectIdentitySetReader(store).ReadAsync(
            run.ObservedObjectIdentitySetReceipt!,
            run.ObservedObjectIdentitySetRef!,
            run.CorpusRecordSet!.Set.RunIdentity,
            CancellationToken.None);

        Assert.IsNull(read.Refusal, read.Refusal?.Detail);

        // Checked against an independent statement the run makes about itself, not against the
        // artifact's own contents: the run separately reports which subjects it observed.
        Assert.AreEqual(
            run.ResourceObservationSubjects.Distinct().Count(),
            read.VerifiedSet!.Set.ObjectRefSha256Values.Count,
            "the reopened premise must cover exactly the objects the run says it observed.");
        Assert.AreEqual(
            run.ObservedObjectIdentitySetRef!.Sha256,
            read.VerifiedSet.SetRef.Sha256);
    }

    /// <summary>
    /// The address is not the reference, which is why the result has to carry both: a consumer
    /// holding only the reference could never fetch the bytes.
    /// </summary>
    [TestMethod]
    public async Task TheRetainedAddressIsNotTheSetReference()
    {
        var (run, _) = await LuxembourgQueryExecutionAdapterTests
            .RunTwoSubjectDeliveredWithStoreAsync();

        Assert.AreNotEqual(
            run.ObservedObjectIdentitySetRef!.Sha256,
            run.ObservedObjectIdentitySetReceipt!.Reference.ContentSha256,
            "the set's canonical digest is domain separated and custody addresses the plain bytes.");
    }

    /// <summary>
    /// The two inputs are checked against the bytes, never against each other. A run's own receipt
    /// paired with another artifact's reference produces a refusal, not a set.
    /// </summary>
    [TestMethod]
    public async Task ThisRunsReceiptUnderAnotherReferenceIsRefused()
    {
        var (run, store) = await LuxembourgQueryExecutionAdapterTests
            .RunTwoSubjectDeliveredWithStoreAsync();

        var read = await new LuxembourgObservedObjectIdentitySetReader(store).ReadAsync(
            run.ObservedObjectIdentitySetReceipt!,
            run.CorpusRecordSetRef!,
            run.CorpusRecordSet!.Set.RunIdentity,
            CancellationToken.None);

        Assert.IsNull(read.VerifiedSet);
        Assert.AreEqual(
            LuxembourgObservedObjectIdentitySetReadRefusalKind.RetainedBytesAreNotThisSet,
            read.Refusal!.Kind);
    }
}
