namespace Lex.V3.Contracts.Evaluation;

/// <summary>The names of the gates the harness reports. They are the names an evaluation card carries, so they are closed.</summary>
public static class EvaluationGateNames
{
    public const string AnchorNdcgAt10 = "anchor_ndcg_at_10";
    public const string NoHitAccuracy = "no_hit_accuracy";
    public const string ResolverExactness = "resolver_exactness";
    public const string VerdictExactMatch = "verdict_exact_match";
    public const string TemporalExactness = "temporal_exactness";
}

/// <summary>The names of the three shuffled controls, as a control result and the evaluation card carry them.</summary>
public static class ShuffledControlNames
{
    public const string QrelsShuffle = "qrels_shuffle";
    public const string VerdictShuffle = "verdict_shuffle";
    public const string DateShuffle = "date_shuffle";
}
