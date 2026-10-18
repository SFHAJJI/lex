using Lex.V3.Api;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What resolve takes for an identifier when no record answers it (refused <c>identifier_unknown</c>) and what it takes
/// for words to look up by title: the same on every system, as production reads it (#983). Uri alone read a rooted path
/// as a file URI on Linux and not on Windows, and read drive and backslash UNC paths as file URIs on both.
/// </summary>
[TestClass]
public sealed class V3IdentifierShapeTests
{
    [TestMethod]
    [DataRow("/lu-legilux/loi-2026-01-01-a1")]
    [DataRow("/eu-eurlex/32016R0679/eng/006")]
    [DataRow("/fr/lu-legilux/loi-1991-08-10-n3")]
    [DataRow("//x")]
    [DataRow(" /lu-legilux/loi-2026-01-01-a1")]
    [DataRow("\t/lu-legilux/loi-2026-01-01-a1")]
    [DataRow("\r\n/lu-legilux/loi-2026-01-01-a1")]
    [DataRow("https://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo")]
    [DataRow(" https://law.soufien.lu/fr/lu-legilux/loi-1991-08-10-n3")]
    [DataRow("file:///lu-legilux/x")]
    [DataRow(" file:///lu-legilux/x")]
    [DataRow("FILE:///x")]
    [DataRow("urn:lex:lu:etat:loi:1991-08-10;n3")]
    [DataRow("eli/etat/leg/loi/1991/08/10/n3/jo")]
    [DataRow("32016R0679")]
    [DataRow("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void AnIdentifierIsOneOnEverySystem(string value) =>
        Assert.IsTrue(V3CorpusMount.LooksLikeIdentifier(value), value);

    [TestMethod]
    [DataRow("loi du 10 août 1991")]
    [DataRow("Code du travail")]
    [DataRow(@"C:\Windows\x")]
    [DataRow("C:/Windows/x")]
    [DataRow(@"\\server\share")]
    [DataRow(@" C:\x")]
    [DataRow("C:x")]
    [DataRow("file:x")]
    [DataRow("%2Flu-legilux%2Fx")]
    public void WordsAndFilePathsAreWordsOnEverySystem(string value) =>
        Assert.IsFalse(V3CorpusMount.LooksLikeIdentifier(value), value);
}
