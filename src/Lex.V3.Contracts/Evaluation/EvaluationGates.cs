using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Evaluation;

/// <summary>A gate's verdict, in the closed vocabulary pass, fail, not measured. Only pass passes.</summary>
public enum GateVerdict
{
    [JsonStringEnumMemberName("pass")]
    Pass = 1,

    [JsonStringEnumMemberName("fail")]
    Fail = 2,

    [JsonStringEnumMemberName("not_measured")]
    NotMeasured = 3,
}

/// <summary>A gate's verdict and its name. A verdict of not measured always carries the reason, and no other verdict carries one.</summary>
public sealed record GateResult
{
    public GateResult(string name, GateVerdict verdict, NotMeasuredReason? reason)
    {
        Name = Identity.Require(name, nameof(name));
        Verdict = Enum.IsDefined(verdict)
            ? verdict
            : throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "The verdict is not in the closed vocabulary.");
        if ((verdict == GateVerdict.NotMeasured) != reason.HasValue)
        {
            throw new ArgumentException("Exactly a verdict of not measured carries a reason.", nameof(reason));
        }

        Reason = reason;
    }

    public string Name { get; }

    public GateVerdict Verdict { get; }

    public NotMeasuredReason? Reason { get; }
}

public static class EvaluationGates
{
    /// <summary>
    /// A gate on a metric that must reach a threshold. It passes at the threshold and fails below it, and where the
    /// metric has no value the gate is not measured, with the metric's reason: a null the gate reads never passes.
    /// </summary>
    public static GateResult AtLeast(string name, MetricResult metric, double threshold)
    {
        ArgumentNullException.ThrowIfNull(metric);
        if (!double.IsFinite(threshold) || threshold is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "A threshold is a number from 0 to 1.");
        }

        return metric.Value is { } value
            ? new GateResult(name, value >= threshold ? GateVerdict.Pass : GateVerdict.Fail, null)
            : new GateResult(name, GateVerdict.NotMeasured, metric.Reason);
    }

    /// <summary>
    /// Whether a release gate set passes: every gate the release requires is reported exactly once, and every gate
    /// reported passes. The required names are the caller's own declaration and are never read off the set being
    /// judged, because a set read against itself passes on whatever it kept: a gate that was dropped is a smaller set
    /// that still passes. A gate that was not measured blocks exactly as a failure does, and a required gate that is not
    /// reported blocks the same, so a report cannot release on fewer gates than its release needs.
    /// </summary>
    public static bool ReleasePasses(IReadOnlyCollection<GateResult> gates, IReadOnlyCollection<string> required)
    {
        ArgumentNullException.ThrowIfNull(gates);
        RequireNames(required);
        return gates.All(static gate => gate.Verdict == GateVerdict.Pass) && Unreported(gates, required).Count == 0;
    }

    /// <summary>The required gates a set does not report exactly once: absent ones and ones reported twice, in the order required.</summary>
    public static IReadOnlyList<string> Unreported(IReadOnlyCollection<GateResult> gates, IReadOnlyCollection<string> required)
    {
        ArgumentNullException.ThrowIfNull(gates);
        RequireNames(required);
        return required
            .Where(name => gates.Count(gate => string.Equals(gate.Name, name, StringComparison.Ordinal)) != 1)
            .ToArray();
    }

    private static void RequireNames(IReadOnlyCollection<string> required)
    {
        ArgumentNullException.ThrowIfNull(required);
        if (required.Count == 0 || required.Any(string.IsNullOrWhiteSpace) || required.Distinct(StringComparer.Ordinal).Count() != required.Count)
        {
            throw new ArgumentException("A release requires at least one gate and names each once.", nameof(required));
        }
    }
}
