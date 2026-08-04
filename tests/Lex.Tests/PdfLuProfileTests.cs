using Lex.Derive;

namespace Lex.Tests;

/// <summary>
/// Freezes the segmentation of pdf-lu/1 (D49).
///
/// These run over page text rather than over a PDF, on purpose. Reading glyphs is PdfPig's job and
/// PdfPig is version-pinned; deciding where an article begins is ours, it is inferred from
/// typography rather than read from publisher markup, and it is the part that can go wrong without
/// anyone noticing. Every case below is a real shape taken from Legilux consolidated PDFs.
/// </summary>
public class PdfLuProfileTests
{
    private const string Header = "loi du 7 mars 1980 Version consolidee au 11 mars 2025";

    private static string Page(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void Articles_are_cut_at_line_starts_and_keep_publisher_numbering()
    {
        var ex = PdfLuProfile.FromPages([Page(
            "Titre Ier — Du pouvoir judiciaire",
            "Art. 1er.",
            "La justice est rendue au nom du Grand-Duc.",
            "Art. 13bis.",
            "Un magistrat delegue peut sieger.",
            "Art.4.",
            "Sans espace apres le point, comme le publie Legilux.")]);

        Assert.Equal(["art_1er", "art_13bis", "art_4"], ex.Provisions.Select(p => p.Anchor));
        Assert.Equal(["Art. 1er.", "Art. 13bis.", "Art. 4."], ex.Provisions.Select(p => p.Num));
        Assert.Equal("La justice est rendue au nom du Grand-Duc.", ex.Provisions[0].TextMd);
    }

    [Fact]
    public void Hyphenated_articles_do_not_collide_with_their_base_number()
    {
        // Art. 7-1 is inserted between 7 and 8. Capturing only the leading digits made it a second
        // "Art. 7", which the duplicate guard then renamed art_7__2, hiding a real article.
        var ex = PdfLuProfile.FromPages([Page(
            "Art. 7.",
            "Sanctions applicables aux entreprises.",
            "Art. 7-1.",
            "Disposition procedurale relative aux delais.",
            "Art. 8.",
            "Les significations sont valablement faites.")]);

        Assert.Equal(["art_7", "art_7-1", "art_8"], ex.Provisions.Select(p => p.Anchor));
        Assert.DoesNotContain(ex.Provisions, p => p.Anchor.Contains("__"));
    }

    [Fact]
    public void A_rubric_on_the_number_line_becomes_the_heading_not_the_first_sentence()
    {
        var ex = PdfLuProfile.FromPages([Page(
            "Art. 6. Sanctions a l'egard des personnes physiques",
            "(1) Les infractions sont punies d'une amende.",
            "Art. 9.",
            "(1) Le present article n'a pas de rubrique.")]);

        Assert.Equal("Sanctions a l'egard des personnes physiques", ex.Provisions[0].Heading);
        Assert.StartsWith("(1) Les infractions", ex.Provisions[0].TextMd);
        Assert.Null(ex.Provisions[1].Heading);
    }

    [Fact]
    public void Containers_nest_by_level_even_when_the_publisher_skips_one()
    {
        // This act opens at Titre with no Livre above it, so Chapitre II must REPLACE Chapitre I
        // rather than sit beside it. Treating depth as a list index appended instead.
        var ex = PdfLuProfile.FromPages([Page(
            "Titre Ier — Du pouvoir judiciaire",
            "Chapitre I. — Des justices de paix",
            "Art. 1er.",
            "Premier.",
            "Chapitre II. — Des tribunaux d'arrondissement",
            "Art. 10.",
            "Dixieme.",
            "Titre II — Dispositions generales",
            "Art. 90.",
            "Nonantieme.")]);

        Assert.Equal(["Titre Ier — Du pouvoir judiciaire", "Chapitre I. — Des justices de paix"],
                     ex.Provisions[0].Path);
        Assert.Equal(["Titre Ier — Du pouvoir judiciaire", "Chapitre II. — Des tribunaux d'arrondissement"],
                     ex.Provisions[1].Path);
        Assert.Equal(["Titre II — Dispositions generales"], ex.Provisions[2].Path);
    }

    [Fact]
    public void A_cross_reference_that_starts_a_line_is_not_a_container()
    {
        // "Section 3 du present reglement" has no dash, so it stays inside the body it belongs to.
        var ex = PdfLuProfile.FromPages([Page(
            "Chapitre I. — Des justices de paix",
            "Art. 1er.",
            "Le juge applique les regles fixees par",
            "Section 3 du present reglement grand-ducal.")]);

        Assert.Single(ex.Provisions);
        Assert.Contains("Section 3 du present reglement", ex.Provisions[0].TextMd);
        Assert.Equal(["Chapitre I. — Des justices de paix"], ex.Provisions[0].Path);
    }

    [Fact]
    public void Running_headers_are_removed_and_front_matter_is_dropped()
    {
        var pages = new List<string>();
        for (var i = 0; i < 6; i++)
            pages.Add(Page(Header, i == 0 ? "Texte consolide" : $"Suite {i}.",
                           i == 0 ? "La consolidation consiste a integrer les modifications." : "Encore du texte.",
                           i == 0 ? "Art. 1er." : $"Art. {i + 1}.",
                           "Corps de l'article."));

        var ex = PdfLuProfile.FromPages(pages);

        Assert.DoesNotContain(ex.Provisions, p => p.TextMd.Contains("Version consolidee"));
        Assert.DoesNotContain("La consolidation consiste", ex.Markdown);
        Assert.Equal("art_1er", ex.Provisions[0].Anchor);
        Assert.Contains(ex.Notes, n => n.Contains("running header"));
    }

    [Fact]
    public void A_document_with_no_article_is_refused_rather_than_half_extracted()
    {
        var ex = PdfLuProfile.FromPages([Page("Texte consolide", "Aucun article ici.")]);

        Assert.Empty(ex.Provisions);
        Assert.Contains(ex.Notes, n => n.Contains("coverage gap"));
    }

    [Fact]
    public void Extraction_is_byte_stable_across_runs()
    {
        // The hash chain rests on this: same input, same provisions, same text_sha256, forever.
        string[] pages = [Page(
            "Titre Ier — Du pouvoir judiciaire",
            "Art. 1er.",
            "La justice est rendue au nom du Grand-Duc.",
            "Art. 2.",
            "(1) Les juges sont nommes a vie.")];

        var a = PdfLuProfile.FromPages(pages);
        var b = PdfLuProfile.FromPages(pages);

        Assert.Equal(a.Markdown, b.Markdown);
        Assert.Equal(a.Provisions.Select(p => p.TextSha256), b.Provisions.Select(p => p.TextSha256));
        // Frozen fingerprint: this value changing means the profile changed, which is a new id.
        Assert.Equal("La justice est rendue au nom du Grand-Duc.", a.Provisions[0].TextMd);
        Assert.Equal(MdUtilProbe.Sha256("La justice est rendue au nom du Grand-Duc."), a.Provisions[0].TextSha256);
    }
}

/// The derive layer keeps its hashing helper internal; this mirrors it so a test can assert the
/// stored digest is a digest of the stored text and not of something else.
internal static class MdUtilProbe
{
    public static string Sha256(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
