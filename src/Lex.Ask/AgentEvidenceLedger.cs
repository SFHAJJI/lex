using System.Text.Json.Nodes;
using Lex.Mcp;

namespace Lex.Ask;

internal sealed class AgentEvidenceLedger
{
    private const int MaxEvidenceItems = 64;
    private const int MaxEvidenceChars = 96_000;

    private readonly List<AgentEvidence> _evidence = [];
    private int _evidenceChars;
    private int _call;

    public IReadOnlyList<AgentEvidence> Evidence => _evidence;

    public void Observe(
        string tool,
        string? status,
        JsonArray docs,
        JsonNode? result = null,
        JsonObject? arguments = null)
    {
        var call = ++_call;
        var ordinal = 0;
        if (string.Equals(status, LegalOperationStatus.PartialResult, StringComparison.Ordinal))
        {
            var limitations = PublisherLimitationPolicy.FromResult(tool, result);
            if (limitations.Count > 0)
                Add(tool, call, ordinal++, AgentEvidenceKind.Coverage,
                    null, null, null, null, null, true, null,
                    PublisherLimitationPayload(LegalOperationStatus.PartialResult, limitations));
        }
        if (status is not null && LegalOperationPolicy.OutcomeForStatus(status) is
            LegalOutcome.NeedsClarification or LegalOutcome.NotAvailable
                or LegalOutcome.NotComparable or LegalOutcome.NotFound)
        {
            var limitations = PublisherLimitationPolicy.FromResult(tool, result);
            var payload = status == McpStatus.FilterNotSupportedByIndex
                          && limitations.Count > 0
                ? PublisherLimitationPayload(status, limitations)
                : EvidencePayload(result, status);
            Add(tool, call, 0, AgentEvidenceKind.Coverage, null, null, null, null, null, true,
                null, payload);
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

        if (kind == AgentEvidenceKind.Ranking && result is not null)
            foreach (var aggregate in RankingAggregates(result))
                Add(tool, call, ordinal++, AgentEvidenceKind.Ranking, null, null, null, null, null,
                    false, $"{aggregate.Publisher} aggregate ranking result",
                    EvidencePayload(aggregate.Payload, status));
        // For a ranking the counts are the evidence; for a comparison the OUTCOME is, and it sits
        // on the result object rather than on either dated document. Without this row a synthesis
        // asked what changed receives two versions and no comparison between them, so it correctly
        // refuses; and the polarity contract that exists to stop it inverting a change is handed
        // zero change facts and passes everything.
        if (tool == "diff" && result is JsonObject comparison)
            Add(tool, call, ordinal++, AgentEvidenceKind.Change,
                WorkKey(comparison["work"]?.GetValue<string>()),
                comparison["anchor"]?.GetValue<string>(), null, null, null, false,
                "verified comparison outcome", ComparisonOutcome(comparison, arguments, status));
        foreach (var doc in docs.OfType<JsonObject>())
        {
            var work = WorkKey(doc["lex_id"]?.GetValue<string>());
            var date = doc["valid_from"]?.GetValue<string>();
            if (doc["pinpoints"] is JsonArray pinpoints && pinpoints.Count > 0)
            {
                var requestedDate = tool == "as_of"
                    ? (arguments?["date"] ?? arguments?["as_of"])?.GetValue<string>()
                    : null;
                if (!string.IsNullOrWhiteSpace(requestedDate))
                    Add(tool, call, ordinal++, AgentEvidenceKind.Timeline, work, null,
                        requestedDate, null, doc["permalink"]?.GetValue<string>(), false,
                        doc["title"]?.GetValue<string>(),
                        AsOfBindingPayload(doc, result, requestedDate, status));
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
                        tool == "as_of" && string.IsNullOrWhiteSpace(excerpt)
                            ? requestedDate ?? date : date,
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

    private static string PublisherLimitationPayload(
        string status,
        IReadOnlyList<PublisherLimitationView> limitations) => new JsonObject
    {
        ["status"] = status,
        ["publisher_limitations"] = new JsonArray(limitations.Select(item =>
            (JsonNode)new JsonObject
            {
                ["status"] = item.Status,
                ["tool"] = item.Tool,
                ["publisher"] = item.Publisher,
                ["jurisdiction"] = item.Jurisdiction,
                ["unsupported_filters"] = new JsonArray(item.UnsupportedFilters
                    .Select(filter => (JsonNode)filter).ToArray()),
            }).ToArray()),
    }.ToJsonString();

    /// <summary>
    /// The verified comparison, carried as the typed fields rather than the whole tool response.
    ///
    /// <para>Both resolved documents already travel as their own evidence rows, so repeating them
    /// here would spend the ledger's budget on text the synthesis has twice. What is added is the
    /// part nothing else carries: which dates were asked for, which publisher versions answered,
    /// and how the comparison came out. The requested dates are named because the served version
    /// is legitimately older than the date asked about, and a synthesis that cites only
    /// <c>valid_from</c> reads as an answer about the wrong day.</para>
    /// </summary>
    private static string ComparisonOutcome(
        JsonObject result,
        JsonObject? arguments,
        string? status)
    {
        var payload = new JsonObject
        {
            ["status"] = status,
            ["work"] = result["work"]?.DeepClone(),
            ["requested_from_date"] = arguments?["from_date"]?.DeepClone(),
            ["requested_to_date"] = arguments?["to_date"]?.DeepClone(),
            ["from_valid_from"] = (result["from"] as JsonObject)?["valid_from"]?.DeepClone(),
            ["to_valid_from"] = (result["to"] as JsonObject)?["valid_from"]?.DeepClone(),
        };
        // Copied only when present. An absent anchor_text_equal means the wording was not
        // compared, and writing an explicit null would offer the synthesis a fact to read as
        // "not equal".
        foreach (var key in new[]
                 {
                     "changed", "provision_level_comparable", "anchor", "anchor_from_present",
                     "anchor_to_present", "anchor_text_equal", "note",
                 })
            if (result[key] is { } value) payload[key] = value.DeepClone();
        return payload.ToJsonString();
    }

    private static string AsOfBindingPayload(
        JsonObject doc,
        JsonNode? result,
        string requestedDate,
        string? status)
    {
        var semantics = result is JsonObject obj
            ? obj["envelope"]?["timeline_semantics"]?.GetValue<string>()
            : null;
        return new JsonObject
        {
            ["status"] = status,
            ["requested_date"] = requestedDate,
            ["selected_valid_from"] = doc["valid_from"]?.DeepClone(),
            ["selected_valid_to"] = doc["valid_to"]?.DeepClone(),
            ["timeline_semantics"] = semantics,
            ["relation"] = "selected publisher version covers requested date",
        }.ToJsonString();
    }

    private static IEnumerable<(string Publisher, JsonObject Payload)> RankingAggregates(JsonNode result)
    {
        var entries = (result switch
        {
            JsonArray array => array.OfType<JsonObject>(),
            JsonObject item => [item],
            _ => [],
        }).ToArray();
        var hasTypedPublisherStatus = entries.Any(entry =>
            LegalOperationPolicy.StatusForPublisherResult(entry) is not null);
        foreach (var entry in entries)
        {
            if (hasTypedPublisherStatus
                && !LegalOperationPolicy.IsProvenSuccessfulPublisherResult(
                    entry, "changes_in_period"))
                continue;
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
