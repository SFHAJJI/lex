using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class StrictPublisherXmlTests
{
    [TestMethod]
    public void ValidRetainedXmlParsesToAReadOnlyWholeDocument()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><root><item code=\"A\">publisher &amp; text</item></root>");

        var document = StrictPublisherXml.Parse(bytes, HostileProfile(bytes.Length));
        var navigator = document.CreateNavigator();

        Assert.AreEqual(
            "strict-publisher-xml/1",
            typeof(StrictPublisherXml).GetField(nameof(StrictPublisherXml.Identity))?.GetRawConstantValue());
        // Reading the const through reflection prevents the compiler from folding this assertion
        // into a comparison of the test literal with itself.
        if (navigator.CanEdit)
        {
            Assert.Fail("The parser result must not expose an editable XML model.");
        }
        Assert.AreEqual("publisher & text", navigator.SelectSingleNode("/root/item")?.Value);
        Assert.AreEqual("A", navigator.SelectSingleNode("/root/item/@code")?.Value);
    }

    [TestMethod]
    public void ProfileByteCeilingIsExactAndWinsBeforeXmlParsing()
    {
        var valid = Encoding.UTF8.GetBytes("<root/>");
        _ = StrictPublisherXml.Parse(valid, HostileProfile(valid.Length));

        var overLimitAndMalformed = Encoding.UTF8.GetBytes("<root/><");
        var failure = Assert.ThrowsExactly<StrictPublisherXmlException>(() =>
            StrictPublisherXml.Parse(overLimitAndMalformed, HostileProfile(valid.Length)));

        Assert.AreEqual(StrictPublisherXmlFailure.InputExceedsLimit, failure.Failure);
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            StrictPublisherXml.Parse(valid, null!));
    }

    [TestMethod]
    public void InvalidDeclaredOrDefaultEncodingIsTypedAsInvalidXml()
    {
        byte[] invalidDefaultEncoding =
        [
            (byte)'<', (byte)'r', (byte)'>', 0xff,
            (byte)'<', (byte)'/', (byte)'r', (byte)'>'
        ];

        var defaultFailure = Assert.ThrowsExactly<StrictPublisherXmlException>(() =>
            StrictPublisherXml.Parse(invalidDefaultEncoding, HostileProfile(invalidDefaultEncoding.Length)));
        var unsupportedDeclaration = Encoding.ASCII.GetBytes(
            "<?xml version='1.0' encoding='not-an-encoding'?><root/>");
        var declaredFailure = Assert.ThrowsExactly<StrictPublisherXmlException>(() =>
            StrictPublisherXml.Parse(unsupportedDeclaration, HostileProfile(unsupportedDeclaration.Length)));

        Assert.AreEqual(StrictPublisherXmlFailure.InvalidXml, defaultFailure.Failure);
        Assert.AreEqual(StrictPublisherXmlFailure.InvalidXml, declaredFailure.Failure);
    }

    [TestMethod]
    public void XmlDeclarationControlsDecodingOfTheRetainedBytes()
    {
        var latin1 = Encoding.Latin1.GetBytes(
            "<?xml version='1.0' encoding='iso-8859-1'?><root>é</root>");
        var utf8 = Encoding.UTF8.GetBytes(
            "<?xml version='1.0' encoding='UTF-8'?><root>é</root>");

        var latin1Document = StrictPublisherXml.Parse(latin1, HostileProfile(latin1.Length));
        var utf8Document = StrictPublisherXml.Parse(utf8, HostileProfile(utf8.Length));

        Assert.AreEqual("é", latin1Document.CreateNavigator().SelectSingleNode("/root")?.Value);
        Assert.AreEqual("é", utf8Document.CreateNavigator().SelectSingleNode("/root")?.Value);
    }

    [TestMethod]
    [DataRow("<!DOCTYPE root [<!ENTITY x 'expanded'>]><root>&x;</root>")]
    [DataRow("<!DOCTYPE root SYSTEM 'file:///publisher-secret'><root/>")]
    public void DtdAndExternalEntityDeclarationsAreRefused(string xml)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);

        var failure = Assert.ThrowsExactly<StrictPublisherXmlException>(() =>
            StrictPublisherXml.Parse(bytes, HostileProfile(bytes.Length)));

        Assert.AreEqual(StrictPublisherXmlFailure.InvalidXml, failure.Failure);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("<root>")]
    [DataRow("<first/><second/>")]
    public void IncompleteOrNonDocumentXmlIsRefused(string xml)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);

        var failure = Assert.ThrowsExactly<StrictPublisherXmlException>(() =>
            StrictPublisherXml.Parse(bytes, HostileProfile(Math.Max(1, bytes.Length))));

        Assert.AreEqual(StrictPublisherXmlFailure.InvalidXml, failure.Failure);
    }

    [TestMethod]
    public void FailureVocabularyIsClosedAndDoesNotClaimPublisherMeaning()
    {
        CollectionAssert.AreEqual(
            new[] { "InputExceedsLimit", "InvalidXml" },
            Enum.GetNames<StrictPublisherXmlFailure>());
        CollectionAssert.AreEqual(
            new[] { "input_exceeds_limit", "invalid_xml" },
            Enum.GetValues<StrictPublisherXmlFailure>()
                .Select(static value => typeof(StrictPublisherXmlFailure)
                    .GetField(value.ToString())!
                    .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name)
                .ToArray());
    }

    [TestMethod]
    public void OnlyAReviewedProfileCanSupplyTheByteCeiling()
    {
        var parse = typeof(StrictPublisherXml).GetMethod(nameof(StrictPublisherXml.Parse));
        CollectionAssert.AreEqual(
            new[] { typeof(ReadOnlyMemory<byte>), typeof(StrictPublisherXmlProfile) },
            parse!.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());
        var constructors = typeof(StrictPublisherXmlProfile).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.AreEqual(1, constructors.Length);
        Assert.IsTrue(constructors[0].IsPrivate, "the reviewed ceiling gained a reachable constructor");
        CollectionAssert.AreEqual(
            new[] { nameof(StrictPublisherXmlProfile.EuFormexPackage) },
            typeof(StrictPublisherXmlProfile)
                .GetProperties(BindingFlags.Static | BindingFlags.Public)
                .Select(static property => property.Name)
                .ToArray());
        Assert.IsNotNull(StrictPublisherXmlProfile.EuFormexPackage);
        var maximumBytes = typeof(StrictPublisherXmlProfile).GetProperty(
            "MaximumBytes",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.AreEqual(
            67_108_864,
            maximumBytes!.GetValue(StrictPublisherXmlProfile.EuFormexPackage),
            "the Formex member ceiling drifted from OPS-EU-FORMAT");
    }

    private static StrictPublisherXmlProfile HostileProfile(int maximumBytes)
    {
        var constructor = typeof(StrictPublisherXmlProfile).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(int)],
            modifiers: null);
        Assert.IsNotNull(constructor, "The test must reach the private value boundary explicitly.");
        return (StrictPublisherXmlProfile)constructor.Invoke([maximumBytes]);
    }
}
