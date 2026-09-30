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
    private readonly ICustodyStore _store;
    private readonly Chunk[] _chunks;
    private readonly CancellationToken _cancellationToken;

    private ChunkedDerivedArtifact(ICustodyStore store, string kind, string canonicalSha256,
        string contentSha256, long byteLength, Chunk[] chunks,
        IReadOnlyList<DurableBlobWriteReceipt> chunkReceipts, CancellationToken cancellationToken)
    {
        _store = store;
        Kind = kind;
        CanonicalSha256 = canonicalSha256;
        ContentSha256 = contentSha256;
        ByteLength = byteLength;
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

    internal static async Task<(DurableBlobWriteReceipt RootReceipt,
        IReadOnlyList<DurableBlobWriteReceipt> ChunkReceipts)> WriteAsync(
        ICustodyStore store, string kind, Func<Stream, string> writeCanonical,
        CancellationToken cancellationToken)
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
                using var output = new ChunkWriteStream(channel.Writer, stop.Token);
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
                contentHash.AppendData(bytes.Span);
                byteLength = checked(byteLength + bytes.Length);
                var (receiptBytes, receiptDigest) = DurableBlobWriteReceiptDigest.Canonicalize(receipt);
                var (receiptEvidence, receiptFailure) = await CustodyHold.TryHoldAsync(store, receiptBytes, stop.Token)
                    .ConfigureAwait(false);
                if (receiptEvidence is null)
                    throw new CustodyRequiredException("Derived chunk receipt was not retained: " + receiptFailure);
                if (receiptEvidence.Reference.ContentSha256 != receiptDigest || receiptEvidence.Reference.ByteLength != receiptBytes.Length)
                    throw new CustodyIntegrityException("Chunk receipt evidence does not name its written bytes.");
                chunks.Add(new Chunk(digest, bytes.Length, receiptDigest));
                receipts.Add(receipt);
            }
            var canonicalSha256 = await producer.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!CustodyDigest.IsLowercaseSha256(canonicalSha256) || byteLength == 0)
                throw new InvalidOperationException("A derived artifact needs canonical bytes and their digest.");
            var contentSha256 = Convert.ToHexStringLower(contentHash.GetHashAndReset());
            var root = SerializeRoot(kind, canonicalSha256, contentSha256, byteLength, chunks);
            var (rootReceipt, rootFailure) = await CustodyHold.TryHoldAsync(store, root, cancellationToken)
                .ConfigureAwait(false);
            if (rootReceipt is null) throw new CustodyRequiredException("Derived root was not held: " + rootFailure);
            if (rootReceipt.Reference.ContentSha256 != CustodyDigest.Of(root) || rootReceipt.Reference.ByteLength != root.Length)
                throw new CustodyIntegrityException("Derived root receipt does not name the written root.");
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

    internal static async Task<ChunkedDerivedArtifact> OpenAsync(
        ICustodyStore store, string rootContentSha256, string expectedKind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKind);
        var rootBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, rootContentSha256, cancellationToken)
            .ConfigureAwait(false);
        string canonicalSha256, contentSha256;
        long byteLength;
        Chunk[] chunks;
        try
        {
            using var document = JsonDocument.Parse(rootBytes);
            var root = document.RootElement;
            RequireMembers(root, "schema", "kind", "canonicalSha256", "contentSha256", "byteLength", "chunkSize", "chunks");
            if (root.GetProperty("schema").GetString() != Schema || root.GetProperty("kind").GetString() != expectedKind ||
                root.GetProperty("chunkSize").GetInt32() != ChunkSize)
                throw new CustodyIntegrityException("Derived root has a different schema, kind or chunk size.");
            canonicalSha256 = root.GetProperty("canonicalSha256").GetString()!;
            contentSha256 = root.GetProperty("contentSha256").GetString()!;
            byteLength = root.GetProperty("byteLength").GetInt64();
            if (!CustodyDigest.IsLowercaseSha256(canonicalSha256) || !CustodyDigest.IsLowercaseSha256(contentSha256) || byteLength <= 0)
                throw new CustodyIntegrityException("Derived root has an invalid digest or length.");
            var array = root.GetProperty("chunks");
            var expectedCount = byteLength / ChunkSize + (byteLength % ChunkSize == 0 ? 0 : 1);
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
                var expectedLength = ordinal == chunks.Length - 1 ? byteLength - (long)ordinal * ChunkSize : ChunkSize;
                if (!CustodyDigest.IsLowercaseSha256(digest) || !CustodyDigest.IsLowercaseSha256(receiptDigest) || length != expectedLength)
                    throw new CustodyIntegrityException("Derived chunk identity or ordered length is invalid.");
                chunks[ordinal++] = new Chunk(digest, length, receiptDigest);
            }
            if (!rootBytes.Span.SequenceEqual(SerializeRoot(expectedKind, canonicalSha256, contentSha256, byteLength, chunks)))
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
            byteLength, chunks, receipts, cancellationToken);
    }

    private static byte[] SerializeRoot(string kind, string canonicalSha256, string contentSha256,
        long byteLength, IReadOnlyList<Chunk> chunks) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = Schema, kind, canonicalSha256, contentSha256, byteLength, chunkSize = ChunkSize,
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
            var ordinal = checked((int)(_position / ChunkSize));
            if (_cachedOrdinal != ordinal)
            {
                // Canonical readers are synchronous. The custody continuation runs on a worker,
                // independently of the caller's synchronization context. Only one chunk is cached.
                _cached = Task.Run(() => ReadChunkAsync(artifact._store, artifact._chunks[ordinal],
                    artifact._cancellationToken)).GetAwaiter().GetResult();
                _cachedOrdinal = ordinal;
            }
            var offset = (int)(_position % ChunkSize);
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

    private sealed class ChunkWriteStream(ChannelWriter<ReadOnlyMemory<byte>> writer, CancellationToken cancellationToken) : Stream
    {
        private byte[] _buffer = new byte[ChunkSize];
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
                var count = Math.Min(buffer.Length, ChunkSize - _used);
                buffer[..count].CopyTo(_buffer.AsSpan(_used));
                _used += count;
                buffer = buffer[count..];
                if (_used == ChunkSize) Emit();
            }
        }
        private void Emit()
        {
            writer.WriteAsync(_buffer.AsMemory(0, _used), cancellationToken).AsTask().GetAwaiter().GetResult();
            _buffer = new byte[ChunkSize];
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
