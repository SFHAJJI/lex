using System.Text.Json.Nodes;

namespace Lex.RetrievalExperiment;

public sealed record ModelItemRejection(int Item, string Reason);

public sealed record ModelDiscoveryParseResult(
    IReadOnlyList<ModelDiscoveryCandidate> Candidates,
    IReadOnlyList<ModelItemRejection> RejectedItems);

public static class ModelDiscoveryParser
{
    private static readonly HashSet<string> ItemFields = new(StringComparer.Ordinal)
    {
        "kind", "value", "confidence", "evidence",
    };

    private static readonly HashSet<string> EvidenceFields = new(StringComparer.Ordinal)
    {
        "anchor", "text_sha256",
    };

    public static ModelDiscoveryParseResult Parse(
        string raw,
        string work,
        string version,
        IReadOnlySet<EvidenceAnchor> held)
    {
        if (JsonNode.Parse(raw) is not JsonObject root)
            throw new InvalidDataException("Model enrichment JSON must be an object.");
        if (root.Count != 1 || root["items"] is not JsonArray items)
            throw new InvalidDataException("Model enrichment JSON must contain only an items array.");

        var byAnchor = held.GroupBy(item => (item.Anchor, item.TextSha256))
            .ToDictionary(group => group.Key, group => group.First());
        var candidates = new List<ModelDiscoveryCandidate>();
        var rejected = new List<ModelItemRejection>();
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (items[itemIndex] is not JsonObject item)
            {
                rejected.Add(new ModelItemRejection(itemIndex, "item_not_object"));
                continue;
            }
            if (item.Any(property => !ItemFields.Contains(property.Key)))
            {
                rejected.Add(new ModelItemRejection(itemIndex, "unknown_item_field"));
                continue;
            }
            if (!TryString(item["kind"], out var kind) || !TryString(item["value"], out var value))
            {
                rejected.Add(new ModelItemRejection(itemIndex, "invalid_kind_or_value"));
                continue;
            }
            if (item["confidence"] is not JsonValue confidenceNode
                || !confidenceNode.TryGetValue<double>(out var confidence))
            {
                rejected.Add(new ModelItemRejection(itemIndex, "confidence_not_number"));
                continue;
            }
            if (item["evidence"] is not JsonArray evidenceNodes || evidenceNodes.Count == 0)
            {
                rejected.Add(new ModelItemRejection(itemIndex, "evidence_missing"));
                continue;
            }

            var evidence = new List<EvidenceAnchor>();
            var evidenceValid = true;
            foreach (var evidenceNode in evidenceNodes)
            {
                if (evidenceNode is not JsonObject evidenceObject
                    || evidenceObject.Any(property => !EvidenceFields.Contains(property.Key))
                    || !TryString(evidenceObject["anchor"], out var anchor)
                    || !TryString(evidenceObject["text_sha256"], out var sha)
                    || !byAnchor.TryGetValue((anchor!, sha!), out var heldItem))
                {
                    evidenceValid = false;
                    break;
                }
                evidence.Add(heldItem with { Work = work, Version = version });
            }
            if (!evidenceValid)
            {
                rejected.Add(new ModelItemRejection(itemIndex, "evidence_not_held"));
                continue;
            }
            candidates.Add(new ModelDiscoveryCandidate(kind!, value!, confidence,
                evidence.Distinct().OrderBy(entry => entry.Anchor, StringComparer.Ordinal).ToArray()));
        }
        return new ModelDiscoveryParseResult(candidates, rejected);
    }

    private static bool TryString(JsonNode? node, out string? value)
    {
        value = null;
        if (node is not JsonValue scalar || !scalar.TryGetValue<string>(out var candidate)
            || string.IsNullOrWhiteSpace(candidate)) return false;
        value = candidate;
        return true;
    }
}
