using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
            IReadOnlyList<WithdrawnBaselineState>? withdrawnStates = null;
            await writer.WriteAsync(adapter, cancellationToken, requireComplete: true,
                plan => withdrawnStates = RequirePreservedBaseline(
                    baseline, plan, adapter));
            if (!writer.Committed)
                throw new InvalidDataException("Fresh corpus candidate was not committed.");
            if (withdrawnStates is null)
                throw new InvalidDataException(
                    "Fresh corpus baseline reconciliation did not run.");

            ImportWithdrawnBaselineStates(
                stage, withdrawnStates, now, cancellationToken);

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

    private static ManifestDoc ReadManifest(string root) =>
        JsonSerializer.Deserialize<ManifestDoc>(
            File.ReadAllText(Path.Combine(root, "manifest.json")), CorpusJson.Options)
        ?? throw new InvalidDataException($"Corpus manifest is empty: {root}");

    private sealed record BaselineState(
        string Key,
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

    private static BaselineInventory ReadBaselineInventory(
        string root, string schema, string publisher)
    {
        var usesPublisherVersionIdentifier = schema == ManifestDoc.CurrentSchema;
        var works = new List<BaselineWork>();
        foreach (var workDirectory in Directory.EnumerateDirectories(
                     Path.Combine(root, "works")).Order(StringComparer.Ordinal))
        {
            var work = JsonSerializer.Deserialize<WorkMeta>(File.ReadAllText(
                           Path.Combine(workDirectory, "meta.json")), CorpusJson.Options)
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
            foreach (var versionDirectory in Directory.EnumerateDirectories(
                         Path.Combine(workDirectory, "versions")).Order(StringComparer.Ordinal))
            {
                var version = JsonSerializer.Deserialize<VersionMeta>(File.ReadAllText(
                                  Path.Combine(versionDirectory, "meta.json")), CorpusJson.Options)
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
                        PublisherStateKey(work.WorkIdentifier,
                            version.PublisherVersionIdentifier),
                        $"work '{Bound(work.WorkIdentifier)}' publisher version "
                        + $"'{Bound(version.PublisherVersionIdentifier)}'",
                        versionDirectory, version,
                        ParseValidFrom(version, root, versionDirectory),
                        IsWithdrawn(version)));
                    continue;
                }

                var validFrom = ParseValidFrom(version, root, versionDirectory);
                var expressionIdentity = ExpressionSourceIdentity(
                    version.Expressions.Select(expression => (
                        expression.Language,
                        expression.SourceUri ?? expression.Text.Url
                        ?? expression.Observations.LastOrDefault()?.SourceUri)));
                states.Add(new BaselineState(
                    LegacyStateKey(work.WorkIdentifier, validFrom, expressionIdentity),
                    $"work '{Bound(work.WorkIdentifier)}' version "
                    + validFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + $" expression/source {expressionIdentity}",
                    versionDirectory, version, validFrom, IsWithdrawn(version)));
            }
            works.Add(new BaselineWork(work, states));
        }
        return new BaselineInventory(usesPublisherVersionIdentifier, works);
    }

    private static IReadOnlyList<WithdrawnBaselineState> RequirePreservedBaseline(
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

        var candidateStates = new Dictionary<string, int>(StringComparer.Ordinal);
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
                var key = baseline.UsesPublisherVersionIdentifier
                    ? PublisherStateKey(item.Work.Id.Value, version.Id.Value)
                    : LegacyStateKey(item.Work.Id.Value, version.ValidFrom,
                        ExpressionSourceIdentity(version.Expressions.Select(expression => (
                            expression.Language, expression.SourceUri))));
                candidateStates[key] = candidateStates.GetValueOrDefault(key) + 1;
                if (!candidatePublisherStates.Add(PublisherStateKey(
                        item.Work.Id.Value, version.Id.Value)))
                    Fail($"candidate contains duplicate publisher version "
                         + $"'{Bound(version.Id.Value)}' for work "
                         + $"'{Bound(item.Work.Id.Value)}'");
            }

        var withdrawn = new List<WithdrawnBaselineState>();
        var withdrawnPublisherStates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in baseline.Works.SelectMany(work => work.States)
                     .GroupBy(state => state.Key,
                     StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var state = group.First();
            var candidates = candidateStates.GetValueOrDefault(group.Key);
            if (group.Count() != 1)
                Fail($"protected baseline contains {group.Count()} indistinguishable states for "
                     + state.Description);
            else if (candidates > 1)
                Fail($"candidate contains {candidates} ambiguous matches for "
                     + state.Description);
            else if (candidates == 0)
            {
                if (!state.IsWithdrawn)
                {
                    Fail("candidate is missing " + state.Description);
                    continue;
                }
                if (!candidateWorks.TryGetValue(state.Version.WorkIdentifier,
                        out var plannedWorks) || plannedWorks.Length != 1)
                {
                    Fail("candidate cannot retain withdrawn " + state.Description
                         + " because its work is not uniquely present in the current plan");
                    continue;
                }

                string publisherVersionIdentifier;
                try
                {
                    publisherVersionIdentifier = ResolvePublisherVersionIdentifier(
                        baseline.UsesPublisherVersionIdentifier, state, adapter);
                }
                catch (InvalidDataException ex)
                {
                    Fail("candidate cannot retain withdrawn " + state.Description
                         + ": " + ex.Message);
                    continue;
                }

                var publisherState = PublisherStateKey(
                    state.Version.WorkIdentifier, publisherVersionIdentifier);
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
                    state, plannedWorks[0].Work.Slug, publisherVersionIdentifier));
            }
        }

        if (failureCount == 0) return withdrawn;
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
        string Source, string Relative, long Length, string Sha256);

    private sealed record PreparedWithdrawnImport(
        string DestinationDirectory,
        VersionMeta Meta,
        IReadOnlyList<VerifiedObservationFile> Files);

    private static void ImportWithdrawnBaselineStates(
        string stage,
        IReadOnlyList<WithdrawnBaselineState> withdrawnStates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var prepared = new List<PreparedWithdrawnImport>();
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
            if (string.IsNullOrWhiteSpace(withdrawn.DestinationWorkSlug)
                || !string.Equals(withdrawn.DestinationWorkSlug,
                    Path.GetFileName(withdrawn.DestinationWorkSlug),
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "A withdrawn baseline state has an unsafe destination work slug: "
                    + state.Description);

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
            if (!destinations.Add(Path.GetFullPath(destination))
                || Directory.Exists(destination) || File.Exists(destination))
                throw new InvalidDataException(
                    "A withdrawn baseline state collides with an existing destination: "
                    + state.Description);

            var meta = JsonSerializer.Deserialize<VersionMeta>(
                JsonSerializer.Serialize(state.Version, CorpusJson.Options),
                CorpusJson.Options)
                ?? throw new InvalidDataException(
                    "A withdrawn baseline state could not be copied: " + state.Description);
            var revised = new List<string>();
            var lexId = $"{meta.Publisher}:{withdrawn.DestinationWorkSlug}:{key}";
            if (!string.Equals(meta.LexId, lexId, StringComparison.Ordinal))
            {
                meta.LexId = lexId;
                revised.Add("lex_id");
            }
            if (!string.Equals(meta.PublisherVersionIdentifier,
                    withdrawn.PublisherVersionIdentifier, StringComparison.Ordinal))
            {
                meta.PublisherVersionIdentifier = withdrawn.PublisherVersionIdentifier;
                revised.Add("publisher_version_identifier");
            }
            if (revised.Count > 0)
                meta.Events.Add(new EventEntry
                {
                    Event = "metadata_revised",
                    ObservedFrom = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Scope = "version",
                    Detail = "fields=" + string.Join(',', revised),
                });
            if (!IsWithdrawn(meta))
                throw new InvalidDataException(
                    "Tombstone identity migration changed the terminal lifecycle: "
                    + state.Description);
            meta.RecordSha256 = CorpusHashes.RecordSha256(meta);

            var files = VerifyObservationFiles(state.VersionDirectory, state.Version);
            prepared.Add(new PreparedWithdrawnImport(destination, meta, files));
        }

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
                File.Copy(file.Source, destination, overwrite: false);
                VerifyCopiedObservation(destination, file);
            }
            File.WriteAllText(Path.Combine(item.DestinationDirectory, "meta.json"),
                JsonSerializer.Serialize(item.Meta, CorpusJson.Options) + "\n");
        }
    }

    private static IReadOnlyList<VerifiedObservationFile> VerifyObservationFiles(
        string versionDirectory, VersionMeta version)
    {
        var files = new Dictionary<string, VerifiedObservationFile>(PathComparer);
        foreach (var observation in version.Expressions
                     .SelectMany(expression => expression.Observations))
        {
            if (string.IsNullOrWhiteSpace(observation.File)
                || string.IsNullOrWhiteSpace(observation.Sha256))
                throw new InvalidDataException(
                    "A withdrawn baseline observation has no file or sha256.");
            var source = CheckedObservationPath(versionDirectory, observation.File);
            if (!File.Exists(source))
                throw new InvalidDataException(
                    $"A withdrawn baseline observation is missing: {observation.File}");
            var (length, sha256) = FileIdentity(source);
            if (!CorpusHashes.Equal(sha256, observation.Sha256))
                throw new InvalidDataException(
                    $"A withdrawn baseline observation has a sha256 mismatch: {observation.File}");
            var relative = Path.GetRelativePath(versionDirectory, source);
            var verified = new VerifiedObservationFile(
                source, relative, length, observation.Sha256);
            if (files.TryGetValue(relative, out var prior)
                && (prior.Length != verified.Length
                    || !CorpusHashes.Equal(prior.Sha256, verified.Sha256)))
                throw new InvalidDataException(
                    $"A withdrawn baseline observation file has conflicting identities: {relative}");
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
        string destination, VerifiedObservationFile expected)
    {
        var actual = FileIdentity(destination);
        if (actual.Length != expected.Length
            || !CorpusHashes.Equal(actual.Sha256, expected.Sha256))
            throw new InvalidDataException(
                $"A migrated withdrawn observation changed while being copied: {expected.Relative}");
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string PublisherStateKey(string workIdentifier, string versionIdentifier) =>
        $"publisher\0{workIdentifier}\0{versionIdentifier}";

    private static string LegacyStateKey(
        string workIdentifier, DateOnly validFrom, string expressionIdentity) =>
        $"legacy\0{workIdentifier}\0"
        + validFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        + $"\0{expressionIdentity}";

    private static string ExpressionSourceIdentity(
        IEnumerable<(string Language, string? SourceUri)> expressions)
    {
        var canonical = string.Join("\n", expressions
            .OrderBy(expression => expression.Language, StringComparer.Ordinal)
            .ThenBy(expression => expression.SourceUri, StringComparer.Ordinal)
            .Select(expression => $"{expression.Language.Length}:{expression.Language}"
                + $"{expression.SourceUri?.Length ?? -1}:{expression.SourceUri}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

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
