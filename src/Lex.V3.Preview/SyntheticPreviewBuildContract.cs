using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;

namespace Lex.V3.Preview;

public static class SyntheticPreviewBuildContract
{
    public const string Publisher = "lu-legilux";
    public const string UpstreamHealth = "not_applicable_synthetic";
    public const string CandidateCoordinate = "historical_legal_id:synthetic-preview";
    public const string CandidateEvidenceBasis = "synthetic_fixture_declared_mapping";

    public const string CanonicalSourceText =
        "LEX V3 SYNTHETIC PREVIEW\n" +
        "Article 1\n" +
        "This text is synthetic and has no legal authority.\n";

    public static ReadOnlySpan<byte> CanonicalSourceUtf8 =>
        "LEX V3 SYNTHETIC PREVIEW\nArticle 1\nThis text is synthetic and has no legal authority.\n"u8;

    public static string NormalizationProfileIdentity => SyntheticNormalizationProfile.PlainV1.ProfileId;

    public static string NormalizationProfileDescriptor => SyntheticNormalizationProfile.PlainV1.Descriptor;

    public static string NormalizationProfileSha256 => SyntheticNormalizationProfile.PlainV1.Sha256;

    public static string SqliteSchemaIdentity => SyntheticSliceIndexStamp.SchemaIdentity;

    public static string HeldCoordinate => SyntheticSliceScope.CompleteLu.EnumeratedMembers[0];
}

internal static class DigestFraming
{
    internal static string Hash(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    internal static string Hash(Stream value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    internal static string Hash(string domain, ReadOnlySpan<byte> value)
    {
        var domainBytes = Encoding.ASCII.GetBytes(domain);
        var framed = new byte[domainBytes.Length + 1 + value.Length];
        domainBytes.CopyTo(framed, 0);
        value.CopyTo(framed.AsSpan(domainBytes.Length + 1));
        return Hash(framed);
    }
}
