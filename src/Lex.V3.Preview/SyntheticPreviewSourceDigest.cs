using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Preview;

public static class SyntheticPreviewSourceDigest
{
    private const string Domain = "lex-v3-s0-05-preview-source-set";
    private const int BufferBytes = 81_920;
    private const int MaximumMembers = 256;
    private const long MaximumMemberBytes = 1_048_576;
    private const long MaximumSourceSetBytes = 8_388_608;
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

        var paths = new List<string>(MaximumMembers);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(path);
            if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(extension, ".cs", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Preview C# source extensions must use canonical casing.");
                }

                if (paths.Count >= MaximumMembers - 2)
                {
                    throw new InvalidDataException("Preview source set contains too many members.");
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
        long sourceSetBytes = 0;
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
                BufferBytes,
                FileOptions.SequentialScan);
            var bytes = CaptureBoundedMember(stream, MaximumSourceSetBytes - sourceSetBytes);
            sourceSetBytes += bytes.LongLength;
            var canonicalLength = MeasureCanonicalLengthAndValidateUtf8(bytes);
            AppendLength(hash, canonicalLength);
            AppendCanonicalBytes(hash, bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static byte[] CaptureBoundedMember(FileStream stream, long remainingSourceSetBytes)
    {
        var length = stream.Length;
        if (length > MaximumMemberBytes || length > remainingSourceSetBytes)
        {
            throw new InvalidDataException("Preview source bytes exceed their bound.");
        }

        var bytes = new byte[checked((int)length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                throw new InvalidDataException("Preview source changed while it was captured.");
            }

            offset += read;
        }

        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("Preview source changed while it was captured.");
        }

        return bytes;
    }

    private static long MeasureCanonicalLengthAndValidateUtf8(ReadOnlySpan<byte> bytes)
    {
        _ = StrictUtf8.GetCharCount(bytes);
        long canonicalLength = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            canonicalLength++;
            if (bytes[index] == (byte)'\r' &&
                index + 1 < bytes.Length &&
                bytes[index + 1] == (byte)'\n')
            {
                index++;
            }
        }

        return canonicalLength;
    }

    private static void AppendCanonicalBytes(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        var output = new byte[BufferBytes];
        var written = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            if (value == (byte)'\r' &&
                index + 1 < bytes.Length &&
                bytes[index + 1] == (byte)'\n')
            {
                value = (byte)'\n';
                index++;
            }

            output[written++] = value;
            if (written == output.Length)
            {
                hash.AppendData(output);
                written = 0;
            }
        }

        hash.AppendData(output.AsSpan(0, written));
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
