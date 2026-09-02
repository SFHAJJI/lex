using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgLiteralCanonicalizerTests
{
    [TestMethod]
    public void RegistryContainsOnlyObservedSelectorDatatypes()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                LuxembourgLiteralCanonicalizer.RdfLangString,
                LuxembourgLiteralCanonicalizer.XsdDate,
                LuxembourgLiteralCanonicalizer.XsdString,
            },
            LuxembourgLiteralCanonicalizer.SupportedDatatypeIris.ToArray());
    }

    [TestMethod]
    public void XsdStringMappingIsIdentityAndPreservesRawTerm()
    {
        const string raw = "  e\u0301  ";

        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            raw,
            LuxembourgLiteralCanonicalizer.XsdString,
            string.Empty);

        Assert.AreEqual(LuxembourgLiteralDisposition.Accepted, result.Disposition);
        Assert.AreEqual(LuxembourgLiteralReason.AcceptedXsdStringIdentity, result.Reason);
        Assert.AreEqual(raw, result.RawLexicalValue);
        Assert.AreEqual(raw, result.CanonicalSelectorLexicalValue);
        Assert.AreEqual(string.Empty, result.LanguageTag);
    }

    [TestMethod]
    public void PlainLiteralUsesXsdStringWithoutRewritingRawDatatype()
    {
        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            "publisher spelling",
            string.Empty,
            string.Empty);

        Assert.AreEqual(LuxembourgLiteralDisposition.Accepted, result.Disposition);
        Assert.AreEqual(string.Empty, result.RawDatatypeIriOrEmpty);
        Assert.AreEqual(LuxembourgLiteralCanonicalizer.XsdString, result.DatatypeIri);
        Assert.AreEqual("publisher spelling", result.CanonicalSelectorLexicalValue);
    }

    [TestMethod]
    public void LanguageLiteralPreservesRawTermAndCanonicalizesOnlyTheTag()
    {
        const string raw = "Texte coordonne";

        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            raw,
            LuxembourgLiteralCanonicalizer.RdfLangString,
            "FR-latn");

        Assert.AreEqual(LuxembourgLiteralDisposition.Accepted, result.Disposition);
        Assert.AreEqual(LuxembourgLiteralReason.AcceptedRdfLangStringIdentity, result.Reason);
        Assert.AreEqual(raw, result.RawLexicalValue);
        Assert.AreEqual("FR-latn", result.RawLanguageTagOrEmpty);
        Assert.AreEqual("fr-latn", result.LanguageTag);
        Assert.AreEqual(raw, result.CanonicalSelectorLexicalValue);
    }

    [TestMethod]
    public void PlainLanguageLiteralUsesRdfLangString()
    {
        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            "Gesetz",
            string.Empty,
            "DE");

        Assert.AreEqual(LuxembourgLiteralDisposition.Accepted, result.Disposition);
        Assert.AreEqual(LuxembourgLiteralCanonicalizer.RdfLangString, result.DatatypeIri);
        Assert.AreEqual("de", result.LanguageTag);
    }

    [TestMethod]
    [DataRow("a")]
    [DataRow("en-a")]
    [DataRow("en-0")]
    [DataRow("x")]
    [DataRow("de-1901-1901")]
    [DataRow("sl-rozaj-rozaj")]
    public void MalformedLanguageTagsRemainTypedQuarantine(string languageTag)
    {
        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            "texte",
            LuxembourgLiteralCanonicalizer.RdfLangString,
            languageTag);

        Assert.AreEqual(LuxembourgLiteralDisposition.TypedQuarantine, result.Disposition);
        Assert.AreEqual(LuxembourgLiteralReason.TypedQuarantineIllTyped, result.Reason);
        Assert.IsNull(result.CanonicalSelectorLexicalValue);
        Assert.AreEqual(languageTag, result.RawLanguageTagOrEmpty);
    }

    [TestMethod]
    [DataRow("x-private")]
    [DataRow("en-a-value")]
    [DataRow("zh-Hant-TW")]
    public void WellFormedPrivateExtensionAndScriptTagsAreAccepted(string languageTag)
    {
        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            "texte",
            LuxembourgLiteralCanonicalizer.RdfLangString,
            languageTag);

        Assert.AreEqual(LuxembourgLiteralDisposition.Accepted, result.Disposition);
        Assert.AreEqual(languageTag.ToLowerInvariant(), result.LanguageTag);
    }

    [TestMethod]
    public void XsdDateUsesDatatypeValueMappingInsteadOfCallerSpelling()
    {
        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            "2026-08-31+00:00",
            LuxembourgLiteralCanonicalizer.XsdDate,
            string.Empty);

        Assert.AreEqual(LuxembourgLiteralDisposition.Accepted, result.Disposition);
        Assert.AreEqual(LuxembourgLiteralReason.AcceptedXsdDateCanonical, result.Reason);
        Assert.AreEqual("2026-08-31+00:00", result.RawLexicalValue);
        Assert.AreEqual("2026-08-31Z", result.CanonicalSelectorLexicalValue);
    }

    [TestMethod]
    public void XsdDateCanonicalizesRecoverableFarTimezone()
    {
        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            "2002-10-10+13:00",
            LuxembourgLiteralCanonicalizer.XsdDate,
            string.Empty);

        Assert.AreEqual(LuxembourgLiteralDisposition.Accepted, result.Disposition);
        Assert.AreEqual("2002-10-09-11:00", result.CanonicalSelectorLexicalValue);
    }

    [TestMethod]
    public void IllTypedDateIsRetainedAsTypedQuarantine()
    {
        const string raw = "2026-02-30";

        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            raw,
            LuxembourgLiteralCanonicalizer.XsdDate,
            string.Empty);

        Assert.AreEqual(LuxembourgLiteralDisposition.TypedQuarantine, result.Disposition);
        Assert.AreEqual(LuxembourgLiteralReason.TypedQuarantineIllTyped, result.Reason);
        Assert.AreEqual(raw, result.RawLexicalValue);
        Assert.IsNull(result.CanonicalSelectorLexicalValue);
    }

    [TestMethod]
    public void UnsupportedDatatypeIsRetainedAsTypedQuarantine()
    {
        const string datatype = "http://www.w3.org/2001/XMLSchema#boolean";

        var result = LuxembourgLiteralCanonicalizer.Canonicalize("1", datatype, string.Empty);

        Assert.AreEqual(LuxembourgLiteralDisposition.TypedQuarantine, result.Disposition);
        Assert.AreEqual(
            LuxembourgLiteralReason.TypedQuarantineUnsupportedDatatype,
            result.Reason);
        Assert.AreEqual("1", result.RawLexicalValue);
        Assert.AreEqual(datatype, result.RawDatatypeIriOrEmpty);
        Assert.IsNull(result.CanonicalSelectorLexicalValue);
    }

    [TestMethod]
    public void ContextDependentDatatypeIsRetainedAsTypedQuarantine()
    {
        const string datatype = "http://www.w3.org/2001/XMLSchema#QName";

        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            "jolux:Act",
            datatype,
            string.Empty);

        Assert.AreEqual(LuxembourgLiteralDisposition.TypedQuarantine, result.Disposition);
        Assert.AreEqual(
            LuxembourgLiteralReason.TypedQuarantineContextDependentDatatype,
            result.Reason);
        Assert.IsNull(result.CanonicalSelectorLexicalValue);
    }

    [TestMethod]
    public void IncompatibleDatatypeAndLanguageTagIsRetainedAsIllTyped()
    {
        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            "texte",
            LuxembourgLiteralCanonicalizer.XsdString,
            "fr");

        Assert.AreEqual(LuxembourgLiteralDisposition.TypedQuarantine, result.Disposition);
        Assert.AreEqual(LuxembourgLiteralReason.TypedQuarantineIllTyped, result.Reason);
        Assert.IsNull(result.CanonicalSelectorLexicalValue);
        Assert.AreEqual("fr", result.RawLanguageTagOrEmpty);
    }

    [TestMethod]
    public void InvalidStringLexicalValueIsRetainedAsIllTyped()
    {
        const string raw = "before\0after";

        var result = LuxembourgLiteralCanonicalizer.Canonicalize(
            raw,
            LuxembourgLiteralCanonicalizer.XsdString,
            string.Empty);

        Assert.AreEqual(LuxembourgLiteralDisposition.TypedQuarantine, result.Disposition);
        Assert.AreEqual(LuxembourgLiteralReason.TypedQuarantineIllTyped, result.Reason);
        Assert.AreEqual(raw, result.RawLexicalValue);
        Assert.IsNull(result.CanonicalSelectorLexicalValue);
    }
}
