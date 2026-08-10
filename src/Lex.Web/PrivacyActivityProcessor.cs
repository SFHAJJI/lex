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
        StripQuery(activity, "url.full");
        StripQuery(activity, "http.url");
    }

    private static void StripQuery(Activity activity, string name)
    {
        if (activity.GetTagItem(name) is not string value) return;
        var query = value.IndexOf('?');
        if (query >= 0) activity.SetTag(name, value[..query]);
    }
}
