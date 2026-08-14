using System.Text;

namespace Lex.Derive;

/// <summary>Writes one work to an adjacent directory, then replaces only that work after all of
/// its extraction and quality gates pass. Adjacent staging keeps moves on the same filesystem.</summary>
internal sealed class DerivedWorkCandidate : IDisposable
{
    private readonly string _target;
    private readonly string _stage;
    private readonly string _backup;
    private readonly Action<string>? _fileWritten;
    private bool _committed;

    public DerivedWorkCandidate(string target, Action<string>? fileWritten = null)
    {
        _target = Path.GetFullPath(target);
        var parent = Directory.GetParent(_target)?.FullName
            ?? throw new InvalidDataException($"Derived work path has no parent: {target}");
        Directory.CreateDirectory(parent);
        var token = Guid.NewGuid().ToString("N");
        var slug = Path.GetFileName(_target);
        _stage = Path.Combine(parent, $".{slug}.derive-stage-{token}");
        _backup = Path.Combine(parent, $".{slug}.derive-backup-{token}");
        Directory.CreateDirectory(_stage);
        _fileWritten = fileWritten;
    }

    public static void Recover(string target, Action<string> validateAcceptedOutput)
    {
        var fullTarget = Path.GetFullPath(target);
        var parent = Directory.GetParent(fullTarget)?.FullName
            ?? throw new InvalidDataException($"Derived work path has no parent: {target}");
        if (!Directory.Exists(parent)) return;
        var slug = Path.GetFileName(fullTarget);
        var backups = Directory.EnumerateDirectories(parent, $".{slug}.derive-backup-*")
            .Order(StringComparer.Ordinal).ToArray();
        var stages = Directory.EnumerateDirectories(parent, $".{slug}.derive-stage-*")
            .Order(StringComparer.Ordinal).ToArray();
        if (backups.Length > 1 || stages.Length > 1)
            throw new InvalidDataException(
                $"Ambiguous interrupted derive transaction for {slug}: "
                + $"backups={backups.Length}, stages={stages.Length}");

        if (Directory.Exists(fullTarget) && backups.Length == 1 && stages.Length == 1)
            throw new InvalidDataException(
                $"Ambiguous interrupted derive transaction for {slug}: "
                + "target, backup, and stage all exist");

        if (!Directory.Exists(fullTarget) && backups.Length == 1)
        {
            validateAcceptedOutput(backups[0]);
            Directory.Move(backups[0], fullTarget);
        }
        else if (Directory.Exists(fullTarget))
        {
            validateAcceptedOutput(fullTarget);
            // No stage means the accepted candidate reached target and only cleanup was
            // interrupted. The validated target wins over its superseded backup.
            if (backups.Length == 1)
                Directory.Delete(backups[0], recursive: true);
        }

        // A stage is never accepted output. It is safe to discard only after an old accepted
        // target has been validated/restored, or after proving there was no target or backup.
        if (stages.Length == 1)
            Directory.Delete(stages[0], recursive: true);
    }

    public void WriteText(string relativePath, string content)
    {
        var path = CheckedStagePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        _fileWritten?.Invoke(path);
    }

    public void Commit()
    {
        var movedOld = false;
        try
        {
            if (Directory.Exists(_target))
            {
                Directory.Move(_target, _backup);
                movedOld = true;
            }
            Directory.Move(_stage, _target);
            _committed = true;
        }
        catch
        {
            if (movedOld && Directory.Exists(_backup))
                Directory.Move(_backup, _target);
            throw;
        }
        if (movedOld)
            try { Directory.Delete(_backup, recursive: true); } catch { }
    }

    private string CheckedStagePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Derived candidate paths must be relative");
        var path = Path.GetFullPath(Path.Combine(_stage, relativePath));
        var prefix = _stage + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Derived candidate path escapes its root: {relativePath}");
        return path;
    }

    public void Dispose()
    {
        if (!_committed)
            try { if (Directory.Exists(_stage)) Directory.Delete(_stage, recursive: true); } catch { }
        else
            try { if (Directory.Exists(_backup)) Directory.Delete(_backup, recursive: true); } catch { }
    }
}
