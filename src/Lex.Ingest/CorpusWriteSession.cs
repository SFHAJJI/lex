using System.Security.Cryptography;
using System.Text;

namespace Lex.Ingest;

internal sealed class CorpusWriteSession : IDisposable
{
    private readonly FileStream _lock;
    private readonly string _rootPath;
    private readonly string? _creationTail;
    private FileStream? _rootIdentityLock;
    private HandleBoundRoot? _root;
    private HandleBoundRoot? _creationAnchor;

    private CorpusWriteSession(
        FileStream writerLock, string rootPath,
        HandleBoundRoot? root, HandleBoundRoot? creationAnchor,
        string? creationTail, CorpusBaseline baseline)
    {
        _lock = writerLock;
        _rootPath = rootPath;
        _root = root;
        _creationAnchor = creationAnchor;
        _creationTail = creationTail;
        Baseline = baseline;
    }

    public CorpusBaseline Baseline { get; }

    public static CorpusWriteSession Acquire(string corpusRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(corpusRoot));
        HandleBoundRoot? rootHandle = null;
        HandleBoundRoot? creationAnchor = null;
        string? creationTail = null;
        string lockIdentity;
        if (Directory.Exists(root))
        {
            VerifiedCorpusPath.RequireExisting(root, root, "writer root");
            rootHandle = HandleBoundRename.OpenRoot(root);
            lockIdentity = rootHandle.StableIdentity;
        }
        else
        {
            var existing = Path.GetDirectoryName(root)
                ?? throw new InvalidDataException(
                    "The corpus root has no parent directory.");
            var tail = Path.GetFileName(root);
            while (!Directory.Exists(existing))
            {
                tail = Path.Combine(Path.GetFileName(existing), tail);
                existing = Path.GetDirectoryName(existing)
                    ?? throw new InvalidDataException(
                        "The corpus root has no existing ancestor.");
            }
            VerifiedCorpusPath.RequireExisting(existing, existing,
                "writer root ancestor");
            creationAnchor = HandleBoundRename.OpenRoot(existing);
            creationTail = tail.Replace('\\', '/');
            lockIdentity = creationAnchor.StableIdentity + ":"
                + (OperatingSystem.IsWindows()
                    ? creationTail.ToUpperInvariant() : creationTail);
        }
        FileStream writerLock;
        try
        {
            writerLock = OpenWriterLock(lockIdentity);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            rootHandle?.Dispose();
            creationAnchor?.Dispose();
            throw new InvalidDataException(
                "The corpus writer lock is already held or cannot be acquired.", error);
        }
        try
        {
            if (rootHandle is not null)
            {
                using var current = HandleBoundRename.OpenRoot(root);
                if (!string.Equals(current.StableIdentity,
                        rootHandle.StableIdentity, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "The corpus root changed while its writer lock was acquired.");
                CorpusTransaction.Recover(rootHandle);
            }
            return new CorpusWriteSession(
                writerLock, root, rootHandle, creationAnchor,
                creationTail, CorpusBaseline.Capture(root));
        }
        catch
        {
            rootHandle?.Dispose();
            creationAnchor?.Dispose();
            writerLock.Dispose();
            throw;
        }
    }

    public HandleBoundRoot EnsureRoot()
    {
        if (_root is not null) return _root;
        var anchor = _creationAnchor
            ?? throw new InvalidOperationException(
                "The corpus root has no trusted creation anchor.");
        var tail = _creationTail
            ?? throw new InvalidOperationException(
                "The corpus root has no trusted creation path.");
        HandleBoundRoot? created = null;
        try
        {
            anchor.EnsureDirectory(tail);
            anchor.FlushDirectory(".");
            created = anchor.OpenRelativeRoot(tail, _rootPath, create: false);
            _rootIdentityLock = OpenWriterLock(created.StableIdentity);
            _root = created;
            created = null;
            _creationAnchor.Dispose();
            _creationAnchor = null;
            return _root;
        }
        finally { created?.Dispose(); }
    }

    private static FileStream OpenWriterLock(string lockIdentity)
    {
        var identity = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(lockIdentity)));
        var lockDirectory = Path.Combine(
            Path.GetTempPath(), "lex-corpus-writer-locks");
        Directory.CreateDirectory(lockDirectory);
        return new FileStream(
            Path.Combine(lockDirectory, identity + ".lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 1, FileOptions.None);
    }

    public void Dispose()
    {
        _root?.Dispose();
        _creationAnchor?.Dispose();
        _rootIdentityLock?.Dispose();
        _lock.Dispose();
    }
}

internal sealed class CorpusBaseline
{
    private sealed record Entry(bool IsDirectory, long Length, string? Sha256);

    private readonly string _root;
    private readonly bool _rootExists;
    private readonly IReadOnlyDictionary<string, Entry> _entries;

    private CorpusBaseline(
        string root, bool rootExists, IReadOnlyDictionary<string, Entry> entries)
    {
        _root = root;
        _rootExists = rootExists;
        _entries = entries;
    }

    public bool IsEmpty => _entries.Count == 0;

    public static CorpusBaseline Capture(string corpusRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(corpusRoot));
        if (File.Exists(root))
            throw new InvalidDataException("The corpus root is a file.");
        if (!Directory.Exists(root))
            return new CorpusBaseline(root, rootExists: false,
                new Dictionary<string, Entry>(PathComparer));

        VerifiedCorpusPath.RequireExisting(root, root, "writer baseline root");
        return new CorpusBaseline(root, rootExists: true, ReadEntries(root));
    }

    public void RequireUnchanged()
    {
        CorpusBaseline current;
        try
        {
            current = Capture(_root);
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException
                                      or InvalidDataException)
        {
            throw Changed(error.Message, error);
        }

        if (_rootExists != current._rootExists
            || _entries.Count != current._entries.Count)
            throw Changed("the path inventory differs");
        foreach (var (path, expected) in _entries)
            if (!current._entries.TryGetValue(path, out var actual)
                || expected != actual)
                throw Changed($"'{path}' differs");
    }

    public void RequireOriginalEntriesUnchanged()
    {
        CorpusBaseline current;
        try { current = Capture(_root); }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException
                                      or InvalidDataException)
        {
            throw Changed(error.Message, error);
        }
        if (_rootExists != current._rootExists
            && !(!_rootExists && current._rootExists && _entries.Count == 0))
            throw Changed("the corpus root differs");
        foreach (var (path, expected) in _entries)
            if (!current._entries.TryGetValue(path, out var actual)
                || expected != actual)
                throw Changed($"'{path}' differs");
    }

    public void RequireTargetUnchanged(string target)
    {
        var full = Path.GetFullPath(target);
        var relative = Path.GetRelativePath(_root, full).Replace('\\', '/');
        if (relative == "." || relative.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw Changed("a candidate target escapes the corpus root");

        if (!_entries.TryGetValue(relative, out var expected))
        {
            if (File.Exists(full) || Directory.Exists(full))
                throw Changed($"new target '{relative}' appeared");
            RequireSafeExistingAncestor(full);
            return;
        }
        if (expected.IsDirectory)
        {
            if (!Directory.Exists(full))
                throw Changed($"target '{relative}' changed type or disappeared");
            VerifiedCorpusPath.RequireExisting(_root, full,
                "compare-and-swap target directory");
            return;
        }
        if (!File.Exists(full))
            throw Changed($"target '{relative}' changed type or disappeared");
        VerifiedCorpusPath.RequireExisting(_root, full, "compare-and-swap target");
        var actual = ReadFile(full);
        if (actual != expected)
            throw Changed($"target '{relative}' differs");
    }

    private void RequireSafeExistingAncestor(string path)
    {
        var current = path;
        while (!File.Exists(current) && !Directory.Exists(current))
            current = Path.GetDirectoryName(current)
                ?? throw Changed("a candidate target has no existing ancestor");
        if (_rootExists)
            VerifiedCorpusPath.RequireExisting(
                _root, current, "compare-and-swap target ancestor");
    }

    private static Dictionary<string, Entry> ReadEntries(string root)
    {
        var entries = new Dictionary<string, Entry>(PathComparer);
        Visit(root);
        return entries;

        void Visit(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory)
                         .Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (string.Equals(relative, CorpusTransaction.DirectoryName,
                        StringComparison.Ordinal))
                    continue;
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        $"Corpus writer baseline contains a reparse point or symbolic link: {relative}");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    entries.Add(relative, new Entry(true, 0, null));
                    Visit(path);
                }
                else
                {
                    entries.Add(relative, ReadFile(path));
                }
            }
        }
    }

    private static Entry ReadFile(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 128 * 1024, FileOptions.SequentialScan);
        return new Entry(false, stream.Length,
            Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    private static InvalidDataException Changed(string detail, Exception? inner = null) =>
        new($"Corpus baseline changed during ingest: {detail}.", inner);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
