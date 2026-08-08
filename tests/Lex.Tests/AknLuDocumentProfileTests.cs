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

        Assert.Equal(AknLuProfile.ProfileId, result.ProfileId);
        Assert.Equal("art_1", Assert.Single(result.Extraction.Provisions).Anchor);
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
}
