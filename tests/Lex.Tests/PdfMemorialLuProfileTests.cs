using Lex.Derive;

namespace Lex.Tests;

/// <summary>
/// Freezes pdf-memorial-lu/1 (D49): one act, cut out of a whole issue of the official gazette.
///
/// The risk here is worse than in pdf-lu/1. There, a mistake misplaces an article boundary inside
/// a document that IS the act. Here, a mistake returns a DIFFERENT act's text under our law's name,
/// which is the single worst thing this product can do. So the cases below are mostly about
/// refusing rather than extracting.
/// </summary>
public class PdfMemorialLuProfileTests
{
    private const string LexId = "lu-legilux:loi-1973-02-19-n1:1992-03-30";

    private static string Issue(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void The_act_is_found_after_the_sommaire_and_the_marker()
    {
        var ex = PdfMemorialLuProfile.FromPages([Issue(
            "MEMORIAL Journal Officiel du Grand-Duche de Luxembourg",
            "Sommaire",
            "Texte coordonne du 29 octobre 1992 de la loi du 19 fevrier 1973 concernant la vente",
            "Texte coordonne",
            "Art. 1er.",
            "Le Grand-Duc reglementera la vente en gros.",
            "Art. 2.",
            "Les infractions sont punies.")], LexId);

        Assert.Equal(["art_1er", "art_2"], ex.Provisions.Select(p => p.Anchor));
        Assert.DoesNotContain(ex.Markdown, c => c == '�');
        Assert.StartsWith("Le Grand-Duc", ex.Provisions[0].TextMd);
    }

    [Fact]
    public void Tracked_out_typesetting_still_matches()
    {
        // Extraction turns the gazette's letter-spaced headings into "T exte coordonn e" and
        // "1 9 f e v r i e r". Matching on de-spaced text is the only reason those documents work,
        // and it is worth a test of its own because the failure is silent: the act simply is not
        // found and the version stays wordless.
        var ex = PdfMemorialLuProfile.FromPages([Issue(
            "Sommaire",
            "T exte coordonn e de la l o i d u 1 9 f e v r i e r 1 9 7 3 concernant la vente",
            "T exte coordonn e",
            "Art. 1er.",
            "Le Grand-Duc reglementera.")], LexId);

        Assert.Single(ex.Provisions);
        Assert.Equal("art_1er", ex.Provisions[0].Anchor);
    }

    [Fact]
    public void An_act_not_named_in_the_issue_yields_nothing()
    {
        // The wrong act's text under our law's name is the failure that matters. Silence is right.
        var ex = PdfMemorialLuProfile.FromPages([Issue(
            "Sommaire",
            "Texte coordonne de la loi du 4 aout 1975 sur autre chose",
            "Texte coordonne",
            "Art. 1er.",
            "Rien a voir avec notre loi.")], LexId);

        Assert.Empty(ex.Provisions);
        Assert.Contains(ex.Notes, n => n.Contains("not named"));
    }

    [Fact]
    public void The_next_act_ends_ours()
    {
        var ex = PdfMemorialLuProfile.FromPages([Issue(
            "Sommaire",
            "Texte coordonne de la loi du 19 fevrier 1973 concernant la vente",
            "Texte coordonne",
            "Art. 1er.",
            "Notre premier article.",
            "Art. 2.",
            "Notre second article.",
            "Loi du 4 aout 1975 concernant tout autre chose.",
            "Art. 1er.",
            "Un article qui ne nous appartient pas.")], LexId);

        Assert.Equal(2, ex.Provisions.Count);
        Assert.DoesNotContain(ex.Provisions, p => p.TextMd.Contains("ne nous appartient pas"));
        Assert.Contains(ex.Notes, n => n.Contains("next act"));
    }

    [Fact]
    public void The_first_of_the_month_is_written_1er()
    {
        // French writes "1er mars", never "1 mars". Getting this wrong lost fifteen documents.
        var needles = PdfMemorialLuProfile.Needles("lu-legilux:rgc-1974-03-01-n1:1992-09-15");
        Assert.Contains("reglementdugouvernementenconseildu1ermars1974", needles);

        var ordinary = PdfMemorialLuProfile.Needles("lu-legilux:loi-1973-02-19-n1:1992-03-30");
        Assert.Contains("loidu19fevrier1973", ordinary);
    }

    [Fact]
    public void The_date_alone_is_the_fallback_when_the_instrument_is_worded_differently()
    {
        var needles = PdfMemorialLuProfile.Needles(LexId);
        Assert.Equal(2, needles.Count);
        Assert.Equal("du19fevrier1973", needles[1]);
    }

    [Fact]
    public void A_marker_that_does_not_open_at_article_one_is_not_the_start()
    {
        // An issue carries several "texte coordonné" markers: in the Sommaire, in running heads,
        // and before each act. Taking the first one after the act is named started 24 of 171 real
        // versions in the middle of their own law, one of them at Art. 32 presented as the whole
        // act. A consolidated text always opens at Article 1, so a candidate that does not is the
        // wrong candidate.
        var ex = PdfMemorialLuProfile.FromPages([Issue(
            "Sommaire",
            "Texte coordonne de la loi du 19 fevrier 1973 concernant la vente",
            "Texte coordonne",
            "Art. 1er.",
            "Le vrai debut de la loi.",
            "Art. 2.",
            "La suite.",
            "Texte coordonne",                    // a later marker, mid-document
            "Art. 32.",
            "Un article tardif.")], LexId);

        Assert.Equal("art_1er", ex.Provisions[0].Anchor);
        Assert.Equal(3, ex.Provisions.Count);
    }

    [Fact]
    public void An_issue_with_no_text_opening_at_article_one_is_refused()
    {
        var ex = PdfMemorialLuProfile.FromPages([Issue(
            "Sommaire",
            "Texte coordonne de la loi du 19 fevrier 1973 concernant la vente",
            "Texte coordonne",
            "Art. 14.",
            "Le texte ne commence pas au debut.")], LexId);

        Assert.Empty(ex.Provisions);
        Assert.Contains(ex.Notes, n => n.Contains("Article 1"));
    }

    [Fact]
    public void A_gazette_with_no_text_layer_is_refused()
    {
        var ex = PdfMemorialLuProfile.FromPages([], LexId);
        Assert.Empty(ex.Provisions);
        Assert.Contains(ex.Notes, n => n.Contains("coverage gap"));
    }
}
