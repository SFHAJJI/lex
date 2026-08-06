using System.Security.Cryptography;
using System.Text.Json;

namespace Lex.Ingest;

public sealed record CheckoutRepairReport(int Observations, int Repaired, IReadOnlyList<string> Unresolved)
{
    public bool IsValid => Unresolved.Count == 0;
}

public static class CheckoutLineEndings
{
    public static CheckoutRepairReport Repair(string corpusRoot)
    {
        var unresolved = new List<string>();
        var observations = 0;
        var repaired = 0;
        var root = Path.GetFullPath(corpusRoot) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var metaPath in Directory.EnumerateFiles(
                     Path.Combine(corpusRoot, "works"), "meta.json", SearchOption.AllDirectories))
        {
            if (!metaPath.Contains($"{Path.DirectorySeparatorChar}versions{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                continue;

            var meta = JsonSerializer.Deserialize<VersionMeta>(
                File.ReadAllText(metaPath), CorpusJson.Options)!;
            var versionDir = Path.GetDirectoryName(metaPath)!;
            foreach (var observation in meta.Expressions.SelectMany(e => e.Observations))
            {
                observations++;
                if (string.IsNullOrWhiteSpace(observation.File)
                    || string.IsNullOrWhiteSpace(observation.Sha256))
                    continue;

                var candidate = Path.GetFullPath(Path.Combine(
                    versionDir, observation.File.Replace('/', Path.DirectorySeparatorChar)));
                if (!candidate.StartsWith(root, comparison) || !File.Exists(candidate)) continue;

                var bytes = File.ReadAllBytes(candidate);
                var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (CorpusHashes.Equal(actual, observation.Sha256)) continue;

                var normalized = NormalizeLf(bytes);
                var normalizedSha = Convert.ToHexStringLower(SHA256.HashData(normalized));
                if (!CorpusHashes.Equal(normalizedSha, observation.Sha256))
                {
                    unresolved.Add(Path.GetRelativePath(corpusRoot, candidate).Replace('\\', '/'));
                    continue;
                }

                var temporary = candidate + ".lex-line-ending-repair";
                File.WriteAllBytes(temporary, normalized);
                File.Move(temporary, candidate, overwrite: true);
                repaired++;
            }
        }

        return new(observations, repaired, unresolved);
    }

    internal static string? LfNormalizedSha256(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var normalized = NormalizeLf(bytes);
        return normalized.Length == bytes.Length
            ? null
            : Convert.ToHexStringLower(SHA256.HashData(normalized));
    }

    private static byte[] NormalizeLf(ReadOnlySpan<byte> bytes)
    {
        var crlf = 0;
        for (var i = 0; i + 1 < bytes.Length; i++)
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n') crlf++;
        if (crlf == 0) return bytes.ToArray();

        var normalized = new byte[bytes.Length - crlf];
        var target = 0;
        for (var source = 0; source < bytes.Length; source++)
        {
            if (bytes[source] == (byte)'\r'
                && source + 1 < bytes.Length
                && bytes[source + 1] == (byte)'\n')
                continue;
            normalized[target++] = bytes[source];
        }
        return normalized;
    }
}
