using System.Security.Cryptography;
using System.Text.Json;
using Lex.Temporal;

namespace Lex.Ingest;

public sealed record CorpusIntegrityReport(
    string Schema,
    string? IngesterCodeCommit,
    int ManifestWorks,
    int ActualWorks,
    int ManifestVersions,
    int ActualVersions,
    int CurrentVersions,
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
            return new("missing", null, 0, 0, 0, 0, 0, 0, 0,
                ["manifest.json is missing"]);

        ManifestDoc manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ManifestDoc>(
                File.ReadAllText(manifestPath), CorpusJson.Options)
                ?? throw new InvalidDataException("manifest is empty");
        }
        catch (Exception ex)
        {
            return new("unreadable", null, 0, 0, 0, 0, 0, 0, 0,
                [$"manifest.json cannot be parsed: {ex.Message}"]);
        }

        var currentSchema = manifest.Schema == ManifestDoc.CurrentSchema;
        if (!currentSchema && manifest.Schema != "lex-corpus/3")
            errors.Add($"manifest schema is '{manifest.Schema}', expected 'lex-corpus/3' or "
                + $"'{ManifestDoc.CurrentSchema}'");
        if (currentSchema)
            try
            {
                CodeIdentity.RequireFullCommit(
                    manifest.IngesterCodeCommit, "manifest ingester_code_commit");
            }
            catch (InvalidDataException ex)
            {
                errors.Add(ex.Message);
            }
        if (manifest.MigrationBaselineWorks is < 0)
            errors.Add("manifest migration_baseline_works cannot be negative");

        if (manifest.AcquisitionRetryMaximumAttempts is < 1 or > 10)
            errors.Add("manifest acquisition_retry_maximum_attempts must be between 1 and 10");
        if (manifest.BuildIssues is null)
            errors.Add("manifest build_issues must be an array");
        else if (manifest.BuildIssues.Count > 1000)
            errors.Add("manifest build_issues must contain at most 1000 entries");
        for (var index = 0; index < (manifest.BuildIssues?.Count ?? 0); index++)
        {
            var issue = manifest.BuildIssues![index];
            if (issue is null)
            {
                errors.Add($"manifest build issue {index + 1} is null");
                continue;
            }
            if (string.IsNullOrWhiteSpace(issue.Code) || issue.Code.Length > 128)
                errors.Add($"manifest build issue {index + 1} has an invalid code");
            if (string.IsNullOrWhiteSpace(issue.Work) || issue.Work.Length > 512)
                errors.Add($"manifest build issue {index + 1} has an invalid work");
            if (issue.Detail?.Length > 2000)
                errors.Add($"manifest build issue {index + 1} has an oversized detail");
        }

        var worksRoot = Path.Combine(corpusRoot, "works");
        if (!Directory.Exists(worksRoot))
            return new(manifest.Schema, manifest.IngesterCodeCommit,
                manifest.Works, 0, manifest.Versions, 0, 0, 0, 0,
                [.. errors, "works directory is missing"]);

        var workIds = new HashSet<string>(StringComparer.Ordinal);
        var versionIds = new HashSet<string>(StringComparer.Ordinal);
        var actualWorks = 0;
        var actualVersions = 0;
        var currentVersions = 0;
        var currentWorks = new HashSet<string>(StringComparer.Ordinal);
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
                try
                {
                    if (currentSchema)
                        VersionIdentity.RequireCanonical(
                            Path.GetFileName(versionDir), version.ValidFrom,
                            version.PublisherVersionIdentifier, version.LexId,
                            $"{version.Publisher}:{Path.GetFileName(workDir)}");
                }
                catch (InvalidDataException ex)
                {
                    errors.Add($"{relativeVersion}/meta.json identity mismatch: {ex.Message}");
                }
                var lifecycle = version.Events.LastOrDefault(e =>
                    e.Event is "withdrawn_from_source" or "resighted");
                if (lifecycle?.Event != "withdrawn_from_source")
                {
                    currentVersions++;
                    currentWorks.Add(version.WorkIdentifier);
                }
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

        if (manifest.Works != currentWorks.Count)
            errors.Add($"manifest works={manifest.Works}, current filesystem works={currentWorks.Count}");
        if (manifest.Versions != currentVersions)
            errors.Add($"manifest versions={manifest.Versions}, current filesystem versions={currentVersions}");

        return new(manifest.Schema, manifest.IngesterCodeCommit,
            manifest.Works, actualWorks, manifest.Versions,
            actualVersions, currentVersions, expressions, observations, errors);
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

        var actual = CorpusHashes.RecordSha256(version);
        if (!CorpusHashes.Equal(actual, claimed))
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
        if (!CorpusHashes.Equal(actual, observation.Sha256))
        {
            var suffix = CheckoutLineEndings.LfNormalizedSha256(candidate) is { } normalized
                         && CorpusHashes.Equal(normalized, observation.Sha256)
                ? " (LF-normalized bytes match; checkout line endings changed)"
                : "";
            errors.Add($"{relativeVersion}/{observation.File} sha256 mismatch{suffix}");
        }
    }
}
