namespace Lex.V3.Ingest;

/// <summary>
/// A mount build's staging directory (slice S4b): <c>lex-v3-mount-build-&lt;32 hex&gt;</c> under a staging root, holding the
/// index files the build wrote, and an owner file the build holds open, exclusively, until it is disposed. A process that
/// ends without disposing its build (a derive the runner's guard stops) releases the owner file as it ends, which is how
/// <see cref="SweepStale"/> tells a directory such a process left from a live build's.
/// </summary>
internal sealed class V3MountStaging : IDisposable
{
    /// <summary>The prefix of every staging directory a mount build makes; a sweep never touches a directory without it.</summary>
    internal const string DirectoryPrefix = "lex-v3-mount-build-";

    /// <summary>The file a live build holds open in its staging directory.</summary>
    internal const string OwnerFileName = "owner.lock";

    private FileStream? _owner;

    private V3MountStaging(string path, FileStream owner)
    {
        Path = path;
        _owner = owner;
    }

    /// <summary>The staging directory.</summary>
    internal string Path { get; }

    /// <summary>A new staging directory under <paramref name="stagingRoot"/>, its owner file held open by this object.</summary>
    internal static V3MountStaging Create(string stagingRoot)
    {
        var path = System.IO.Path.Combine(stagingRoot, DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            return new(path, new FileStream(
                System.IO.Path.Combine(path, OwnerFileName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
        }
        catch
        {
            Directory.Delete(path, recursive: true);
            throw;
        }
    }

    /// <summary>Releases the owner file and removes the directory with whatever is left in it.</summary>
    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    /// <summary>
    /// Removes the staging directories mount builds left under <paramref name="stagingRoot"/> when their processes ended without
    /// disposing them. A directory is removed only when all of these hold: its name is exactly the prefix and 32 hex digits;
    /// it is a plain directory (not a link); it was last written before <paramref name="untouchedSince"/> (the sweeping
    /// process's start; a directory's last write moves with every file made or removed in it, and is kept on every system,
    /// where a creation time is not); and no live build holds its owner file open. Anything else is left as it is. Returns
    /// each removed directory with the bytes it held.
    /// </summary>
    internal static IReadOnlyList<(string Path, long ByteLength)> SweepStale(string stagingRoot, DateTimeOffset untouchedSince)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        var swept = new List<(string Path, long ByteLength)>();
        if (!Directory.Exists(stagingRoot))
        {
            return swept;
        }

        var cutoff = untouchedSince.UtcDateTime;
        foreach (var directory in Directory.EnumerateDirectories(stagingRoot, DirectoryPrefix + "*", SearchOption.TopDirectoryOnly))
        {
            var info = new DirectoryInfo(directory);
            if (!IsStagingName(info.Name) || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LastWriteTimeUtc >= cutoff)
            {
                continue;
            }

            try
            {
                var owner = System.IO.Path.Combine(directory, OwnerFileName);
                if (File.Exists(owner))
                {
                    // Opened exclusively only when no live build holds it; a held one throws, and the directory is left.
                    using (new FileStream(owner, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }

                var bytes = info.EnumerateFiles("*", SearchOption.AllDirectories).Sum(static file => file.Length);
                Directory.Delete(directory, recursive: true);
                swept.Add((directory, bytes));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // In use, or not removable by this process: left for its owner, or for a later sweep.
            }
        }

        return swept;
    }

    private static bool IsStagingName(string name) =>
        name.Length == DirectoryPrefix.Length + 32 &&
        name.StartsWith(DirectoryPrefix, StringComparison.Ordinal) &&
        name.AsSpan(DirectoryPrefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;
}
