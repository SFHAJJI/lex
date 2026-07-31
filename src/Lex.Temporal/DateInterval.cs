namespace Lex.Temporal;

/// <summary>
/// A half-open-ended validity interval over calendar dates. <see cref="To"/> null means open (still valid).
/// Knows nothing about law, publishers, or documents.
/// </summary>
public readonly record struct DateInterval(DateOnly From, DateOnly? To)
{
    public bool Contains(DateOnly date) => date >= From && (To is null || date <= To.Value);

    public bool Overlaps(DateInterval other) =>
        From <= (other.To ?? DateOnly.MaxValue) && other.From <= (To ?? DateOnly.MaxValue);

    public bool IsOpen => To is null;

    public override string ToString() => To is null ? $"{From:yyyy-MM-dd}--" : $"{From:yyyy-MM-dd}--{To:yyyy-MM-dd}";
}

/// <summary>As-of resolution over any interval-carrying items.</summary>
public static class AsOfResolver
{
    /// <summary>
    /// The item whose interval contains <paramref name="date"/>; when several match,
    /// the one with the latest start wins (the most specific state).
    /// </summary>
    public static T? Resolve<T>(IEnumerable<T> items, Func<T, DateInterval> interval, DateOnly date) where T : class
    {
        T? best = null;
        DateOnly bestFrom = DateOnly.MinValue;
        foreach (var item in items)
        {
            var iv = interval(item);
            if (!iv.Contains(date)) continue;
            if (best is null || iv.From >= bestFrom) { best = item; bestFrom = iv.From; }
        }
        return best;
    }

    /// <summary>All items in force on a date.</summary>
    public static IEnumerable<T> InForceOn<T>(IEnumerable<T> items, Func<T, DateInterval> interval, DateOnly date)
        => items.Where(i => interval(i).Contains(date));
}
