using System.Text.Json.Nodes;

namespace Lex.Ask;

/// <summary>
/// The rendering directive the interface consumes — a FIELD on the reply, never a reply
/// TYPE. A turn may produce prose AND one or more views ("how did this law change, and what
/// else moved that month?" needs a diff and a ranking); making the payload a type would make
/// that unanswerable by construction. New views extend this record; nothing else changes.
///
/// Derived in the Ask layer, never inside McpCore: the MCP tools are the product and are
/// consumed by other people's agents, so their responses stay free of our rendering concerns.
/// </summary>
public sealed record UiEffect(
    ProvisionView? Provision = null,
    DiffView? Diff = null,
    HistoryView? History = null,
    RankingView? Ranking = null,
    InForceView? InForce = null,
    GapView? Gap = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Provision is null && Diff is null && History is null
                           && Ranking is null && InForce is null && Gap is null;

    /// <summary>Merge the effects of every tool call in one turn into a single payload.</summary>
    public static UiEffect Merge(IEnumerable<UiEffect> parts)
    {
        UiEffect acc = new();
        foreach (var p in parts)
            acc = acc with
            {
                Provision = acc.Provision ?? p.Provision,
                Diff = acc.Diff ?? p.Diff,
                History = acc.History ?? p.History,
                Ranking = acc.Ranking ?? p.Ranking,
                InForce = acc.InForce ?? p.InForce,
                Gap = acc.Gap ?? p.Gap,
            };
        return acc;
    }
}

/// <summary>Workspace coordinates: what the interface should have loaded and selected.</summary>
public sealed record Subject(string Work, string? Title, string? Date, string? Anchor);

public sealed record ProvisionView(Subject Subject, string ValidFrom, string? ValidTo,
    IReadOnlyList<ProvisionItem> Provisions, string? Permalink);

public sealed record ProvisionItem(string Anchor, string? Num, string? Heading, string Text, string? Sha);

public sealed record DiffView(Subject Subject, string FromDate, string ToDate,
    string? FromPermalink, string? ToPermalink, string? Note);

public sealed record HistoryView(Subject Subject, string Anchor, int DistinctTexts,
    IReadOnlyList<HistoryState> States);

public sealed record HistoryState(string ValidFrom, string? ValidTo, string? Sha, string? Permalink);

public sealed record RankingView(string FromDate, string ToDate, string Order,
    int WorksChanged, int NewVersions, IReadOnlyList<RankingRow> Rows);

public sealed record RankingRow(string Work, string? Title, int VersionsInPeriod, int VersionsTotal,
    string FirstChange, string LastChange, string? Permalink, string? DiffPermalink);

public sealed record InForceView(string Date, int Total, IReadOnlyList<InForceRow> Rows);

public sealed record InForceRow(string Work, string? Title, string? Kind, string ValidFrom, string? Permalink);

/// <summary>An honest gap: what was asked for, and why Lex cannot show it.</summary>
public sealed record GapView(string Status, string? Work, string? Date, string Explanation,
    IReadOnlyList<string> Available);
