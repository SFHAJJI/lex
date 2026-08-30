using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Preview;

public static class SyntheticPreviewSourceDigest
{
    private const string Domain = "lex-v3-s0-05-preview-source-set";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Compute(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var root = Path.GetFullPath(projectRoot);
        var rootAttributes = File.GetAttributes(root);
        if ((rootAttributes & FileAttributes.Directory) == 0 ||
            (rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Preview source root must be a real directory.");
        }

        var paths = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(path);
            if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(extension, ".cs", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Preview C# source extensions must use canonical casing.");
                }

                paths.Add(path);
            }
        }

        paths.Add(Path.Combine(root, "Lex.V3.Preview.csproj"));
        paths.Add(Path.Combine(root, "packages.lock.json"));
        var members = paths
            .Select(path => new SourceMember(Path.GetFileName(path), ValidateFile(path)))
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
        if (members.Select(member => member.Name).Distinct(StringComparer.Ordinal).Count() != members.Length)
        {
            throw new InvalidDataException("Preview source set contains a duplicate canonical name.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(Domain));
        hash.AppendData(stackalloc byte[] { 0 });
        AppendLength(hash, members.LongLength);
        foreach (var member in members)
        {
            var nameBytes = StrictUtf8.GetBytes(member.Name);
            AppendLength(hash, nameBytes.LongLength);
            hash.AppendData(nameBytes);
            using var stream = new FileStream(
                member.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.SequentialScan);
            AppendLength(hash, stream.Length);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string ValidateFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException("Preview source members must be real files.");
        }

        return path;
    }

    private static void AppendLength(IncrementalHash hash, long value)
    {
        Span<byte> frame = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(frame, value);
        hash.AppendData(frame);
    }

    private sealed record SourceMember(string Name, string Path);
}
