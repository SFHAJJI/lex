using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// How far the closed-surface census in this project reaches: the Lex assemblies it can see at all,
/// and the split between what it sweeps and what its sibling project sweeps.
/// </summary>
/// <remarks>
/// <para>
/// The three census pins each sweep a list of assemblies, and a sweep is only as wide as that list.
/// This is the test that keeps the list honest. The first assertion pins what is actually deployed
/// beside these tests, read off the deployment directory rather than off the list, so a project
/// reference added or dropped fails here. The second binds the split to that same reading, so an
/// assembly cannot be quietly dropped from both halves and disappear from every census.
/// </para>
/// <para>
/// What this cannot reach, said plainly rather than left to be inferred: a source project that no
/// test project references is deployed beside neither test assembly and is therefore in no census
/// in this repository. When this was written that was exactly <c>Lex.V3.ContractTool</c>. The
/// control for it is a person noticing, which is weaker than a test and is named here as what it
/// is rather than dressed up as coverage.
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
                "Lex.V3.Artifacts",
                "Lex.V3.Contracts",
                "Lex.V3.Ingest",
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
}
