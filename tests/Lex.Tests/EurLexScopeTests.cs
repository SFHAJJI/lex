using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Law;
using Lex.Sources.EurLex;

namespace Lex.Tests;

public sealed class EurLexScopeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-eu-scope-{Guid.NewGuid():N}");

    public EurLexScopeTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Engineering_scope_keeps_languages_histories_and_waves_explicit()
    {
        var scope = EurLexScopeConfig.Load();

        Assert.Equal("lex-eu-scope/1", scope.Schema);
        Assert.Equal(["en", "fr"], scope.Languages);
        Assert.True(scope.History.IncludeOriginal);
        Assert.True(scope.History.IncludeAllOfficialConsolidations);
        Assert.True(scope.History.IncludeUnamended);
        Assert.False(scope.History.ManufactureConsolidations);
        Assert.Equal(512, scope.History.MaxUnscopedConsolidations);
        Assert.Equal(2, scope.ActiveDomains(1).Count());
        Assert.Contains(scope.Domains, d => d.Id == "financial-services" && d.Wave == 2);
        Assert.Contains(scope.Exclusions, e => e.Kind == "citation");
        Assert.Contains(scope.Exclusions, e => e.Kind == "out_of_scope_language");
    }

    [Fact]
    public void Engineering_scope_reasons_never_become_corpus_legal_metadata()
    {
        var raw = EurLexAdapter.SourceRaw(
            "32016R0679", "REG", "in_force", "published");

        Assert.DoesNotContain("domains", raw.Keys);
        Assert.DoesNotContain("scope_reasons", raw.Keys);
        Assert.DoesNotContain("financial-services", raw.Values);
        Assert.Equal("32016R0679", raw["celex"]);
    }

    [Fact]
    public void Metadata_queries_batch_every_work_once_below_the_Virtuoso_sorted_top_10000_limit()
    {
        var celex = Enumerable.Range(1, 33).Select(index => $"32024R{index:0000}")
            .Reverse().Append("32024R0001").ToArray();
        var batches = EurLexAdapter.MetadataWorkBatches(celex).ToArray();
        var workQueries = batches.Select(EurLexAdapter.WorkMetadataQuery).ToArray();
        var publisherQueries = batches.Select(EurLexAdapter.PublisherMetadataQuery).ToArray();

        Assert.Equal([16, 16, 1], batches.Select(batch => batch.Length));
        Assert.Equal(celex.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            batches.SelectMany(batch => batch));
        Assert.All(workQueries.Concat(publisherQueries), query =>
        {
            Assert.DoesNotContain("LIMIT 20001", query, StringComparison.Ordinal);
            var line = query.Split('\n').Single(value =>
                value.TrimStart().StartsWith("LIMIT ", StringComparison.Ordinal));
            var limit = int.Parse(line.AsSpan(line.IndexOf("LIMIT ", StringComparison.Ordinal) + 6),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(limit, 1, 10_000);
        });
        Assert.All(celex.Distinct(StringComparer.Ordinal), id =>
            Assert.Equal(1, workQueries.Count(query => query.Contains($"\"{id}\"", StringComparison.Ordinal))));
        Assert.All(celex.Distinct(StringComparer.Ordinal), id =>
            Assert.Equal(1, publisherQueries.Count(query => query.Contains($"\"{id}\"", StringComparison.Ordinal))));
    }

    [Fact]
    public void Consolidation_query_keeps_dated_states_without_an_EN_or_FR_expression()
    {
        var query = EurLexAdapter.ConsolidationsQuery(
            ["32014R0910", "32019L0944", "32023R1114", "32023R2854"]);

        Assert.Contains("SELECT DISTINCT", query, StringComparison.Ordinal);
        Assert.Contains(
            "?s cdm:act_consolidated_based_on_resource_legal ?baseWork",
            query,
            StringComparison.Ordinal);
        Assert.Contains("OPTIONAL {", query, StringComparison.Ordinal);
        Assert.Contains(
            "?e cdm:expression_belongs_to_work ?s",
            query,
            StringComparison.Ordinal);
        Assert.Contains("LIMIT 2049", query, StringComparison.Ordinal);

        var rowsWithoutScopedExpression = new[]
        {
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["base"] = "32014R0910",
                ["celex"] = "02014R0910-20140917",
                ["date"] = "2014-09-17",
            },
        };
        Assert.Empty(EurLexAdapter.ConsolidationLanguages(
            rowsWithoutScopedExpression, ["en", "fr"]));
    }

    [Fact]
    public void Consolidation_languages_follow_the_official_expression_rows_in_scope_order()
    {
        var rows = new[]
        {
            new Dictionary<string, string>(StringComparer.Ordinal) { ["lang"] = "fr" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["lang"] = "en" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["lang"] = "fr" },
        };

        Assert.Equal(
            ["en", "fr"],
            EurLexAdapter.ConsolidationLanguages(rows, ["en", "fr"]));
    }

    [Fact]
    public void Expression_titles_never_cross_the_publisher_language_boundary()
    {
        var noStateTitles = new Dictionary<string, string?>(StringComparer.Ordinal);
        var frenchWorkTitle = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fr"] = "Règlement de test",
        };

        Assert.Null(EurLexAdapter.ExpressionTitle(
            "en", noStateTitles, frenchWorkTitle));
        Assert.Equal("Règlement de test", EurLexAdapter.ExpressionTitle(
            "fr", noStateTitles, frenchWorkTitle));
    }

    [Fact]
    public void Portal_fallback_uses_the_selected_legal_document_not_the_page_shell()
    {
        var html = PortalHtml("32014R0680", "FR",
            shellLanguage: "en", shellTitleLanguage: "EN");

        Assert.True(EurLexAdapter.IsExactPortalExpression(
            html, "32014R0680", "fr"));
    }

    [Fact]
    public void Portal_fallback_treats_a_regex_timeout_as_non_exact_identity()
    {
        Assert.False(EurLexAdapter.IsExactPortalExpression(
            "<html></html>", "32014R0680", "fr",
            _ => throw new System.Text.RegularExpressions.RegexMatchTimeoutException()));
    }

    [Theory]
    [InlineData("32014R0681", "fr")]
    [InlineData("32014R0680", "en")]
    public void Portal_fallback_rejects_the_wrong_CELEX_or_selected_language(
        string celex, string language)
    {
        var html = PortalHtml("32014R0680", "FR");

        Assert.False(EurLexAdapter.IsExactPortalExpression(html, celex, language));
    }

    [Fact]
    public void Portal_fallback_accepts_attribute_order_quotes_case_and_decoded_CELEX()
    {
        const string html = """
            <HTML lang='en'><head><title>misleading shell</title>
            <META CONTENT='12012E&#47;TXT' data-extra='x' PROPERTY='eli:id_local'>
            </head><body><DIV CLASS='panel' ID='PP1Contents'>
              <DIV data-extra='x' LANG='FR'><p>Texte sélectionné</p></DIV>
            </DIV></body></HTML>
            """;

        Assert.True(EurLexAdapter.IsExactPortalExpression(
            html, "12012E/TXT", "fr"));
    }

    [Fact]
    public void Portal_fallback_rejects_duplicate_or_ambiguous_identity_roots()
    {
        var exact = PortalHtml("32014R0680", "FR");
        var duplicateCelex = exact.Replace("</head>",
            "<meta property='eli:id_local' content='32014R0680'></head>",
            StringComparison.Ordinal);
        var duplicateContents = exact.Replace("</body>",
            "<div id='PP1Contents'><div lang='FR'>duplicate</div></div></body>",
            StringComparison.Ordinal);

        Assert.False(EurLexAdapter.IsExactPortalExpression(
            duplicateCelex, "32014R0680", "fr"));
        Assert.False(EurLexAdapter.IsExactPortalExpression(
            duplicateContents, "32014R0680", "fr"));
    }

    [Fact]
    public void Portal_fallback_requires_the_official_meta_and_div_marker_shapes()
    {
        var exact = PortalHtml("32014R0680", "FR");
        var arbitraryIdentityTag = exact.Replace(
            "<meta about=\"official\" property=\"eli:id_local\"",
            "<span about=\"official\" property=\"eli:id_local\"",
            StringComparison.Ordinal).Replace(
            "content=\"32014R0680\" lang=\"\">",
            "content=\"32014R0680\" lang=\"\"></span>",
            StringComparison.Ordinal);
        var arbitraryContentRoot = exact.Replace(
            "<div id=\"PP1Contents\"", "<section id=\"PP1Contents\"",
            StringComparison.Ordinal);

        Assert.False(EurLexAdapter.IsExactPortalExpression(
            arbitraryIdentityTag, "32014R0680", "fr"));
        Assert.False(EurLexAdapter.IsExactPortalExpression(
            arbitraryContentRoot, "32014R0680", "fr"));
    }

    [Fact]
    public void Portal_fallback_does_not_take_language_from_translation_metadata_or_nested_content()
    {
        const string translationOnly = """
            <html lang='en'><head>
              <meta property='eli:id_local' content='32014R0680'>
              <meta property='eli:language' content='FR'>
            </head><body><div id='PP1Contents'>
              <div><span lang='FR'>French appears only below the selected root</span></div>
            </div></body></html>
            """;

        Assert.False(EurLexAdapter.IsExactPortalExpression(
            translationOnly, "32014R0680", "fr"));
    }

    [Theory]
    [InlineData("<!-- {0} -->")]
    [InlineData("<script>const fixture = `{0}`;</script>")]
    [InlineData("<style>/* {0} */</style>")]
    [InlineData("<textarea>{0}</textarea>")]
    [InlineData("<template>{0}</template>")]
    [InlineData("<noscript>{0}</noscript>")]
    [InlineData("<iframe>{0}</iframe>")]
    [InlineData("<noembed>{0}</noembed>")]
    [InlineData("<noframes>{0}</noframes>")]
    [InlineData("<xmp>{0}</xmp>")]
    [InlineData("<title>{0}</title>")]
    [InlineData("<listing>{0}</listing>")]
    [InlineData("<plaintext>{0}")]
    [InlineData("<template><template></template>{0}</template>")]
    public void Portal_fallback_ignores_identity_markers_in_non_document_content(
        string wrapper)
    {
        const string fake = """
            <meta property='eli:id_local' content='32014R0680'>
            <div id='PP1Contents'><div lang='FR'>not selected law</div></div>
            """;
        var html = "<html><body>" + string.Format(
            System.Globalization.CultureInfo.InvariantCulture, wrapper, fake)
            + "</body></html>";

        Assert.False(EurLexAdapter.IsExactPortalExpression(
            html, "32014R0680", "fr"));
    }

    [Fact]
    public void A_portal_shell_cannot_manufacture_an_English_expression_for_a_Portuguese_state()
    {
        var html = PortalHtml("01989L0665-19900103", "PT",
            shellLanguage: "en", shellTitleLanguage: "EN");

        Assert.False(EurLexAdapter.IsExactPortalExpression(
            html, "01989L0665-19900103", "en"));

        var original = new DateOnly(1989, 12, 21);
        var gap = new DateOnly(1990, 1, 3);
        var next = new DateOnly(1992, 7, 14);
        var coordinates = EurLexAdapter.ConsolidatedCoordinates(
        [
            ("31989L0665", original),
            ("01989L0665-19900103", gap),
            ("01989L0665-19920714", next),
        ]);
        Assert.Equal(gap.AddDays(-1), coordinates[0].ValidTo);
        Assert.Equal(next.AddDays(-1), coordinates[1].ValidTo);
    }

    [Fact]
    public void Unscoped_consolidation_budget_is_explicit_and_fails_before_network_work()
    {
        EurLexAdapter.RequireUnscopedConsolidationBudget(192, 512);

        var error = Assert.Throws<InvalidDataException>(() =>
            EurLexAdapter.RequireUnscopedConsolidationBudget(513, 512));

        Assert.Contains("513 dated states", error.Message, StringComparison.Ordinal);
        Assert.Contains("permits 512", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Portal_bound_body_retries_one_transient_identity_mismatch()
    {
        const string celex = "32025L0516";
        var handler = new PortalSequenceHandler(
            new PortalResponse(System.Net.HttpStatusCode.NotFound, ""),
            new PortalResponse(System.Net.HttpStatusCode.OK,
                PortalHtml("32025L9999", "EN")),
            new PortalResponse(System.Net.HttpStatusCode.OK, PortalHtml(celex, "EN")));
        using var client = new HttpClient(handler);
        var adapter = new EurLexAdapter(
            scopePath: null, wave: null, http: client,
            delay: static (_, _) => Task.CompletedTask);
        var (version, expression) = VersionWithEnumeratedExpression(celex);

        var result = await adapter.FetchBody(version, expression, default);

        Assert.Equal(SourceBodyStatus.Retrieved, result.Status);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task Portal_bound_body_exhaustion_is_typed_and_logs_only_a_fingerprint()
    {
        const string celex = "32025L0516";
        const string sentinel = "publisher-response-must-not-leak";
        var invalid = PortalHtml("32025L9999", "EN") + sentinel;
        var handler = new PortalSequenceHandler(
            new PortalResponse(System.Net.HttpStatusCode.NotFound, ""),
            new PortalResponse(System.Net.HttpStatusCode.OK, invalid),
            new PortalResponse(System.Net.HttpStatusCode.OK, invalid));
        using var client = new HttpClient(handler);
        var adapter = new EurLexAdapter(
            scopePath: null, wave: null, http: client,
            delay: static (_, _) => Task.CompletedTask);
        var (version, expression) = VersionWithEnumeratedExpression(celex);

        var result = await adapter.FetchBody(version, expression, default);

        Assert.Equal(SourceBodyStatus.ParserFailure, result.Status);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(3, handler.RequestCount);
        Assert.Contains("endpoint=eur-lex.europa.eu/legal-content/EN/TXT/", result.Detail,
            StringComparison.Ordinal);
        Assert.Contains("status=200", result.Detail, StringComparison.Ordinal);
        Assert.Contains("content_type=text/html", result.Detail, StringComparison.Ordinal);
        Assert.Contains("bytes=", result.Detail, StringComparison.Ordinal);
        Assert.Contains("sha256=", result.Detail, StringComparison.Ordinal);
        Assert.Contains("exact_identity=false", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Portal_body_transport_and_identity_failures_share_one_attempt_budget()
    {
        const string celex = "32025L0516";
        var invalid = PortalHtml("32025L9999", "EN");
        var handler = new PortalSequenceHandler(
            new PortalResponse(System.Net.HttpStatusCode.NotFound, ""),
            new PortalResponse(System.Net.HttpStatusCode.ServiceUnavailable, ""),
            new PortalResponse(System.Net.HttpStatusCode.OK, invalid),
            new PortalResponse(System.Net.HttpStatusCode.OK, invalid),
            new PortalResponse(System.Net.HttpStatusCode.OK, PortalHtml(celex, "EN")));
        using var client = new HttpClient(handler);
        var adapter = new EurLexAdapter(
            scopePath: null, wave: null, http: client,
            delay: static (_, _) => Task.CompletedTask);
        var (version, expression) = VersionWithEnumeratedExpression(celex);

        var result = await adapter.FetchBody(version, expression, default);

        Assert.Equal(SourceBodyStatus.ParserFailure, result.Status);
        Assert.Equal(4, result.Attempts);
        Assert.Equal(4, handler.RequestCount);
        Assert.Equal(4, adapter.GetBuildInventory().RetryMaximumAttempts);
    }

    [Fact]
    public async Task Portal_identity_failure_is_not_relabelled_by_a_later_not_found()
    {
        const string celex = "32025L0516";
        var handler = new PortalSequenceHandler(
            new PortalResponse(System.Net.HttpStatusCode.NotFound, ""),
            new PortalResponse(System.Net.HttpStatusCode.OK,
                PortalHtml("32025L9999", "EN")),
            new PortalResponse(System.Net.HttpStatusCode.NotFound, ""));
        using var client = new HttpClient(handler);
        var adapter = new EurLexAdapter(
            scopePath: null, wave: null, http: client,
            delay: static (_, _) => Task.CompletedTask);
        var (version, expression) = VersionWithEnumeratedExpression(celex);

        var result = await adapter.FetchBody(version, expression, default);

        Assert.Equal(SourceBodyStatus.ParserFailure, result.Status);
        Assert.Equal(3, result.Attempts);
        Assert.Contains("status=200", result.Detail, StringComparison.Ordinal);
        Assert.Contains("exact_identity=false", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Consolidation_without_a_scoped_publisher_expression_stays_language_empty()
    {
        const string celex = "01989L0665-19900103";
        var handler = new PortalSequenceHandler(PortalHtml(celex, "PT"));
        using var client = new HttpClient(handler);
        var adapter = new EurLexAdapter(
            scopePath: null, wave: null, http: client,
            delay: static (_, _) => Task.CompletedTask);
        var row = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["celex"] = celex,
        };
        var rows = new Dictionary<string, List<Dictionary<string, string>>>(
            StringComparer.Ordinal)
        {
            ["31989L0665"] = [row],
        };

        adapter.RetainUnscopedConsolidations(rows);

        Assert.Equal(0, handler.RequestCount);
        Assert.False(row.ContainsKey("lang"));
        Assert.False(row.ContainsKey("expression_source"));
        Assert.Empty(EurLexAdapter.ConsolidationLanguages([row], ["en", "fr"]));
    }

    [Fact]
    public async Task Portal_client_is_stateless_between_public_expression_requests()
    {
        using var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (System.Net.IPEndPoint)listener.LocalEndpoint;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = CaptureCookieRequestsAsync(listener, timeout.Token);
        using var handler = EurLexAdapter.CreateHandler();
        using var client = new HttpClient(handler);

        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        await client.GetAsync($"http://127.0.0.1:{endpoint.Port}/first", timeout.Token);
        await client.GetAsync($"http://127.0.0.1:{endpoint.Port}/second", timeout.Token);
        var requests = await server;

        Assert.Equal(2, requests.Count);
        Assert.DoesNotContain("\r\nCookie:", requests[1],
            StringComparison.OrdinalIgnoreCase);
    }

    private static (VersionRecord Version, ExpressionRecord Expression)
        VersionWithEnumeratedExpression(string celex)
    {
        var date = new DateOnly(2025, 3, 25);
        var work = new Identifier("http://publications.europa.eu/resource/celex/" + celex);
        var expression = new ExpressionRecord(
            "en", date, null, "publisher", "Test", "Test",
            EurLexAdapter.ExpressionSourceUri("en", celex));
        return (new VersionRecord(
            work, work, "DIR", date, null, "publisher", "true", date,
            [expression], [], new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["celex"] = celex,
            }, null, null), expression);
    }

    private sealed record PortalResponse(System.Net.HttpStatusCode Status, string Body);

    private static async Task<IReadOnlyList<string>> CaptureCookieRequestsAsync(
        System.Net.Sockets.TcpListener listener, CancellationToken cancellationToken)
    {
        var requests = new List<string>();
        for (var index = 0; index < 2; index++)
        {
            using var socket = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = socket.GetStream();
            using var bytes = new MemoryStream();
            var buffer = new byte[1024];
            while (bytes.Length < 16 * 1024)
            {
                var count = await stream.ReadAsync(buffer, cancellationToken);
                if (count == 0) break;
                bytes.Write(buffer, 0, count);
                var value = System.Text.Encoding.ASCII.GetString(bytes.ToArray());
                if (value.Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            }
            requests.Add(System.Text.Encoding.ASCII.GetString(bytes.ToArray()));
            var response = System.Text.Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n"
                + "Set-Cookie: lex-session=publisher-value; Path=/\r\n"
                + "Connection: close\r\n\r\n");
            await stream.WriteAsync(response, cancellationToken);
        }
        return requests;
    }

    private sealed class PortalSequenceHandler : HttpMessageHandler
    {
        private readonly Queue<PortalResponse> _responses;

        public PortalSequenceHandler(params string[] bodies)
            : this(bodies.Select(body => new PortalResponse(
                System.Net.HttpStatusCode.OK, body)).ToArray())
        {
        }

        public PortalSequenceHandler(params PortalResponse[] responses) =>
            _responses = new Queue<PortalResponse>(responses);

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            if (_responses.Count == 0)
                throw new InvalidOperationException("The test portal response queue is empty.");
            var current = _responses.Dequeue();
            var response = new HttpResponseMessage(current.Status)
            {
                RequestMessage = request,
                Content = new StringContent(current.Body,
                    System.Text.Encoding.UTF8, "text/html"),
            };
            response.Content.Headers.ContentLanguage.Add("en");
            return Task.FromResult(response);
        }
    }

    private static string PortalHtml(
        string celex,
        string selectedLanguage,
        string shellLanguage = "en",
        string shellTitleLanguage = "EN") => $$"""
            <html lang="{{shellLanguage}}"><head>
              <title>EUR-Lex - shell - {{shellTitleLanguage}} - EUR-Lex</title>
              <meta about="official" property="eli:id_local" content="{{celex}}" lang="">
              <meta property="eli:language" content="EN">
              <meta property="eli:language" content="FR">
            </head><body>
              <div id="PP1Contents" class="panel"><div class="" lang="{{selectedLanguage}}">
                <p>Selected legal document</p>
              </div></div>
            </body></html>
            """;

    [Fact]
    public void Work_metadata_query_rejects_one_work_cap_plus_one()
    {
        var query = EurLexAdapter.WorkMetadataQuery(["32016R0679"]);
        var oversized = Enumerable.Range(0, 513)
            .Select(index => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["base"] = "32016R0679",
                ["lang"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToList();

        Assert.Contains("SELECT DISTINCT", query, StringComparison.Ordinal);
        Assert.Contains("LIMIT 513", query, StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(
            () => EurLexAdapter.RequireBoundedWorkMetadataRows(oversized));
        Assert.Contains("32016R0679", error.Message, StringComparison.Ordinal);
        Assert.Contains("512 records", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabling_synthetic_consolidation_is_rejected()
    {
        var path = Path.Combine(_dir, "unsafe.json");
        var safe = EurLexScopeConfig.Load();
        var unsafeScope = safe with { History = safe.History with { ManufactureConsolidations = true } };
        File.WriteAllText(path, JsonSerializer.Serialize(unsafeScope,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

        Assert.Throws<InvalidDataException>(() => EurLexScopeConfig.Load(path));
    }

    [Fact]
    public void Unscoped_consolidation_budget_cannot_exceed_the_offline_ingest_safety_limit()
    {
        var path = Path.Combine(_dir, "unsafe-portal-budget.json");
        var safe = EurLexScopeConfig.Load();
        var unsafeScope = safe with
        {
            History = safe.History with { MaxUnscopedConsolidations = 513 },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(unsafeScope,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

        var error = Assert.Throws<InvalidDataException>(() => EurLexScopeConfig.Load(path));
        Assert.Contains("between 1 and 512", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Historical_v1_scope_without_unscoped_state_budget_keeps_the_safe_default()
    {
        var path = Path.Combine(_dir, "historical-v1-scope.json");
        var root = JsonSerializer.SerializeToNode(EurLexScopeConfig.Load(),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            })!.AsObject();
        root["history"]!.AsObject().Remove("max_verified_portal_fallbacks");
        File.WriteAllText(path, root.ToJsonString());

        Assert.Equal(64, EurLexScopeConfig.Load(path).History.MaxUnscopedConsolidations);
    }

    [Theory]
    [InlineData("true", "in_force")]
    [InlineData("1", "in_force")]
    [InlineData("false", "not_in_force")]
    [InlineData("0", "not_in_force")]
    [InlineData(null, "unknown")]
    [InlineData("publisher-specific", "unknown")]
    public void Publisher_binding_status_is_normalized_for_search_filters(string? source, string expected)
    {
        Assert.Equal(expected, EurLexAdapter.NormalizeBindingStatus(source));
    }

    [Fact]
    public void Pinned_cellar_rows_preserve_bilingual_discovery_metadata_without_splitting_short_titles()
    {
        var titles = new[]
        {
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lang"] = "en",
                ["title_short"] = "gdpr, personal data, personal data protection",
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lang"] = "fr",
                ["title_short"] = "rgdp, GDPR, Protection des données à caractère personnel",
            },
        };
        var subjects = new[]
        {
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = "eurovoc",
                ["identifier"] = "http://eurovoc.europa.eu/5181",
                ["lang"] = "en",
                ["label"] = "protection of personal data",
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = "directory",
                ["identifier"] = "http://publications.europa.eu/resource/authority/dir-eu-legal-act/152020",
                ["lang"] = "fr",
                ["label"] = "Protection des données à caractère personnel",
            },
        };

        var metadata = EurLexAdapter.BuildPublisherMetadata("32016R0679", titles, subjects);

        Assert.Contains(metadata, item => item.Kind == "publisher_short_title"
            && item.Language == "en"
            && item.Label == "gdpr, personal data, personal data protection");
        Assert.DoesNotContain(metadata, item => item.Kind == "publisher_short_title"
            && item.Label == "gdpr");
        Assert.Contains(metadata, item => item.Kind == "eurovoc"
            && item.Identifier == "http://eurovoc.europa.eu/5181"
            && item.Language == "en");
        Assert.Contains(metadata, item => item.Kind == "directory"
            && item.Language == "fr"
            && item.SourceUri == item.Identifier);
    }

    [Fact]
    public void Cellar_taxonomy_rows_preserve_alt_broader_subdomain_and_domain_as_weak_typed_metadata()
    {
        const string concept = "http://eurovoc.europa.eu/5181";
        var subjects = new[]
        {
            Subject("eurovoc", concept, "en", "data protection"),
            Subject("eurovoc_alt_label", concept, "en", "data breach"),
            Subject("eurovoc_broader", "http://eurovoc.europa.eu/2472", "en", "information policy"),
            Subject("eurovoc_subdomain", "http://eurovoc.europa.eu/100222", "en",
                "3231 information and information processing"),
            Subject("eurovoc_domain", "http://eurovoc.europa.eu/100150", "en",
                "32 EDUCATION AND COMMUNICATIONS"),
        };

        var metadata = EurLexAdapter.BuildPublisherMetadata("32016R0679", [], subjects);

        Assert.Equal(5, metadata.Count);
        Assert.Equal(subjects.Select(row => row["kind"]).Order(StringComparer.Ordinal),
            metadata.Select(item => item.Kind));
        Assert.All(metadata, item =>
        {
            Assert.Equal("en", item.Language);
            Assert.Equal(item.Identifier, item.SourceUri);
        });

        static Dictionary<string, string> Subject(
            string kind, string identifier, string language, string label) => new(StringComparer.Ordinal)
            {
                ["kind"] = kind,
                ["identifier"] = identifier,
                ["lang"] = language,
                ["label"] = label,
            };
    }

    [Fact]
    public void Cellar_taxonomy_rows_fail_closed_when_the_official_shape_is_unknown_or_incomplete()
    {
        static Dictionary<string, string> Row(
            string kind, string identifier, string? language = "en", string? label = "label")
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal)
            { ["kind"] = kind, ["identifier"] = identifier };
            if (language is not null) row["lang"] = language;
            if (label is not null) row["label"] = label;
            return row;
        }

        Assert.Throws<InvalidDataException>(() => EurLexAdapter.BuildPublisherMetadata(
            "32016R0679", [], [Row("invented", "https://example.test/concept")]));
        Assert.Throws<InvalidDataException>(() => EurLexAdapter.BuildPublisherMetadata(
            "32016R0679", [], [Row("eurovoc", "not-a-uri")]));
        Assert.Throws<InvalidDataException>(() => EurLexAdapter.BuildPublisherMetadata(
            "32016R0679", [], [Row("eurovoc", "https://example.test/concept", label: null)]));
    }

    [Fact]
    public void Cellar_resource_type_and_relationships_produce_only_controlled_document_roles()
    {
        Assert.Equal(["amending", "consolidated", "delegated"],
            EurLexAdapter.DocumentRoles(
                "http://publications.europa.eu/resource/authority/resource-type/REG_DEL",
                amending: true, correcting: false, consolidated: true));
        Assert.Equal(["corrigendum", "implementing"],
            EurLexAdapter.DocumentRoles(
                "http://publications.europa.eu/resource/authority/resource-type/REG_IMPL",
                amending: false, correcting: true, consolidated: false));
    }

    [Fact]
    public void Corpus_display_titles_are_derived_only_from_the_official_title()
    {
        var title = EurLexAdapter.OfficialDisplayTitle(
            "Regulation (EU) 2016/679 of the European Parliament and of the Council",
            "32016R0679");

        Assert.Equal("Regulation (EU) 2016/679", title);
        Assert.DoesNotContain("GDPR", title, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("32016R0679", "32016r0679")]
    [InlineData("12012E/TXT", "12012e-txt")]
    public void Celex_identifiers_have_path_safe_stable_work_slugs(string celex, string expected)
    {
        Assert.Equal(expected, EurLexAdapter.NormalizeWorkSlug(celex));
    }

    [Theory]
    [InlineData("32016R0679", "https://publications.europa.eu/resource/celex/32016R0679")]
    [InlineData("12012E/TXT", "https://publications.europa.eu/resource/celex/12012E%2FTXT")]
    public void Cellar_resource_url_keeps_celex_as_one_encoded_path_segment(string celex, string expected)
    {
        Assert.Equal(expected, EurLexAdapter.CellarResourceUrl(celex));
    }

    [Theory]
    [InlineData("https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32006L0112R%2810%29", true)]
    [InlineData("https://publications.europa.eu/resource/celex/32006R1791", true)]
    [InlineData("http://eur-lex.europa.eu/legal-content/FR/TXT/", false)]
    [InlineData("https://europa.eu.example.org/legal-content/FR/TXT/", false)]
    [InlineData("https://example.org/", false)]
    public void Body_fallback_accepts_only_https_eu_institutional_hosts(string uri, bool accepted)
    {
        Assert.Equal(accepted, EurLexAdapter.OfficialEuUri(uri) is not null);
    }

    [Fact]
    public async Task Bounded_reader_accepts_the_limit_and_rejects_the_next_byte()
    {
        var exact = await EurLexAdapter.ReadBounded(new MemoryStream(new byte[8]), 8, default);
        var over = await EurLexAdapter.ReadBounded(new MemoryStream(new byte[9]), 8, default);

        Assert.False(exact.LimitExceeded);
        Assert.Equal(8, exact.Bytes?.Length);
        Assert.True(over.LimitExceeded);
        Assert.Null(over.Bytes);
        Assert.Equal(9, over.BytesRead);
    }

    [Fact]
    public void Sparql_alias_keeps_primary_celex_as_one_encoded_path_segment()
    {
        Assert.Equal("http://publications.europa.eu/resource/celex/12012E%2FTXT",
            EurLexAdapter.CelexAliasUri("12012E/TXT"));
    }

    [Fact]
    public void Relationship_closure_requires_an_expression_in_the_reviewed_languages()
    {
        var query = EurLexAdapter.RelationshipClosureQuery(
            ["32016R0679"],
            ["resource_legal_corrects_resource_legal"],
            ["en", "fr"]);

        Assert.Contains("cdm:expression_belongs_to_work ?related", query);
        Assert.Contains("cdm:expression_uses_language ?relatedLanguage", query);
        Assert.Contains("/language/ENG>", query);
        Assert.Contains("/language/FRA>", query);
        Assert.DoesNotContain("/language/DEU>", query);
    }

    [Fact]
    public void Original_state_is_kept_only_when_it_extends_temporal_coverage()
    {
        Assert.True(EurLexAdapter.ShouldIncludeOriginalState(
            new DateOnly(2014, 9, 17), [new DateOnly(2024, 5, 20), new DateOnly(2024, 10, 18)]));
        Assert.False(EurLexAdapter.ShouldIncludeOriginalState(
            new DateOnly(2014, 9, 17), [new DateOnly(2014, 9, 17), new DateOnly(2024, 10, 18)]));
        Assert.False(EurLexAdapter.ShouldIncludeOriginalState(
            new DateOnly(2025, 1, 1), [new DateOnly(2024, 10, 18)]));
        Assert.True(EurLexAdapter.ShouldIncludeOriginalState(new DateOnly(2014, 9, 17), []));
    }

    [Fact]
    public void Same_date_publisher_states_survive_with_the_next_distinct_date_as_their_boundary()
    {
        var sameDate = new DateOnly(2025, 7, 28);
        var nextDate = new DateOnly(2025, 8, 4);
        var coordinates = EurLexAdapter.ConsolidatedCoordinates(
        [
            ("02025R0001-20250728", sameDate),
            ("02025R0001-20250728R(01)", sameDate),
            ("02025R0001-20250804", nextDate),
        ]);

        Assert.Equal(3, coordinates.Count);
        var siblings = coordinates.Where(version => version.Date == sameDate).ToArray();
        Assert.Equal(2, siblings.Length);
        Assert.Equal(2, siblings.Select(version => version.Celex).Distinct().Count());
        Assert.All(siblings, version => Assert.Equal(nextDate.AddDays(-1), version.ValidTo));
        Assert.All(siblings, version => Assert.True(version.ValidTo >= version.Date));
        Assert.Null(coordinates.Single(version => version.Date == nextDate).ValidTo);
    }

    [Fact]
    public async Task Original_expressions_skip_the_incompatible_consolidation_manifestation()
    {
        var date = new DateOnly(2024, 1, 1);
        var identifier = new Identifier("http://publications.europa.eu/resource/celex/32024R0001");
        var expression = new ExpressionRecord(
            "en",
            date,
            null,
            "publisher",
            "Test regulation",
            "Test regulation",
            "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32024R0001");
        var version = new VersionRecord(
            identifier,
            identifier,
            "REG",
            date,
            null,
            "publisher",
            "true",
            date,
            [expression],
            [],
            new Dictionary<string, string>
            {
                ["celex"] = "32024R0001",
                ["consolidation_status"] = "not_published_or_not_required",
            });

        var result = await new EurLexAdapter().FetchAltManifestation(version, expression, default);

        Assert.Equal(SourceBodyStatus.PublisherMetadataOnly, result.Status);
        Assert.Null(result.Value);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
