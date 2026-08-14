using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Lex.Derive;
using Lex.Index;
using Lex.Ingest;
using Lex.Law;
using Lex.Mcp;
using Lex.Sources.EurLex;

namespace Lex.Tests;

public sealed class OfficialMetadataPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-official-metadata-{Guid.NewGuid():N}");

    public OfficialMetadataPolicyTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Repository_has_no_manual_legal_enrichment_artifacts_or_loader()
    {
        var root = RepoRoot();

        Assert.False(File.Exists(Path.Combine(root, "config", "eu-work-enrichment.json")));
        Assert.False(File.Exists(Path.Combine(root, "config", "lu-work-enrichment.json")));
        Assert.Null(typeof(IndexFromCorpus).Assembly.GetType("Lex.Ingest.WorkEnrichmentFile"));
    }

    [Fact]
    public void Test_project_and_ci_require_real_nonzero_test_execution()
    {
        var root = RepoRoot();
        var project = File.ReadAllText(Path.Combine(root, "tests", "Lex.Tests", "Lex.Tests.csproj"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.Contains("<IsTestProject>true</IsTestProject>", project, StringComparison.Ordinal);
        Assert.Contains("trx;LogFileName=ci.trx", workflow, StringComparison.Ordinal);
        Assert.Contains("if total < 300 or executed < 300", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Generation_v3_binds_corpus_manifest_without_an_unused_reviewed_configuration()
    {
        DerivationGeneration.UpdatePublisher(
            _root, "eu-eurlex", new string('c', 40), new string('a', 64),
            new string('b', 40), new string('d', 40), new string('e', 40),
            ["akn-eu/1"]);

        var root = JsonNode.Parse(File.ReadAllText(
            Path.Combine(_root, DerivationGeneration.FileName)))!.AsObject();
        Assert.Equal("lex-articles-generation/3", root["schema"]!.GetValue<string>());
        var entry = root["publishers"]!["eu-eurlex"]!.AsObject();
        Assert.False(entry.ContainsKey("reviewed_configuration_sha256"));
        Assert.Equal(new string('a', 64), entry["corpus_manifest_sha256"]!.GetValue<string>());
    }

    [Fact]
    public async Task Corpus_manifest_declares_code_only_source_configuration()
    {
        await new CorpusWriter(_root, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), new string('c', 40))
            .WriteAsync(new CodeOnlyAdapter(), default);

        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(_root, "manifest.json")))!.AsObject();
        Assert.Equal("code_only", manifest["source_configuration_kind"]!.GetValue<string>());
        Assert.Null(manifest["source_configuration_sha256"]);
    }

    [Fact]
    public void Eu_engineering_scope_uses_the_exact_eol_pinned_source_bytes()
    {
        var root = RepoRoot();
        var path = Path.Combine(root, "src", "Lex.Sources.EurLex", "eu-scope.json");
        var expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        var inventory = new EurLexAdapter(path).GetBuildInventory();

        Assert.Equal("engineering_scope", inventory.SourceConfigurationKind);
        Assert.Equal(expected, inventory.SourceConfigurationSha256);
        Assert.Contains("src/Lex.Sources.EurLex/eu-scope.json text eol=lf",
            File.ReadAllLines(Path.Combine(root, ".gitattributes")));
    }

    [Fact]
    public void Eu_engineering_scope_digest_changes_when_the_raw_bytes_change()
    {
        var source = Path.Combine(RepoRoot(), "src", "Lex.Sources.EurLex", "eu-scope.json");
        var copy = Path.Combine(_root, "eu-scope.json");
        File.WriteAllBytes(copy, [.. File.ReadAllBytes(source), (byte)'\n']);

        var original = new EurLexAdapter(source).GetBuildInventory();
        var changed = new EurLexAdapter(copy).GetBuildInventory();

        Assert.NotEqual(original.SourceConfigurationSha256, changed.SourceConfigurationSha256);
    }

    [Fact]
    public async Task Engineering_scope_labels_fail_before_corpus_publication_and_never_reach_index_or_MCP()
    {
        var rejectedCorpus = Path.Combine(_root, "rejected-corpus");
        Directory.CreateDirectory(rejectedCorpus);
        var sentinel = Path.Combine(rejectedCorpus, "protected.txt");
        await File.WriteAllTextAsync(sentinel, "unchanged");
        var rejectedWriter = new CorpusWriter(
            rejectedCorpus, DateTimeOffset.Parse("2026-08-14T00:00:00Z"),
            new string('c', 40));

        var rejection = await Assert.ThrowsAsync<InvalidDataException>(() =>
            rejectedWriter.WriteAsync(new EngineeringScopeAdapter(leakScopeLabels: true), default));

        Assert.Contains("engineering_scope", rejection.Message, StringComparison.Ordinal);
        Assert.Contains("scope_reasons", rejection.Message, StringComparison.Ordinal);
        Assert.False(rejectedWriter.Accepted);
        Assert.False(rejectedWriter.Committed);
        Assert.Equal("unchanged", await File.ReadAllTextAsync(sentinel));
        Assert.False(File.Exists(Path.Combine(rejectedCorpus, "manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(rejectedCorpus, "works")));

        // Defense in depth: even a post-publication tamper cannot turn an engineering selector
        // into a domain facet, an FTS term, or MCP evidence. Integrity detects the tamper first.
        var corpus = Path.Combine(_root, "tampered-corpus");
        var db = Path.Combine(_root, "index.db");
        await new CorpusWriter(corpus, DateTimeOffset.Parse("2026-08-14T00:00:00Z"),
                new string('c', 40))
            .WriteAsync(new EngineeringScopeAdapter(leakScopeLabels: false), default);
        var versionMetaPath = Directory.EnumerateFiles(
                Path.Combine(corpus, "works"), "meta.json", SearchOption.AllDirectories)
            .Single(path => JsonNode.Parse(File.ReadAllText(path))!.AsObject().ContainsKey("raw"));
        var versionMeta = JsonNode.Parse(await File.ReadAllTextAsync(versionMetaPath))!.AsObject();
        versionMeta["raw"]!["domains"] = "financial-services";
        versionMeta["raw"]!["scope_reasons"] = "domain:financial-services,seed:official";
        await File.WriteAllTextAsync(versionMetaPath, versionMeta.ToJsonString());
        var tamperedBytes = await File.ReadAllBytesAsync(versionMetaPath);

        var appendWriter = new CorpusWriter(
            corpus, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), new string('e', 40));
        var appendRejection = await Assert.ThrowsAsync<InvalidDataException>(() =>
            appendWriter.WriteAsync(new EngineeringScopeAdapter(leakScopeLabels: false), default));
        Assert.Contains("engineering_scope", appendRejection.Message, StringComparison.Ordinal);
        Assert.False(appendWriter.Committed);
        Assert.Equal(tamperedBytes, await File.ReadAllBytesAsync(versionMetaPath));

        var integrity = CorpusIntegrity.Verify(corpus);
        Assert.False(integrity.IsValid);
        Assert.Contains(integrity.Errors, error =>
            error.Contains("engineering_scope", StringComparison.Ordinal)
            && error.Contains("scope_reasons", StringComparison.Ordinal));

        IndexFromCorpus.Build(corpus, null, db, null,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"),
            codeCommit: new string('d', 40));

        using var reader = LexIndexReader.Open(db);
        Assert.Empty(reader.SearchFacets().Domains);
        Assert.Empty(reader.SearchKeyword(
            "financial services", FilterSet.All, 10, fuzzyAuto: false).Hits);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT facets || ' ' || publisher || ' ' || discovery FROM work_fts";
            Assert.DoesNotContain("financial-services",
                Convert.ToString(command.ExecuteScalar()) ?? "", StringComparison.Ordinal);
        }

        var response = new McpCore(
            new Dictionary<string, LexIndexReader> { [reader.Collection] = reader })
            .CallTool("search", new JsonObject { ["query"] = "financial services" });
        Assert.DoesNotContain("financial-services", response.ToJsonString(),
            StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private sealed class CodeOnlyAdapter : ISourceAdapter
    {
        private static readonly WorkRef Work = new(new Identifier("official:w1"), "w1", "LOI", "Work one");

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "LU", "https://example.test", Tier.A, "test", null),
            [], ["fr"], false, false, "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return Work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VersionRecord>>
            ([new VersionRecord(new Identifier("official:v1"), Work.Id, "LOI",
                new DateOnly(2024, 1, 1), null, "publisher", null, null,
                [new ExpressionRecord("fr", new DateOnly(2024, 1, 1), null,
                    "publisher", "Work one", null, "https://example.test/v1")],
                [], new Dictionary<string, string>())]);

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly));
    }

    private sealed class EngineeringScopeAdapter(bool leakScopeLabels)
        : ISourceAdapter, ISourceBuildInventory
    {
        private static readonly WorkRef Work = new(
            new Identifier("official:eu-work"), "eu-work", "REG", "Official work");

        public PublisherDescriptor Describe() => new(
            new Publisher("eu-test", "EU test", "EU", "https://example.test", Tier.A,
                "test", null),
            [], ["en"], false, false, "publisher");

        public SourceBuildInventory GetBuildInventory() => new(
            1, [], SourceConfigurationKind: "engineering_scope",
            SourceConfigurationSha256: new string('a', 64));

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return Work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work, CancellationToken ct)
        {
            var raw = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["celex"] = "32024R0001",
            };
            if (leakScopeLabels)
            {
                raw["domains"] = "financial-services";
                raw["scope_reasons"] = "domain:financial-services,seed:official";
            }
            return Task.FromResult<IReadOnlyList<VersionRecord>>
            ([new VersionRecord(new Identifier("official:eu-v1"), Work.Id, "REG",
                new DateOnly(2024, 1, 1), null, "publisher", "true", new DateOnly(2024, 1, 1),
                [new ExpressionRecord("en", new DateOnly(2024, 1, 1), null,
                    "publisher", "Official work", null, "https://example.test/v1")],
                [], raw)]);
        }

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly));
    }
}
