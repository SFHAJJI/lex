namespace Lex.V3.Custody.Azure;

/// <summary>Validated coordinates and opaque policy identities for Azure custody.</summary>
public sealed record AzureBlobCustodyOptions
{
    public AzureBlobCustodyOptions(
        Uri serviceUri,
        string stagingContainer,
        string nightlyContainer,
        string legalHoldContainer,
        Guid managedIdentityClientId,
        Guid nightlyPolicyKey,
        Guid legalHoldPolicyKey,
        Guid subscriptionId,
        string resourceGroup)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        if (!serviceUri.IsAbsoluteUri
            || !string.Equals(serviceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(serviceUri.Host)
            || !string.IsNullOrEmpty(serviceUri.UserInfo)
            || !string.IsNullOrEmpty(serviceUri.Query)
            || !string.IsNullOrEmpty(serviceUri.Fragment)
            || !string.Equals(serviceUri.AbsolutePath, "/", StringComparison.Ordinal)
            || (!serviceUri.IsDefaultPort && serviceUri.Port != 443))
        {
            throw new ArgumentException(
                "The custody service URI must be an HTTPS account root without user information, query or fragment.",
                nameof(serviceUri));
        }

        const string publicBlobSuffix = ".blob.core.windows.net";
        var host = serviceUri.IdnHost;
        if (!host.EndsWith(publicBlobSuffix, StringComparison.Ordinal)
            || !IsStorageAccountName(host[..^publicBlobSuffix.Length]))
        {
            throw new ArgumentException(
                "The custody endpoint must be an exact Azure public Blob service account host.",
                nameof(serviceUri));
        }

        ValidateContainerName(stagingContainer, nameof(stagingContainer));
        ValidateContainerName(nightlyContainer, nameof(nightlyContainer));
        ValidateContainerName(legalHoldContainer, nameof(legalHoldContainer));
        if (string.Equals(stagingContainer, nightlyContainer, StringComparison.Ordinal)
            || string.Equals(stagingContainer, legalHoldContainer, StringComparison.Ordinal)
            || string.Equals(nightlyContainer, legalHoldContainer, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Staging, nightly and legal-hold custody containers must be distinct.");
        }

        if (managedIdentityClientId == Guid.Empty)
        {
            throw new ArgumentException(
                "A nonempty user-assigned managed-identity client ID is required.",
                nameof(managedIdentityClientId));
        }

        if (nightlyPolicyKey == Guid.Empty)
        {
            throw new ArgumentException(
                "A nonempty nightly policy key is required.", nameof(nightlyPolicyKey));
        }

        if (legalHoldPolicyKey == Guid.Empty)
        {
            throw new ArgumentException(
                "A nonempty legal-hold policy key is required.", nameof(legalHoldPolicyKey));
        }

        if (nightlyPolicyKey == legalHoldPolicyKey)
        {
            throw new ArgumentException("The two custody lanes must not share a policy key.");
        }

        if (subscriptionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A nonempty Azure subscription ID is required.", nameof(subscriptionId));
        }

        ValidateResourceGroup(resourceGroup, nameof(resourceGroup));

        ServiceUri = serviceUri;
        StagingContainer = stagingContainer;
        NightlyContainer = nightlyContainer;
        LegalHoldContainer = legalHoldContainer;
        ManagedIdentityClientId = managedIdentityClientId;
        NightlyPolicyKey = nightlyPolicyKey;
        LegalHoldPolicyKey = legalHoldPolicyKey;
        SubscriptionId = subscriptionId;
        ResourceGroup = resourceGroup;
        StorageAccountName = host[..^publicBlobSuffix.Length];
    }

    public Uri ServiceUri { get; }

    public string StagingContainer { get; }

    public string NightlyContainer { get; }

    public string LegalHoldContainer { get; }

    public Guid ManagedIdentityClientId { get; }

    public Guid NightlyPolicyKey { get; }

    public Guid LegalHoldPolicyKey { get; }

    public Guid SubscriptionId { get; }

    public string ResourceGroup { get; }

    public string StorageAccountName { get; }

    private static void ValidateContainerName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length is < 3 or > 63
            || !IsLowercaseLetterOrDigit(value[0])
            || !IsLowercaseLetterOrDigit(value[^1])
            || value.Contains("--", StringComparison.Ordinal)
            || value.Any(character => character != '-' && !IsLowercaseLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "An Azure container name must be 3 to 63 lowercase letters, digits or single hyphens, and must start and end with a letter or digit.",
                parameterName);
        }
    }

    private static bool IsLowercaseLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsStorageAccountName(string value) =>
        value.Length is >= 3 and <= 24 && value.All(IsLowercaseLetterOrDigit);

    private static void ValidateResourceGroup(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 90
            || value[^1] == '.'
            || value.Any(character => character is not (
                >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_' or '.' or '(' or ')')))
        {
            throw new ArgumentException("The Azure resource-group name is invalid.", parameterName);
        }
    }
}
