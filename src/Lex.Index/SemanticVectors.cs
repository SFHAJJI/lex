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
    IReadOnlyList<float[]> EncodeBatch(
        IReadOnlyList<string> texts, EmbeddingInputKind kind, int? padToTokens = null)
    {
        var vectors = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++) vectors[i] = Encode(texts[i], kind);
        return vectors;
    }
}

public sealed record SemanticChunk(int Index, string Text, string Sha256, int TokenCount);

public static class SemanticChunker
{
    public const int MaxTokens = 256;
    public const int OverlapTokens = 32;
    private const string PassagePrefix = "passage: ";
    private const int InitialProbeCharacters = 4_096;

    public static IReadOnlyList<SemanticChunk> Split(string text, ITextEncoder encoder)
    {
        // Never materialize `prefix + the entire remaining suffix`. A 46 MB regulatory table
        // exposed that even a token-count API which stops at 256 tokens cannot save the caller
        // from first copying the other 45.9 MB. Probe a bounded window and grow it only when the
        // whole window fits. For ordinary provisions this is byte-for-byte the old decision; for
        // long annexes the tokenizer never receives the already-scanned suffix again.
        var first = BoundedPrefix(text, 0, encoder);
        if (first.FitsRemaining)
            return [Chunk(0, text, encoder)];

        var paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pieces = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var start = 0;
            while (start < paragraph.Length)
            {
                var bounded = BoundedPrefix(paragraph, start, encoder);
                var take = bounded.Take;
                if (bounded.FitsRemaining)
                {
                    pieces.Add(paragraph[start..]);
                    break;
                }
                var emitted = paragraph.Substring(start, take);
                pieces.Add(emitted.Trim());
                var overlapStart = encoder.SuffixStartForTokens(emitted, OverlapTokens);
                // Some SentencePiece fragments are shorter than the requested overlap even when
                // PrefixLengthForTokens stopped before the end of the normalized input. Its
                // suffix boundary is then zero. Reusing the whole prefix would leave `rest`
                // unchanged forever; the Fleet heartbeat exposed that exact loop on CRR Art. 261.
                // The complete prefix has already been emitted, so advance past it when no proper
                // suffix exists. This sacrifices overlap for that one boundary, never source text.
                var nextStart = overlapStart > 0 ? overlapStart : take;
                if (nextStart <= 0 || nextStart > take)
                    throw new InvalidDataException(
                        $"Semantic chunk preparation made no progress: remaining={paragraph.Length - start}, " +
                        $"take={take}, overlap_start={overlapStart}.");
                start += nextStart;
                while (start < paragraph.Length && char.IsWhiteSpace(paragraph[start])) start++;
            }
        }

        var chunks = new List<SemanticChunk>();
        var current = new List<string>();
        foreach (var piece in pieces)
        {
            var candidate = string.Join("\n\n", current.Append(piece));
            if (current.Count > 0 && encoder.CountTokens(PassagePrefix + candidate) > MaxTokens)
            {
                chunks.Add(Chunk(chunks.Count, string.Join("\n\n", current), encoder));
                var overlap = current.AsEnumerable().Reverse().TakeWhileAccumulated(
                    p => encoder.CountTokens(p), OverlapTokens).Reverse().ToList();
                current = overlap.Count > 0
                    && encoder.CountTokens(PassagePrefix + string.Join("\n\n", overlap.Append(piece))) <= MaxTokens
                    ? overlap : [];
            }
            current.Add(piece);
        }
        if (current.Count > 0) chunks.Add(Chunk(chunks.Count, string.Join("\n\n", current), encoder));
        return chunks;
    }

    private static (int Take, bool FitsRemaining) BoundedPrefix(
        string text, int start, ITextEncoder encoder)
    {
        var remaining = text.Length - start;
        if (remaining == 0)
            return (Take: 0, FitsRemaining: true);
        var windowLength = Math.Min(remaining, InitialProbeCharacters);
        while (true)
        {
            var probe = string.Concat(PassagePrefix.AsSpan(), text.AsSpan(start, windowLength));
            var boundary = encoder.PrefixLengthForTokens(probe, MaxTokens);
            var take = Math.Clamp(boundary - PassagePrefix.Length, 1, windowLength);
            if (take < windowLength)
                return (take, FitsRemaining: false);
            if (windowLength == remaining)
                return (remaining, FitsRemaining: true);
            windowLength = Math.Min(remaining, checked(windowLength * 2));
        }
    }

    private static SemanticChunk Chunk(int index, string text, ITextEncoder encoder) => new(
        index,
        text,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
        encoder.CountTokens(PassagePrefix + text));

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
        return WriteRecord(Quantize(normalized));
    }

    public long WriteRecord(ReadOnlySpan<byte> record)
    {
        if (record.Length != RecordBytes(_dimensions))
            throw new InvalidDataException("Quantized embedding record dimension mismatch.");
        var ordinal = _count++;
        _writer.Write(record);
        return ordinal;
    }

    public static byte[] Quantize(float[] normalized)
    {
        var binaryBytes = BinaryBytes(normalized.Length);
        var record = new byte[RecordBytes(normalized.Length)];
        for (var i = 0; i < normalized.Length; i++)
            if (normalized[i] >= 0) record[i >> 3] |= (byte)(1 << (i & 7));
        for (var i = 0; i < normalized.Length; i++)
            record[binaryBytes + i] = unchecked((byte)(sbyte)Math.Clamp(
                (int)Math.Round(normalized[i] * 127f), -127, 127));
        return record;
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
    public static int RecordBytes(int dimensions) => BinaryBytes(dimensions) + dimensions;
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

    /// <summary>
    /// Scans the compact binary prefix in physical vector order and retains only the closest
    /// candidates. Legal/date filters are applied to this bounded set by the index reader.
    /// Keeping the scan here avoids the previous cold path, which joined every semantic mapping,
    /// provision occurrence and document before it knew which vectors were remotely relevant.
    /// </summary>
    public IReadOnlyList<(long Ordinal, int Distance)> NearestByHamming(
        byte[] queryBits, int limit) => NearestByHamming(queryBits, limit, 0, Count);

    public IReadOnlyList<(long Ordinal, int Distance)> NearestByHamming(
        byte[] queryBits, int limit, long startOrdinal, long count)
    {
        if (queryBits.Length != _binaryBytes) throw new ArgumentException("Query bit dimension mismatch.");
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (startOrdinal < 0 || count < 0 || startOrdinal > Count - count)
            throw new InvalidDataException("Semantic vector scan range is outside the file.");
        var queue = new PriorityQueue<(long Ordinal, int Distance), long>();
        for (var ordinal = startOrdinal; ordinal < startOrdinal + count; ordinal++)
        {
            var distance = HammingDistance(ordinal, queryBits);
            // PriorityQueue removes its smallest priority. Negating distance and ordinal makes
            // the worst retained candidate leave first and keeps ties deterministic.
            var priority = -(((long)distance << 32) | (uint)ordinal);
            queue.Enqueue((ordinal, distance), priority);
            if (queue.Count > limit) queue.Dequeue();
        }
        return queue.UnorderedItems.Select(item => item.Element)
            .OrderBy(item => item.Distance).ThenBy(item => item.Ordinal).ToList();
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
    int BatchSize = 16,
    int MaxBatchTokens = 32_768,
    TimeSpan? ProgressHeartbeatInterval = null,
    string ExecutionProvider = "cpu",
    string? EmbeddingCachePath = null,
    string EmbeddingProfile = "lex-embedding-profile/2-fixed-token-buckets");

public enum SemanticBuildStage
{
    Preparation,
    Embeddings,
    WorkEmbeddings,
    Database,
    Finalization,
}

public sealed record SemanticBuildProgress(
    long Completed,
    long Total,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining,
    SemanticBuildStage Stage = SemanticBuildStage.Embeddings,
    string? CurrentItem = null,
    long? CurrentItemCharacters = null,
    TimeSpan? CurrentItemElapsed = null,
    bool IsHeartbeat = false)
{
    public double Percent => Total == 0 ? 100 : Completed * 100d / Total;
}
