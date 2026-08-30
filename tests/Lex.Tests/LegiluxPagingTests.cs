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
    public void Manifestation_and_licence_queries_do_not_multiply_the_global_rowset()
    {
        var query = LegiluxAdapter.ManifestationQuery(limit: 101, offset: 202);
        var licenceQuery = LegiluxAdapter.ManifestationLicenceQuery(
            ["http://data.legilux.public.lu/manifestation/1"]);

        Assert.Contains("SELECT ?c ?expr ?m ?fmt ?file", query,
            StringComparison.Ordinal);
        Assert.DoesNotContain("?license", query, StringComparison.Ordinal);
        Assert.Contains("ORDER BY ?c ?expr ?m ?fmt ?file", query,
            StringComparison.Ordinal);
        Assert.EndsWith("LIMIT 101 OFFSET 202", query, StringComparison.Ordinal);

        Assert.Contains("SELECT ?m ?license", licenceQuery, StringComparison.Ordinal);
        Assert.Contains("VALUES ?m", licenceQuery, StringComparison.Ordinal);
        Assert.Contains("OPTIONAL { ?m jolux:license ?license }", licenceQuery,
            StringComparison.Ordinal);
        Assert.Contains("LIMIT 3", licenceQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("OFFSET", licenceQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void Licence_VALUES_batches_are_exactly_bounded_and_injection_safe()
    {
        var maximumBatch = Enumerable.Range(0, 32)
            .Select(index =>
                $"http://data.legilux.public.lu/manifestation/{index}")
            .ToArray();

        var query = LegiluxAdapter.ManifestationLicenceQuery(maximumBatch);

        Assert.Contains("LIMIT 65", query, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LegiluxAdapter.ManifestationLicenceQuery(
                maximumBatch.Append(
                    "http://data.legilux.public.lu/manifestation/overflow")
                    .ToArray()));
        Assert.Throws<InvalidDataException>(() =>
            LegiluxAdapter.ManifestationLicenceQuery(
                [maximumBatch[0], maximumBatch[0]]));
        Assert.Throws<InvalidDataException>(() =>
            LegiluxAdapter.ManifestationLicenceQuery(
                ["http://data.legilux.public.lu/manifestation/1> ?s ?p ?o"]));
    }

    [Fact]
    public void Duplicate_binding_names_fail_closed_instead_of_overwriting_evidence()
    {
        var response = Encoding.UTF8.GetBytes("""
            {"head":{"vars":["license"]},"results":{"bindings":[{"license":{"type":"uri","value":"first"},"license":{"type":"uri","value":"second"}}]}}
            """);

        var error = Assert.Throws<JsonException>(() =>
            SparqlClient.ParseSelectResponse(response));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_properties_inside_a_binding_term_fail_closed()
    {
        var response = Encoding.UTF8.GetBytes("""
            {"head":{"vars":["license"]},"results":{"bindings":[{"license":{"type":"uri","value":"first","value":"second"}}]}}
            """);

        var error = Assert.Throws<JsonException>(() =>
            SparqlClient.ParseSelectResponse(response));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bindings_not_declared_by_the_projection_fail_closed()
    {
        var response = Encoding.UTF8.GetBytes("""
            {"head":{"vars":["m"]},"results":{"bindings":[{
              "m":{"type":"uri","value":"http://data.legilux.public.lu/manifestation/1"},
              "license":{"type":"uri","value":"https://example.test/licence"}
            }]}}
            """);

        var error = Assert.Throws<JsonException>(() =>
            SparqlClient.ParseSelectPage(response));

        Assert.Contains("not declared", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_projected_variables_fail_closed()
    {
        var response = Encoding.UTF8.GetBytes("""
            {"head":{"vars":["m","m"]},"results":{"bindings":[]}}
            """);

        var error = Assert.Throws<JsonException>(() =>
            SparqlClient.ParseSelectPage(response));

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
    public async Task Response_body_has_its_own_deadline_after_headers_arrive()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NeverEndingReadStream()),
            }));
        var client = new SparqlClient(http, TimeSpan.FromMilliseconds(25));

        var error = await Assert.ThrowsAsync<SourceAcquisitionException>(() =>
            client.SelectAsync("SELECT * WHERE { ?s ?p ?o } LIMIT 1", default));

        Assert.Equal("enumeration_body_timeout", error.Issue.Code);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_relabelled_as_a_body_timeout()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NeverEndingReadStream()),
            }));
        var client = new SparqlClient(http, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SelectAsync(
                "SELECT * WHERE { ?s ?p ?o } LIMIT 1",
                cancellation.Token));
    }

    [Fact]
    public void Missing_licence_projection_is_not_reported_as_absence()
    {
        const string manifestation =
            "http://data.legilux.public.lu/manifestation/1";
        var page = SparqlClient.ParseSelectPage(Encoding.UTF8.GetBytes($$$"""
            {"head":{"vars":["m"]},"results":{"bindings":[
              {"m":{"type":"uri","value":"{{{manifestation}}}"}}
            ]}}
            """));

        var evidence = LegiluxAdapter.ParseManifestationLicenceBatch(
            [manifestation], page);

        Assert.Equal(LicenceChannelState.NotObserved, evidence[manifestation].State);
    }

    [Fact]
    public void Completed_licence_batch_distinguishes_absent_invalid_and_unobserved()
    {
        const string absent =
            "http://data.legilux.public.lu/manifestation/absent";
        const string invalid =
            "http://data.legilux.public.lu/manifestation/invalid";
        const string unobserved =
            "http://data.legilux.public.lu/manifestation/unobserved";
        var page = new SparqlSelectPage(
            new HashSet<string>(["m", "license"], StringComparer.Ordinal),
            [
                new(StringComparer.Ordinal)
                {
                    ["m"] = new SparqlTerm("uri", absent),
                },
                new(StringComparer.Ordinal)
                {
                    ["m"] = new SparqlTerm("uri", invalid),
                },
                new(StringComparer.Ordinal)
                {
                    ["m"] = new SparqlTerm("uri", invalid),
                    ["license"] = new SparqlTerm(
                        "uri", "https://example.test/licence"),
                },
            ]);

        var evidence = LegiluxAdapter.ParseManifestationLicenceBatch(
            [absent, invalid, unobserved], page);

        Assert.Equal(LicenceChannelState.Absent, evidence[absent].State);
        Assert.Equal(LicenceChannelState.Invalid, evidence[invalid].State);
        Assert.Equal(LicenceChannelState.NotObserved, evidence[unobserved].State);
    }

    [Fact]
    public void Licence_batch_rejects_a_manifestation_outside_its_VALUES_set()
    {
        const string requested =
            "http://data.legilux.public.lu/manifestation/requested";
        var page = new SparqlSelectPage(
            new HashSet<string>(["m", "license"], StringComparer.Ordinal),
            [
                new(StringComparer.Ordinal)
                {
                    ["m"] = new SparqlTerm(
                        "uri",
                        "http://data.legilux.public.lu/manifestation/foreign"),
                },
            ]);

        Assert.Throws<InvalidDataException>(() =>
            LegiluxAdapter.ParseManifestationLicenceBatch([requested], page));
    }

    [Fact]
    public void Valid_typed_manifestation_rows_reach_the_bound_licence_evidence()
    {
        const string consolidation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/n1/consolidation/20200101";
        const string expression = consolidation + "/fra";
        const string manifestation = expression + "/xml";
        const string file =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2020/01/01/n1.xml";
        var basePage = SparqlClient.ParseSelectPage(Encoding.UTF8.GetBytes($$$"""
            {"head":{"vars":["c","expr","m","fmt","file"]},"results":{"bindings":[{
              "c":{"type":"uri","value":"{{{consolidation}}}"},
              "expr":{"type":"uri","value":"{{{expression}}}"},
              "m":{"type":"uri","value":"{{{manifestation}}}"},
              "fmt":{"type":"uri","value":"http://data.legilux.public.lu/resource/authority/user-format/xml"},
              "file":{"type":"uri","value":"{{{file}}}"}
            }]}}
            """));
        var licencePage = SparqlClient.ParseSelectPage(Encoding.UTF8.GetBytes($$$"""
            {"head":{"vars":["m","license"]},"results":{"bindings":[{
              "m":{"type":"uri","value":"{{{manifestation}}}"},
              "license":{"type":"uri","value":"http://creativecommons.org/licenses/by/4.0/"}
            }]}}
            """));

        var licences = LegiluxAdapter.ParseManifestationLicenceBatch(
            [manifestation], licencePage);
        var maps = LegiluxAdapter.BuildManifestationMaps(basePage.Rows, licences);

        var bound = Assert.Single(maps.Xml).Value;
        Assert.Equal(manifestation, bound.Identifier);
        Assert.Equal(file, bound.FileIdentifier);
        Assert.Equal(LicenceChannelState.Present, bound.SparqlLicence.State);
        Assert.Equal("http://creativecommons.org/licenses/by/4.0/",
            Assert.Single(bound.SparqlLicence.Claims).LicenceUri);
        Assert.Empty(maps.Pdf);
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

    private sealed class NeverEndingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
