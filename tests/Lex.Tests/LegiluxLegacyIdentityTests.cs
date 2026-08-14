using System.Globalization;
using Lex.Law;
using Lex.Sources.Legilux;

namespace Lex.Tests;

public sealed class LegiluxLegacyIdentityTests
{
    [Fact]
    public void Exact_work_state_source_recovers_the_active_publisher_identity()
    {
        var resolver = Assert.IsAssignableFrom<ILegacyVersionIdentityResolver>(
            new LegiluxAdapter());

        var actual = resolver.ResolveLegacyVersionIdentity(new LegacyVersionIdentity(
            "http://data.legilux.public.lu/eli/etat/leg/code/civil",
            new DateOnly(2025, 4, 20),
            [new LegacyExpressionIdentity("fr",
                "https://legilux.public.lu/eli/etat/leg/code/civil/20250420/fr")]));

        Assert.Equal(
            "http://data.legilux.public.lu/eli/etat/leg/code/civil/20250420",
            actual.Value);
    }

    [Theory]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1",
        "2016-09-01",
        "https://legilux.public.lu/eli/etat/leg/code/civil/20160901/fr",
        "http://data.legilux.public.lu/eli/etat/leg/code/civil/20160901")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/adm/agc/2023/10/06/b3399",
        "2023-11-06",
        "https://legilux.public.lu/eli/etat/adm/agc/2023/10/06/b3399/consolide/20231106/fr",
        "http://data.legilux.public.lu/eli/etat/adm/agc/2023/10/06/b3399/consolide/20231106")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/adm/agc/2024/09/04/b3521",
        "2026-06-15",
        "https://legilux.public.lu/eli/etat/adm/agc/2024/09/04/b3521/consolide/20260615",
        "http://data.legilux.public.lu/eli/etat/adm/agc/2024/09/04/b3521/consolide/20260615")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/leg/recueil/sites_monuments",
        "2015-10-09",
        "https://legilux.public.lu/eli/etat/leg/recueil/sites_monuments/20142209/fr",
        "http://data.legilux.public.lu/eli/etat/leg/recueil/sites_monuments/20142209")]
    public void Protected_v3_sources_recover_the_exact_current_publisher_identity(
        string work, string validFrom, string source, string expected)
    {
        var resolver = Assert.IsAssignableFrom<ILegacyVersionIdentityResolver>(
            new LegiluxAdapter());

        var actual = resolver.ResolveLegacyVersionIdentity(new LegacyVersionIdentity(
            work, DateOnly.ParseExact(validFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            [new LegacyExpressionIdentity("fr", source)]));

        Assert.Equal(expected, actual.Value);
    }

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
    [InlineData("https://legilux.public.lu/eli/etat/leg/code/civil/other/20250420/fr")]
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

    [Theory]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/adm/agc/2023/10/06/b3399",
        "2023-11-06",
        "https://legilux.public.lu/eli/etat/adm/agc/2023/10/06/other/consolide/20231106/fr")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/adm/agc/2023/10/06/b3399",
        "2023-11-06",
        "https://legilux.public.lu/eli/etat/adm/agc/2023/10/06/b3399/consolide/20231105/fr")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/adm/agc/2023/10/06/b3399",
        "2023-11-06",
        "https://legilux.public.lu/eli/etat/leg/code/civil/20231106/fr")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1",
        "2025-04-20",
        "https://legilux.public.lu/eli/etat/leg/code/civil/extra/20250420/fr")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1",
        "2025-04-20",
        "https://legilux.public.lu/eli/etat/leg/code/civil/20250419/fr")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1",
        "2025-04-20",
        "https://legilux.public.lu/eli/etat/leg/code/civile%2Fcachee/20250420/fr")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/leg/recueil/sites_monuments",
        "2015-10-09",
        "https://legilux.public.lu/eli/etat/leg/recueil/autre/20142209/fr")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/leg/recueil/sites_monuments",
        "2015-10-09",
        "https://legilux.public.lu/eli/etat/leg/recueil/sites_monuments/2014220/fr")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/leg/recueil/sites_monuments",
        "2015-10-09",
        "https://legilux.public.lu/eli/etat/leg/recueil/sites_monuments/20142209/extra/fr")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/leg/recueil/sites_monuments",
        "2015-10-09",
        "https://legilux.public.lu/eli/etat/leg/recueil/sites_monuments/20142209/en")]
    [InlineData(
        "http://data.legilux.public.lu/eli/etat/adm/agc//b3399",
        "2023-11-06",
        "https://legilux.public.lu/eli/etat/adm/agc//b3399/consolide/20231106/fr")]
    public void Legacy_identity_recovery_rejects_unbound_publisher_paths(
        string work, string validFrom, string source)
    {
        var resolver = Assert.IsAssignableFrom<ILegacyVersionIdentityResolver>(
            new LegiluxAdapter());

        Assert.Throws<InvalidDataException>(() => resolver.ResolveLegacyVersionIdentity(
            new LegacyVersionIdentity(
                work, DateOnly.ParseExact(validFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                [new LegacyExpressionIdentity("fr", source)])));
    }
}
