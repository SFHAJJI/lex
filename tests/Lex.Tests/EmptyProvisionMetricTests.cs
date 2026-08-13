using Lex.Derive;
using Xunit;

namespace Lex.Tests;

/// <summary>
/// A provision the profile emits with no body text is not a skip and not an error. The provision
/// exists, carries its heading, and mints a text_sha256 over the empty string, so it counts as
/// coverage and any later arrival of the real text reads as an amendment that never happened.
///
/// <para>Nothing measured this. <c>Skipped</c> only counts documents that produced no provisions at
/// all, so a document where most articles came out empty was reported as a clean success. The count
/// has to exist before any threshold can be argued for: the current backlog would abort every run,
/// since derive fails on a non-empty error list.</para>
/// </summary>
public sealed class EmptyProvisionMetricTests
{
    // One article with publisher text, one whose heading is present with nothing under it. The
    // second is the shape being counted: a real provision that carries no words.
    private const string Html = """
        <html><body>
        <p class="title-article-norm">Article 1</p>
        <p>Original publisher wording.</p>
        <p class="title-article-norm">Article 2</p>
        </body></html>
        """;

    [Fact]
    public void A_provision_with_no_body_text_is_counted_without_failing_the_run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            var stats = DeriveFixture(root, Html);

            // Both provisions are published, exactly as before. This measures, it does not drop.
            Assert.Equal(2, stats.Provisions);
            Assert.Equal(1, stats.EmptyProvisions);

            // The document produced provisions, so it is not a skip, and an empty body is not an
            // acquisition error. Counting it must not change either verdict.
            Assert.Equal(0, stats.Skipped);
            Assert.Empty(stats.Errors);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // The guard against a counter that only ever reports the fixture's own defect: a document whose
    // articles all carry text must report zero, or the metric would be noise rather than a signal.
    [Fact]
    public void A_document_whose_provisions_all_carry_text_reports_none()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            var stats = DeriveFixture(root, """
                <html><body>
                <p class="title-article-norm">Article 1</p>
                <p>Original publisher wording.</p>
                <p class="title-article-norm">Article 2</p>
                <p>Second article wording.</p>
                </body></html>
                """);

            Assert.Equal(2, stats.Provisions);
            Assert.Equal(0, stats.EmptyProvisions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // The distinction the flag exists to make. One empty article out of four is a gap in a document;
    // three out of four is a profile that did not work on it, and only the second is worth a name.
    // Both report the same provision-level count, so a corpus rate cannot tell them apart.
    [Fact]
    public void A_version_that_is_mostly_empty_is_named_while_a_scattered_gap_is_not()
    {
        var scattered = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        var failed = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            // One of four empty: 25 percent, below the threshold.
            var scatteredStats = DeriveFixture(scattered, """
                <html><body>
                <p class="title-article-norm">Article 1</p><p>Wording one.</p>
                <p class="title-article-norm">Article 2</p><p>Wording two.</p>
                <p class="title-article-norm">Article 3</p><p>Wording three.</p>
                <p class="title-article-norm">Article 4</p>
                </body></html>
                """);
            Assert.Equal(1, scatteredStats.EmptyProvisions);
            Assert.Empty(scatteredStats.MostlyEmpty ?? []);

            // Three of four empty: 75 percent, at or above the threshold.
            var failedStats = DeriveFixture(failed, """
                <html><body>
                <p class="title-article-norm">Article 1</p><p>Wording one.</p>
                <p class="title-article-norm">Article 2</p>
                <p class="title-article-norm">Article 3</p>
                <p class="title-article-norm">Article 4</p>
                </body></html>
                """);
            Assert.Equal(3, failedStats.EmptyProvisions);
            var named = Assert.Single(failedStats.MostlyEmpty ?? []);
            Assert.Contains("3 of 4", named);
            Assert.Contains("32000r0001", named);

            // Flagged, never rejected: the provisions that did extract are still published, the
            // version is not skipped, and the run does not fail.
            Assert.Equal(4, failedStats.Provisions);
            Assert.Equal(0, failedStats.Skipped);
            Assert.Empty(failedStats.Errors);
        }
        finally
        {
            foreach (var root in new[] { scattered, failed })
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static DeriveWriter.Stats DeriveFixture(string root, string html)
    {
        var corpus = Path.Combine(root, "corpus");
        var output = Path.Combine(root, "articles");
        var work = Path.Combine(corpus, "works", "32000r0001");
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "meta.json"), "{\"title\":\"Empty provision fixture\"}");

        var version = Path.Combine(work, "versions", "2020-01-01");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "meta.json"), """
            {
              "work_identifier": "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32000R0001",
              "expressions": [{
                "language": "en",
                "source_uri": "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32000R0001",
                "observations": [{ "file": "en.html", "sha256": "fixture" }]
              }]
            }
            """);
        File.WriteAllText(Path.Combine(version, "en.html"), html);

        return DeriveWriter.Derive(corpus, output, "eu-eurlex");
    }
}
