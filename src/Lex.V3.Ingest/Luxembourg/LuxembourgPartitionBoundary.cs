using System.Text;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Chooses an interior six-part cursor without inspecting or excluding publisher rows.</summary>
internal static class LuxembourgPartitionBoundary
{
    internal static LuxembourgQueryCursor? Between(LuxembourgQueryPartitionRange range)
    {
        var lower = Parts(range.StartInclusive);
        var upper = Parts(range.EndExclusive);
        var differing = Enumerable.Range(0, 6).First(i => lower[i] != upper[i]);
        var middle = BetweenStrings(lower[differing], upper[differing]);
        if (middle is not null)
        {
            var candidate = Make(lower.Take(differing).Concat([middle])
                .Concat(Enumerable.Repeat("", 5 - differing)).ToArray());
            if (candidate is not null && candidate.CompareTo(range.StartInclusive) > 0 &&
                candidate.CompareTo(range.EndExclusive) < 0) return candidate;
        }

        // Adjacent strings can still have an interior tuple: retain the lower differing
        // component and advance a later component. The upper tuple remains strictly larger.
        for (var i = differing + 1; i < 6; i++)
        {
            var next = Above(lower[i]);
            if (next is null) continue;
            var candidate = Make(lower.Take(i).Concat([next])
                .Concat(Enumerable.Repeat("", 5 - i)).ToArray());
            if (candidate is not null && candidate.CompareTo(range.StartInclusive) > 0 &&
                candidate.CompareTo(range.EndExclusive) < 0) return candidate;
        }

        return null;
    }

    private static string? BetweenStrings(string lower, string upper)
    {
        var a = lower.EnumerateRunes().Select(static r => Ordinal(r.Value)).ToArray();
        var b = upper.EnumerateRunes().Select(static r => Ordinal(r.Value)).ToArray();
        var shared = 0;
        while (shared < a.Length && shared < b.Length && a[shared] == b[shared]) shared++;
        var prefix = FromOrdinals(a.Take(shared));
        if (shared == a.Length)
        {
            if (b[shared] > 0) return Bounded(prefix + Scalar(Midpoint(-1, b[shared])));
            return shared + 1 < b.Length ? Bounded(prefix + "\0") : null;
        }

        if (b[shared] - a[shared] > 1)
            return Bounded(prefix + Scalar(Midpoint(a[shared], b[shared])));

        var tail = Above(FromOrdinals(a.Skip(shared + 1)));
        return tail is null ? null : Bounded(prefix + Scalar(a[shared]) + tail);
    }

    private static string? Above(string lower)
    {
        var runes = lower.EnumerateRunes().Select(static r => Ordinal(r.Value)).ToArray();
        for (var i = 0; i < runes.Length; i++)
        {
            if (runes[i] == MaxOrdinal) continue;
            return Bounded(FromOrdinals(runes.Take(i)) + Scalar(Math.Max(runes[i] + 1, Midpoint(runes[i], MaxOrdinal + 1))));
        }
        return Bounded(lower + "?");
    }

    // Unicode scalar order agrees with strict UTF-8 lexical order; surrogate code points
    // are absent. Prefer ASCII cuts while possible, since most publisher keys are IRIs.
    private const int MaxOrdinal = 0x10ffff - 0x800;
    private static int Ordinal(int scalar) => scalar < 0xd800 ? scalar : scalar - 0x800;
    private static string Scalar(int ordinal) => char.ConvertFromUtf32(ordinal < 0xd800 ? ordinal : ordinal + 0x800);
    private static string FromOrdinals(IEnumerable<int> values) => string.Concat(values.Select(Scalar));
    private static int Midpoint(int lower, int upper) => lower + (Math.Min(upper, lower < 126 ? 127 : upper) - lower) / 2;
    private static string? Bounded(string value) => Encoding.UTF8.GetByteCount(value) <= 2047 ? value : null;
    private static string[] Parts(LuxembourgQueryCursor cursor) =>
        [cursor.Key1, cursor.Key2, cursor.Key3, cursor.Key4, cursor.Key5, cursor.Key6];
    private static LuxembourgQueryCursor? Make(string[] parts) =>
        parts.Any(static p => Encoding.UTF8.GetByteCount(p) > 2047) ? null :
            new(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
}
