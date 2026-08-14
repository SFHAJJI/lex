using System.Text;

namespace Lex.Ingest;

/// <summary>
/// Stages one corpus refresh outside the live checkout and applies it only after acquisition,
/// reconciliation, and manifest construction all succeed. Applying is journalled too: a local
/// disk failure restores every replaced file and removes every newly-created file, so the caller
/// either sees the complete candidate or the exact prior corpus.
/// </summary>
internal sealed class CorpusCandidate : IDisposable
{
    private readonly string _root;
    private readonly string _stage;
    private readonly string _backup;

    public CorpusCandidate(string corpusRoot)
    {
        _root = Path.GetFullPath(corpusRoot);
        var token = Guid.NewGuid().ToString("N");
        _stage = Path.Combine(Path.GetTempPath(), $"lex-corpus-candidate-{token}");
        _backup = Path.Combine(Path.GetTempPath(), $"lex-corpus-backup-{token}");
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

    public void Commit()
    {
        var applied = new List<(string Target, string? Backup)>();
        try
        {
            foreach (var staged in Directory.EnumerateFiles(_stage, "*", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal).ToArray())
            {
                var relative = Path.GetRelativePath(_stage, staged);
                var target = CheckedTarget(Path.Combine(_root, relative));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = Path.Combine(_backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, overwrite: false);
                }
                File.Copy(staged, target, overwrite: true);
                File.Delete(staged);
                applied.Add((target, backup));
            }
        }
        catch
        {
            foreach (var (target, backup) in applied.AsEnumerable().Reverse())
            {
                if (backup is null)
                {
                    if (File.Exists(target)) File.Delete(target);
                }
                else
                {
                    File.Copy(backup, target, overwrite: true);
                }
            }
            throw;
        }
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
        return full;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_stage)) Directory.Delete(_stage, recursive: true); } catch { }
        try { if (Directory.Exists(_backup)) Directory.Delete(_backup, recursive: true); } catch { }
    }
}
