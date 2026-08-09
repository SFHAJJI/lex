using System.Text.Json.Nodes;

namespace Lex.Ask;

internal sealed class AgentEvidenceLedger
{
    private const int MaxEvidenceItems = 64;
    private const int MaxEvidenceChars = 96_000;
    private static readonly HashSet<string> GapStatuses = new(StringComparer.Ordinal)
    {
        "no_corpus_mounted",
        "anchor_not_in_version",
        "no_version_for_date",
        "no_provision_history",
        "outside_observed_window",
        "text_not_available",
        "text_withheld",
        "unknown_work",
        "unknown_anchor",
        "unavailable",
    };

    private readonly List<AgentEvidence> _evidence = [];
    private int _evidenceChars;
    private int _call;

    public IReadOnlyList<AgentEvidence> Evidence => _evidence;

    public void Observe(string tool, string? status, JsonArray docs, JsonNode? result = null)
    {
        var call = ++_call;
        if (status is not null && GapStatuses.Contains(status))
        {
            Add(tool, call, 0, AgentEvidenceKind.Coverage, null, null, null, null, null, true,
                null, EvidencePayload(result, status));
            return;
        }

        if (tool == "coverage")
        {
            Add(tool, call, 0, AgentEvidenceKind.Coverage, null, null, null, null, null, false,
                null, EvidencePayload(result, status));
            return;
        }

        var kind = tool switch
        {
            "search" => AgentEvidenceKind.Pointer,
            "timeline" or "in_force_on" => AgentEvidenceKind.Timeline,
            "diff" or "article_history" => AgentEvidenceKind.Change,
            "changes_in_period" => AgentEvidenceKind.Ranking,
            "provenance" or "cited_by" => AgentEvidenceKind.Provenance,
            "as_of" => AgentEvidenceKind.Timeline,
            _ => (AgentEvidenceKind?)null,
        };
        if (kind is null) return;

        var ordinal = 0;
        if (kind == AgentEvidenceKind.Ranking && result is not null)
            foreach (var aggregate in RankingAggregates(result))
                Add(tool, call, ordinal++, AgentEvidenceKind.Ranking, null, null, null, null, null,
                    false, $"{aggregate.Publisher} aggregate ranking result",
                    EvidencePayload(aggregate.Payload, status));
        foreach (var doc in docs.OfType<JsonObject>())
        {
            var work = WorkKey(doc["lex_id"]?.GetValue<string>());
            var date = doc["valid_from"]?.GetValue<string>();
            if (doc["pinpoints"] is JsonArray pinpoints && pinpoints.Count > 0)
            {
                foreach (var pinpoint in pinpoints.OfType<JsonObject>())
                {
                    var excerpt = FullExcerpt(result, doc["lex_id"]?.GetValue<string>(),
                                      pinpoint["anchor"]?.GetValue<string>())
                                  ?? pinpoint["quote"]?.GetValue<string>();
                    Add(tool, call, ordinal++,
                        tool == "as_of" && !string.IsNullOrWhiteSpace(excerpt)
                            ? AgentEvidenceKind.LegalText : kind.Value,
                        work,
                        pinpoint["anchor"]?.GetValue<string>(),
                        date,
                        pinpoint["text_sha256"]?.GetValue<string>(),
                        pinpoint["permalink"]?.GetValue<string>() ?? doc["permalink"]?.GetValue<string>(),
                        false,
                        doc["title"]?.GetValue<string>(),
                        excerpt);
                }
                continue;
            }

            Add(tool, call, ordinal++, kind.Value, work,
                doc["anchor"]?.GetValue<string>(), date, doc["text_sha256"]?.GetValue<string>(),
                doc["permalink"]?.GetValue<string>(), false,
                doc["title"]?.GetValue<string>(), kind == AgentEvidenceKind.Ranking
                    ? EvidencePayload(doc, doc["snippet"]?.GetValue<string>())
                    : doc["snippet"]?.GetValue<string>());
        }
    }

    private void Add(
        string tool,
        int call,
        int ordinal,
        AgentEvidenceKind kind,
        string? work,
        string? anchor,
        string? date,
        string? textSha256,
        string? permalink,
        bool disclosure,
        string? title,
        string? excerpt)
    {
        var item = new AgentEvidence(
            $"{tool}:{call}:{ordinal}", kind,
            Bounded(work, 300), Bounded(anchor, 300), Bounded(date, 50),
            Bounded(textSha256, 128), Bounded(permalink, 2_048), disclosure,
            Bounded(title, 500), Bounded(excerpt, 8_000));
        _evidence.Add(item);
        _evidenceChars += CharacterCount(item);
        while (_evidence.Count > MaxEvidenceItems || _evidenceChars > MaxEvidenceChars)
        {
            _evidenceChars -= CharacterCount(_evidence[0]);
            _evidence.RemoveAt(0);
        }
    }

    private static string? Bounded(string? value, int maximum) =>
        value is null || value.Length <= maximum ? value : value[..maximum];

    private static int CharacterCount(AgentEvidence item) =>
        item.Id.Length + (item.Work?.Length ?? 0) + (item.Anchor?.Length ?? 0)
        + (item.Date?.Length ?? 0) + (item.TextSha256?.Length ?? 0)
        + (item.Permalink?.Length ?? 0) + (item.Title?.Length ?? 0)
        + (item.Excerpt?.Length ?? 0);

    private static string? WorkKey(string? value)
    {
        if (value is null) return null;
        var parts = value.Split(':');
        return parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : value;
    }

    private static string? EvidencePayload(JsonNode? result, string? fallback)
    {
        if (result is null) return fallback;
        var json = result.ToJsonString();
        return json.Length <= 8_000 ? json : json[..8_000];
    }

    private static IEnumerable<(string Publisher, JsonObject Payload)> RankingAggregates(JsonNode result)
    {
        var entries = result switch
        {
            JsonArray array => array.OfType<JsonObject>(),
            JsonObject item => [item],
            _ => [],
        };
        foreach (var entry in entries)
        {
            var aggregate = new JsonObject();
            foreach (var key in new[]
                     {
                         "envelope", "window", "from_date", "to_date", "order",
                         "works_changed", "new_versions", "shown", "offset",
                     })
                if (entry[key] is { } value) aggregate[key] = value.DeepClone();
            var publisher = entry["envelope"]?["publisher"]?.GetValue<string>() ?? "collection";
            yield return (publisher, aggregate);
        }
    }

    private static string? FullExcerpt(JsonNode? result, string? lexId, string? anchor)
    {
        if (result is null || anchor is null) return null;
        string? found = null;
        static string? ProvisionText(JsonNode? node, string expectedAnchor)
        {
            if (node is not JsonArray provisions) return null;
            foreach (var provision in provisions.OfType<JsonObject>())
                if (provision["anchor"]?.GetValue<string>() == expectedAnchor
                    && (provision["text"] ?? provision["text_md"])?.GetValue<string>() is { } text)
                    return text.Length <= 8_000 ? text : text[..8_000];
            return null;
        }
        void Walk(JsonNode? node)
        {
            if (found is not null) return;
            switch (node)
            {
                case JsonObject obj:
                    var candidate = (obj["document"]?["lex_id"] ?? obj["lex_id"])?.GetValue<string>();
                    if (lexId is null || candidate == lexId)
                        found = ProvisionText(obj["provisions"], anchor);
                    if (found is null)
                        foreach (var property in obj.Where(property => property.Key != "provisions"))
                            Walk(property.Value);
                    break;
                case JsonArray array:
                    foreach (var item in array) Walk(item);
                    break;
            }
        }
        Walk(result);
        return found;
    }
}
