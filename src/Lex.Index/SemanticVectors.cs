using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Lex.Index;

public enum EmbeddingInputKind { Query, Passage }

public interface ITextEncoder : IDisposable
{
    string ModelId { get; }
    string ModelRevision { get; }
    int Dimensions { get; }
    int CountTokens(string text);
    int PrefixLengthForTokens(string text, int maxTokens);
    int SuffixStartForTokens(string text, int maxTokens);
    float[] Encode(string text, EmbeddingInputKind kind);
    IReadOnlyList<float[]> EncodeBatch(IReadOnlyList<string> texts, EmbeddingInputKind kind)
    {
        var vectors = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++) vectors[i] = Encode(texts[i], kind);
        return vectors;
    }
}

public sealed record SemanticChunk(int Index, string Text, string Sha256);

public static class SemanticChunker
{
    public const int MaxTokens = 256;
    public const int OverlapTokens = 32;

    public static IReadOnlyList<SemanticChunk> Split(string text, ITextEncoder encoder)
    {
        if (encoder.CountTokens("passage: " + text) <= MaxTokens)
            return [Chunk(0, text)];

        var paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pieces = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var rest = paragraph;
            while (encoder.CountTokens("passage: " + rest) > MaxTokens)
            {
                var take = Math.Max(1, encoder.PrefixLengthForTokens("passage: " + rest, MaxTokens) - "passage: ".Length);
                take = Math.Min(take, rest.Length);
                pieces.Add(rest[..take].Trim());
                var overlapStart = encoder.SuffixStartForTokens(rest[..take], OverlapTokens);
                rest = rest[overlapStart..].Trim();
            }
            if (rest.Length > 0) pieces.Add(rest);
        }

        var chunks = new List<SemanticChunk>();
        var current = new List<string>();
        foreach (var piece in pieces)
        {
            var candidate = string.Join("\n\n", current.Append(piece));
            if (current.Count > 0 && encoder.CountTokens("passage: " + candidate) > MaxTokens)
            {
                chunks.Add(Chunk(chunks.Count, string.Join("\n\n", current)));
                var overlap = current.AsEnumerable().Reverse().TakeWhileAccumulated(
                    p => encoder.CountTokens(p), OverlapTokens).Reverse().ToList();
                current = overlap.Count > 0
                    && encoder.CountTokens("passage: " + string.Join("\n\n", overlap.Append(piece))) <= MaxTokens
                    ? overlap : [];
            }
            current.Add(piece);
        }
        if (current.Count > 0) chunks.Add(Chunk(chunks.Count, string.Join("\n\n", current)));
        return chunks;
    }

    private static SemanticChunk Chunk(int index, string text) => new(index, text,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));

    private static IEnumerable<T> TakeWhileAccumulated<T>(this IEnumerable<T> values, Func<T, int> cost, int maximum)
    {
        var total = 0;
        foreach (var value in values)
        {
            var next = cost(value);
            if (total + next > maximum) yield break;
            total += next;
            yield return value;
        }
    }
}

public sealed class SemanticVectorWriter : IDisposable
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LEXVEC3\0");
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _dimensions;
    private long _count;
    private bool _disposed;

    public SemanticVectorWriter(string path, int dimensions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
        _dimensions = dimensions;
        _writer.Write(Magic);
        _writer.Write(1);
        _writer.Write(dimensions);
        _writer.Write((long)0);
        _writer.Write(BinaryBytes(dimensions) + dimensions);
        _writer.Write(0);
    }

    public long Write(float[] normalized)
    {
        if (normalized.Length != _dimensions) throw new InvalidDataException("Embedding dimension mismatch.");
        var ordinal = _count++;
        var binary = new byte[BinaryBytes(_dimensions)];
        for (var i = 0; i < normalized.Length; i++)
            if (normalized[i] >= 0) binary[i >> 3] |= (byte)(1 << (i & 7));
        _writer.Write(binary);
        for (var i = 0; i < normalized.Length; i++)
            _writer.Write((sbyte)Math.Clamp((int)Math.Round(normalized[i] * 127f), -127, 127));
        return ordinal;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Flush();
        _stream.Position = 16;
        _writer.Write(_count);
        _writer.Dispose();
        _stream.Dispose();
    }

    internal static int BinaryBytes(int dimensions) => (dimensions + 7) / 8;
}

public sealed class SemanticVectorReader : IDisposable
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly int _recordSize;
    private readonly int _binaryBytes;
    public int Dimensions { get; }
    public long Count { get; }

    public SemanticVectorReader(string path)
    {
        _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        var magic = new byte[8];
        _view.ReadArray(0, magic, 0, magic.Length);
        if (!magic.SequenceEqual(Encoding.ASCII.GetBytes("LEXVEC3\0")) || _view.ReadInt32(8) != 1)
            throw new InvalidDataException("Unsupported semantic vector format.");
        Dimensions = _view.ReadInt32(12);
        Count = _view.ReadInt64(16);
        _recordSize = _view.ReadInt32(24);
        _binaryBytes = SemanticVectorWriter.BinaryBytes(Dimensions);
        if (_recordSize != _binaryBytes + Dimensions || Count < 0
            || 32L + Count * _recordSize != new FileInfo(path).Length)
            throw new InvalidDataException("Semantic vector header does not match the file length.");
    }

    public int HammingDistance(long ordinal, byte[] queryBits)
    {
        ValidateOrdinal(ordinal);
        if (queryBits.Length != _binaryBytes) throw new ArgumentException("Query bit dimension mismatch.");
        var offset = 32L + ordinal * _recordSize;
        var distance = 0;
        var blockBytes = _binaryBytes - _binaryBytes % sizeof(long);
        var i = 0;
        for (; i < blockBytes; i += sizeof(long))
        {
            var stored = unchecked((ulong)_view.ReadInt64(offset + i));
            var wanted = BinaryPrimitives.ReadUInt64LittleEndian(queryBits.AsSpan(i, sizeof(long)));
            distance += BitOperations.PopCount(stored ^ wanted);
        }
        for (; i < _binaryBytes; i++)
            distance += BitOperations.PopCount((uint)(_view.ReadByte(offset + i) ^ queryBits[i]));
        return distance;
    }

    public int Int8Dot(long ordinal, sbyte[] query)
    {
        ValidateOrdinal(ordinal);
        if (query.Length != Dimensions) throw new ArgumentException("Query vector dimension mismatch.");
        var offset = 32L + ordinal * _recordSize + _binaryBytes;
        var score = 0;
        for (var i = 0; i < Dimensions; i++) score += query[i] * _view.ReadSByte(offset + i);
        return score;
    }

    public static byte[] Binary(float[] normalized)
    {
        var bits = new byte[SemanticVectorWriter.BinaryBytes(normalized.Length)];
        for (var i = 0; i < normalized.Length; i++)
            if (normalized[i] >= 0) bits[i >> 3] |= (byte)(1 << (i & 7));
        return bits;
    }

    public static sbyte[] Int8(float[] normalized) => normalized
        .Select(v => (sbyte)Math.Clamp((int)Math.Round(v * 127f), -127, 127)).ToArray();

    private void ValidateOrdinal(long ordinal)
    {
        if (ordinal < 0 || ordinal >= Count) throw new InvalidDataException("Semantic vector ordinal is outside the file.");
    }

    public void Dispose()
    {
        _view.Dispose();
        _file.Dispose();
    }
}

public sealed record SemanticBuildOptions(
    ITextEncoder Encoder,
    string VectorPath,
    string ModelSha256,
    string TokenizerSha256,
    string VectorFormat = "lex-vectors/1-binary-int8",
    Action<SemanticBuildProgress>? Progress = null,
    int BatchSize = 16);

public enum SemanticBuildStage
{
    Preparation,
    Embeddings,
}

public sealed record SemanticBuildProgress(
    long Completed,
    long Total,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining,
    SemanticBuildStage Stage = SemanticBuildStage.Embeddings)
{
    public double Percent => Total == 0 ? 100 : Completed * 100d / Total;
}
