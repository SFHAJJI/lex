using Lex.Derive;
using Lex.Temporal;
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
    private const string IngesterCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DeriverCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DeriverTree = "dddddddddddddddddddddddddddddddddddddddd";
    private const string CorpusCommit = "cccccccccccccccccccccccccccccccccccccccc";
    private const string EnrichmentDigest =
        "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
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

    [Fact]
    public void A_work_cannot_increase_its_empty_provision_baseline()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            var first = DeriveFixture(root, Html);
            Assert.Empty(first.Errors);
            Assert.Equal(1, first.EmptyProvisions);
            var output = Path.Combine(root, "articles", "eu-eurlex", "works", "32000r0001",
                "versions", FixtureVersionKey, "en.json");
            var acceptedBytes = File.ReadAllBytes(output);

            var regression = DeriveFixture(root, """
                <html><body>
                <p class="title-article-norm">Article 1</p>
                <p class="title-article-norm">Article 2</p>
                </body></html>
                """);

            var error = Assert.Single(regression.Errors);
            Assert.Contains("empty-provision count increased", error,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(acceptedBytes, File.ReadAllBytes(output));

            // The rejected output must not become tomorrow's baseline.
            var repeated = DeriveFixture(root, """
                <html><body>
                <p class="title-article-norm">Article 1</p>
                <p class="title-article-norm">Article 2</p>
                </body></html>
                """);
            Assert.Single(repeated.Errors);
            Assert.Equal(acceptedBytes, File.ReadAllBytes(output));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Accepted_work_replaces_stale_output_while_a_rejected_work_is_preserved()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            WriteWork(root, "accepted", TextFor("Accepted original."));
            WriteWork(root, "rejected", Html);
            var initial = DeriveRoot(root);
            Assert.Empty(initial.Errors);

            var acceptedOutput = Output(root, "accepted");
            var rejectedOutput = Output(root, "rejected");
            var rejectedBytes = File.ReadAllBytes(rejectedOutput);
            var stale = Path.Combine(Path.GetDirectoryName(acceptedOutput)!, "stale.txt");
            File.WriteAllText(stale, "old output");

            WriteWork(root, "accepted", TextFor("Accepted replacement."));
            WriteWork(root, "rejected", """
                <html><body>
                <p class="title-article-norm">Article 1</p>
                <p class="title-article-norm">Article 2</p>
                </body></html>
                """);
            var next = DeriveRoot(root);

            Assert.Single(next.Errors);
            Assert.Equal(1, next.Works);
            Assert.Equal(1, next.Versions);
            Assert.Equal(2, next.Provisions);
            Assert.Equal(0, next.EmptyProvisions);
            Assert.False(File.Exists(stale));
            Assert.Contains("Accepted replacement.", File.ReadAllText(acceptedOutput));
            Assert.Equal(rejectedBytes, File.ReadAllBytes(rejectedOutput));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_large_work_is_written_to_same_filesystem_staging_one_file_at_a_time()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            var corpus = Path.Combine(root, "corpus");
            var output = Path.Combine(root, "articles");
            Directory.CreateDirectory(corpus);
            File.WriteAllText(Path.Combine(corpus, "manifest.json"), $$"""
                { "schema": "lex-corpus/4", "ingester_code_commit": "{{IngesterCommit}}" }
                """);
            var work = Path.Combine(corpus, "works", "large");
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "meta.json"), "{\"title\":\"Large fixture\"}");
            for (var day = 1; day <= 50; day++)
                WriteVersion(work, new DateOnly(2020, 1, 1).AddDays(day - 1),
                    $"fixture:v{day}", TextFor($"Version {day}."));

            var stagedWrites = 0;
            var stats = DeriveWriter.Derive(corpus, output, "eu-eurlex",
                DeriverCommit, DeriverTree, CorpusCommit, EnrichmentDigest, path =>
            {
                Assert.True(File.Exists(path));
                Assert.Equal(Path.GetPathRoot(output), Path.GetPathRoot(path));
                stagedWrites++;
            });

            Assert.Empty(stats.Errors);
            Assert.Equal(50, stats.Versions);
            Assert.Equal(100, stagedWrites);
            Assert.DoesNotContain(Directory.EnumerateDirectories(
                    Path.Combine(output, "eu-eurlex", "works")),
                path => Path.GetFileName(path).Contains("derive-stage", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Interrupted_swap_restores_the_accepted_backup_before_applying_the_ratchet()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            var first = DeriveFixture(root, Html);
            Assert.Empty(first.Errors);
            var output = Output(root, "32000r0001");
            var accepted = File.ReadAllBytes(output);
            var workOutput = Path.Combine(root, "articles", "eu-eurlex", "works", "32000r0001");
            var worksOutput = Directory.GetParent(workOutput)!.FullName;
            var backup = Path.Combine(worksOutput,
                ".32000r0001.derive-backup-interrupted");
            var stage = Path.Combine(worksOutput,
                ".32000r0001.derive-stage-interrupted");
            Directory.Move(workOutput, backup);
            Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(stage, "candidate.tmp"), "unaccepted");

            WriteWork(root, "32000r0001", """
                <html><body>
                <p class="title-article-norm">Article 1</p>
                <p class="title-article-norm">Article 2</p>
                </body></html>
                """);
            var recovered = DeriveRoot(root);

            Assert.Single(recovered.Errors);
            Assert.Equal(accepted, File.ReadAllBytes(output));
            Assert.False(Directory.Exists(backup));
            Assert.False(Directory.Exists(stage));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Ambiguous_interrupted_swap_fails_closed_without_removing_evidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            Assert.Empty(DeriveFixture(root, Html).Errors);
            var output = Output(root, "32000r0001");
            var accepted = File.ReadAllBytes(output);
            var workOutput = Path.Combine(root, "articles", "eu-eurlex", "works", "32000r0001");
            var backup = Path.Combine(Directory.GetParent(workOutput)!.FullName,
                ".32000r0001.derive-backup-ambiguous");
            var stage = Path.Combine(Directory.GetParent(workOutput)!.FullName,
                ".32000r0001.derive-stage-ambiguous");
            Directory.CreateDirectory(backup);
            Directory.CreateDirectory(stage);

            var stats = DeriveRoot(root);

            Assert.Contains("target, backup, and stage all exist", Assert.Single(stats.Errors),
                StringComparison.Ordinal);
            Assert.Equal(accepted, File.ReadAllBytes(output));
            Assert.True(Directory.Exists(backup));
            Assert.True(Directory.Exists(stage));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Completed_swap_with_leftover_backup_keeps_the_validated_target()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            Assert.Empty(DeriveFixture(root, Html).Errors);
            var output = Output(root, "32000r0001");
            var accepted = File.ReadAllBytes(output);
            var workOutput = Path.Combine(root, "articles", "eu-eurlex", "works", "32000r0001");
            var backup = Path.Combine(Directory.GetParent(workOutput)!.FullName,
                ".32000r0001.derive-backup-completed");
            Directory.CreateDirectory(backup);

            var stats = DeriveRoot(root);

            Assert.Empty(stats.Errors);
            Assert.Equal(accepted, File.ReadAllBytes(output));
            Assert.False(Directory.Exists(backup));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_corpus_work_metadata_is_an_error_and_preserves_accepted_output()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            Assert.Empty(DeriveFixture(root, Html).Errors);
            var output = Output(root, "32000r0001");
            var accepted = File.ReadAllBytes(output);
            File.Delete(Path.Combine(root, "corpus", "works", "32000r0001", "meta.json"));

            var stats = DeriveRoot(root);

            Assert.Contains("meta.json is missing", Assert.Single(stats.Errors),
                StringComparison.Ordinal);
            Assert.Equal(accepted, File.ReadAllBytes(output));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_corpus_version_metadata_is_an_error_and_preserves_accepted_output()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-empty-prov-{Guid.NewGuid():N}");
        try
        {
            Assert.Empty(DeriveFixture(root, Html).Errors);
            var output = Output(root, "32000r0001");
            var accepted = File.ReadAllBytes(output);
            File.Delete(Path.Combine(root, "corpus", "works", "32000r0001", "versions",
                FixtureVersionKey, "meta.json"));

            var stats = DeriveRoot(root);

            Assert.Contains("version meta.json is missing", Assert.Single(stats.Errors),
                StringComparison.Ordinal);
            Assert.Equal(accepted, File.ReadAllBytes(output));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static DeriveWriter.Stats DeriveFixture(string root, string html)
    {
        WriteWork(root, "32000r0001", html);
        return DeriveRoot(root);
    }

    private static DeriveWriter.Stats DeriveRoot(string root) => DeriveWriter.Derive(
        Path.Combine(root, "corpus"), Path.Combine(root, "articles"), "eu-eurlex",
        DeriverCommit, DeriverTree, CorpusCommit, EnrichmentDigest);

    private static void WriteWork(string root, string slug, string html)
    {
        var corpus = Path.Combine(root, "corpus");
        Directory.CreateDirectory(corpus);
        var manifest = Path.Combine(corpus, "manifest.json");
        if (!File.Exists(manifest))
            File.WriteAllText(manifest, $$"""
                { "schema": "lex-corpus/4", "ingester_code_commit": "{{IngesterCommit}}" }
                """);
        var work = Path.Combine(corpus, "works", slug);
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "meta.json"), "{\"title\":\"Empty provision fixture\"}");
        WriteVersion(work, new DateOnly(2020, 1, 1), "fixture:v1", html);
    }

    private static void WriteVersion(string work, DateOnly date, string publisherVersionId, string html)
    {
        var key = VersionIdentity.Create(date, publisherVersionId);
        var version = Path.Combine(work, "versions", key);
        Directory.CreateDirectory(version);
        var slug = Path.GetFileName(work);
        File.WriteAllText(Path.Combine(version, "meta.json"), $$"""
            {
              "lex_id": "eu-eurlex:{{slug}}:{{key}}",
              "publisher": "eu-eurlex",
              "publisher_version_identifier": "{{publisherVersionId}}",
              "valid_from": "{{date:yyyy-MM-dd}}",
              "work_identifier": "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32000R0001",
              "expressions": [{
                "language": "en",
                "source_uri": "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32000R0001",
                "observations": [{ "file": "en.html", "sha256": "fixture" }]
              }]
            }
            """);
        File.WriteAllText(Path.Combine(version, "en.html"), html);
    }

    private static string Output(string root, string slug) => Path.Combine(root, "articles",
        "eu-eurlex", "works", slug, "versions", FixtureVersionKey, "en.json");

    private static string FixtureVersionKey =>
        VersionIdentity.Create(new DateOnly(2020, 1, 1), "fixture:v1");

    private static string TextFor(string wording) => $$"""
        <html><body>
        <p class="title-article-norm">Article 1</p><p>{{wording}}</p>
        <p class="title-article-norm">Article 2</p><p>Second article wording.</p>
        </body></html>
        """;
}
