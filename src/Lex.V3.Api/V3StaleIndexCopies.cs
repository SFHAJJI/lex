namespace Lex.V3.Api;

/// <summary>
/// The private index copies an earlier process left in a temporary folder that belongs to this process alone. Mounting
/// copies each index there before verifying it, and the copies are removed when the mount is disposed; a process that is
/// ended leaves them. A container's <c>/tmp</c> outlives a restart of its process (on Azure Container Apps an EmptyDir
/// lives as long as the replica), and the population mount's copies are 2.15 GB a start against the 4 GiB of ephemeral
/// storage one vCPU has, so a restart would leave too little room to mount again (the review of #949). The deployment says
/// the folder is the process's own (<see cref="PrivateTemporaryFolderVariable"/>); nowhere else is anything removed, since
/// a folder other processes share can hold their copies.
/// </summary>
internal static class V3StaleIndexCopies
{
    /// <summary>The variable a deployment sets to <c>1</c> when the process's temporary folder is its own.</summary>
    internal const string PrivateTemporaryFolderVariable = "LEX_V3_TEMP_IS_PRIVATE";

    /// <summary>The names the Luxembourg and EU readers give their private copies (and anything SQLite keeps beside one).</summary>
    internal const string CopyPattern = "lex-v3-*-mount-*.sqlite*";

    /// <summary>Removes the copies in <paramref name="directory"/>, and answers how many; one that cannot be removed is left.</summary>
    internal static int Remove(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var copy in Directory.EnumerateFiles(directory, CopyPattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(copy);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // In use or not ours to remove: left as it is.
            }
        }

        return removed;
    }
}
