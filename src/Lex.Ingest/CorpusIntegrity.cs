using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;
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
        var identitySchema = currentSchema || manifest.Schema == "lex-corpus/4";
        IReadOnlyDictionary<string, string> completedRuns =
            new Dictionary<string, string>(StringComparer.Ordinal);
        if (!currentSchema
            && manifest.Schema is not ("lex-corpus/3" or "lex-corpus/4"))
            errors.Add($"manifest schema is '{manifest.Schema}', expected 'lex-corpus/3', "
                + "'lex-corpus/4' or "
                + $"'{ManifestDoc.CurrentSchema}'");
        if (currentSchema && manifest.Canon != ManifestDoc.CurrentCanon)
            errors.Add($"{ManifestDoc.CurrentSchema} requires canon "
                + $"'{ManifestDoc.CurrentCanon}'");
        if (currentSchema && (manifest.ObservationRun is null
            || !UtcTimestamp(manifest.ObservationRun)))
            errors.Add($"{ManifestDoc.CurrentSchema} requires a UTC observation_run");
        if (!currentSchema && manifest.Canon is not null)
            errors.Add("historical corpus schemas cannot claim a canon identity retroactively");
        if (!currentSchema && manifest.ObservationRun is not null)
            errors.Add("historical corpus schemas cannot claim an observation run retroactively");
        if (identitySchema)
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
        {
            if (manifest.Works == 0 && manifest.Versions == 0
                && manifest.Expressions == 0)
                return new(manifest.Schema, manifest.IngesterCodeCommit,
                    0, 0, 0, 0, 0, 0, 0, errors);
            return new(manifest.Schema, manifest.IngesterCodeCommit,
                manifest.Works, 0, manifest.Versions, 0, 0, 0, 0,
                [.. errors, "works directory is missing"]);
        }

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
                    if (identitySchema)
                        VersionIdentity.RequireCanonical(
                            Path.GetFileName(versionDir), version.ValidFrom,
                            version.PublisherVersionIdentifier, version.LexId,
                            $"{version.Publisher}:{Path.GetFileName(workDir)}");
                }
                catch (InvalidDataException ex)
                {
                    errors.Add($"{relativeVersion}/meta.json identity mismatch: {ex.Message}");
                }
                if (identitySchema)
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
                if (identitySchema)
                    try
                    {
                        CorpusWriter.ValidateVersionRawForSourceConfiguration(
                            manifest.SourceConfigurationKind, version.Raw, relativeVersion);
                    }
                    catch (InvalidDataException ex)
                    {
                        errors.Add($"{relativeVersion}/meta.json source boundary is invalid: {ex.Message}");
                    }
                if (identitySchema)
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
                        VerifyObservation(versionDir, relativeVersion,
                            expression.Language, observation, currentSchema, errors);
                    }
                    var freshPrimaryCount = currentSchema
                        ? expression.Observations.Count(observation =>
                            observation.Format is null
                            && observation.Http is not null
                            && observation.ObservedFrom == manifest.ObservationRun)
                        : 0;
                    if (freshPrimaryCount > 1)
                        errors.Add($"{relativeVersion}/{expression.Language} has multiple fresh "
                            + "primary observations for manifest observation_run");
                    if (currentSchema
                        && lifecycle?.Event is not (
                            "absent_unconfirmed" or "withdrawn_from_source")
                        && expression.Text.Available
                        && (expression.Observations.Any(observation =>
                                observation.Format is null)
                            || !expression.Observations.Any(observation =>
                                observation.Format is not null))
                        && freshPrimaryCount == 0)
                        errors.Add($"{relativeVersion}/{expression.Language} has no fresh "
                            + "primary observation for manifest observation_run");
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
                    // The condition order is load-bearing: runIdentities.Add mutates, so it must
                    // stay behind the same guards it sat behind as a short-circuiting disjunction.
                    var pendingReason =
                        state == "withdrawn_from_source"
                            ? "a withdrawn work cannot return to unconfirmed absence"
                        : expectedRuns is < 1 or > 2
                            ? $"runs_missed would reach {expectedRuns}, past the two-run bound"
                        : string.IsNullOrWhiteSpace(entry.FirstMissedAt)
                            ? "first_missed_at is empty"
                        : entry.RunsMissed != expectedRuns
                            ? $"runs_missed={Show(entry.RunsMissed)}, expected {expectedRuns}"
                        : !TryRunIdentity(entry.RunIdentity)
                            ? $"run_identity {Show(entry.RunIdentity)} is not a well-formed run identity"
                        : !runIdentities.Add(entry.RunIdentity!)
                            ? $"run_identity {entry.RunIdentity} repeats inside one version"
                        : !string.Equals(entry.FirstMissedAt,
                            expectedFirstMissedAt, StringComparison.Ordinal)
                            ? $"first_missed_at={Show(entry.FirstMissedAt)}, expected {Show(expectedFirstMissedAt)}"
                        : historicalAuditRequired
                          && !HistoricalWithdrawalAuditValidation.IsAuditCorrection(entry)
                            ? "a historical withdrawal is open and this entry is not its audit correction"
                        : historicalAuditRequired
                          && historicalLegacyObservedAt is not null
                          && completedAt <= historicalLegacyObservedAt.Value
                            ? $"observed_from={Show(entry.ObservedFrom)} does not follow the legacy withdrawal at {Show(historicalLegacyObservedAt)}"
                        : !completedAtIsValid
                            ? $"observed_from={Show(entry.ObservedFrom)} is not a canonical UTC instant"
                        : priorCompletedAt is not null
                          && completedAt <= priorCompletedAt.Value
                            ? $"observed_from={Show(entry.ObservedFrom)} does not advance past the prior run at {Show(priorCompletedAt)}"
                        : null;
                    if (pendingReason is not null)
                        errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid: absent_unconfirmed, {pendingReason}");
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
                        if (!explicitMigration)
                            errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid: withdrawn_from_source carries legacy fields without an explicit migration");
                        else if (historicalAuditRequired)
                            errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid: a second legacy withdrawal while an earlier one is still awaiting its audit correction");
                        historicalAuditRequired = true;
                        if (!TryCanonicalUtc(entry.ObservedFrom,
                                out var legacyObservedAt))
                            errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid: legacy withdrawn_from_source observed_from={Show(entry.ObservedFrom)} is not a canonical UTC instant");
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
                    var withdrawalReason =
                        state != "absent_unconfirmed"
                            ? $"withdrawal from state {Show(state)}, which must be absent_unconfirmed"
                        : runsMissed != 2
                            ? $"the prior entry recorded runs_missed={runsMissed}, and withdrawal requires two"
                        : entry.RunsMissed != 3
                            ? $"runs_missed={Show(entry.RunsMissed)}, and withdrawal requires three"
                        : string.IsNullOrWhiteSpace(entry.FirstMissedAt)
                            ? "first_missed_at is empty"
                        : !TryRunIdentity(entry.RunIdentity)
                            ? $"run_identity {Show(entry.RunIdentity)} is not a well-formed run identity"
                        : !runIdentities.Add(entry.RunIdentity!)
                            ? $"run_identity {entry.RunIdentity} repeats inside one version"
                        : !string.Equals(entry.FirstMissedAt,
                            firstMissedAt, StringComparison.Ordinal)
                            ? $"first_missed_at={Show(entry.FirstMissedAt)}, expected {Show(firstMissedAt)}"
                        : historicalAuditRequired
                          && !HistoricalWithdrawalAuditValidation.IsAuditCorrection(entry)
                            ? "a historical withdrawal is open and this entry is not its audit correction"
                        : !withdrawalAtIsValid
                            ? $"observed_from={Show(entry.ObservedFrom)} is not a canonical UTC instant"
                        : priorCompletedAt is null
                            ? "no prior completed run to withdraw against"
                        : withdrawalAt <= priorCompletedAt.Value
                            ? $"observed_from={Show(entry.ObservedFrom)} does not advance past the prior run at {Show(priorCompletedAt)}"
                        : null;
                    if (withdrawalReason is not null)
                        errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid: withdrawn_from_source, {withdrawalReason}");
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
                    var resightedReason =
                        state is not ("absent_unconfirmed" or "withdrawn_from_source")
                            ? $"resighting from state {Show(state)}, which must be absent_unconfirmed or withdrawn_from_source"
                        : !TryRunIdentity(entry.RunIdentity)
                            ? $"run_identity {Show(entry.RunIdentity)} is not a well-formed run identity"
                        : !runIdentities.Add(entry.RunIdentity!)
                            ? $"run_identity {entry.RunIdentity} repeats inside one version"
                        : !resightedAtIsValid
                            ? $"observed_from={Show(entry.ObservedFrom)} is not a canonical UTC instant"
                        : priorCompletedAt is not null
                          && resightedAt <= priorCompletedAt.Value
                            ? $"observed_from={Show(entry.ObservedFrom)} does not advance past the prior run at {Show(priorCompletedAt)}"
                        : null;
                    if (resightedReason is not null)
                        errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid: resighted, {resightedReason}");
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
            errors.Add($"{relativeVersion}/meta.json absence lifecycle is invalid: a legacy withdrawal is still open at the end of the event list, with no audit correction");

        // Renders a value for an operator, without turning null into an empty string.
        static string Show(object? value) => value switch
        {
            null => "(absent)",
            string text when string.IsNullOrWhiteSpace(text) => "(blank)",
            DateTimeOffset instant => instant.ToString("o", CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "(absent)",
        };

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
        string language,
        ObservationEntry observation,
        bool transportSchema,
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

        if (observation.Format is not null)
        {
            if (observation.Http is not null)
                errors.Add($"{relativeVersion}/{observation.File} alternative manifestation has primary HTTP evidence");
            return;
        }

        var fileStem = Path.GetFileNameWithoutExtension(observation.File);
        var appendSafe = PrimaryObservationName.Matches(
            fileStem, language, observation.Sha256);
        var extension = Path.GetExtension(observation.File);
        var topLevel = !observation.File.Contains('/')
            && !observation.File.Contains('\\');
        var supportedExtension = extension is ".xml" or ".html"
            || appendSafe && extension == ".body";
        if (!topLevel || !supportedExtension)
            errors.Add($"{relativeVersion}/{observation.File} primary observation path is not canonical");
        if (!string.Equals(fileStem, language, StringComparison.Ordinal)
            && !appendSafe)
            errors.Add($"{relativeVersion}/{observation.File} primary observation name is not append-safe");
        if (!appendSafe)
        {
            if (observation.Http is not null)
                errors.Add($"{relativeVersion}/{observation.File} legacy primary observation carries retroactive HTTP evidence");
            return;
        }
        if (!transportSchema)
        {
            errors.Add($"{relativeVersion}/{observation.File} content-addressed primary observation requires {ManifestDoc.CurrentSchema}");
            return;
        }

        var http = observation.Http;
        if (http is null)
        {
            errors.Add($"{relativeVersion}/{observation.File} has no bounded HTTP evidence");
            return;
        }
        var completeOutcome = http.AttemptOutcome is
            "retrieved" or "body_empty" or "body_parser_failure";
        var rejectedOutcome = http.AttemptOutcome is
            "body_oversized" or "body_not_found" or "body_gone"
            or "body_retry_exhausted";
        if (http.StatusCode is < 100 or > 599
            || http.Attempts is < 1 or > 10
            || !completeOutcome && !rejectedOutcome
            || completeOutcome && http.BodyComplete == false
            || http.AttemptOutcome == "body_oversized" && http.BodyComplete is not false
            || http.AttemptOutcome == "retrieved" && http.StatusCode is not (>= 200 and <= 299)
            || !UtcTimestamp(http.FetchedAt)
            || http.LastModified is not null && !UtcTimestamp(http.LastModified)
            || !Bounded(http.ContentType, 128)
            || !Bounded(http.Charset, 64)
            || !Bounded(http.EntityTag, 512))
            errors.Add($"{relativeVersion}/{observation.File} bounded HTTP evidence is invalid");
        if (!string.Equals(
                observation.RetrievedAt, http.FetchedAt, StringComparison.Ordinal)
            || !UtcTimestamp(observation.ObservedFrom))
            errors.Add($"{relativeVersion}/{observation.File} primary observation times are incoherent");
        if (!Bounded(observation.SourceUri, 2048)
            || !Uri.TryCreate(observation.SourceUri, UriKind.Absolute, out var sourceUri)
            || sourceUri.Scheme is not ("http" or "https"))
            errors.Add($"{relativeVersion}/{observation.File} effective source URI is invalid");
    }

    private static bool Bounded(string? value, int maximumLength) =>
        value is null || !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximumLength
            && !value.Any(char.IsControl);

    private static bool UtcTimestamp(string value) =>
        DateTimeOffset.TryParseExact(value, "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
            out var parsed)
        && parsed.Offset == TimeSpan.Zero;

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
