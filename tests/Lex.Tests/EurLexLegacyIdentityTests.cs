using Lex.Law;
using Lex.Sources.EurLex;

namespace Lex.Tests;

public sealed class EurLexLegacyIdentityTests
{
    [Theory]
    [InlineData(
        "http://publications.europa.eu/resource/celex/31996L0071",
        "1997-02-10",
        "31996L0071")]
    [InlineData(
        "http://publications.europa.eu/resource/celex/12012E/TXT",
        "2012-10-26",
        "12012E/TXT")]
    [InlineData(
        "http://publications.europa.eu/resource/celex/11997E083",
        "1997-10-02",
        "11997E083")]
    [InlineData(
        "http://publications.europa.eu/resource/celex/12012E016",
        "2012-10-26",
        "12012E016")]
    [InlineData(
        "http://publications.europa.eu/resource/celex/12003TN02/18/A",
        "2003-09-23",
        "12003TN02/18/A")]
    public void Original_sources_recover_the_exact_work_CELEX_identity(
        string workIdentifier, string validFrom, string celex)
    {
        var resolver = Resolver();

        var actual = resolver.ResolveLegacyVersionIdentity(new LegacyVersionIdentity(
            workIdentifier,
            DateOnly.Parse(validFrom),
            [Expression("en", celex), Expression("fr", celex)]));

        Assert.Equal(workIdentifier, actual.Value);
    }

    [Fact]
    public void Consolidation_sources_recover_the_exact_dated_CELEX_identity()
    {
        const string work =
            "http://publications.europa.eu/resource/celex/32004R0139R(03)";
        const string state = "02004R0139R(03)-20050217";
        var resolver = Resolver();

        var actual = resolver.ResolveLegacyVersionIdentity(new LegacyVersionIdentity(
            work,
            new DateOnly(2005, 2, 17),
            [Expression("en", state), Expression("fr", state)]));

        Assert.Equal(
            "http://publications.europa.eu/resource/celex/" + state,
            actual.Value);
    }

    [Theory]
    [InlineData("https://example.test/legal-content/EN/TXT/?uri=CELEX:31996L0071")]
    [InlineData("https://eur-lex.europa.eu.example.test/legal-content/EN/TXT/?uri=CELEX:31996L0071")]
    [InlineData("https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:31996L0071")]
    [InlineData("https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:31996L0071&extra=true")]
    [InlineData("https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:31996L0071#fragment")]
    public void Legacy_identity_recovery_rejects_noncanonical_expression_sources(string source)
    {
        var resolver = Resolver();

        Assert.Throws<InvalidDataException>(() => resolver.ResolveLegacyVersionIdentity(
            new LegacyVersionIdentity(
                "http://publications.europa.eu/resource/celex/31996L0071",
                new DateOnly(1997, 2, 10),
                [new LegacyExpressionIdentity("en", source)])));
    }

    [Theory]
    [InlineData("http://example.test/resource/celex/31996L0071")]
    [InlineData("https://publications.europa.eu/resource/celex/31996L0071")]
    [InlineData("http://publications.europa.eu/resource/celex/31996L0071/extra")]
    [InlineData("http://publications.europa.eu/resource/celex/31996L0071?extra=true")]
    public void Legacy_identity_recovery_rejects_noncanonical_work_identifiers(
        string workIdentifier)
    {
        var resolver = Resolver();

        Assert.Throws<InvalidDataException>(() => resolver.ResolveLegacyVersionIdentity(
            new LegacyVersionIdentity(
                workIdentifier,
                new DateOnly(1997, 2, 10),
                [Expression("en", "31996L0071")])));
    }

    [Theory]
    [InlineData("12012E16")]
    [InlineData("12012E")]
    [InlineData("12012AB")]
    [InlineData("12012AB/TXT")]
    [InlineData("12012TN00/00/Z")]
    [InlineData("19999ZZ999")]
    [InlineData("12012E/TXT/EXTRA")]
    [InlineData("12003TN02/18/A/EXTRA")]
    public void Legacy_identity_recovery_rejects_malformed_treaty_CELEX_forms(
        string celex)
    {
        var resolver = Resolver();

        Assert.Throws<InvalidDataException>(() => resolver.ResolveLegacyVersionIdentity(
            new LegacyVersionIdentity(
                "http://publications.europa.eu/resource/celex/" + celex,
                new DateOnly(2012, 10, 26),
                [Expression("en", celex)])));
    }

    [Theory]
    [InlineData("02004R0139R(03)-20050218")]
    [InlineData("02005R0139R(03)-20050217")]
    [InlineData("02004R0139R(03)")]
    public void Legacy_identity_recovery_rejects_a_wrong_consolidation_date_or_base(
        string state)
    {
        var resolver = Resolver();

        Assert.Throws<InvalidDataException>(() => resolver.ResolveLegacyVersionIdentity(
            new LegacyVersionIdentity(
                "http://publications.europa.eu/resource/celex/32004R0139R(03)",
                new DateOnly(2005, 2, 17),
                [Expression("en", state)])));
    }

    [Theory]
    [InlineData("fr", "EN")]
    [InlineData("EN", "EN")]
    [InlineData("english", "EN")]
    [InlineData("en", "en")]
    public void Legacy_identity_recovery_rejects_an_ambiguous_or_noncanonical_language(
        string retainedLanguage, string pathLanguage)
    {
        var resolver = Resolver();
        var source =
            $"https://eur-lex.europa.eu/legal-content/{pathLanguage}/TXT/?uri=CELEX:31996L0071";

        Assert.Throws<InvalidDataException>(() => resolver.ResolveLegacyVersionIdentity(
            new LegacyVersionIdentity(
                "http://publications.europa.eu/resource/celex/31996L0071",
                new DateOnly(1997, 2, 10),
                [new LegacyExpressionIdentity(retainedLanguage, source)])));
    }

    [Fact]
    public void Every_legacy_expression_must_agree_on_one_CELEX_state()
    {
        var resolver = Resolver();

        Assert.Throws<InvalidDataException>(() => resolver.ResolveLegacyVersionIdentity(
            new LegacyVersionIdentity(
                "http://publications.europa.eu/resource/celex/31996L0071",
                new DateOnly(1997, 2, 10),
                [Expression("en", "31996L0071"), Expression("fr", "31997L0081")])));
    }

    [Fact]
    public void Legacy_identity_recovery_requires_at_least_one_expression()
    {
        var resolver = Resolver();

        Assert.Throws<InvalidDataException>(() => resolver.ResolveLegacyVersionIdentity(
            new LegacyVersionIdentity(
                "http://publications.europa.eu/resource/celex/31996L0071",
                new DateOnly(1997, 2, 10),
                [])));
    }

    private static ILegacyVersionIdentityResolver Resolver() =>
        Assert.IsAssignableFrom<ILegacyVersionIdentityResolver>(new EurLexAdapter());

    private static LegacyExpressionIdentity Expression(string language, string celex) =>
        new(language,
            $"https://eur-lex.europa.eu/legal-content/{language.ToUpperInvariant()}/TXT/"
            + $"?uri=CELEX:{Uri.EscapeDataString(celex)}");
}
