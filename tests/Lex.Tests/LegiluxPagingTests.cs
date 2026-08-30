using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lex.Ingest;
using Lex.Law;
using Lex.Sources.Legilux;

namespace Lex.Tests;

public sealed class LegiluxPagingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-legilux-paging-{Guid.NewGuid():N}");

    public LegiluxPagingTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Manifestation_query_projects_exact_identity_and_licence_terms()
    {
        var query = LegiluxAdapter.ManifestationQuery(limit: 101, offset: 202);

        Assert.Contains("SELECT ?c ?expr ?m ?fmt ?file ?license", query,
            StringComparison.Ordinal);
        Assert.Contains("OPTIONAL { ?m jolux:license ?license }", query,
            StringComparison.Ordinal);
        Assert.Contains("ORDER BY ?c ?expr ?m ?fmt ?file ?license", query,
            StringComparison.Ordinal);
        Assert.EndsWith("LIMIT 101 OFFSET 202", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_binding_names_fail_closed_instead_of_overwriting_evidence()
    {
        var response = Encoding.UTF8.GetBytes("""
            {"results":{"bindings":[{"license":{"type":"uri","value":"first"},"license":{"type":"uri","value":"second"}}]}}
            """);

        var error = Assert.Throws<JsonException>(() =>
            SparqlClient.ParseSelectResponse(response));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_properties_inside_a_binding_term_fail_closed()
    {
        var response = Encoding.UTF8.GetBytes("""
            {"results":{"bindings":[{"license":{"type":"uri","value":"first","value":"second"}}]}}
            """);

        var error = Assert.Throws<JsonException>(() =>
            SparqlClient.ParseSelectResponse(response));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Declared_oversized_response_is_rejected_before_content_is_read_and_POST_is_preserved()
    {
        var content = new FailOnSerializationContent(
            SparqlClient.ResponseMaximumBytes + 1L);
        HttpMethod? observedMethod = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            observedMethod = request.Method;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        var client = new SparqlClient(http);

        var error = await Assert.ThrowsAsync<SourceAcquisitionException>(() =>
            client.SelectAsync("SELECT * WHERE { ?s ?p ?o } LIMIT 1", default));

        Assert.Equal("enumeration_response_too_large", error.Issue.Code);
        Assert.Equal(HttpMethod.Post, observedMethod);
        Assert.False(content.SerializeCalled);
    }

    [Fact]
    public async Task Unknown_length_response_reads_only_cap_plus_one_and_retains_only_the_cap()
    {
        await using var stream = new MemoryStream(new byte[17]);

        var bounded = await SparqlClient.ReadBoundedResponseAsync(
            stream, maximumBytes: 16, default);

        Assert.True(bounded.LimitExceeded);
        Assert.Equal(16, bounded.Bytes.Length);
        Assert.Equal(17, stream.Position);
    }

    [Fact]
    public async Task Repeated_full_pages_detect_cap_plus_one_without_growing_the_accumulator()
    {
        const int totalRows = 11;
        var requests = new List<(int Limit, int Offset)>();
        var accumulated = new List<int>();
        var client = new SparqlClient((query, _) =>
        {
            var request = PageRequest(query);
            requests.Add(request);
            var count = Math.Clamp(totalRows - request.Offset, 0, request.Limit);
            return Task.FromResult(Rows(request.Offset, count));
        });

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.SelectPagedAsync(
                (limit, offset) => $"LIMIT {limit} OFFSET {offset}",
                pageSize: 5, maximumRows: 10, default, accumulated.Add));

        Assert.Equal([(5, 0), (5, 5), (1, 10)], requests);
        Assert.Equal([5, 10], accumulated);
        Assert.All(accumulated, count => Assert.InRange(count, 0, 10));
        Assert.Contains("exceeds the configured maximum of 10 rows", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_total_maximum_is_accepted_after_one_bounded_sentinel_request()
    {
        const int totalRows = 10;
        var requests = new List<(int Limit, int Offset)>();
        var client = new SparqlClient((query, _) =>
        {
            var request = PageRequest(query);
            requests.Add(request);
            var count = Math.Clamp(totalRows - request.Offset, 0, request.Limit);
            return Task.FromResult(Rows(request.Offset, count));
        });

        var rows = await client.SelectPagedAsync(
            (limit, offset) => $"LIMIT {limit} OFFSET {offset}",
            pageSize: 5, maximumRows: 10, default);

        Assert.Equal(10, rows.Count);
        Assert.Equal([(5, 0), (5, 5), (1, 10)], requests);
    }

    [Fact]
    public async Task Sorted_offset_paging_stops_before_Virtuoso_SR353_and_before_candidate_publication()
    {
        var corpus = Path.Combine(_root, "corpus");
        Directory.CreateDirectory(corpus);
        var sentinel = Path.Combine(corpus, "protected.txt");
        await File.WriteAllTextAsync(sentinel, "unchanged");
        var requests = new List<(int Limit, int Offset)>();
        var client = new SparqlClient((query, _) =>
        {
            Assert.Contains("jolux:Consolidation", query, StringComparison.Ordinal);
            var request = PageRequest(query);
            requests.Add(request);
            return Task.FromResult(Rows(request.Offset, request.Limit));
        });
        var writer = new CorpusWriter(corpus,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), new string('c', 40));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            writer.WriteAsync(new LegiluxAdapter(client), default));

        Assert.Equal(20_000, LegiluxAdapter.CatalogueMaximumRows);
        Assert.Equal(200_000, LegiluxAdapter.SubjectMaximumRows);
        Assert.Equal(20_000, LegiluxAdapter.IdentityMaximumRows);
        Assert.Equal(50_000, LegiluxAdapter.ManifestationMaximumRows);
        Assert.Equal([5000, 5000], requests.Select(x => x.Limit));
        Assert.Equal([0, 5000], requests.Select(x => x.Offset));
        Assert.Contains("cannot verify a sorted result beyond 10000 rows", error.Message,
            StringComparison.Ordinal);
        Assert.False(writer.Accepted);
        Assert.False(writer.Committed);
        Assert.Equal("unchanged", await File.ReadAllTextAsync(sentinel));
        Assert.False(File.Exists(Path.Combine(corpus, "manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(corpus, "works")));
    }

    [Fact]
    public async Task Held_work_subject_cap_plus_one_aborts_before_a_corpus_candidate_is_published()
    {
        const string work = "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/n1";
        var corpus = Path.Combine(_root, "subject-overflow-corpus");
        Directory.CreateDirectory(corpus);
        var sentinel = Path.Combine(corpus, "protected.txt");
        await File.WriteAllTextAsync(sentinel, "unchanged");
        string? subjectQuery = null;
        var client = new SparqlClient((query, _) =>
        {
            if (query.Contains("subjectLevel1", StringComparison.Ordinal))
            {
                subjectQuery = query;
                var limit = QueryLimit(query);
                return Task.FromResult(Enumerable.Range(0, limit)
                    .Select(_ => new Dictionary<string, string>(StringComparer.Ordinal)
                        { ["work"] = work })
                    .ToList());
            }
            Assert.Contains("SELECT ?c ?work", query, StringComparison.Ordinal);
            return Task.FromResult(new List<Dictionary<string, string>>
            {
                new(StringComparer.Ordinal)
                {
                    ["c"] = work + "/consolidation/20200101",
                    ["work"] = work,
                    ["from"] = "2020-01-01",
                },
            });
        });
        var writer = new CorpusWriter(corpus,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), new string('c', 40));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            writer.WriteAsync(new LegiluxAdapter(client), default));

        Assert.NotNull(subjectQuery);
        Assert.Contains("VALUES ?work", subjectQuery, StringComparison.Ordinal);
        Assert.Contains("LIMIT 1025", subjectQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("OFFSET", subjectQuery, StringComparison.Ordinal);
        Assert.Contains("exceeds 1024 rows", error.Message, StringComparison.Ordinal);
        Assert.False(writer.Accepted);
        Assert.False(writer.Committed);
        Assert.Equal("unchanged", await File.ReadAllTextAsync(sentinel));
        Assert.False(File.Exists(Path.Combine(corpus, "manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(corpus, "works")));
    }

    private static List<Dictionary<string, string>> Rows(int offset, int count) =>
        Enumerable.Range(offset, count).Select(index => new Dictionary<string, string>
        {
            ["row"] = index.ToString(CultureInfo.InvariantCulture),
        }).ToList();

    private static (int Limit, int Offset) PageRequest(string query)
    {
        var match = Regex.Match(query, @"LIMIT\s+(\d+)\s+OFFSET\s+(\d+)",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Query has no LIMIT/OFFSET: {query}");
        return (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private static int QueryLimit(string query)
    {
        var match = Regex.Match(query, @"LIMIT\s+(\d+)", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Query has no LIMIT: {query}");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class FailOnSerializationContent(long declaredLength) : HttpContent
    {
        internal bool SerializeCalled { get; private set; }

        protected override Task SerializeToStreamAsync(
            Stream stream, TransportContext? context)
        {
            SerializeCalled = true;
            throw new InvalidOperationException("The oversized body must not be read.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = declaredLength;
            return true;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
