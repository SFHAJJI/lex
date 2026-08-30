using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class PreviewCanonicalizationTests
{
    [TestMethod]
    public void CanonicalDocumentBytesMatchTheIndependentKnownVector()
    {
        var expected = Encoding.UTF8.GetBytes(
            "lex-v3-preview-document-canonical-json/1\n" +
            "{\"a\":\"line\\n\\\"\\\\\",\"count\":7,\"z\":\"é\"}\n");
        var actual = PreviewDocumentCanonicalizer.CanonicalizeJsonForEvidence(
            "{\"z\":\"é\",\"a\":\"line\\n\\\"\\\\\",\"count\":7}");

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(
            "d74cd07847b84f854870b5331afdfce5f2fe1fbfe00411327f7765b5058df278",
            PreviewSchemaExporter.ComputeSha256(actual));
    }

    [TestMethod]
    public void CanonicalNestedVectorPinsEscapesArraysBooleansNullAndSignedIntegers()
    {
        var expected = Encoding.UTF8.GetBytes(
            "lex-v3-preview-document-canonical-json/1\n" +
            "{\"array\":[2,1],\"false\":false,\"max\":9223372036854775807," +
            "\"min\":-9223372036854775808,\"null\":null," +
            "\"string\":\"\\b\\t\\n\\f\\r\\u0000\\u001f/😀�\",\"true\":true}\n");
        var actual = PreviewDocumentCanonicalizer.CanonicalizeJsonForEvidence(
            "{\"true\":true,\"string\":\"\\b\\t\\n\\f\\r\\u0000\\u001f/😀�\"," +
            "\"array\":[2,1],\"null\":null,\"false\":false," +
            "\"min\":-9223372036854775808,\"max\":9223372036854775807}");

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(
            "a89a59cac66d52a8d546a05bca89013d348b79479300f79de350a162171fe4c3",
            PreviewSchemaExporter.ComputeSha256(actual));
    }

    [TestMethod]
    public void ObjectMemberInsertionOrderCannotChangeTheDigest()
    {
        var first = PreviewDocumentCanonicalizer.CanonicalizeJsonForEvidence(
            "{\"z\":\"last\",\"a\":\"first\"}");
        var second = PreviewDocumentCanonicalizer.CanonicalizeJsonForEvidence(
            "{\"a\":\"first\",\"z\":\"last\"}");

        CollectionAssert.AreEqual(first, second);
    }

    [TestMethod]
    [DataRow("{\"value\":1.5}")]
    [DataRow("{\"value\":1.0}")]
    [DataRow("{\"value\":-0}")]
    [DataRow("{\"value\":18446744073709551615}")]
    public void CanonicalDocumentsRejectNumbersOutsideSignedIntegerIdentity(string json)
    {
        Assert.ThrowsExactly<JsonException>(() =>
            PreviewDocumentCanonicalizer.CanonicalizeJsonForEvidence(json));
    }

    [TestMethod]
    [DataRow("{\"a\":1,\"a\":2}")]
    [DataRow("{\"a\":1,\"\\u0061\":2}")]
    public void CanonicalDocumentsRejectDuplicateDecodedMemberNames(string json)
    {
        Assert.ThrowsExactly<JsonException>(() =>
            PreviewDocumentCanonicalizer.CanonicalizeJsonForEvidence(json));
    }

    [TestMethod]
    public void PublicCanonicalizationSurfaceAcceptsOnlyTheThreeBoundDocumentTypes()
    {
        var parameterTypes = typeof(PreviewSchemaExporter)
            .GetMethods()
            .Where(static method =>
                method.Name is "GetDocumentCanonicalBytes" or "ComputeDocumentSha256")
            .Select(static method => method.GetParameters().Single().ParameterType)
            .Distinct()
            .OrderBy(static type => type.Name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                typeof(PreviewObjectSet),
                typeof(PreviewOperationCatalog),
                typeof(PreviewRefusalRegistry),
            },
            parameterTypes);
    }
}
