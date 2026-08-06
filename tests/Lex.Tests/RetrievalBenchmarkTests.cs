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
        Assert.Equal(30, cases.Count(c => c.Category == "exact"));
        Assert.Equal(40, cases.Count(c => c.Category == "temporal"));
        Assert.Equal(60, cases.Count(c => c.Category == "conceptual"));
        Assert.Equal(30, cases.Count(c => c.Category == "bilingual"));
        Assert.Equal(20, cases.Count(c => c.Category == "fuzzy"));
        Assert.Equal(20, cases.Count(c => c.Category == "hierarchy"));
        Assert.All(cases, c =>
        {
            Assert.NotEmpty(c.RelevantWorks);
            Assert.NotEmpty(c.Explanation);
            Assert.Equal("engineer-reviewed", c.ReviewStatus);
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
