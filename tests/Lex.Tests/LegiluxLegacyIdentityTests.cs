using Lex.Law;
using Lex.Sources.Legilux;

namespace Lex.Tests;

public sealed class LegiluxLegacyIdentityTests
{
    [Theory]
    [InlineData("2025-04-20", "loi/1804/03/21/n1/consolide/20250420", null)]
    [InlineData("2025-08-04", "loi/1808/11/17/n1/consolide/20250804", null)]
    [InlineData("2026-06-07", "loi/1808/11/17/n1/consolide/20260607", null)]
    [InlineData("2025-03-11", "loi/1879/06/18/n1/consolide/20250311", null)]
    [InlineData("2025-08-04", "loi/1879/06/18/n1/consolide/20250804", null)]
    [InlineData("2024-03-09", "loi/1925/12/17/n1/consolide/20240309", null)]
    [InlineData("2002-01-01", "loi/1979/03/15/n4/consolide/20020101", "fr")]
    [InlineData("2025-04-01", "loi/2006/07/31/n2/consolide/20250401", null)]
    [InlineData("2025-06-28", "loi/2006/07/31/n2/consolide/20250628", null)]
    [InlineData("2026-03-10", "loi/2006/07/31/n2/consolide/20260310", null)]
    [InlineData("2026-07-01", "loi/2006/07/31/n2/consolide/20260701", null)]
    [InlineData("2026-07-26", "loi/2006/07/31/n2/consolide/20260726", null)]
    [InlineData("2024-09-10", "loi/2011/04/08/n2/consolide/20240910", null)]
    [InlineData("2025-10-19", "loi/2011/04/08/n2/consolide/20251019", null)]
    [InlineData("2026-08-01", "loi/2014/07/24/n1/consolide/20260801", "fr")]
    public void All_withdrawn_baseline_sources_recover_the_exact_official_identity(
        string validFrom, string path, string? languageSuffix)
    {
        var publicSource = "https://legilux.public.lu/eli/etat/leg/" + path
            + (languageSuffix is null ? "" : "/" + languageSuffix);
        var expected = "http://data.legilux.public.lu/eli/etat/leg/" + path;
        var resolver = Assert.IsAssignableFrom<ILegacyVersionIdentityResolver>(
            new LegiluxAdapter());

        var actual = resolver.ResolveLegacyVersionIdentity(new LegacyVersionIdentity(
            "http://data.legilux.public.lu/eli/etat/leg/code/civil",
            DateOnly.Parse(validFrom),
            [new LegacyExpressionIdentity("fr", publicSource)]));

        Assert.Equal(expected, actual.Value);
    }

    [Theory]
    [InlineData("https://example.test/eli/etat/leg/loi/1804/03/21/n1/consolide/20250420")]
    [InlineData("https://legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1/consolide/20250419")]
    [InlineData("https://legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1/consolide/20250420?changed=true")]
    [InlineData("https://legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1/20250420")]
    [InlineData("https://legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1/not-consolide/20250420/fr")]
    [InlineData("https://legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1/consolide/20250420/fr/extra")]
    [InlineData("https://legilux.public.lu/eli/bogus/consolide/20250420")]
    [InlineData("https://legilux.public.lu/eli/etat/leg//consolide/20250420")]
    public void Legacy_identity_recovery_refuses_non_official_or_wrong_date_sources(
        string source)
    {
        var resolver = Assert.IsAssignableFrom<ILegacyVersionIdentityResolver>(
            new LegiluxAdapter());

        Assert.Throws<InvalidDataException>(() => resolver.ResolveLegacyVersionIdentity(
            new LegacyVersionIdentity(
                "http://data.legilux.public.lu/eli/etat/leg/code/civil",
                new DateOnly(2025, 4, 20),
                [new LegacyExpressionIdentity("fr", source)])));
    }
}
