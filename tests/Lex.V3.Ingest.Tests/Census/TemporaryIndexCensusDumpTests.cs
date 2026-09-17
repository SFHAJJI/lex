using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests.Census;

[TestClass]
public sealed class TemporaryIndexCensusDumpTests
{
    [TestMethod]
    public void Dump() => Assert.Fail(
        "VOCAB\n" + ClosedSurfaceCensus.RenderForTranscription(
            ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere)
                .Where(row => row.Contains("LuxembourgIndex", StringComparison.Ordinal)).ToArray())
        + "\nREGISTRY\n" + ClosedSurfaceCensus.RenderForTranscription(
            ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere)
                .Where(row => row.Contains("LuxembourgIndex", StringComparison.Ordinal)).ToArray())
        + "\nGUARDED\n" + ClosedSurfaceCensus.RenderForTranscription(
            ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere)
                .Where(row => row.Contains("LuxembourgIndex", StringComparison.Ordinal)).ToArray()));
}
