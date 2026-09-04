using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-06b: the corpus/6 record set writer's own acquisition door. Covers item 1 (the pure
/// per-manifest builder, honest <c>NotYetAcquired</c> for every accepted body since no fetch
/// capability exists yet, and the door a future D1-06c can supply a real outcome through), item 4
/// (canonical set write under the run's own required floor, and a checked reopen), item 5 (typed
/// completion), and item 6 (Decision 80: no path to a <see cref="CorpusBodyRecordKind.Held"/> record
/// without a caller supplying a real <see cref="DurableBlobWriteReceipt"/>).
/// </summary>
[TestClass]
public sealed class CorpusRecordSetWriterTests
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void BuildRecordsProducesNotYetAcquiredForEveryAcceptedBodyByDefault()
    {
        // The honest, correct D1-06b behavior: no fetch capability exists in this codebase yet, so
        // every accepted body is NotYetAcquired, never Held and never AcquisitionRefused, unless a
        // caller explicitly supplies an outcome.
        var manifest = ManifestFixture();
        var records = CorpusRecordBuilder.BuildRecords(manifest, ManifestRef(), RunIdentity());

        Assert.AreEqual(4, records.Count);
        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, records[0].Body.Kind);
        Assert.AreEqual(
            CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired,
            records[0].Body.PendingAcquisitionReason!.Kind);
        Assert.AreEqual(CorpusBodyRecordKind.NotHeld, records[1].Body.Kind);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, records[1].Body.NotHeldReason);
        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, records[2].Body.Kind);
        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, records[3].Body.Kind);

        for (var ordinal = 0; ordinal < records.Count; ordinal++)
        {
            Assert.AreEqual(ordinal, records[ordinal].ObjectOrdinal);
            Assert.AreEqual(ManifestRef(), records[ordinal].ManifestRef);
            Assert.AreEqual(RunIdentity(), records[ordinal].RunIdentity);
        }
    }

    [TestMethod]
    public void BuildRecordsHonorsASuppliedHeldOutcomeForOneAcceptedOrdinal()
    {
        var manifest = ManifestFixture();
        var receipt = FlooredReceipt();
        var outcomes = new Dictionary<int, CorpusAcquisitionOutcome>
        {
            [2] = CorpusAcquisitionOutcome.Held(receipt),
        };

        var records = CorpusRecordBuilder.BuildRecords(manifest, ManifestRef(), RunIdentity(), outcomes);

        Assert.AreEqual(CorpusBodyRecordKind.Held, records[2].Body.Kind);
        Assert.AreSame(receipt, records[2].Body.Receipt);
        Assert.AreEqual(CustodyMembership.Floored, records[2].Body.Floor);
        // Every other ordinal is unaffected by the one supplied outcome.
        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, records[0].Body.Kind);
        Assert.AreEqual(CorpusBodyRecordKind.NotHeld, records[1].Body.Kind);
        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, records[3].Body.Kind);
    }

    [TestMethod]
    public void BuildRecordsHonorsASuppliedRefusedOutcomeForOneAcceptedOrdinal()
    {
        var manifest = ManifestFixture();
        var outcomes = new Dictionary<int, CorpusAcquisitionOutcome>
        {
            [3] = CorpusAcquisitionOutcome.Refused(CorpusAcquisitionRefusalReason.StatusContentForbidden),
        };

        var records = CorpusRecordBuilder.BuildRecords(manifest, ManifestRef(), RunIdentity(), outcomes);

        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, records[3].Body.Kind);
        Assert.AreEqual(
            CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused,
            records[3].Body.PendingAcquisitionReason!.Kind);
        Assert.AreEqual(
            CorpusAcquisitionRefusalReason.StatusContentForbidden,
            records[3].Body.PendingAcquisitionReason!.Refusal);
    }

    [TestMethod]
    public void BuildRecordsRejectsAnOutcomeForANonAcceptedOrdinal()
    {
        var manifest = ManifestFixture();
        var outcomes = new Dictionary<int, CorpusAcquisitionOutcome>
        {
            [1] = CorpusAcquisitionOutcome.Held(FlooredReceipt()),
        };

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => CorpusRecordBuilder.BuildRecords(manifest, ManifestRef(), RunIdentity(), outcomes));
        StringAssert.Contains(exception.Message, "accepted-selected body");
    }

    [TestMethod]
    public void BuildRecordsRejectsAnOutcomeForAnOutOfRangeOrdinal()
    {
        var manifest = ManifestFixture();
        var outcomes = new Dictionary<int, CorpusAcquisitionOutcome>
        {
            [99] = CorpusAcquisitionOutcome.Held(FlooredReceipt()),
        };

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => CorpusRecordBuilder.BuildRecords(manifest, ManifestRef(), RunIdentity(), outcomes));
        StringAssert.Contains(exception.Message, "outside the manifest");
    }

    [TestMethod]
    public void BuildRecordsRejectsAManifestWhoseAccountingDoesNotCoverEveryOrdinal()
    {
        var profile = Profile();
        var incomplete = new ScopeManifest(
            ScopeManifestSchemaIds.Manifest,
            profile,
            CompleteEnumerationRef(),
            Array.Empty<SourceArtifactRef>(),
            [ObservedObject(0), ObservedObject(1)],
            Array.Empty<ScopeManifestRow>(),
            [
                // Only ordinal 0 is covered for the Record axis; ordinal 1 is missing entirely.
                new ScopeAccountingSet(ScopeAxis.Record, ScopeDisposition.AcceptedSelected, [0]),
                new ScopeAccountingSet(ScopeAxis.Body, ScopeDisposition.AcceptedSelected, [0, 1]),
                new ScopeAccountingSet(ScopeAxis.Relation, ScopeDisposition.AcceptedSelected, [0, 1]),
                new ScopeAccountingSet(
                    ScopeAxis.SupportingDocument, ScopeDisposition.AcceptedSelected, [0, 1]),
            ],
            Array.Empty<int>());

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => CorpusRecordBuilder.BuildRecords(incomplete, ManifestRef(), RunIdentity()));
        StringAssert.Contains(exception.Message, "does not cover ordinal");
    }

    [TestMethod]
    public void BuildRecordsRejectsAManifestThatDoubleCoversAnOrdinal()
    {
        var profile = Profile();
        var doubled = new ScopeManifest(
            ScopeManifestSchemaIds.Manifest,
            profile,
            CompleteEnumerationRef(),
            Array.Empty<SourceArtifactRef>(),
            [ObservedObject(0)],
            Array.Empty<ScopeManifestRow>(),
            [
                // Ordinal 0 is named in two different Record dispositions.
                new ScopeAccountingSet(ScopeAxis.Record, ScopeDisposition.AcceptedSelected, [0]),
                new ScopeAccountingSet(ScopeAxis.Record, ScopeDisposition.NeverIngest, [0]),
                new ScopeAccountingSet(ScopeAxis.Body, ScopeDisposition.AcceptedSelected, [0]),
                new ScopeAccountingSet(ScopeAxis.Relation, ScopeDisposition.AcceptedSelected, [0]),
                new ScopeAccountingSet(
                    ScopeAxis.SupportingDocument, ScopeDisposition.AcceptedSelected, [0]),
            ],
            Array.Empty<int>());

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => CorpusRecordBuilder.BuildRecords(doubled, ManifestRef(), RunIdentity()));
        StringAssert.Contains(exception.Message, "more than one disposition");
    }

    [TestMethod]
    public async Task WriteAsyncWritesUnderTheFloorAndReopensTheSet()
    {
        var manifest = ManifestFixture();
        var writer = new CorpusRecordSetWriter(new EnforcingInMemoryCustodyStore());

        var result = await writer.WriteAsync(
            manifest, ManifestRef(), RunIdentity(), null, CancellationToken.None);

        Assert.IsNull(result.Refusal, result.Refusal?.Detail);
        Assert.IsNotNull(result.SetRef);
        Assert.IsNotNull(result.VerifiedSet);
        Assert.AreEqual(4, result.VerifiedSet!.Set.Records.Count);
        Assert.AreEqual(ManifestRef(), result.VerifiedSet.Set.ManifestRef);
        Assert.AreEqual(RunIdentity(), result.VerifiedSet.Set.RunIdentity);

        Assert.AreEqual(CorpusRecordSetCompletionState.Complete, result.Completion!.State);
        Assert.AreEqual(4, result.Completion.ExpectedObjectCount);
        Assert.AreEqual(4, result.Completion.Entries.Count);
        Assert.AreEqual(CorpusRecordOutcomeKind.PendingAcquisition, result.Completion.Entries[0].Kind);
        Assert.AreEqual(CorpusRecordOutcomeKind.NotHeld, result.Completion.Entries[1].Kind);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, result.Completion.Entries[1].NotHeldReason);
    }

    [TestMethod]
    public async Task WriteAsyncRefusesWhenTheStoreEnforcesNoFloor()
    {
        // A bare FileSystemCustodyStore publishes NotEnforced for every write (Decision 71), exactly
        // the discipline EuQueryExecutionAdapter and LuxembourgQueryExecutionAdapter already apply
        // to their own scope-manifest write, applied here to this writer's own set write.
        var manifest = ManifestFixture();
        var root = Path.Combine(
            Path.GetTempPath(), "lex-corpus-set-writer-unfloored-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var writer = new CorpusRecordSetWriter(new FileSystemCustodyStore(root));

            var result = await writer.WriteAsync(
                manifest, ManifestRef(), RunIdentity(), null, CancellationToken.None);

            Assert.IsNull(result.SetRef);
            Assert.IsNull(result.VerifiedSet);
            Assert.IsNotNull(result.Refusal);
            Assert.AreEqual(CorpusRecordSetWriteRefusalKind.RecordSetNotHeld, result.Refusal!.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Decision 80, from the writer's own perspective: the acquisition door's only path onto
    /// <see cref="CorpusBodyRecordKind.Held"/> is <see cref="CorpusAcquisitionOutcome.Held"/>, and
    /// that factory requires a real, non-null <see cref="DurableBlobWriteReceipt"/> -- there is no
    /// boolean flag, no unchecked assumption, and no second door.
    /// </summary>
    [TestMethod]
    public void AcquisitionOutcomeHasNoPathToHeldWithoutARealReceipt()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CorpusAcquisitionOutcome.Held(null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => CorpusAcquisitionOutcome.Refused((CorpusAcquisitionRefusalReason)999));

        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance Lex.V3.Ingest.CorpusAcquisitionOutcome::.ctor("
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt, "
                    + "System.Nullable<Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason>) -> "
                    + "Lex.V3.Ingest.CorpusAcquisitionOutcome",
                "constructor private instance Lex.V3.Ingest.CorpusAcquisitionOutcome::.ctor("
                    + "Lex.V3.Ingest.CorpusAcquisitionOutcome) -> Lex.V3.Ingest.CorpusAcquisitionOutcome",
                "method public instance Lex.V3.Ingest.CorpusAcquisitionOutcome::<Clone>$() -> "
                    + "Lex.V3.Ingest.CorpusAcquisitionOutcome",
                "method public static Lex.V3.Ingest.CorpusAcquisitionOutcome::Held("
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt) -> "
                    + "Lex.V3.Ingest.CorpusAcquisitionOutcome",
                "method public static Lex.V3.Ingest.CorpusAcquisitionOutcome::Refused("
                    + "Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason) -> "
                    + "Lex.V3.Ingest.CorpusAcquisitionOutcome",
            },
            ConstructionSurface.Of(typeof(CorpusAcquisitionOutcome)).ToArray(),
            "a second, unchecked path onto this type must be justified in review, not discovered " +
            "later");
    }

    private static ScopeManifest ManifestFixture()
    {
        var profile = Profile();
        return new ScopeManifest(
            ScopeManifestSchemaIds.Manifest,
            profile,
            CompleteEnumerationRef(),
            Array.Empty<SourceArtifactRef>(),
            [ObservedObject(0), ObservedObject(1), ObservedObject(2), ObservedObject(3)],
            Array.Empty<ScopeManifestRow>(),
            [
                new ScopeAccountingSet(ScopeAxis.Record, ScopeDisposition.AcceptedSelected, [0, 1, 2, 3]),
                new ScopeAccountingSet(ScopeAxis.Body, ScopeDisposition.AcceptedSelected, [0, 2, 3]),
                new ScopeAccountingSet(ScopeAxis.Body, ScopeDisposition.TypedQuarantine, [1]),
                new ScopeAccountingSet(
                    ScopeAxis.Relation, ScopeDisposition.AcceptedSelected, [0, 1, 2, 3]),
                new ScopeAccountingSet(
                    ScopeAxis.SupportingDocument, ScopeDisposition.AcceptedSelected, [0, 1, 2, 3]),
            ],
            Array.Empty<int>());
    }

    private static ScopeObservedObjectEntry ObservedObject(int ordinal)
    {
        var canonicalKey = $"eu-consolidation-root:example-{ordinal}";
        var objectRef = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(ProfileRef(), "eu_consolidation_root"),
            $"https://publications.europa.eu/resource/celex/3201900{ordinal}",
            canonicalKey,
            CanonicalKeySha256(canonicalKey),
            ProfileRef(),
            null);
        return new ScopeObservedObjectEntry(
            objectRef, ScopeManifestCanonicalWriter.ComputeObjectRefSha256(objectRef));
    }

    private static ScopeProfileBinding Profile()
    {
        var profileRef = ProfileRef();
        var tableRef = TableRef();
        var members = new[]
            {
                Member(profileRef, "body_candidate"),
                Member(tableRef, "body_allow"),
                Member(tableRef, "record_allow"),
                Member(tableRef, "relation_allow"),
                Member(tableRef, "support_allow"),
            }
            .OrderBy(static member => member.RegistryRef.ResourceId, StringComparer.Ordinal)
            .ThenBy(static member => member.RegistryRef.Sha256, StringComparer.Ordinal)
            .ThenBy(static member => member.MemberKey, StringComparer.Ordinal)
            .ToArray();
        int Ordinal(SourceArtifactRef registry, string key) => Array.FindIndex(
            members, member => member.RegistryRef == registry && member.MemberKey == key);

        return new ScopeProfileBinding(
            profileRef,
            tableRef,
            members,
            [Ordinal(tableRef, "body_allow")],
            [
                new ScopeRuleBinding(ScopeAxis.Record, Ordinal(tableRef, "record_allow"), 0),
                new ScopeRuleBinding(ScopeAxis.Body, Ordinal(tableRef, "body_allow"), 1),
                new ScopeRuleBinding(ScopeAxis.Relation, Ordinal(tableRef, "relation_allow"), 2),
                new ScopeRuleBinding(
                    ScopeAxis.SupportingDocument, Ordinal(tableRef, "support_allow"), 3),
            ],
            Ordinal(profileRef, "body_candidate"));
    }

    private static SourceRegistryMemberRef Member(SourceArtifactRef registry, string key) =>
        new(registry, key);

    private static SourceArtifactRef ProfileRef() => Artifact("c0e28bb7-f26a-4ea0-9628-d084fd3aaf22");

    private static SourceArtifactRef TableRef() => Artifact("ddaa3f1b-994d-47b8-83c7-e6221a90c388");

    private static SourceArtifactRef CompleteEnumerationRef() =>
        Artifact("11111111-2222-4333-8444-555555555555");

    private static SourceArtifactRef ManifestRef() =>
        Artifact("33333333-3333-4333-8333-333333333333");

    private static SourceArtifactRef RunIdentity() =>
        Artifact("44444444-4444-4444-8444-444444444444");

    private static SourceArtifactRef Artifact(string id) => new($"urn:uuid:{id}", Digest);

    private static string CanonicalKeySha256(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static DurableBlobWriteReceipt FlooredReceipt()
    {
        var observedAt = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, new string('e', 64), 4096, CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.ImmutableObject1,
            Guid.Parse("00000000-0000-0000-0000-0000000000f1"),
            CustodyProtection.LockedTime,
            observedAt,
            observedAt.AddDays(91));
        return new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
    }

    /// <summary>
    /// A real in-memory content-addressed store publishing enforced (<see cref="CustodyProtection.LockedTime"/>)
    /// protection for every write, mirroring <c>LuxembourgQueryExecutionAdapterTests.InMemoryCustodyStore</c>
    /// exactly: a real store this writer's own retention floor check passes against, never a bare
    /// unenforced double.
    /// </summary>
    private sealed class EnforcingInMemoryCustodyStore : ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _byDigest = new(StringComparer.Ordinal);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen);
            _byDigest[digest] = frozen;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, digest, frozen.LongLength, custodyClass);
            var observedAt = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-0000000000f2"),
                CustodyProtection.LockedTime,
                observedAt,
                observedAt.AddDays(91));
            return Task.FromResult(new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt, reference, policy));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[reference.ContentSha256]);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[contentSha256]);
    }
}
