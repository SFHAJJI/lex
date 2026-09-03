using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Core;

public static class SourceCoreSchemaIds
{
    public const string Common = "lex-v3-source-common/1";
    public const string SourceObjectRef = "lex-v3-source-object-ref/1";
    public const string SourceProfileTopology = "lex-v3-source-profile-topology/1";
    public const string MachineQueryPlan = "machine_query_plan/1";
    public const string MachineQueryRenderReceipt = "machine_query_render_receipt/1";
}

public static class SourceCoreSchemaResourceIds
{
    public const string Common = "urn:uuid:26641197-e5e5-422c-ba6c-61dce566f8a3";
    public const string SourceObjectRef = "urn:uuid:4710a0f9-f83a-4747-82d9-84185db6728f";
    public const string SourceProfileTopology = "urn:uuid:6c66e724-8fb2-4521-81b9-12a2f15c508d";
    public const string MachineQueryPlan = "urn:uuid:8383f554-e474-4619-8826-2c99cb16b91a";
    public const string MachineQueryRenderReceipt = "urn:uuid:eed9f5fc-620f-4369-ba60-785d8f249071";

    public static string ForWireSchema(string schema) => schema switch
    {
        SourceCoreSchemaIds.Common => Common,
        SourceCoreSchemaIds.SourceObjectRef => SourceObjectRef,
        SourceCoreSchemaIds.SourceProfileTopology => SourceProfileTopology,
        SourceCoreSchemaIds.MachineQueryPlan => MachineQueryPlan,
        SourceCoreSchemaIds.MachineQueryRenderReceipt => MachineQueryRenderReceipt,
        _ => throw new ArgumentException("Unknown source-core schema identity.", nameof(schema)),
    };
}

public enum SourceAuthority
{
    [JsonStringEnumMemberName("jolux")]
    Jolux = 1,

    [JsonStringEnumMemberName("cellar")]
    Cellar = 2,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceArtifactRef
{
    [JsonConstructor]
    public SourceArtifactRef(string resourceId, string sha256)
    {
        ResourceId = SourceCoreValidation.RequireUuidUrn(resourceId, nameof(resourceId));
        Sha256 = SourceCoreValidation.RequireSha256(sha256, nameof(sha256));
    }

    public string ResourceId { get; }

    public string Sha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceRegistryMemberRef
{
    [JsonConstructor]
    public SourceRegistryMemberRef(SourceArtifactRef registryRef, string memberKey)
    {
        RegistryRef = registryRef ?? throw new ArgumentNullException(nameof(registryRef));
        MemberKey = SourceCoreValidation.RequireMemberKey(memberKey, nameof(memberKey));
    }

    public SourceArtifactRef RegistryRef { get; }

    public string MemberKey { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceProfileTopology
{
    [JsonConstructor]
    public SourceProfileTopology(
        string schema,
        SourceArtifactRef identityProfileRef,
        SourceRegistryMemberRef topology)
    {
        if (!string.Equals(schema, SourceCoreSchemaIds.SourceProfileTopology, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A source profile topology must declare {SourceCoreSchemaIds.SourceProfileTopology}.",
                nameof(schema));
        }

        Schema = schema;
        IdentityProfileRef = identityProfileRef
            ?? throw new ArgumentNullException(nameof(identityProfileRef));
        Topology = topology ?? throw new ArgumentNullException(nameof(topology));
    }

    public string Schema { get; }

    public SourceArtifactRef IdentityProfileRef { get; }

    public SourceRegistryMemberRef Topology { get; }
}

internal static class SourceCoreValidation
{
    private const int MaximumCanonicalKeyUtf8Bytes = 4096;
    private const int MaximumMemberKeyLength = 256;
    private const int MaximumPublisherUriLength = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    public static string RequireUuidUrn(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        const string prefix = "urn:uuid:";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(value[prefix.Length..], "D", out var parsed) ||
            parsed == Guid.Empty ||
            !string.Equals(value, prefix + parsed.ToString("D"), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Artifact resource identities must be exact lowercase non-empty UUID URNs.",
                parameterName);
        }

        return value;
    }

    public static string RequireSha256(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "SHA-256 values must be 64 lowercase hexadecimal characters.",
                parameterName);
        }

        return value;
    }

    public static string RequireMemberKey(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumMemberKeyLength ||
            value.Any(static character => character is < '!' or > '~'))
        {
            throw new ArgumentException(
                "Registry member keys must be bounded non-whitespace printable ASCII.",
                parameterName);
        }

        return value;
    }

    public static string RequirePublisherUri(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumPublisherUriLength ||
            value.Any(static character => character is < '!' or > '~') ||
            HasInvalidPercentEscape(value) ||
            !(value.StartsWith("http://", StringComparison.Ordinal) ||
              value.StartsWith("https://", StringComparison.Ordinal)) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            string.IsNullOrEmpty(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            throw new ArgumentException(
                "Publisher identities must be exact absolute HTTP(S) URIs without userinfo, query, or fragment.",
                parameterName);
        }

        return value;
    }

    private static bool HasInvalidPercentEscape(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' &&
                (index + 2 >= value.Length ||
                 !Uri.IsHexDigit(value[index + 1]) ||
                 !Uri.IsHexDigit(value[index + 2])))
            {
                return true;
            }
        }

        return false;
    }

    public static string RequireCanonicalKey(
        string value,
        string expectedSha256,
        string valueParameterName,
        string digestParameterName)
    {
        ArgumentNullException.ThrowIfNull(value, valueParameterName);
        RequireSha256(expectedSha256, digestParameterName);

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Canonical keys must contain only valid Unicode scalar values.",
                valueParameterName,
                exception);
        }

        if (bytes.Length is 0 or > MaximumCanonicalKeyUtf8Bytes)
        {
            throw new ArgumentException(
                $"Canonical keys must encode to between 1 and {MaximumCanonicalKeyUtf8Bytes} UTF-8 bytes.",
                valueParameterName);
        }

        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The canonical-key SHA-256 does not bind the exact UTF-8 key bytes.",
                digestParameterName);
        }

        return value;
    }
}
