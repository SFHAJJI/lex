using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

public static class SyntheticSliceContractLimits
{
    public const int MaximumManifestBytes = 65_536;
    public const int MaximumControlBytes = 131_072;
    public const int MaximumSchemaBytes = 262_144;
    public const int MaximumSourceBytes = 4_096;
    public const int MaximumDerivedBytes = 4_096;
    public const int MaximumSqliteBytes = 1_048_576;
    public const int MaximumCandidateBytes = 1_253_376;
    public const int MaximumTrackedSchemaBytes = 1_048_576;
    public const int MaximumResponseBytes = 65_536;
    public const int MaximumProblemDetailsBytes = 4_096;
}

public static class SyntheticSliceSchemaGraph
{
    public static ReadOnlyCollection<string> OwnedSchemaIds { get; } = Array.AsReadOnly(
        new[]
        {
            V3SchemaIds.SyntheticSliceArtifact,
            V3SchemaIds.SyntheticSliceControl,
            V3SchemaIds.SyntheticResolveEnvelope,
        });

    public static ReadOnlyCollection<string> SchemaIds { get; } = Array.AsReadOnly(
        new[]
        {
            V3SchemaIds.SyntheticSliceArtifact,
            V3SchemaIds.SyntheticSliceControl,
            V3SchemaIds.SyntheticResolveEnvelope,
            V3SchemaIds.PreviewOperationCatalog,
            V3SchemaIds.PreviewRefusalRegistry,
            V3SchemaIds.PreviewObjectSet,
        });
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceSchemaMember
{
    [JsonConstructor]
    public SyntheticSliceSchemaMember(
        string schema,
        string schemaResource,
        string sha256,
        long bytes)
    {
        Schema = ContractValidation.RequireIdentifier(schema, nameof(schema));
        if (!SyntheticSliceSchemaGraph.SchemaIds.Contains(schema, StringComparer.Ordinal))
        {
            throw new ArgumentException("The schema is outside the synthetic-slice graph.", nameof(schema));
        }

        var expectedResource = V3SchemaResourceIds.ForWireSchema(schema);
        if (!string.Equals(schemaResource, expectedResource, StringComparison.Ordinal))
        {
            throw new ArgumentException("The schema resource does not match its wire identity.", nameof(schemaResource));
        }

        if (bytes is <= 0 or > SyntheticSliceContractLimits.MaximumSchemaBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        SchemaResource = schemaResource;
        Sha256 = ContractValidation.RequireSha256(sha256, nameof(sha256));
        Bytes = bytes;
    }

    public string Schema { get; }

    public string SchemaResource { get; }

    public string Sha256 { get; }

    public long Bytes { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceSchemaTable
{
    [JsonConstructor]
    public SyntheticSliceSchemaTable(IReadOnlyList<SyntheticSliceSchemaMember> members)
    {
        var copy = (members ?? throw new ArgumentNullException(nameof(members))).ToArray();
        if (copy.Any(static member => member is null) ||
            !copy.Select(static member => member.Schema)
                .SequenceEqual(SyntheticSliceSchemaGraph.SchemaIds, StringComparer.Ordinal) ||
            copy.Sum(static member => member.Bytes) > SyntheticSliceContractLimits.MaximumTrackedSchemaBytes)
        {
            throw new ArgumentException("The synthetic schema table must be the exact bounded six-member graph.", nameof(members));
        }

        Members = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<SyntheticSliceSchemaMember> Members { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticNormalizationProfile
{
    public const string Identity = "synthetic-plain/1";
    public const string DigestDomain = "lex-v3-profile-descriptor";
    public const string CanonicalDescriptor =
        "strict_utf8_without_replacement\n" +
        "crlf_to_lf\n" +
        "lone_cr_to_lf\n" +
        "unicode_nfc\n" +
        "preserve_other_scalars_and_whitespace\n" +
        "require_visible_non_whitespace\n" +
        "utf8_without_bom";

    [JsonConstructor]
    public SyntheticNormalizationProfile(string profileId, string descriptor, string sha256)
    {
        if (!string.Equals(profileId, Identity, StringComparison.Ordinal) ||
            !string.Equals(descriptor, CanonicalDescriptor, StringComparison.Ordinal) ||
            !string.Equals(sha256, ComputeSha256(descriptor), StringComparison.Ordinal))
        {
            throw new ArgumentException("The normalization profile must match synthetic-plain/1 exactly.");
        }

        ProfileId = profileId;
        Descriptor = descriptor;
        Sha256 = sha256;
    }

    public string ProfileId { get; }

    public string Descriptor { get; }

    public string Sha256 { get; }

    public static SyntheticNormalizationProfile PlainV1 { get; } = new(
        Identity,
        CanonicalDescriptor,
        ComputeSha256(CanonicalDescriptor));

    public static string ComputeSha256(string descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(DigestDomain + "\0" + descriptor)))
            .ToLowerInvariant();
    }
}
