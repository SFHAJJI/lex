using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Lex.Derive;
using Lex.Ingest;
using Lex.Law;
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
}
