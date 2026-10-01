using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using System.Threading.Channels;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest;

/// <summary>
/// Storage for derived canonical byte sequences larger than one custody object. A root receipt
/// names only the root: callers must retain the individual chunk receipts when assessing retention.
/// Opening verifies the complete ordered content; every subsequent chunk load rechecks custody.
/// This layer proves bytes and storage closure, not the meaning of a scope or corpus document.
/// </summary>
internal sealed class ChunkedDerivedArtifact
{
    internal const string Schema = "lex-v3-derived-byte-sequence/1";
    internal const int ChunkSize = 4 * 1024 * 1024;
    internal const string SmallChunkSchema = "lex-v3-derived-byte-sequence/2";
    internal const int SmallChunkSize = 64 * 1024;
    private readonly int _chunkSize;
    private readonly ICustodyStore _store;
    private readonly Chunk[] _chunks;
    private readonly CancellationToken _cancellationToken;

    private ChunkedDerivedArtifact(ICustodyStore store, string kind, string canonicalSha256,
        string contentSha256, long byteLength, int chunkSize, Chunk[] chunks,
        IReadOnlyList<DurableBlobWriteReceipt> chunkReceipts, CancellationToken cancellationToken)
    {
        _store = store;
        Kind = kind;
        CanonicalSha256 = canonicalSha256;
        ContentSha256 = contentSha256;
        ByteLength = byteLength;
        _chunkSize = chunkSize;
        _chunks = chunks;
        ChunkReceipts = Array.AsReadOnly(chunkReceipts.ToArray());
        _cancellationToken = cancellationToken;
    }

    internal IReadOnlyList<DurableBlobWriteReceipt> ChunkReceipts { get; }
    internal string Kind { get; }
    internal string CanonicalSha256 { get; }
    internal string ContentSha256 { get; }
    internal long ByteLength { get; }
    internal Stream OpenRead() => new ChunkReadStream(this);

    internal static long MeasureCanonicalBytes(Func<Stream, string> writeCanonical, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var counter = new CountingWriteStream(cancellationToken);
        _ = writeCanonical(counter);
        return counter.Length;
    }

    internal static Task<(DurableBlobWriteReceipt RootReceipt,
        IReadOnlyList<DurableBlobWriteReceipt> ChunkReceipts)> WriteAsync(
        ICustodyStore store, string kind, Func<Stream, string> writeCanonical,
        CancellationToken cancellationToken, Action<DurableBlobWriteReceipt>? observeReceipt = null) =>
        WriteCoreAsync(store, kind, writeCanonical, Schema, ChunkSize, cancellationToken, observeReceipt);

    /// <summary>Small chunks bound the custody bytes loaded by a short random lookup. The v2
    /// root uses exactly 64 KiB chunks; v1 keeps its existing 4 MiB representation unchanged.
    /// Opening still verifies the whole sequence before any seekable reader is returned.</summary>
    internal static Task<(DurableBlobWriteReceipt RootReceipt,
        IReadOnlyList<DurableBlobWriteReceipt> ChunkReceipts)> WriteSmallChunksAsync(
        ICustodyStore store, string kind, Func<Stream, string> writeCanonical,
        CancellationToken cancellationToken, Action<DurableBlobWriteReceipt>? observeReceipt = null) =>
        WriteCoreAsync(store, kind, writeCanonical, SmallChunkSchema, SmallChunkSize,
            cancellationToken, observeReceipt);

    private static async Task<(DurableBlobWriteReceipt RootReceipt,
        IReadOnlyList<DurableBlobWriteReceipt> ChunkReceipts)> WriteCoreAsync(
        ICustodyStore store, string kind, Func<Stream, string> writeCanonical,
        string schema, int chunkSize, CancellationToken cancellationToken,
        Action<DurableBlobWriteReceipt>? observeReceipt)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(writeCanonical);
        cancellationToken.ThrowIfCancellationRequested();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(2)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait,
        });
        // The synchronous canonical writer blocks only this worker when the bounded channel fills.
        // Custody calls stay asynchronous on the consuming path; no synchronization context is blocked.
        var producer = Task.Run(() =>
        {
            try
            {
                using var output = new ChunkWriteStream(channel.Writer, chunkSize, stop.Token);
                var digest = writeCanonical(output);
                output.Complete();
                channel.Writer.TryComplete();
                return digest;
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
                throw;
            }
        }, CancellationToken.None);

        var receipts = new List<DurableBlobWriteReceipt>();
        var chunks = new List<Chunk>();
        using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long byteLength = 0;
        try
        {
            await foreach (var bytes in channel.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                var (receipt, failure) = await CustodyHold.TryHoldAsync(store, bytes, stop.Token)
                    .ConfigureAwait(false);
                if (receipt is null) throw new CustodyRequiredException("Derived chunk was not held: " + failure);
                var digest = CustodyDigest.Of(bytes.Span, stop.Token);
                if (receipt.Reference.ContentSha256 != digest || receipt.Reference.ByteLength != bytes.Length)
                    throw new CustodyIntegrityException("Derived chunk receipt does not name the written bytes.");
                observeReceipt?.Invoke(receipt);
                contentHash.AppendData(bytes.Span);
                byteLength = checked(byteLength + bytes.Length);
                var (receiptBytes, receiptDigest) = DurableBlobWriteReceiptDigest.Canonicalize(receipt);
                var (receiptEvidence, receiptFailure) = await CustodyHold.TryHoldAsync(store, receiptBytes, stop.Token)
                    .ConfigureAwait(false);
                if (receiptEvidence is null)
                    throw new CustodyRequiredException("Derived chunk receipt was not retained: " + receiptFailure);
                if (receiptEvidence.Reference.ContentSha256 != receiptDigest || receiptEvidence.Reference.ByteLength != receiptBytes.Length)
                    throw new CustodyIntegrityException("Chunk receipt evidence does not name its written bytes.");
                observeReceipt?.Invoke(receiptEvidence);
                chunks.Add(new Chunk(digest, bytes.Length, receiptDigest));
                receipts.Add(receipt);
            }
            var canonicalSha256 = await producer.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!CustodyDigest.IsLowercaseSha256(canonicalSha256) || byteLength == 0)
                throw new InvalidOperationException("A derived artifact needs canonical bytes and their digest.");
            var contentSha256 = Convert.ToHexStringLower(contentHash.GetHashAndReset());
            var root = SerializeRoot(schema, chunkSize, kind, canonicalSha256, contentSha256, byteLength, chunks);
            var (rootReceipt, rootFailure) = await CustodyHold.TryHoldAsync(store, root, cancellationToken)
                .ConfigureAwait(false);
            if (rootReceipt is null) throw new CustodyRequiredException("Derived root was not held: " + rootFailure);
            if (rootReceipt.Reference.ContentSha256 != CustodyDigest.Of(root) || rootReceipt.Reference.ByteLength != root.Length)
                throw new CustodyIntegrityException("Derived root receipt does not name the written root.");
            observeReceipt?.Invoke(rootReceipt);
            return (rootReceipt, Array.AsReadOnly(receipts.ToArray()));
        }
        finally
        {
            // A failed consumer must release a writer waiting on a full channel before returning.
            stop.Cancel();
            channel.Writer.TryComplete();
            try { await producer.ConfigureAwait(false); }
            catch { /* Preserve the consumer/producer failure already propagated above. */ }
        }
    }

    // Routing only: OpenAsync still verifies the complete root, ordered closure and digests.
    internal static bool IsRoot(ReadOnlySpan<byte> bytes, out string? kind)
    {
        kind = null;
        try
        {
            var reader = new Utf8JsonReader(bytes);
            if (!(reader.Read() && reader.TokenType == JsonTokenType.StartObject &&
                reader.Read() && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("schema") &&
                reader.Read() && reader.TokenType == JsonTokenType.String &&
                (reader.ValueTextEquals(Schema) || reader.ValueTextEquals(SmallChunkSchema))))
                return false;
            if (reader.Read() && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("kind") &&
                reader.Read() && reader.TokenType == JsonTokenType.String)
                kind = reader.GetString();
            return true;
        }
        catch (JsonException) { return false; }
    }

    internal static async Task<ChunkedDerivedArtifact> OpenAsync(
        ICustodyStore store, string rootContentSha256, string expectedKind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKind);
        var rootBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, rootContentSha256, cancellationToken)
            .ConfigureAwait(false);
        string schema, canonicalSha256, contentSha256;
        int chunkSize;
        long byteLength;
        Chunk[] chunks;
        try
        {
            using var document = JsonDocument.Parse(rootBytes);
            var root = document.RootElement;
            RequireMembers(root, "schema", "kind", "canonicalSha256", "contentSha256", "byteLength", "chunkSize", "chunks");
            schema = root.GetProperty("schema").GetString()!;
            chunkSize = root.GetProperty("chunkSize").GetInt32();
            if (!((schema == Schema && chunkSize == ChunkSize) ||
                  (schema == SmallChunkSchema && chunkSize == SmallChunkSize)) ||
                root.GetProperty("kind").GetString() != expectedKind)
                throw new CustodyIntegrityException("Derived root has a different schema, kind or chunk size.");
            canonicalSha256 = root.GetProperty("canonicalSha256").GetString()!;
            contentSha256 = root.GetProperty("contentSha256").GetString()!;
            byteLength = root.GetProperty("byteLength").GetInt64();
            if (!CustodyDigest.IsLowercaseSha256(canonicalSha256) || !CustodyDigest.IsLowercaseSha256(contentSha256) || byteLength <= 0)
                throw new CustodyIntegrityException("Derived root has an invalid digest or length.");
            var array = root.GetProperty("chunks");
            var expectedCount = byteLength / chunkSize + (byteLength % chunkSize == 0 ? 0 : 1);
            if (array.GetArrayLength() != expectedCount)
                throw new CustodyIntegrityException("Derived root does not cover its declared byte length.");
            chunks = new Chunk[array.GetArrayLength()];
            var ordinal = 0;
            foreach (var value in array.EnumerateArray())
            {
                RequireMembers(value, "sha256", "byteLength", "receiptSha256");
                var digest = value.GetProperty("sha256").GetString()!;
                var length = value.GetProperty("byteLength").GetInt32();
                var receiptDigest = value.GetProperty("receiptSha256").GetString()!;
                var expectedLength = ordinal == chunks.Length - 1 ? byteLength - (long)ordinal * chunkSize : chunkSize;
                if (!CustodyDigest.IsLowercaseSha256(digest) || !CustodyDigest.IsLowercaseSha256(receiptDigest) || length != expectedLength)
                    throw new CustodyIntegrityException("Derived chunk identity or ordered length is invalid.");
                chunks[ordinal++] = new Chunk(digest, length, receiptDigest);
            }
            if (!rootBytes.Span.SequenceEqual(SerializeRoot(schema, chunkSize, expectedKind, canonicalSha256, contentSha256, byteLength, chunks)))
                throw new CustodyIntegrityException("Derived root is not its exact canonical representation.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new CustodyIntegrityException("Derived root is not a valid complete typed document.", exception);
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var receipts = new List<DurableBlobWriteReceipt>();
        foreach (var chunk in chunks)
        {
            var receiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, chunk.ReceiptSha256, cancellationToken)
                .ConfigureAwait(false);
            DurableBlobWriteReceipt receipt;
            try
            {
                receipt = ContractJson.Deserialize<DurableBlobWriteReceipt>(new UTF8Encoding(false, true).GetString(receiptBytes.Span));
            }
            catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
            {
                throw new CustodyIntegrityException("Derived chunk receipt is not valid typed evidence.", exception);
            }
            if (!receiptBytes.Span.SequenceEqual(DurableBlobWriteReceiptDigest.CanonicalBytes(receipt)) ||
                receipt.Reference.ContentSha256 != chunk.Sha256 || receipt.Reference.ByteLength != chunk.ByteLength)
                throw new CustodyIntegrityException("Derived chunk receipt does not bind its exact payload.");
            receipts.Add(receipt);
            var bytes = await ReadChunkAsync(store, chunk, cancellationToken).ConfigureAwait(false);
            hash.AppendData(bytes.Span);
        }
        if (Convert.ToHexStringLower(hash.GetHashAndReset()) != contentSha256)
            throw new CustodyIntegrityException("Derived chunks do not reproduce the complete ordered content digest.");
        return new ChunkedDerivedArtifact(store, expectedKind, canonicalSha256, contentSha256,
            byteLength, chunkSize, chunks, receipts, cancellationToken);
    }

    private static byte[] SerializeRoot(string schema, int chunkSize, string kind, string canonicalSha256, string contentSha256,
        long byteLength, IReadOnlyList<Chunk> chunks) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema, kind, canonicalSha256, contentSha256, byteLength, chunkSize,
            chunks = chunks.Select(static chunk => new { sha256 = chunk.Sha256, byteLength = chunk.ByteLength, receiptSha256 = chunk.ReceiptSha256 }),
        });

    private static void RequireMembers(JsonElement value, params string[] names)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!found.Add(property.Name) || !names.Contains(property.Name, StringComparer.Ordinal))
                throw new CustodyIntegrityException("Derived root contains duplicate or unknown members.");
        if (found.Count != names.Length) throw new CustodyIntegrityException("Derived root has missing members.");
    }

    private static async Task<ReadOnlyMemory<byte>> ReadChunkAsync(ICustodyStore store, Chunk chunk,
        CancellationToken cancellationToken)
    {
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, chunk.Sha256, cancellationToken)
            .ConfigureAwait(false);
        if (bytes.Length != chunk.ByteLength)
            throw new CustodyIntegrityException("Derived chunk has a different length.");
        return bytes;
    }

    private sealed record Chunk(string Sha256, int ByteLength, string ReceiptSha256);

    private sealed class CountingWriteStream(CancellationToken cancellationToken) : Stream
    {
        private long _length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _length = checked(_length + buffer.Length);
        }
    }

    private sealed class ChunkReadStream(ChunkedDerivedArtifact artifact) : Stream
    {
        private long _position;
        private int _cachedOrdinal = -1;
        private ReadOnlyMemory<byte> _cached;
        private bool _disposed;
        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override long Length => artifact.ByteLength;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            artifact._cancellationToken.ThrowIfCancellationRequested();
            if (buffer.IsEmpty || _position == Length) return 0;
            var ordinal = checked((int)(_position / artifact._chunkSize));
            if (_cachedOrdinal != ordinal)
            {
                // Canonical readers are synchronous. The custody continuation runs on a worker,
                // independently of the caller's synchronization context. Only one chunk is cached.
                _cached = Task.Run(() => ReadChunkAsync(artifact._store, artifact._chunks[ordinal],
                    artifact._cancellationToken)).GetAwaiter().GetResult();
                _cachedOrdinal = ordinal;
            }
            var offset = (int)(_position % artifact._chunkSize);
            var count = Math.Min(buffer.Length, _cached.Length - offset);
            _cached.Span.Slice(offset, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var next = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (next < 0 || next > Length) throw new IOException("Seek is outside the derived artifact.");
            _position = next;
            // A new pass must recheck custody even when the document fits in one chunk.
            _cached = default;
            _cachedOrdinal = -1;
            return next;
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            _disposed = true; _cached = default;
            base.Dispose(disposing);
        }
    }

    private sealed class ChunkWriteStream(ChannelWriter<ReadOnlyMemory<byte>> writer, int chunkSize, CancellationToken cancellationToken) : Stream
    {
        private byte[] _buffer = new byte[chunkSize];
        private int _used;
        private bool _completed;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_completed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            while (!buffer.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(buffer.Length, chunkSize - _used);
                buffer[..count].CopyTo(_buffer.AsSpan(_used));
                _used += count;
                buffer = buffer[count..];
                if (_used == chunkSize) Emit();
            }
        }
        private void Emit()
        {
            writer.WriteAsync(_buffer.AsMemory(0, _used), cancellationToken).AsTask().GetAwaiter().GetResult();
            _buffer = new byte[chunkSize];
            _used = 0;
        }
        internal void Complete()
        {
            if (_used != 0) Emit();
            _completed = true;
        }
        protected override void Dispose(bool disposing)
        {
            _completed = true; _buffer = [];
            base.Dispose(disposing);
        }
    }
}
