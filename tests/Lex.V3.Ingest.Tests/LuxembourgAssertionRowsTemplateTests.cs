using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The assertion-rows template's datatype term is total for every literal (the Luxembourg population run of 2026-10-05).
/// Legilux's engine does not answer <c>DATATYPE()</c> with <c>rdf:langString</c> for a language-tagged literal, so the
/// template's raw <c>STR(DATATYPE(?object))</c> left <c>datatype_iri</c>, and <c>key_5</c> with it, unbound on every
/// language-tagged <c>jolux:title</c>. 12 of 997 and 8 of 997 rows of two retained assertion pages had that shape. A page
/// with such a row was refused <c>page_decode_failed_on_our_side</c>, so <c>legislative-loi-a</c> and
/// <c>legislative-rgd-a</c> could never be proven, and the run was refused, in both runs.
/// </summary>
[TestClass]
public sealed class LuxembourgAssertionRowsTemplateTests
{
    private const string RdfLangString = "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString";

    [TestMethod]
    public void TheDatatypeTermIsBoundForALanguageTaggedLiteralThePublisherDoesNotType()
    {
        var (plan, _, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var template = plan.QueryTemplates.Single(static candidate => candidate.TemplateId == "assertion-rows");

        // The column is COALESCEd, not only the key: it is part of the canonical key the delivery proof requires bound,
        // and the decoder refuses an unbound datatype term. key_5 is the column.
        StringAssert.Contains(template.Utf8QueryTemplate,
            "BIND(IF(isLiteral(?object), COALESCE(STR(DATATYPE(?object)), \"\"), \"\") AS ?datatype_iri)");
        StringAssert.Contains(template.Utf8QueryTemplate, "BIND(?datatype_iri AS ?key_5)");
        Assert.IsFalse(template.Utf8QueryTemplate.Contains("IF(isLiteral(?object), STR(DATATYPE(?object))", StringComparison.Ordinal),
            "no datatype term the publisher can leave unbound");

        // Nothing is lost by the empty datatype: the language tag is non-empty for exactly those literals, and the
        // canonicalizer reads the pair as rdf:langString, as SPARQL 1.1 types it. A typed literal keeps its datatype.
        var title = LuxembourgLiteralCanonicalizer.Canonicalize(
            "Loi du 17 brumaire an V (7 novembre 1792) relative à la répartition des contributions directes.", "", "fr");
        Assert.AreEqual(RdfLangString, title.DatatypeIri);
        Assert.AreEqual("fr", title.LanguageTag);
        var typed = LuxembourgLiteralCanonicalizer.Canonicalize(
            "LOI des 16-24 août 1790 Sur l'Organisation judiciaire.", "http://www.w3.org/2001/XMLSchema#string", "");
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#string", typed.DatatypeIri);
    }
}
