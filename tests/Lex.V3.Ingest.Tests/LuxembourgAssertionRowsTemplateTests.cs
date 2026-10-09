using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The assertion-rows template's datatype term is total for every literal (the Luxembourg population run of 2026-10-05).
/// Legilux's engine does not answer <c>DATATYPE()</c> with <c>rdf:langString</c> for a language-tagged literal, so the
/// template's raw <c>STR(DATATYPE(?object))</c> left <c>datatype_iri</c>, and <c>key_5</c> with it, unbound on every
/// language-tagged <c>jolux:title</c>. 12 of 997 and 8 of 997 rows of two retained assertion pages had that shape. A page
/// with such a row was refused <c>page_decode_failed_on_our_side</c>, so <c>legislative-loi-a</c> and
/// <c>legislative-rgd-a</c> could never be proven, and the run was refused, in both runs.
/// The next run, with that fixed, refused a <c>legislative-loi-a</c> page on its object key: a <c>jolux:title</c> of 2,678
/// UTF-8 bytes, over the cursor's 2,047-byte key part, so <c>key_4</c> is now a digest of the object.
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

    [TestMethod]
    public void TheObjectKeyIsADigestSoALiteralLongerThanAKeyPartStillResumesTheCursor()
    {
        var (plan, _, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var template = plan.QueryTemplates.Single(static candidate => candidate.TemplateId == "assertion-rows");

        // Page and count select over the same keys, so both carry the digest; the object itself stays in its own column.
        const string digestKey = "BIND(IF(isIRI(?object) || isLiteral(?object), SHA256(STR(?object)), \"\") AS ?key_4)";
        StringAssert.Contains(template.Utf8QueryTemplate, digestKey);
        StringAssert.Contains(template.Utf8CountTemplate, digestKey);
        Assert.IsFalse(template.Utf8QueryTemplate.Contains("STR(?object), \"\") AS ?key_4", StringComparison.Ordinal),
            "no object key as long as the object");
        StringAssert.Contains(template.Utf8QueryTemplate, "SELECT DISTINCT ?subject ?predicate ?object ");

        // The measured title's length: 2,678 UTF-8 bytes cannot be a cursor key part; its digest always can.
        var title = new string('é', 1_339);
        Assert.AreEqual(2_678, Encoding.UTF8.GetByteCount(title));
        const string subject = "http://data.legilux.public.lu/eli/etat/leg/loi/2005/06/21/n2/jo";
        const string predicate = "http://data.legilux.public.lu/resource/ontology/jolux#title";
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgQueryCursor(subject, predicate, "literal", title, "", "fr"));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(title)));
        var cursor = new LuxembourgQueryCursor(subject, predicate, "literal", digest, "", "fr");
        Assert.AreEqual(64, Encoding.UTF8.GetByteCount(cursor.Key4));
    }
}
