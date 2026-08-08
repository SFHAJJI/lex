using Lex.Index;

namespace Lex.RetrievalExperiment;

public static class SemanticProposalMatcher
{
    private static readonly HashSet<string> WeakKinds = new(StringComparer.Ordinal)
    {
        "description", "concept", "search_synonym", "practice_area",
    };

    public static IReadOnlyList<EnrichmentProposal> Match(
        string work,
        string language,
        IReadOnlyList<IReadOnlyList<ModelDiscoveryCandidate>> runs,
        string modelDeployment,
        string promptHash,
        ITextEncoder encoder,
        double minimumCosine)
    {
        if (runs.Count != 2)
            throw new ArgumentException("The semantic consensus experiment requires exactly two runs.", nameof(runs));
        if (minimumCosine is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(minimumCosine));

        var left = runs[0].Where(IsWeak).ToArray();
        var right = runs[1].Where(IsWeak).ToArray();
        var all = left.Concat(right).ToArray();
        var vectors = encoder.EncodeBatch(all.Select(candidate => candidate.Value).ToArray(), EmbeddingInputKind.Passage);
        var pairs = new List<Pair>();
        for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
        {
            for (var rightIndex = 0; rightIndex < right.Length; rightIndex++)
            {
                var first = left[leftIndex];
                var second = right[rightIndex];
                if (EnrichmentContract.Normalize(first.Value) == EnrichmentContract.Normalize(second.Value))
                    continue;
                if (!SharesEvidence(first, second)) continue;
                var cosine = Cosine(vectors[leftIndex], vectors[left.Length + rightIndex]);
                if (cosine < minimumCosine) continue;
                pairs.Add(new Pair(leftIndex, rightIndex, cosine, first, second));
            }
        }

        var usedLeft = new HashSet<int>();
        var usedRight = new HashSet<int>();
        var matches = new List<EnrichmentProposal>();
        foreach (var pair in pairs.OrderByDescending(pair => pair.Cosine)
                     .ThenBy(pair => Key(pair.Left), StringComparer.Ordinal)
                     .ThenBy(pair => Key(pair.Right), StringComparer.Ordinal))
        {
            if (usedLeft.Contains(pair.LeftIndex) || usedRight.Contains(pair.RightIndex)) continue;
            usedLeft.Add(pair.LeftIndex);
            usedRight.Add(pair.RightIndex);
            var representative = new[] { pair.Left, pair.Right }
                .OrderBy(candidate => EnrichmentContract.Normalize(candidate.Value), StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Kind, StringComparer.Ordinal)
                .First();
            var kind = pair.Left.Kind == pair.Right.Kind ? pair.Left.Kind : "concept";
            var evidence = pair.Left.Evidence.Concat(pair.Right.Evidence).Distinct()
                .OrderBy(EvidenceKey, StringComparer.Ordinal).ToArray();
            matches.Add(new EnrichmentProposal(
                work,
                language,
                kind,
                representative.Value,
                evidence,
                modelDeployment,
                promptHash,
                (pair.Left.Confidence + pair.Right.Confidence) / 2,
                RepeatRuns: 2,
                AgreementRatio: 1,
                ReviewedBy: null));
        }
        return matches.OrderBy(proposal => proposal.Kind, StringComparer.Ordinal)
            .ThenBy(proposal => EnrichmentContract.Normalize(proposal.Value), StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsWeak(ModelDiscoveryCandidate candidate) => WeakKinds.Contains(candidate.Kind);

    private static bool SharesEvidence(ModelDiscoveryCandidate first, ModelDiscoveryCandidate second)
    {
        var firstEvidence = first.Evidence.Select(EvidenceKey).ToHashSet(StringComparer.Ordinal);
        return second.Evidence.Any(item => firstEvidence.Contains(EvidenceKey(item)));
    }

    private static double Cosine(float[] first, float[] second)
    {
        if (first.Length != second.Length) throw new InvalidDataException("Embedding dimensions do not match.");
        double dot = 0;
        double firstNorm = 0;
        double secondNorm = 0;
        for (var index = 0; index < first.Length; index++)
        {
            dot += first[index] * second[index];
            firstNorm += first[index] * first[index];
            secondNorm += second[index] * second[index];
        }
        return firstNorm == 0 || secondNorm == 0 ? 0 : dot / Math.Sqrt(firstNorm * secondNorm);
    }

    private static string Key(ModelDiscoveryCandidate candidate) =>
        candidate.Kind + "\u001f" + EnrichmentContract.Normalize(candidate.Value) + "\u001f"
        + string.Join("\u001e", candidate.Evidence.Select(EvidenceKey).Order(StringComparer.Ordinal));

    private static string EvidenceKey(EvidenceAnchor evidence) =>
        $"{evidence.Work}\u001f{evidence.Version}\u001f{evidence.Anchor}\u001f{evidence.TextSha256}";

    private sealed record Pair(
        int LeftIndex,
        int RightIndex,
        double Cosine,
        ModelDiscoveryCandidate Left,
        ModelDiscoveryCandidate Right);
}
