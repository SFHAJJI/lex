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
            VerifiedCorpusPath.RequireExisting(corpusRoot, corpusRoot, "root");
            manifestPath = VerifiedCorpusPath.RequireExisting(
                corpusRoot, manifestPath, "manifest");
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
        IReadOnlyDictionary<string, string> completedRuns =
            new Dictionary<string, string>(StringComparer.Ordinal);
        if (!currentSchema && manifest.Schema != "lex-corpus/3")
            errors.Add($"manifest schema is '{manifest.Schema}', expected 'lex-corpus/3' or "
                + $"'{ManifestDoc.CurrentSchema}'");
        if (currentSchema)
            try
            {
                CodeIdentity.RequireFullCommit(
                    manifest.IngesterCodeCommit, "manifest ingester_code_commit");
                CorpusWriter.ValidateSourceConfiguration(
                    manifest.SourceConfigurationKind, manifest.SourceConfigurationSha256);
                var ledger = CompletedRunLedger.Load(corpusRoot);
                var ledgerPath = Path.Combine(corpusRoot, "completed-runs.json");
                if (manifest.CompletedRunsSha256 is null)
                {
                    if (File.Exists(ledgerPath) || ledger.Runs.Count != 0)
                        errors.Add(
                            "manifest completed_runs_sha256 is required when the completed-run ledger exists");
                }
                else
                {
                    CodeIdentity.RequireSha256(
                        manifest.CompletedRunsSha256,
                        "manifest completed_runs_sha256");
                    if (!File.Exists(ledgerPath) || ledger.Runs.Count == 0)
                        errors.Add(
                            "manifest completed_runs_sha256 names a missing or empty completed-run ledger");
                    else if (!CorpusHashes.Equal(
                                 manifest.CompletedRunsSha256,
                                 CompletedRunLedger.FileSha256(corpusRoot)))
                        errors.Add(
                            "manifest completed_runs_sha256 does not match completed-runs.json");
                }
                completedRuns = ledger.Runs.ToDictionary(
                    run => run.RunIdentity, run => run.CompletedAt,
                    StringComparer.Ordinal);
            }
            catch (Exception ex) when (ex is InvalidDataException
                                       or IOException
                                       or UnauthorizedAccessException)
            {
                errors.Add(ex.Message);
            }
        if (manifest.MigrationBaselineWorks is < 0)
            errors.Add("manifest migration_baseline_works cannot be negative");
        string? manifestPublisher = null;
        if (manifest.Publisher is null
            || !manifest.Publisher.TryGetValue("id", out manifestPublisher)
            || string.IsNullOrWhiteSpace(manifestPublisher))
            errors.Add("manifest publisher.id is required");

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
        var publisherWorkIds = new HashSet<string>(StringComparer.Ordinal);
        var versionIds = new HashSet<string>(StringComparer.Ordinal);
        var actualWorks = 0;
        var actualVersions = 0;
        var currentVersions = 0;
        var currentWorks = new HashSet<string>(StringComparer.Ordinal);
        var expressions = 0;
        var observations = 0;

        if (TryVerifiedPath(corpusRoot, worksRoot, "works directory", errors) is not { } verifiedWorksRoot)
            return new(manifest.Schema, manifest.IngesterCodeCommit,
                manifest.Works, 0, manifest.Versions, 0, 0, 0, 0, errors);

        foreach (var unverifiedWorkDir in Directory.EnumerateDirectories(verifiedWorksRoot)
                     .Order(StringComparer.Ordinal))
        {
            if (TryVerifiedPath(corpusRoot, unverifiedWorkDir, "work directory", errors)
                is not { } workDir)
                continue;
            var relativeWork = Path.GetRelativePath(corpusRoot, workDir).Replace('\\', '/');
            var workMetaPath = Path.Combine(workDir, "meta.json");
            if (!File.Exists(workMetaPath))
            {
                errors.Add($"{relativeWork}/meta.json is missing");
                continue;
            }
            if (TryVerifiedPath(corpusRoot, workMetaPath, "work metadata", errors) is null)
                continue;

            actualWorks++;
            WorkMeta? work = null;
            try
            {
                work = JsonSerializer.Deserialize<WorkMeta>(
                    File.ReadAllText(workMetaPath), CorpusJson.Options)
                    ?? throw new InvalidDataException("record is empty");
                if (!workIds.Add(work.LexWorkId))
                    errors.Add($"duplicate lex_work_id '{work.LexWorkId}'");
                if (!publisherWorkIds.Add(work.WorkIdentifier))
                    errors.Add($"duplicate work_identifier '{work.WorkIdentifier}'");
                var directorySlug = Path.GetFileName(workDir);
                if (!string.Equals(work.Publisher, manifestPublisher, StringComparison.Ordinal))
                    errors.Add($"{relativeWork}/meta.json publisher does not match manifest publisher");
                if (!string.Equals(work.Slug, directorySlug, StringComparison.Ordinal))
                    errors.Add($"{relativeWork}/meta.json slug does not match its directory");
                if (!string.Equals(work.LexWorkId,
                        $"{work.Publisher}:{directorySlug}", StringComparison.Ordinal))
                    errors.Add($"{relativeWork}/meta.json lex_work_id is not canonical");
                if (string.IsNullOrWhiteSpace(work.WorkIdentifier))
                    errors.Add($"{relativeWork}/meta.json work_identifier is required");
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
            if (TryVerifiedPath(corpusRoot, versionsRoot, "versions directory", errors)
                is not { } verifiedVersionsRoot)
                continue;

            foreach (var unverifiedVersionDir in Directory.EnumerateDirectories(verifiedVersionsRoot)
                         .Order(StringComparer.Ordinal))
            {
                if (TryVerifiedPath(corpusRoot, unverifiedVersionDir,
                        "version directory", errors) is not { } versionDir)
                    continue;
                var relativeVersion = Path.GetRelativePath(corpusRoot, versionDir).Replace('\\', '/');
                var versionMetaPath = Path.Combine(versionDir, "meta.json");
                if (!File.Exists(versionMetaPath))
                {
                    errors.Add($"{relativeVersion}/meta.json is missing");
                    continue;
                }
                if (TryVerifiedPath(corpusRoot, versionMetaPath, "version metadata", errors) is null)
                    continue;

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
                if (!string.Equals(version.Publisher, manifestPublisher, StringComparison.Ordinal))
                    errors.Add($"{relativeVersion}/meta.json publisher does not match manifest publisher");
                if (work is not null
                    && !string.Equals(version.WorkIdentifier,
                        work.WorkIdentifier, StringComparison.Ordinal))
                    errors.Add($"{relativeVersion}/meta.json work_identifier does not match its work");
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
                if (currentSchema)
                    try
                    {
                        var canonicalMetadata = PublisherMetadataValidation.Canonicalize(
                            version.PublisherMetadata);
                        if (!(version.PublisherMetadata ?? []).SequenceEqual(canonicalMetadata))
                            errors.Add(
                                $"{relativeVersion}/meta.json publisher_metadata is not canonical");
                    }
                    catch (InvalidDataException ex)
                    {
                        errors.Add(
                            $"{relativeVersion}/meta.json publisher_metadata is invalid: {ex.Message}");
                    }
                if (currentSchema)
                    try
                    {
                        CorpusWriter.ValidateVersionRawForSourceConfiguration(
                            manifest.SourceConfigurationKind, version.Raw, relativeVersion);
                    }
                    catch (InvalidDataException ex)
                    {
                        errors.Add($"{relativeVersion}/meta.json source boundary is invalid: {ex.Message}");
                    }
                if (currentSchema)
                    VerifyAbsenceLifecycle(version, relativeVersion,
                        manifest.MigrationBaselineWorks is not null,
                        completedRuns, errors);
                var lifecycle = version.Events.LastOrDefault(e =>
                    e.Event is "absent_unconfirmed" or "withdrawn_from_source" or "resighted");
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

    private static void VerifyAbsenceLifecycle(
        VersionMeta version,
        string relativeVersion,
        bool explicitMigration,
        IReadOnlyDictionary<string, string> completedRuns,
        ICollection<string> errors)
    {
        string? state = null;
        string? firstMissedAt = null;
        var runsMissed = 0;
        var runIdentities = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset? priorCompletedAt = null;
        var historicalAuditRequired = false;
        DateTimeOffset? historicalLegacyObservedAt = null;
        foreach (var entry in version.Events)
        {
            switch (entry.Event)
            {
                case "absent_unconfirmed":
                    var expectedRuns = state == "absent_unconfirmed" ? runsMissed + 1 : 1;
                    var expectedFirstMissedAt = expectedRuns == 1
                        ? entry.ObservedFrom : firstMissedAt;
                    var completedAtIsValid = TryCanonicalUtc(entry.ObservedFrom,
                        out var completedAt);
                    var invalidPending = state == "withdrawn_from_source"
                        || expectedRuns is < 1 or > 2
                        || string.IsNullOrWhiteSpace(entry.FirstMissedAt)
                        || entry.RunsMissed != expectedRuns
                        || !TryRunIdentity(entry.RunIdentity)
                        || !runIdentities.Add(entry.RunIdentity!)
                        || !string.Equals(entry.FirstMissedAt,
                            expectedFirstMissedAt, StringComparison.Ordinal)
                        || historicalAuditRequired
                           && !HistoricalWithdrawalAuditValidation.IsAuditCorrection(entry)
                        || historicalAuditRequired
                           && historicalLegacyObservedAt is not null
                           && completedAt <= historicalLegacyObservedAt.Value
                        || !completedAtIsValid
                        || (priorCompletedAt is not null
                            && completedAt <= priorCompletedAt.Value);
                    if (invalidPending)
                        errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid");
                    else if (!completedRuns.TryGetValue(
                                 entry.RunIdentity!, out var pendingCompletedAt))
                        errors.Add($"{relativeVersion}/meta.json run identity is absent from the completed-run ledger");
                    else if (!string.Equals(entry.ObservedFrom,
                                 pendingCompletedAt, StringComparison.Ordinal))
                        errors.Add($"{relativeVersion}/meta.json completed-run ledger time does not match observed_from");
                    state = "absent_unconfirmed";
                    firstMissedAt = entry.FirstMissedAt;
                    runsMissed = entry.RunsMissed ?? 0;
                    priorCompletedAt = completedAtIsValid ? completedAt : null;
                    break;
                case "withdrawn_from_source":
                    var legacyFields = entry.FirstMissedAt is null
                        && entry.RunsMissed is null
                        && entry.RunIdentity is null;
                    if (legacyFields)
                    {
                        if (!explicitMigration || historicalAuditRequired)
                            errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid");
                        historicalAuditRequired = true;
                        if (!TryCanonicalUtc(entry.ObservedFrom,
                                out var legacyObservedAt))
                            errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid");
                        else
                            historicalLegacyObservedAt = legacyObservedAt;
                        state = null;
                        firstMissedAt = null;
                        runsMissed = 0;
                        priorCompletedAt = null;
                        break;
                    }

                    var withdrawalAtIsValid = TryCanonicalUtc(entry.ObservedFrom,
                        out var withdrawalAt);
                    var invalidWithdrawal = state != "absent_unconfirmed"
                            || runsMissed != 2
                            || entry.RunsMissed != 3
                            || string.IsNullOrWhiteSpace(entry.FirstMissedAt)
                            || !TryRunIdentity(entry.RunIdentity)
                            || !runIdentities.Add(entry.RunIdentity!)
                            || !string.Equals(entry.FirstMissedAt,
                                firstMissedAt, StringComparison.Ordinal)
                            || historicalAuditRequired
                               && !HistoricalWithdrawalAuditValidation.IsAuditCorrection(entry)
                            || !withdrawalAtIsValid
                            || priorCompletedAt is null
                            || withdrawalAt <= priorCompletedAt.Value;
                    if (invalidWithdrawal)
                        errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid");
                    else if (!completedRuns.TryGetValue(
                                 entry.RunIdentity!, out var withdrawalCompletedAt))
                        errors.Add($"{relativeVersion}/meta.json run identity is absent from the completed-run ledger");
                    else if (!string.Equals(entry.ObservedFrom,
                                 withdrawalCompletedAt, StringComparison.Ordinal))
                        errors.Add($"{relativeVersion}/meta.json completed-run ledger time does not match observed_from");
                    else if (historicalAuditRequired)
                    {
                        historicalAuditRequired = false;
                        historicalLegacyObservedAt = null;
                    }
                    state = "withdrawn_from_source";
                    firstMissedAt = null;
                    runsMissed = 0;
                    priorCompletedAt = withdrawalAtIsValid ? withdrawalAt : null;
                    break;
                case "resighted":
                    var resightedAtIsValid = TryCanonicalUtc(
                        entry.ObservedFrom, out var resightedAt);
                    if (state is not ("absent_unconfirmed" or "withdrawn_from_source")
                        || !TryRunIdentity(entry.RunIdentity)
                        || !runIdentities.Add(entry.RunIdentity!)
                        || !resightedAtIsValid
                        || priorCompletedAt is not null
                           && resightedAt <= priorCompletedAt.Value)
                        errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid");
                    else if (!completedRuns.TryGetValue(
                                 entry.RunIdentity!, out var resightedCompletedAt))
                        errors.Add($"{relativeVersion}/meta.json run identity is absent from the completed-run ledger");
                    else if (!string.Equals(entry.ObservedFrom,
                                 resightedCompletedAt, StringComparison.Ordinal))
                        errors.Add($"{relativeVersion}/meta.json completed-run ledger time does not match observed_from");
                    state = "resighted";
                    firstMissedAt = null;
                    runsMissed = 0;
                    priorCompletedAt = null;
                    break;
            }
        }
        if (historicalAuditRequired)
            errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid");

        static bool TryRunIdentity(string? value)
        {
            try
            {
                _ = IngestRunIdentity.Require(value, "absence run identity");
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        static bool TryCanonicalUtc(string? value, out DateTimeOffset parsed)
        {
            try
            {
                parsed = HistoricalWithdrawalAuditValidation.ParseCanonicalUtc(
                    value, "absence observed_from");
                return true;
            }
            catch (InvalidDataException)
            {
                parsed = default;
                return false;
            }
        }
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

        var candidate = Path.GetFullPath(Path.Combine(
            versionDir, observation.File.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(candidate))
        {
            errors.Add($"{relativeVersion}/{observation.File} is missing");
            return;
        }
        if (TryVerifiedPath(versionDir, candidate, "observation file", errors)
            is not { } verifiedCandidate)
            return;

        using var stream = File.OpenRead(verifiedCandidate);
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

    private static string? TryVerifiedPath(
        string root, string candidate, string description, ICollection<string> errors)
    {
        try
        {
            return VerifiedCorpusPath.RequireExisting(root, candidate, description);
        }
        catch (InvalidDataException error)
        {
            errors.Add(error.Message);
            return null;
        }
    }
}
