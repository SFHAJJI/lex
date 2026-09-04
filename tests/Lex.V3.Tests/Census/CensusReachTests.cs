using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Census;

/// <summary>
/// How far the closed-surface census in this project reaches: the Lex assemblies it can see at all,
/// the split between what it sweeps and what its sibling project sweeps, and the types it has
/// declared out of reach.
/// </summary>
/// <remarks>
/// <para>
/// Every census pin sweeps a list of assemblies, and a sweep is only as wide as that list. This is
/// the test that keeps the list honest. The first assertion pins what is actually deployed beside
/// these tests, read off the deployment directory rather than off the list, so a project reference
/// added or dropped fails here. The second binds the split to that same reading, so an assembly
/// cannot be quietly dropped from both halves and disappear from every census. The third checks
/// that the types declared out of reach really are out of reach, so that decision stops being true
/// the day somebody makes them reachable.
/// </para>
/// <para>
/// To re-derive the pinned list when a project reference changes, print
/// <c>ClosedSurfaceCensus.RenderForTranscription</c> over
/// <c>ClosedSurfaceCensus.LexAssembliesBeside(typeof(CensusReachTests).Assembly)</c>
/// from a throwaway test and paste the block between the braces below. Do not edit it by hand, and
/// never build the expected side from the call in the test itself.
/// </para>
/// <para>
/// What this cannot reach, said plainly rather than left to be inferred: a source project that no
/// test project references is deployed beside neither test assembly and is therefore in no census
/// in this repository. When this was written that was exactly <c>Lex.V3.ContractTool</c>, whose two
/// types are named in <see cref="CensusScope.DeclinedOutOfReach"/>. The control for them is a
/// person noticing, which is weaker than a test and is named as what it is rather than dressed up
/// as coverage.
/// </para>
/// </remarks>
[TestClass]
public sealed class CensusReachTests
{
    [TestMethod]
    public void TheLexAssembliesDeployedBesideTheseTestsAreExactlyThese()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "Lex.V3.Api",
                "Lex.V3.Artifacts",
                "Lex.V3.Contracts",
                "Lex.V3.Custody.Azure",
                "Lex.V3.Custody.Probe",
                "Lex.V3.Preview",
            },
            ClosedSurfaceCensus.LexAssembliesBeside(typeof(CensusReachTests).Assembly).ToArray());
    }

    [TestMethod]
    public void EveryDeployedLexAssemblyIsSweptHereOrDeclaredAsSweptBySibling()
    {
        CollectionAssert.AreEqual(
            ClosedSurfaceCensus.LexAssembliesBeside(typeof(CensusReachTests).Assembly).ToArray(),
            CensusScope.SweptHere
                .Concat(CensusScope.SweptBySibling)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            CensusScope.SweptHere
                .Intersect(CensusScope.SweptBySibling, StringComparer.Ordinal)
                .ToArray(),
            "an assembly claimed by both halves is swept twice and reasoned about once");
    }

    [TestMethod]
    public void NoTypeDeclaredOutOfReachIsActuallyReachable()
    {
        var reachable = ClosedSurfaceCensus.Candidates(CensusScope.SweptHere).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            CensusScope.DeclinedOutOfReach
                .Select(static entry => entry[..entry.IndexOf(':', StringComparison.Ordinal)])
                .Where(reachable.Contains)
                .ToArray(),
            "a type declared out of reach is in the sweep, so the reason recorded for it is stale");
    }
}
