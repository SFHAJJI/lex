using Lex.Derive;
using System.Text.Json.Nodes;
using Xunit;

namespace Lex.Tests;

// fmx4-eu/1 extraction contract: Formex structural boundaries, anchors continuous with
// xhtml-eu/1 (art_N / anx_<roman>), publisher numbering preserved, tables to GFM,
// footnotes never inline, publisher quote characters honoured (QUOT CODE = hex codepoint).
public class Fmx4Tests
{
    private const string Fmx = """
        <?xml version="1.0" encoding="utf-8"?>
        <CONS.ACT xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
        <INFO.CONSLEG CONSLEG.REF="2016R0001" CONSLEG.DATE="20180523" START.DATE="20160504" END.DATE="99999999"/>
        <TITLE><TI><P>Regulation (EU) 2016/1 (consolidated)</P></TI></TITLE>
        <PREAMBLE><PREAMBLE.INIT>THE EUROPEAN PARLIAMENT,</PREAMBLE.INIT></PREAMBLE>
        <ENACTING.TERMS>
        <DIVISION><TITLE><TI><P><HT TYPE="ITALIC">CHAPTER I</HT></P></TI><STI><P><HT TYPE="BOLD">General provisions</HT></P></STI></TITLE>
        <ARTICLE IDENTIFIER="001"><TI.ART>Article 1</TI.ART><STI.ART>Scope</STI.ART>
          <PARAG IDENTIFIER="001.001"><NO.PARAG>1.</NO.PARAG><ALINEA>This Regulation   lays down
             rules.</ALINEA></PARAG>
          <PARAG IDENTIFIER="001.002"><NO.PARAG>2.</NO.PARAG><ALINEA><TXT>It applies to:</TXT>
            <LIST TYPE="alpha"><ITEM><NP><NO.P>(a)</NO.P><TXT>controllers;</TXT></NP></ITEM>
            <ITEM><NP><NO.P>(b)</NO.P><TXT>processors established in the <QUOT.START CODE="2018"/>Union<QUOT.END CODE="2019"/>.</TXT></NP></ITEM></LIST></ALINEA></PARAG>
        </ARTICLE>
        <ARTICLE IDENTIFIER="002"><TI.ART>Article 2</TI.ART><STI.ART>Ratios</STI.ART>
          <PARAG IDENTIFIER="002.001"><NO.PARAG>1.</NO.PARAG><ALINEA>Where <HT TYPE="ITALIC">EL</HT><HT TYPE="SUB">BE</HT> applies<NOTE NOTE.ID="E0001"><P>A footnote that must never appear inline.</P></NOTE>, the formula <FORMULA><HT TYPE="ITALIC">RW</HT> = max <EXPR TYPE="BRACKET">12.5 &#183; <HT TYPE="ITALIC">LGD</HT><IND LOC="SUB">i</IND></EXPR></FORMULA> is used as set out in Table 1.</ALINEA></PARAG>
          <PARAG IDENTIFIER="002.002"><NO.PARAG>2.</NO.PARAG><ALINEA><TBL COLS="2" NO.SEQ="0001"><TITLE><TI><P>Table 1</P></TI></TITLE>
            <CORPUS><ROW><CELL COL="1" TYPE="HEADER">Step</CELL><CELL COL="2" TYPE="HEADER">Weight</CELL></ROW>
            <ROW><CELL COL="1">1</CELL><CELL COL="2">20 %</CELL></ROW></CORPUS></TBL></ALINEA></PARAG>
        </ARTICLE>
        </DIVISION>
        </ENACTING.TERMS>
        <CONS.ANNEX><TITLE><TI><P>ANNEX I</P></TI></TITLE>
          <CONTENTS><GR.SEQ><TITLE><TI><P>Classification</P></TI></TITLE>
          <NP><NO.P>1.</NO.P><TXT>Full risk items.</TXT></NP>
          <NP><NO.P>2.</NO.P><TXT>Medium risk items.</TXT></NP></GR.SEQ></CONTENTS></CONS.ANNEX>
        </CONS.ACT>
        """;

    [Fact]
    public void Anchors_continue_the_xhtml_convention()
    {
        var x = Fmx4EuProfile.Extract(Fmx, "eu-eurlex:test:2016-05-04");
        Assert.Equal(["art_1", "art_2", "anx_i"], x.Provisions.Select(p => p.Anchor));
        Assert.Equal("article", x.Provisions[0].Type);
        Assert.Equal("annex", x.Provisions[2].Type);
    }

    [Fact]
    public void Numbering_headings_and_paths_are_plain_text()
    {
        var x = Fmx4EuProfile.Extract(Fmx, "lex:test");
        var a1 = x.Provisions.Single(p => p.Anchor == "art_1");
        Assert.Equal("Article 1", a1.Num);
        Assert.Equal("Scope", a1.Heading);
        Assert.Equal(["CHAPTER I — General provisions"], a1.Path);   // HT emphasis stripped
        var annex = x.Provisions.Single(p => p.Anchor == "anx_i");
        Assert.Null(annex.Num);
        Assert.Equal("ANNEX I", annex.Heading);
    }

    [Fact]
    public void Paragraph_numbers_lists_and_whitespace()
    {
        var x = Fmx4EuProfile.Extract(Fmx, "lex:test");
        var a1 = x.Provisions.Single(p => p.Anchor == "art_1");
        Assert.Contains("**1.** This Regulation lays down rules.", a1.TextMd);
        Assert.Contains("(a) controllers;", a1.TextMd);
        Assert.Contains("(b) processors established in the ‘Union’.", a1.TextMd);   // QUOT CODE chars
        // a TXT intro followed by a LIST inside one ALINEA must render exactly once
        Assert.Equal(1, a1.TextMd.Split("It applies to:").Length - 1);
    }

    [Fact]
    public void Tables_become_gfm_and_footnotes_stay_out()
    {
        var x = Fmx4EuProfile.Extract(Fmx, "lex:test");
        var a2 = x.Provisions.Single(p => p.Anchor == "art_2");
        Assert.Contains("| Step | Weight |", a2.TextMd);
        Assert.Contains("| --- | --- |", a2.TextMd);
        Assert.Contains("| 1 | 20 % |", a2.TextMd);
        Assert.DoesNotContain("footnote", a2.TextMd);
        Assert.Contains(x.Notes, n => n.Contains("footnotes omitted"));
    }

    [Fact]
    public void Formulas_flatten_deterministically()
    {
        var x = Fmx4EuProfile.Extract(Fmx, "lex:test");
        var a2 = x.Provisions.Single(p => p.Anchor == "art_2");
        Assert.Contains("*RW* = max [12.5 · *LGD*_i]", a2.TextMd);
        Assert.Contains("*EL*_BE applies", a2.TextMd);
    }

    [Fact]
    public void Annex_contents_are_extracted()
    {
        var x = Fmx4EuProfile.Extract(Fmx, "lex:test");
        var annex = x.Provisions.Single(p => p.Anchor == "anx_i");
        Assert.Contains("**Classification**", annex.TextMd);
        Assert.Contains("1. Full risk items.", annex.TextMd);
        Assert.Contains("2. Medium risk items.", annex.TextMd);
    }

    [Fact]
    public void Spans_address_the_markdown()
    {
        var x = Fmx4EuProfile.Extract(Fmx, "lex:test");
        foreach (var p in x.Provisions)
        {
            var slice = x.Markdown.Substring(p.MdStart, p.MdEnd - p.MdStart);
            Assert.Equal(p.TextMd, slice);
        }
    }

    [Fact]
    public void Profile_fmx4_eu_1_is_frozen()
    {
        // Profile permanence (SCHEMA.md): fmx4-eu/1 output over a fixed input never changes.
        // If this test fails, you have edited a published profile — make a NEW profile
        // (fmx4-eu/2) instead; citations pinned under fmx4-eu/1 must verify forever.
        var x = Fmx4EuProfile.Extract(Fmx, "lex:frozen");
        var combined = string.Join("|", x.Provisions.Select(p => $"{p.Anchor}:{p.TextSha256}"));
        var pinned = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined)));
        Assert.Equal("5cdf47db9b187db61ecd01d1660dec168ec9b6ad4cc3247d60caa9358cd787ca", pinned);
    }

    [Fact]
    public void Extraction_is_deterministic()
    {
        var x1 = Fmx4EuProfile.Extract(Fmx, "lex:test");
        var x2 = Fmx4EuProfile.Extract(Fmx, "lex:test");
        Assert.Equal(x1.Markdown, x2.Markdown);
        Assert.Equal(
            x1.Provisions.Select(p => (p.Anchor, p.TextSha256, p.MdStart, p.MdEnd)),
            x2.Provisions.Select(p => (p.Anchor, p.TextSha256, p.MdStart, p.MdEnd)));
    }

    [Fact]
    public void Formex_recovers_a_textless_version_even_when_an_older_version_uses_html()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-fmx-recovery-{Guid.NewGuid():N}");
        var corpus = Path.Combine(root, "corpus");
        var output = Path.Combine(root, "articles");
        try
        {
            var work = Path.Combine(corpus, "works", "32000r0001");
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "meta.json"), "{\"title\":\"Recovery fixture\"}");

            var htmlVersion = Path.Combine(work, "versions", "2020-01-01");
            Directory.CreateDirectory(htmlVersion);
            File.WriteAllText(Path.Combine(htmlVersion, "meta.json"), VersionMeta("en.html"));
            File.WriteAllText(Path.Combine(htmlVersion, "en.html"), """
                <html><body><p class="title-article-norm">Article 1</p>
                <p>Original publisher wording.</p></body></html>
                """);

            var fmxVersion = Path.Combine(work, "versions", "2021-01-01");
            Directory.CreateDirectory(Path.Combine(fmxVersion, "en.fmx4"));
            File.WriteAllText(Path.Combine(fmxVersion, "meta.json"), VersionMeta("en.fmx4/main.xml"));
            File.WriteAllText(Path.Combine(fmxVersion, "en.fmx4", "main.xml"), Fmx);

            var stats = DeriveWriter.Derive(corpus, output, "eu-eurlex");

            Assert.Empty(stats.Errors);
            var recovered = Path.Combine(output, "eu-eurlex", "works", "32000r0001",
                "versions", "2021-01-01", "en.json");
            Assert.True(File.Exists(recovered), "publisher Formex must fill an otherwise textless expression");
            var json = JsonNode.Parse(File.ReadAllText(recovered))!;
            Assert.Equal(Fmx4EuProfile.ProfileId, json["generator"]?["profile"]?.GetValue<string>());
            Assert.NotEmpty(json["provisions"]?.AsArray() ?? []);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string VersionMeta(string observationFile) => $$"""
        {
          "work_identifier": "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32000R0001",
          "expressions": [{
            "language": "en",
            "source_uri": "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32000R0001",
            "observations": [{ "file": "{{observationFile}}", "sha256": "fixture" }]
          }]
        }
        """;
}
