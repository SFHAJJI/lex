namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LexCorpus6BuilderTests
{
    [TestMethod]
    public void TerminalBuilderAndStrictReaderAreOneVerticalSlice()
    {
        Assert.AreEqual("lex-corpus/6", LexCorpus6Builder.Schema);
        Assert.IsNotNull(typeof(LexCorpus6Builder).GetMethod(nameof(LexCorpus6Builder.TryBuild)));
        Assert.IsNotNull(typeof(VerifiedLexCorpus6ManifestSet).GetMethod(
            nameof(VerifiedLexCorpus6ManifestSet.ParseAndVerify)));
    }
}
