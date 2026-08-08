using System.Text.Json.Nodes;
using Lex.Index;

namespace Lex.RetrievalExperiment;

public static class EnrichmentSampleAnalyzer
{
    private static readonly double[] Thresholds = [0.8, 0.85, 0.9, 0.925, 0.95];

    public static JsonObject Analyze(string reportPath, string modelDirectory)
    {
        var source = JsonNode.Parse(File.ReadAllBytes(reportPath))?.AsObject()
            ?? throw new InvalidDataException("Enrichment report is empty.");
        var deployment = source["model_deployment"]?.GetValue<string>()
            ?? throw new InvalidDataException("Enrichment report has no model deployment.");
        var promptHash = source["prompt_hash"]?.GetValue<string>()
            ?? throw new InvalidDataException("Enrichment report has no prompt hash.");
        using var encoder = MultilingualE5Encoder.Open(modelDirectory);
        var workAnalyses = new JsonArray();
        foreach (var work in source["works"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            if (work["status"]?.GetValue<string>() != "completed") continue;
            var workId = work["work"]?.GetValue<string>()
                ?? throw new InvalidDataException("Completed work has no identifier.");
            var language = work["language"]?.GetValue<string>()
                ?? throw new InvalidDataException($"{workId} has no language.");
            var version = work["version"]?.GetValue<string>()
                ?? throw new InvalidDataException($"{workId} has no version.");
            var held = HeldEvidence(work, workId, version);
            var runs = work["runs"]?.AsArray().OfType<JsonObject>().Select(run =>
            {
                var raw = run["raw"]?.GetValue<string>()
                    ?? throw new InvalidDataException($"{workId} run has no raw response.");
                return ModelDiscoveryParser.Parse(raw, workId, version, held).Candidates;
            }).Cast<IReadOnlyList<ModelDiscoveryCandidate>>().ToArray()
                ?? throw new InvalidDataException($"{workId} has no runs.");
            var thresholdAnalyses = new JsonArray();
            foreach (var threshold in Thresholds)
            {
                var matches = SemanticProposalMatcher.Match(
                    workId, language, runs, deployment, promptHash, encoder, threshold);
                var validation = EnrichmentContract.Validate(matches, held);
                thresholdAnalyses.Add(new JsonObject
                {
                    ["minimum_cosine"] = threshold,
                    ["matched"] = matches.Count,
                    ["accepted"] = JsonNode.Parse(EnrichmentContract.Export(validation.Accepted)),
                    ["rejected"] = new JsonArray(validation.Rejected.Select(item => (JsonNode)new JsonObject
                    {
                        ["kind"] = item.Proposal.Kind,
                        ["value"] = item.Proposal.Value,
                        ["reason"] = item.Reason,
                    }).ToArray()),
                });
            }
            workAnalyses.Add(new JsonObject
            {
                ["work"] = workId,
                ["language"] = language,
                ["thresholds"] = thresholdAnalyses,
            });
        }
        return new JsonObject
        {
            ["schema"] = "lex-enrichment-semantic-consensus-analysis/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["source_report"] = Path.GetFileName(reportPath),
            ["source_report_sha256"] = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(reportPath))),
            ["embedding_model"] = encoder.ModelId,
            ["embedding_revision"] = encoder.ModelRevision,
            ["embedding_sha256"] = encoder.ModelSha256,
            ["tokenizer_sha256"] = encoder.TokenizerSha256,
            ["works"] = workAnalyses,
        };
    }

    private static HashSet<EvidenceAnchor> HeldEvidence(JsonObject work, string workId, string version)
    {
        var held = new HashSet<EvidenceAnchor>();
        foreach (var proposal in work["proposals"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            foreach (var evidence in proposal["evidence"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                var anchor = evidence["anchor"]?.GetValue<string>();
                var sha = evidence["text_sha256"]?.GetValue<string>();
                if (anchor is not null && sha is not null)
                    held.Add(new EvidenceAnchor(workId, version, anchor, sha));
            }
        }
        if (held.Count == 0) throw new InvalidDataException($"{workId} has no held evidence in the source report.");
        return held;
    }
}
