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
            await writer.WriteAsync(adapter, cancellationToken, requireComplete: true);
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

            var baselineWorks = baselineIntegrity.ActualWorks;
            if (baselineWorks > 0 && candidateManifest.Works * 100L < baselineWorks * 95L)
                throw new InvalidDataException(
                    $"Fresh corpus work count fell from {baselineWorks} to "
                    + $"{candidateManifest.Works} (>5%); migration refused.");
            candidateManifest.MigrationBaselineWorks = baselineWorks;
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
