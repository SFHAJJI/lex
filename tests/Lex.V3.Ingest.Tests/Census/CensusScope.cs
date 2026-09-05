namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// Which assemblies the closed-surface census in this test project sweeps, which its sibling
/// project sweeps instead, and which types it has decided are out of reach entirely.
/// </summary>
/// <remarks>
/// <para>
/// This list narrows the census, so it is the one place the census could be narrowed into a lie,
/// and <c>CensusReachTests</c> is the control: it compares this list plus
/// <see cref="SweptBySibling"/> against the assemblies actually deployed beside these tests. Adding
/// a project reference therefore fails that test until somebody decides which census owns it, and
/// naming it here pulls that assembly's whole closed surface into the pins, which then fail until
/// the surface is transcribed. Nothing here is narrowed by the contents anyone expects.
/// </para>
/// <para>
/// Lex.V3.Ingest is deployed only beside these tests, so it is swept here.
/// Lex.V3.Contracts and Lex.V3.Artifacts are deployed beside both test projects and are
/// swept by Lex.V3.Tests. Sweeping them again here would duplicate tens of thousands of
/// characters of pinned surface in a second place, which is two places to update and one
/// to forget.
/// </para>
/// </remarks>
internal static class CensusScope
{
    internal static readonly string[] SweptHere =
    [
        "Lex.V3.Ingest",
    ];

    internal static readonly string[] SweptBySibling =
    [
        "Lex.V3.Artifacts",
        "Lex.V3.Contracts",
    ];

    /// <summary>
    /// Types this repository holds that no census here can reach, with the reason. Each entry is
    /// <c>full name: reason</c>. <c>CensusReachTests</c> asserts none of them is in fact reachable,
    /// so the day one becomes reachable this stops being true and fails rather than lingering as a
    /// stale sentence. The control for the type itself remains a person noticing, which is weaker
    /// than a test and is named as what it is.
    /// </summary>
    internal static readonly string[] DeclinedOutOfReach =
    [
        "Lex.V3.ContractTool.ScopeScaleProbe: ContractTool is deployed beside no test project",
        "Lex.V3.ContractTool.ScopeScaleProbe+ScopeSortKey: ContractTool is deployed beside no test project",
    ];
}
