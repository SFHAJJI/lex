using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests.Census;

[TestClass]
public sealed class CensusDumpTests
{
    [TestMethod]
    public void DumpCurrentRows() => Assert.Fail(
        "CLOSED\n" + ClosedSurfaceCensus.RenderForTranscription(
            ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere)) +
        "\nGUARDED\n" + ClosedSurfaceCensus.RenderForTranscription(
            ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere)) +
        "\nCANDIDATES\n" + string.Join("\n", ClosedSurfaceCensus.Candidates(CensusScope.SweptHere)));
}
