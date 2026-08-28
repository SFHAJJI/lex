using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Derive;
using Lex.Ingest;
using Lex.Law;
using Lex.Sources.EurLex;
using Lex.Temporal;

namespace Lex.Tests;

public sealed partial class CorpusWriterTests
{
    [Fact]
    public async Task V3_contract_requires_a_fresh_transport_observation_on_every_run()
    {
        var first = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var bytes = Encoding.UTF8.GetBytes("<html>fresh</html>");
        await new CorpusWriter(_dir, first, CodeCommit).WriteAsync(
            new OneVersionAdapter("in_force", "finance", bodyFetch:
                SourceBodyFetch.Retrieved(bytes, new(200, "text/html", "utf-8", null, null, first, EffectiveBodyUri))), default);
        var second = first.AddDays(1);
        await new CorpusWriter(_dir, second, CodeCommit).WriteAsync(
            new OneVersionAdapter("in_force", "finance", bodyFetch:
                SourceBodyFetch.Retrieved(bytes, new(200, "text/html", "utf-8", null, null, second, EffectiveBodyUri))), default);

        var observations = Assert.Single((await ReadVersionMeta()).Expressions).Observations;
        Assert.Equal(2, observations.Count);
        Assert.Equal(["2026-08-14T09:10:11Z", "2026-08-15T09:10:11Z"],
            observations.Select(item => item.RetrievedAt).ToArray());
    }

    [Fact]
    public async Task V3_contract_integrity_rejects_omission_of_the_run_observation()
    {
        var at = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        await new CorpusWriter(_dir, at, CodeCommit).WriteAsync(
            new OneVersionAdapter("in_force", "finance", bodyFetch:
                SourceBodyFetch.Retrieved(Encoding.UTF8.GetBytes("<html>fresh</html>"),
                    new(200, "text/html", "utf-8", null, null, at, EffectiveBodyUri))), default);
        var meta = await ReadVersionMeta();
        Assert.Single(meta.Expressions).Observations.Clear();
        RefreshRecordHash(meta);
        await File.WriteAllTextAsync(Path.Combine(OneVersionDirectory, "meta.json"),
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");

        Assert.Contains(CorpusIntegrity.Verify(_dir).Errors,
            error => error.Contains("no fresh primary observation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task V3_contract_persists_bounded_rejected_bytes_with_final_uri()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var prefix = Encoding.UTF8.GetBytes("bounded rejected prefix");
        const string finalUri = "https://eur-lex.europa.eu/final/body";
        var rejected = new SourceBodyFetch(SourceBodyStatus.Oversized, prefix,
            new SourceHttpEvidence(200, "text/html", "utf-8", null, null, fetchedAt,
                finalUri, BodyComplete: false), Attempts: 2);

        await new CorpusWriter(_dir, fetchedAt, CodeCommit).WriteAsync(
            new OneVersionAdapter("in_force", "finance", bodyFetch: rejected), default);

        var observation = Assert.Single(Assert.Single((await ReadVersionMeta()).Expressions).Observations);
        Assert.Equal(finalUri, observation.SourceUri);
        Assert.Equal(prefix, await File.ReadAllBytesAsync(Path.Combine(OneVersionDirectory, observation.File!)));
        Assert.Equal("body_oversized", observation.Http?.AttemptOutcome);
        Assert.False(observation.Http!.BodyComplete);
    }

    [Fact]
    public async Task V3_contract_rejected_response_is_durable_but_cannot_enter_derive_or_index()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var prefix = Encoding.UTF8.GetBytes("bounded rejected prefix");
        var writer = new CorpusWriter(_dir, fetchedAt, CodeCommit);

        await writer.WriteAsync(new OneVersionAdapter(
            "in_force", "finance",
            bodyFetch: new SourceBodyFetch(
                SourceBodyStatus.Oversized,
                prefix,
                new SourceHttpEvidence(
                    200, "text/html", "utf-8", null, null, fetchedAt,
                    EffectiveBodyUri, BodyComplete: false),
                Attempts: 3)), default, requireComplete: true);

        Assert.False(writer.Accepted);
        Assert.True(writer.Committed);
        var expression = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.False(expression.Text.Available);
        Assert.Equal("body_oversized", expression.Text.Reason);
        Assert.DoesNotContain(expression.Observations,
            value => value.Format is not null);
        var observation = Assert.Single(expression.Observations);
        Assert.Equal(prefix, await File.ReadAllBytesAsync(Path.Combine(
            OneVersionDirectory, observation.File!)));
        Assert.Equal(EffectiveBodyUri, observation.SourceUri);
        Assert.Equal("body_oversized", observation.Http?.AttemptOutcome);
        Assert.True(CorpusIntegrity.Verify(_dir).IsValid);

        var corpusCommit = CommitGitDirectory(_dir);
        var articles = Path.Combine(_dir, "rejected-articles");
        var deriveError = Assert.Throws<InvalidDataException>(() =>
            DeriveWriter.Derive(_dir, articles, "test",
                new string('b', 40), new string('d', 40), corpusCommit));
        Assert.Contains("rejected acquisition evidence", deriveError.Message,
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(articles));

        var database = Path.Combine(_dir, "rejected.db");
        var indexError = Assert.Throws<InvalidDataException>(() =>
            IndexFromCorpus.Build(_dir, null, database, null, fetchedAt,
                codeCommit: new string('c', 40), corpusCommit: corpusCommit));
        Assert.Contains("rejected acquisition evidence", indexError.Message,
            StringComparison.Ordinal);
        Assert.False(File.Exists(database));
    }

    [Fact]
    public async Task V3_contract_missing_effective_response_uri_fails_before_publication()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var evidence = new SourceHttpEvidence(
            200, "text/html", "utf-8", null, null, fetchedAt,
            EffectiveSourceUri: null!);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, fetchedAt, CodeCommit).WriteAsync(
                new OneVersionAdapter("in_force", "finance", bodyFetch:
                    SourceBodyFetch.Retrieved(
                        Encoding.UTF8.GetBytes("<html>publisher</html>"), evidence)),
                default));

        Assert.False(File.Exists(Path.Combine(_dir, "manifest.json")));
    }

    [Fact]
    public void V3_contract_strips_utf8_bom_only_from_decoded_view()
    {
        var transport = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("publisher")).ToArray();
        Assert.Equal("publisher", StrictPublisherText.Decode(transport, "utf-8"));
        Assert.Throws<InvalidDataException>(() =>
            StrictPublisherText.Decode(new byte[] { 0xff, 0xfe, 0x61, 0 }, "utf-8"));
    }

    [Fact]
    public async Task V3_contract_normal_append_hard_kill_recovers_forward_to_an_integrity_valid_corpus()
    {
        var firstAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        await new CorpusWriter(_dir, firstAt, CodeCommit).WriteAsync(
            new OneVersionAdapter("in_force", "finance", bodyFetch:
                SourceBodyFetch.Retrieved(
                    Encoding.UTF8.GetBytes("<html>first</html>"),
                    new(200, "text/html", "utf-8", null, null,
                        firstAt, EffectiveBodyUri))), default);
        var interrupted = new CorpusWriter(_dir, firstAt.AddDays(1), CodeCommit);
        var published = 0;

        await Assert.ThrowsAsync<IOException>(() => WriteWithPublishHook(
            interrupted,
            new OneVersionAdapter("in_force", "finance", bodyFetch:
                SourceBodyFetch.Retrieved(
                    Encoding.UTF8.GetBytes("<html>replacement</html>"),
                    new(200, "text/html", "utf-8", null, null,
                        firstAt.AddDays(1), EffectiveBodyUri))),
            (_, _) =>
        {
            if (++published == 2) throw new IOException("injected crash boundary");
        }));

        var transaction = Path.Combine(_dir, ".lex-corpus-transaction");
        Assert.True(Directory.Exists(transaction));
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            transaction, "journal.json")))!.AsObject();
        Assert.True(journal["require_corpus_integrity"]!.GetValue<bool>());
        RecoverCorpus(_dir);

        Assert.False(Directory.Exists(Path.Combine(
            _dir, ".lex-corpus-transaction")));
        var report = CorpusIntegrity.Verify(_dir);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(2, Assert.Single((await ReadVersionMeta()).Expressions)
            .Observations.Count);
    }

    [Fact]
    public async Task V3_contract_integrity_invalid_candidate_never_reaches_the_live_corpus()
    {
        var at = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        await new CorpusWriter(_dir, at, CodeCommit).WriteAsync(
            new OneVersionAdapter("in_force", "finance"), default);
        var manifestPath = Path.Combine(_dir, "manifest.json");
        var before = await File.ReadAllBytesAsync(manifestPath);

        var assembly = typeof(CorpusWriter).Assembly;
        var sessionType = assembly.GetType(
            "Lex.Ingest.CorpusWriteSession", throwOnError: true)!;
        var sessionObject = sessionType.GetMethod("Acquire",
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [_dir])!;
        using var session = Assert.IsAssignableFrom<IDisposable>(sessionObject);
        var baseline = sessionType.GetProperty("Baseline")!.GetValue(sessionObject)!;

        var candidateType = assembly.GetType(
            "Lex.Ingest.CorpusCandidate", throwOnError: true)!;
        var candidateObject = Activator.CreateInstance(candidateType, [_dir])!;
        using var candidate = Assert.IsAssignableFrom<IDisposable>(candidateObject);
        var write = Assert.IsAssignableFrom<Task>(candidateType
            .GetMethod("WriteTextAsync")!.Invoke(candidateObject,
                [manifestPath, "{}", CancellationToken.None]));
        await write;

        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            candidateType.GetMethod("Commit")!.Invoke(candidateObject,
                [baseline, sessionObject, null, null]));
        Assert.Contains("pre-publication integrity",
            Assert.IsType<InvalidDataException>(error.InnerException).Message,
            StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(manifestPath));
        Assert.True(CorpusIntegrity.Verify(_dir).IsValid);
        Assert.False(Directory.Exists(Path.Combine(
            _dir, ".lex-corpus-transaction")));
    }

    [Fact]
    public async Task V3_contract_normal_append_duplicate_bytes_are_content_addressed_once()
    {
        var firstAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var bytes = Encoding.UTF8.GetBytes("<html>same</html>");
        await new CorpusWriter(_dir, firstAt, CodeCommit).WriteAsync(
            new OneVersionAdapter("in_force", "finance", bodyFetch:
                SourceBodyFetch.Retrieved(bytes,
                    new(200, "text/html", "utf-8", null, null,
                        firstAt, EffectiveBodyUri))), default);
        await new CorpusWriter(_dir, firstAt.AddDays(1), CodeCommit).WriteAsync(
            new OneVersionAdapter("in_force", "finance", bodyFetch:
                SourceBodyFetch.Retrieved(bytes,
                    new(200, "text/html", "utf-8", null, null,
                        firstAt.AddDays(1), EffectiveBodyUri))), default);

        var observations = Assert.Single((await ReadVersionMeta()).Expressions)
            .Observations;
        Assert.Equal(2, observations.Count);
        Assert.Single(observations.Select(value => value.File).Distinct());
        Assert.Single(Directory.EnumerateFiles(
            OneVersionDirectory, "en--*.html", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task V3_contract_transport_evidence_layout_mints_a_new_corpus_schema()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        await new CorpusWriter(_dir, fetchedAt, CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance"), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")),
            CorpusJson.Options)!;

        Assert.Equal("lex-corpus/5", manifest.Schema);
        Assert.Equal("canon/1", manifest.Canon);
    }

    [Fact]
    public async Task V3_contract_rejects_schema_downgrade_and_canon_drift()
    {
        var at = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        await new CorpusWriter(_dir, at, CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance"), default);
        var path = Path.Combine(_dir, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        manifest.Schema = "lex-corpus/4";
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, CorpusJson.Options));
        Assert.False(CorpusIntegrity.Verify(_dir).IsValid);

        manifest.Schema = ManifestDoc.CurrentSchema;
        foreach (var canon in new[] { "canon/2", "canon/999" })
        {
            manifest.Canon = canon;
            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(manifest, CorpusJson.Options));
            Assert.False(CorpusIntegrity.Verify(_dir).IsValid);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new CorpusWriter(_dir, at.AddDays(1), CodeCommit)
                    .WriteAsync(new OneVersionAdapter("in_force", "finance"), default));
        }
    }

    [Fact]
    public async Task V3_contract_primary_body_preserves_transport_bytes_and_bounded_http_evidence()
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><root>publisher text</root>"))
            .ToArray();
        var runAt = DateTimeOffset.Parse("2026-08-14T09:00:00Z");
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var evidence = new SourceHttpEvidence(
            StatusCode: 200,
            ContentType: "application/xml",
            Charset: "utf-8",
            EntityTag: "\"publisher-etag\"",
            LastModified: DateTimeOffset.Parse("2026-08-13T12:00:00Z"),
            FetchedAt: fetchedAt,
            EffectiveSourceUri: EffectiveBodyUri);

        await new CorpusWriter(_dir, runAt, CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved(bytes, evidence, attempts: 3)), default);

        var meta = await ReadVersionMeta();
        var observation = Assert.Single(Assert.Single(meta.Expressions).Observations);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal($"en--{digest[..24]}.xml", observation.File);
        Assert.Equal(digest, observation.Sha256);
        Assert.Equal("2026-08-14T09:10:11Z", observation.RetrievedAt);
        Assert.Equal("2026-08-14T09:00:00Z", observation.ObservedFrom);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(
            OneVersionDirectory, observation.File!)));
        var http = Assert.IsType<HttpObservationEvidence>(observation.Http);
        Assert.Equal(200, http.StatusCode);
        Assert.Equal("application/xml", http.ContentType);
        Assert.Equal("utf-8", http.Charset);
        Assert.Equal("\"publisher-etag\"", http.EntityTag);
        Assert.Equal("2026-08-13T12:00:00Z", http.LastModified);
        Assert.Equal("2026-08-14T09:10:11Z", http.FetchedAt);
        Assert.Equal("retrieved", http.AttemptOutcome);
        Assert.Equal(3, http.Attempts);
        Assert.True(CorpusIntegrity.Verify(_dir).IsValid);
    }

    [Theory]
    [InlineData("iso-8859-1", true)]
    [InlineData("utf-8", false)]
    public async Task V3_contract_primary_body_retains_transport_but_rejects_untrusted_text(
        string charset,
        bool validUtf8)
    {
        var bytes = validUtf8
            ? Encoding.UTF8.GetBytes("<html>publisher text</html>")
            : new byte[] { (byte)'<', 0xc3, 0x28, (byte)'>' };
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var evidence = new SourceHttpEvidence(
            200, "text/html", charset, null, null, fetchedAt, EffectiveBodyUri);

        await new CorpusWriter(_dir, fetchedAt, CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved(bytes, evidence)), default);

        var meta = await ReadVersionMeta();
        var expression = Assert.Single(meta.Expressions);
        var observation = Assert.Single(expression.Observations);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), observation.Sha256);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(
            OneVersionDirectory, observation.File!)));
        Assert.Equal("body_parser_failure", observation.Http?.AttemptOutcome);
        Assert.False(expression.Text.Available);
        Assert.Equal("body_parser_failure", expression.Text.Reason);
        Assert.Equal("body_parser_failure", Assert.Single(
            JsonSerializer.Deserialize<ManifestDoc>(
                await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")),
                CorpusJson.Options)!.BuildIssues).Code);
    }

    [Fact]
    public async Task V3_contract_unsupported_content_type_retains_bytes_as_a_typed_parser_failure()
    {
        var bytes = Encoding.UTF8.GetBytes("%PDF-not-legal-text");
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");

        await new CorpusWriter(_dir, fetchedAt, CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved(
                    bytes,
                    new SourceHttpEvidence(
                        200, "application/pdf", null, null, null, fetchedAt,
                        EffectiveBodyUri))),
                default);

        var expression = Assert.Single((await ReadVersionMeta()).Expressions);
        var observation = Assert.Single(expression.Observations);
        Assert.EndsWith(".body", observation.File, StringComparison.Ordinal);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(
            OneVersionDirectory, observation.File!)));
        Assert.Equal("body_parser_failure", observation.Http?.AttemptOutcome);
        Assert.False(expression.Text.Available);
        Assert.Equal("body_parser_failure", expression.Text.Reason);
    }

    [Fact]
    public async Task V3_contract_missing_content_type_can_be_strictly_decoded_after_storage()
    {
        var bytes = Encoding.UTF8.GetBytes("<html>publisher text</html>");
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");

        await new CorpusWriter(_dir, fetchedAt, CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved(
                    bytes,
                    new SourceHttpEvidence(200, null, null, null, null, fetchedAt,
                        EffectiveBodyUri))),
                default);

        var expression = Assert.Single((await ReadVersionMeta()).Expressions);
        var observation = Assert.Single(expression.Observations);
        Assert.EndsWith(".body", observation.File, StringComparison.Ordinal);
        Assert.Equal("retrieved", observation.Http?.AttemptOutcome);
        Assert.True(expression.Text.Available);
    }

    [Fact]
    public async Task V3_contract_retained_parser_failure_does_not_block_a_later_valid_backfill()
    {
        var firstAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var invalid = new byte[] { (byte)'<', 0xc3, 0x28, (byte)'>' };
        await new CorpusWriter(_dir, firstAt, CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved(
                    invalid,
                    new SourceHttpEvidence(
                        200, "text/html", "utf-8", null, null, firstAt,
                        EffectiveBodyUri))),
                default);

        var valid = Encoding.UTF8.GetBytes("<html>corrected publisher text</html>");
        var secondAt = firstAt.AddDays(1);
        await new CorpusWriter(_dir, secondAt, CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved(
                    valid,
                    new SourceHttpEvidence(
                        200, "text/html", "utf-8", null, null, secondAt,
                        EffectiveBodyUri))),
                default);

        var expression = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.True(expression.Text.Available);
        Assert.Null(expression.Text.Reason);
        Assert.Equal(2, expression.Observations.Count);
        Assert.Equal(
            new string?[] { "body_parser_failure", "retrieved" },
            expression.Observations.Select(item => item.Http?.AttemptOutcome).ToArray());
        Assert.All(expression.Observations, observation =>
            Assert.True(File.Exists(Path.Combine(OneVersionDirectory, observation.File!))));
    }

    [Fact]
    public async Task V3_contract_unbounded_or_incoherent_http_evidence_fails_before_publication()
    {
        var utc = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var cases = new Dictionary<string, SourceHttpEvidence>(StringComparer.Ordinal)
        {
            ["non-success"] = new(199, "text/html", "utf-8", null, null, utc, EffectiveBodyUri),
            ["empty-content-type"] = new(200, "", "utf-8", null, null, utc, EffectiveBodyUri),
            ["whitespace-content-type"] = new(200, "   ", "utf-8", null, null, utc, EffectiveBodyUri),
            ["oversized-charset"] = new(200, "text/html", new string('x', 65), null, null, utc, EffectiveBodyUri),
            ["control-etag"] = new(200, "text/html", "utf-8", "bad\rvalue", null, utc, EffectiveBodyUri),
            ["non-utc-fetch"] = new(200, "text/html", "utf-8", null, null,
                DateTimeOffset.Parse("2026-08-14T10:10:11+01:00"), EffectiveBodyUri),
        };

        foreach (var (name, evidence) in cases)
        {
            var root = Path.Combine(_dir, name);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new CorpusWriter(root, utc, CodeCommit).WriteAsync(
                    new OneVersionAdapter(
                        "in_force", "finance",
                        bodyFetch: SourceBodyFetch.Retrieved(
                            Encoding.UTF8.GetBytes("<html>publisher text</html>"),
                            evidence)),
                    default));
            Assert.False(File.Exists(Path.Combine(root, "manifest.json")));
        }
    }

    [Fact]
    public async Task V3_contract_truncated_filename_collision_fails_without_overwriting_existing_bytes()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        await new CorpusWriter(_dir, fetchedAt, CodeCommit).WriteAsync(
            new OneVersionAdapter("in_force", "finance", bodyFetch:
                SourceBodyFetch.Retrieved(
                    Encoding.UTF8.GetBytes("<html>initial</html>"),
                    new SourceHttpEvidence(
                        200, "text/html", "utf-8", null, null, fetchedAt,
                        EffectiveBodyUri))), default);
        var bytes = Encoding.UTF8.GetBytes("<html>publisher text</html>");
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var collision = Path.Combine(
            OneVersionDirectory, $"en--{digest[..24]}.html");
        var existing = Encoding.UTF8.GetBytes("different retained bytes");
        await File.WriteAllBytesAsync(collision, existing);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, fetchedAt.AddDays(1), CodeCommit).WriteAsync(
                new OneVersionAdapter(
                    "in_force", "finance",
                    bodyFetch: SourceBodyFetch.Retrieved(
                        bytes,
                        new SourceHttpEvidence(
                            200, "text/html", "utf-8", null, null,
                            fetchedAt.AddDays(1),
                            EffectiveBodyUri))),
                default));

        Assert.Contains("prefix collision", error.Message, StringComparison.Ordinal);
        Assert.Equal(existing, await File.ReadAllBytesAsync(collision));
        Assert.True(CorpusIntegrity.Verify(_dir).IsValid);
    }

    [Fact]
    public void V3_contract_publisher_xml_parser_does_not_expand_internal_entities()
    {
        const string hostile = """
            <!DOCTYPE html [<!ENTITY payload "expanded">]>
            <html xmlns:xlink="http://www.w3.org/1999/xlink">
              <body><article><p>&payload;</p><a xlink:href="/source">source</a></article></body>
            </html>
            """;

        Assert.Throws<System.Xml.XmlException>(() =>
            StrictPublisherXml.Parse(hostile));
        var extracted = StructuredTextExtractor.Extract(
            hostile, "test:work:version");
        Assert.DoesNotContain("expanded", extracted.Extraction.Markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void V3_contract_strict_decoder_rejects_invalid_utf8_without_replacement_text()
    {
        var invalid = new byte[] { 0xc3, 0x28 };

        var error = Assert.Throws<InvalidDataException>(() =>
            StrictPublisherText.Decode(invalid, "utf-8"));

        Assert.DoesNotContain("�", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void V3_contract_strict_decoder_enforces_an_ascii_declaration()
    {
        var utf8ButNotAscii = Encoding.UTF8.GetBytes("publisher café");

        var error = Assert.Throws<InvalidDataException>(() =>
            StrictPublisherText.Decode(utf8ButNotAscii, "us-ascii"));

        Assert.Contains("US-ASCII", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V3_contract_eurlex_fetch_keeps_exact_response_bytes_and_bounded_headers()
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("<html>publisher bytes</html>"))
            .ToArray();
        using var client = new HttpClient(new ExactByteHandler(bytes));
        var adapter = new EurLexAdapter(
            scopePath: null,
            wave: null,
            http: client,
            delay: static (_, _) => Task.CompletedTask);
        var work = new Identifier(
            "http://publications.europa.eu/resource/celex/32025L0516");
        var date = new DateOnly(2025, 3, 19);
        var expression = new ExpressionRecord(
            "en", date, null, "publisher", "Test", "Test",
            EurLexAdapter.ExpressionSourceUri("en", "32025L0516"));
        var version = new VersionRecord(
            work, work, "DIR", date, null, "publisher", "true", date,
            [expression], [], new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["celex"] = "32025L0516",
            });
        var before = DateTimeOffset.UtcNow;

        var result = await adapter.FetchBody(version, expression, default);

        var after = DateTimeOffset.UtcNow;
        Assert.Equal(SourceBodyStatus.Retrieved, result.Status);
        Assert.Equal(bytes, result.Bytes);
        var http = Assert.IsType<SourceHttpEvidence>(result.Http);
        Assert.Equal(200, http.StatusCode);
        Assert.Equal("application/xhtml+xml", http.ContentType);
        Assert.Equal("utf-8", http.Charset);
        Assert.Equal("\"v3-etag\"", http.EntityTag);
        Assert.Equal(DateTimeOffset.Parse("2026-08-13T12:00:00Z"), http.LastModified);
        Assert.InRange(http.FetchedAt, before, after);
        Assert.NotNull(http.EffectiveSourceUri);
    }

    [Fact]
    public async Task V3_contract_eurlex_evidence_binds_to_final_redirect_uri()
    {
        const string finalUri = "https://eur-lex.europa.eu/final/32025L0516";
        using var client = new HttpClient(new RedirectByteHandler(finalUri));
        var adapter = new EurLexAdapter(null, null, client,
            delay: static (_, _) => Task.CompletedTask);
        var work = new Identifier("http://publications.europa.eu/resource/celex/32025L0516");
        var date = new DateOnly(2025, 3, 19);
        var expression = new ExpressionRecord("en", date, null, "publisher", "T", "T",
            EurLexAdapter.ExpressionSourceUri("en", "32025L0516"));
        var version = new VersionRecord(work, work, "DIR", date, null, "publisher", "true", date,
            [expression], [], new Dictionary<string, string> { ["celex"] = "32025L0516" });

        var result = await adapter.FetchBody(version, expression, default);

        Assert.Equal(finalUri, Assert.IsType<SourceHttpEvidence>(result.Http).EffectiveSourceUri);
    }

    [Fact]
    public async Task V3_contract_eurlex_retains_bounded_http_failure_bytes()
    {
        var rejected = Encoding.UTF8.GetBytes("publisher 404 evidence");
        using var client = new HttpClient(new FixedStatusHandler(
            System.Net.HttpStatusCode.NotFound, rejected));
        var adapter = new EurLexAdapter(null, null, client,
            delay: static (_, _) => Task.CompletedTask);
        var work = new Identifier("http://publications.europa.eu/resource/celex/32025L0516");
        var date = new DateOnly(2025, 3, 19);
        var expression = new ExpressionRecord("en", date, null, "publisher", "T", "T", null);
        var version = new VersionRecord(work, work, "DIR", date, null, "publisher", "true", date,
            [expression], [], new Dictionary<string, string> { ["celex"] = "32025L0516" });

        var result = await adapter.FetchBody(version, expression, default);

        Assert.Equal(SourceBodyStatus.PermanentNotFound, result.Status);
        Assert.Equal(rejected, result.Bytes);
        Assert.Equal(404, Assert.IsType<SourceHttpEvidence>(result.Http).StatusCode);
    }

    [Fact]
    public async Task V3_contract_eurlex_retains_final_retry_response_bytes()
    {
        var rejected = Encoding.UTF8.GetBytes("publisher final retry evidence");
        using var client = new HttpClient(new FixedStatusHandler(
            System.Net.HttpStatusCode.ServiceUnavailable, rejected));
        var adapter = new EurLexAdapter(null, null, client,
            delay: static (_, _) => Task.CompletedTask);
        var work = new Identifier(
            "http://publications.europa.eu/resource/celex/32025L0516");
        var date = new DateOnly(2025, 3, 19);
        var expression = new ExpressionRecord(
            "en", date, null, "publisher", "T", "T", null);
        var version = new VersionRecord(
            work, work, "DIR", date, null, "publisher", "true", date,
            [expression], [], new Dictionary<string, string>
            {
                ["celex"] = "32025L0516",
            });

        var result = await adapter.FetchBody(version, expression, default);

        Assert.Equal(SourceBodyStatus.RetryExhausted, result.Status);
        Assert.Equal(rejected, result.Bytes);
        Assert.Equal(503, Assert.IsType<SourceHttpEvidence>(result.Http).StatusCode);
        Assert.Equal(4, result.Attempts);
    }

    [Fact]
    public async Task V3_contract_eurlex_identity_mismatch_retains_the_rejected_response()
    {
        var mismatch = Encoding.UTF8.GetBytes(
            "<html><head><title>different expression</title></head><body>publisher</body></html>");
        using var client = new HttpClient(new IdentityMismatchHandler(mismatch));
        var adapter = new EurLexAdapter(null, null, client,
            delay: static (_, _) => Task.CompletedTask);
        var work = new Identifier(
            "http://publications.europa.eu/resource/celex/32025L0516");
        var date = new DateOnly(2025, 3, 19);
        var expression = new ExpressionRecord(
            "en", date, null, "publisher", "T", "T",
            EurLexAdapter.ExpressionSourceUri("en", "32025L0516"));
        var version = new VersionRecord(
            work, work, "DIR", date, null, "publisher", "true", date,
            [expression], [], new Dictionary<string, string>
            {
                ["celex"] = "32025L0516",
            });

        var result = await adapter.FetchBody(version, expression, default);

        Assert.Equal(SourceBodyStatus.ParserFailure, result.Status);
        Assert.Equal(mismatch, result.Bytes);
        Assert.Equal(expression.SourceUri,
            Assert.IsType<SourceHttpEvidence>(result.Http).EffectiveSourceUri);
    }

    [Fact]
    public async Task V3_contract_effective_response_uri_propagates_through_derive_and_index()
    {
        var corpus = Path.Combine(_dir, "metadata-body-corpus");
        var articles = Path.Combine(_dir, "metadata-body-articles");
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        var body = Encoding.UTF8.GetBytes("""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0">
              <act><body><article id="art_1"><num>Art. 1.</num>
                <paragraph><content><p>Synthetic publisher wording.</p></content></paragraph>
              </article></body></act>
            </akomaNtoso>
            """);
        await new CorpusWriter(corpus, fetchedAt, CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved(
                    body,
                    new SourceHttpEvidence(200, null, "utf-8", null, null, fetchedAt,
                        EffectiveBodyUri))),
                default);
        var corpusCommit = CommitGitDirectory(corpus);

        var stats = DeriveWriter.Derive(
            corpus,
            articles,
            "test",
            new string('b', 40),
            new string('d', 40),
            corpusCommit);

        Assert.Empty(stats.Errors);
        var derivedPath = Assert.Single(Directory.EnumerateFiles(
            articles, "en.json", SearchOption.AllDirectories));
        using (var derived = JsonDocument.Parse(
                   await File.ReadAllTextAsync(derivedPath)))
            Assert.Equal(EffectiveBodyUri,
                derived.RootElement.GetProperty("derived_from")
                    .GetProperty("source_uri").GetString());

        var articlesCommit = CommitGitDirectory(articles);
        var database = Path.Combine(_dir, "metadata-body.db");
        IndexFromCorpus.Build(corpus, articles, database, null,
            fetchedAt, codeCommit: new string('c', 40),
            articlesCommit: articlesCommit, corpusCommit: corpusCommit);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_uri FROM docs";
        Assert.Equal(EffectiveBodyUri, command.ExecuteScalar());
    }

    [Fact]
    public async Task V3_contract_derivation_fails_closed_on_retained_parser_failure()
    {
        var corpus = Path.Combine(_dir, "rejected-body-corpus");
        var articles = Path.Combine(_dir, "rejected-body-articles");
        var fetchedAt = DateTimeOffset.Parse("2026-08-14T09:10:11Z");
        await new CorpusWriter(corpus, fetchedAt, CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved(
                    new byte[] { (byte)'<', 0xc3, 0x28, (byte)'>' },
                    new SourceHttpEvidence(
                        200, "text/html", "utf-8", null, null, fetchedAt,
                        EffectiveBodyUri))),
                default);
        var corpusCommit = CommitGitDirectory(corpus);

        var error = Assert.Throws<InvalidDataException>(() =>
            DeriveWriter.Derive(
                corpus,
                articles,
                "test",
                new string('b', 40),
                new string('d', 40),
                corpusCommit));

        Assert.Contains("rejected acquisition evidence", error.Message,
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(articles));
    }

    private sealed class ExactByteHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/xhtml+xml")
            {
                CharSet = "utf-8",
            };
            content.Headers.LastModified = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content,
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(
                "\"v3-etag\"");
            return Task.FromResult(response);
        }
    }

    private static void RecoverCorpus(string root)
    {
        var type = typeof(CorpusWriter).Assembly.GetType(
            "Lex.Ingest.CorpusWriteSession", throwOnError: true)!;
        using var session = Assert.IsAssignableFrom<IDisposable>(
            type.GetMethod("Acquire",
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [root]));
    }

    private sealed class RedirectByteHandler(string finalUri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!string.Equals(request.RequestUri?.AbsoluteUri, finalUri, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Found)
                {
                    RequestMessage = request,
                    Headers = { Location = new Uri(finalUri) },
                });
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("<html>publisher</html>", Encoding.UTF8,
                    "application/xhtml+xml"),
            });
        }
    }

    private sealed class FixedStatusHandler(
        System.Net.HttpStatusCode status, byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(bytes),
            });
    }

    private sealed class IdentityMismatchHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var portal = string.Equals(
                request.RequestUri?.Host, "eur-lex.europa.eu",
                StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new HttpResponseMessage(portal
                ? System.Net.HttpStatusCode.OK
                : System.Net.HttpStatusCode.NotFound)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(portal ? bytes : []),
            });
        }
    }
}
