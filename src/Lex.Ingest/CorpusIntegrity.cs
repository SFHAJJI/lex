using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.Ingest;

public sealed record CorpusIntegrityReport(
    string Schema,
    int ManifestWorks,
    int ActualWorks,
    int ManifestVersions,
    int ActualVersions,
    int Expressions,
    int Observations,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class CorpusIntegrity
{
    public static CorpusIntegrityReport Verify(string corpusRoot)
    {
        var errors = new List<string>();
        var manifestPath = Path.Combine(corpusRoot, "manifest.json");
        if (!File.Exists(manifestPath))
            return new("missing", 0, 0, 0, 0, 0, 0, ["manifest.json is missing"]);

        ManifestDoc manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ManifestDoc>(
                File.ReadAllText(manifestPath), CorpusJson.Options)
                ?? throw new InvalidDataException("manifest is empty");
        }
        catch (Exception ex)
        {
            return new("unreadable", 0, 0, 0, 0, 0, 0,
                [$"manifest.json cannot be parsed: {ex.Message}"]);
        }

        if (manifest.Schema != "lex-corpus/3")
            errors.Add($"manifest schema is '{manifest.Schema}', expected 'lex-corpus/3'");

        var worksRoot = Path.Combine(corpusRoot, "works");
        if (!Directory.Exists(worksRoot))
            return new(manifest.Schema, manifest.Works, 0, manifest.Versions, 0, 0, 0,
                [.. errors, "works directory is missing"]);

        var workIds = new HashSet<string>(StringComparer.Ordinal);
        var versionIds = new HashSet<string>(StringComparer.Ordinal);
        var actualWorks = 0;
        var actualVersions = 0;
        var expressions = 0;
        var observations = 0;

        foreach (var workDir in Directory.EnumerateDirectories(worksRoot).Order(StringComparer.Ordinal))
        {
            var relativeWork = Path.GetRelativePath(corpusRoot, workDir).Replace('\\', '/');
            var workMetaPath = Path.Combine(workDir, "meta.json");
            if (!File.Exists(workMetaPath))
            {
                errors.Add($"{relativeWork}/meta.json is missing");
                continue;
            }

            actualWorks++;
            try
            {
                var work = JsonSerializer.Deserialize<WorkMeta>(
                    File.ReadAllText(workMetaPath), CorpusJson.Options)
                    ?? throw new InvalidDataException("record is empty");
                if (!workIds.Add(work.LexWorkId))
                    errors.Add($"duplicate lex_work_id '{work.LexWorkId}'");
            }
            catch (Exception ex)
            {
                errors.Add($"{relativeWork}/meta.json cannot be parsed: {ex.Message}");
            }

            var versionsRoot = Path.Combine(workDir, "versions");
            if (!Directory.Exists(versionsRoot))
            {
                errors.Add($"{relativeWork}/versions is missing");
                continue;
            }

            foreach (var versionDir in Directory.EnumerateDirectories(versionsRoot).Order(StringComparer.Ordinal))
            {
                var relativeVersion = Path.GetRelativePath(corpusRoot, versionDir).Replace('\\', '/');
                var versionMetaPath = Path.Combine(versionDir, "meta.json");
                if (!File.Exists(versionMetaPath))
                {
                    errors.Add($"{relativeVersion}/meta.json is missing");
                    continue;
                }

                actualVersions++;
                VersionMeta version;
                try
                {
                    version = JsonSerializer.Deserialize<VersionMeta>(
                        File.ReadAllText(versionMetaPath), CorpusJson.Options)
                        ?? throw new InvalidDataException("record is empty");
                }
                catch (Exception ex)
                {
                    errors.Add($"{relativeVersion}/meta.json cannot be parsed: {ex.Message}");
                    continue;
                }

                if (!versionIds.Add(version.LexId))
                    errors.Add($"duplicate lex_id '{version.LexId}'");
                VerifyRecordHash(version, relativeVersion, errors);

                var languages = new HashSet<string>(StringComparer.Ordinal);
                foreach (var expression in version.Expressions)
                {
                    expressions++;
                    if (!languages.Add(expression.Language))
                        errors.Add($"{relativeVersion} repeats language '{expression.Language}'");
                    foreach (var observation in expression.Observations)
                    {
                        observations++;
                        VerifyObservation(versionDir, relativeVersion, observation, errors);
                    }
                }
            }
        }

        if (manifest.Works != actualWorks)
            errors.Add($"manifest works={manifest.Works}, filesystem works={actualWorks}");
        if (manifest.Versions != actualVersions)
            errors.Add($"manifest versions={manifest.Versions}, filesystem versions={actualVersions}");

        return new(manifest.Schema, manifest.Works, actualWorks, manifest.Versions,
            actualVersions, expressions, observations, errors);
    }

    private static void VerifyRecordHash(
        VersionMeta version, string relativeVersion, ICollection<string> errors)
    {
        var claimed = version.RecordSha256;
        if (string.IsNullOrWhiteSpace(claimed))
        {
            errors.Add($"{relativeVersion}/meta.json has no record_sha256");
            return;
        }

        version.RecordSha256 = null;
        var canonical = JsonSerializer.Serialize(version, CorpusJson.Options);
        version.RecordSha256 = claimed;
        var actual = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(claimed.ToLowerInvariant())))
            errors.Add($"{relativeVersion}/meta.json record_sha256 mismatch");
    }

    private static void VerifyObservation(
        string versionDir,
        string relativeVersion,
        ObservationEntry observation,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(observation.File) || string.IsNullOrWhiteSpace(observation.Sha256))
        {
            errors.Add($"{relativeVersion} has an observation without file or sha256");
            return;
        }

        var root = Path.GetFullPath(versionDir) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(
            versionDir, observation.File.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(root, comparison))
        {
            errors.Add($"{relativeVersion} observation escapes its version directory");
            return;
        }
        if (!File.Exists(candidate))
        {
            errors.Add($"{relativeVersion}/{observation.File} is missing");
            return;
        }

        using var stream = File.OpenRead(candidate);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual),
                Encoding.ASCII.GetBytes(observation.Sha256.ToLowerInvariant())))
            errors.Add($"{relativeVersion}/{observation.File} sha256 mismatch");
    }
}
