using System.Buffers;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Custody.Azure;

/// <summary>
/// An Azure Blob adapter that stages and verifies bytes before publishing a unique immutable
/// generation under their content digest.
/// </summary>
public sealed class AzureBlobCustodyStore : ICustodyStore
{
    private const string StorageScope = "https://storage.azure.com/.default";
    private static readonly TimeSpan NightlyFloor = TimeSpan.FromDays(90);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);
    private static readonly Meter CustodyMeter = new("Lex.V3.Custody", "1.0.0");
    private static readonly Counter<long> CleanupFailures = CustodyMeter.CreateCounter<long>(
        "lex.v3.custody.staging_cleanup_failures",
        description: "Staging objects that could not be removed after a custody attempt.");

    private readonly AzureBlobCustodyOptions _options;
    private readonly BlobContainerClient _staging;
    private readonly BlobContainerClient _nightly;
    private readonly BlobContainerClient _legalHold;
    private readonly TokenCredential _credential;
    private readonly IAzureCustodyPolicyReader _policyReader;
    private readonly IAzureCustodyConfigurationReceiptJournal _configurationJournal;

    public AzureBlobCustodyStore(
        AzureBlobCustodyOptions options,
        IAzureCustodyConfigurationReceiptJournal configurationJournal)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _configurationJournal = configurationJournal
            ?? throw new ArgumentNullException(nameof(configurationJournal));
        _credential = new ManagedIdentityCredential(
            ManagedIdentityId.FromUserAssignedClientId(
                options.ManagedIdentityClientId.ToString("D", CultureInfo.InvariantCulture)));
        var service = new BlobServiceClient(options.ServiceUri, _credential);
        _staging = service.GetBlobContainerClient(options.StagingContainer);
        _nightly = service.GetBlobContainerClient(options.NightlyContainer);
        _legalHold = service.GetBlobContainerClient(options.LegalHoldContainer);
        _policyReader = new AzureArmCustodyPolicyReader(options, _credential);
    }

    internal AzureBlobCustodyStore(
        AzureBlobCustodyOptions options,
        BlobContainerClient staging,
        BlobContainerClient nightly,
        BlobContainerClient legalHold,
        TokenCredential credential,
        IAzureCustodyPolicyReader policyReader,
        IAzureCustodyConfigurationReceiptJournal configurationJournal)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _nightly = nightly ?? throw new ArgumentNullException(nameof(nightly));
        _legalHold = legalHold ?? throw new ArgumentNullException(nameof(legalHold));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _policyReader = policyReader ?? throw new ArgumentNullException(nameof(policyReader));
        _configurationJournal = configurationJournal
            ?? throw new ArgumentNullException(nameof(configurationJournal));
    }

    public async Task<DurableBlobWriteReceipt> CreateAsync(
        ReadOnlyMemory<byte> bytes,
        CustodyClass custodyClass,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CreateCoreAsync(bytes, custodyClass, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            throw new CustodyIntegrityException(
                "Azure refused an ETag-bound custody operation.", exception);
        }
        catch (Exception exception)
            when (exception is not (CustodyRequiredException
                or CustodyIntegrityException
                or CustodyPolicyException
                or ArgumentException))
        {
            throw new CustodyRequiredException(
                "Azure custody was unavailable, so no receipt can be issued.", exception);
        }
    }

    public async Task<ReadOnlyMemory<byte>> ReadAsync(
        DurableBlobRef reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var container = DurableContainer(reference.CustodyClass);
            var names = await ListGenerationNamesAsync(
                    container, reference.ContentSha256, cancellationToken)
                .ConfigureAwait(false);

            ReadOnlyMemory<byte>? selected = null;
            foreach (var name in names)
            {
                var observation = await ReadExactAsync(
                        container.GetBlockBlobClient(name),
                        reference,
                        expectedETag: null,
                        retainBytes: selected is null,
                        cancellationToken)
                    .ConfigureAwait(false);
                selected ??= observation.Bytes;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return selected
                ?? throw new CustodyIntegrityException("A promised Azure custody object is missing.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is not (CustodyRequiredException
                or CustodyIntegrityException
                or CustodyPolicyException
                or ArgumentException))
        {
            throw new CustodyRequiredException(
                "Azure custody was unavailable while restoring retained bytes.", exception);
        }
    }

    private async Task<DurableBlobWriteReceipt> CreateCoreAsync(
        ReadOnlyMemory<byte> bytes,
        CustodyClass custodyClass,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCreate(bytes, custodyClass);

        var frozen = bytes.ToArray();
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            CustodyDigest.Of(frozen, cancellationToken),
            frozen.LongLength,
            custodyClass);
        var durable = DurableContainer(custodyClass);
        var candidateNames = await ListGenerationNamesAsync(
                durable, reference.ContentSha256, cancellationToken)
            .ConfigureAwait(false);

        var observations = new List<RemoteObservation>();
        foreach (var name in candidateNames)
        {
            var observation = await ReadExactAsync(
                    durable.GetBlockBlobClient(name),
                    reference,
                    expectedETag: null,
                    retainBytes: false,
                    cancellationToken)
                .ConfigureAwait(false);
            observations.Add(observation);
        }

        var policy = await _policyReader.ReadAsync(custodyClass, cancellationToken)
            .ConfigureAwait(false);
        DurableBlobWriteReceipt? reusableReceipt = null;
        foreach (var observation in observations)
        {
            await RevalidateExactGenerationAsync(observation, policy.ObservedAt, cancellationToken)
                .ConfigureAwait(false);
            if (TryCreateReceipt(reference, observation, policy, out var existingReceipt))
            {
                reusableReceipt ??= existingReceipt;
            }
        }

        if (reusableReceipt is not null)
        {
            await _configurationJournal.AppendAsync(
                    policy.ConfigurationReceipt, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return reusableReceipt;
        }

        await _policyReader.VerifyPrivateStagingAsync(cancellationToken).ConfigureAwait(false);

        BlockBlobClient? stage = null;
        ETag? stageCreatedETag = null;
        DurableBlobWriteReceipt? createdReceipt = null;
        try
        {
            stage = _staging.GetBlockBlobClient($"pending/{Guid.NewGuid():N}");
            await using var upload = new MemoryStream(frozen, writable: false);
            var uploaded = await stage.UploadAsync(
                    upload,
                    new BlobUploadOptions
                    {
                        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                        TransferValidation = new UploadTransferValidationOptions
                        {
                            ChecksumAlgorithm = StorageChecksumAlgorithm.StorageCrc64,
                        },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            var stageETag = RequireETag(uploaded.Value.ETag, "The staging upload returned no ETag.");
            stageCreatedETag = stageETag;

            _ = await ReadExactAsync(
                    stage, reference, stageETag, retainBytes: false, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var accessToken = await _credential.GetTokenAsync(
                    new TokenRequestContext([StorageScope]), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var generationName = $"{reference.ContentSha256}/g/{Guid.NewGuid():N}";
            var generation = durable.GetBlockBlobClient(generationName);
            var copied = await generation.SyncUploadFromUriAsync(
                    stage.Uri,
                    new BlobSyncUploadFromUriOptions
                    {
                        CopySourceBlobProperties = false,
                        SourceAuthentication = new HttpAuthorization("Bearer", accessToken.Token),
                        SourceConditions = new BlobRequestConditions { IfMatch = stageETag },
                        DestinationConditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            var finalETag = RequireETag(copied.Value.ETag, "The durable copy returned no ETag.");
            cancellationToken.ThrowIfCancellationRequested();

            var finalObservation = await ReadExactAsync(
                    generation, reference, finalETag, retainBytes: false, cancellationToken)
                .ConfigureAwait(false);
            var finalPolicy = await _policyReader.ReadAsync(custodyClass, cancellationToken)
                .ConfigureAwait(false);
            await RevalidateExactGenerationAsync(
                    finalObservation, finalPolicy.ObservedAt, cancellationToken)
                .ConfigureAwait(false);
            if (!TryCreateReceipt(reference, finalObservation, finalPolicy, out var receipt))
            {
                throw new CustodyPolicyException(
                    "The final Azure object did not prove the protection required by its custody lane.");
            }

            await _configurationJournal.AppendAsync(
                    finalPolicy.ConfigurationReceipt, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            createdReceipt = receipt;
        }
        finally
        {
            if (stage is not null && stageCreatedETag is not null)
            {
                await CleanupStageAsync(stage, stageCreatedETag.Value).ConfigureAwait(false);
            }
            else if (stage is not null)
            {
                RecordCleanupFailure();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return createdReceipt
            ?? throw new CustodyIntegrityException("Azure custody produced no write receipt.");
    }

    private async Task<IReadOnlyList<string>> ListGenerationNamesAsync(
        BlobContainerClient container,
        string digest,
        CancellationToken cancellationToken)
    {
        var prefix = $"{digest}/";
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var item in container.GetBlobsAsync(
                           BlobTraits.None,
                           BlobStates.None,
                           prefix,
                           cancellationToken)
                       .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsGenerationName(item.Name, digest))
            {
                throw new CustodyIntegrityException(
                    "An unrecognised object occupies a durable digest prefix.");
            }

            if (!seen.Add(item.Name))
            {
                throw new CustodyIntegrityException(
                    "A durable enumeration returned the same generation more than once.");
            }

            names.Add(item.Name);
        }

        return names;
    }

    private static async Task<RemoteObservation> ReadExactAsync(
        BlockBlobClient blob,
        DurableBlobRef reference,
        ETag? expectedETag,
        bool retainBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var propertyConditions = expectedETag is null
                ? null
                : new BlobRequestConditions { IfMatch = expectedETag.Value };
            var propertyResponse = await blob.GetPropertiesAsync(
                    propertyConditions, cancellationToken)
                .ConfigureAwait(false);
            var properties = propertyResponse.Value;
            if (properties.BlobType != BlobType.Block
                || !string.IsNullOrEmpty(properties.VersionId))
            {
                throw new CustodyIntegrityException(
                    "Azure custody requires an unversioned block blob in a container-level WORM lane.");
            }

            if (properties.ContentLength != reference.ByteLength)
            {
                throw new CustodyIntegrityException(
                    "An Azure custody object has the wrong byte length.");
            }

            var observedETag = RequireETag(
                properties.ETag, "An Azure custody object returned no ETag.");
            if (expectedETag is not null && !observedETag.Equals(expectedETag.Value))
            {
                throw new CustodyIntegrityException(
                    "An Azure custody object changed before verification.");
            }

            var downloadResponse = await blob.DownloadStreamingAsync(
                    new BlobDownloadOptions
                    {
                        Conditions = new BlobRequestConditions { IfMatch = observedETag },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!downloadResponse.Value.Details.ETag.Equals(observedETag))
            {
                throw new CustodyIntegrityException(
                    "The downloaded Azure object does not match the inspected generation.");
            }

            await using var stream = downloadResponse.Value.Content;
            var exact = await ReadAndVerifyAsync(
                    stream, reference, retainBytes, cancellationToken)
                .ConfigureAwait(false);

            return new RemoteObservation(
                blob,
                exact,
                properties);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new CustodyIntegrityException(
                "An enumerated Azure custody object disappeared before verification.", exception);
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            throw new CustodyIntegrityException(
                "An Azure custody object changed during ETag-bound verification.", exception);
        }
    }

    private static async Task<ReadOnlyMemory<byte>> ReadAndVerifyAsync(
        Stream stream,
        DurableBlobRef reference,
        bool retainBytes,
        CancellationToken cancellationToken)
    {
        if (retainBytes)
        {
            var exact = GC.AllocateUninitializedArray<byte>(checked((int)reference.ByteLength));
            try
            {
                await stream.ReadExactlyAsync(exact, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException exception)
            {
                throw new CustodyIntegrityException(
                    "The Azure custody object ended before its declared length.", exception);
            }

            await RequireEndOfStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    CustodyDigest.Of(exact, cancellationToken),
                    reference.ContentSha256,
                    StringComparison.Ordinal))
            {
                throw new CustodyIntegrityException(
                    "The Azure custody object bytes do not match their durable reference.");
            }

            return exact;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var remaining = reference.ByteLength;
            while (remaining > 0)
            {
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = await stream.ReadAsync(
                        buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new CustodyIntegrityException(
                        "The Azure custody object ended before its declared length.");
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            await RequireEndOfStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, reference.ContentSha256, StringComparison.Ordinal))
            {
                throw new CustodyIntegrityException(
                    "The Azure custody object bytes do not match their durable reference.");
            }

            return ReadOnlyMemory<byte>.Empty;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static async Task RequireEndOfStreamAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var sentinel = new byte[1];
        if (await stream.ReadAsync(sentinel, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new CustodyIntegrityException(
                "The Azure custody object bytes do not match their durable reference.");
        }
    }

    private bool TryCreateReceipt(
        DurableBlobRef reference,
        RemoteObservation observation,
        AzureContainerPolicyObservation policy,
        out DurableBlobWriteReceipt receipt)
    {
        receipt = null!;
        if (policy.ConfigurationReceipt is null
            || policy.CustodyClass != reference.CustodyClass
            || policy.ConfigurationReceipt.CustodyClass != reference.CustodyClass
            || policy.ConfigurationReceipt.ObservedAt != policy.ObservedAt
            || policy.ConfigurationReceipt.RetentionDays != policy.LockedRetentionDays
            || policy.ConfigurationReceipt.ActiveLegalHold != policy.ActiveLegalHold
            || observation.Properties.CreatedOn == default)
        {
            return false;
        }

        var observedAt = policy.ObservedAt.ToUniversalTime();
        var createdOn = observation.Properties.CreatedOn.ToUniversalTime();
        if (createdOn > observedAt)
        {
            return false;
        }

        CustodyProtection protection;
        DateTimeOffset? protectedUntil;
        Guid policyKey;
        switch (reference.CustodyClass)
        {
            case CustodyClass.NightlyFloor90d:
                if (policy.LockedRetentionDays is null || policy.ActiveLegalHold)
                {
                    return false;
                }

                try
                {
                    protectedUntil = createdOn.AddDays(policy.LockedRetentionDays.Value);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }

                if (protectedUntil.Value - observedAt < NightlyFloor)
                {
                    return false;
                }

                protection = CustodyProtection.LockedTime;
                policyKey = _options.NightlyPolicyKey;
                break;

            case CustodyClass.LegalHoldEvidence:
                if (!policy.ActiveLegalHold || policy.LockedRetentionDays is not null)
                {
                    return false;
                }

                protection = CustodyProtection.ActiveLegalHold;
                protectedUntil = null;
                policyKey = _options.LegalHoldPolicyKey;
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(reference), reference.CustodyClass, "Unknown custody class.");
        }

        if (policy.ConfigurationReceipt.PolicyKey != policyKey)
        {
            return false;
        }

        var evidence = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.ImmutableObject1,
            policyKey,
            protection,
            observedAt,
            protectedUntil);
        receipt = new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            evidence);
        return true;
    }

    private static async Task RevalidateExactGenerationAsync(
        RemoteObservation observation,
        DateTimeOffset policyObservedAt,
        CancellationToken cancellationToken)
    {
        var originalETag = RequireETag(
            observation.Properties.ETag,
            "An Azure custody object returned no ETag.");
        try
        {
            var response = await observation.Blob.GetPropertiesAsync(
                    new BlobRequestConditions { IfMatch = originalETag }, cancellationToken)
                .ConfigureAwait(false);
            var current = response.Value;
            var revalidatedAt = TryReadServerDate(response.GetRawResponse());
            if (revalidatedAt is null || revalidatedAt.Value < policyObservedAt.ToUniversalTime())
            {
                throw new CustodyPolicyException(
                    "The exact Azure object was not observed after its container policy.");
            }

            if (current.BlobType != BlobType.Block
                || !string.IsNullOrEmpty(current.VersionId)
                || current.ContentLength != observation.Properties.ContentLength
                || current.CreatedOn != observation.Properties.CreatedOn
                || !RequireETag(
                        current.ETag,
                        "An Azure custody object returned no ETag.")
                    .Equals(originalETag))
            {
                throw new CustodyIntegrityException(
                    "An Azure custody object changed across its policy observation.");
            }
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
            throw new CustodyIntegrityException(
                "An Azure custody object changed across its policy observation.", exception);
        }
    }

    private async Task CleanupStageAsync(BlockBlobClient stage, ETag uploadedETag)
    {
        try
        {
            using var timeout = new CancellationTokenSource(CleanupTimeout);
            _ = await stage.DeleteIfExistsAsync(
                    DeleteSnapshotsOption.IncludeSnapshots,
                    new BlobRequestConditions { IfMatch = uploadedETag },
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return;
        }
        catch
        {
            RecordCleanupFailure();
        }
    }

    private void RecordCleanupFailure()
    {
        try
        {
            CleanupFailures.Add(1);
        }
        catch
        {
            // Cleanup telemetry must never replace the operation's primary result.
        }
    }

    private BlobContainerClient DurableContainer(CustodyClass custodyClass) => custodyClass switch
    {
        CustodyClass.NightlyFloor90d => _nightly,
        CustodyClass.LegalHoldEvidence => _legalHold,
        _ => throw new ArgumentOutOfRangeException(
            nameof(custodyClass), custodyClass, "Unknown custody class."),
    };

    private static void ValidateCreate(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass)
    {
        if (bytes.Length > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytes), "A body above the admitted custody bound is refused before Azure is touched.");
        }

        if (!Enum.IsDefined(custodyClass))
        {
            throw new ArgumentOutOfRangeException(
                nameof(custodyClass), custodyClass, "Unknown custody class.");
        }
    }

    private static ETag RequireETag(ETag value, string message)
    {
        if (string.IsNullOrEmpty(value.ToString()))
        {
            throw new CustodyIntegrityException(message);
        }

        return value;
    }

    private static DateTimeOffset? TryReadServerDate(Response response)
    {
        if (!response.Headers.TryGetValue("Date", out var value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return null;
        }

        return parsed.ToUniversalTime();
    }

    private static bool IsGenerationName(string? name, string digest)
    {
        var prefix = $"{digest}/g/";
        if (name is null
            || !name.StartsWith(prefix, StringComparison.Ordinal)
            || name.Length != prefix.Length + 32)
        {
            return false;
        }

        foreach (var character in name.AsSpan(prefix.Length))
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record RemoteObservation(
        BlockBlobClient Blob,
        ReadOnlyMemory<byte> Bytes,
        BlobProperties Properties);
}
