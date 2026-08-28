using System.Text;

namespace Lex.Ingest;

/// <summary>
/// Stages one corpus refresh outside the live checkout and applies it only after acquisition,
/// reconciliation, and manifest construction all succeed. Publication copies a durable payload
/// into an in-root journal and replaces each target atomically. A later writer rolls an
/// interrupted publication forward before it reads the completed-run ledger.
/// </summary>
internal sealed class CorpusCandidate : IDisposable
{
    private readonly string _root;
    private readonly string _stage;

    public CorpusCandidate(string corpusRoot)
    {
        _root = Path.GetFullPath(corpusRoot);
        var token = Guid.NewGuid().ToString("N");
        var protectedParent = Path.GetDirectoryName(_root)
            ?? throw new InvalidDataException("The corpus root has no parent directory.");
        _stage = Path.Combine(protectedParent, $".lex-corpus-candidate-{token}");
        Directory.CreateDirectory(_stage);
    }

    public bool Exists(string target) => File.Exists(Staged(target)) || File.Exists(CheckedTarget(target));

    public bool HasChanges => Directory.EnumerateFiles(
        _stage, "*", SearchOption.AllDirectories).Any();

    public async Task WriteBytesAsync(string target, byte[] bytes, CancellationToken ct)
    {
        var staged = Staged(target);
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        await File.WriteAllBytesAsync(staged, bytes, ct);
    }

    public async Task WriteTextAsync(string target, string content, CancellationToken ct) =>
        await WriteBytesAsync(target, Encoding.UTF8.GetBytes(content), ct);

    public void WriteIfChanged(string target, string content)
    {
        var canonical = content.TrimEnd('\n') + "\n";
        var existing = File.Exists(Staged(target)) ? File.ReadAllText(Staged(target))
            : File.Exists(CheckedTarget(target)) ? File.ReadAllText(CheckedTarget(target)) : null;
        if (existing?.TrimEnd('\n') == canonical.TrimEnd('\n')) return;
        var staged = Staged(target);
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, canonical);
    }

    public void Commit(
        CorpusBaseline baseline,
        CorpusWriteSession session,
        Action? beforeCompareAndSwap = null,
        Action<int, string>? afterPublish = null)
    {
        baseline.RequireUnchanged();
        var root = session.EnsureRoot();
        beforeCompareAndSwap?.Invoke();
        baseline.RequireOriginalEntriesUnchanged();
        CorpusTransaction.CommitFiles(
            root, _stage, afterPublish);
    }

    private string Staged(string target) => Path.Combine(_stage,
        Path.GetRelativePath(_root, CheckedTarget(target)));

    private string CheckedTarget(string target)
    {
        var full = Path.GetFullPath(target);
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Corpus candidate path escapes its root: {target}");
        if (Directory.Exists(_root))
        {
            var existing = full;
            while (!File.Exists(existing) && !Directory.Exists(existing))
                existing = Path.GetDirectoryName(existing)
                    ?? throw new InvalidDataException(
                        $"Corpus candidate target has no existing ancestor: {target}");
            VerifiedCorpusPath.RequireExisting(_root, existing, "candidate target ancestor");
        }
        return full;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_stage)) Directory.Delete(_stage, recursive: true); } catch { }
    }
}
