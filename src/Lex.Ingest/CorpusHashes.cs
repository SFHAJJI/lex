using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.Ingest;

internal static class CorpusHashes
{
    public static string RecordSha256(VersionMeta version)
    {
        var claimed = version.RecordSha256;
        try
        {
            version.RecordSha256 = null;
            var canonical = JsonSerializer.Serialize(version, CorpusJson.Options);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }
        finally
        {
            version.RecordSha256 = claimed;
        }
    }

    public static bool Equal(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        var a = left.ToLowerInvariant();
        var b = right.ToLowerInvariant();
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
    }
}
