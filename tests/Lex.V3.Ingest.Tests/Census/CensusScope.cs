namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// Which assemblies the closed-surface census in this test project sweeps, and which its sibling
/// project sweeps instead.
/// </summary>
/// <remarks>
/// <para>
/// This list narrows the census, so it is the one place the census could be narrowed into a lie,
/// and <c>CensusReachTests</c> is the control: it compares this list plus
/// <see cref="SweptBySibling"/> against the assemblies actually deployed beside these tests. Adding
/// a project reference therefore fails that test until somebody decides which census owns it, and
/// naming it here pulls that assembly's whole closed surface into the three pins, which then fail
/// until the surface is transcribed. Nothing here is narrowed by the contents anyone expects.
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
}
