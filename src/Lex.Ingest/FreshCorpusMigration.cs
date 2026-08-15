using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lex.Law;
using Lex.Temporal;

namespace Lex.Ingest;

/// <summary>
/// Builds a fresh corpus beside the legacy tree inside one disposable checkout. The Git remote is
/// the protected authority; local legacy bytes are not replaced until every gate passes.
/// </summary>
public static class FreshCorpusMigration
{
    public static Task<CorpusIntegrityReport> RunAsync(
        string corpusRoot,
        string publisher,
        ISourceAdapter adapter,
        DateTimeOffset now,
        string ingesterCodeCommit,
        CancellationToken cancellationToken) =>
        RunAsync(corpusRoot, publisher, adapter, now, ingesterCodeCommit,
            beforeMove: null, cancellationToken);

    internal static async Task<CorpusIntegrityReport> RunAsync(
        string corpusRoot,
        string publisher,
        ISourceAdapter adapter,
        DateTimeOffset now,
        string ingesterCodeCommit,
        Action<string, string>? beforeMove,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(corpusRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        if (Directory.GetParent(root) is null)
            throw new InvalidDataException(
                "Fresh corpus migration refuses a filesystem root.");

        var baselineIntegrity = CorpusIntegrity.Verify(root);
        if (!baselineIntegrity.IsValid)
            throw new InvalidDataException(
                "Protected corpus baseline is not integrity-compatible:\n"
                + string.Join("\n", baselineIntegrity.Errors));
        var baselineManifest = ReadManifest(root);
        var baselinePublisher = baselineManifest.Publisher.GetValueOrDefault("id")
            ?? throw new InvalidDataException(
                "Protected corpus baseline has no publisher identity.");
        var requestedPublisher = adapter.Describe().Publisher.Id;
        if (!string.Equals(requestedPublisher, baselinePublisher, StringComparison.Ordinal)
            || !string.Equals(requestedPublisher, publisher, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Fresh corpus publisher '{requestedPublisher}' does not match protected "
                + $"baseline '{baselinePublisher}' and requested publisher '{publisher}'.");
        var baseline = ReadBaselineInventory(
            root, baselineManifest.Schema, baselinePublisher);
        var token = Guid.NewGuid().ToString("N");
        var parent = Directory.GetParent(root)!.FullName;
        var name = Path.GetFileName(root.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var stage = Path.Combine(parent, $".{name}.lex-fresh-stage-{token}");
        var backup = Path.Combine(parent, $".{name}.lex-fresh-backup-{token}");
        Directory.CreateDirectory(stage);
        try
        {
            var writer = new CorpusWriter(stage, now, ingesterCodeCommit);
            BaselineReconciliation? reconciliation = null;
            IReadOnlyList<string>? matchedProtectedPaths = null;
            await writer.WriteAsync(adapter, cancellationToken, requireComplete: true,
                plan =>
                {
                    reconciliation = RequirePreservedBaseline(baseline, plan, adapter);
                    matchedProtectedPaths = ImportMatchedBaselineStates(
                        root, stage, reconciliation.Matched, now, cancellationToken);
                });
            if (!writer.Committed)
                throw new InvalidDataException("Fresh corpus candidate was not committed.");
            if (reconciliation is null)
                throw new InvalidDataException(
                    "Fresh corpus baseline reconciliation did not run.");

            ImportWithdrawnBaselineStates(
                root, stage, reconciliation.Withdrawn, now, cancellationToken);

            var candidateManifest = ReadManifest(stage);
            var candidatePublisher = candidateManifest.Publisher.GetValueOrDefault("id")
                ?? throw new InvalidDataException(
                    "Fresh corpus candidate has no publisher identity.");
            if (!string.Equals(candidatePublisher, baselinePublisher, StringComparison.Ordinal)
                || !string.Equals(candidatePublisher, publisher, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Fresh corpus publisher '{candidatePublisher}' does not match protected "
                    + $"baseline '{baselinePublisher}' and requested publisher '{publisher}'.");

            candidateManifest.MigrationBaselineWorks = baselineIntegrity.ActualWorks;
            File.WriteAllText(Path.Combine(stage, "manifest.json"),
                JsonSerializer.Serialize(candidateManifest, CorpusJson.Options) + "\n");

            var integrity = CorpusIntegrity.Verify(stage);
            if (!integrity.IsValid)
                throw new InvalidDataException("Fresh corpus candidate failed integrity:\n"
                    + string.Join("\n", integrity.Errors));

            if (matchedProtectedPaths is null)
                throw new InvalidDataException(
                    "Fresh corpus matched baseline import did not run.");
            RevalidateMatchedBaselinePaths(
                root, matchedProtectedPaths, cancellationToken);
            ReplaceCandidateTree(root, stage, backup, beforeMove);
            return integrity;
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); } catch { }
            // ReplaceCandidateTree removes a backup only after a proven successful swap. If a
            // move or rollback throws, the adjacent backup is the only recoverable baseline and
            // must survive; the disposable checkout can then be discarded or repaired manually.
        }
    }

    private static ManifestDoc ReadManifest(string root)
    {
        var path = VerifiedCorpusPath.RequireExisting(
            root, Path.Combine(root, "manifest.json"), "manifest");
        return JsonSerializer.Deserialize<ManifestDoc>(
            File.ReadAllText(path), CorpusJson.Options)
            ?? throw new InvalidDataException($"Corpus manifest is empty: {root}");
    }

    private sealed record BaselineState(
        string Description,
        string VersionDirectory,
        VersionMeta Version,
        DateOnly ValidFrom,
        bool IsWithdrawn);

    private sealed record BaselineWork(
        WorkMeta Work,
        IReadOnlyList<BaselineState> States);

    private sealed record BaselineInventory(
        bool UsesPublisherVersionIdentifier,
        IReadOnlyList<BaselineWork> Works);

    private sealed record WithdrawnBaselineState(
        BaselineState Baseline,
        string DestinationWorkSlug,
        string PublisherVersionIdentifier);

    private sealed record MatchedBaselineState(
        BaselineState Baseline,
        string DestinationWorkSlug,
        string PublisherVersionIdentifier,
        VersionRecord Current);

    private sealed record BaselineReconciliation(
        IReadOnlyList<MatchedBaselineState> Matched,
        IReadOnlyList<WithdrawnBaselineState> Withdrawn);

    private static BaselineInventory ReadBaselineInventory(
        string root, string schema, string publisher)
    {
        var usesPublisherVersionIdentifier = schema == ManifestDoc.CurrentSchema;
        var works = new List<BaselineWork>();
        var worksRoot = VerifiedCorpusPath.RequireExisting(
            root, Path.Combine(root, "works"), "works directory");
        foreach (var unverifiedWorkDirectory in Directory.EnumerateDirectories(worksRoot)
                     .Order(StringComparer.Ordinal))
        {
            var workDirectory = VerifiedCorpusPath.RequireExisting(
                root, unverifiedWorkDirectory, "work directory");
            var workMetaPath = VerifiedCorpusPath.RequireExisting(
                root, Path.Combine(workDirectory, "meta.json"), "work metadata");
            var work = JsonSerializer.Deserialize<WorkMeta>(File.ReadAllText(
                           workMetaPath), CorpusJson.Options)
                       ?? throw new InvalidDataException(
                           $"Protected corpus work metadata is empty: {workDirectory}");
            if (!string.Equals(work.Publisher, publisher, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Protected corpus work publisher '{Bound(work.Publisher)}' does not match "
                    + $"manifest publisher '{Bound(publisher)}': "
                    + Path.GetRelativePath(root, workDirectory));
            if (!string.Equals(work.Slug, Path.GetFileName(workDirectory),
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Protected corpus work slug '{Bound(work.Slug)}' does not match its "
                    + $"directory: {Path.GetRelativePath(root, workDirectory)}");
            var states = new List<BaselineState>();
            var versionsRoot = VerifiedCorpusPath.RequireExisting(
                root, Path.Combine(workDirectory, "versions"), "versions directory");
            foreach (var unverifiedVersionDirectory in Directory.EnumerateDirectories(versionsRoot)
                         .Order(StringComparer.Ordinal))
            {
                var versionDirectory = VerifiedCorpusPath.RequireExisting(
                    root, unverifiedVersionDirectory, "version directory");
                var versionMetaPath = VerifiedCorpusPath.RequireExisting(
                    root, Path.Combine(versionDirectory, "meta.json"), "version metadata");
                var version = JsonSerializer.Deserialize<VersionMeta>(File.ReadAllText(
                                  versionMetaPath), CorpusJson.Options)
                              ?? throw new InvalidDataException(
                                  $"Protected corpus version metadata is empty: {versionDirectory}");
                if (!string.Equals(version.WorkIdentifier, work.WorkIdentifier,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Protected corpus version work identity '{Bound(version.WorkIdentifier)}' "
                        + $"does not match containing work '{Bound(work.WorkIdentifier)}': "
                        + Path.GetRelativePath(root, versionDirectory));
                if (!string.Equals(version.Publisher, publisher, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Protected corpus version publisher '{Bound(version.Publisher)}' "
                        + $"does not match manifest publisher '{Bound(publisher)}': "
                        + Path.GetRelativePath(root, versionDirectory));
                if (usesPublisherVersionIdentifier)
                {
                    if (string.IsNullOrEmpty(version.PublisherVersionIdentifier))
                        throw new InvalidDataException(
                            $"Protected v4 corpus version has no publisher identity: "
                            + Path.GetRelativePath(root, versionDirectory));
                    states.Add(new BaselineState(
                        $"work '{Bound(work.WorkIdentifier)}' publisher version "
                        + $"'{Bound(version.PublisherVersionIdentifier)}'",
                        versionDirectory, version,
                        ParseValidFrom(version, root, versionDirectory),
                        IsWithdrawn(version)));
                    continue;
                }

                var validFrom = ParseValidFrom(version, root, versionDirectory);
                states.Add(new BaselineState(
                    $"work '{Bound(work.WorkIdentifier)}' version "
                    + validFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    versionDirectory, version, validFrom, IsWithdrawn(version)));
            }
            works.Add(new BaselineWork(work, states));
        }
        return new BaselineInventory(usesPublisherVersionIdentifier, works);
    }

    private static BaselineReconciliation RequirePreservedBaseline(
        BaselineInventory baseline,
        IReadOnlyList<CorpusPlannedWork> plan,
        ISourceAdapter adapter)
    {
        const int maximumDiagnostics = 20;
        var diagnostics = new List<string>();
        var failureCount = 0;
        void Fail(string message)
        {
            failureCount++;
            if (diagnostics.Count < maximumDiagnostics) diagnostics.Add(message);
        }

        var baselineStates = new List<(BaselineState State, string Key,
            string Description, string PublisherVersionIdentifier)>();
        foreach (var state in baseline.Works.SelectMany(work => work.States))
        {
            try
            {
                var publisherVersionIdentifier = ResolvePublisherVersionIdentifier(
                    baseline.UsesPublisherVersionIdentifier, state, adapter);
                baselineStates.Add((
                    state,
                    PublisherStateKey(
                        state.Version.WorkIdentifier, publisherVersionIdentifier),
                    $"work '{Bound(state.Version.WorkIdentifier)}' publisher version "
                    + $"'{Bound(publisherVersionIdentifier)}'",
                    publisherVersionIdentifier));
            }
            catch (InvalidDataException ex)
            {
                Fail("protected " + state.Description
                     + " has no exact publisher identity: " + ex.Message);
            }
        }

        var candidateWorks = plan.GroupBy(item => item.Work.Id.Value,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.Ordinal);
        foreach (var group in baseline.Works.GroupBy(value => value.Work.WorkIdentifier,
                     StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var count = candidateWorks.GetValueOrDefault(group.Key)?.Length ?? 0;
            if (group.Count() != 1)
                Fail($"protected baseline contains {group.Count()} works with identity "
                     + $"'{Bound(group.Key)}'");
            else if (count == 0)
                Fail($"candidate is missing work '{Bound(group.Key)}'");
            else if (count != 1)
                Fail($"candidate contains {count} works with identity '{Bound(group.Key)}'");
        }

        var candidateStates = new Dictionary<string,
            List<(CorpusPlannedWork Work, VersionRecord Version)>>(StringComparer.Ordinal);
        var candidatePublisherStates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in plan)
            foreach (var version in item.Versions)
            {
                if (!string.Equals(version.WorkId.Value, item.Work.Id.Value,
                        StringComparison.Ordinal))
                {
                    Fail($"candidate version '{Bound(version.Id.Value)}' belongs to work "
                         + $"'{Bound(version.WorkId.Value)}', not enumerated work "
                         + $"'{Bound(item.Work.Id.Value)}'");
                    continue;
                }
                var key = PublisherStateKey(item.Work.Id.Value, version.Id.Value);
                if (!candidateStates.TryGetValue(key, out var matches))
                    candidateStates[key] = matches = [];
                matches.Add((item, version));
                if (!candidatePublisherStates.Add(PublisherStateKey(
                        item.Work.Id.Value, version.Id.Value)))
                    Fail($"candidate contains duplicate publisher version "
                         + $"'{Bound(version.Id.Value)}' for work "
                         + $"'{Bound(item.Work.Id.Value)}'");
            }

        var matched = new List<MatchedBaselineState>();
        var withdrawn = new List<WithdrawnBaselineState>();
        var withdrawnPublisherStates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in baselineStates
                     .GroupBy(state => state.Key,
                     StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var state = group.First();
            var candidates = candidateStates.GetValueOrDefault(group.Key) ?? [];
            if (group.Count() != 1)
                Fail($"protected baseline contains {group.Count()} indistinguishable states for "
                     + state.Description);
            else if (candidates.Count > 1)
                Fail($"candidate contains {candidates.Count} ambiguous matches for "
                     + state.Description);
            else if (candidates.Count == 0)
            {
                if (!state.State.IsWithdrawn)
                {
                    Fail("candidate is missing " + state.Description);
                    continue;
                }
                if (!candidateWorks.TryGetValue(state.State.Version.WorkIdentifier,
                        out var plannedWorks) || plannedWorks.Length != 1)
                {
                    Fail("candidate cannot retain withdrawn " + state.Description
                         + " because its work is not uniquely present in the current plan");
                    continue;
                }

                var publisherState = state.Key;
                if (candidatePublisherStates.Contains(publisherState))
                {
                    Fail("withdrawn " + state.Description
                         + " resolves to a publisher identity already present in the current plan");
                    continue;
                }
                if (!withdrawnPublisherStates.Add(publisherState))
                {
                    Fail("protected baseline contains multiple withdrawn records that resolve "
                         + "to the same publisher identity for " + state.Description);
                    continue;
                }
                withdrawn.Add(new WithdrawnBaselineState(
                    state.State, plannedWorks[0].Work.Slug,
                    state.PublisherVersionIdentifier));
            }
            else
            {
                var candidate = candidates[0];
                if (candidate.Version.ValidFrom != state.State.ValidFrom)
                {
                    Fail("candidate changed valid_from for " + state.Description
                         + $" from {state.State.ValidFrom:yyyy-MM-dd} to "
                         + $"{candidate.Version.ValidFrom:yyyy-MM-dd}");
                    continue;
                }
                matched.Add(new MatchedBaselineState(
                    state.State, candidate.Work.Work.Slug,
                    state.PublisherVersionIdentifier, candidate.Version));
            }
        }

        if (failureCount == 0) return new BaselineReconciliation(matched, withdrawn);
        var suffix = failureCount > diagnostics.Count
            ? $"\n... {failureCount - diagnostics.Count} more failure(s) omitted"
            : "";
        throw new InvalidDataException(
            $"Fresh corpus candidate does not preserve {failureCount} protected baseline "
            + $"identity constraint(s) (showing at most {maximumDiagnostics}):\n"
            + string.Join("\n", diagnostics) + suffix);
    }

    private static string ResolvePublisherVersionIdentifier(
        bool baselineUsesPublisherVersionIdentifier,
        BaselineState state,
        ISourceAdapter adapter)
    {
        if (baselineUsesPublisherVersionIdentifier)
            return state.Version.PublisherVersionIdentifier
                   ?? throw new InvalidDataException(
                       "the protected v4 record has no publisher identity");

        if (adapter is not ILegacyVersionIdentityResolver resolver)
            throw new InvalidDataException(
                "the publisher adapter cannot recover a legacy publisher version identity");
        var expressions = state.Version.Expressions.Select(expression =>
        {
            var sourceUri = expression.SourceUri ?? expression.Text.Url
                ?? expression.Observations.LastOrDefault()?.SourceUri;
            if (string.IsNullOrWhiteSpace(sourceUri))
                throw new InvalidDataException(
                    "the protected legacy expression has no source identity");
            return new LegacyExpressionIdentity(expression.Language, sourceUri);
        }).ToArray();
        var resolved = resolver.ResolveLegacyVersionIdentity(new LegacyVersionIdentity(
            state.Version.WorkIdentifier, state.ValidFrom, expressions)).Value;
        if (string.IsNullOrWhiteSpace(resolved))
            throw new InvalidDataException(
                "the publisher adapter returned an empty legacy version identity");
        if (state.Version.PublisherVersionIdentifier is { Length: > 0 } persisted
            && !string.Equals(persisted, resolved, StringComparison.Ordinal))
            throw new InvalidDataException(
                "the recovered publisher identity disagrees with the retained record");
        return resolved;
    }

    private static DateOnly ParseValidFrom(
        VersionMeta version, string root, string versionDirectory)
    {
        if (DateOnly.TryParseExact(version.ValidFrom, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var validFrom))
            return validFrom;
        throw new InvalidDataException(
            $"Protected corpus version has invalid valid_from "
            + $"'{Bound(version.ValidFrom)}': "
            + Path.GetRelativePath(root, versionDirectory));
    }

    private static bool IsWithdrawn(VersionMeta version) =>
        version.Events.LastOrDefault(entry =>
            entry.Event is "withdrawn_from_source" or "resighted")?.Event
        == "withdrawn_from_source";

    private sealed record VerifiedObservationFile(
        string Source, string Relative, string Sha256);

    private sealed record PreparedBaselineImport(
        string DestinationDirectory,
        VersionMeta Meta,
        IReadOnlyList<VerifiedObservationFile> Files);

    private static IReadOnlyList<string> ImportMatchedBaselineStates(
        string protectedRoot,
        string stage,
        IReadOnlyList<MatchedBaselineState> matchedStates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var prepared = new List<PreparedBaselineImport>();
        var destinations = new HashSet<string>(PathComparer);
        var protectedPaths = new HashSet<string>(PathComparer);
        foreach (var matched in matchedStates.OrderBy(
                     item => item.Baseline.Description, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = matched.Baseline;
            RequireSafeWorkSlug(matched.DestinationWorkSlug, state.Description);
            if (!string.Equals(matched.Current.WorkId.Value,
                    state.Version.WorkIdentifier, StringComparison.Ordinal)
                || !string.Equals(matched.Current.Id.Value,
                    matched.PublisherVersionIdentifier, StringComparison.Ordinal)
                || matched.Current.ValidFrom != state.ValidFrom)
                throw new InvalidDataException(
                    "A matched baseline state changed publisher identity before import: "
                    + state.Description);

            var destination = Path.Combine(
                stage, "works", matched.DestinationWorkSlug, "versions",
                VersionIdentity.Create(state.ValidFrom, matched.PublisherVersionIdentifier));
            RequireUnusedDestination(destinations, destination, state.Description);

            var currentLanguages = matched.Current.Expressions
                .GroupBy(expression => expression.Language, StringComparer.Ordinal)
                .ToArray();
            if (currentLanguages.Any(group => group.Count() != 1))
                throw new InvalidDataException(
                    "A matched candidate state contains duplicate expression languages: "
                    + state.Description);
            var retainedLanguages = currentLanguages
                .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            var baselineLanguages = state.Version.Expressions
                .GroupBy(expression => expression.Language, StringComparer.Ordinal)
                .ToArray();
            if (baselineLanguages.Any(group => group.Count() != 1))
                throw new InvalidDataException(
                    "A matched baseline state contains duplicate expression languages: "
                    + state.Description);

            var meta = CloneVersionMeta(state);
            var priorExpressionCount = meta.Expressions.Count;
            meta.Expressions = meta.Expressions
                .Where(expression => retainedLanguages.Contains(expression.Language))
                .ToList();
            var revised = new List<string>();
            ReconcileTemporalMetadata(meta, matched.Current, now);
            ReconcileCurrentMetadata(meta, matched.Current, revised);
            revised.AddRange(RewriteDestinationIdentity(
                meta, matched.DestinationWorkSlug,
                matched.PublisherVersionIdentifier, destination));
            if (meta.Expressions.Count != priorExpressionCount)
                revised.Add("expressions");
            AppendMigrationRevision(meta, revised, now);

            var files = PrepareObservationFiles(
                protectedRoot, state.VersionDirectory, meta);
            protectedPaths.Add(state.VersionDirectory);
            foreach (var file in files) protectedPaths.Add(file.Source);
            prepared.Add(new PreparedBaselineImport(destination, meta, files));
        }

        WritePreparedBaselineImports(protectedRoot, prepared, cancellationToken);
        return protectedPaths.OrderBy(path => path, PathComparer).ToArray();
    }

    private static void RevalidateMatchedBaselinePaths(
        string protectedRoot,
        IReadOnlyList<string> protectedPaths,
        CancellationToken cancellationToken)
    {
        foreach (var path in protectedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerifiedCorpusPath.RequireExisting(
                protectedRoot, path, "matched baseline path");
        }
    }

    private static void ImportWithdrawnBaselineStates(
        string protectedRoot,
        string stage,
        IReadOnlyList<WithdrawnBaselineState> withdrawnStates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var prepared = new List<PreparedBaselineImport>();
        var destinations = new HashSet<string>(PathComparer);
        foreach (var withdrawn in withdrawnStates.OrderBy(
                     item => item.Baseline.Description, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = withdrawn.Baseline;
            if (!state.IsWithdrawn || !IsWithdrawn(state.Version))
                throw new InvalidDataException(
                    "A baseline record selected for tombstone migration is not terminally withdrawn: "
                    + state.Description);
            RequireSafeWorkSlug(withdrawn.DestinationWorkSlug, state.Description);

            var workDirectory = Path.Combine(
                stage, "works", withdrawn.DestinationWorkSlug);
            var workMetaPath = Path.Combine(workDirectory, "meta.json");
            var candidateWork = File.Exists(workMetaPath)
                ? JsonSerializer.Deserialize<WorkMeta>(
                    File.ReadAllText(workMetaPath), CorpusJson.Options)
                : null;
            if (candidateWork is null
                || !string.Equals(candidateWork.WorkIdentifier,
                    state.Version.WorkIdentifier, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "A withdrawn baseline state has no matching current work destination: "
                    + state.Description);

            var key = VersionIdentity.Create(
                state.ValidFrom, withdrawn.PublisherVersionIdentifier);
            var destination = Path.Combine(workDirectory, "versions", key);
            RequireUnusedDestination(destinations, destination, state.Description);

            var meta = CloneVersionMeta(state);
            var revised = RewriteDestinationIdentity(
                meta, withdrawn.DestinationWorkSlug,
                withdrawn.PublisherVersionIdentifier, destination);
            AppendMigrationRevision(meta, revised, now);
            if (!IsWithdrawn(meta))
                throw new InvalidDataException(
                    "Tombstone identity migration changed the terminal lifecycle: "
                    + state.Description);

            var files = PrepareObservationFiles(
                protectedRoot, state.VersionDirectory, state.Version);
            prepared.Add(new PreparedBaselineImport(destination, meta, files));
        }

        WritePreparedBaselineImports(protectedRoot, prepared, cancellationToken);
    }

    private static void WritePreparedBaselineImports(
        string protectedRoot,
        IReadOnlyList<PreparedBaselineImport> prepared,
        CancellationToken cancellationToken)
    {
        foreach (var item in prepared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(item.DestinationDirectory);
            foreach (var file in item.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = CheckedObservationPath(
                    item.DestinationDirectory, file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var source = VerifiedCorpusPath.RequireExisting(
                    protectedRoot, file.Source, "baseline observation file");
                var sourceIdentity = VerifySourceObservation(source, file);
                File.Copy(source, destination, overwrite: false);
                VerifyCopiedObservation(destination, file, sourceIdentity.Length);
            }
            File.WriteAllText(Path.Combine(item.DestinationDirectory, "meta.json"),
                JsonSerializer.Serialize(item.Meta, CorpusJson.Options) + "\n");
        }
    }

    private static VersionMeta CloneVersionMeta(BaselineState state) =>
        JsonSerializer.Deserialize<VersionMeta>(
            JsonSerializer.Serialize(state.Version, CorpusJson.Options),
            CorpusJson.Options)
        ?? throw new InvalidDataException(
            "A protected baseline state could not be copied: " + state.Description);

    private static List<string> RewriteDestinationIdentity(
        VersionMeta meta,
        string destinationWorkSlug,
        string publisherVersionIdentifier,
        string destination)
    {
        var revised = new List<string>();
        var lexId = $"{meta.Publisher}:{destinationWorkSlug}:{Path.GetFileName(destination)}";
        if (!string.Equals(meta.LexId, lexId, StringComparison.Ordinal))
        {
            meta.LexId = lexId;
            revised.Add("lex_id");
        }
        if (!string.Equals(meta.PublisherVersionIdentifier,
                publisherVersionIdentifier, StringComparison.Ordinal))
        {
            meta.PublisherVersionIdentifier = publisherVersionIdentifier;
            revised.Add("publisher_version_identifier");
        }
        return revised;
    }

    private static void ReconcileCurrentMetadata(
        VersionMeta meta, VersionRecord current, List<string> revised)
    {
        if (!string.Equals(meta.ValidTimeSource,
                current.ValidTimeSource, StringComparison.Ordinal))
        {
            meta.ValidTimeSource = current.ValidTimeSource;
            revised.Add("valid_time_source");
        }
        if (meta.Raw.Count != current.Raw.Count
            || meta.Raw.Any(item => !current.Raw.TryGetValue(item.Key, out var value)
                                    || !string.Equals(item.Value, value,
                                        StringComparison.Ordinal)))
        {
            meta.Raw = new Dictionary<string, string>(
                current.Raw, StringComparer.Ordinal);
            revised.Add("raw");
        }

        var relations = current.Relations.Select(relation =>
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["type"] = relation.Type,
                ["target"] = relation.Target.Value,
            }).ToList();
        if (!RelationsEqual(meta.Relations, relations))
        {
            meta.Relations = relations;
            revised.Add("relations");
        }

        var currentExpressions = current.Expressions.ToDictionary(
            expression => expression.Language, StringComparer.Ordinal);
        foreach (var expression in meta.Expressions)
        {
            var planned = currentExpressions[expression.Language];
            if (!string.Equals(expression.ValidTimeSource,
                    planned.ValidTimeSource, StringComparison.Ordinal))
            {
                expression.ValidTimeSource = planned.ValidTimeSource;
                revised.Add($"expressions.{expression.Language}.valid_time_source");
            }
        }
    }

    private static void ReconcileTemporalMetadata(
        VersionMeta meta, VersionRecord current, DateTimeOffset now)
    {
        var observedFrom = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        ReconcileDate(meta, meta.ValidTo, current.ValidTo?.ToString("yyyy-MM-dd"),
            "version", "valid_to", observedFrom, value => meta.ValidTo = value);
        ReconcileDate(meta, meta.PublicationDate,
            current.PublicationDate?.ToString("yyyy-MM-dd"),
            "version", "publication_date", observedFrom,
            value => meta.PublicationDate = value);

        var currentExpressions = current.Expressions.ToDictionary(
            expression => expression.Language, StringComparer.Ordinal);
        foreach (var expression in meta.Expressions)
        {
            var planned = currentExpressions[expression.Language];
            var plannedValidFrom = planned.ValidFrom?.ToString("yyyy-MM-dd");
            var scope = $"expression:{expression.Language}:{plannedValidFrom ?? "null"}";
            ReconcileDate(meta, expression.ValidFrom, plannedValidFrom,
                scope, "valid_from", observedFrom,
                value => expression.ValidFrom = value);
            ReconcileDate(meta, expression.ValidTo,
                planned.ValidTo?.ToString("yyyy-MM-dd"),
                scope, "valid_to", observedFrom,
                value => expression.ValidTo = value);
        }
    }

    private static void ReconcileDate(
        VersionMeta meta,
        string? previous,
        string? current,
        string scope,
        string field,
        string observedFrom,
        Action<string?> assign)
    {
        if (string.Equals(previous, current, StringComparison.Ordinal)) return;
        assign(current);
        var closed = field == "valid_to" && previous is null && current is not null;
        meta.Events.Add(new EventEntry
        {
            Event = closed ? "interval_closed" : "validity_revised",
            ObservedFrom = observedFrom,
            Scope = scope,
            Detail = closed
                ? $"field={field};new={current}"
                : $"field={field};old={previous ?? "null"};new={current ?? "null"}",
        });
    }

    private static bool RelationsEqual(
        IReadOnlyList<Dictionary<string, string>> current,
        IReadOnlyList<Dictionary<string, string>> planned) =>
        current.Count == planned.Count
        && current.Zip(planned).All(pair =>
            pair.First.Count == 2
            && pair.First.GetValueOrDefault("type") == pair.Second["type"]
            && pair.First.GetValueOrDefault("target") == pair.Second["target"]);

    private static void AppendMigrationRevision(
        VersionMeta meta, IReadOnlyList<string> revised, DateTimeOffset now)
    {
        if (revised.Count > 0)
            meta.Events.Add(new EventEntry
            {
                Event = "metadata_revised",
                ObservedFrom = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Scope = "version",
                Detail = "fields=" + string.Join(',', revised),
            });
        meta.RecordSha256 = CorpusHashes.RecordSha256(meta);
    }

    private static void RequireSafeWorkSlug(string workSlug, string description)
    {
        if (string.IsNullOrWhiteSpace(workSlug)
            || Path.IsPathRooted(workSlug)
            || workSlug is "." or ".."
            || workSlug.Contains('/') || workSlug.Contains('\\')
            || workSlug.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(workSlug, Path.GetFileName(workSlug),
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "A baseline state has an unsafe destination work slug: " + description);
    }

    private static void RequireUnusedDestination(
        HashSet<string> destinations, string destination, string description)
    {
        if (!destinations.Add(Path.GetFullPath(destination))
            || Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidDataException(
                "A baseline state collides with an existing destination: " + description);
    }

    private static IReadOnlyList<VerifiedObservationFile> PrepareObservationFiles(
        string protectedRoot, string versionDirectory, VersionMeta version)
    {
        versionDirectory = VerifiedCorpusPath.RequireExisting(
            protectedRoot, versionDirectory, "version directory");
        var files = new Dictionary<string, VerifiedObservationFile>(PathComparer);
        foreach (var observation in version.Expressions
                     .SelectMany(expression => expression.Observations))
        {
            if (string.IsNullOrWhiteSpace(observation.File)
                || string.IsNullOrWhiteSpace(observation.Sha256))
                throw new InvalidDataException(
                    "A baseline observation has no file or sha256.");
            var source = VerifiedCorpusPath.RequireExisting(
                protectedRoot,
                CheckedObservationPath(versionDirectory, observation.File),
                "observation file");
            if (!File.Exists(source))
                throw new InvalidDataException(
                    $"A baseline observation is missing: {observation.File}");
            var relative = Path.GetRelativePath(versionDirectory, source);
            var verified = new VerifiedObservationFile(
                source, relative, observation.Sha256);
            if (files.TryGetValue(relative, out var prior)
                && !CorpusHashes.Equal(prior.Sha256, verified.Sha256))
                throw new InvalidDataException(
                    $"A baseline observation file has conflicting identities: {relative}");
            files[relative] = verified;
        }
        return files.Values.OrderBy(file => file.Relative, StringComparer.Ordinal).ToArray();
    }

    private static string CheckedObservationPath(string versionDirectory, string relative)
    {
        var root = Path.GetFullPath(versionDirectory) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(
            versionDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(root, comparison))
            throw new InvalidDataException(
                $"A withdrawn baseline observation escapes its version directory: {relative}");
        return candidate;
    }

    private static (long Length, string Sha256) FileIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        return (stream.Length,
            Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    private static void VerifyCopiedObservation(
        string destination, VerifiedObservationFile expected, long sourceLength)
    {
        var actual = FileIdentity(destination);
        if (actual.Length != sourceLength
            || !CorpusHashes.Equal(actual.Sha256, expected.Sha256))
            throw new InvalidDataException(
                $"A migrated baseline observation changed while being copied: {expected.Relative}");
    }

    private static (long Length, string Sha256) VerifySourceObservation(
        string source, VerifiedObservationFile expected)
    {
        var actual = FileIdentity(source);
        if (!CorpusHashes.Equal(actual.Sha256, expected.Sha256))
            throw new InvalidDataException(
                $"A protected baseline observation changed before it was copied: {expected.Relative}");
        return actual;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string PublisherStateKey(string workIdentifier, string versionIdentifier) =>
        $"publisher\0{workIdentifier}\0{versionIdentifier}";

    private static string Bound(string? value)
    {
        var safe = new string((value ?? "<null>")
            .Select(character => char.IsControl(character) ? '?' : character).ToArray());
        return safe.Length <= 120 ? safe : safe[..117] + "...";
    }

    private static void ReplaceCandidateTree(
        string candidateRoot, string stage, string backup,
        Action<string, string>? beforeMove)
    {
        var targetWorks = Path.Combine(candidateRoot, "works");
        var targetManifest = Path.Combine(candidateRoot, "manifest.json");
        var targetNotice = Path.Combine(candidateRoot, "NOTICE");
        var stagedWorks = Path.Combine(stage, "works");
        var stagedManifest = Path.Combine(stage, "manifest.json");
        var stagedNotice = Path.Combine(stage, "NOTICE");
        if (!Directory.Exists(stagedWorks) || !File.Exists(stagedManifest)
            || !File.Exists(stagedNotice))
            throw new InvalidDataException("Fresh corpus candidate is incomplete.");

        Directory.CreateDirectory(backup);
        var oldWorks = Path.Combine(backup, "works");
        var oldManifest = Path.Combine(backup, "manifest.json");
        var oldNotice = Path.Combine(backup, "NOTICE");
        var movedOldWorks = false;
        var movedOldManifest = false;
        var movedOldNotice = false;
        var movedNewWorks = false;
        var movedNewManifest = false;
        var movedNewNotice = false;
        try
        {
            if (Directory.Exists(targetWorks))
            {
                MoveDirectory(targetWorks, oldWorks, beforeMove);
                movedOldWorks = true;
            }
            if (File.Exists(targetManifest))
            {
                MoveFile(targetManifest, oldManifest, beforeMove);
                movedOldManifest = true;
            }
            if (File.Exists(targetNotice))
            {
                MoveFile(targetNotice, oldNotice, beforeMove);
                movedOldNotice = true;
            }
            MoveDirectory(stagedWorks, targetWorks, beforeMove);
            movedNewWorks = true;
            MoveFile(stagedManifest, targetManifest, beforeMove);
            movedNewManifest = true;
            MoveFile(stagedNotice, targetNotice, beforeMove);
            movedNewNotice = true;
        }
        catch
        {
            if (movedNewNotice && File.Exists(targetNotice)) File.Delete(targetNotice);
            if (movedNewManifest && File.Exists(targetManifest)) File.Delete(targetManifest);
            if (movedNewWorks && Directory.Exists(targetWorks))
                Directory.Delete(targetWorks, recursive: true);
            if (movedOldManifest && File.Exists(oldManifest))
                MoveFile(oldManifest, targetManifest, beforeMove);
            if (movedOldNotice && File.Exists(oldNotice))
                MoveFile(oldNotice, targetNotice, beforeMove);
            if (movedOldWorks && Directory.Exists(oldWorks))
                MoveDirectory(oldWorks, targetWorks, beforeMove);
            throw;
        }
        try { Directory.Delete(backup, recursive: true); } catch { }
    }

    private static void MoveFile(
        string source, string destination, Action<string, string>? beforeMove)
    {
        beforeMove?.Invoke(source, destination);
        File.Move(source, destination);
    }

    private static void MoveDirectory(
        string source, string destination, Action<string, string>? beforeMove)
    {
        beforeMove?.Invoke(source, destination);
        Directory.Move(source, destination);
    }
}
