using Lex.Derive;
using Lex.Temporal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Lex.Tests;

// akn-lu/1 extraction contract: deterministic, publisher anchors reused, citations
// structured (never inline), tables to GFM, spans addressable. The fixture is a
// minimal but structurally faithful Legilux AKN document.
public class DeriveTests
{
    private const string IngesterCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DeriverCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DeriverTree = "dddddddddddddddddddddddddddddddddddddddd";
    private const string CorpusCommit = "cccccccccccccccccccccccccccccccccccccccc";
    private const string EnrichmentDigest =
        "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private const string Akn = """
        <?xml version="1.0" encoding="UTF-8"?>
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" xmlns:scl="http://www.scl.lu">
        <act>
        <meta><identification source="#casemates"/></meta>
        <body>
        <chapter id="chap_1"><num>Chapitre I<sup>er</sup>.</num><heading>Objet</heading>
        <article id="art_1er">
          <scl:JOLUXWork><scl:JOLUXLegalResource>
            <scl:jolux scl:name="uriThis">http://data.legilux.public.lu/eli/x/art_1er/20230716</scl:jolux>
            <scl:jolux scl:name="dateApplicability">2023-07-16</scl:jolux>
          </scl:JOLUXLegalResource></scl:JOLUXWork>
          <num>Art. 1<sup>er</sup>.</num>
          <alinea><content><p>Le present reglement s'applique selon la <ref href="http://data.legilux.public.lu/eli/etat/leg/loi/2006/07/31/n2">loi du 31 juillet 2006</ref> modifiee.</p></content></alinea>
        </article>
        <article id="art_2">
          <num>Art. 2.</num>
          <paragraph id="p1"><num>(1)</num><alinea><content><p>Premier    paragraphe
              avec espaces.</p></content></alinea></paragraph>
          <paragraph id="p2"><num>(2)</num><alinea><content>
            <table><tr><td><p>A</p></td><td><p>B</p></td></tr><tr><td><p>1</p></td><td><p>2</p></td></tr></table>
          </content></alinea></paragraph>
        </article>
        </chapter>
        </body>
        </act>
        <attachment id="att_a"><doc><preface><longTitle><p><b>Annexe A</b></p></longTitle></preface>
        <mainBody><alinea><content><p>Contenu de l'annexe.</p></content></alinea></mainBody></doc></attachment>
        </akomaNtoso>
        """;

    [Fact]
    public void Extracts_articles_with_publisher_anchors_paths_and_dates()
    {
        var x = AknLuProfile.Extract(Akn, "lu-legilux:test:2023-07-16");
        var a1 = x.Provisions.Single(p => p.Anchor == "art_1er");
        Assert.Equal("article", a1.Type);
        Assert.Equal("Art. 1er.", a1.Num);
        Assert.Equal("2023-07-16", a1.ArticleValidFrom);
        Assert.Equal("http://data.legilux.public.lu/eli/x/art_1er/20230716", a1.Eli);
        Assert.Equal(["Chapitre Ier. — Objet"], a1.Path);
    }

    [Fact]
    public void Citations_are_structured_not_inline()
    {
        var x = AknLuProfile.Extract(Akn, "lex:test");
        var a1 = x.Provisions.Single(p => p.Anchor == "art_1er");
        Assert.DoesNotContain("](", a1.TextMd);           // no inline markdown links
        Assert.DoesNotContain("http", a1.TextMd);          // no raw URLs in the text
        Assert.Contains("loi du 31 juillet 2006", a1.TextMd);
        var cite = Assert.Single(a1.Citations);
        Assert.Equal("http://data.legilux.public.lu/eli/etat/leg/loi/2006/07/31/n2", cite.Href);
        Assert.Equal("loi du 31 juillet 2006", cite.Text);
    }

    [Fact]
    public void Paragraph_numbers_and_whitespace_normalization()
    {
        var x = AknLuProfile.Extract(Akn, "lex:test");
        var a2 = x.Provisions.Single(p => p.Anchor == "art_2");
        Assert.Contains("**(1)** Premier paragraphe avec espaces.", a2.TextMd);
        var proseLine = a2.TextMd.Split('\n').First(l => l.Contains("Premier"));
        Assert.DoesNotContain("  ", proseLine);            // whitespace runs collapsed in prose
        Assert.Contains("**(2)**\n", a2.TextMd);           // number precedes the table as its own block
    }

    [Fact]
    public void Tables_become_gfm()
    {
        var x = AknLuProfile.Extract(Akn, "lex:test");
        var a2 = x.Provisions.Single(p => p.Anchor == "art_2");
        Assert.Contains("| A | B |", a2.TextMd);
        Assert.Contains("| --- | --- |", a2.TextMd);
        Assert.Contains("| 1 | 2 |", a2.TextMd);
    }

    [Fact]
    public void Annexes_are_provisions()
    {
        var x = AknLuProfile.Extract(Akn, "lex:test");
        var annex = x.Provisions.Single(p => p.Type == "annex");
        Assert.Equal("att_a", annex.Anchor);
        Assert.Equal("Annexe A", annex.Heading);           // plain, no emphasis markers
        Assert.Contains("Contenu de l'annexe.", annex.TextMd);
    }

    [Fact]
    public void Spans_address_the_markdown()
    {
        var x = AknLuProfile.Extract(Akn, "lex:test");
        foreach (var p in x.Provisions)
        {
            // fixture is BMP-only so codepoint offsets == char offsets
            var slice = x.Markdown.Substring(p.MdStart, p.MdEnd - p.MdStart);
            Assert.Equal(p.TextMd, slice);
        }
    }

    [Fact]
    public void Extraction_is_deterministic()
    {
        var x1 = AknLuProfile.Extract(Akn, "lex:test");
        var x2 = AknLuProfile.Extract(Akn, "lex:test");
        Assert.Equal(x1.Markdown, x2.Markdown);
        Assert.Equal(
            x1.Provisions.Select(p => (p.Anchor, p.TextSha256, p.MdStart, p.MdEnd)),
            x2.Provisions.Select(p => (p.Anchor, p.TextSha256, p.MdStart, p.MdEnd)));
    }

    [Fact]
    public void Profile_akn_lu_1_is_frozen()
    {
        // Profile permanence (SCHEMA.md): akn-lu/1 output over a fixed input never changes.
        // If this test fails, you have edited a published profile — make a NEW profile
        // (akn-lu/2) instead; citations pinned under akn-lu/1 must verify forever.
        var x = AknLuProfile.Extract(Akn, "lex:frozen");
        var combined = string.Join("|", x.Provisions.Select(p => $"{p.Anchor}:{p.TextSha256}"));
        var pinned = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined)));
        Assert.Equal("1c55e3140c5945dca3be0d782d1a0b1a9b742f03de2721f7ff6aab8e2db9a223", pinned);
        Assert.Null(x.PublisherStructuralEmptyArticles);
        var observable = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                x.Provisions,
                x.Markdown,
                x.Notes,
            })));
        Assert.Equal("4492b54be03b7e849022e5dd7a80090d8fa5122fd458f6af5f0e4108563b69c8",
            observable);
    }

    [Fact]
    public void Sha256_covers_text_md_exactly()
    {
        var x = AknLuProfile.Extract(Akn, "lex:test");
        foreach (var p in x.Provisions)
        {
            var expected = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p.TextMd)));
            Assert.Equal(expected, p.TextSha256);
        }
    }

    // Publisher-identity hashes in version keys are STORAGE, not validity. Publishing the key as
    // a date, or treating a same-date sibling as "the next version",
    // produced valid_to = 2025-07-27 — an interval ending before it began, invisible to every
    // point-in-time query. Thirteen such intervals reached production.
    [Theory]
    [InlineData("2025-07-28", "2025-07-28")]
    [InlineData("2025-07-28--aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "2025-07-28")]
    [InlineData("1849-03-14", "1849-03-14")]
    [InlineData("not-a-date", "not-a-date")]
    public void Version_key_yields_its_date_without_the_storage_suffix(string key, string expected)
        => Assert.Equal(expected, DeriveWriter.DateKeyOf(key));

    [Fact]
    public void Same_date_publisher_versions_derive_to_separate_stable_keys_and_cleanup_stale_output()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-derive-collision-{Guid.NewGuid():N}");
        var corpus = Path.Combine(root, "corpus");
        var output = Path.Combine(root, "output");
        try
        {
            Directory.CreateDirectory(corpus);
            File.WriteAllText(Path.Combine(corpus, "manifest.json"), $$"""
                { "schema": "lex-corpus/5", "canon": "canon/1", "build_issues": [], "ingester_code_commit": "{{IngesterCommit}}" }
                """);
            var work = Path.Combine(corpus, "works", "w1");
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "meta.json"),
                new JsonObject { ["title"] = "Work one" }.ToJsonString());
            var date = new DateOnly(2025, 7, 28);
            var firstKey = VersionIdentity.Create(date, "official:first");
            var secondKey = VersionIdentity.Create(date, "official:second");
            WriteVersion(work, firstKey, "official:first", "first");
            WriteVersion(work, secondKey, "official:second", "second");

            var first = DeriveWriter.Derive(
                corpus, output, "lu-legilux", DeriverCommit, DeriverTree,
                CorpusCommit);

            Assert.Empty(first.Errors);
            Assert.True(File.Exists(Path.Combine(output, "lu-legilux", "works", "w1", "versions",
                firstKey, "fr.json")));
            Assert.True(File.Exists(Path.Combine(output, "lu-legilux", "works", "w1", "versions",
                secondKey, "fr.json")));

            var recordBytes = Directory.EnumerateFiles(
                    Path.Combine(output, "lu-legilux"), "*", SearchOption.AllDirectories)
                .ToDictionary(Path.GetFullPath, File.ReadAllBytes);
            const string unrelatedCommit =
                "9999999999999999999999999999999999999999";
            var unrelated = DeriveWriter.Derive(
                corpus, output, "lu-legilux", unrelatedCommit, DeriverTree,
                CorpusCommit);
            Assert.Empty(unrelated.Errors);
            Assert.All(recordBytes, item =>
                Assert.Equal(item.Value, File.ReadAllBytes(item.Key)));
            Assert.Equal(unrelatedCommit,
                DerivationGeneration.ReadPublisher(output, "lu-legilux")
                    .DeriverCodeCommit);

            Directory.Delete(Path.Combine(work, "versions", secondKey), recursive: true);
            var second = DeriveWriter.Derive(
                corpus, output, "lu-legilux", DeriverCommit, DeriverTree,
                CorpusCommit);

            Assert.Empty(second.Errors);
            Assert.False(Directory.Exists(Path.Combine(output, "lu-legilux", "works", "w1", "versions",
                secondKey)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Canon_two_to_canon_one_is_refused_before_any_work_is_staged_or_replaced()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-derive-downgrade-{Guid.NewGuid():N}");
        var corpus = Path.Combine(root, "corpus");
        var output = Path.Combine(root, "output");
        try
        {
            Directory.CreateDirectory(corpus);
            File.WriteAllText(Path.Combine(corpus, "manifest.json"), $$"""
                { "schema": "lex-corpus/5", "canon": "canon/1", "build_issues": [], "ingester_code_commit": "{{IngesterCommit}}" }
                """);
            var work = Path.Combine(corpus, "works", "w1");
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "meta.json"),
                new JsonObject { ["title"] = "Work one" }.ToJsonString());
            var key = VersionIdentity.Create(new DateOnly(2025, 7, 28), "official:first");
            WriteVersion(work, key, "official:first", "first");

            var initial = DeriveWriter.Derive(
                corpus, output, "lu-legilux", DeriverCommit, DeriverTree,
                CorpusCommit, stagedFileWritten: null, enableAknLuV3: true);
            Assert.Empty(initial.Errors);
            Assert.Equal(DerivationGeneration.Canon2,
                DerivationGeneration.ReadPublisher(output, "lu-legilux").ArticlesCanon);
            var before = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(Path.GetFullPath, File.ReadAllBytes, StringComparer.Ordinal);

            var staged = new List<string>();
            Assert.Throws<InvalidDataException>(() => DeriveWriter.Derive(
                corpus, output, "lu-legilux", DeriverCommit, DeriverTree,
                CorpusCommit, staged.Add, enableAknLuV3: false));

            Assert.Empty(staged);
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal),
                Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
                    .Select(Path.GetFullPath).Order(StringComparer.Ordinal));
            Assert.All(before, item => Assert.Equal(item.Value, File.ReadAllBytes(item.Key)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Concurrent_canon_one_cannot_pass_the_guard_while_canon_two_is_staged()
    {
        var root = Path.Combine(Path.GetTempPath(),
            $"lex-derive-lock-{Guid.NewGuid():N}");
        var firstCorpus = Path.Combine(root, "corpus-first");
        var secondCorpus = Path.Combine(root, "corpus-second");
        var output = Path.Combine(root, "output");
        using var staged = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            PrepareCorpus(firstCorpus, "w1", "official:first", "first");
            PrepareCorpus(secondCorpus, "w2", "official:second", "second");

            var initial = DeriveWriter.Derive(
                firstCorpus, output, "lu-legilux", DeriverCommit, DeriverTree,
                CorpusCommit);
            Assert.Empty(initial.Errors);

            var upgrade = Task.Run(() => DeriveWriter.Derive(
                firstCorpus, output, "lu-legilux", DeriverCommit, DeriverTree,
                CorpusCommit,
                _ =>
                {
                    staged.Set();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
                },
                enableAknLuV3: true));

            Assert.True(staged.Wait(TimeSpan.FromSeconds(15)));
            var beforeBlockedAttempt = Snapshot(output);
            var blockedStageCount = 0;
            var active = Assert.Throws<InvalidOperationException>(() =>
                DeriveWriter.Derive(
                    secondCorpus, output, "lu-legilux", DeriverCommit,
                    DeriverTree, CorpusCommit,
                    _ => blockedStageCount++, enableAknLuV3: false));
            Assert.Contains("already active", active.Message,
                StringComparison.Ordinal);
            Assert.Equal(0, blockedStageCount);
            AssertSnapshot(beforeBlockedAttempt, output);

            release.Set();
            Assert.Empty((await upgrade).Errors);
            Assert.Equal(DerivationGeneration.Canon2,
                DerivationGeneration.ReadPublisher(output, "lu-legilux").ArticlesCanon);

            var afterUpgrade = Snapshot(output);
            var rejectedStageCount = 0;
            Assert.Throws<InvalidDataException>(() => DeriveWriter.Derive(
                secondCorpus, output, "lu-legilux", DeriverCommit, DeriverTree,
                CorpusCommit,
                _ => rejectedStageCount++, enableAknLuV3: false));
            Assert.Equal(0, rejectedStageCount);
            AssertSnapshot(afterUpgrade, output);
        }
        finally
        {
            release.Set();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        static void PrepareCorpus(
            string corpus,
            string slug,
            string publisherVersionIdentifier,
            string marker)
        {
            Directory.CreateDirectory(corpus);
            File.WriteAllText(Path.Combine(corpus, "manifest.json"), $$"""
                { "schema": "lex-corpus/5", "canon": "canon/1", "build_issues": [], "ingester_code_commit": "{{IngesterCommit}}" }
                """);
            var work = Path.Combine(corpus, "works", slug);
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "meta.json"),
                new JsonObject { ["title"] = $"Work {slug}" }.ToJsonString());
            var key = VersionIdentity.Create(
                new DateOnly(2025, 7, 28), publisherVersionIdentifier);
            WriteVersion(work, key, publisherVersionIdentifier, marker, slug);
        }

        static Dictionary<string, byte[]> Snapshot(string path) =>
            Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    file => Path.GetRelativePath(path, file),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);

        static void AssertSnapshot(
            IReadOnlyDictionary<string, byte[]> expected,
            string path)
        {
            var actual = Snapshot(path);
            Assert.Equal(expected.Keys.Order(StringComparer.Ordinal),
                actual.Keys.Order(StringComparer.Ordinal));
            Assert.All(expected, item => Assert.Equal(item.Value, actual[item.Key]));
        }
    }

    [Fact]
    public async Task Different_publishers_cannot_replace_output_before_the_generation_lock()
    {
        var root = Path.Combine(Path.GetTempPath(),
            $"lex-derive-generation-lock-{Guid.NewGuid():N}");
        var firstCorpus = Path.Combine(root, "corpus-first");
        var secondCorpus = Path.Combine(root, "corpus-second");
        var output = Path.Combine(root, "output");
        using var staged = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<DeriveWriter.Stats>? first = null;
        try
        {
            PrepareCorpus(firstCorpus, "lu-legilux", "w1",
                "official:first", "first");
            PrepareCorpus(secondCorpus, "eu-eurlex", "w2",
                "official:second", "second");

            first = Task.Run(() => DeriveWriter.Derive(
                firstCorpus, output, "lu-legilux", DeriverCommit, DeriverTree,
                CorpusCommit,
                _ =>
                {
                    staged.Set();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
                },
                enableAknLuV3: true));

            Assert.True(staged.Wait(TimeSpan.FromSeconds(15)));
            var beforeBlockedAttempt = Snapshot(output);
            var blockedStageCount = 0;
            var active = Assert.Throws<InvalidOperationException>(() =>
                DeriveWriter.Derive(
                    secondCorpus, output, "eu-eurlex", DeriverCommit,
                    DeriverTree, CorpusCommit,
                    _ => blockedStageCount++, enableAknLuV3: false));
            Assert.Contains("already active", active.Message,
                StringComparison.Ordinal);
            Assert.Equal(0, blockedStageCount);
            AssertSnapshot(beforeBlockedAttempt, output);

            release.Set();
            Assert.Empty((await first).Errors);
        }
        finally
        {
            release.Set();
            if (first is not null)
            {
                try { await first; } catch { }
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        static void PrepareCorpus(
            string corpus,
            string publisher,
            string slug,
            string publisherVersionIdentifier,
            string marker)
        {
            Directory.CreateDirectory(corpus);
            File.WriteAllText(Path.Combine(corpus, "manifest.json"), $$"""
                { "schema": "lex-corpus/5", "canon": "canon/1", "build_issues": [], "ingester_code_commit": "{{IngesterCommit}}" }
                """);
            var work = Path.Combine(corpus, "works", slug);
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "meta.json"),
                new JsonObject { ["title"] = $"Work {slug}" }.ToJsonString());
            var key = VersionIdentity.Create(
                new DateOnly(2025, 7, 28), publisherVersionIdentifier);
            WriteVersion(work, key, publisherVersionIdentifier, marker, slug,
                publisher);
        }

        static Dictionary<string, byte[]> Snapshot(string path) =>
            Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    file => Path.GetRelativePath(path, file),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);

        static void AssertSnapshot(
            IReadOnlyDictionary<string, byte[]> expected,
            string path)
        {
            var actual = Snapshot(path);
            Assert.Equal(expected.Keys.Order(StringComparer.Ordinal),
                actual.Keys.Order(StringComparer.Ordinal));
            Assert.All(expected, item => Assert.Equal(item.Value, actual[item.Key]));
        }
    }

    [Theory]
    [InlineData((int)DerivedPublisherTransaction.Step.CandidatePrepared, false)]
    [InlineData((int)DerivedPublisherTransaction.Step.JournalPublished, true)]
    [InlineData((int)DerivedPublisherTransaction.Step.PublisherSaved, true)]
    [InlineData((int)DerivedPublisherTransaction.Step.PublisherInstalled, true)]
    [InlineData((int)DerivedPublisherTransaction.Step.GenerationSaved, true)]
    [InlineData((int)DerivedPublisherTransaction.Step.GenerationInstalled, true)]
    public void Publisher_transaction_recovery_obeys_the_journal_commit_point(
        int failAfterValue,
        bool expectNew)
    {
        var failAfter = (DerivedPublisherTransaction.Step)failAfterValue;
        var root = Path.Combine(Path.GetTempPath(),
            $"lex-derived-publish-recovery-{Guid.NewGuid():N}", "articles");
        var publisher = Path.Combine(root, "lu-legilux");
        var generation = Path.Combine(root, DerivationGeneration.FileName);
        try
        {
            Directory.CreateDirectory(publisher);
            File.WriteAllText(Path.Combine(publisher, "identity.txt"), "old publisher");
            File.WriteAllText(generation, "old generation");
            using var publisherLock = DerivationGeneration.AcquirePublisherLock(
                root, "lu-legilux");
            using var generationLock = DerivationGeneration.AcquireGenerationLock(root);

            using (var transaction = new DerivedPublisherTransaction(root, "lu-legilux"))
            {
                File.WriteAllText(Path.Combine(
                    transaction.PublisherCandidateRoot, "identity.txt"), "new publisher");
                transaction.WriteGenerationCandidate(
                    System.Text.Encoding.UTF8.GetBytes("new generation"));
                Assert.Throws<InvalidOperationException>(() => transaction.Commit(step =>
                {
                    if (step == failAfter)
                        throw new InvalidOperationException("synthetic crash");
                }));
            }

            if (expectNew)
                Assert.Throws<InvalidDataException>(() =>
                    DerivedPublisherTransaction.RequireNoCommittedTransaction(root));
            DerivedPublisherTransaction.RecoverUnderLocks(root);
            DerivedPublisherTransaction.RecoverUnderLocks(root);

            Assert.Equal(expectNew ? "new publisher" : "old publisher",
                File.ReadAllText(Path.Combine(publisher, "identity.txt")));
            Assert.Equal(expectNew ? "new generation" : "old generation",
                File.ReadAllText(generation));
            Assert.False(File.Exists(DerivedPublisherTransaction.JournalPathFor(root)));
            Assert.Empty(Directory.EnumerateDirectories(
                root, ".lex-derived-publish-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(Directory.GetParent(root)!.FullName))
                Directory.Delete(Directory.GetParent(root)!.FullName, recursive: true);
        }
    }

    [Fact]
    public void Derived_evidence_comes_from_the_exact_selected_primary_observation()
    {
        var root = Path.Combine(Path.GetTempPath(),
            $"lex-derive-primary-evidence-{Guid.NewGuid():N}");
        var corpus = Path.Combine(root, "corpus");
        var output = Path.Combine(root, "output");
        try
        {
            Directory.CreateDirectory(corpus);
            File.WriteAllText(Path.Combine(corpus, "manifest.json"), $$"""
                { "schema": "lex-corpus/5", "canon": "canon/1", "build_issues": [], "ingester_code_commit": "{{IngesterCommit}}" }
                """);
            var work = Path.Combine(corpus, "works", "w1");
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "meta.json"),
                new JsonObject { ["title"] = "Work one" }.ToJsonString());
            var key = VersionIdentity.Create(
                new DateOnly(2025, 7, 28), "official:first");
            WriteVersion(work, key, "official:first", "first");

            var metaPath = Path.Combine(work, "versions", key, "meta.json");
            var meta = JsonNode.Parse(File.ReadAllText(metaPath))!.AsObject();
            var observations = meta["expressions"]![0]!["observations"]!.AsArray();
            var selected = observations[0]!.AsObject();
            selected["source_uri"] = "https://publisher.example/selected";
            observations.Add(new JsonObject
            {
                ["file"] = selected["file"]!.GetValue<string>(),
                ["sha256"] = selected["sha256"]!.GetValue<string>(),
                ["source_uri"] = "https://publisher.example/non-primary",
                ["format"] = "fmx4",
            });
            File.WriteAllText(metaPath, meta.ToJsonString());

            var stats = DeriveWriter.Derive(
                corpus, output, "lu-legilux", DeriverCommit, DeriverTree,
                CorpusCommit);
            Assert.Empty(stats.Errors);
            using var derived = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                output, "lu-legilux", "works", "w1", "versions", key, "fr.json")));
            Assert.Equal("https://publisher.example/selected",
                derived.RootElement.GetProperty("derived_from")
                    .GetProperty("source_uri").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("foo.")]
    [InlineData("foo ")]
    [InlineData("Foo")]
    [InlineData("con")]
    public void Publisher_lock_identity_must_resolve_to_a_direct_child(string publisher)
    {
        var root = Path.Combine(Path.GetTempPath(),
            $"lex-derive-lock-identity-{Guid.NewGuid():N}", "articles");
        FileStream? acquired = null;
        try
        {
            var error = Assert.Throws<InvalidDataException>(() =>
            {
                acquired = DerivationGeneration.AcquirePublisherLock(root, publisher);
            });
            Assert.Contains("path segment", error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            acquired?.Dispose();
            var parent = Directory.GetParent(root)?.FullName;
            if (parent is not null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }

    private static void WriteVersion(
        string work,
        string key,
        string publisherVersionIdentifier,
        string marker,
        string slug = "w1",
        string publisher = "lu-legilux")
    {
        var version = Path.Combine(work, "versions", key);
        Directory.CreateDirectory(version);
        var xml = Akn.Replace("Premier    paragraphe", $"{marker} paragraphe", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(version, "fr.xml"), xml);
        File.WriteAllText(Path.Combine(version, "meta.json"), new JsonObject
        {
            ["lex_id"] = $"{publisher}:{slug}:{key}",
            ["publisher"] = publisher,
            ["publisher_version_identifier"] = publisherVersionIdentifier,
            ["valid_from"] = "2025-07-28",
            ["work_identifier"] = $"official:{slug}",
            ["expressions"] = new JsonArray(new JsonObject
            {
                ["language"] = "fr",
                ["source_uri"] = $"https://example.test/{marker}",
                ["observations"] = new JsonArray(new JsonObject
                {
                    ["file"] = "fr.xml",
                    ["sha256"] = marker,
                    ["source_uri"] = $"https://publisher.example/{marker}",
                }),
            }),
        }.ToJsonString());
    }
}
