using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The requests whose whole answers a mount of an earlier index schema must keep, byte for byte, as the code without index 9
/// sends them: each request's exact body and trace identity, answered at one fixed observation time by the real handler.
/// The answers were recorded at the integration head #980 was based on (5a659831), then again at b1b41312 (search results by
/// act, on 8e6d3d27, whose two schema-8 search answers gained additive members and a ranking sentence for the new order),
/// then at 34d493d0 (freshness and the commits, plan 6.1 and 6.5, on 0b908870: every envelope's context gained `commits`
/// and the freshness header's new members, and nothing else moved), then at 496eeced (the owner's maximum data age of 30
/// days, O11, 2026-10-19: every answer's `maximum_data_age_days`, 10 to 30, and nothing else), then on data/yield-per-class
/// (the text yield per class, R86(4): each mount's coverage answer gained `text_yield`, null, since neither mount has a
/// build report stating one, and `text_yield_note`, and nothing else moved; the two bodies replaced, every other byte kept),
/// over the committed schema-8 mount (written at 3b7913ec) and the schema-6 one, by this same code, on Linux as the release
/// image runs (globalization-invariant, UTC), with <c>V3CorpusLegacySchema8MountTests.TheAnswersAreRecordedWhereTheRecordingAsks</c>,
/// and every system is held to them (a rooted path reads alike everywhere since #983); a later change that moves one byte
/// of an answer over those schemas fails <c>V3CorpusLegacySchema8MountTests</c>.
/// </summary>
internal static class LegacyMountAnswers
{
    internal static readonly DateTimeOffset ObservedAt = new(2026, 10, 15, 7, 0, 0, TimeSpan.Zero);

    private const string Act = "/lu-legilux/loi-2026-01-01-a1";
    private const string Date = "2026-02-03";

    /// <summary>The schema-8 mount's one state's permalink (state digest /1, which no corpus reference moves).</summary>
    private const string Permalink = Act + "/2026-02-03--084f808885bf9c974e93c79efa6ba2f7d8503b1e9f1970a5ddadbe6bd77e6784/fra";

    /// <summary>
    /// The schema-8 mount's one act, asked of every operation that reads Luxembourg states, and the mount as a whole;
    /// as_observed names the mount's own index digest as its snapshot.
    /// </summary>
    internal static IReadOnlyList<(string Operation, object Parameters)> Schema8Requests(string indexSha256) =>
    [
        ("coverage", new { }),
        ("resolve", new { identifier = Act }),
        ("resolve", new { identifier = Permalink }),
        ("as_of", new { identifier = Act, date = Date, language = "fra" }),
        ("as_of", new { identifier = Permalink, date = Date, language = "fra" }),
        ("as_of", new { identifier = Act, date = "2026-01-15", language = "fra" }),
        ("in_force_on", new { date = Date }),
        ("evidence_bundle", new { identifier = Act, date = Date, language = "fra" }),
        ("provenance", new { identifier = Act, date = Date, language = "fra" }),
        ("events", new { }),
        ("timeline", new { identifier = Act }),
        ("as_observed", new { identifier = Act, date = Date, language = "fra", snapshot = indexSha256 }),
        ("as_observed", new { identifier = Act, date = Date, language = "fra", at = "2026-10-01T00:00:00Z" }),
        ("search", new { query = "synthétique", language = "fra" }),
        ("search", new { query = "synthétique", language = "fra", date = Date }),
        ("browse", new { }),
        ("dossier", new { identifier = Act }),
        ("status_on", new { identifier = Act, date = Date }),
        ("article_history", new { identifier = Act, anchor = "art_1er" }),
        ("diff", new { identifier = Act, date_from = Date, date_to = "2026-03-01" }),
        ("answer_drift", new { }),
        ("changes_in_period", new { date_from = "2026-01-01", date_to = "2026-12-31" }),
        ("relations", new { identifier = Act }),
        ("citation", new { identifier = Act, date = Date }),
        ("cited_by", new { identifier = Act }),
        ("classification", new { identifier = Act }),
        ("verify", new { identifier = Act }),
        ("verify", new { identifier = Permalink }),
        ("manifestation", new { identifier = Act }),
    ];

    private const string RealWork = "/lu-legilux/loi-2017-03-14-a439";

    /// <summary>The real bounded first mount (schema 6): the mount as a whole, its one work, and as_observed's refusal.</summary>
    internal static IReadOnlyList<(string Operation, object Parameters)> Schema6Requests { get; } =
    [
        ("coverage", new { }),
        ("events", new { }),
        ("as_observed", new { identifier = RealWork, date = "2017-03-20", snapshot = "c7f405486cb3603babf23fdd3e94f03ba9ecb46f775d0302bf1935350d7b3e1a" }),
        ("resolve", new { identifier = RealWork }),
        ("browse", new { }),
        ("search", new { query = "Loi du 14 mars 2017", language = "fra" }),
        ("dossier", new { identifier = RealWork }),
        ("timeline", new { identifier = RealWork }),
        ("answer_drift", new { }),
    ];

    /// <summary>Each request's body, trace identity, HTTP status and whole response body, as the handler answered.</summary>
    internal static async Task<JsonArray> RecordAsync(V3CorpusMount mount, IReadOnlyList<(string Operation, object Parameters)> requests, string tracePrefix)
    {
        var rows = new JsonArray();
        for (var index = 0; index < requests.Count; index++)
        {
            var (operation, parameters) = requests[index];
            var request = JsonSerializer.Serialize(new { operation_id = operation, parameters });
            var trace = $"{tracePrefix}-{index:D2}";
            var (status, body) = await RespondAsync(mount, operation, request, trace);
            rows.Add(new JsonObject
            {
                ["operation"] = operation,
                ["request"] = request,
                ["trace"] = trace,
                ["http_status"] = status,
                ["body"] = body,
            });
        }

        return rows;
    }

    /// <summary>One REST request with the given body and trace identity, answered by the real handler at <see cref="ObservedAt"/>.</summary>
    internal static async Task<(int Status, string Body)> RespondAsync(V3CorpusMount mount, string operation, string request, string trace)
    {
        var bytes = Encoding.UTF8.GetBytes(request);
        var context = new DefaultHttpContext { TraceIdentifier = trace };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }
}
