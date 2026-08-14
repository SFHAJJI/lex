using System.Globalization;
using System.Text.RegularExpressions;
using Lex.Ingest;
using Lex.Sources.Legilux;

namespace Lex.Tests;

public sealed class LegiluxPagingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-legilux-paging-{Guid.NewGuid():N}");

    public LegiluxPagingTests() => Directory.CreateDirectory(_root);

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

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
