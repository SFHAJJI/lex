namespace Lex.V3.Contracts.Evaluation;

/// <summary>
/// One provision a query's answer is judged against: a work and an anchor inside it, with the grade of its support.
/// The grades are closed. 3 is the provision that alone supports the answer, 1 is a provision the answer needs for
/// context (a definition, a transitional rule), 0 is the same work and the wrong provision.
/// </summary>
public sealed record JudgedAnchor
{
    public const int SupportingGrade = 3;
    public const int ContextGrade = 1;
    public const int WrongProvisionGrade = 0;

    public JudgedAnchor(string workKey, string anchorId, int grade)
    {
        WorkKey = Identity.Require(workKey, nameof(workKey));
        AnchorId = Identity.Require(anchorId, nameof(anchorId));
        Grade = grade is SupportingGrade or ContextGrade or WrongProvisionGrade
            ? grade
            : throw new ArgumentOutOfRangeException(nameof(grade), grade, "A grade is 3, 1 or 0.");
    }

    public string WorkKey { get; }

    public string AnchorId { get; }

    public int Grade { get; }
}

/// <summary>What a retrieval path returned at one rank: a work and the anchor inside it. Judged at the provision, never at the work.</summary>
public sealed record RankedAnchor
{
    public RankedAnchor(string workKey, string anchorId)
    {
        WorkKey = Identity.Require(workKey, nameof(workKey));
        AnchorId = Identity.Require(anchorId, nameof(anchorId));
    }

    public string WorkKey { get; }

    public string AnchorId { get; }
}

/// <summary>
/// The provisions one query is judged against. An anchor is judged once: two grades for the same work and anchor are a
/// contradiction and are refused, not resolved.
/// </summary>
public sealed record QueryJudgments
{
    private readonly Dictionary<(string Work, string Anchor), int> _grades;

    public QueryJudgments(string queryId, IReadOnlyList<JudgedAnchor> anchors)
    {
        QueryId = Identity.Require(queryId, nameof(queryId));
        ArgumentNullException.ThrowIfNull(anchors);
        _grades = new Dictionary<(string, string), int>();
        foreach (var anchor in anchors)
        {
            if (!_grades.TryAdd((anchor.WorkKey, anchor.AnchorId), anchor.Grade))
            {
                throw new ArgumentException(
                    $"Query '{queryId}' judges '{anchor.WorkKey}' '{anchor.AnchorId}' twice.", nameof(anchors));
            }
        }

        Anchors = Array.AsReadOnly(anchors.ToArray());
    }

    public string QueryId { get; }

    public IReadOnlyList<JudgedAnchor> Anchors { get; }

    /// <summary>The grade of a returned anchor, 0 where the query does not judge it: an unjudged provision earns nothing.</summary>
    public int GradeOf(RankedAnchor item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _grades.GetValueOrDefault((item.WorkKey, item.AnchorId));
    }
}

internal static class Identity
{
    internal static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("An identity is not blank.", name)
            : value;
}
