using System.Buffers.Binary;
using System.Collections;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Immutable sorted object identities derived from observations. The retained payload
/// uses 32 bytes per distinct digest; hexadecimal strings are produced only when requested.
/// This set states identity membership, not publisher delivery or enumeration completeness.</summary>
internal sealed class LuxembourgObjectDigestSet : IReadOnlyList<string>
{
    private readonly Digest[] _values;

    private LuxembourgObjectDigestSet(Digest[] values) => _values = values;

    public int Count => _values.Length;
    public string this[int index] => _values[index].ToHex();

    internal static LuxembourgObjectDigestSet FromObservations(
        IReadOnlyList<LuxembourgResourceObservation> observations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        cancellationToken.ThrowIfCancellationRequested();
        var values = new Digest[observations.Count];
        var index = 0;
        foreach (var observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index == values.Length)
                throw new InvalidOperationException("Observation enumeration exceeds its declared count.");
            ArgumentNullException.ThrowIfNull(observation);
            var hex = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observation.ObjectRef);
            if (!Digest.TryParse(hex, out values[index++]))
                throw new InvalidOperationException("Object identity writer returned a non-canonical digest.");
        }
        if (index != values.Length)
            throw new InvalidOperationException("Observation enumeration ended before its declared count.");
        Array.Sort(values);
        cancellationToken.ThrowIfCancellationRequested();
        var distinct = 0;
        foreach (var value in values)
            if (distinct == 0 || values[distinct - 1].CompareTo(value) != 0)
                values[distinct++] = value;
        if (distinct != values.Length) Array.Resize(ref values, distinct);
        cancellationToken.ThrowIfCancellationRequested();
        return new LuxembourgObjectDigestSet(values);
    }

    internal bool Contains(string? hex) => Digest.TryParse(hex, out var digest) &&
        Array.BinarySearch(_values, digest) >= 0;

    public IEnumerator<string> GetEnumerator()
    {
        foreach (var value in _values) yield return value.ToHex();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private readonly struct Digest(ulong first, ulong second, ulong third, ulong fourth) : IComparable<Digest>
    {
        private readonly ulong _first = first;
        private readonly ulong _second = second;
        private readonly ulong _third = third;
        private readonly ulong _fourth = fourth;

        public int CompareTo(Digest other)
        {
            var comparison = _first.CompareTo(other._first);
            if (comparison != 0) return comparison;
            comparison = _second.CompareTo(other._second);
            if (comparison != 0) return comparison;
            comparison = _third.CompareTo(other._third);
            return comparison != 0 ? comparison : _fourth.CompareTo(other._fourth);
        }

        public string ToHex()
        {
            Span<byte> bytes = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, _first);
            BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], _second);
            BinaryPrimitives.WriteUInt64BigEndian(bytes[16..], _third);
            BinaryPrimitives.WriteUInt64BigEndian(bytes[24..], _fourth);
            return Convert.ToHexStringLower(bytes);
        }

        public static bool TryParse(string? hex, out Digest digest)
        {
            digest = default;
            if (hex is null || hex.Length != 64) return false;
            Span<byte> bytes = stackalloc byte[32];
            for (var index = 0; index < bytes.Length; index++)
            {
                var high = Nibble(hex[index * 2]);
                var low = Nibble(hex[index * 2 + 1]);
                if (high < 0 || low < 0) return false;
                bytes[index] = (byte)((high << 4) | low);
            }
            digest = new Digest(BinaryPrimitives.ReadUInt64BigEndian(bytes),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[16..]),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[24..]));
            return true;
        }

        private static int Nibble(char value) => value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => value - 'a' + 10,
            _ => -1
        };
    }
}
