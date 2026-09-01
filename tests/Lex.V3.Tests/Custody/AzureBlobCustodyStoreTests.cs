using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Custody.Azure;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class AzureBlobCustodyStoreTests
{
    private const string ServiceUri = "https://stlexv3custody.blob.core.windows.net/";
    private static readonly byte[] Body = "publisher transport bytes"u8.ToArray();
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);
    private static readonly Guid ManagedIdentityClientId =
        Guid.Parse("caecb92d-1f9c-43ec-8798-d9e83d02c4bc");
    private static readonly Guid NightlyPolicyKey =
        Guid.Parse("e21680d3-badd-4a46-9293-c5d0b34f0300");
    private static readonly Guid LegalHoldPolicyKey =
        Guid.Parse("dc31db8e-3909-48e5-b60d-a86364175e30");
    private static readonly Guid SubscriptionId =
        Guid.Parse("7b937a55-7a06-47de-acd6-2a78e43d7782");

    [TestMethod]
    public void OptionsRequireAnHttpsAccountRootAndThreeDistinctContainers()
    {
        foreach (var serviceUri in new[]
                 {
                     "http://custody.example.test/",
                     "https://custody.example.test/",
                     "https://stlexv3custody.blob.core.windows.net.evil.test/",
                     "https://user@custody.example.test/",
                     "https://custody.example.test/container",
                     "https://custody.example.test:444/",
                     "https://custody.example.test/?secret=value",
                     "https://custody.example.test/#fragment",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(() => Options(serviceUri));
        }

        Assert.ThrowsExactly<ArgumentException>(() => Options(
            ServiceUri,
            stagingContainer: "nightly"));
        Assert.ThrowsExactly<ArgumentException>(() => Options(
            ServiceUri,
            legalHoldContainer: "nightly"));
        Assert.ThrowsExactly<ArgumentException>(() => Options(
            ServiceUri,
            stagingContainer: "Bad_Name"));
    }

    [TestMethod]
    public void OptionsRequireDistinctNonemptyPolicyAndIdentityKeys()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Options(
            ServiceUri,
            managedIdentityClientId: Guid.Empty));
        Assert.ThrowsExactly<ArgumentException>(() => Options(
            ServiceUri,
            nightlyPolicyKey: Guid.Empty));
        Assert.ThrowsExactly<ArgumentException>(() => Options(
            ServiceUri,
            legalHoldPolicyKey: NightlyPolicyKey));
        Assert.ThrowsExactly<ArgumentException>(() => Options(
            ServiceUri,
            subscriptionId: Guid.Empty));
        Assert.ThrowsExactly<ArgumentException>(() => Options(
            ServiceUri,
            resourceGroup: "invalid/resource/group"));
    }

    [TestMethod]
    public async Task FirstCreateStagesVerifiesCopiesWithBearerAndCleansByEtag()
    {
        var harness = new Harness();

        var receipt = await harness.Store.CreateAsync(
            Body, CustodyClass.NightlyFloor90d, CancellationToken.None);

        var digest = CustodyDigest.Of(Body);
        var stage = harness.Staging.SingleBlob;
        var generation = harness.Nightly.SingleBlob;
        StringAssert.Matches(stage.Name, new("^pending/[0-9a-f]{32}$"));
        StringAssert.Matches(
            generation.Name,
            new($"^{digest}/g/[0-9a-f]{{32}}$"));

        Assert.AreEqual(ETag.All, stage.UploadOptions!.Conditions!.IfNoneMatch);
        Assert.AreEqual(
            StorageChecksumAlgorithm.StorageCrc64,
            stage.UploadOptions.TransferValidation!.ChecksumAlgorithm);
        Assert.AreEqual(false, generation.CopyOptions!.CopySourceBlobProperties);
        Assert.AreEqual(stage.ETag, generation.CopyOptions.SourceConditions!.IfMatch);
        Assert.AreEqual(ETag.All, generation.CopyOptions.DestinationConditions!.IfNoneMatch);
        Assert.AreEqual("Bearer", generation.CopyOptions.SourceAuthentication!.Scheme);
        Assert.AreEqual(FakeTokenCredential.TokenValue, generation.CopyOptions.SourceAuthentication.Parameter);
        Assert.AreEqual(stage.Uri, generation.CopySource);
        Assert.AreEqual(DeleteSnapshotsOption.IncludeSnapshots, stage.DeletedSnapshotsOption);
        Assert.AreEqual(stage.ETag, stage.DeleteConditions!.IfMatch);
        Assert.IsTrue(stage.DeleteToken.CanBeCanceled);
        Assert.IsFalse(stage.DeleteToken.IsCancellationRequested);

        AssertOrderedSubsequence(
            harness.Events,
            "nightly.list",
            "policy.nightly",
            "policy.staging",
            "staging.upload",
            "staging.properties",
            "staging.download",
            "credential",
            "nightly.copy",
            "nightly.properties",
            "nightly.download",
            "policy.nightly",
            "nightly.properties",
            "configuration.append",
            "staging.delete");
        Assert.AreEqual(
            1,
            harness.Events.Count(value => string.Equals(
                value, "staging.properties", StringComparison.Ordinal)));
        Assert.AreEqual(digest, receipt.Reference.ContentSha256);
        Assert.AreEqual(CustodyProtection.LockedTime, receipt.PolicyEvidence.Protection);
        Assert.AreEqual(ObservedAt, receipt.PolicyEvidence.ObservedAt);
        Assert.AreEqual(ObservedAt.AddDays(91), receipt.PolicyEvidence.ProtectedUntil);
    }

    [TestMethod]
    public async Task PrivateStagingIsRequiredBeforeAnyUpload()
    {
        var harness = new Harness();
        harness.Policy.StagingException = new CustodyPolicyException(
            "The staging container is public.");

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.IsTrue(harness.Staging.Blobs.Count == 0);
        Assert.IsTrue(harness.Events.Contains("policy.staging", StringComparer.Ordinal));
        Assert.IsFalse(harness.Events.Contains("staging.upload", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task ConfigurationReceiptMustBeRetainedBeforePortableReceiptReturns()
    {
        var harness = new Harness();
        harness.Journal.Exception = new IOException("Journal unavailable");

        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.IsTrue(harness.Events.Contains("configuration.append", StringComparer.Ordinal));
        Assert.IsTrue(harness.Events.Contains("staging.delete", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task ReusedGenerationCannotReturnWhenConfigurationJournalFails()
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var name = GenerationName(digest, '2');
        harness.Nightly.AddExisting(name, Body, createdOn: ObservedAt);
        harness.Nightly.Pages.Add([name]);
        harness.Journal.Exception = new IOException("Journal unavailable");

        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.IsTrue(harness.Events.Contains("configuration.append", StringComparer.Ordinal));
        Assert.AreEqual(0, harness.Staging.Blobs.Count);
    }

    [TestMethod]
    public async Task ReusedGenerationCannotReturnAfterCallerCancellationDuringJournalAppend()
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var name = GenerationName(digest, '3');
        harness.Nightly.AddExisting(name, Body, createdOn: ObservedAt);
        harness.Nightly.Pages.Add([name]);
        using var cancellation = new CancellationTokenSource();
        harness.Journal.AfterAppend = cancellation.Cancel;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            harness.Store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, cancellation.Token));

        Assert.AreEqual(1, harness.Journal.Receipts.Count);
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.AreEqual(0, harness.Staging.Blobs.Count);
    }

    [TestMethod]
    public async Task ConfigurationReceiptPolicyKeyMustMatchTheRequestedLane()
    {
        var harness = new Harness();
        harness.Policy.ConfigurationPolicyKeyOverride =
            Guid.Parse("6822ca9c-5bc4-4532-8318-6474cf0e4552");

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.AreEqual(0, harness.Journal.Receipts.Count);
    }

    [TestMethod]
    public async Task ConfigurationReceiptObservationMustMatchThePolicyObservation()
    {
        var harness = new Harness();
        harness.Policy.ConfigurationObservedAtOverride = ObservedAt.AddTicks(-1);

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.AreEqual(0, harness.Journal.Receipts.Count);
    }

    [TestMethod]
    [DataRow("stage")]
    [DataRow("final")]
    public async Task ByteMismatchAtEitherReadbackBlocksReceiptAndStillCleansStage(string boundary)
    {
        var harness = new Harness();
        if (boundary == "stage")
        {
            harness.Staging.ConfigureNewBlob = blob =>
                blob.AfterUpload = candidate => candidate.DownloadBytes = Corrupt(Body);
        }
        else
        {
            harness.Nightly.ConfigureNewBlob = blob =>
                blob.AfterCopy = candidate => candidate.DownloadBytes = Corrupt(Body);
        }

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.IsTrue(harness.Events.Contains("staging.delete", StringComparer.Ordinal));
        Assert.AreEqual(
            boundary == "final",
            harness.Events.Contains("nightly.copy", StringComparer.Ordinal));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public async Task StageStreamLengthMismatchFailsWithCorrectAdvertisedLength(int lengthDelta)
    {
        var harness = new Harness();
        harness.Staging.ConfigureNewBlob = blob =>
            blob.AfterUpload = candidate => candidate.DownloadBytes =
                LengthAdjusted(Body, lengthDelta);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        var stage = harness.Staging.SingleBlob;
        Assert.AreEqual(Body.LongLength, stage.PropertiesContentLengths.Single());
        Assert.AreEqual(Body.LongLength + lengthDelta, stage.DownloadBytes!.LongLength);
        Assert.IsFalse(harness.Events.Contains("nightly.copy", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task MissingAuthoritativeLockedRetentionDaysBlocksReceipt()
    {
        var harness = new Harness();
        harness.Policy.NightlyLockedRetentionDays = null;

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));
    }

    [TestMethod]
    public async Task NightlyProtectionBelowNinetyDaysBlocksReceipt()
    {
        var harness = new Harness();
        harness.Policy.NightlyLockedRetentionDays = 90;
        harness.Nightly.ConfigureNewBlob = blob =>
            blob.CreatedOn = ObservedAt.AddTicks(-1);

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));
    }

    [TestMethod]
    public async Task AnObservationWhoseConfigurationReceiptDisagreesWithItIsRefused()
    {
        // I set out to prove the nightly path's `|| policy.ActiveLegalHold` disjunct and
        // found it unreachable. To arrive there an observation needs a hold and a nightly
        // configuration receipt that also reports a hold, and the receipt constructor refuses
        // exactly that pair. Every attempt to build the input is caught first by the
        // consistency block at the top of TryCreateReceipt, which is what this now proves:
        // an observation and its receipt must agree on class, retention and hold.
        var harness = new Harness();
        harness.Policy.NightlyMisreportsLegalHold = true;
        harness.Nightly.ConfigureNewBlob = blob => blob.CreatedOn = ObservedAt;

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));
    }

    [TestMethod]
    public async Task AnAppendBlobIsRefusedForItsTypeRatherThanForSomethingElse()
    {
        // FinalGenerationMustBeAnUnversionedBlockBlob covers both shapes, but neutralising the
        // block-blob half of that condition left it green: the version-id half was carrying
        // the whole test. Asserting the reason isolates the block-blob half.
        var harness = new Harness();
        harness.Nightly.ConfigureNewBlob = blob => blob.BlobType = BlobType.Append;

        var refusal = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        StringAssert.Contains(refusal.Message, "unversioned block blob");
    }

    [TestMethod]
    public async Task NightlyProtectionAtExactlyNinetyDaysIsAccepted()
    {
        var harness = new Harness();
        harness.Policy.NightlyLockedRetentionDays = 90;

        var receipt = await harness.Store.CreateAsync(
            Body, CustodyClass.NightlyFloor90d, CancellationToken.None);

        Assert.AreEqual(ObservedAt.AddDays(90), receipt.PolicyEvidence.ProtectedUntil);
    }

    [TestMethod]
    public async Task LegalHoldLaneRequiresAnActiveContainerHold()
    {
        var harness = new Harness();
        harness.Policy.ActiveLegalHold = false;

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.LegalHoldEvidence, CancellationToken.None));
    }

    [TestMethod]
    public async Task ActiveContainerLegalHoldIssuesReceiptWithoutBlobLevelHoldEvidence()
    {
        var harness = new Harness();

        var receipt = await harness.Store.CreateAsync(
            Body, CustodyClass.LegalHoldEvidence, CancellationToken.None);

        Assert.AreEqual(
            CustodyProtection.ActiveLegalHold,
            receipt.PolicyEvidence.Protection);
        Assert.IsNull(receipt.PolicyEvidence.ProtectedUntil);
        Assert.IsTrue(harness.Events.Contains("policy.legal_hold", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task ExactGenerationIsRevalidatedByEtagAfterPolicyObservation()
    {
        var harness = new Harness();
        harness.Policy.AfterRead = _ =>
        {
            if (harness.Nightly.Blobs.Count != 0)
            {
                harness.Nightly.SingleBlob.ChangeEtag(
                    new ETag("\"changed-during-policy-read\""));
            }
        };

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        var generation = harness.Nightly.SingleBlob;
        Assert.AreEqual(2, generation.PropertiesConditions.Count);
        Assert.AreEqual(
            new ETag("\"durable-etag\""),
            generation.PropertiesConditions[1]!.IfMatch);
        Assert.IsTrue(harness.Events.Contains("policy.nightly", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task PostPolicyEtagBracketDoesNotCompareIndependentServiceClocks()
    {
        var harness = new Harness();
        harness.Policy.PolicyObservedAt = ObservedAt.AddSeconds(1);

        var receipt = await harness.Store.CreateAsync(
            Body, CustodyClass.NightlyFloor90d, CancellationToken.None);

        Assert.AreEqual(harness.Policy.PolicyObservedAt, receipt.VerifiedAt);
        Assert.AreEqual(ObservedAt, harness.Nightly.SingleBlob.ServerDate);
        Assert.AreEqual(2, harness.Nightly.SingleBlob.PropertiesConditions.Count);
    }

    [TestMethod]
    public async Task ReusedGenerationIsRevalidatedByEtagAfterPolicyObservation()
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var name = GenerationName(digest, '5');
        harness.Nightly.AddExisting(name, Body, createdOn: ObservedAt);
        harness.Nightly.Pages.Add([name]);
        harness.Policy.AfterRead = _ => harness.Nightly.SingleBlob.ChangeEtag(
            new ETag("\"changed-during-policy-read\""));

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.AreEqual(0, harness.Staging.Blobs.Count);
        Assert.AreEqual(2, harness.Nightly.SingleBlob.PropertiesConditions.Count);
        Assert.AreEqual(
            new ETag("\"initial\""),
            harness.Nightly.SingleBlob.PropertiesConditions[1]!.IfMatch);
    }

    [TestMethod]
    public async Task AdequateExistingGenerationIsReusedWithoutStagingOrCredential()
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var name = GenerationName(digest, 'a');
        harness.Nightly.AddExisting(
            name,
            Body,
            createdOn: ObservedAt);
        harness.Nightly.Pages.Add([name]);

        var receipt = await harness.Store.CreateAsync(
            Body, CustodyClass.NightlyFloor90d, CancellationToken.None);

        Assert.AreEqual(ObservedAt, receipt.VerifiedAt);
        Assert.AreEqual(0, harness.Staging.Blobs.Count);
        Assert.IsFalse(harness.Events.Contains("credential", StringComparer.Ordinal));
        Assert.IsFalse(harness.Events.Contains("nightly.copy", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task InadequateExistingGenerationMintsAndVerifiesANewGeneration()
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var existing = GenerationName(digest, 'b');
        harness.Nightly.AddExisting(
            existing,
            Body,
            createdOn: ObservedAt.AddDays(-2));
        harness.Nightly.Pages.Add([existing]);

        var receipt = await harness.Store.CreateAsync(
            Body, CustodyClass.NightlyFloor90d, CancellationToken.None);

        Assert.AreEqual(CustodyProtection.LockedTime, receipt.PolicyEvidence.Protection);
        Assert.AreEqual(2, harness.Nightly.Blobs.Count);
        Assert.IsTrue(harness.Events.Contains("nightly.copy", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task CompletePaginationRejectsAMalformedLateSiblingBeforeReuse()
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var valid = GenerationName(digest, 'c');
        var malformed = $"{digest}/g/not-a-generation";
        harness.Nightly.AddExisting(
            valid,
            Body,
            createdOn: ObservedAt);
        harness.Nightly.Pages.Add([valid]);
        harness.Nightly.Pages.Add([malformed]);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.AreEqual($"{digest}/", harness.Nightly.LastPrefix);
        Assert.AreEqual(BlobTraits.None, harness.Nightly.LastTraits);
        Assert.AreEqual(BlobStates.None, harness.Nightly.LastStates);
        Assert.IsFalse(harness.Events.Contains("nightly.properties", StringComparer.Ordinal));
        Assert.AreEqual(0, harness.Staging.Blobs.Count);
    }

    [TestMethod]
    public async Task EveryListedGenerationIsHashedBeforeAnAdequateOneCanBeReused()
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var adequate = GenerationName(digest, 'e');
        var corrupt = GenerationName(digest, 'f');
        harness.Nightly.AddExisting(
            adequate,
            Body,
            createdOn: ObservedAt);
        harness.Nightly.AddExisting(
            corrupt,
            Corrupt(Body),
            createdOn: ObservedAt);
        harness.Nightly.Pages.Add([adequate, corrupt]);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.AreEqual(2, harness.Events.Count(value => string.Equals(
            value, "nightly.download", StringComparison.Ordinal)));
        Assert.AreEqual(0, harness.Staging.Blobs.Count);
    }

    [TestMethod]
    public async Task EnumeratedGenerationThatDisappearsFailsIntegrity()
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var missing = GenerationName(digest, '1');
        harness.Nightly.AddExisting(missing, Body, createdOn: ObservedAt);
        harness.Nightly.Blobs[missing].Present = false;
        harness.Nightly.Pages.Add([missing]);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.AreEqual(0, harness.Staging.Blobs.Count);
    }

    [TestMethod]
    [DataRow("append_blob")]
    [DataRow("version_id")]
    public async Task FinalGenerationMustBeAnUnversionedBlockBlob(string invalidShape)
    {
        var harness = new Harness();
        harness.Nightly.ConfigureNewBlob = blob =>
        {
            if (invalidShape == "append_blob")
            {
                blob.BlobType = BlobType.Append;
            }
            else
            {
                blob.VersionId = "version-1";
            }
        };

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));
    }

    [TestMethod]
    public async Task PolicyReaderFailureIsFailClosedBeforeStaging()
    {
        var harness = new Harness();
        harness.Policy.Exception = new CustodyPolicyException(
            "Version-level WORM was reported by ARM.");

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.IsTrue(harness.Events.Contains("policy.nightly", StringComparer.Ordinal));
        Assert.AreEqual(0, harness.Staging.Blobs.Count);
    }

    [TestMethod]
    public async Task PolicyObservationForAnotherLaneCannotIssueAReceipt()
    {
        var harness = new Harness();
        harness.Policy.ReturnedCustodyClass = CustodyClass.LegalHoldEvidence;

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.IsTrue(harness.Events.Contains("policy.nightly", StringComparer.Ordinal));
    }

    [TestMethod]
    [DataRow("stage")]
    [DataRow("copy")]
    public async Task ConditionalWriteConflictIsIntegrityNotProviderUnavailability(string boundary)
    {
        var harness = new Harness();
        if (boundary == "stage")
        {
            harness.Staging.ConfigureNewBlob = blob =>
                blob.UploadException = new RequestFailedException(412, "occupied stage name");
        }
        else
        {
            harness.Nightly.ConfigureNewBlob = blob =>
                blob.CopyException = new RequestFailedException(412, "occupied generation name");
        }

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAcceptsAnExactGenerationAfterItsNinetyDayFloorHasElapsed()
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var name = GenerationName(digest, 'd');
        harness.Nightly.AddExisting(
            name,
            Body,
            createdOn: ObservedAt.AddDays(-100));
        harness.Nightly.Pages.Add([name]);
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            digest,
            Body.LongLength,
            CustodyClass.NightlyFloor90d);

        var restored = await harness.Store.ReadAsync(reference, CancellationToken.None);

        CollectionAssert.AreEqual(Body, restored.ToArray());
        Assert.AreEqual(0, harness.Staging.Blobs.Count);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public async Task DurableReadStreamLengthMismatchFailsWithCorrectAdvertisedLength(
        int lengthDelta)
    {
        var harness = new Harness();
        var digest = CustodyDigest.Of(Body);
        var name = GenerationName(digest, '4');
        harness.Nightly.AddExisting(name, Body, createdOn: ObservedAt);
        var generation = harness.Nightly.Blobs[name];
        generation.DownloadBytes = LengthAdjusted(Body, lengthDelta);
        harness.Nightly.Pages.Add([name]);
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            digest,
            Body.LongLength,
            CustodyClass.NightlyFloor90d);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.ReadAsync(reference, CancellationToken.None));

        Assert.AreEqual(Body.LongLength, generation.PropertiesContentLengths.Single());
        Assert.AreEqual(Body.LongLength + lengthDelta, generation.DownloadBytes.LongLength);
    }

    [TestMethod]
    public async Task ProviderCancellationIsUnavailabilityButCallerCancellationPropagates()
    {
        var providerTimeout = new Harness();
        providerTimeout.Nightly.ListException = new OperationCanceledException("provider timeout");

        var unavailable = await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
            providerTimeout.Store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, CancellationToken.None));
        Assert.IsInstanceOfType<OperationCanceledException>(unavailable.InnerException);

        var callerCancellation = new Harness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            callerCancellation.Store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, cancellation.Token));
        Assert.IsFalse(callerCancellation.Events.Contains("nightly.list", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task PolicyReaderTimeoutIsProviderUnavailability()
    {
        var harness = new Harness();
        harness.Policy.Exception = new OperationCanceledException("ARM timeout");

        var unavailable = await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
            harness.Store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, CancellationToken.None));

        Assert.IsInstanceOfType<OperationCanceledException>(unavailable.InnerException);
        Assert.AreEqual(0, harness.Staging.Blobs.Count);
    }

    [TestMethod]
    public async Task ReceiptContainsNoAzureOrPhysicalGenerationCoordinates()
    {
        var options = Options(
            "https://stprivatecustody.blob.core.windows.net/",
            stagingContainer: "stage-private",
            nightlyContainer: "durable-private",
            legalHoldContainer: "hold-private");
        var harness = new Harness(options);

        var receipt = await harness.Store.CreateAsync(
            Body, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var json = ContractJson.Serialize(receipt);

        foreach (var forbiddenValue in new[]
                 {
                     "stprivatecustody.blob.core.windows.net",
                     "stage-private",
                     "durable-private",
                     "hold-private",
                     harness.Nightly.SingleBlob.Name,
                 })
        {
            Assert.IsFalse(json.Contains(forbiddenValue, StringComparison.Ordinal));
        }

        using var document = JsonDocument.Parse(json);
        AssertNoPhysicalCoordinateNames(document.RootElement);
    }

    private static AzureBlobCustodyOptions Options(
        string serviceUri,
        string stagingContainer = "staging",
        string nightlyContainer = "nightly",
        string legalHoldContainer = "legal-hold",
        Guid? managedIdentityClientId = null,
        Guid? nightlyPolicyKey = null,
        Guid? legalHoldPolicyKey = null,
        Guid? subscriptionId = null,
        string resourceGroup = "rg-lex-v3-custody") =>
        new(
            new Uri(serviceUri),
            stagingContainer,
            nightlyContainer,
            legalHoldContainer,
            managedIdentityClientId ?? ManagedIdentityClientId,
            nightlyPolicyKey ?? NightlyPolicyKey,
            legalHoldPolicyKey ?? LegalHoldPolicyKey,
            subscriptionId ?? SubscriptionId,
            resourceGroup);

    private static string GenerationName(string digest, char fill) =>
        $"{digest}/g/{new string(fill, 32)}";

    private static byte[] Corrupt(byte[] source)
    {
        var corrupted = source.ToArray();
        corrupted[0] ^= 0xff;
        return corrupted;
    }

    private static byte[] LengthAdjusted(byte[] source, int lengthDelta) => lengthDelta switch
    {
        -1 => source[..^1],
        1 => [.. source, 0],
        _ => throw new ArgumentOutOfRangeException(nameof(lengthDelta)),
    };

    private static void AssertNoPhysicalCoordinateNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.IsFalse(new[]
                {
                    "account", "bucket", "container", "generation", "path", "region", "uri", "url",
                }.Contains(property.Name, StringComparer.OrdinalIgnoreCase));
                AssertNoPhysicalCoordinateNames(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertNoPhysicalCoordinateNames(item);
            }
        }
    }

    private static void AssertOrderedSubsequence(
        IReadOnlyList<string> actual,
        params string[] expected)
    {
        var next = 0;
        foreach (var item in actual)
        {
            if (next < expected.Length
                && string.Equals(item, expected[next], StringComparison.Ordinal))
            {
                next++;
            }
        }

        Assert.AreEqual(
            expected.Length,
            next,
            $"Expected ordered subsequence: {string.Join(", ", expected)}. Actual: {string.Join(", ", actual)}.");
    }

    private sealed class Harness
    {
        public Harness(AzureBlobCustodyOptions? options = null)
        {
            Events = [];
            Staging = new FakeBlobContainerClient("staging", Events);
            Nightly = new FakeBlobContainerClient("nightly", Events);
            LegalHold = new FakeBlobContainerClient("legal_hold", Events);
            var containers = new[] { Staging, Nightly, LegalHold };
            foreach (var container in containers)
            {
                container.ResolveSource = uri => containers
                    .SelectMany(candidate => candidate.Blobs.Values)
                    .SingleOrDefault(candidate => candidate.Uri == uri);
            }

            Credential = new FakeTokenCredential(Events);
            Policy = new FakePolicyReader(Events);
            Journal = new FakeConfigurationJournal(Events);
            Store = new AzureBlobCustodyStore(
                options ?? Options(ServiceUri),
                Staging,
                Nightly,
                LegalHold,
                Credential,
                Policy,
                Journal);
        }

        public List<string> Events { get; }

        public FakeBlobContainerClient Staging { get; }

        public FakeBlobContainerClient Nightly { get; }

        public FakeBlobContainerClient LegalHold { get; }

        public FakeTokenCredential Credential { get; }

        public FakePolicyReader Policy { get; }

        public FakeConfigurationJournal Journal { get; }

        public AzureBlobCustodyStore Store { get; }
    }

    private sealed class FakeBlobContainerClient : BlobContainerClient
    {
        private readonly string _label;
        private readonly List<string> _events;

        public FakeBlobContainerClient(string label, List<string> events)
        {
            _label = label;
            _events = events;
        }

        public Dictionary<string, FakeBlockBlobClient> Blobs { get; } =
            new(StringComparer.Ordinal);

        public List<IReadOnlyList<string>> Pages { get; } = [];

        public Action<FakeBlockBlobClient>? ConfigureNewBlob { get; set; }

        public Func<Uri, FakeBlockBlobClient?>? ResolveSource { get; set; }

        public Exception? ListException { get; set; }

        public string? LastPrefix { get; private set; }

        public BlobTraits LastTraits { get; private set; }

        public BlobStates LastStates { get; private set; }

        public FakeBlockBlobClient SingleBlob => Blobs.Values.Single();

        public void AddExisting(
            string name,
            byte[] bytes,
            DateTimeOffset? createdOn = null)
        {
            var blob = NewBlob(name);
            blob.Content = bytes.ToArray();
            blob.Present = true;
            blob.CreatedOn = createdOn ?? ObservedAt;
            Blobs.Add(name, blob);
        }

        public override AsyncPageable<BlobItem> GetBlobsAsync(
            BlobTraits traits = BlobTraits.None,
            BlobStates states = BlobStates.None,
            string? prefix = null,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"{_label}.list");
            LastTraits = traits;
            LastStates = states;
            LastPrefix = prefix;
            if (ListException is not null)
            {
                throw ListException;
            }

            var pages = Pages.Select((names, index) => Page<BlobItem>.FromValues(
                names.Select(name => BlobsModelFactory.BlobItem(name: name)).ToArray(),
                index == Pages.Count - 1 ? null : $"page-{index + 1}",
                new FakeResponse()));
            return AsyncPageable<BlobItem>.FromPages(pages);
        }

        protected override BlockBlobClient GetBlockBlobClientCore(string blobName)
        {
            if (!Blobs.TryGetValue(blobName, out var blob))
            {
                blob = NewBlob(blobName);
                ConfigureNewBlob?.Invoke(blob);
                Blobs.Add(blobName, blob);
            }

            return blob;
        }

        private FakeBlockBlobClient NewBlob(string name)
        {
            var blob = new FakeBlockBlobClient(
                _label,
                name,
                _events,
                new Uri($"https://storage.example.test/{_label}/{name}"),
                uri => ResolveSource?.Invoke(uri));
            return blob;
        }
    }

    private sealed class FakeBlockBlobClient : BlockBlobClient
    {
        private readonly string _label;
        private readonly List<string> _events;
        private readonly Func<Uri, FakeBlockBlobClient?> _resolveSource;

        public FakeBlockBlobClient(
            string label,
            string name,
            List<string> events,
            Uri uri,
            Func<Uri, FakeBlockBlobClient?> resolveSource)
        {
            _label = label;
            Name = name;
            _events = events;
            Uri = uri;
            _resolveSource = resolveSource;
        }

        public override string Name { get; }

        public override Uri Uri { get; }

        public bool Present { get; set; }

        public byte[] Content { get; set; } = [];

        public byte[]? DownloadBytes { get; set; }

        public ETag ETag { get; private set; } = new("\"initial\"");

        public DateTimeOffset? ServerDate { get; set; } = ObservedAt;

        public DateTimeOffset CreatedOn { get; set; } = ObservedAt;

        public BlobType BlobType { get; set; } = BlobType.Block;

        public string? VersionId { get; set; }

        public Exception? UploadException { get; set; }

        public Exception? CopyException { get; set; }

        public Action<FakeBlockBlobClient>? AfterUpload { get; set; }

        public Action<FakeBlockBlobClient>? AfterCopy { get; set; }

        public BlobUploadOptions? UploadOptions { get; private set; }

        public BlobSyncUploadFromUriOptions? CopyOptions { get; private set; }

        public Uri? CopySource { get; private set; }

        public DeleteSnapshotsOption? DeletedSnapshotsOption { get; private set; }

        public BlobRequestConditions? DeleteConditions { get; private set; }

        public CancellationToken DeleteToken { get; private set; }

        public List<BlobRequestConditions?> PropertiesConditions { get; } = [];

        public List<long> PropertiesContentLengths { get; } = [];

        public void ChangeEtag(ETag etag) => ETag = etag;

        public override async Task<Response<BlobContentInfo>> UploadAsync(
            Stream content,
            BlobUploadOptions options,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"{_label}.upload");
            cancellationToken.ThrowIfCancellationRequested();
            if (UploadException is not null)
            {
                throw UploadException;
            }

            UploadOptions = options;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Content = buffer.ToArray();
            Present = true;
            ETag = new ETag("\"stage-etag\"");
            AfterUpload?.Invoke(this);
            return Response.FromValue(ContentInfo(ETag), new FakeResponse(ServerDate));
        }

        public override Task<Response<BlobContentInfo>> SyncUploadFromUriAsync(
            Uri copySource,
            BlobSyncUploadFromUriOptions options,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"{_label}.copy");
            cancellationToken.ThrowIfCancellationRequested();
            if (CopyException is not null)
            {
                throw CopyException;
            }

            CopySource = copySource;
            CopyOptions = options;
            var source = _resolveSource(copySource)
                ?? throw new InvalidOperationException("The test copy source was not registered.");
            Content = source.Content.ToArray();
            Present = true;
            ETag = new ETag("\"durable-etag\"");
            AfterCopy?.Invoke(this);
            return Task.FromResult(Response.FromValue(
                ContentInfo(ETag),
                new FakeResponse(ServerDate)));
        }

        public override Task<Response<BlobProperties>> GetPropertiesAsync(
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"{_label}.properties");
            cancellationToken.ThrowIfCancellationRequested();
            PropertiesConditions.Add(conditions);
            RequireExistsAndMatchingEtag(conditions);
            PropertiesContentLengths.Add(Content.LongLength);
            var properties = BlobsModelFactory.BlobProperties(
                contentLength: Content.LongLength,
                eTag: ETag,
                blobType: BlobType,
                versionId: VersionId,
                createdOn: CreatedOn);
            return Task.FromResult(Response.FromValue(
                properties,
                new FakeResponse(ServerDate)));
        }

        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions options,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"{_label}.download");
            cancellationToken.ThrowIfCancellationRequested();
            RequireExistsAndMatchingEtag(options.Conditions);
            var bytes = DownloadBytes ?? Content;
            var details = BlobsModelFactory.BlobDownloadDetails(
                contentLength: Content.LongLength,
                eTag: ETag);
            var result = BlobsModelFactory.BlobDownloadStreamingResult(
                new MemoryStream(bytes, writable: false),
                details);
            return Task.FromResult(Response.FromValue(
                result,
                new FakeResponse(ServerDate)));
        }

        public override Task<Response<bool>> DeleteIfExistsAsync(
            DeleteSnapshotsOption snapshotsOption = DeleteSnapshotsOption.IncludeSnapshots,
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"{_label}.delete");
            cancellationToken.ThrowIfCancellationRequested();
            DeletedSnapshotsOption = snapshotsOption;
            DeleteConditions = conditions;
            DeleteToken = cancellationToken;
            RequireExistsAndMatchingEtag(conditions);
            Present = false;
            return Task.FromResult(Response.FromValue(true, new FakeResponse(ServerDate)));
        }

        private static BlobContentInfo ContentInfo(ETag etag) =>
            BlobsModelFactory.BlobContentInfo(
                eTag: etag,
                lastModified: ObservedAt,
                contentHash: null,
                encryptionKeySha256: null,
                blobSequenceNumber: 0);

        private void RequireExistsAndMatchingEtag(BlobRequestConditions? conditions)
        {
            if (!Present)
            {
                throw new RequestFailedException(404, "Not found");
            }

            if (conditions?.IfMatch is { } expected && !expected.Equals(ETag))
            {
                throw new RequestFailedException(412, "ETag mismatch");
            }
        }
    }

    private sealed class FakePolicyReader(List<string> events) : IAzureCustodyPolicyReader
    {
        public int? NightlyLockedRetentionDays { get; set; } = 91;

        public bool ActiveLegalHold { get; set; } = true;

        /// <summary>
        /// Reports a hold on the nightly lane, which the real reader never does and the
        /// nightly configuration receipt cannot even represent. The store's disjoint-lane
        /// check exists for a reader that misreports, so proving it needs a reader that
        /// misreports: the observation says nightly and carries a legal-hold receipt.
        /// </summary>
        public bool NightlyMisreportsLegalHold { get; set; }

        public DateTimeOffset PolicyObservedAt { get; set; } = ObservedAt;

        public CustodyClass? ReturnedCustodyClass { get; set; }

        public Exception? Exception { get; set; }

        public Exception? StagingException { get; set; }

        public Action<CustodyClass>? AfterRead { get; set; }

        public Guid? ConfigurationPolicyKeyOverride { get; set; }

        public DateTimeOffset? ConfigurationObservedAtOverride { get; set; }

        public Task<AzureContainerPolicyObservation> ReadAsync(
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(custodyClass switch
            {
                CustodyClass.NightlyFloor90d => "policy.nightly",
                CustodyClass.LegalHoldEvidence => "policy.legal_hold",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(custodyClass), custodyClass, "Unknown custody class."),
            });
            if (Exception is not null)
            {
                throw Exception;
            }

            AfterRead?.Invoke(custodyClass);
            var returnedClass = ReturnedCustodyClass ?? custodyClass;
            var misreported = NightlyMisreportsLegalHold
                && returnedClass == CustodyClass.NightlyFloor90d;
            var activeLegalHold = misreported
                || (returnedClass == CustodyClass.LegalHoldEvidence && ActiveLegalHold);
            // The observation keeps its locked retention, so the retention disjunct passes
            // and only the hold disjunct can refuse it. The receipt is built as a legal-hold
            // receipt because a nightly receipt cannot represent a hold at all, which is the
            // shape of a reader that misreports.
            var retentionDays = returnedClass == CustodyClass.NightlyFloor90d
                ? NightlyLockedRetentionDays
                : null;
            var configurationReceipt = ConfigurationReceipt(
                misreported ? CustodyClass.LegalHoldEvidence : returnedClass,
                misreported ? null : retentionDays,
                activeLegalHold,
                ConfigurationPolicyKeyOverride,
                ConfigurationObservedAtOverride ?? PolicyObservedAt);
            return Task.FromResult(new AzureContainerPolicyObservation(
                returnedClass,
                PolicyObservedAt,
                retentionDays,
                activeLegalHold,
                configurationReceipt));
        }

        public Task VerifyPrivateStagingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("policy.staging");
            if (StagingException is not null)
            {
                throw StagingException;
            }

            return Task.CompletedTask;
        }

        private static AzureCustodyConfigurationReceipt ConfigurationReceipt(
            CustodyClass custodyClass,
            int? retentionDays,
            bool activeLegalHold,
            Guid? policyKeyOverride,
            DateTimeOffset observedAt) => new(
                AzureCustodySchemaIds.ConfigurationReceipt,
                policyKeyOverride ?? (custodyClass == CustodyClass.NightlyFloor90d
                    ? NightlyPolicyKey
                    : LegalHoldPolicyKey),
                custodyClass,
                observedAt,
                $"/subscriptions/{SubscriptionId:D}/resourceGroups/rg-lex-v3/providers/Microsoft.Storage/storageAccounts/stlexv3custody/blobServices/default/containers/"
                    + (custodyClass == CustodyClass.NightlyFloor90d ? "nightly" : "legal-hold"),
                "2025-06-01",
                "\"resource-etag\"",
                "7e9f7c8e-4f47-4c39-bd39-10844679e12f",
                ManagedIdentityClientId,
                "None",
                immutableStorageWithVersioningEnabled: false,
                migrationState: null,
                immutabilityPolicyEtag: custodyClass == CustodyClass.NightlyFloor90d
                    ? "\"policy-etag\""
                    : null,
                immutabilityPolicyState: custodyClass == CustodyClass.NightlyFloor90d
                    ? "Locked"
                    : null,
                retentionDays: custodyClass == CustodyClass.NightlyFloor90d
                    ? retentionDays ?? 91
                    : null,
                protectedAppendWrites: false,
                protectedAppendWritesAll: false,
                activeLegalHold: custodyClass == CustodyClass.LegalHoldEvidence,
                protectedBlockBlobAppends: false);
    }

    private sealed class FakeConfigurationJournal(List<string> events)
        : IAzureCustodyConfigurationReceiptJournal
    {
        public Exception? Exception { get; set; }

        public Action? AfterAppend { get; set; }

        public List<AzureCustodyConfigurationReceipt> Receipts { get; } = [];

        public Task AppendAsync(
            AzureCustodyConfigurationReceipt receipt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("configuration.append");
            if (Exception is not null)
            {
                throw Exception;
            }

            Receipts.Add(receipt);
            AfterAppend?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTokenCredential(List<string> events) : TokenCredential
    {
        public const string TokenValue = "fake-storage-token";

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("credential");
            Assert.AreEqual("https://storage.azure.com/.default", requestContext.Scopes.Single());
            return new AccessToken(TokenValue, ObservedAt.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }
    }

    private sealed class FakeResponse : Response
    {
        private readonly Dictionary<string, string> _headers =
            new(StringComparer.OrdinalIgnoreCase);
        private Stream? _contentStream;
        private string _clientRequestId = string.Empty;

        public FakeResponse(DateTimeOffset? serverDate = null, int status = 200)
        {
            Status = status;
            if (serverDate is not null)
            {
                _headers.Add("Date", serverDate.Value.ToString("R"));
            }
        }

        public override int Status { get; }

        public override string ReasonPhrase => "Synthetic response";

        public override Stream? ContentStream
        {
            get => _contentStream;
            set => _contentStream = value;
        }

        public override string ClientRequestId
        {
            get => _clientRequestId;
            set => _clientRequestId = value;
        }

        public override void Dispose()
        {
            _contentStream?.Dispose();
        }

        protected override bool ContainsHeader(string name) => _headers.ContainsKey(name);

        protected override IEnumerable<HttpHeader> EnumerateHeaders() =>
            _headers.Select(pair => new HttpHeader(pair.Key, pair.Value));

        protected override bool TryGetHeader(
            string name,
            [NotNullWhen(true)] out string? value) =>
            _headers.TryGetValue(name, out value);

        protected override bool TryGetHeaderValues(
            string name,
            [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            if (_headers.TryGetValue(name, out var value))
            {
                values = [value];
                return true;
            }

            values = null;
            return false;
        }
    }
}
