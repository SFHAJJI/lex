using Lex.Index;

namespace Lex.Tests;

public sealed class RetrievalBenchmarkTests
{
    [Fact]
    public void Public_suite_has_the_accepted_200_case_shape()
    {
        var cases = RetrievalBenchmarkCatalog.Create();

        Assert.Equal(200, cases.Count);
        Assert.Equal(200, cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(200, cases.Select(c => string.Join('|',
            c.Category, c.Query, c.Language, c.TimeScope, c.AsOf, c.Hierarchy, c.Domain,
            string.Join(',', c.RelevantWorks))).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(30, cases.Count(c => c.Category == "exact"));
        Assert.Equal(40, cases.Count(c => c.Category == "temporal"));
        Assert.Equal(60, cases.Count(c => c.Category == "conceptual"));
        Assert.Equal(30, cases.Count(c => c.Category == "bilingual"));
        Assert.Equal(20, cases.Count(c => c.Category == "fuzzy"));
        Assert.Equal(20, cases.Count(c => c.Category == "hierarchy"));
        Assert.Equal(16, cases.Count(c => c.Category == "temporal" && c.AsOf != "2026-08-06"));
        Assert.Contains(cases, c => c.Category == "exact" && c.Query == "12012E/TXT"
            && c.RelevantWorks.SequenceEqual(["12012e-txt"]));
        Assert.All(cases, c =>
        {
            Assert.NotEmpty(c.RelevantWorks);
            Assert.NotEmpty(c.Explanation);
            Assert.Equal("generated-unreviewed", c.ReviewStatus);
        });
        var filterDomains = cases.Where(c => c.Category == "hierarchy")
            .Select(c => c.Domain).Where(d => d is not null).Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var expectedDomains = new HashSet<string>(
        [
            "financial-services", "aml-corporate", "competition", "tax", "employment",
            "consumer-environment", "procurement-and-ip", "judicial-cooperation", "primary-eu-law",
        ], StringComparer.Ordinal);
        Assert.True(expectedDomains.IsSubsetOf(filterDomains),
            $"Missing filter domains: {string.Join(", ", expectedDomains.Except(filterDomains))}");
    }
}
