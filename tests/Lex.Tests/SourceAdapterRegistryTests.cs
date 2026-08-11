using Lex.Ingest;
using Lex.Law;
using Xunit;

namespace Lex.Tests;

public class SourceAdapterRegistryTests
{
    [Fact]
    public void New_publisher_is_added_by_registration_without_a_core_switch()
    {
        var registry = new SourceAdapterRegistry()
            .Register("test-official", option => new TestAdapter(option("--scope")));

        var adapter = registry.Resolve(
            "test-official",
            key => key == "--scope" ? "reviewed-scope.json" : null);

        var test = Assert.IsType<TestAdapter>(adapter);
        Assert.Equal("reviewed-scope.json", test.Scope);
        Assert.Equal("XX", test.Describe().Publisher.Jurisdiction);
    }

    [Fact]
    public void Unknown_publisher_error_lists_registered_adapters()
    {
        var registry = new SourceAdapterRegistry().Register("test-official", _ => new TestAdapter(null));

        var error = Assert.Throws<ArgumentException>(() => registry.Resolve("missing", _ => null));

        Assert.Contains("test-official", error.Message);
    }

    private sealed class TestAdapter(string? scope) : ISourceAdapter
    {
        public string? Scope { get; } = scope;

        public PublisherDescriptor Describe() => new(
            new Publisher("test-official", "Test Official Publisher", "XX", "https://example.invalid",
                Tier.A, "test", null),
            [new DocumentType("test-official", "ACT", new Dictionary<string, string> { ["en"] = "Act" })],
            ["en"], true, true, "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<VersionRecord>>([]);

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct)
            => Task.FromResult(new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly));
    }
}
