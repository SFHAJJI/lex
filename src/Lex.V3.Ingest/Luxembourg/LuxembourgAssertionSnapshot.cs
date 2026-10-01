using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Immutable derived assertion bytes with a compact subject/offset index rebuilt at open.
/// This storage layer preserves rows and source references; it does not prove publisher delivery,
/// census membership, admission, or completeness. Callers must independently replay those proofs.
/// Subjects must be contiguous in the input, and repeated subject groups refuse at open.</summary>
internal sealed class LuxembourgAssertionSnapshot
{
    internal const string Kind = "lex-v3-luxembourg-assertion-snapshot/1";
    // A resource bound, not an admission rule. Oversized assertions refuse; none are truncated.
    internal const int MaximumRecordBytes = 4 * 1024 * 1024;
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes(Kind + "\n");
    private readonly ChunkedDerivedArtifact _artifact;
    private readonly SourceArtifactRef _observation;
    private readonly Entry[] _entries;
    private readonly CancellationToken _cancellationToken;

    private LuxembourgAssertionSnapshot(ChunkedDerivedArtifact artifact, SourceArtifactRef observation,
        Entry[] entries, long assertionCount, CancellationToken cancellationToken)
    {
        _artifact = artifact;
        _observation = observation;
        _entries = entries;
        AssertionCount = assertionCount;
        _cancellationToken = cancellationToken;
    }

    internal int SubjectCount => _entries.Length;
    internal long AssertionCount { get; }

    /// <summary>Writes a deterministic sequence without retaining the input rows. Order and
    /// multiplicity are preserved, including identical assertions. The returned digest is domain
    /// separated. Retaining these bytes through ChunkedDerivedArtifact supplies storage integrity.</summary>
    internal static string Write(Stream destination, SourceArtifactRef runIdentity,
        SourceArtifactRef observation, IReadOnlyList<SourceArtifactRef> censusProofs,
        IReadOnlyList<SourceArtifactRef> assertionProofs, IEnumerable<LuxembourgObservedAssertion> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(rows);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        WriteRecord(destination, hash, HeaderBytes(runIdentity, observation, censusProofs, assertionProofs), cancellationToken);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecord(destination, hash, AssertionBytes(row, observation), cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Writes verified rows as they arrive without retaining their union. The source
    /// must preserve contiguous subject groups and the same proof/admission checks as Write.
    /// This method preserves the synchronous writer's bytes and leaves the destination open.</summary>
    internal static async Task<string> WriteAsync(Stream destination, SourceArtifactRef runIdentity,
        SourceArtifactRef observation, IReadOnlyList<SourceArtifactRef> censusProofs,
        IReadOnlyList<SourceArtifactRef> assertionProofs, IAsyncEnumerable<LuxembourgObservedAssertion> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(rows);
        cancellationToken.ThrowIfCancellationRequested();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        WriteRecord(destination, hash, HeaderBytes(runIdentity, observation, censusProofs, assertionProofs), cancellationToken);
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecord(destination, hash, AssertionBytes(row, observation), cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static byte[] AssertionBytes(LuxembourgObservedAssertion row, SourceArtifactRef observation)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.ObservationRef != observation)
            throw new InvalidOperationException("An assertion snapshot cannot mix run observations.");
        return JsonSerializer.SerializeToUtf8Bytes(ToStored(row));
    }

    private static void WriteRecord(Stream destination, IncrementalHash hash, byte[] bytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length == 0 || bytes.Length > MaximumRecordBytes)
            throw new InvalidOperationException("An assertion snapshot record exceeds its storage bound.");
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        destination.Write(length);
        destination.Write(bytes);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    /// <summary>Rebuilds every index entry by parsing the entire verified sequence. No caller index
    /// or file offset is accepted. Only compact hashes/offsets survive opening; rows are read on demand.
    /// The expected references bind this derived snapshot to the caller's separately verified inputs.</summary>
    internal static LuxembourgAssertionSnapshot Open(ChunkedDerivedArtifact artifact,
        SourceArtifactRef runIdentity, SourceArtifactRef observation,
        IReadOnlyList<SourceArtifactRef> censusProofs, IReadOnlyList<SourceArtifactRef> assertionProofs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Kind != Kind) throw Invalid("Wrong assertion snapshot kind.");
        using var stream = artifact.OpenRead();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        var header = ReadRecord(stream, hash, cancellationToken);
        if (!header.AsSpan().SequenceEqual(HeaderBytes(runIdentity, observation, censusProofs, assertionProofs)))
            throw Invalid("Assertion snapshot run, observation, or source proof references differ.");
        var entries = new List<Entry>();
        string? previous = null;
        long groupOffset = 0;
        var groupCount = 0;
        long assertionCount = 0;
        while (stream.Position < stream.Length)
        {
            var offset = stream.Position;
            var row = ReadAssertion(ReadRecord(stream, hash, cancellationToken), observation);
            if (row.SubjectIri != previous)
            {
                if (previous is not null) entries.Add(Entry.For(previous, groupOffset, groupCount));
                previous = row.SubjectIri;
                groupOffset = offset;
                groupCount = 0;
            }
            groupCount = checked(groupCount + 1);
            assertionCount = checked(assertionCount + 1);
        }
        if (previous is not null) entries.Add(Entry.For(previous, groupOffset, groupCount));
        if (Convert.ToHexStringLower(hash.GetHashAndReset()) != artifact.CanonicalSha256)
            throw Invalid("Assertion snapshot canonical digest differs.");
        cancellationToken.ThrowIfCancellationRequested();
        entries.Sort(static (left, right) => left.CompareDigest(right));
        for (var index = 1; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries[index - 1].CompareDigest(entries[index]) == 0)
                throw Invalid("Repeated assertion subject group or subject digest collision.");
        }
        return new LuxembourgAssertionSnapshot(artifact, observation, entries.ToArray(),
            assertionCount, cancellationToken);
    }

    internal IReadOnlyList<LuxembourgObservedAssertion> ReadSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        _cancellationToken.ThrowIfCancellationRequested();
        var wanted = Entry.For(subject, 0, 0);
        var low = 0;
        var high = _entries.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var entry = _entries[middle];
            var comparison = entry.CompareDigest(wanted);
            if (comparison < 0) { low = middle + 1; continue; }
            if (comparison > 0) { high = middle - 1; continue; }
            using var stream = _artifact.OpenRead();
            stream.Seek(entry.Offset, SeekOrigin.Begin);
            var rows = new List<LuxembourgObservedAssertion>();
            for (var index = 0; index < entry.Count; index++)
            {
                var row = ReadAssertion(ReadRecord(stream, null, _cancellationToken), _observation);
                if (row.SubjectIri != subject)
                    throw Invalid("Assertion subject lookup digest matched another subject.");
                rows.Add(row);
            }
            return rows;
        }
        // Absence here states only that the derived snapshot has no assertion for this subject.
        return [];
    }

    private static byte[] HeaderBytes(SourceArtifactRef runIdentity, SourceArtifactRef observation,
        IReadOnlyList<SourceArtifactRef> censusProofs, IReadOnlyList<SourceArtifactRef> assertionProofs)
    {
        ArgumentNullException.ThrowIfNull(runIdentity);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(censusProofs);
        ArgumentNullException.ThrowIfNull(assertionProofs);
        foreach (var reference in censusProofs.Concat(assertionProofs))
            ArgumentNullException.ThrowIfNull(reference);
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = Kind, runIdentity, observation, censusProofs, assertionProofs,
        });
    }

    private static byte[] ReadRecord(Stream stream, IncrementalHash? hash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.Length - stream.Position < 4) throw Invalid("Truncated assertion record length.");
        Span<byte> lengthBytes = stackalloc byte[4];
        stream.ReadExactly(lengthBytes);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > MaximumRecordBytes || length > stream.Length - stream.Position)
            throw Invalid("Invalid or truncated assertion record length.");
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        hash?.AppendData(lengthBytes);
        hash?.AppendData(bytes);
        return bytes;
    }

    private static LuxembourgObservedAssertion ReadAssertion(byte[] bytes, SourceArtifactRef observation)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredAssertion>(bytes)
                ?? throw Invalid("Null assertion record.");
            // Exact reserialization rejects unknown, missing, repeated or reordered JSON properties,
            // noncanonical encodings, and every alternate representation of the stored row.
            if (!bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(stored)))
                throw Invalid("Noncanonical assertion record.");
            return new LuxembourgObservedAssertion(stored.Subject, stored.Predicate, stored.ObjectKind,
                stored.Value, stored.Datatype, stored.Language, observation);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw Invalid("Invalid assertion record: " + error.Message);
        }
    }

    private static StoredAssertion ToStored(LuxembourgObservedAssertion row) =>
        new(row.SubjectIri, row.PredicateIri, row.ObjectKind, row.ObjectIriOrLexical,
            row.DatatypeIriOrEmpty, row.LanguageTagOrEmpty);

    private static CustodyIntegrityException Invalid(string message) => new(message);

    private sealed record StoredAssertion(string Subject, string Predicate,
        LuxembourgAssertionObjectKind ObjectKind, string Value, string Datatype, string Language);

    private readonly record struct Entry(ulong A, ulong B, ulong C, ulong D, long Offset, int Count)
    {
        internal static Entry For(string subject, long offset, int count)
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(subject));
            return new(BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(0, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(8, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(16, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(24, 8)), offset, count);
        }

        internal int CompareDigest(Entry other)
        {
            var value = A.CompareTo(other.A);
            if (value != 0) return value;
            value = B.CompareTo(other.B);
            if (value != 0) return value;
            value = C.CompareTo(other.C);
            return value != 0 ? value : D.CompareTo(other.D);
        }
    }
}
