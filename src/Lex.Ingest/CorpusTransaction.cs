using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lex.Ingest;

internal sealed class CorpusTransactionJournal
{
    public const string CurrentSchema = "lex-corpus-transaction/1";
    public string Schema { get; set; } = CurrentSchema;
    public bool RequireCorpusIntegrity { get; set; }
    public List<CorpusTransactionEntry> Entries { get; set; } = [];
}

internal sealed class CorpusTransactionEntry
{
    public required string Operation { get; set; }
    public required string Path { get; set; }
    public string? Payload { get; set; }
    public string? BeforeSha256 { get; set; }
    public string? AfterSha256 { get; set; }
}

internal static class CorpusTransaction
{
    internal const string DirectoryName = ".lex-corpus-transaction";
    private const string WriteOperation = "write";
    private const string DeleteFileOperation = "delete_file";
    private const string DeleteDirectoryOperation = "delete_directory";
    private const string PayloadDirectory = DirectoryName + "/payload";
    private const string InstallDirectory = DirectoryName + "/install";
    private const string TrashDirectory = DirectoryName + "/trash";
    private const string JournalPath = DirectoryName + "/journal.json";
    private const string JournalTemporaryPath = DirectoryName + "/journal.tmp";
    private const string CompletedRunsPath = "completed-runs.json";
    private const int MaximumJournalBytes = 32 * 1024 * 1024;
    private const int MaximumEntries = 250_000;

    private sealed record Deletion(string Operation, string Path, string BeforeSha256);

    public static void Recover(HandleBoundRoot root)
    {
        if (!root.Exists(DirectoryName, expectDirectory: true)) return;
        if (!root.Exists(JournalPath, expectDirectory: false))
        {
            RequireOrphanTransactionShape(root);
            root.DeleteTree(DirectoryName);
            root.FlushDirectory(".");
            return;
        }

        var journal = ReadJournal(root);
        Apply(root, journal, afterPublish: null);
        RequireIntegrity(root, journal);
        Complete(root, journal);
    }

    public static void CommitFiles(
        HandleBoundRoot root,
        string stagedRoot,
        Action<int, string>? afterPublish) =>
        Commit(root, stagedRoot, [], requireCorpusIntegrity: true, afterPublish);

    public static void CommitSnapshot(
        HandleBoundRoot root,
        string stagedRoot,
        CorpusBaseline baseline,
        Action<int, string>? afterPublish)
    {
        baseline.RequireOriginalEntriesUnchanged();
        Commit(root, stagedRoot,
            CollectSnapshotDeletions(root, stagedRoot),
            requireCorpusIntegrity: true, afterPublish);
    }

    private static void Commit(
        HandleBoundRoot root,
        string stagedRoot,
        IReadOnlyList<Deletion> deletions,
        bool requireCorpusIntegrity,
        Action<int, string>? afterPublish)
    {
        if (root.Exists(DirectoryName, expectDirectory: true))
            throw new InvalidDataException(
                "A corpus transaction is still present after recovery.");

        var stagedFiles = Directory.EnumerateFiles(
                stagedRoot, "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: CanonicalRelative(
                Path.GetRelativePath(stagedRoot, path))))
            .Select(item => (item.Path, item.Relative, Priority: Priority(item.Relative)))
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Relative, StringComparer.Ordinal)
            .ToArray();
        if (stagedFiles.Length + deletions.Count == 0) return;
        if (stagedFiles.Length + deletions.Count > MaximumEntries)
            throw new InvalidDataException(
                "The corpus transaction exceeds its entry limit.");

        root.EnsureDirectory(DirectoryName);
        root.EnsureDirectory(PayloadDirectory);
        root.EnsureDirectory(InstallDirectory);
        root.EnsureDirectory(TrashDirectory);
        var journal = new CorpusTransactionJournal
        {
            RequireCorpusIntegrity = requireCorpusIntegrity,
        };
        var journalPublished = false;
        try
        {
            var payloadIndex = 0;
            foreach (var item in stagedFiles.Where(item => item.Priority == 0))
                AddWrite(item.Path, item.Relative);
            foreach (var deletion in deletions
                         .OrderBy(value => value.Operation == DeleteFileOperation ? 0 : 1)
                         .ThenByDescending(value => value.Path.Count(character => character == '/'))
                         .ThenBy(value => value.Path, StringComparer.Ordinal))
                journal.Entries.Add(new CorpusTransactionEntry
                {
                    Operation = deletion.Operation,
                    Path = deletion.Path,
                    BeforeSha256 = deletion.BeforeSha256,
                });
            foreach (var item in stagedFiles.Where(item => item.Priority == 1))
                AddWrite(item.Path, item.Relative);
            foreach (var item in stagedFiles.Where(item => item.Priority == 2))
                AddWrite(item.Path, item.Relative);

            Validate(journal);
            root.FlushDirectory(PayloadDirectory);
            root.FlushDirectory(InstallDirectory);
            root.FlushDirectory(TrashDirectory);
            root.FlushDirectory(DirectoryName);
            var journalBytes = JsonSerializer.SerializeToUtf8Bytes(
                journal, CorpusJson.Options);
            if (journalBytes.Length is <= 0 or > MaximumJournalBytes)
                throw new InvalidDataException(
                    "The corpus transaction journal exceeds its size limit.");
            root.WriteNewFile(JournalTemporaryPath, journalBytes);
            root.Move(JournalTemporaryPath, JournalPath, replace: false);
            root.FlushDirectory(DirectoryName);
            root.FlushDirectory(".");
            journalPublished = true;

            Apply(root, journal, afterPublish);
            RequireIntegrity(root, journal);
            Complete(root, journal);
            return;

            void AddWrite(string stagedPath, string relative)
            {
                _ = VerifiedCorpusPath.RequireExisting(
                    stagedRoot, stagedPath, "candidate payload");
                var payload = $"{PayloadDirectory}/{payloadIndex++:D8}.bin";
                using (var source = new FileStream(
                           stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                           128 * 1024, FileOptions.SequentialScan))
                using (var destination = root.CreateNewFile(payload))
                {
                    source.CopyTo(destination);
                    destination.Flush(flushToDisk: true);
                }
                journal.Entries.Add(new CorpusTransactionEntry
                {
                    Operation = WriteOperation,
                    Path = relative,
                    Payload = payload,
                    BeforeSha256 = root.HashFileOrNull(relative),
                    AfterSha256 = root.HashFile(payload),
                });
            }
        }
        catch
        {
            if (!journalPublished)
            {
                try { root.DeleteTree(DirectoryName); } catch { }
            }
            throw;
        }
    }

    private static void Apply(
        HandleBoundRoot root,
        CorpusTransactionJournal journal,
        Action<int, string>? afterPublish)
    {
        Validate(journal);
        for (var index = 0; index < journal.Entries.Count; index++)
        {
            var entry = journal.Entries[index];
            switch (entry.Operation)
            {
                case WriteOperation:
                    ApplyWrite(root, entry, index);
                    break;
                case DeleteFileOperation:
                    ApplyDelete(root, entry, index, directory: false);
                    break;
                case DeleteDirectoryOperation:
                    ApplyDelete(root, entry, index, directory: true);
                    break;
                default:
                    throw new InvalidDataException(
                        "The corpus transaction contains an unknown operation.");
            }
            afterPublish?.Invoke(index + 1, entry.Path);
        }

        foreach (var entry in journal.Entries)
            if (entry.Operation == WriteOperation)
            {
                if (!CorpusHashes.Equal(root.HashFile(entry.Path), entry.AfterSha256!))
                    throw new InvalidDataException(
                        $"Corpus transaction did not publish '{entry.Path}'.");
            }
            else if (root.EntryExists(entry.Path))
                throw new InvalidDataException(
                    $"Corpus transaction did not remove '{entry.Path}'.");
    }

    private static void ApplyWrite(
        HandleBoundRoot root, CorpusTransactionEntry entry, int index)
    {
        var payloadHash = root.HashFile(entry.Payload!);
        if (!CorpusHashes.Equal(payloadHash, entry.AfterSha256!))
            throw new InvalidDataException(
                $"Corpus transaction payload is corrupt: {entry.Payload}");

        var current = root.HashFileOrNull(entry.Path);
        if (HashEquals(current, entry.AfterSha256)) return;
        if (!HashEquals(current, entry.BeforeSha256))
            throw new InvalidDataException(
                $"Corpus transaction target has an unexpected state: {entry.Path}");
        var install = $"{InstallDirectory}/{index:D8}.bin";
        if (root.Exists(install, expectDirectory: false))
            root.DeleteFile(install);
        using (var payload = root.OpenRead(entry.Payload!))
        using (var destination = root.CreateNewFile(install))
        {
            payload.CopyTo(destination);
            destination.Flush(flushToDisk: true);
        }
        root.Move(install, entry.Path,
            replace: entry.BeforeSha256 is not null);
        root.FlushFile(entry.Path);
        root.FlushDirectory(Parent(entry.Path));
        if (!CorpusHashes.Equal(root.HashFile(entry.Path), entry.AfterSha256!))
            throw new InvalidDataException(
                $"Corpus transaction target verification failed: {entry.Path}");
    }

    private static void ApplyDelete(
        HandleBoundRoot root,
        CorpusTransactionEntry entry,
        int index,
        bool directory)
    {
        if (!root.EntryExists(entry.Path)) return;
        var current = directory
            ? root.HashTree(entry.Path)
            : root.HashFile(entry.Path);
        if (!CorpusHashes.Equal(current, entry.BeforeSha256!))
            throw new InvalidDataException(
                $"Corpus transaction delete target has an unexpected state: {entry.Path}");
        var trash = $"{TrashDirectory}/{index:D8}";
        if (root.EntryExists(trash))
            throw new InvalidDataException(
                "The corpus transaction trash target is already occupied.");
        root.Move(entry.Path, trash, replace: false);
        root.FlushDirectory(Parent(entry.Path));
    }

    private static IReadOnlyList<Deletion> CollectSnapshotDeletions(
        HandleBoundRoot root, string stagedRoot)
    {
        var result = new List<Deletion>();
        var liveWorks = Path.Combine(root.RootPath, "works");
        var stagedWorks = Path.Combine(stagedRoot, "works");
        if (!Directory.Exists(liveWorks)) return result;
        _ = VerifiedCorpusPath.RequireExisting(
            root.RootPath, liveWorks, "snapshot works directory");
        Visit(liveWorks, stagedWorks);
        return result;

        void Visit(string liveDirectory, string stagedDirectory)
        {
            foreach (var live in Directory.EnumerateFileSystemEntries(liveDirectory)
                         .Order(StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(live);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "The protected snapshot contains a link.");
                var relative = CanonicalRelative(Path.GetRelativePath(root.RootPath, live));
                var staged = Path.Combine(stagedDirectory, Path.GetFileName(live));
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (File.Exists(staged))
                        throw new InvalidDataException(
                            $"Snapshot path changes type: {relative}");
                    if (!Directory.Exists(staged))
                        result.Add(new Deletion(DeleteDirectoryOperation,
                            relative, root.HashTree(relative)));
                    else
                        Visit(live, staged);
                }
                else
                {
                    if (Directory.Exists(staged))
                        throw new InvalidDataException(
                            $"Snapshot path changes type: {relative}");
                    if (!File.Exists(staged))
                        result.Add(new Deletion(DeleteFileOperation,
                            relative, root.HashFile(relative)));
                }
            }
        }
    }

    private static void RequireIntegrity(
        HandleBoundRoot root, CorpusTransactionJournal journal)
    {
        if (!journal.RequireCorpusIntegrity) return;
        var report = CorpusIntegrity.Verify(root.RootPath);
        if (!report.IsValid)
            throw new InvalidDataException(
                "Published corpus transaction failed integrity:\n"
                + string.Join("\n", report.Errors));
    }

    private static void Complete(
        HandleBoundRoot root, CorpusTransactionJournal journal)
    {
        root.DeleteFile(JournalPath);
        root.FlushDirectory(DirectoryName);
        foreach (var entry in journal.Entries)
            if (entry.Payload is not null
                && root.Exists(entry.Payload, expectDirectory: false))
                root.DeleteFile(entry.Payload);
        root.DeleteTree(InstallDirectory);
        root.DeleteTree(PayloadDirectory);
        root.DeleteTree(TrashDirectory);
        root.DeleteDirectory(DirectoryName);
        root.FlushDirectory(".");
    }

    private static CorpusTransactionJournal ReadJournal(HandleBoundRoot root)
    {
        using var stream = root.OpenRead(JournalPath);
        if (stream.Length is <= 0 or > MaximumJournalBytes)
            throw new InvalidDataException(
                "The corpus transaction journal has an invalid size.");
        var options = new JsonSerializerOptions(CorpusJson.Options)
        {
            MaxDepth = 8,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var journal = JsonSerializer.Deserialize<CorpusTransactionJournal>(
            stream, options)
            ?? throw new InvalidDataException(
                "The corpus transaction journal is empty.");
        Validate(journal);
        return journal;
    }

    private static void RequireOrphanTransactionShape(HandleBoundRoot root)
    {
        var directory = Path.Combine(root.RootPath, DirectoryName);
        _ = VerifiedCorpusPath.RequireExisting(
            root.RootPath, directory, "orphan transaction directory");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "payload", "install", "trash", "journal.tmp",
        };
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(entry);
            if (!allowed.Contains(name))
                throw new InvalidDataException(
                    "An unjournaled corpus transaction contains an unexpected entry.");
            _ = VerifiedCorpusPath.RequireExisting(
                root.RootPath, entry, "orphan transaction entry");
        }
    }

    private static void Validate(CorpusTransactionJournal journal)
    {
        if (journal.Entries is null
            || journal.Schema != CorpusTransactionJournal.CurrentSchema
            || journal.Entries.Count is <= 0 or > MaximumEntries)
            throw new InvalidDataException(
                "The corpus transaction journal schema or count is invalid.");
        var paths = new HashSet<string>(PathComparer);
        var payloads = new HashSet<string>(PathComparer);
        var sawLedger = false;
        foreach (var entry in journal.Entries)
        {
            if (entry is null)
                throw new InvalidDataException(
                    "The corpus transaction journal contains a null entry.");
            var path = CanonicalRelative(entry.Path);
            if (!string.Equals(path, entry.Path, StringComparison.Ordinal)
                || path.StartsWith(DirectoryName + "/", StringComparison.Ordinal)
                || !paths.Add(path))
                throw new InvalidDataException(
                    "The corpus transaction journal contains an invalid target path.");
            switch (entry.Operation)
            {
                case WriteOperation:
                    if (entry.Payload is null || entry.AfterSha256 is null)
                        throw new InvalidDataException(
                            "A corpus transaction write is incomplete.");
                    var payload = CanonicalRelative(entry.Payload);
                    if (!string.Equals(payload, entry.Payload, StringComparison.Ordinal)
                        || !payload.StartsWith(PayloadDirectory + "/",
                            StringComparison.Ordinal)
                        || !payloads.Add(payload))
                        throw new InvalidDataException(
                            "The corpus transaction journal contains an invalid payload path.");
                    RequireSha256(entry.AfterSha256);
                    if (entry.BeforeSha256 is not null)
                        RequireSha256(entry.BeforeSha256);
                    break;
                case DeleteFileOperation:
                case DeleteDirectoryOperation:
                    if (entry.Payload is not null || entry.AfterSha256 is not null
                        || entry.BeforeSha256 is null)
                        throw new InvalidDataException(
                            "A corpus transaction delete is incomplete.");
                    RequireSha256(entry.BeforeSha256);
                    break;
                default:
                    throw new InvalidDataException(
                        "The corpus transaction journal contains an unknown operation.");
            }
            if (IsCompletedRuns(path)) sawLedger = true;
            else if (sawLedger)
                throw new InvalidDataException(
                    "The completed-run ledger must be the final transaction entry.");
        }
    }

    private static int Priority(string path) => IsCompletedRuns(path) ? 2
        : path is "manifest.json" or "NOTICE" ? 1 : 0;

    private static string CanonicalRelative(string? path)
    {
        if (path is null)
            throw new InvalidDataException(
                "A corpus transaction path is missing.");
        var canonical = path.Replace('\\', '/');
        if (canonical.Length == 0 || canonical.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(path)
            || canonical.Split('/').Any(component => component is "" or "." or ".."))
            throw new InvalidDataException(
                "A corpus transaction path is not a canonical relative path.");
        return canonical;
    }

    private static bool IsCompletedRuns(string path) =>
        string.Equals(path, CompletedRunsPath, StringComparison.Ordinal);

    private static string Parent(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? "." : path[..separator];
    }

    private static bool HashEquals(string? left, string? right) =>
        left is null || right is null
            ? left is null && right is null
            : CorpusHashes.Equal(left, right);

    private static void RequireSha256(string? value)
    {
        if (value is null || value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
            throw new InvalidDataException(
                "The corpus transaction contains a non-canonical SHA-256 digest.");
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
