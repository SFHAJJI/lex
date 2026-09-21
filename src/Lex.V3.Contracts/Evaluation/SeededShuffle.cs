namespace Lex.V3.Contracts.Evaluation;

/// <summary>
/// A small deterministic generator (SplitMix64), written here and not taken from <see cref="Random"/> because the
/// sequence of <c>System.Random</c> is not promised across runtimes, and a shuffled control whose expected collapse
/// depends on the machine that ran it is not evidence. The same seed gives the same sequence everywhere.
/// </summary>
public sealed class SplitMix64
{
    private ulong _state;

    public SplitMix64(ulong seed)
    {
        _state = seed;
    }

    public ulong Next()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            var mixed = _state;
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            return mixed ^ (mixed >> 31);
        }
    }

    /// <summary>A whole number in <c>[0, bound)</c>, without modulo bias: a draw below 2^64 mod bound is discarded.</summary>
    public int NextBelow(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bound, 1);
        var span = (ulong)bound;
        var threshold = unchecked(0UL - span) % span;
        ulong draw;
        do
        {
            draw = Next();
        }
        while (draw < threshold);

        return (int)(draw % span);
    }
}

/// <summary>
/// Which item each item receives when values are permuted among items so that, as far as the counts allow, no item
/// keeps a value with its own key.
/// </summary>
/// <remarks>
/// Items are grouped by key, each group is shuffled by the seeded generator, and the groups laid end to end are
/// rotated by the size of the largest group. An item and the one it receives are then in different groups whenever
/// the largest group is at most half of all items, and when it is larger exactly <c>2m - n</c> items must keep a value
/// with their own key, which is the fewest any permutation can leave. The values are only moved, so the marginal
/// distribution of keys is exactly what it was.
/// </remarks>
public static class SeededDerangement
{
    /// <summary><c>result[i]</c> is the index whose value item <c>i</c> receives.</summary>
    public static int[] ByKey(IReadOnlyList<string> keys, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var count = keys.Count;
        var random = new SplitMix64(seed);
        var groups = Enumerable.Range(0, count)
            .GroupBy(index => keys[index], StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.ToArray())
            .ToArray();
        foreach (var group in groups)
        {
            for (var index = group.Length - 1; index > 0; index--)
            {
                var other = random.NextBelow(index + 1);
                (group[index], group[other]) = (group[other], group[index]);
            }
        }

        var order = groups.SelectMany(static group => group).ToArray();
        var shift = groups.Length == 0 ? 0 : groups.Max(static group => group.Length);
        var result = new int[count];
        for (var position = 0; position < count; position++)
        {
            result[order[position]] = order[(position + shift) % count];
        }

        return result;
    }
}
