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
        if (members.Length > MaximumMembers)
        {
            throw new InvalidDataException("Preview source set contains too many members.");
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
            if (stream.Length > MaximumMemberBytes ||
                sourceSetBytes > MaximumSourceSetBytes - stream.Length)
            {
                throw new InvalidDataException("Preview source bytes exceed their bound.");
            }

            sourceSetBytes += stream.Length;
            var canonicalLength = MeasureCanonicalLengthAndValidateUtf8(stream);
            AppendLength(hash, canonicalLength);
            stream.Position = 0;
            AppendCanonicalBytes(hash, stream);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static long MeasureCanonicalLengthAndValidateUtf8(Stream stream)
    {
        var decoder = StrictUtf8.GetDecoder();
        var bytes = new byte[BufferBytes];
        var characters = new char[BufferBytes];
        long canonicalLength = 0;
        var pendingCarriageReturn = false;
        int read;
        while ((read = stream.Read(bytes, 0, bytes.Length)) != 0)
        {
            var offset = 0;
            while (offset < read)
            {
                decoder.Convert(
                    bytes.AsSpan(offset, read - offset),
                    characters,
                    flush: false,
                    out var bytesUsed,
                    out _,
                    out _);
                offset += bytesUsed;
            }

            for (var index = 0; index < read; index++)
            {
                var value = bytes[index];
                if (pendingCarriageReturn)
                {
                    canonicalLength++;
                    pendingCarriageReturn = false;
                    if (value == (byte)'\n')
                    {
                        continue;
                    }
                }

                if (value == (byte)'\r')
                {
                    pendingCarriageReturn = true;
                }
                else
                {
                    canonicalLength++;
                }
            }
        }

        decoder.Convert([], characters, flush: true, out _, out _, out var completed);
        if (!completed)
        {
            throw new InvalidDataException("Preview source UTF-8 validation did not complete.");
        }
        if (pendingCarriageReturn)
        {
            canonicalLength++;
        }

        return canonicalLength;
    }

    private static void AppendCanonicalBytes(IncrementalHash hash, Stream stream)
    {
        var input = new byte[BufferBytes];
        var output = new byte[BufferBytes + 1];
        var pendingCarriageReturn = false;
        int read;
        while ((read = stream.Read(input, 0, input.Length)) != 0)
        {
            var written = 0;
            for (var index = 0; index < read; index++)
            {
                var value = input[index];
                if (pendingCarriageReturn)
                {
                    pendingCarriageReturn = false;
                    output[written++] = value == (byte)'\n' ? (byte)'\n' : (byte)'\r';
                    if (value == (byte)'\n')
                    {
                        continue;
                    }
                }

                if (value == (byte)'\r')
                {
                    pendingCarriageReturn = true;
                }
                else
                {
                    output[written++] = value;
                }
            }

            hash.AppendData(output.AsSpan(0, written));
        }

        if (pendingCarriageReturn)
        {
            hash.AppendData(stackalloc byte[] { (byte)'\r' });
        }
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
