using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Evaluation;

/// <summary>What a retrieval case asks for. A case with nothing judged relevant is a no-hit case whatever its kind.</summary>
public enum EvaluationCaseKind
{
    [JsonStringEnumMemberName("retrieval")]
    Retrieval = 1,

    /// <summary>An identifier that the deterministic resolver must resolve to exactly one provision, its single supporting anchor.</summary>
    [JsonStringEnumMemberName("exact_identifier")]
    ExactIdentifier = 2,
}

/// <summary>One retrieval case: a query identity, the collection it belongs to, and what it is judged against.</summary>
public sealed record EvaluationCase
{
    public EvaluationCase(string caseId, string collection, EvaluationCaseKind kind, QueryJudgments judgments)
    {
        CaseId = Identity.Require(caseId, nameof(caseId));
        Collection = Identity.Require(collection, nameof(collection));
        Kind = Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind is not in the closed vocabulary.");
        Judgments = judgments ?? throw new ArgumentNullException(nameof(judgments));
        if (!string.Equals(judgments.QueryId, caseId, StringComparison.Ordinal))
        {
            throw new ArgumentException("A case is judged under its own identity.", nameof(judgments));
        }
    }

    public string CaseId { get; }

    public string Collection { get; }

    public EvaluationCaseKind Kind { get; }

    public QueryJudgments Judgments { get; }
}

/// <summary>An assistant case: the verdict the case is gold-labelled with, as an opaque token the assistant contract owns.</summary>
public sealed record VerdictCase
{
    public VerdictCase(string caseId, string goldVerdict)
    {
        CaseId = Identity.Require(caseId, nameof(caseId));
        GoldVerdict = Identity.Require(goldVerdict, nameof(goldVerdict));
    }

    public string CaseId { get; }

    public string GoldVerdict { get; }
}

/// <summary>A temporal case: for this work on this date, the state the answer must select.</summary>
public sealed record TemporalCase
{
    public TemporalCase(string caseId, string workKey, DateOnly asOf, string expectedStateKey)
    {
        CaseId = Identity.Require(caseId, nameof(caseId));
        WorkKey = Identity.Require(workKey, nameof(workKey));
        AsOf = asOf;
        ExpectedStateKey = Identity.Require(expectedStateKey, nameof(expectedStateKey));
    }

    public string CaseId { get; }

    public string WorkKey { get; }

    public DateOnly AsOf { get; }

    public string ExpectedStateKey { get; }
}

/// <summary>The ranked provisions the system under test returns for a case, asked by identity and never by judgments.</summary>
public delegate IReadOnlyList<RankedAnchor> RetrievalArm(string caseId);

/// <summary>The verdict the system under test emits for a case.</summary>
public delegate string VerdictArm(string caseId);

/// <summary>The state key the system under test selects for a work on a date, or null where it selects none.</summary>
public delegate string? TemporalArm(string workKey, DateOnly asOf);
