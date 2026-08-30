using System.Security.Cryptography;
using Lex.Law;
using Lex.Temporal;

namespace Lex.Ingest;

internal static class EvidenceFiles
{
    public static string RequireRoot(string path, string description)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException($"{description} must be absolute.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(full))
            throw new InvalidDataException($"{description} does not exist.");
        ProtectedPath.RequireExisting(full, full, description);
        return full;
    }

    public static string RequireEntry(
        string root, string path, string description) =>
        ProtectedPath.RequireExisting(root, path, description);

    public static void RequireRootIdentity(
        string path, HandleBoundRoot root, string description)
    {
        using var current = HandleBoundRename.OpenRoot(path);
        if (current.StableIdentity != root.StableIdentity)
            throw new InvalidDataException(
                $"{description} no longer names the handle-bound root.");
    }

    public static FileStream CreateOwnerLock(string root)
    {
        try
        {
            var stream = new FileStream(
                Path.Combine(root, PrivateEvidenceBundle.OwnerLockFileName),
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                });
            stream.Flush(flushToDisk: true);
            return stream;
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "Private evidence staging root cannot acquire exclusive ownership.",
                error);
        }
    }

    public static FileStream OpenOwnerLock(string root)
    {
        var path = RequireEntry(
            root,
            Path.Combine(root, PrivateEvidenceBundle.OwnerLockFileName),
            "Private evidence owner lock");
        try
        {
            return new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
            });
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "Private evidence staging root is already owned.", error);
        }
    }

    public static string ObjectRelative(string sha256) =>
        PrivateEvidenceBundle.ObjectsDirectoryName + "/" +
        CodeIdentity.RequireSha256(
            sha256, "Private evidence object SHA-256") + ".bin";

    public static string ResponseReceiptRelative(string requestId) =>
        PrivateEvidenceBundle.ReceiptsDirectoryName + "/" +
        CodeIdentity.RequireSha256(
            requestId, "Private evidence request ID") + ".json";

    public static string AttemptStartRelative(RecordedSourceRequest request) =>
        PrivateEvidenceBundle.AttemptsDirectoryName + "/" +
        AttemptPrefix(request) + ".start.json";

    public static string AttemptTerminalRelative(RecordedSourceRequest request) =>
        PrivateEvidenceBundle.AttemptsDirectoryName + "/" +
        AttemptPrefix(request) + ".terminal.json";

    public static string CaptureIntentRelative(string requestId) =>
        PendingRelative(requestId, ".intent.json");

    public static string CaptureBodyRelative(string requestId) =>
        PendingRelative(requestId, ".body");

    public static string CaptureOutcomeRelative(string requestId) =>
        PendingRelative(requestId, ".outcome.json");

    public static byte[] ReadBounded(
        HandleBoundRoot root,
        string relative,
        int maximumBytes,
        string description)
    {
        using var stream = root.OpenRead(relative);
        var length = stream.Length;
        if (length < 1 || length > maximumBytes)
            throw new InvalidDataException(
                $"{description} length is outside its allowed bound.");
        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException($"{description} changed while read.");
        return bytes;
    }

    public static void VerifyObject(
        HandleBoundRoot root,
        string relative,
        string expectedSha256,
        long expectedLength)
    {
        using var stream = root.OpenRead(relative);
        if (stream.Length != expectedLength
            || Convert.ToHexStringLower(SHA256.HashData(stream))
            != expectedSha256)
            throw new InvalidDataException(
                "Private evidence object length or SHA-256 changed.");
    }

    public static void WriteAtomic(
        HandleBoundRoot root,
        string finalRelative,
        byte[] bytes,
        bool replace = false)
    {
        var parent = NormalizeParent(finalRelative);
        var tempName = $".{Path.GetFileName(finalRelative)}-{Guid.NewGuid():N}.tmp";
        var temp = parent == "." ? tempName : parent + "/" + tempName;
        try
        {
            using (var stream = root.CreateNewFile(temp))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            root.Move(temp, finalRelative, replace);
        }
        catch
        {
            TryDelete(root, temp);
            throw;
        }
    }

    public static void VerifyExactLayout(
        string rootPath,
        HandleBoundRoot root,
        IReadOnlyCollection<PrivateEvidenceAttemptState> attempts,
        IReadOnlyCollection<StagedResponseRecord> records,
        bool includeManifest,
        bool includeCommit)
    {
        var expectedRoot = new HashSet<string>(StringComparer.Ordinal)
        {
            "D:" + PrivateEvidenceBundle.AttemptsDirectoryName,
            "D:" + PrivateEvidenceBundle.ObjectsDirectoryName,
            "D:" + PrivateEvidenceBundle.PendingDirectoryName,
            "D:" + PrivateEvidenceBundle.ReceiptsDirectoryName,
            "F:" + PrivateEvidenceBundle.OwnerLockFileName,
            "F:" + PrivateEvidenceBundle.PlanFileName,
            "F:" + PrivateEvidenceBundle.AttemptHeadFileName,
        };
        if (includeManifest)
            expectedRoot.Add("F:" + PrivateEvidenceBundle.ManifestFileName);
        if (includeCommit)
            expectedRoot.Add("F:" + PrivateEvidenceBundle.CommitMarkerFileName);
        if (!EntrySet(rootPath, root, ".").SetEquals(expectedRoot))
            throw new InvalidDataException(
                "Private evidence root does not have the exact required file set.");

        var expectedObjects = records.Select(record =>
                "F:" + record.Evidence.ObjectSha256 + ".bin")
            .ToHashSet(StringComparer.Ordinal);
        if (!EntrySet(rootPath, root, PrivateEvidenceBundle.ObjectsDirectoryName)
            .SetEquals(expectedObjects))
            throw new InvalidDataException(
                "Private evidence objects do not exactly match the receipts.");

        var expectedAttempts = attempts.SelectMany(attempt =>
            attempt.Terminal is null
                ? new[] { "F:" + AttemptFileName(attempt.Request, ".start.json") }
                :
                [
                    "F:" + AttemptFileName(attempt.Request, ".start.json"),
                    "F:" + AttemptFileName(attempt.Request, ".terminal.json"),
                ]).ToHashSet(StringComparer.Ordinal);
        if (!EntrySet(rootPath, root, PrivateEvidenceBundle.AttemptsDirectoryName)
            .SetEquals(expectedAttempts))
            throw new InvalidDataException(
                "Private evidence attempt files do not exactly match the ledger.");

        var expectedReceipts = records.Select(record =>
                "F:" + record.Request.RequestId + ".json")
            .ToHashSet(StringComparer.Ordinal);
        if (!EntrySet(rootPath, root, PrivateEvidenceBundle.ReceiptsDirectoryName)
            .SetEquals(expectedReceipts))
            throw new InvalidDataException(
                "Private evidence receipts do not exactly match the inventory.");

        if (EntrySet(rootPath, root, PrivateEvidenceBundle.PendingDirectoryName)
            .Count != 0)
            throw new InvalidDataException(
                "Private evidence pending directory is not empty.");
    }

    public static void TryDelete(HandleBoundRoot root, string relative)
    {
        try
        {
            if (root.EntryExists(relative)) root.DeleteFile(relative);
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException)
        {
        }
    }

    public static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private static HashSet<string> EntrySet(
        string rootPath, HandleBoundRoot root, string directory)
    {
        RequireRootIdentity(rootPath, root, "Private evidence staging root");
        if (directory != "."
            && !root.Exists(directory, expectDirectory: true))
            throw new InvalidDataException(
                "Private evidence directory is missing.");
        var absolute = directory == "."
            ? rootPath
            : Path.Combine(rootPath, directory);
        var entries = Directory.EnumerateFileSystemEntries(absolute)
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var relative = Relative(directory, name);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "Private evidence contains a link or reparse point.");
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var isHeldOwnerLock = directory == "."
                                      && name
                                      == PrivateEvidenceBundle.OwnerLockFileName;
                if (isHeldOwnerLock && isDirectory)
                    throw new InvalidDataException(
                        "Private evidence owner lock is not a file.");
                if (!isHeldOwnerLock && !root.Exists(relative, isDirectory))
                    throw new InvalidDataException(
                        "Private evidence entry disappeared during verification.");
                return (isDirectory ? "D:" : "F:") + name;
            })
            .ToHashSet(StringComparer.Ordinal);
        RequireRootIdentity(rootPath, root, "Private evidence staging root");
        return entries;
    }

    private static string PendingRelative(string requestId, string suffix) =>
        PrivateEvidenceBundle.PendingDirectoryName + "/" +
        CodeIdentity.RequireSha256(
            requestId, "Private evidence request ID") + suffix;

    private static string AttemptPrefix(RecordedSourceRequest request) =>
        request.Ordinal.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)
        + "-" + request.RequestId;

    private static string AttemptFileName(
        RecordedSourceRequest request, string suffix) =>
        AttemptPrefix(request) + suffix;

    private static string NormalizeParent(string relative)
    {
        var parent = Path.GetDirectoryName(relative);
        return string.IsNullOrEmpty(parent)
            ? "."
            : parent.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string Relative(string directory, string name) =>
        directory == "." ? name : directory + "/" + name;
}
