using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lex.RetrievalExperiment;

public sealed record EvidenceAnchor(string Work, string Version, string Anchor, string TextSha256);

public sealed record EnrichmentProposal(
    string Work,
    string Language,
    string Kind,
    string Value,
    IReadOnlyList<EvidenceAnchor> Evidence,
    string ModelDeployment,
    string PromptHash,
    double Confidence,
    int RepeatRuns,
    double AgreementRatio,
    string? ReviewedBy);

public sealed record AcceptedEnrichment(
    string Work,
    string Language,
    string Kind,
    string Value,
    string NormalizedValue,
    string RankTier,
    string Provenance,
    IReadOnlyList<EvidenceAnchor> Evidence,
    string ModelDeployment,
    string PromptHash,
    double Confidence,
    int RepeatRuns,
    double AgreementRatio,
    string? ReviewedBy);

public sealed record RejectedEnrichment(EnrichmentProposal Proposal, string Reason);

public sealed record EnrichmentValidation(
    IReadOnlyList<AcceptedEnrichment> Accepted,
    IReadOnlyList<RejectedEnrichment> Rejected);

public static partial class EnrichmentContract
{
    private static readonly HashSet<string> AllowedFields = new(StringComparer.Ordinal)
    {
        "work", "language", "kind", "value", "evidence", "model_deployment", "prompt_hash",
        "confidence", "repeat_runs", "agreement_ratio", "reviewed_by",
    };

    private static readonly HashSet<string> EvidenceFields = new(StringComparer.Ordinal)
    {
        "work", "version", "anchor", "text_sha256",
    };

    private static readonly HashSet<string> StrongKinds = new(StringComparer.Ordinal)
    {
        "alias", "acronym",
    };

    private static readonly HashSet<string> WeakKinds = new(StringComparer.Ordinal)
    {
        "description", "concept", "search_synonym", "practice_area",
    };

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    public static EnrichmentProposal Parse(JsonObject json)
    {
        RejectUnknown(json, AllowedFields, "proposal");
        var evidenceNodes = json["evidence"]?.AsArray()
            ?? throw new InvalidDataException("proposal.evidence is required");
        var evidence = evidenceNodes.Select((node, index) =>
        {
            var item = node?.AsObject()
                ?? throw new InvalidDataException($"proposal.evidence[{index}] must be an object");
            RejectUnknown(item, EvidenceFields, $"proposal.evidence[{index}]");
            return new EvidenceAnchor(
                RequiredString(item, "work"),
                RequiredString(item, "version"),
                RequiredString(item, "anchor"),
                RequiredString(item, "text_sha256"));
        }).ToArray();

        return new EnrichmentProposal(
            RequiredString(json, "work"),
            RequiredString(json, "language"),
            RequiredString(json, "kind"),
            RequiredString(json, "value"),
            evidence,
            RequiredString(json, "model_deployment"),
            RequiredString(json, "prompt_hash"),
            json["confidence"]?.GetValue<double>()
                ?? throw new InvalidDataException("proposal.confidence is required"),
            json["repeat_runs"]?.GetValue<int>()
                ?? throw new InvalidDataException("proposal.repeat_runs is required"),
            json["agreement_ratio"]?.GetValue<double>()
                ?? throw new InvalidDataException("proposal.agreement_ratio is required"),
            json["reviewed_by"]?.GetValue<string>());
    }

    public static EnrichmentValidation Validate(IReadOnlyList<EnrichmentProposal> proposals)
    {
        var accepted = new List<AcceptedEnrichment>();
        var rejected = new List<RejectedEnrichment>();
        var collidingAliases = proposals
            .Where(proposal => StrongKinds.Contains(proposal.Kind) && !string.IsNullOrWhiteSpace(proposal.ReviewedBy))
            .GroupBy(proposal => (proposal.Language, Value: Normalize(proposal.Value)))
            .Where(group => group.Select(proposal => proposal.Work).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var proposal in proposals)
        {
            var normalized = Normalize(proposal.Value);
            string? reason = null;
            if (!StrongKinds.Contains(proposal.Kind) && !WeakKinds.Contains(proposal.Kind))
                reason = "unsupported_kind";
            else if (normalized.Length < 2)
                reason = "empty_or_short_value";
            else if (string.IsNullOrWhiteSpace(proposal.ModelDeployment)
                     || !Sha256Pattern().IsMatch(proposal.PromptHash))
                reason = "invalid_model_provenance";
            else if (proposal.Confidence is < 0 or > 1 || proposal.AgreementRatio is < 0 or > 1)
                reason = "invalid_score";
            else if (proposal.Evidence.Count == 0
                     || proposal.Evidence.Any(evidence => evidence.Work != proposal.Work
                         || string.IsNullOrWhiteSpace(evidence.Version)
                         || string.IsNullOrWhiteSpace(evidence.Anchor)
                         || !Sha256Pattern().IsMatch(evidence.TextSha256)))
                reason = "invalid_evidence";
            else if (StrongKinds.Contains(proposal.Kind) && string.IsNullOrWhiteSpace(proposal.ReviewedBy))
                reason = "alias_requires_review";
            else if (StrongKinds.Contains(proposal.Kind)
                     && collidingAliases.Contains((proposal.Language, normalized)))
                reason = "alias_collision";
            else if (WeakKinds.Contains(proposal.Kind)
                     && (proposal.RepeatRuns < 2 || proposal.AgreementRatio < 0.8 || proposal.Confidence < 0.7))
                reason = "insufficient_repeatable_evidence";

            if (reason is not null)
            {
                rejected.Add(new RejectedEnrichment(proposal, reason));
                continue;
            }

            var strong = StrongKinds.Contains(proposal.Kind);
            accepted.Add(new AcceptedEnrichment(
                proposal.Work,
                proposal.Language,
                proposal.Kind,
                proposal.Value.Trim(),
                normalized,
                strong ? "exact_alias" : "weak_discovery",
                strong ? "reviewed" : "model-derived",
                proposal.Evidence,
                proposal.ModelDeployment,
                proposal.PromptHash,
                proposal.Confidence,
                proposal.RepeatRuns,
                proposal.AgreementRatio,
                proposal.ReviewedBy));
        }
        return new EnrichmentValidation(accepted, rejected);
    }

    public static string Export(IReadOnlyList<AcceptedEnrichment> accepted)
    {
        var rows = accepted
            .OrderBy(row => row.Work, StringComparer.Ordinal)
            .ThenBy(row => row.Language, StringComparer.Ordinal)
            .ThenBy(row => row.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.NormalizedValue, StringComparer.Ordinal)
            .Select(row => (JsonNode)new JsonObject
            {
                ["work"] = row.Work,
                ["language"] = row.Language,
                ["kind"] = row.Kind,
                ["value"] = row.Value,
                ["normalized_value"] = row.NormalizedValue,
                ["rank_tier"] = row.RankTier,
                ["provenance"] = row.Provenance,
                ["model_deployment"] = row.ModelDeployment,
                ["prompt_hash"] = row.PromptHash,
                ["confidence"] = row.Confidence,
                ["repeat_runs"] = row.RepeatRuns,
                ["agreement_ratio"] = row.AgreementRatio,
                ["reviewed_by"] = row.ReviewedBy,
                ["evidence"] = new JsonArray(row.Evidence.Select(evidence => (JsonNode)new JsonObject
                {
                    ["work"] = evidence.Work,
                    ["version"] = evidence.Version,
                    ["anchor"] = evidence.Anchor,
                    ["text_sha256"] = evidence.TextSha256,
                }).ToArray()),
            }).ToArray();
        return new JsonArray(rows).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var output = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && output.Length > 0) output.Append(' ');
                output.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = output.Length > 0;
            }
        }
        return output.ToString();
    }

    private static void RejectUnknown(JsonObject json, HashSet<string> allowed, string path)
    {
        var unknown = json.Select(property => property.Key).Where(key => !allowed.Contains(key)).Order().ToArray();
        if (unknown.Length > 0)
            throw new InvalidDataException($"{path} contains forbidden field(s): {string.Join(", ", unknown)}");
    }

    private static string RequiredString(JsonObject json, string name)
    {
        var value = json[name]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"{name} is required")
            : value;
    }
}

