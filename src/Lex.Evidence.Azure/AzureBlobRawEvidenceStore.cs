using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Lex.Law;
using System.Runtime.ExceptionServices;

namespace Lex.Evidence.Azure;

internal sealed class AzureBlobRawEvidenceStore : IAzureRawEvidenceStore
{
    private readonly BlobContainerClient _container;

    public AzureBlobRawEvidenceStore(Uri containerUri, TokenCredential credential)
        : this(new BlobContainerClient(
            containerUri,
            credential,
            CreateClientOptions()))
    {
    }

    internal AzureBlobRawEvidenceStore(BlobContainerClient container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    internal static BlobClientOptions CreateClientOptions()
    {
        var options = new BlobClientOptions
        {
            // No secondary is configured. Evidence readback must come from the primary.
            // https://learn.microsoft.com/dotnet/api/azure.storage.blobs.blobclientoptions.georedundantsecondaryuri
            GeoRedundantSecondaryUri = null,
        };
        options.Retry.MaxRetries = 0;
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);
        options.Diagnostics.IsLoggingContentEnabled = false;
        options.Diagnostics.LoggedHeaderNames.Clear();
        options.Diagnostics.LoggedQueryParameters.Clear();
        return options;
    }

    public async Task VerifyCreatePrerequisitesAsync(
        EvidenceRetentionLane retentionLane,
        CancellationToken cancellationToken)
    {
        ValidateRetentionLane(retentionLane);
        if (retentionLane != EvidenceRetentionLane.Nightly90Days) return;
        try
        {
            var properties = await _container.GetPropertiesAsync(
                conditions: null,
                cancellationToken).ConfigureAwait(false);
            RequireNightlyContainerPolicy(
                properties.Value.HasImmutabilityPolicy == true);
        }
        catch (Exception error)
        {
            throw MapFailure(error, cancellationToken);
        }
    }

    public async Task<AzureEvidenceObjectVersion> CreateOnlyAsync(
        string blobName,
        Stream content,
        IReadOnlyDictionary<string, string> metadata,
        EvidenceRetentionLane retentionLane,
        CancellationToken cancellationToken)
    {
        ValidateRetentionLane(retentionLane);
        try
        {
            var response = await _container.GetBlobClient(blobName).UploadAsync(
                content,
                CreateUploadOptions(metadata, retentionLane),
                cancellationToken).ConfigureAwait(false);
            return RequireVersion(
                response.Value.VersionId,
                response.Value.ETag.ToString(),
                response.Value.LastModified);
        }
        catch (Exception error)
        {
            throw MapFailure(error, cancellationToken, isCreate: true);
        }
    }

    public async Task<AzureEvidenceObjectVersion> ResolveCurrentVersionAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.GetBlobClient(blobName)
                .GetPropertiesAsync(
                    conditions: null,
                    cancellationToken).ConfigureAwait(false);
            return RequireVersion(
                response.Value.VersionId,
                response.Value.ETag.ToString(),
                response.Value.LastModified);
        }
        catch (Exception error)
        {
            throw MapFailure(
                error, cancellationToken, missingIsAmbiguous: true);
        }
    }

    public async Task<AzureEvidenceReadback> ReadbackAsync(
        string blobName,
        AzureEvidenceObjectVersion version,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await VersionClient(blobName, version.VersionId)
                .DownloadStreamingAsync(
                    CreateDownloadOptions(version.ETag),
                    cancellationToken).ConfigureAwait(false);
            var download = response.Value;
            return await TakeReadbackOwnershipAsync(
                download.Content,
                download.Details.ContentLength,
                new Dictionary<string, string>(download.Details.Metadata),
                download.Details.VersionId,
                download.Details.ETag.ToString()).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            throw MapFailure(error, cancellationToken);
        }
    }

    public async Task<AzureEvidenceRetentionFacts> ReadRetentionAsync(
        string blobName,
        AzureEvidenceObjectVersion version,
        AzureEvidenceRetentionRequest expectedRetention,
        CancellationToken cancellationToken)
    {
        try
        {
            var blob = VersionClient(blobName, version.VersionId);
            var response = await blob.GetPropertiesAsync(
                conditions: null,
                cancellationToken).ConfigureAwait(false);
            var properties = response.Value;
            return new AzureEvidenceRetentionFacts(
                RequireValue(properties.VersionId),
                properties.ImmutabilityPolicy?.ExpiresOn,
                properties.ImmutabilityPolicy?.PolicyMode?.ToString(),
                properties.HasLegalHold);
        }
        catch (Exception error)
        {
            throw MapFailure(error, cancellationToken);
        }
    }

    private BlobClient VersionClient(string blobName, string versionId) =>
        _container.GetBlobClient(blobName).WithVersion(versionId);

    internal static BlobUploadOptions CreateUploadOptions(
        IReadOnlyDictionary<string, string> metadata,
        EvidenceRetentionLane retentionLane)
    {
        ValidateRetentionLane(retentionLane);
        return new BlobUploadOptions
        {
            Metadata = new Dictionary<string, string>(metadata),
            TransferOptions = new StorageTransferOptions
            {
                InitialTransferSize = EvidenceRef.MaximumByteLength,
                MaximumTransferSize = EvidenceRef.MaximumByteLength,
                MaximumConcurrency = 1,
            },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
            // Nightly blobs must inherit the provisioned locked 90-day
            // container default. A per-blob upload override is always unlocked.
            ImmutabilityPolicy = null,
            LegalHold = retentionLane == EvidenceRetentionLane.EvidenceReleaseIndefinite,
        };
    }

    internal static BlobDownloadOptions CreateDownloadOptions(string etag) =>
        new()
        {
            Conditions = new BlobRequestConditions
            {
                IfMatch = new ETag(etag),
            },
        };

    internal static async Task<AzureEvidenceReadback> TakeReadbackOwnershipAsync(
        Stream content,
        long contentLength,
        IReadOnlyDictionary<string, string> metadata,
        string? versionId,
        string? etag)
    {
        try
        {
            return new AzureEvidenceReadback(
                content,
                contentLength,
                metadata,
                RequireValue(versionId),
                RequireValue(etag));
        }
        catch (Exception error)
        {
            try
            {
                await content.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // The constructor/validation failure has precedence.
            }
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
    }

    private static void ValidateRetentionLane(EvidenceRetentionLane retentionLane)
    {
        if (retentionLane is not EvidenceRetentionLane.Nightly90Days
            and not EvidenceRetentionLane.EvidenceReleaseIndefinite)
            throw new InvalidDataException(
                "The evidence retention lane is not supported.");
    }

    internal static void RequireNightlyContainerPolicy(bool hasPolicy)
    {
        if (!hasPolicy)
            throw new AzureEvidenceStoreException(
                AzureEvidenceStoreFailureKind.Rejected);
    }

    private static AzureEvidenceObjectVersion RequireVersion(
        string? versionId,
        string? etag,
        DateTimeOffset createdAt) =>
        new(RequireValue(versionId), RequireValue(etag), createdAt);

    private static string RequireValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 512
            || value.Any(character => character is < ' ' or > '~'))
            throw new AzureEvidenceStoreException(
                AzureEvidenceStoreFailureKind.Rejected);
        return value;
    }

    internal static Exception MapFailure(
        Exception error,
        CancellationToken cancellationToken,
        bool missingIsAmbiguous = false,
        bool isCreate = false)
    {
        if (error is OperationCanceledException)
            return cancellationToken.IsCancellationRequested
                ? new OperationCanceledException(
                    "Azure evidence operation was canceled.",
                    innerException: null,
                    token: cancellationToken)
                : new AzureEvidenceStoreException(
                    AzureEvidenceStoreFailureKind.Ambiguous);
        if (error is AzureEvidenceStoreException) return error;
        if (error is RequestFailedException failed)
        {
            if (isCreate && failed.Status == 412)
                return new AzureEvidenceStoreException(
                    AzureEvidenceStoreFailureKind.AlreadyExists);
            if (missingIsAmbiguous && failed.Status == 404)
                return new AzureEvidenceStoreException(
                    AzureEvidenceStoreFailureKind.Ambiguous);
            if (failed.Status is 0 or 408 or 429 || failed.Status >= 500)
                return new AzureEvidenceStoreException(
                    AzureEvidenceStoreFailureKind.Ambiguous);
            return new AzureEvidenceStoreException(
                AzureEvidenceStoreFailureKind.Rejected);
        }
        if (error is IOException or TimeoutException)
            return new AzureEvidenceStoreException(
                AzureEvidenceStoreFailureKind.Ambiguous);
        return new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Rejected);
    }
}
