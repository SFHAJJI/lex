namespace Lex.V3.Tests.Census;

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
/// Every Lex assembly deployed beside these tests is swept here, so <see cref="SweptBySibling"/>
/// is empty. Lex.V3.Ingest is deployed beside Lex.V3.Ingest.Tests and swept there.
/// </para>
/// </remarks>
internal static class CensusScope
{
    internal static readonly string[] SweptHere =
    [
        "Lex.V3.Api",
        "Lex.V3.Artifacts",
        "Lex.V3.Contracts",
        "Lex.V3.Custody.Azure",
        "Lex.V3.Custody.Probe",
        "Lex.V3.Preview",
    ];

    internal static readonly string[] SweptBySibling = [];
}
