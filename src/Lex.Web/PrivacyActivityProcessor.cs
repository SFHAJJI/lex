using System.Diagnostics;
using System.Text.RegularExpressions;
using Lex.Mcp;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Lex.Web;

internal sealed class PrivacyActivityProcessor : BaseProcessor<Activity>
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "lex.request", "lex.tool", "lex.plan",
    };
    private static readonly HashSet<string> Surfaces = new(StringComparer.Ordinal)
        { "search", "mcp", "ask", "ask_stream" };
    private static readonly HashSet<string> ResponseClasses = new(StringComparer.Ordinal)
        { "1xx", "2xx", "3xx", "4xx", "5xx" };
    private static readonly HashSet<string> Statuses = new(
        McpStatus.All.Concat(["mixed", "invalid_request", "cancelled", "failed"]),
        StringComparer.Ordinal);
    private static readonly HashSet<string> HitBuckets = new(StringComparer.Ordinal)
        { "0", "1", "2-5", "6-10", "11-25", "26-50", "51+" };
    private static readonly HashSet<string> Languages = new(StringComparer.Ordinal)
    {
        "de", "en", "fr", "lb", "other", "pt",
    };
    private static readonly HashSet<string> PlanShapes = new(StringComparer.Ordinal)
    {
        "clarification", "gap", "legal_boundary", "application_mixed",
        "single_legal", "single_legal_synthesis", "multi_legal",
        "multi_legal_synthesis", "mixed", "mixed_synthesis",
    };
    private static readonly Regex Digest = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public override void OnEnd(Activity activity)
    {
        if (!Names.Contains(activity.DisplayName)) activity.DisplayName = "lex.unknown";
        foreach (var tag in activity.TagObjects.ToArray())
            if (!Allowed(tag)) activity.SetTag(tag.Key, null);
        foreach (var (name, _) in activity.Baggage.ToArray())
            activity.SetBaggage(name, null);
        activity.TraceStateString = null;
        activity.SetStatus(activity.Status);
        // Events and links are immutable once attached to an Activity. Dropping the recorded
        // flag makes the exporter processor reject the entire span at the final export edge.
        if (activity.Events.Any() || activity.Links.Any())
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
    }

    private static bool Allowed(KeyValuePair<string, object?> tag) => tag.Key switch
    {
        "lex.surface" => tag.Value is string value && Surfaces.Contains(value),
        "lex.response_class" => tag.Value is string value && ResponseClasses.Contains(value),
        "http.response.status_code" => tag.Value is int value && value is >= 100 and <= 599,
        "lex.tool" => tag.Value is string value
            && (value == "unknown"
                || LegalOperationCatalog.ToolNames.Contains(value, StringComparer.Ordinal)),
        "lex.status" => tag.Value is string value && Statuses.Contains(value),
        "lex.hit_count_bucket" => tag.Value is string value && HitBuckets.Contains(value),
        "lex.zero_hit" => tag.Value is bool,
        "lex.language" => tag.Value is string value && Languages.Contains(value),
        "lex.plan_shape" => tag.Value is string value && PlanShapes.Contains(value),
        "lex.digest" => tag.Value is string value && Digest.IsMatch(value),
        "lex.retrieval_mode" => tag.Value is "keyword" or "hybrid",
        _ => false,
    };
}

internal static class LexTraceConfiguration
{
    internal static Sampler TraceSampler { get; } = new AlwaysOnSampler();
}
