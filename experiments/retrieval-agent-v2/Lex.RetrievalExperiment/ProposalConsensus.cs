namespace Lex.RetrievalExperiment;

public sealed record ModelDiscoveryCandidate(
    string Kind,
    string Value,
    double Confidence,
    IReadOnlyList<EvidenceAnchor> Evidence);

public static class ProposalConsensus
{
    public static IReadOnlyList<EnrichmentProposal> Merge(
        string work,
        string language,
        IReadOnlyList<IReadOnlyList<ModelDiscoveryCandidate>> runs,
        string modelDeployment,
        string promptHash)
    {
        if (runs.Count == 0) return [];
        var observations = runs.SelectMany((run, runIndex) => run
            .GroupBy(Key, StringComparer.Ordinal)
            .Select(group => (Run: runIndex, Key: group.Key, Candidate: group.First())))
            .GroupBy(observation => observation.Key, StringComparer.Ordinal);

        return observations.Select(group =>
        {
            var candidates = group.Select(observation => observation.Candidate).ToArray();
            var representative = candidates.OrderBy(candidate => candidate.Value, StringComparer.Ordinal).First();
            var repeatRuns = group.Select(observation => observation.Run).Distinct().Count();
            return new EnrichmentProposal(
                work,
                language,
                representative.Kind,
                representative.Value,
                representative.Evidence.OrderBy(EvidenceKey, StringComparer.Ordinal).ToArray(),
                modelDeployment,
                promptHash,
                candidates.Average(candidate => candidate.Confidence),
                repeatRuns,
                repeatRuns / (double)runs.Count,
                ReviewedBy: null);
        }).OrderBy(proposal => proposal.Kind, StringComparer.Ordinal)
          .ThenBy(proposal => EnrichmentContract.Normalize(proposal.Value), StringComparer.Ordinal)
          .ToArray();
    }

    private static string Key(ModelDiscoveryCandidate candidate) =>
        candidate.Kind + "\u001f" + EnrichmentContract.Normalize(candidate.Value) + "\u001f"
        + string.Join("\u001e", candidate.Evidence.Select(EvidenceKey).Order(StringComparer.Ordinal));

    private static string EvidenceKey(EvidenceAnchor evidence) =>
        $"{evidence.Work}\u001f{evidence.Version}\u001f{evidence.Anchor}\u001f{evidence.TextSha256}";
}
