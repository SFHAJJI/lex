namespace Lex.V3.Api;

internal static class SyntheticImmutableCustody
{
    public static void AssertReadOnly(string graphRoot, string sqlitePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlitePath);
        var root = Path.GetFullPath(graphRoot);
        var index = Path.GetFullPath(sqlitePath);
        var rootPrefix = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!index.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The SQLite member is outside the synthetic graph root.");
        }

        if (CanOpenForWrite(index))
        {
            throw new SyntheticImmutableCustodyException(
                "The admitted SQLite member is writable by the runtime user.");
        }

        var probe = Path.Combine(root, $".custody-probe-{Guid.NewGuid():N}");
        if (CanCreate(probe))
        {
            File.Delete(probe);
            throw new SyntheticImmutableCustodyException(
                "The synthetic graph directory is writable by the runtime user.");
        }
    }

    private static bool CanOpenForWrite(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool CanCreate(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

internal sealed class SyntheticImmutableCustodyException(string message) : Exception(message);
