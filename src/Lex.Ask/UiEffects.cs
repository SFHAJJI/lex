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
    TimelineView? Timeline = null,
    RankingView? Ranking = null,
    InForceView? InForce = null,
    CitedByView? CitedBy = null,
    CoverageView? Coverage = null,
    VerificationView? Verification = null,
    // The controls, not a view. Everything above answers "what should the workspace SHOW"; this
    // answers "how should it be SET". Asking for statutes only, or for the next page, is a move a
    // reader makes with a filter, and the assistant should be able to make the same move rather
    // than describe it in prose and leave the visitor to find the control.
    WorkspaceView? Workspace = null,
    GapView? Gap = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Provision is null && Diff is null && History is null && Timeline is null
                           && Ranking is null && InForce is null && CitedBy is null
                           && Coverage is null && Verification is null
                           && Workspace is null && Gap is null;

    /// <summary>Merge the effects of every tool call in one turn into a single payload.</summary>
    public static UiEffect Merge(IEnumerable<UiEffect> parts)
    {
        UiEffect acc = new();
        foreach (var p in parts)
        {
            var ranking = acc.Ranking;
            var workspace = acc.Workspace;
            if (p.Ranking is not null && ranking is null)
            {
                ranking = p.Ranking;
                workspace = p.Workspace;
            }
            else if (p.Ranking is not null && SameRankingQuery(ranking!, p.Ranking)
                     && workspace is null)
            {
                // The existing call already covers the unfiltered corpus. A narrower repeat
                // cannot make that answer more complete and must not narrow its workspace.
            }
            else if (p.Ranking is not null && SameRankingQuery(ranking!, p.Ranking)
                     && p.Workspace is null)
            {
                ranking = p.Ranking;
                workspace = null;
            }
            else if (ranking is not null && p.Ranking is not null
                && CanMergePublisherRankings(ranking, p.Ranking, workspace, p.Workspace))
            {
                ranking = MergePublisherRankings(ranking, p.Ranking);
                workspace = workspace! with { Jurisdiction = null };
            }
            else if (ranking is null)
                workspace ??= p.Workspace;
            acc = acc with
            {
                Provision = acc.Provision ?? p.Provision,
                Diff = acc.Diff ?? p.Diff,
                History = acc.History ?? p.History,
                Timeline = acc.Timeline ?? p.Timeline,
                Ranking = ranking,
                InForce = acc.InForce ?? p.InForce,
                CitedBy = acc.CitedBy ?? p.CitedBy,
                Coverage = acc.Coverage ?? p.Coverage,
                Verification = acc.Verification ?? p.Verification,
                Workspace = workspace,
                Gap = acc.Gap ?? p.Gap,
            };
        }
        return acc;
    }

    private static bool SameRankingQuery(RankingView first, RankingView second) =>
        first.FromDate == second.FromDate
        && first.ToDate == second.ToDate
        && first.Order == second.Order;

    private static bool CanMergePublisherRankings(
        RankingView first,
        RankingView second,
        WorkspaceView? firstScope,
        WorkspaceView? secondScope) =>
        SameRankingQuery(first, second)
        && firstScope is not null
        && secondScope is not null
        && !string.IsNullOrWhiteSpace(secondScope.Jurisdiction)
        && firstScope with { Jurisdiction = null, Page = null, Evidence = null }
            == secondScope with { Jurisdiction = null, Page = null, Evidence = null }
        && (firstScope.Jurisdiction is { Length: > 0 } firstJurisdiction
            ? !string.Equals(firstJurisdiction, secondScope.Jurisdiction,
                StringComparison.OrdinalIgnoreCase)
            : first.Rows.All(row => !string.IsNullOrWhiteSpace(row.Jurisdiction))
              && !first.Rows.Any(row => string.Equals(row.Jurisdiction,
                  secondScope.Jurisdiction, StringComparison.OrdinalIgnoreCase)));

    private static RankingView MergePublisherRankings(RankingView first, RankingView second)
    {
        var rows = first.Rows.Concat(second.Rows)
            .DistinctBy(row => row.Work)
            .OrderByDescending(row => first.Order == "by_churn"
                ? row.VersionsInPeriod : 0)
            .ThenByDescending(row => row.LastChange, StringComparer.Ordinal)
            .Take(50)
            .ToList();
        return first with
        {
            WorksChanged = first.WorksChanged + second.WorksChanged,
            NewVersions = first.NewVersions + second.NewVersions,
            PopulationWorks = first.PopulationWorks + second.PopulationWorks,
            KnownExclusions = (first.KnownExclusions ?? [])
                .Concat(second.KnownExclusions ?? []).Distinct().ToArray(),
            Rows = rows,
            Evidence = (first.Evidence ?? []).Concat(second.Evidence ?? [])
                .Distinct().Take(8).ToArray(),
        };
    }
}

/// <summary>Workspace coordinates: what the interface should have loaded and selected.</summary>
public sealed record Subject(string Work, string? Title, string? Date, string? Anchor,
    string? Language = null);

public sealed record ProvisionView(Subject Subject, string ValidFrom, string? ValidTo,
    IReadOnlyList<ProvisionItem> Provisions, string? Permalink,
    IReadOnlyList<EvidenceContext>? Evidence = null);

public sealed record ProvisionItem(string Anchor, string? Num, string? Heading, string Text, string? Sha);

public sealed record DiffView(Subject Subject, string FromDate, string ToDate,
    string? FromPermalink, string? ToPermalink, string? Note, string? Status = null,
    IReadOnlyList<EvidenceContext>? Evidence = null);

public sealed record HistoryView(Subject Subject, string Anchor, int DistinctTexts,
    IReadOnlyList<HistoryState> States, IReadOnlyList<EvidenceContext>? Evidence = null);

public sealed record HistoryState(string ValidFrom, string? ValidTo, string? Sha, string? Permalink);

public sealed record TimelineView(Subject Subject, IReadOnlyList<EvidenceContext>? Evidence = null);

public sealed record RankingView(string FromDate, string ToDate, string Order,
    int WorksChanged, int NewVersions, IReadOnlyList<RankingRow> Rows, string? Status = null,
    int PopulationWorks = 0, string? PopulationBasis = null,
    IReadOnlyList<string>? KnownExclusions = null,
    IReadOnlyList<EvidenceContext>? Evidence = null);

public sealed record RankingRow(string Work, string? Title, int VersionsInPeriod, int VersionsTotal,
    string FirstChange, string LastChange, string? Permalink, string? DiffPermalink,
    // The state before the window, so a row the assistant surfaces opens the same comparison a
    // clicked row does. Null when the window contains the work's first version.
    string? Baseline = null, string? DiffFrom = null, string? DiffTo = null,
    int DistinctTexts = 0, bool WordingChanged = true, bool TextComparable = false,
    string? Jurisdiction = null, string? Hierarchy = null,
    IReadOnlyList<string>? Domains = null, string? SourceClass = null,
    string? ActForm = null, string? BindingStatus = null, string? Language = null);

public sealed record InForceView(string Date, int Total, IReadOnlyList<InForceRow> Rows,
    string? Status = null, IReadOnlyList<EvidenceContext>? Evidence = null);

/// <summary>Which articles point at one law: the reverse of the publisher's own cross-references.</summary>
public sealed record CitedByView(string CitedWork, int CitingArticles, IReadOnlyList<CitedByRow> Rows,
    string? Status = null, IReadOnlyList<EvidenceContext>? Evidence = null);

public sealed record CitedByRow(string Work, string? Title, string ValidFrom, string Anchor,
                                string? Num, string? Permalink, string? Jurisdiction = null);

public sealed record CoverageView(IReadOnlyList<PublisherCoverage> Publishers,
    IReadOnlyList<EvidenceContext>? Evidence = null);

public sealed record PublisherCoverage(
    string Publisher,
    string? Name,
    string? Tier,
    int Works,
    int Versions,
    int VersionsWithText,
    int VersionsWithoutText,
    string? Earliest,
    string? Latest,
    string? InventoryStatus,
    bool? BuildComplete,
    bool? SignatureValid,
    IReadOnlyList<string> KnownGaps);

public sealed record VerificationView(
    string LexId,
    string? Title,
    string? SourceUri,
    string? RecordSha256,
    string? BodySha256,
    string? Permalink,
    bool? SignatureValid,
    string? Algorithm,
    IReadOnlyList<EvidenceContext>? Evidence = null);

/// <summary>Bounded coordinates retained from the authoritative MCP evidence.</summary>
public sealed record EvidenceContext(
    string? Publisher,
    string? Jurisdiction,
    string? TimelineSemantics,
    string? RequestedDate,
    string? RequestedFromDate,
    string? RequestedToDate,
    string? ObservedAt,
    string? ValidFrom,
    string? ValidTo,
    bool Provisional,
    string? SourceUri,
    string? ExtractionProfile,
    string? RecordSha256,
    string? BodySha256,
    string? TextSha256,
    string? ArtifactManifestId,
    string? ContentDigest,
    bool? SignatureValid);

/// <summary>
/// The complete filter scope for a workspace navigation, as opposed to what it should show.
/// A null Workspace means no filter directive; null fields inside one mean that filter is clear.
/// </summary>
public sealed record WorkspaceView(
    string? Query = null,
    string? Jurisdiction = null,
    string? Hierarchy = null,
    string? Domain = null,
    string? SourceClass = null,
    string? ActForm = null,
    string? BindingStatus = null,
    int? Page = null,
    string? Language = null,
    IReadOnlyList<EvidenceContext>? Evidence = null,
    string? Work = null,
    string? Date = null,
    string? Anchor = null);

public sealed record InForceRow(string Work, string? Title, string? Kind, string ValidFrom,
                                string? Permalink, string? Jurisdiction = null,
                                string? Hierarchy = null);

/// <summary>An honest gap: what was asked for, and why Lex cannot show it.</summary>
public sealed record GapView(string Status, string? Work, string? Date, string Explanation,
    IReadOnlyList<string> Available, IReadOnlyList<EvidenceContext>? Evidence = null);
