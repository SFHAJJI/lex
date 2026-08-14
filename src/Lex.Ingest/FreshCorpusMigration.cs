using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Law;

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
        var baseline = ReadBaselineInventory(root, baselineManifest.Schema);
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
            await writer.WriteAsync(adapter, cancellationToken, requireComplete: true,
                plan => RequirePreservedBaseline(baseline, plan));
            if (!writer.Committed)
                throw new InvalidDataException("Fresh corpus candidate was not committed.");

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

    private sealed record BaselineState(string Key, string Description);

    private sealed record BaselineInventory(
        bool UsesPublisherVersionIdentifier,
        IReadOnlyList<string> WorkIdentifiers,
        IReadOnlyList<BaselineState> States);

    private static BaselineInventory ReadBaselineInventory(string root, string schema)
    {
        var usesPublisherVersionIdentifier = schema == ManifestDoc.CurrentSchema;
        var works = new List<string>();
        var states = new List<BaselineState>();
        foreach (var workDirectory in Directory.EnumerateDirectories(
                     Path.Combine(root, "works")).Order(StringComparer.Ordinal))
        {
            var work = JsonSerializer.Deserialize<WorkMeta>(File.ReadAllText(
                           Path.Combine(workDirectory, "meta.json")), CorpusJson.Options)
                       ?? throw new InvalidDataException(
                           $"Protected corpus work metadata is empty: {workDirectory}");
            works.Add(work.WorkIdentifier);
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
                        + $"'{Bound(version.PublisherVersionIdentifier)}'"));
                    continue;
                }

                if (!DateOnly.TryParseExact(version.ValidFrom, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var validFrom))
                    throw new InvalidDataException(
                        $"Protected legacy corpus version has invalid valid_from "
                        + $"'{Bound(version.ValidFrom)}': "
                        + Path.GetRelativePath(root, versionDirectory));
                var expressionIdentity = ExpressionSourceIdentity(
                    version.Expressions.Select(expression => (
                        expression.Language,
                        expression.SourceUri ?? expression.Text.Url
                        ?? expression.Observations.LastOrDefault()?.SourceUri)));
                states.Add(new BaselineState(
                    LegacyStateKey(work.WorkIdentifier, validFrom, expressionIdentity),
                    $"work '{Bound(work.WorkIdentifier)}' version "
                    + validFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + $" expression/source {expressionIdentity}"));
            }
        }
        return new BaselineInventory(usesPublisherVersionIdentifier, works, states);
    }

    private static void RequirePreservedBaseline(
        BaselineInventory baseline, IReadOnlyList<CorpusPlannedWork> plan)
    {
        const int maximumDiagnostics = 20;
        var diagnostics = new List<string>();
        var failureCount = 0;
        void Fail(string message)
        {
            failureCount++;
            if (diagnostics.Count < maximumDiagnostics) diagnostics.Add(message);
        }

        var candidateWorkCounts = plan.GroupBy(item => item.Work.Id.Value,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var group in baseline.WorkIdentifiers.GroupBy(value => value,
                     StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var count = candidateWorkCounts.GetValueOrDefault(group.Key);
            if (group.Count() != 1)
                Fail($"protected baseline contains {group.Count()} works with identity "
                     + $"'{Bound(group.Key)}'");
            else if (count == 0)
                Fail($"candidate is missing work '{Bound(group.Key)}'");
            else if (count != 1)
                Fail($"candidate contains {count} works with identity '{Bound(group.Key)}'");
        }

        var candidateStates = new Dictionary<string, int>(StringComparer.Ordinal);
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
        }

        foreach (var group in baseline.States.GroupBy(state => state.Key,
                     StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var state = group.First();
            var candidates = candidateStates.GetValueOrDefault(group.Key);
            if (group.Count() != 1)
                Fail($"protected baseline contains {group.Count()} indistinguishable states for "
                     + state.Description);
            else if (candidates == 0)
                Fail("candidate is missing " + state.Description);
            else if (candidates != 1)
                Fail($"candidate contains {candidates} ambiguous matches for "
                     + state.Description);
        }

        if (failureCount == 0) return;
        var suffix = failureCount > diagnostics.Count
            ? $"\n... {failureCount - diagnostics.Count} more failure(s) omitted"
            : "";
        throw new InvalidDataException(
            $"Fresh corpus candidate does not preserve {failureCount} protected baseline "
            + $"identity constraint(s) (showing at most {maximumDiagnostics}):\n"
            + string.Join("\n", diagnostics) + suffix);
    }

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
