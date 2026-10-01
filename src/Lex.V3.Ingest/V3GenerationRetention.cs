using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest;

/// <summary>One earlier build of a chained Luxembourg log: the index its successor names, its observation and its build time.</summary>
public sealed record V3Generation(string IndexSha256, long Observation, string BuiltAt);

/// <summary>A generation the retention line keeps, with every reason it is kept.</summary>
public sealed record V3RetainedGeneration(string IndexSha256, long Observation, string BuiltAt, IReadOnlyList<string> Reasons);

/// <summary>What the retention line decided over a log: the generations kept, those dropped, and those no longer present.</summary>
public sealed record V3RetentionDecision(
    string PolicyId,
    string EvaluatedAt,
    IReadOnlyList<V3RetainedGeneration> Retained,
    IReadOnlyList<V3Generation> Dropped,
    IReadOnlyList<V3Generation> Absent);

/// <summary>
/// The retention line for built generations (Stage 7, S7-A09, which the panel applied to the generation mount on the
/// owner's behalf): every generation a published permalink or evidence bundle references, indefinitely; the last build
/// of each UTC day for 90 days; and the earliest generation of each UTC month, the monthly keeper, indefinitely.
/// </summary>
/// <remarks>
/// The decision is a function of the log and the sets given, never of the clock: "now" is the mounted build's own time,
/// so two builds of one chain decide alike. A generation can be kept only while it is present (its directory is held);
/// one that is not is reported absent rather than claimed. Nothing records which permalinks or bundles were published,
/// so <paramref name="referenced"/> in <see cref="Decide"/> is the caller's: the generations promoted to production,
/// which is empty until the owner promotes one. The mounted build is the mount itself, not a generation.
/// </remarks>
public static class V3GenerationRetention
{
    public const string PolicyId = "lex-v3-generation-retention/1";

    public const int NightlyDays = 90;

    public const string Referenced = "referenced";

    public const string Nightly = "nightly";

    public const string MonthlyKeeper = "monthly_keeper";

    /// <summary>The earlier builds of a log, oldest first: observation k's build is the index observation k + 1 names as its predecessor.</summary>
    public static IReadOnlyList<V3Generation> GenerationsOf(IReadOnlyList<LuxembourgIndexObservation> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return Enumerable.Range(0, Math.Max(0, log.Count - 1))
            .Select(k => new V3Generation(log[k + 1].PredecessorIndexSha256!, log[k].Observation, log[k].BuiltAt))
            .ToArray();
    }

    public static V3RetentionDecision Decide(
        IReadOnlyList<LuxembourgIndexObservation> log,
        IReadOnlySet<string> present,
        IReadOnlySet<string> referenced)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(referenced);
        if (log.Count == 0)
        {
            throw new ArgumentException("A log that records no build has no generation to retain.", nameof(log));
        }

        var now = Time(log[^1].BuiltAt);
        var generations = GenerationsOf(log);
        var held = generations.Where(generation => present.Contains(generation.IndexSha256)).ToArray();

        // The last build of each UTC day, over every build of the log (the mounted one included, so an earlier build of
        // the mounted build's day is not its day's last).
        var lastOfDay = log
            .GroupBy(static observation => Time(observation.BuiltAt).UtcDateTime.Date)
            .Select(static day => day.MaxBy(static observation => observation.Observation)!.Observation)
            .ToHashSet();

        // The earliest held generation of each UTC month: it never changes as the month fills.
        var monthlyKeepers = held
            .GroupBy(static generation => (Time(generation.BuiltAt).Year, Time(generation.BuiltAt).Month))
            .Select(static month => month.MinBy(static generation => generation.Observation)!.Observation)
            .ToHashSet();

        var retained = new List<V3RetainedGeneration>();
        var dropped = new List<V3Generation>();
        foreach (var generation in held)
        {
            var reasons = new List<string>();
            if (referenced.Contains(generation.IndexSha256)) reasons.Add(Referenced);
            if (lastOfDay.Contains(generation.Observation) && now - Time(generation.BuiltAt) <= TimeSpan.FromDays(NightlyDays)) reasons.Add(Nightly);
            if (monthlyKeepers.Contains(generation.Observation)) reasons.Add(MonthlyKeeper);
            if (reasons.Count == 0)
            {
                dropped.Add(generation);
            }
            else
            {
                retained.Add(new V3RetainedGeneration(generation.IndexSha256, generation.Observation, generation.BuiltAt, reasons.AsReadOnly()));
            }
        }

        return new V3RetentionDecision(
            PolicyId,
            log[^1].BuiltAt,
            retained.AsReadOnly(),
            dropped.AsReadOnly(),
            generations.Where(generation => !present.Contains(generation.IndexSha256)).ToArray());
    }

    private static DateTimeOffset Time(string builtAt) =>
        LuxembourgIndexBuilder.ParseBuiltAt(builtAt)
        ?? throw new ArgumentException($"The build time {builtAt} is not a UTC second.", nameof(builtAt));
}
