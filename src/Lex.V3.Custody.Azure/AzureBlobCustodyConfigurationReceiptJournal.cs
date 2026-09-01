using System.Globalization;
using System.Text;
using Azure;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Custody.Azure;

/// <summary>
/// Retains private Azure policy observations beside the durable object whose protection they
/// support. Journal objects share the destination container's WORM lifetime and never enter a
/// portable receipt.
/// </summary>
public sealed class AzureBlobCustodyConfigurationReceiptJournal
    : IAzureCustodyConfigurationReceiptJournal
{
    private const int MaxReceiptBytes = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly AzureBlobCustodyOptions _options;
    private readonly BlobContainerClient _nightly;
    private readonly BlobContainerClient _legalHold;

    public AzureBlobCustodyConfigurationReceiptJournal(AzureBlobCustodyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        var credential = new ManagedIdentityCredential(
            ManagedIdentityId.FromUserAssignedClientId(
                options.ManagedIdentityClientId.ToString("D", CultureInfo.InvariantCulture)));
        var service = new BlobServiceClient(options.ServiceUri, credential);
        _nightly = service.GetBlobContainerClient(options.NightlyContainer);
        _legalHold = service.GetBlobContainerClient(options.LegalHoldContainer);
    }

    internal AzureBlobCustodyConfigurationReceiptJournal(
        AzureBlobCustodyOptions options,
        BlobContainerClient nightly,
        BlobContainerClient legalHold)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _nightly = nightly ?? throw new ArgumentNullException(nameof(nightly));
        _legalHold = legalHold ?? throw new ArgumentNullException(nameof(legalHold));
    }

    public async Task AppendAsync(
        AzureCustodyConfigurationReceipt receipt,
        CancellationToken cancellationToken)
    {
        try
        {
            await AppendCoreAsync(receipt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is not (ArgumentException
                or CustodyIntegrityException
                or CustodyRequiredException))
        {
            throw new CustodyRequiredException(
                "Azure custody configuration evidence was unavailable.", exception);
        }
    }

    private async Task AppendCoreAsync(
        AzureCustodyConfigurationReceipt receipt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();

        var lane = SelectLane(receipt);
        var bytes = StrictUtf8.GetBytes(ContractJson.Serialize(receipt));
        if (bytes.Length > MaxReceiptBytes)
        {
            throw new ArgumentException(
                "The Azure custody configuration receipt exceeds its private evidence bound.",
                nameof(receipt));
        }

        var tuplePrefix = string.Create(
            CultureInfo.InvariantCulture,
            $"_configuration/v1/{receipt.PolicyKey:N}/{receipt.ObservedAt.UtcDateTime.Ticks:D19}/{lane.Token}");
        var anchor = lane.Container.GetBlockBlobClient($"{tuplePrefix}/anchor.json");
        await EnsureAnchorAsync(anchor, receipt, bytes, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.Parse(receipt.ArmRequestId).ToString("N");
        var request = lane.Container.GetBlockBlobClient(
            $"{tuplePrefix}/requests/{requestId}.json");
        await EnsureExactAsync(request, bytes, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task EnsureAnchorAsync(
        BlockBlobClient blob,
        AzureCustodyConfigurationReceipt receipt,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var createdETag = await TryCreateAsync(blob, bytes, cancellationToken).ConfigureAwait(false);
        if (createdETag is not null)
        {
            await VerifyExactAsync(blob, createdETag.Value, bytes, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var existingBytes = await ReadBoundedAsync(blob, cancellationToken).ConfigureAwait(false);
        AzureCustodyConfigurationReceipt existing;
        try
        {
            existing = ContractJson.Deserialize<AzureCustodyConfigurationReceipt>(
                StrictUtf8.GetString(existingBytes));
        }
        catch (Exception exception) when (exception is ArgumentException
            or DecoderFallbackException
            or System.Text.Json.JsonException)
        {
            throw new CustodyIntegrityException(
                "An existing Azure custody configuration anchor is malformed.", exception);
        }

        if (!SameNormalizedFacts(existing, receipt))
        {
            throw new CustodyIntegrityException(
                "Azure custody configuration facts conflict for one policy observation tuple.");
        }
    }

    private async Task EnsureExactAsync(
        BlockBlobClient blob,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var createdETag = await TryCreateAsync(blob, bytes, cancellationToken).ConfigureAwait(false);
        if (createdETag is not null)
        {
            await VerifyExactAsync(blob, createdETag.Value, bytes, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var existing = await ReadBoundedAsync(blob, cancellationToken).ConfigureAwait(false);
        if (!existing.AsSpan().SequenceEqual(bytes))
        {
            throw new CustodyIntegrityException(
                "An Azure custody configuration request identity has conflicting bytes.");
        }
    }

    private static async Task<ETag?> TryCreateAsync(
        BlockBlobClient blob,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new MemoryStream(bytes, writable: false);
            var response = await blob.UploadAsync(
                    stream,
                    new BlobUploadOptions
                    {
                        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                        TransferValidation = new UploadTransferValidationOptions
                        {
                            ChecksumAlgorithm = StorageChecksumAlgorithm.StorageCrc64,
                        },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return RequireETag(response.Value.ETag);
        }
        catch (RequestFailedException exception) when (IsCreateCollision(exception))
        {
            return null;
        }
    }

    private static async Task VerifyExactAsync(
        BlockBlobClient blob,
        ETag expectedETag,
        byte[] expected,
        CancellationToken cancellationToken)
    {
        var actual = await ReadBoundedAsync(blob, expectedETag, cancellationToken)
            .ConfigureAwait(false);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new CustodyIntegrityException(
                "Azure custody configuration evidence changed during durable publication.");
        }
    }

    private static Task<byte[]> ReadBoundedAsync(
        BlockBlobClient blob,
        CancellationToken cancellationToken) =>
        ReadBoundedAsync(blob, expectedETag: null, cancellationToken);

    private static async Task<byte[]> ReadBoundedAsync(
        BlockBlobClient blob,
        ETag? expectedETag,
        CancellationToken cancellationToken)
    {
        try
        {
            var conditions = expectedETag is null
                ? null
                : new BlobRequestConditions { IfMatch = expectedETag.Value };
            var propertyResponse = await blob.GetPropertiesAsync(conditions, cancellationToken)
                .ConfigureAwait(false);
            var properties = propertyResponse.Value;
            var observedETag = RequireETag(properties.ETag);
            if (expectedETag is not null && !observedETag.Equals(expectedETag.Value))
            {
                throw new CustodyIntegrityException(
                    "Azure custody configuration evidence changed before verification.");
            }

            // A current blob has a version ID when account versioning is enabled.
            // Source: https://learn.microsoft.com/azure/storage/blobs/versioning-overview
            if (properties.BlobType != BlobType.Block
                || properties.ContentLength is < 1 or > MaxReceiptBytes)
            {
                throw new CustodyIntegrityException(
                    "Azure custody configuration evidence has an invalid durable shape.");
            }

            var download = await blob.DownloadStreamingAsync(
                    new BlobDownloadOptions
                    {
                        Conditions = new BlobRequestConditions { IfMatch = observedETag },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!download.Value.Details.ETag.Equals(observedETag))
            {
                throw new CustodyIntegrityException(
                    "Downloaded Azure custody configuration evidence changed generation.");
            }

            var bytes = GC.AllocateUninitializedArray<byte>(checked((int)properties.ContentLength));
            await using var stream = download.Value.Content;
            try
            {
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException exception)
            {
                throw new CustodyIntegrityException(
                    "Azure custody configuration evidence ended before its declared length.",
                    exception);
            }

            var sentinel = new byte[1];
            if (await stream.ReadAsync(sentinel, cancellationToken).ConfigureAwait(false) != 0)
            {
                throw new CustodyIntegrityException(
                    "Azure custody configuration evidence exceeds its declared length.");
            }

            return bytes;
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
            throw new CustodyIntegrityException(
                "Azure custody configuration evidence disappeared or changed.", exception);
        }
    }

    private Lane SelectLane(AzureCustodyConfigurationReceipt receipt)
    {
        var lane = receipt.CustodyClass switch
        {
            CustodyClass.NightlyFloor90d =>
                new Lane(
                    _nightly,
                    _options.NightlyContainer,
                    _options.NightlyPolicyKey,
                    "nightly_floor_90d"),
            CustodyClass.LegalHoldEvidence =>
                new Lane(
                    _legalHold,
                    _options.LegalHoldContainer,
                    _options.LegalHoldPolicyKey,
                    "legal_hold_evidence"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(receipt), receipt.CustodyClass, "Unknown custody class."),
        };
        if (receipt.PolicyKey != lane.PolicyKey)
        {
            throw new CustodyIntegrityException(
                "The Azure custody configuration receipt names the wrong policy lane.");
        }

        if (receipt.ManagedIdentityClientId != _options.ManagedIdentityClientId
            || !string.Equals(
                receipt.ArmResourceId,
                ResourceId(lane.ContainerName),
                StringComparison.Ordinal))
        {
            throw new CustodyIntegrityException(
                "The Azure custody configuration receipt does not match the configured destination and identity.");
        }

        return lane;
    }

    private string ResourceId(string container) =>
        $"/subscriptions/{_options.SubscriptionId:D}"
        + $"/resourceGroups/{_options.ResourceGroup}"
        + "/providers/Microsoft.Storage/storageAccounts/"
        + _options.StorageAccountName
        + "/blobServices/default/containers/"
        + container;

    private static bool SameNormalizedFacts(
        AzureCustodyConfigurationReceipt left,
        AzureCustodyConfigurationReceipt right) =>
        left.PolicyKey == right.PolicyKey
        && left.ObservedAt == right.ObservedAt
        && string.Equals(left.ArmResourceId, right.ArmResourceId, StringComparison.Ordinal)
        && string.Equals(left.ArmResourceEtag, right.ArmResourceEtag, StringComparison.Ordinal)
        && left.ManagedIdentityClientId == right.ManagedIdentityClientId
        && string.Equals(
            left.ImmutabilityPolicyEtag,
            right.ImmutabilityPolicyEtag,
            StringComparison.Ordinal)
        && left.RetentionDays == right.RetentionDays;

    private static bool IsCreateCollision(RequestFailedException exception) =>
        exception.Status == 412
        || exception.Status == 409
            && string.Equals(exception.ErrorCode, "BlobAlreadyExists", StringComparison.Ordinal);

    private static ETag RequireETag(ETag value)
    {
        if (string.IsNullOrEmpty(value.ToString()))
        {
            throw new CustodyIntegrityException(
                "Azure custody configuration evidence returned no ETag.");
        }

        return value;
    }

    private sealed record Lane(
        BlobContainerClient Container,
        string ContainerName,
        Guid PolicyKey,
        string Token);
}
