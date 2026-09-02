using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class ContractUnicodeScalarTests
{
    [TestMethod]
    [DataRow(0xd800)]
    [DataRow(0xdc00)]
    public void TypedSerializationRejectsUnpairedSurrogateBeforeItCanCollideWithReplacementCharacter(
        int invalidCodeUnit)
    {
        Assert.AreEqual(
            "{\"value\":\"\\uFFFD\"}",
            ContractJson.Serialize(new UnicodeProbe("\uFFFD")));

        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Serialize(new UnicodeProbe(new string((char)invalidCodeUnit, 1))));
    }

    [TestMethod]
    [DataRow(0xd800)]
    [DataRow(0xdc00)]
    public void TypedCanonicalizationRejectsUnpairedSurrogateBeforeItCanCollideWithReplacementCharacter(
        int invalidCodeUnit)
    {
        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("test/1\n{\"value\":\"\uFFFD\"}\n"),
            ContractCanonicalizer.Canonicalize(
                new UnicodeProbe("\uFFFD"),
                "test/1",
                maximumDepth: 8));

        Assert.ThrowsExactly<JsonException>(() =>
            ContractCanonicalizer.Canonicalize(
                new UnicodeProbe(new string((char)invalidCodeUnit, 1)),
                "test/1",
                maximumDepth: 8));
    }

    [TestMethod]
    [DataRow("\\ud800")]
    [DataRow("\\udc00")]
    public void TypedDeserializationRejectsUnpairedEscapedSurrogates(string invalidEscape)
    {
        var json = "{\"value\":\"" + invalidEscape + "\"}";
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<UnicodeProbe>(json));
    }

    public sealed record UnicodeProbe(string Value);
}
