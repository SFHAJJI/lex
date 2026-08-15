using Lex.Derive;

namespace Lex.Tests;

public sealed class AknLuDocumentProfileTests
{
    private const string Notice = """
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0">
          <act>
            <meta><notes><note><content><p>Metadata title must not become body text.</p></content></note></notes></meta>
            <body>
              <alinea><content><p>Il est porté à la connaissance du public.</p></content></alinea>
              <alinea><content><table><tr><th>Nom</th><th>Date</th></tr><tr><td>Alice</td><td>2025</td></tr></table></content></alinea>
            </body>
          </act>
        </akomaNtoso>
        """;

    [Fact]
    public void Articleless_official_Akn_body_becomes_one_document_level_provision()
    {
        Assert.Empty(AknLuProfile.Extract(Notice, "lu-legilux:pa-2025-01-01-a1:2025-01-01").Provisions);

        var result = StructuredTextExtractor.Extract(Notice, "lu-legilux:pa-2025-01-01-a1:2025-01-01");

        Assert.Equal(AknLuDocumentProfile.ProfileId, result.ProfileId);
        var provision = Assert.Single(result.Extraction.Provisions);
        Assert.Equal("document", provision.Anchor);
        Assert.Equal("document", provision.Type);
        Assert.Contains("Il est porté à la connaissance du public.", provision.TextMd);
        Assert.Contains("| Nom | Date |", provision.TextMd);
        Assert.DoesNotContain("Metadata title", provision.TextMd);
        Assert.Contains(result.Extraction.Notes,
            note => note.StartsWith("publisher markup exposes no article or annex boundary", StringComparison.Ordinal));
    }

    [Fact]
    public void Ordinary_article_Akn_stays_on_the_frozen_profile()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="art_1"><num>Art. 1.</num><alinea><content><p>Texte.</p></content></alinea></article>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(xml, "lu-legilux:loi-2025-01-01-a1:2025-01-01");

        Assert.Equal(AknLuProfileV2.ProfileId, result.ProfileId);
        Assert.Equal("art_1", Assert.Single(result.Extraction.Provisions).Anchor);
    }

    [Fact]
    public void Identical_duplicate_Akn_attributes_are_repaired_for_parsing_only()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"
                xmlns:scl="http://www.scl.lu"><act><body>
              <article id="art_1"><num>Art. 1.</num><alinea><content>
                <table scl:cols-nb="5" scl:cols-nb="5"><tr><td>Texte.</td></tr></table>
              </content></alinea></article>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:loi-2025-01-01-a1:2025-01-01");

        Assert.Equal(AknLuDuplicateSclAttributeProfile.ProfileId, result.ProfileId);
        Assert.Contains("Texte.", Assert.Single(result.Extraction.Provisions).TextMd);
        Assert.Contains(result.Extraction.Notes,
            note => note.Contains("publisher evidence unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void Conflicting_duplicate_Akn_attributes_remain_invalid()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"
                xmlns:scl="http://www.scl.lu"><act><body>
              <article id="art_1"><num>Art. 1.</num><alinea><content>
                <table scl:cols-nb="5" scl:cols-nb="6"><tr><td>Texte.</td></tr></table>
              </content></alinea></article>
            </body></act></akomaNtoso>
            """;

        Assert.Throws<System.Xml.XmlException>(() => StructuredTextExtractor.Extract(
            xml, "lu-legilux:loi-2025-01-01-a1:2025-01-01"));
    }

    [Fact]
    public void Identical_duplicate_non_presentation_attributes_remain_invalid()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="art_1" id="art_1"><num>Art. 1.</num><alinea><content><p>Texte.</p></content></alinea></article>
            </body></act></akomaNtoso>
            """;

        Assert.Throws<System.Xml.XmlException>(() => StructuredTextExtractor.Extract(
            xml, "lu-legilux:loi-2025-01-01-a1:2025-01-01"));
    }

    [Fact]
    public void More_than_one_identical_publisher_defect_remains_invalid()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"
                xmlns:scl="http://www.scl.lu"><act><body>
              <article id="art_1"><num>Art. 1.</num><alinea><content>
                <table scl:cols-nb="5" scl:cols-nb="5"><tr><td>Un.</td></tr></table>
                <table scl:cols-nb="5" scl:cols-nb="5"><tr><td>Deux.</td></tr></table>
              </content></alinea></article>
            </body></act></akomaNtoso>
            """;

        Assert.Throws<System.Xml.XmlException>(() => StructuredTextExtractor.Extract(
            xml, "lu-legilux:loi-2025-01-01-a1:2025-01-01"));
    }

    [Fact]
    public void Official_empty_Akn_articles_are_preserved_and_typed_while_labeled_ones_remain_measured()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="anonymous" wId="/eli/etat/leg/loi/2025/01/01/n1/art_1"><num/><alinea><content><p/></content></alinea></article>
              <article id="labeled" wId="/eli/etat/leg/loi/2025/01/01/n1/art_185"><num>Art. 185.</num><alinea><content><p/></content></alinea></article>
              <article id="real" wId="/eli/etat/leg/loi/2025/01/01/n1/art_186"><num>Art. 186.</num><alinea><content><p>Texte.</p></content></alinea></article>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:recueil-cours_tribunaux:2026-09-16");

        Assert.Equal(AknLuProfileV2.ProfileId, result.ProfileId);
        Assert.Equal(["labeled", "real"],
            result.Extraction.Provisions.Select(p => p.Anchor));
        var structural = Assert.Single(result.Extraction.PublisherStructuralEmptyArticles ?? []);
        Assert.Equal("anonymous", structural.Anchor);
        Assert.Equal("/eli/etat/leg/loi/2025/01/01/n1/art_1", structural.WId);
        Assert.DoesNotContain("anonymous", result.Extraction.Markdown, StringComparison.Ordinal);
        Assert.Empty(result.Extraction.Provisions.Single(p => p.Anchor == "labeled").TextMd);
        Assert.Contains(result.Extraction.Notes,
            note => note == "1 official publisher-structural empty article(s) preserved outside searchable provisions");

        var frozenV1 = AknLuProfile.Extract(
            xml, "lu-legilux:recueil-cours_tribunaux:2026-09-16");
        Assert.Equal(["anonymous", "labeled", "real"],
            frozenV1.Provisions.Select(provision => provision.Anchor));
        Assert.Empty(frozenV1.PublisherStructuralEmptyArticles ?? []);
    }

    [Fact]
    public void Anonymous_Akn_article_with_non_text_legal_content_is_not_silently_omitted()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="scan" wId="/eli/etat/leg/loi/2025/01/01/n1/art_scan"><alinea><content><p><img src="publisher-scan.png"/></p></content></alinea></article>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:recueil-cours_tribunaux:2026-09-16");

        var provision = Assert.Single(result.Extraction.Provisions);
        Assert.Equal("scan", provision.Anchor);
        Assert.Empty(result.Extraction.PublisherStructuralEmptyArticles ?? []);
    }

    [Fact]
    public void Empty_Akn_article_with_an_unresolved_publisher_note_reference_is_coverage_only()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="art_N120BC" wId="/eli/etat/leg/loi/1979/12/20/n2/art_1bis_20040612/consolide/20040612">
                <num/><alinea><content><p><noteRef href="#M1" marker="3"/></p></content></alinea>
              </article>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:recueil-presse_medias:2025-11-25");

        Assert.Empty(result.Extraction.Provisions);
        Assert.Equal("art_N120BC",
            Assert.Single(result.Extraction.PublisherStructuralEmptyArticles ?? []).Anchor);
        Assert.DoesNotContain(result.Extraction.Notes,
            note => note.Contains("no article/annex elements found", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_Akn_article_with_a_resolved_note_reference_remains_an_ordinary_gap()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="art_N120BC" wId="/eli/etat/leg/loi/1979/12/20/n2/art_1bis_20040612/consolide/20040612">
                <num/><alinea><content><p><noteRef href="#M1" marker="1"/></p></content></alinea>
              </article>
              <authorialNote xml:id="M1"><p>Publisher note with legal text.</p></authorialNote>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:recueil-presse_medias:2025-11-25");

        Assert.Single(result.Extraction.Provisions);
        Assert.Empty(result.Extraction.PublisherStructuralEmptyArticles ?? []);
    }

    [Fact]
    public void Duplicate_official_empty_identities_are_not_typed()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="duplicate" wId="/eli/etat/leg/loi/2025/01/01/n1/art_1"><num/><alinea><content><p/></content></alinea></article>
              <article id="duplicate" wId="/eli/etat/leg/loi/2025/01/01/n1/art_1"><num/><alinea><content><p/></content></alinea></article>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:recueil-cours_tribunaux:2026-09-16");

        Assert.Equal(2, result.Extraction.Provisions.Count);
        Assert.Empty(result.Extraction.PublisherStructuralEmptyArticles ?? []);
    }

    [Fact]
    public void Unnumbered_empty_article_is_not_assumed_to_be_a_publisher_tombstone()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="empty" wId="/eli/etat/leg/loi/2025/01/01/n1/art_1"><alinea><content><p/></content></alinea></article>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:recueil-cours_tribunaux:2026-09-16");

        Assert.Single(result.Extraction.Provisions);
        Assert.Empty(result.Extraction.PublisherStructuralEmptyArticles ?? []);
    }

    [Fact]
    public void Repeated_empty_num_or_heading_wrappers_are_not_typed_as_publisher_coverage()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="num" wId="/eli/etat/leg/loi/2025/01/01/n1/art_1"><num><u/><u/></num><alinea><content><p/></content></alinea></article>
              <article id="heading" wId="/eli/etat/leg/loi/2025/01/01/n1/art_2"><num/><heading><b/><b/></heading><alinea><content><p/></content></alinea></article>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:recueil-cours_tribunaux:2026-09-16");

        Assert.Equal(AknLuProfileV2.ProfileId, result.ProfileId);
        Assert.Equal(["num", "heading"],
            result.Extraction.Provisions.Select(provision => provision.Anchor));
        Assert.Empty(result.Extraction.PublisherStructuralEmptyArticles ?? []);
    }

    [Fact]
    public void Profile_akn_lu_2_is_frozen()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="empty" wId="/eli/etat/leg/loi/2025/01/01/n1/art_1"><num/><alinea><content><p/></content></alinea></article>
              <article id="real" wId="/eli/etat/leg/loi/2025/01/01/n1/art_2"><num>Art. 2.</num><alinea><content><p>Texte.</p></content></alinea></article>
            </body></act></akomaNtoso>
            """;
        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:recueil-cours_tribunaux:2026-09-16");

        Assert.Equal(AknLuProfileV2.ProfileId, result.ProfileId);
        Assert.Equal("428e50af94f68fb4d1ce0ebb54c4358ee4a7c5b28c686c560e5f192e9b4e1083",
            Fingerprint(result));
    }

    [Fact]
    public void Profile_akn_lu_identical_scl_duplicate_1_is_frozen()
    {
        const string xml = """
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"
                xmlns:scl="http://www.scl.lu"><act><body>
              <article id="empty" wId="/eli/etat/leg/loi/2025/01/01/n1/art_0"><num/><alinea><content><p/></content></alinea></article>
              <article id="art_1"><num>Art. 1.</num><alinea><content>
                <table scl:cols-nb="5" scl:cols-nb="5"><tr><td>Texte.</td></tr></table>
              </content></alinea></article>
            </body></act></akomaNtoso>
            """;
        var result = StructuredTextExtractor.Extract(
            xml, "lu-legilux:loi-2025-01-01-a1:2025-01-01");

        Assert.Equal(AknLuDuplicateSclAttributeProfile.ProfileId, result.ProfileId);
        Assert.Equal("97b2e8b9126532c42ef1a992daba9bbe076cfbf2125ddc000f9136721f599074",
            Fingerprint(result));
    }

    [Fact]
    public void Profile_akn_lu_document_1_is_frozen()
    {
        var extraction = AknLuDocumentProfile.Extract(
            Notice,
            "lu-legilux:pa-2025-01-01-a1:2025-01-01");

        var combined = string.Join("|", extraction.Provisions.Select(
            provision => $"{provision.Anchor}:{provision.TextSha256}"));
        var pinned = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(combined)));

        Assert.Equal("80356361933c0fef1ea09b59b37819a86dc15ce64b790075205b5d63a543be4f", pinned);
    }

    private static string Fingerprint(StructuredTextExtractor.Result result)
    {
        var canonical = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            result.ProfileId,
            result.Extraction.Provisions,
            result.Extraction.Markdown,
            result.Extraction.Notes,
            PublisherStructuralEmptyArticles =
                result.Extraction.PublisherStructuralEmptyArticles ?? [],
        });
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            canonical));
    }
}
