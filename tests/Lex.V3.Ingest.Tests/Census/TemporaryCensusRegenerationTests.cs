using System.Text;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

[TestClass]
public sealed class TemporaryCensusRegenerationTests
{
    [TestMethod]
    public void RenderCombinedCensusPins()
    {
        var output = new StringBuilder()
            .AppendLine("CLOSED")
            .Append(ClosedSurfaceCensus.RenderForTranscription(
                ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere)))
            .AppendLine("GUARDED")
            .Append(ClosedSurfaceCensus.RenderForTranscription(
                ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere)))
            .ToString();
        Directory.CreateDirectory(@"C:\lex-v3\test-output");
        File.WriteAllText(
            @"C:\lex-v3\test-output\formex-census-regeneration.txt",
            output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
