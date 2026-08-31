using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Custody.Azure;

internal interface IAzureCustodyPolicyReader
{
    Task<AzureContainerPolicyObservation> ReadAsync(
        CustodyClass custodyClass,
        CancellationToken cancellationToken);

    Task VerifyPrivateStagingAsync(CancellationToken cancellationToken);
}

internal sealed record AzureContainerPolicyObservation(
    CustodyClass CustodyClass,
    DateTimeOffset ObservedAt,
    int? LockedRetentionDays,
    bool ActiveLegalHold,
    AzureCustodyConfigurationReceipt ConfigurationReceipt);

/// <summary>
/// Reads one exact Azure container resource. The response is private configuration evidence; only
/// its provider-neutral conclusion can enter a portable custody receipt.
/// </summary>
internal sealed class AzureArmCustodyPolicyReader : IAzureCustodyPolicyReader
{
    private const string ArmScope = "https://management.azure.com/.default";
    private const string ApiVersion = "2025-06-01";
    private const int MaximumResponseBytes = 256 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient SharedClient = new(
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
        })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly AzureBlobCustodyOptions _options;
    private readonly TokenCredential _credential;
    private readonly HttpClient _client;

    public AzureArmCustodyPolicyReader(
        AzureBlobCustodyOptions options,
        TokenCredential credential)
        : this(options, credential, SharedClient)
    {
    }

    internal AzureArmCustodyPolicyReader(
        AzureBlobCustodyOptions options,
        TokenCredential credential,
        HttpClient client)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task<AzureContainerPolicyObservation> ReadAsync(
        CustodyClass custodyClass,
        CancellationToken cancellationToken)
    {
        var container = custodyClass switch
        {
            CustodyClass.NightlyFloor90d => _options.NightlyContainer,
            CustodyClass.LegalHoldEvidence => _options.LegalHoldContainer,
            _ => throw new ArgumentOutOfRangeException(
                nameof(custodyClass), custodyClass, "Unknown custody class."),
        };

        return ReadContainerAsync(
            container,
            (root, resourceId, observedAt, armResourceEtag, armRequestId) => Parse(
                root,
                resourceId,
                container,
                custodyClass,
                observedAt,
                armResourceEtag,
                armRequestId),
            cancellationToken);
    }

    public async Task VerifyPrivateStagingAsync(CancellationToken cancellationToken)
    {
        _ = await ReadContainerAsync(
                _options.StagingContainer,
                (root, resourceId, _, armResourceEtag, _) => ParseStaging(
                    root, resourceId, _options.StagingContainer, armResourceEtag),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<T> ReadContainerAsync<T>(
        string container,
        Func<JsonElement, string, DateTimeOffset, string, string, T> parse,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        var token = await _credential.GetTokenAsync(
                new TokenRequestContext([ArmScope]), timeout.Token)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(token.Token))
        {
            throw new CustodyRequiredException(
                "Azure returned no management token for custody-policy verification.");
        }

        var resourceId = ResourceId(container);
        var uri = new Uri(
            $"https://management.azure.com{RequestPath(container)}?api-version={ApiVersion}",
            UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new CustodyRequiredException(
                "Azure did not return the configured custody policy.");
        }

        if (response.Headers.Date is null)
        {
            throw new CustodyPolicyException(
                "The Azure policy observation carried no authoritative response date.");
        }

        var armResourceEtag = response.Headers.ETag?.ToString();
        if (string.IsNullOrWhiteSpace(armResourceEtag))
        {
            throw new CustodyPolicyException(
                "The Azure policy observation carried no resource ETag.");
        }

        var armRequestId = RequireSingleHeader(response, "x-ms-request-id");

        if (response.Content.Headers.ContentLength is > MaximumResponseBytes
            || !string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new CustodyPolicyException(
                "The Azure policy response has an inadmissible representation.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        var bounded = await ReadBoundedAsync(
                stream, MaximumResponseBytes, timeout.Token)
            .ConfigureAwait(false);
        if (bounded.ExceededLimit)
        {
            throw new CustodyPolicyException("The Azure policy response exceeded its bound.");
        }

        try
        {
            using var document = JsonDocument.Parse(bounded.Bytes);
            return parse(
                document.RootElement,
                resourceId,
                response.Headers.Date.Value.ToUniversalTime(),
                armResourceEtag,
                armRequestId);
        }
        catch (JsonException exception)
        {
            throw new CustodyPolicyException(
                "The Azure policy response was not strict JSON.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyPolicyException(
                "The Azure policy response violated the admitted configuration contract.",
                exception);
        }
    }

    private AzureContainerPolicyObservation Parse(
        JsonElement root,
        string resourceId,
        string container,
        CustodyClass custodyClass,
        DateTimeOffset observedAt,
        string armResourceEtag,
        string armRequestId)
    {
        RequireString(root, "id", resourceId, StringComparison.OrdinalIgnoreCase);
        RequireString(root, "name", container, StringComparison.Ordinal);
        RequireString(
            root,
            "type",
            "Microsoft.Storage/storageAccounts/blobServices/containers",
            StringComparison.OrdinalIgnoreCase);
        RequireMatchingResourceEtag(root, armResourceEtag);
        var properties = RequireObject(root, "properties");
        RequireString(properties, "publicAccess", "None", StringComparison.Ordinal);
        var versioning = RequireObject(properties, "immutableStorageWithVersioning");
        if (RequireBoolean(versioning, "enabled"))
        {
            throw new CustodyPolicyException(
                "Version-level WORM cannot back stable unversioned custody references.");
        }

        if (TryGetUniqueProperty(versioning, "migrationState", out var migrationState)
            && migrationState.ValueKind != JsonValueKind.Null)
        {
            throw new CustodyPolicyException(
                "An object-level immutability migration makes unversioned references unsafe.");
        }

        return custodyClass switch
        {
            CustodyClass.NightlyFloor90d => ParseNightly(
                properties,
                resourceId,
                custodyClass,
                observedAt,
                armResourceEtag,
                armRequestId),
            CustodyClass.LegalHoldEvidence => ParseLegalHold(
                properties,
                resourceId,
                custodyClass,
                observedAt,
                armResourceEtag,
                armRequestId),
            _ => throw new ArgumentOutOfRangeException(
                nameof(custodyClass), custodyClass, "Unknown custody class."),
        };
    }

    private bool ParseStaging(
        JsonElement root,
        string resourceId,
        string container,
        string armResourceEtag)
    {
        RequireString(root, "id", resourceId, StringComparison.OrdinalIgnoreCase);
        RequireString(root, "name", container, StringComparison.Ordinal);
        RequireString(
            root,
            "type",
            "Microsoft.Storage/storageAccounts/blobServices/containers",
            StringComparison.OrdinalIgnoreCase);
        RequireMatchingResourceEtag(root, armResourceEtag);
        var properties = RequireObject(root, "properties");
        RequireString(properties, "publicAccess", "None", StringComparison.Ordinal);
        if (RequireBoolean(properties, "hasImmutabilityPolicy")
            || RequireBoolean(properties, "hasLegalHold"))
        {
            throw new CustodyPolicyException(
                "The staging container must remain private and mutable for bounded cleanup.");
        }

        var versioning = RequireObject(properties, "immutableStorageWithVersioning");
        if (RequireBoolean(versioning, "enabled")
            || TryGetUniqueProperty(versioning, "migrationState", out var migrationState)
                && migrationState.ValueKind != JsonValueKind.Null)
        {
            throw new CustodyPolicyException(
                "The staging container cannot use or migrate to object-level immutability.");
        }

        return true;
    }

    private AzureContainerPolicyObservation ParseNightly(
        JsonElement properties,
        string resourceId,
        CustodyClass custodyClass,
        DateTimeOffset observedAt,
        string armResourceEtag,
        string armRequestId)
    {
        if (!RequireBoolean(properties, "hasImmutabilityPolicy"))
        {
            throw new CustodyPolicyException("The nightly container has no immutability policy.");
        }

        if (RequireBoolean(properties, "hasLegalHold"))
        {
            throw new CustodyPolicyException(
                "The locked-time and legal-hold custody lanes must remain disjoint.");
        }

        var policyResource = RequireObject(properties, "immutabilityPolicy");
        var policyEtag = RequireNonemptyString(policyResource, "etag");
        var policy = RequireObject(policyResource, "properties");
        RequireString(policy, "state", "Locked", StringComparison.Ordinal);
        if (ReadOptionalBoolean(policy, "allowProtectedAppendWrites")
            || ReadOptionalBoolean(policy, "allowProtectedAppendWritesAll"))
        {
            throw new CustodyPolicyException(
                "Protected append writes weaken the nightly exact-byte claim.");
        }

        var daysElement = RequireUniqueProperty(policy, "immutabilityPeriodSinceCreationInDays");
        if (!daysElement.TryGetInt32(out var days) || days is < 1 or > 146_000)
        {
            throw new CustodyPolicyException(
                "The Azure immutability period is outside the admitted range.");
        }

        var receipt = new AzureCustodyConfigurationReceipt(
            AzureCustodySchemaIds.ConfigurationReceipt,
            _options.NightlyPolicyKey,
            custodyClass,
            observedAt,
            resourceId,
            ApiVersion,
            armResourceEtag,
            armRequestId,
            _options.ManagedIdentityClientId,
            "None",
            immutableStorageWithVersioningEnabled: false,
            migrationState: null,
            policyEtag,
            "Locked",
            days,
            protectedAppendWrites: false,
            protectedAppendWritesAll: false,
            activeLegalHold: false,
            protectedBlockBlobAppends: false);
        return new AzureContainerPolicyObservation(
            custodyClass, observedAt, days, ActiveLegalHold: false, receipt);
    }

    private AzureContainerPolicyObservation ParseLegalHold(
        JsonElement properties,
        string resourceId,
        CustodyClass custodyClass,
        DateTimeOffset observedAt,
        string armResourceEtag,
        string armRequestId)
    {
        if (RequireBoolean(properties, "hasImmutabilityPolicy"))
        {
            throw new CustodyPolicyException(
                "The legal-hold custody lane must not also carry an immutability policy.");
        }

        if (!RequireBoolean(properties, "hasLegalHold"))
        {
            throw new CustodyPolicyException("The evidence container has no active legal hold.");
        }

        var legalHold = RequireObject(properties, "legalHold");
        if (!RequireBoolean(legalHold, "hasLegalHold"))
        {
            throw new CustodyPolicyException(
                "The Azure legal-hold summary contradicts its detailed state.");
        }

        if (TryGetUniqueProperty(legalHold, "protectedAppendWritesHistory", out var history)
            && (history.ValueKind != JsonValueKind.Object
                || ReadOptionalBoolean(history, "allowProtectedAppendWritesAll")))
        {
            throw new CustodyPolicyException(
                "Protected block-blob appends weaken the legal-hold exact-byte claim.");
        }

        var receipt = new AzureCustodyConfigurationReceipt(
            AzureCustodySchemaIds.ConfigurationReceipt,
            _options.LegalHoldPolicyKey,
            custodyClass,
            observedAt,
            resourceId,
            ApiVersion,
            armResourceEtag,
            armRequestId,
            _options.ManagedIdentityClientId,
            "None",
            immutableStorageWithVersioningEnabled: false,
            migrationState: null,
            immutabilityPolicyEtag: null,
            immutabilityPolicyState: null,
            retentionDays: null,
            protectedAppendWrites: false,
            protectedAppendWritesAll: false,
            activeLegalHold: true,
            protectedBlockBlobAppends: false);
        return new AzureContainerPolicyObservation(
            custodyClass,
            observedAt,
            LockedRetentionDays: null,
            ActiveLegalHold: true,
            receipt);
    }

    private string ResourceId(string container) =>
        $"/subscriptions/{_options.SubscriptionId:D}"
        + $"/resourceGroups/{_options.ResourceGroup}"
        + "/providers/Microsoft.Storage/storageAccounts/"
        + _options.StorageAccountName
        + "/blobServices/default/containers/"
        + container;

    private string RequestPath(string container) =>
        $"/subscriptions/{_options.SubscriptionId:D}"
        + $"/resourceGroups/{Uri.EscapeDataString(_options.ResourceGroup)}"
        + "/providers/Microsoft.Storage/storageAccounts/"
        + _options.StorageAccountName
        + "/blobServices/default/containers/"
        + container;

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        var value = RequireUniqueProperty(parent, name);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new CustodyPolicyException($"Azure policy member {name} is not an object.");
        }

        return value;
    }

    private static bool RequireBoolean(JsonElement parent, string name)
    {
        var value = RequireUniqueProperty(parent, name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new CustodyPolicyException(
                $"Azure policy member {name} is not a boolean."),
        };
    }

    private static bool ReadOptionalBoolean(JsonElement parent, string name)
    {
        if (!TryGetUniqueProperty(parent, name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new CustodyPolicyException(
                $"Azure policy member {name} is not a boolean."),
        };
    }

    private static void RequireString(
        JsonElement parent,
        string name,
        string expected,
        StringComparison comparison)
    {
        var value = RequireUniqueProperty(parent, name);
        if (value.ValueKind != JsonValueKind.String
            || !string.Equals(value.GetString(), expected, comparison))
        {
            throw new CustodyPolicyException(
                $"Azure policy member {name} does not identify the configured resource.");
        }
    }

    private static string RequireNonemptyString(JsonElement parent, string name)
    {
        var value = RequireUniqueProperty(parent, name);
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new CustodyPolicyException(
                $"Azure policy member {name} must be a nonempty string.");
        }

        return text;
    }

    private static void RequireMatchingResourceEtag(JsonElement root, string expected)
    {
        if (!string.Equals(
                RequireNonemptyString(root, "etag"), expected, StringComparison.Ordinal))
        {
            throw new CustodyPolicyException(
                "The Azure policy body and response identify different resource versions.");
        }
    }

    private static string RequireSingleHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            throw new CustodyPolicyException(
                $"The Azure policy observation carried no {name} header.");
        }

        var materialized = values.ToArray();
        if (materialized.Length != 1 || string.IsNullOrWhiteSpace(materialized[0]))
        {
            throw new CustodyPolicyException(
                $"The Azure policy observation carried an invalid {name} header.");
        }

        return materialized[0];
    }

    private static JsonElement RequireUniqueProperty(JsonElement parent, string name)
    {
        if (!TryGetUniqueProperty(parent, name, out var found))
        {
            throw new CustodyPolicyException(
                $"Azure policy member {name} must occur exactly once.");
        }

        return found;
    }

    private static bool TryGetUniqueProperty(
        JsonElement parent,
        string name,
        out JsonElement found)
    {
        if (parent.ValueKind != JsonValueKind.Object)
        {
            throw new CustodyPolicyException("The Azure policy response has the wrong shape.");
        }

        var count = 0;
        found = default;
        foreach (var property in parent.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                count++;
                found = property.Value;
            }
        }

        if (count > 1)
        {
            throw new CustodyPolicyException(
                $"Azure policy member {name} must occur exactly once.");
        }

        return count == 1;
    }

    private static async ValueTask<(bool ExceededLimit, byte[] Bytes)> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(maximumBytes);
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        if (count == maximumBytes)
        {
            var overflowProbe = new byte[1];
            if (await stream.ReadAsync(overflowProbe, cancellationToken).ConfigureAwait(false) != 0)
            {
                return (true, Array.Empty<byte>());
            }

            return (false, buffer);
        }

        return (false, buffer.AsMemory(0, count).ToArray());
    }
}
