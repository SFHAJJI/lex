using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Census;

[TestClass]
public sealed class ZZScratchCensusDumpTests
{
    [TestMethod]
    public void Dump()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lex-v3-census-dump");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "guarded.txt"), ClosedSurfaceCensus.RenderForTranscription(ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere)));
        File.WriteAllText(Path.Combine(dir, "vocab.txt"), ClosedSurfaceCensus.RenderForTranscription(ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere)));
        File.WriteAllText(Path.Combine(dir, "registries.txt"), ClosedSurfaceCensus.RenderForTranscription(ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere)));
        File.WriteAllLines(Path.Combine(dir, "candidates.txt"), ClosedSurfaceCensus.Candidates(CensusScope.SweptHere));
        File.WriteAllText(Path.Combine(dir, "counts.txt"),
            $"candidates={ClosedSurfaceCensus.Candidates(CensusScope.SweptHere).Count} " +
            $"vocab={ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere).Count} " +
            $"guarded={ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere).Count} " +
            $"registries={ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere).Count}");
    }
}
