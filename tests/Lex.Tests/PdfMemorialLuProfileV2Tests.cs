using Lex.Derive;

namespace Lex.Tests;

public sealed class PdfMemorialLuProfileV2Tests
{
    private const string WorkTitle =
        "Version consolidée applicable au 04/12/1991 : Loi du 14 février 1955 portant modification et coordination des dispositions légales et règlementaires en matière de baux à loyer.";

    private static string Issue(params string[] lines) => string.Join('\n', lines);

    [Fact]
    public void Leading_guillemet_and_Ier_are_normalized_after_a_verified_marker()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "Texte coordonné de l'arrêté grand-ducal du 27 août 1957 approuvant le règlement sur les pensions",
            "T e x t e  c o o r d o n n é",
            "«Art. Ier. Est approuvé le règlement sur les pensions.",
            "Art. 2. La suite.")],
            "lu-legilux:agd-1957-08-27-n1:1984-11-09",
            "Arrêté grand-ducal du 27 août 1957 approuvant le règlement sur les pensions.");

        Assert.Equal(["art_1er", "art_2"], result.Provisions.Select(provision => provision.Anchor));
        Assert.StartsWith("Est approuvé", result.Provisions[0].TextMd);
    }

    [Fact]
    public void Article_wording_is_supported_when_a_distinctive_title_fingerprint_matches()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "CONVENTION COORDONNEE INSTITUANT L'UNION ECONOMIQUE BELGO-LUXEMBOURGEOISE",
            "Article 1",
            "Il est institué une union économique.",
            "Article 2",
            "Les territoires forment une union douanière.",
            "Table de concordance de la convention d'union économique",
            "Article 1",
            "art. 1, art. 1",
            "Article 2",
            "art. 1, art. 2")],
            "lu-legilux:loi-1922-03-05-n1:1965-05-31",
            "Loi du 5 mars 1922 portant approbation de la Convention d'Union économique signée entre le Luxembourg et la Belgique.");

        Assert.Equal(["art_1", "art_2"], result.Provisions.Select(provision => provision.Anchor));
        Assert.StartsWith("Il est institué", result.Provisions[0].TextMd);
        Assert.Contains(result.Notes, note => note.Contains("title fingerprint", StringComparison.Ordinal));
    }

    [Fact]
    public void Premier_is_the_first_article_and_its_rubric_is_preserved()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "Texte coordonné de la loi du 17 juin 1994 concernant la sécurité et la santé des travailleurs",
            "Texte coordonné",
            "Art. premier - Objet",
            "La présente loi protège les travailleurs.",
            "Article 2 - Organismes de surveillance",
            "Les organismes assurent la surveillance.")],
            "lu-legilux:loi-1994-06-17-n3:1998-06-01",
            "Loi du 17 juin 1994 concernant la sécurité et la santé des travailleurs au travail.");

        var first = Assert.Single(result.Provisions, provision => provision.Anchor == "art_1er");
        Assert.Equal("Objet", first.Heading);
        Assert.Equal("Organismes de surveillance",
            Assert.Single(result.Provisions, provision => provision.Anchor == "art_2").Heading);
    }

    [Fact]
    public void Earlier_amending_act_is_not_mistaken_for_the_later_consolidated_text()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "Loi du 15 mars 1993 portant modification de la loi du 13 décembre 1988",
            "Article I",
            "L'article 1er de la loi du 13 décembre 1988 est modifié comme suit:",
            "«Art.1er. Citation appartenant à l'acte modificatif.",
            "Texte coordonné du 15 mars 1993 de la loi du 13 décembre 1988",
            "T e x t e  c o o r d o n n é",
            "«Art.1er. Texte consolidé demandé.",
            "Art.2. Suite consolidée.")],
            "lu-legilux:loi-1988-12-13-n2:1993-04-12",
            "Loi du 13 décembre 1988 instaurant un régime fiscal temporaire spécial.");

        Assert.Equal(2, result.Provisions.Count);
        Assert.Contains("Texte consolidé demandé", result.Provisions[0].TextMd);
        Assert.DoesNotContain("Citation appartenant", result.Markdown);
    }

    [Fact]
    public void Wrong_publisher_link_is_refused_even_when_that_issue_contains_articles()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "Règlement grand-ducal du 31 mars 1996 concernant un comité pour l'égalité",
            "Texte coordonné",
            "Art. 1er. Un autre acte.")],
            "lu-legilux:loi-1963-06-22-n1:2005-12-12",
            "Loi du 22 juin 1963 fixant le régime des traitements des fonctionnaires de l'Etat.");

        Assert.Empty(result.Provisions);
        Assert.Contains(result.Notes, note => note.Contains("identity", StringComparison.Ordinal));
    }

    [Fact]
    public void Strongly_identified_articleless_nomenclature_becomes_one_document_provision()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "Loi du 19 novembre 2003 modifiant une autre loi",
            "Art. unique.- Texte qui ne doit pas être attribué au règlement.",
            "Texte coordonné de la nomenclature des établissements classés, tel qu'il résulte du règlement",
            "grand-ducal du 16 juillet 1999 portant nomenclature et classification des établissements classés",
            "Les références entre crochets indiquent le règlement concerné.",
            "N° Désignation et classification des établissements classés Classe",
            "1. Abattage des animaux 3",
            "2. Abeilles dans les parties agglomérées 4")],
            "lu-legilux:rgd-1999-07-16-n1:2003-05-03",
            "Règlement grand-ducal du 16 juillet 1999 portant nomenclature et classification des établissements classés.");

        var provision = Assert.Single(result.Provisions);
        Assert.Equal("document", provision.Anchor);
        Assert.Equal("document", provision.Type);
        Assert.Contains("N° Désignation", provision.TextMd);
        Assert.DoesNotContain("autre loi", provision.TextMd);
        Assert.DoesNotContain("Art. unique", provision.TextMd);
    }

    [Fact]
    public void Nested_amendment_article_number_is_body_text_not_a_second_boundary()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "Texte coordonné de la loi du 30 juin 1976 portant création d'un fonds pour l'emploi",
            "Texte coordonné",
            "Art. 1er. Premier article.",
            "Art. 5. Cinquième article.",
            "Art. 39. La loi fiscale est modifiée comme suit:",
            "Art. 5. L'article 115 est complété par un numéro 10.",
            "Art. 40. p.m.")],
            "lu-legilux:loi-1976-06-30-n1:1987-06-03",
            "Loi du 30 juin 1976 portant création d'un fonds pour l'emploi.");

        Assert.Equal(["art_1er", "art_5", "art_39", "art_40"],
            result.Provisions.Select(provision => provision.Anchor));
        Assert.Contains("Art. 5. L'article 115", result.Provisions[2].TextMd);
    }

    [Fact]
    public void Repeated_first_article_in_an_annex_falls_back_to_one_disclosed_document()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "Texte coordonné de l'arrêté grand-ducal du 27 août 1957 approuvant le règlement sur les pensions",
            "Texte coordonné",
            "Art. Ier. Est approuvé le règlement dont la teneur suit.",
            "Article 1er. A droit à la pension l'agent qui remplit les conditions.",
            "Article 2. La pension est calculée comme suit.")],
            "lu-legilux:agd-1957-08-27-n1:1984-11-09",
            "Arrêté grand-ducal du 27 août 1957 approuvant le règlement sur les pensions.");

        var provision = Assert.Single(result.Provisions);
        Assert.Equal("document", provision.Anchor);
        Assert.Contains("Est approuvé", provision.TextMd);
        Assert.Contains("A droit à la pension", provision.TextMd);
        Assert.Contains(result.Notes, note => note.Contains("repeated article identity", StringComparison.Ordinal));
    }

    [Fact]
    public void Spaceless_next_act_heading_ends_the_verified_section()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "Texte coordonné du règlement grand-ducal du 17 février 1971 concernant les valeurs mobilières",
            "Texte coordonné",
            "Art. 1er. Texte demandé.",
            "Art. 2. Suite demandée.",
            "loi du 28 août 1924 citée à titre de modification interne.",
            "Art. 3. Dernier article demandé.",
            "Règlementgrand-ducaldu18décembre1981concernantunautresujet",
            "Art. 1er. Texte étranger.")],
            "lu-legilux:rgd-1971-02-17-n1:1994-07-01",
            "Règlement grand-ducal du 17 février 1971 concernant les valeurs mobilières.");

        Assert.Equal(["art_1er", "art_2", "art_3"], result.Provisions.Select(provision => provision.Anchor));
        Assert.Contains("loi du 28 août 1924", result.Provisions[1].TextMd);
        Assert.DoesNotContain("Texte étranger", result.Markdown);
    }

    [Fact]
    public void Spaced_uppercase_next_act_heading_ends_the_verified_section()
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "Texte coordonné de la loi du 14 février 1955 concernant les baux à loyer",
            "Texte coordonné",
            "Art. 1er. Texte demandé.",
            "Art. 2. Suite demandée.",
            "Loi du 4 août 1975 concernant un autre sujet.",
            "Art. 1er. Texte étranger.")],
            "lu-legilux:loi-1955-02-14-n1:1987-09-01",
            WorkTitle);

        Assert.Equal(["art_1er", "art_2"], result.Provisions.Select(provision => provision.Anchor));
        Assert.DoesNotContain("Texte étranger", result.Markdown);
    }

    [Fact]
    public void Standalone_article_headings_are_never_removed_as_running_furniture()
    {
        var pages = new[]
        {
            Issue("CONVENTION COORDONNEE INSTITUANT L'UNION ECONOMIQUE", "Article 1", "Premier corps."),
            Issue("Article 2", "Deuxième corps."),
            Issue("Article 3", "Troisième corps."),
            Issue("Article 4", "Quatrième corps."),
            Issue("Article 5", "Cinquième corps."),
            Issue("Article 6", "Sixième corps."),
        };

        var result = PdfMemorialLuProfileV2.FromPages(
            pages,
            "lu-legilux:loi-1922-03-05-n1:1965-05-31",
            "Loi du 5 mars 1922 portant approbation de la Convention d'Union économique.");

        Assert.Equal(["art_1", "art_2", "art_3", "art_4", "art_5", "art_6"],
            result.Provisions.Select(provision => provision.Anchor));
        Assert.StartsWith("Premier corps", result.Provisions[0].TextMd);
    }

    [Theory]
    [InlineData("Article II")]
    [InlineData("ARTICLE II")]
    public void Roman_looking_II_is_eleven_only_between_articles_ten_and_twelve(string articleEleven)
    {
        var result = PdfMemorialLuProfileV2.FromPages([Issue(
            "CONVENTION COORDONNEE INSTITUANT L'UNION ECONOMIQUE",
            "Article 1",
            "Premier corps.",
            "Article 10",
            "Dixième corps.",
            articleEleven,
            "Onzième corps.",
            "Article 12",
            "Douzième corps.")],
            "lu-legilux:loi-1922-03-05-n1:1965-05-31",
            "Loi du 5 mars 1922 portant approbation de la Convention d'Union économique.");

        Assert.Equal(["art_1", "art_10", "art_11", "art_12"],
            result.Provisions.Select(provision => provision.Anchor));
        Assert.StartsWith("Onzième corps", result.Provisions[2].TextMd);
    }

    [Fact]
    public void Profile_pdf_memorial_lu_2_is_frozen()
    {
        var extraction = PdfMemorialLuProfileV2.FromPages([Issue(
            "Texte coordonné de la loi du 14 février 1955 concernant les baux à loyer",
            "Texte coordonné",
            "Art. premier - Objet",
            "La présente loi protège les locataires.",
            "Article 2 - Organismes de surveillance",
            "Les organismes assurent la surveillance.")],
            "lu-legilux:loi-1955-02-14-n1:1987-09-01",
            WorkTitle);

        var combined = string.Join("|", extraction.Provisions.Select(
            provision => $"{provision.Anchor}:{provision.TextSha256}"));
        var pinned = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(combined)));

        Assert.Equal("6e7e090036b8673a6a022f164dcec53dc59e61ffa0fd32f50f25646eeca180c3", pinned);
    }
}
