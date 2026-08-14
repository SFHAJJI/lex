using System.Diagnostics;
using OpenTelemetry;

namespace Lex.Web;

internal sealed class PrivacyActivityProcessor : BaseProcessor<Activity>
{
    private static readonly string[] AddressTags =
    [
        "client.address", "client.socket.address", "network.peer.address",
        "net.peer.ip", "http.client_ip",
    ];

    public override void OnEnd(Activity activity)
    {
        activity.SetTag("url.query", null);
        foreach (var name in AddressTags) activity.SetTag(name, null);
        foreach (var (name, _) in activity.TagObjects.ToArray())
            if (IsSensitive(name)) activity.SetTag(name, null);
        foreach (var (name, _) in activity.Baggage.ToArray())
            if (IsSensitive(name)) activity.SetBaggage(name, null);
        StripQuery(activity, "url.full");
        StripQuery(activity, "http.url");
    }

    private static bool IsSensitive(string name)
    {
        var normalized = name.ToLowerInvariant().Replace('-', '_').Replace('.', '_');
        return normalized.Contains("thread_token", StringComparison.Ordinal)
            || normalized.Contains("idempotency", StringComparison.Ordinal)
            || normalized.Contains("evaluation_admission", StringComparison.Ordinal)
            || normalized.EndsWith("_body", StringComparison.Ordinal);
    }

    private static void StripQuery(Activity activity, string name)
    {
        if (activity.GetTagItem(name) is not string value) return;
        var query = value.IndexOf('?');
        if (query >= 0) activity.SetTag(name, value[..query]);
    }
}
