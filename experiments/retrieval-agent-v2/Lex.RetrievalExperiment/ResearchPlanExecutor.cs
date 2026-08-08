using System.Text.Json.Nodes;
using Lex.Index;
using Lex.Mcp;

namespace Lex.RetrievalExperiment;

public sealed class ResearchPlanExecutor : IDisposable
{
    private const double MinimumDiscoverySimilarity = 0.92;
    private readonly string _workIndexPath;
    private readonly string _workVectorPath;
    private readonly ITextEncoder _encoder;
    private readonly IReadOnlyDictionary<string, LexIndexReader> _readers;
    private readonly McpCore _mcp;

    public ResearchPlanExecutor(
        string workIndexPath,
        string workVectorPath,
        string indexDirectory,
        string modelDirectory)
    {
        _workIndexPath = Path.GetFullPath(workIndexPath);
        _workVectorPath = Path.GetFullPath(workVectorPath);
        _encoder = MultilingualE5Encoder.Open(modelDirectory);
        var readers = new Dictionary<string, LexIndexReader>(StringComparer.Ordinal);
        try
        {
            foreach (var index in Directory.GetFiles(indexDirectory, "index-*.db").Order(StringComparer.Ordinal))
            {
                var vector = Path.ChangeExtension(index, ".vectors");
                var reader = File.Exists(vector)
                    ? LexIndexReader.Open(index, _encoder, vector)
                    : LexIndexReader.Open(index);
                readers.Add(reader.Collection, reader);
            }
        }
        catch
        {
            foreach (var reader in readers.Values) reader.Dispose();
            _encoder.Dispose();
            throw;
        }
        _readers = readers;
        _mcp = new McpCore(readers);
    }

    public JsonObject Execute(LegalResearchPlan rawPlan, DateOnly today, string? conversationInput = null)
    {
        var plan = ResearchPlanContract.Validate(rawPlan);
        if (plan.Action == "clarify")
            return new JsonObject
            {
                ["status"] = "clarification_required",
                ["clarification"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(
                    plan.Clarification, ResearchPlanContract.JsonOptions)),
                ["tool_calls"] = new JsonArray(),
            };

        var candidates = ResolveCandidates(plan);
        var resolved = candidates.FirstOrDefault(candidate => IsResolvedCandidate(
            candidate.Exact, candidate.DiscoveryMatches, candidate.MaximumSemanticSimilarity));
        var candidateWorks = resolved is null
            ? candidates.Select(candidate => candidate.Work).Distinct(StringComparer.Ordinal).Take(12).ToArray()
            : [resolved.Work];
        var toolCalls = new JsonArray();
        var articleRanks = new Dictionary<string, double>(StringComparer.Ordinal);
        var firstAnchors = new Dictionary<string, string>(StringComparer.Ordinal);
        var searchBudget = new SearchBudget(2);
        foreach (var query in plan.ProvisionQueries.Take(2))
        {
            searchBudget.Consume(query.Query);
            var args = new JsonObject
            {
                ["query"] = query.Query,
                ["language"] = query.Language,
                ["retrieval_mode"] = "hybrid",
                ["fuzzy"] = "auto",
                ["limit"] = 12,
            };
            if (candidateWorks.Length > 0) args["works"] = string.Join(',', candidateWorks);
            if (plan.Jurisdiction is "EU" or "LU") args["jurisdiction"] = plan.Jurisdiction;
            if (plan.AsOf is not null)
            {
                args["time_scope"] = "as_of";
                args["as_of"] = plan.AsOf;
            }
            var result = _mcp.CallTool("search", args);
            toolCalls.Add(new JsonObject
            {
                ["tool"] = "search",
                ["args"] = args.DeepClone(),
                ["result"] = result.DeepClone(),
            });
            foreach (var hit in SearchHits(result))
            {
                articleRanks[hit.Work] = articleRanks.GetValueOrDefault(hit.Work)
                    + 1d / (60 + hit.Rank);
                if (hit.Anchor is not null) firstAnchors.TryAdd(hit.Work, hit.Anchor);
            }
        }

        var selected = resolved is not null && articleRanks.ContainsKey(resolved.Work) ? resolved : null;
        JsonNode? validation = null;
        if (selected is not null)
        {
            var date = plan.AsOf ?? today.ToString("yyyy-MM-dd");
            var anchor = WorkQueryIntent.ArticleAnchor(conversationInput ?? "")
                         ?? WorkQueryIntent.ConsensusArticleAnchor(
                             plan.ProvisionQueries.Select(query => query.Query))
                         ?? firstAnchors.GetValueOrDefault(selected.Work);
            var args = new JsonObject
            {
                ["work"] = selected.Work,
                ["date"] = date,
                ["language"] = selected.Language,
                ["mode"] = anchor is null ? "outline" : "select",
            };
            if (anchor is not null) args["anchors"] = anchor;
            validation = _mcp.CallTool("as_of", args);
            toolCalls.Add(new JsonObject
            {
                ["tool"] = "as_of",
                ["args"] = args.DeepClone(),
                ["result"] = validation.DeepClone(),
            });
        }

        return new JsonObject
        {
            ["status"] = selected is null ? "no_validated_candidate" : "validated_candidate",
            ["minimum_discovery_similarity"] = MinimumDiscoverySimilarity,
            ["candidate_works"] = new JsonArray(candidates.Take(10).Select(candidate => (JsonNode)new JsonObject
            {
                ["work"] = candidate.Work,
                ["language"] = candidate.Language,
                ["title"] = candidate.Title,
                ["score"] = candidate.Score,
                ["query_matches"] = candidate.QueryMatches,
                ["discovery_matches"] = candidate.DiscoveryMatches,
                ["maximum_semantic_similarity"] = candidate.MaximumSemanticSimilarity,
                ["exact"] = candidate.Exact,
                ["reasons"] = new JsonArray(candidate.Reasons.Select(reason => (JsonNode)reason).ToArray()),
            }).ToArray()),
            ["selected_work"] = selected?.Work,
            ["selected_anchor"] = selected is null
                ? null : WorkQueryIntent.ArticleAnchor(conversationInput ?? "")
                         ?? WorkQueryIntent.ConsensusArticleAnchor(
                             plan.ProvisionQueries.Select(query => query.Query))
                         ?? firstAnchors.GetValueOrDefault(selected.Work),
            ["validation_status"] = ValidationStatus(validation),
            ["tool_calls"] = toolCalls,
        };
    }

    private IReadOnlyList<PlanCandidate> ResolveCandidates(LegalResearchPlan plan)
    {
        const int rrfK = 60;
        var scores = new Dictionary<string, CandidateAccumulator>(StringComparer.Ordinal);
        foreach (var query in plan.Queries)
        {
            var lexical = WorkSearchIndex.Search(_workIndexPath, query.Query, query.Language, 100);
            var semantic = WorkVectorIndex.Search(
                _workIndexPath, _workVectorPath, _encoder, query.Query, query.Language, 100);
            var semanticByWork = semantic.ToDictionary(hit => hit.Work, StringComparer.Ordinal);
            var hits = WorkHybridSearch.Fuse(lexical, semantic, 100)
                .Where(hit => InJurisdiction(hit.Work, plan.Jurisdiction)).ToArray();
            for (var rank = 0; rank < hits.Length; rank++)
            {
                var hit = hits[rank];
                if (!scores.TryGetValue(hit.Work, out var value))
                    scores[hit.Work] = value = new CandidateAccumulator(hit.Language, hit.Title);
                value.Score += 1d / (rrfK + rank + 1);
                value.QueryMatches++;
                value.Exact |= hit.Reason.StartsWith("exact_", StringComparison.Ordinal)
                               || hit.Reason.StartsWith("contained_", StringComparison.Ordinal);
                if (hit.Reason.Contains("semantic_concept", StringComparison.Ordinal))
                    value.DiscoveryMatches++;
                if (semanticByWork.TryGetValue(hit.Work, out var semanticHit))
                    value.MaximumSemanticSimilarity = Math.Max(
                        value.MaximumSemanticSimilarity, semanticHit.Score);
                value.Reasons.Add(hit.Reason);
            }
        }
        return scores.Select(item => new PlanCandidate(
                item.Key,
                item.Value.Language,
                item.Value.Title,
                item.Value.Score,
                item.Value.QueryMatches,
                item.Value.DiscoveryMatches,
                item.Value.MaximumSemanticSimilarity,
                item.Value.Exact,
                item.Value.Reasons.Order(StringComparer.Ordinal).ToArray()))
            .OrderByDescending(candidate => candidate.Exact)
            .ThenByDescending(candidate => candidate.DiscoveryMatches > 0)
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Work, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool InJurisdiction(string work, string? jurisdiction) => jurisdiction switch
    {
        "EU" => work.StartsWith("eu-eurlex:", StringComparison.Ordinal),
        "LU" => work.StartsWith("lu-legilux:", StringComparison.Ordinal),
        _ => true,
    };

    public static bool IsResolvedCandidate(bool exactName, int discoveryMatches, double similarity) =>
        exactName || discoveryMatches > 0 && similarity >= MinimumDiscoverySimilarity;

    private static IEnumerable<SearchHit> SearchHits(JsonNode result)
    {
        if (result is not JsonArray publishers) yield break;
        foreach (var publisherResult in publishers.OfType<JsonObject>())
        {
            var publisher = publisherResult["envelope"]?["publisher"]?.GetValue<string>();
            if (publisher is null || publisherResult["hits"] is not JsonArray hits) continue;
            var rank = 0;
            foreach (var hit in hits.OfType<JsonObject>())
            {
                rank++;
                var work = hit["work"]?.GetValue<string>();
                if (work is null) continue;
                yield return new SearchHit(
                    $"{publisher}:{work}",
                    hit["anchor"]?.GetValue<string>(),
                    rank);
            }
        }
    }

    private static string? ValidationStatus(JsonNode? validation) =>
        (validation?["envelope"]?["status"] ?? validation?["status"])?.GetValue<string>();

    public void Dispose()
    {
        foreach (var reader in _readers.Values) reader.Dispose();
        _encoder.Dispose();
    }

    private sealed class CandidateAccumulator(string language, string? title)
    {
        public string Language { get; } = language;
        public string? Title { get; } = title;
        public double Score { get; set; }
        public int QueryMatches { get; set; }
        public int DiscoveryMatches { get; set; }
        public double MaximumSemanticSimilarity { get; set; } = -1;
        public bool Exact { get; set; }
        public HashSet<string> Reasons { get; } = new(StringComparer.Ordinal);
    }

    private sealed record SearchHit(string Work, string? Anchor, int Rank);
    private sealed record PlanCandidate(
        string Work,
        string Language,
        string? Title,
        double Score,
        int QueryMatches,
        int DiscoveryMatches,
        double MaximumSemanticSimilarity,
        bool Exact,
        IReadOnlyList<string> Reasons);
}
