using Lex.Derive;
using Xunit;

namespace Lex.Tests;

// xhtml-eu/1 is named in SCHEMA.md as an immutable, permanently-runnable profile and is the
// fallback for every EU work without a Formex manifestation — and it had no test of any kind.
// The promise "re-run the pinned extractor and you get these bytes" was unenforced for a third
// of the extraction surface. These tests are what make it true.
//
// Two shapes matter: the modern Cellar format, where each article is a div.eli-subdivision
// carrying a publisher-minted id, and the older flat format with no wrappers, where articles
// are segmented on p.title-article-norm. The flat path is the hairiest state machine in
// Lex.Derive and drove 17 EU works that would otherwise have yielded nothing.
public class XhtmlEuTests
{
    private const string LegacyMalformed = """
        <html><head><meta http-equiv=Content-Type content="text/html"></head><body>
        <div><p class=title-article-norm>Article 1</p>
        <p class=stitle-article-norm>Scope</p>
        <p>Legacy body<br>with a void element.</p></div>
        </body></html>
        """;

    private const string PublisherFragmentWithoutArticleWrapper = """
        <!DOCTYPE html><html><head><title>Fragment</title></head>
        <body><p>Authoritative fragment wording.</p></body></html>
        """;

    [Fact]
    public void Structured_dispatch_uses_tolerant_profile_for_malformed_legacy_html()
    {
        var result = StructuredTextExtractor.Extract(LegacyMalformed, "lex:test");

        Assert.Equal(TolerantHtmlEuProfile.ProfileId, result.ProfileId);
        var provision = Assert.Single(result.Extraction.Provisions);
        Assert.Equal("art_1", provision.Anchor);
        Assert.Contains("Legacy body", provision.TextMd);
    }

    [Fact]
    public void Structured_dispatch_does_not_confuse_xhtml_xml_with_akoma_ntoso()
    {
        var result = StructuredTextExtractor.Extract(Flat, "lex:test");

        Assert.Equal(XhtmlEuProfile.ProfileId, result.ProfileId);
        Assert.NotEmpty(result.Extraction.Provisions);
    }

    [Fact]
    public void Structured_dispatch_preserves_document_fragments_without_article_boundaries()
    {
        var result = StructuredTextExtractor.Extract(PublisherFragmentWithoutArticleWrapper, "lex:test");

        Assert.Equal(TolerantHtmlEuProfile.ProfileId, result.ProfileId);
        var provision = Assert.Single(result.Extraction.Provisions);
        Assert.Equal("document", provision.Type);
        Assert.Equal("Authoritative fragment wording.", provision.TextMd);
        Assert.Contains("document-level fallback", Assert.Single(result.Extraction.Notes));
    }

    private const string Subdivision = """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><body>
        <div class="eli-container">
          <div id="cpt_I" class="eli-subdivision">
            <p class="title-division-1">CHAPTER I</p>
            <p class="title-division-2">General provisions</p>
            <div id="art_1" class="eli-subdivision">
              <p class="title-article-norm">Article 1</p>
              <p class="stitle-article-norm">Subject-matter</p>
              <div class="norm"><span class="no-parag">1.</span><p>This Regulation lays down rules.</p></div>
              <div class="grid-container">
                <div class="grid-list-column-1">(a)</div>
                <div class="grid-list-column-2"><p>controllers;</p></div>
              </div>
              <p class="modref"><a href="./mod.html" title="Amended by Reg 2018/1">M1</a></p>
            </div>
          </div>
          <div id="anx_I" class="eli-subdivision">
            <p class="title-annex-1">ANNEX I</p>
            <table><tr><th>Step</th><th>Weight</th></tr><tr><td>1</td><td>0 %</td></tr></table>
          </div>
        </div>
        </body></html>
        """;

    private const string Flat = """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><body><div>
        <p class="title-division-1">TITLE I</p>
        <p class="title-article-norm">Article 1</p>
        <p class="stitle-article-norm">Scope</p>
        <p>It applies to institutions.</p>
        <p class="title-article-norm">Article 2</p>
        <p>Definitions follow.</p>
        <p class="title-annex-1">ANNEX II</p>
        <p>Listed items.</p>
        </div></body></html>
        """;

    [Fact]
    public void Subdivision_format_uses_publisher_anchors_and_keeps_citations_out_of_the_text()
    {
        var x = XhtmlEuProfile.Extract(Subdivision, "lex:test");

        Assert.Equal(["art_1", "anx_I"], x.Provisions.Select(p => p.Anchor));
        var a1 = x.Provisions[0];
        Assert.Equal("Article 1", a1.Num);
        Assert.Equal("Subject-matter", a1.Heading);
        Assert.Equal("article", a1.Type);
        Assert.Contains("CHAPTER I", string.Join(" ", a1.Path));
        Assert.Contains("**1.** This Regulation lays down rules.", a1.TextMd);
        Assert.Contains("(a) controllers;", a1.TextMd);

        // Amendment arrows are provenance, not law: they must be structured, never inlined.
        Assert.DoesNotContain("M1", a1.TextMd);
        Assert.Contains(a1.Citations, c => c.Text.Contains("Amended by"));

        var annex = x.Provisions[1];
        Assert.Equal("annex", annex.Type);
        Assert.Contains("| Step | Weight |", annex.TextMd);   // GFM table
    }

    [Fact]
    public void Flat_format_still_yields_articles_when_there_are_no_subdivision_wrappers()
    {
        var x = XhtmlEuProfile.Extract(Flat, "lex:test");

        Assert.Equal(3, x.Provisions.Count);
        Assert.Equal("art_1", x.Provisions[0].Anchor);
        Assert.Equal("Scope", x.Provisions[0].Heading);
        Assert.Contains("It applies to institutions.", x.Provisions[0].TextMd);
        Assert.Equal("annex", x.Provisions[2].Type);
        Assert.Contains(x.Notes, n => n.Contains("flat format"));
    }

    [Fact]
    public void Extraction_is_deterministic()
    {
        var a = XhtmlEuProfile.Extract(Subdivision, "lex:test");
        var b = XhtmlEuProfile.Extract(Subdivision, "lex:test");
        Assert.Equal(a.Markdown, b.Markdown);
        Assert.Equal(a.Provisions.Select(p => p.TextSha256), b.Provisions.Select(p => p.TextSha256));
    }

    [Fact]
    public void Spans_address_the_provision_inside_the_markdown()
    {
        var x = XhtmlEuProfile.Extract(Subdivision, "lex:test");
        foreach (var p in x.Provisions)
        {
            var slice = string.Concat(x.Markdown.EnumerateRunes()
                .Skip(p.MdStart).Take(p.MdEnd - p.MdStart).Select(r => r.ToString()));
            Assert.Equal(p.TextMd, slice);
        }
    }

    // Profile permanence (SCHEMA.md): xhtml-eu/1's output over a fixed input must never change.
    // If this fails you have edited a published profile — ship a NEW one (xhtml-eu/2) instead,
    // because every citation pinned under xhtml-eu/1 has to keep verifying forever.
    [Fact]
    public void Profile_xhtml_eu_1_is_frozen()
    {
        var x = XhtmlEuProfile.Extract(Subdivision, "lex:frozen");
        var combined = string.Join("|", x.Provisions.Select(p => $"{p.Anchor}:{p.TextSha256}"));
        var pinned = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined)));
        Assert.Equal("a1ae9e20086de92f96e879e4d19b03f875c9ad19c6efdd46dd1f27c2a4f4564e", pinned);
    }
}
