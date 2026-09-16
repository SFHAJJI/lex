using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests.Census;

[TestClass]
public sealed class CensusTranscriptionProbeTests
{
    [TestMethod]
    public void PrintNewRows()
    {
        var vocabularies = ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere)
            .Where(row => row.Contains("AknLegalContent", StringComparison.Ordinal)).ToArray();
        var guarded = ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere)
            .Where(row => row.Contains("AknLegalContent", StringComparison.Ordinal)).ToArray();
        var candidates = ClosedSurfaceCensus.Candidates(CensusScope.SweptHere)
            .Where(row => row.Contains("AknLegalContent", StringComparison.Ordinal)).ToArray();
        Assert.Fail(
            "VOCABULARIES\n" + ClosedSurfaceCensus.RenderForTranscription(vocabularies) +
            "\nGUARDED\n" + ClosedSurfaceCensus.RenderForTranscription(guarded) +
            "\nCANDIDATES\n" + string.Join("\n", candidates));
    }
}
