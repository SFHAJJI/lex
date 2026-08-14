namespace Lex.Ingest;

internal static class VerifiedCorpusPath
{
    public static string RequireExisting(string root, string candidate, string description)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullCandidate = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!string.Equals(fullCandidate, fullRoot, comparison)
            && !fullCandidate.StartsWith(rootPrefix, comparison))
            throw new InvalidDataException(
                $"Corpus {description} escapes its protected root: {candidate}");

        RequireNotLink(fullRoot, description);
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (relative == ".") return fullCandidate;

        var current = fullRoot;
        foreach (var component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            RequireNotLink(current, description);
        }
        return fullCandidate;
    }

    private static void RequireNotLink(string path, string description)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Corpus {description} cannot be verified: {path}", error);
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"Corpus {description} contains a reparse point or symbolic link: {path}");
    }
}
