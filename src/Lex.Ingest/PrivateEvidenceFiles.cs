using System.Security.Cryptography;
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

    public static void RequireDirectory(
        string root, string path, string description)
    {
        var verified = RequireEntry(root, path, description);
        if (!Directory.Exists(verified) || File.Exists(verified))
            throw new InvalidDataException($"{description} is not a directory.");
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

    public static string ObjectPath(string root, string sha256) => Path.Combine(
        root,
        PrivateEvidenceBundle.ObjectsDirectoryName,
        CodeIdentity.RequireSha256(
            sha256, "Private evidence object SHA-256") + ".bin");

    public static string ResponseReceiptPath(string root, string requestId) =>
        Path.Combine(
            root,
            PrivateEvidenceBundle.ReceiptsDirectoryName,
            CodeIdentity.RequireSha256(
                requestId, "Private evidence request ID") + ".json");

    public static byte[] ReadBounded(
        string root, string path, int maximumBytes, string description)
    {
        var verified = RequireEntry(root, path, description);
        if (!File.Exists(verified) || Directory.Exists(verified))
            throw new InvalidDataException($"{description} is not a regular file.");
        var length = new FileInfo(verified).Length;
        if (length < 1 || length > maximumBytes)
            throw new InvalidDataException(
                $"{description} length is outside its allowed bound.");
        using var stream = new FileStream(verified, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.SequentialScan,
        });
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException($"{description} changed while read.");
        return bytes;
    }

    public static void VerifyObject(
        string root,
        string path,
        string expectedSha256,
        long expectedLength)
    {
        var verified = RequireEntry(root, path, "Private evidence object");
        if (!File.Exists(verified) || Directory.Exists(verified))
            throw new InvalidDataException(
                "Private evidence object is not a regular file.");
        using var stream = new FileStream(verified, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.SequentialScan,
        });
        if (stream.Length != expectedLength
            || Convert.ToHexStringLower(SHA256.HashData(stream))
            != expectedSha256)
            throw new InvalidDataException(
                "Private evidence object length or SHA-256 changed.");
    }

    public static void WriteAtomic(string finalPath, byte[] bytes)
    {
        var temp = Path.Combine(
            Path.GetDirectoryName(finalPath)!,
            $".{Path.GetFileName(finalPath)}-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.WriteThrough,
            }))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, finalPath, overwrite: false);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public static void CleanupTemporaryFiles(string root)
    {
        foreach (var directory in new[]
                 {
                     root,
                     Path.Combine(root, PrivateEvidenceBundle.ObjectsDirectoryName),
                     Path.Combine(root, PrivateEvidenceBundle.ReceiptsDirectoryName),
                 })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(
                         directory, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                var allowed = directory == root
                    ? name.StartsWith($".{PrivateEvidenceBundle.PlanFileName}-",
                          StringComparison.Ordinal)
                      || name.StartsWith(
                          $".{PrivateEvidenceBundle.ManifestFileName}-",
                          StringComparison.Ordinal)
                      || name.StartsWith(
                          $".{PrivateEvidenceBundle.CommitMarkerFileName}-",
                          StringComparison.Ordinal)
                    : directory.EndsWith(
                        PrivateEvidenceBundle.ObjectsDirectoryName,
                        StringComparison.Ordinal)
                        ? name.StartsWith(".capture-", StringComparison.Ordinal)
                        : name.StartsWith(".", StringComparison.Ordinal);
                if (allowed && name.EndsWith(".tmp", StringComparison.Ordinal))
                    File.Delete(RequireEntry(
                        root, path, "Private evidence temporary file"));
            }
        }
    }

    public static void DeleteOrphanObjects(
        string root, IReadOnlyCollection<StagedResponseRecord> records)
    {
        var objectsRoot = Path.Combine(root, PrivateEvidenceBundle.ObjectsDirectoryName);
        RequireDirectory(root, objectsRoot, "Private evidence objects directory");
        var expected = records.Select(record =>
                record.Evidence.ObjectSha256 + ".bin")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(objectsRoot))
        {
            var verified = RequireEntry(root, path, "Private evidence object");
            if (!File.Exists(verified) || Directory.Exists(verified))
                throw new InvalidDataException(
                    "Private evidence objects directory contains a non-file entry.");
            var name = Path.GetFileName(verified);
            if (name.Length != 68 || !name.EndsWith(".bin", StringComparison.Ordinal)
                || !IsSha256(name[..64]))
                throw new InvalidDataException(
                    "Private evidence object has an invalid name.");
            if (!expected.Contains(name)) File.Delete(verified);
        }
    }

    public static void VerifyExactLayout(
        string root,
        IReadOnlyCollection<StagedResponseRecord> records,
        bool includeManifest,
        bool includeCommit)
    {
        var expectedRoot = new HashSet<string>(StringComparer.Ordinal)
        {
            "D:" + PrivateEvidenceBundle.ObjectsDirectoryName,
            "D:" + PrivateEvidenceBundle.ReceiptsDirectoryName,
            "F:" + PrivateEvidenceBundle.OwnerLockFileName,
            "F:" + PrivateEvidenceBundle.PlanFileName,
        };
        if (includeManifest)
            expectedRoot.Add("F:" + PrivateEvidenceBundle.ManifestFileName);
        if (includeCommit)
            expectedRoot.Add("F:" + PrivateEvidenceBundle.CommitMarkerFileName);
        if (!EntrySet(root, root).SetEquals(expectedRoot))
            throw new InvalidDataException(
                "Private evidence root does not have the exact required file set.");

        var expectedObjects = records.Select(record =>
                "F:" + record.Evidence.ObjectSha256 + ".bin")
            .ToHashSet(StringComparer.Ordinal);
        if (!EntrySet(
                root,
                Path.Combine(root, PrivateEvidenceBundle.ObjectsDirectoryName))
            .SetEquals(expectedObjects))
            throw new InvalidDataException(
                "Private evidence objects do not exactly match the receipts.");

        var expectedReceipts = records.Select(record =>
                "F:" + record.Request.RequestId + ".json")
            .ToHashSet(StringComparer.Ordinal);
        if (!EntrySet(
                root,
                Path.Combine(root, PrivateEvidenceBundle.ReceiptsDirectoryName))
            .SetEquals(expectedReceipts))
            throw new InvalidDataException(
                "Private evidence receipts do not exactly match the inventory.");
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
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

    private static HashSet<string> EntrySet(string root, string directory)
    {
        RequireDirectory(root, directory, "Private evidence directory");
        return Directory.EnumerateFileSystemEntries(directory)
            .Select(path =>
            {
                var verified = RequireEntry(root, path, "Private evidence entry");
                return Directory.Exists(verified)
                    ? "D:" + Path.GetFileName(verified)
                    : File.Exists(verified)
                        ? "F:" + Path.GetFileName(verified)
                        : throw new InvalidDataException(
                            "Private evidence contains an unsupported entry.");
            })
            .ToHashSet(StringComparer.Ordinal);
    }
}
