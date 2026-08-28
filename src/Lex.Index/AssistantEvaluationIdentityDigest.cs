using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lex.Evaluation;

/// <summary>Closed Azure resource identities accepted by assistant evaluation evidence.</summary>
public static class AssistantEvaluationAzureResource
{
    public static bool IsModelAccount(string? value) =>
        IsResource(value, "Microsoft.CognitiveServices", "accounts", IsAccountName);

    public static bool IsContainerApp(string? value) =>
        IsResource(value, "Microsoft.App", "containerApps", IsContainerAppName);

    private static bool IsResource(
        string? value,
        string provider,
        string resourceType,
        Func<string, bool> validName)
    {
        if (string.IsNullOrEmpty(value)
            || value[0] != '/'
            || value[^1] == '/'
            || value.Contains("//", StringComparison.Ordinal))
            return false;
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 8
            && string.Equals(segments[0], "subscriptions", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(segments[1], "D", out _)
            && string.Equals(segments[2], "resourceGroups", StringComparison.OrdinalIgnoreCase)
            && IsResourceGroup(segments[3])
            && string.Equals(segments[4], "providers", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[5], provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[6], resourceType, StringComparison.OrdinalIgnoreCase)
            && validName(segments[7]);
    }

    private static bool IsResourceGroup(string value) =>
        value.Length is >= 1 and <= 90
        && value[^1] != '.'
        && value.All(character => char.IsLetterOrDigit(character)
            || character is '_' or '-' or '.' or '(' or ')');

    private static bool IsAccountName(string value) =>
        value.Length is >= 2 and <= 64
        && IsAsciiLetterOrDigit(value[0])
        && IsAsciiLetterOrDigit(value[^1])
        && value.All(character => IsAsciiLetterOrDigit(character) || character == '-');

    private static bool IsContainerAppName(string value) =>
        value.Length is >= 2 and <= 32
        && value[0] is >= 'a' and <= 'z'
        && IsLowerAsciiLetterOrDigit(value[^1])
        && value.All(character => IsLowerAsciiLetterOrDigit(character) || character == '-')
        && !value.Contains("--", StringComparison.Ordinal);

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsLowerAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';
}

/// <summary>Canonical digests for Azure facts carried by assistant evaluation evidence.</summary>
public static class AssistantEvaluationIdentityDigest
{
    /// <summary>Returns the host only when the value is one bare HTTPS authority.</summary>
    public static string BareHttpsHost(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Contains('@')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || Uri.CheckHostName(uri.IdnHost) != UriHostNameType.Dns
            || !IsBoundedAsciiDnsName(uri.IdnHost)
            || !string.Equals(value, $"https://{uri.IdnHost}",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Assistant evaluation model endpoint is not a bare HTTPS authority.");
        return uri.IdnHost;
    }

    private static bool IsBoundedAsciiDnsName(string host)
    {
        if (host.Length is < 1 or > 253 || host[^1] == '.')
            return false;

        var labelLength = 0;
        for (var index = 0; index < host.Length; index++)
        {
            var character = host[index];
            if (character == '.')
            {
                if (labelLength == 0 || host[index - 1] == '-')
                    return false;
                labelLength = 0;
                continue;
            }

            if (!IsAsciiLetterOrDigit(character)
                && character != '-')
                return false;
            if (labelLength == 0 && character == '-')
                return false;
            if (++labelLength > 63)
                return false;
        }

        // DNS presentation syntax permits a terminal root dot. The guard above rejects it
        // because signed Azure evidence requires one bare canonical authority spelling.
        return labelLength == 0 || host[^1] != '-';
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    /// <summary>Hashes the complete Container App revision identity in its stable wire order.</summary>
    public static string TargetSha256(
        string resourceId,
        string revisionName,
        string revisionFqdn,
        string image,
        decimal cpuCores,
        long memoryLimitBytes,
        int minimumReplicas,
        int maximumReplicas,
        int trafficWeight,
        string codeCommit,
        string artifactManifestSet,
        string candidateModelHost,
        string candidateDeployment)
    {
        // ARM can render one CPU allocation with different decimal scales. Normalize that field
        // and format every number invariantly so one revision has one digest across cultures.
        var invariant = CultureInfo.InvariantCulture;
        var canonical = string.Join('\n',
            resourceId.TrimEnd('/').ToLowerInvariant(), revisionName,
            revisionFqdn.ToLowerInvariant(), image,
            cpuCores.ToString("0.############################", invariant),
            memoryLimitBytes.ToString(invariant), minimumReplicas.ToString(invariant),
            maximumReplicas.ToString(invariant), trafficWeight.ToString(invariant),
            codeCommit, artifactManifestSet, candidateModelHost.ToLowerInvariant(),
            candidateDeployment);
        return Sha256(canonical);
    }

    /// <summary>Hashes the complete Azure model deployment identity in its stable wire order.</summary>
    public static string ModelSha256(
        string resourceId,
        string endpoint,
        string deployment,
        string sku,
        string modelFormat,
        string modelName,
        string modelVersion)
    {
        var endpointHost = BareHttpsHost(endpoint);
        var canonical = string.Join('\n',
            resourceId.TrimEnd('/').ToLowerInvariant(), endpointHost.ToLowerInvariant(),
            deployment, sku, modelFormat, modelName, modelVersion);
        return Sha256(canonical);
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
