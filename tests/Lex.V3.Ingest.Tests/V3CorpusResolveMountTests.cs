using System.Text;
using System.Security.Cryptography;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class V3CorpusResolveMountTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 9, 18, 7, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task MountedExactIndexServesResolveSuccessAndBindsSnapshot()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request(fixture.ExpressionIri);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual(fixture.CorpusSha256, envelope.Context.Snapshot.SnapshotSha256);
        Assert.AreEqual(ObservedAt, envelope.Context.Freshness.ObservedAt);
        Assert.AreEqual("stale", envelope.Context.Freshness.UpstreamHealth);
        Assert.AreEqual(fixture.ExpressionIri, envelope.Result!.Value.GetProperty("expression_iri").GetString());
        Assert.AreEqual(fixture.IndexSha256, envelope.Result.Value.GetProperty("index_sha256").GetString());
        Assert.IsGreaterThan(0, envelope.Result.Value.GetProperty("article_identities").GetArrayLength());
    }

    [TestMethod]
    public async Task UnknownIdentifierReturnsReviewedDomainRefusalFromTheMountedCorpus()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("eli/unknown");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code);
        Assert.AreEqual("eli/unknown", envelope.Refusal.HelpfulPayload.GetProperty("requested_identifier").GetString());
        Assert.AreEqual(fixture.CorpusSha256, envelope.Context.Snapshot.SnapshotSha256);
    }

    [TestMethod]
    public async Task NormalizedPublisherTitleUsesR1WithoutSelectingAnExpression()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddWorkTitleAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request(fixture.WorkTitle.ToUpperInvariant() + "...");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("r1_work_discovery", envelope.Result!.Value.GetProperty("retrieval_lane").GetString());
        Assert.AreEqual("exact_normalized_title", envelope.Result.Value.GetProperty("match_reason").GetString());
        Assert.AreEqual(fixture.PublisherWid, envelope.Result.Value.GetProperty("work_identifier").GetString());
        CollectionAssert.Contains(
            envelope.Result.Value.GetProperty("expressions")
                .EnumerateArray().Select(static value => value.GetString()).ToArray(),
            fixture.ExpressionIri);
        Assert.IsFalse(envelope.Result.Value.TryGetProperty("expression_iri", out _),
            "R1 resolves a work and must not silently select one expression.");
    }

    [TestMethod]
    public async Task MissingTitleCapabilityRefusesR1WithoutGuessing()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("ordinary unknown words");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("retrieval_mode_unavailable", envelope.Refusal!.Code);
        Assert.AreEqual(
            "r1_work_discovery",
            envelope.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
        CollectionAssert.AreEqual(
            new[] { "r0_exact_coordinate" },
            envelope.Refusal.HelpfulPayload.GetProperty("available_modes")
                .EnumerateArray().Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task PrefixMatchingMultipleWorksReturnsDeterministicAmbiguity()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var secondWork = await fixture.AddTwoWorkTitlesAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("Reglement sur l'epreuve");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("ambiguous_identifier", envelope.Refusal!.Code);
        Assert.AreEqual("unique_prefix", envelope.Refusal.HelpfulPayload.GetProperty("match_reason").GetString());
        CollectionAssert.AreEqual(
            new[] { fixture.PublisherWid, secondWork }.Order(StringComparer.Ordinal).ToArray(),
            envelope.Refusal.HelpfulPayload.GetProperty("candidates")
                .EnumerateArray().Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"resolve\",\"parameters\":{}}")]
    [DataRow("{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":7}}")]
    [DataRow("{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":\" \\t\"}}")]
    public async Task UnusableIdentifierRemainsABelowEnvelopeTransportFailure(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = RequestBody(body);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        using var problem = System.Text.Json.JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(problem.RootElement.TryGetProperty("verdict", out _));
        Assert.IsFalse(problem.RootElement.TryGetProperty("refusal", out _));
        Assert.IsFalse(
            System.Text.Json.JsonSerializer.Serialize(problem.RootElement)
                .Contains("requested_identifier", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MissingOrChangedMountInputsFailClosedBeforeAReaderIsReturned()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));

        await fixture.RestoreCapabilityManifestAsync();
        var corpusPath = Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName);
        var corpusBytes = await File.ReadAllBytesAsync(corpusPath);
        corpusBytes[^2] ^= 0x01;
        await File.WriteAllBytesAsync(corpusPath, corpusBytes);
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));

        await fixture.RestoreCorpusAsync();
        var indexPath = Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName);
        var bytes = await File.ReadAllBytesAsync(indexPath);
        bytes[^1] ^= 0xff;
        await File.WriteAllBytesAsync(indexPath, bytes);
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));
    }

    [TestMethod]
    public async Task ValidCorpusFromAnotherBuildIsRejectedByTheIndexCorpusBinding()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var original = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(fixture.CorpusBytes);
        var replacementProfiles = original.Set.ProfileIdentities.ToArray();
        replacementProfiles[0] = replacementProfiles[0] == new string('f', 64)
            ? new string('e', 64)
            : new string('f', 64);
        Array.Sort(replacementProfiles, StringComparer.Ordinal);
        var foreignBytes = LexCorpus6Builder.Write(original.Set with
        {
            ProfileIdentities = replacementProfiles,
        });
        var foreign = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(foreignBytes);
        Assert.AreNotEqual(original.ArtifactRef, foreign.ArtifactRef);
        await File.WriteAllBytesAsync(
            Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName),
            foreignBytes);

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));

        StringAssert.Contains(exception.Message, "does not bind the mounted corpus/6 artifact");
    }

    [TestMethod]
    public async Task WorkIdentifierMatchingMultipleExpressionsReturnsAllAmbiguousCandidates()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var alternateExpression = await fixture.AddAlternateExpressionForSameWorkAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        Assert.IsNotNull(fixture.PublisherWid);
        var context = Request(fixture.PublisherWid);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("ambiguous_identifier", envelope.Refusal!.Code);
        Assert.AreEqual(
            fixture.PublisherWid,
            envelope.Refusal.HelpfulPayload.GetProperty("requested_identifier").GetString());
        var candidates = envelope.Refusal.HelpfulPayload.GetProperty("candidates")
            .EnumerateArray().Select(static value => value.GetString()).ToArray();
        CollectionAssert.AreEqual(
            new[] { fixture.ExpressionIri, alternateExpression }.Order(StringComparer.Ordinal).ToArray(),
            candidates);
    }

    [TestMethod]
    public async Task AbsentMountPreservesNoCorpusMountedRefusal()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-no-mount-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNull(mount);
            var context = Request("eli/example");
            var handler = new V3ApiHandler(
                SyntheticApiState.Unavailable,
                new V3PlatformHost(),
                static () => ObservedAt,
                mount);

            await handler.HandleAsync(context, CancellationToken.None);

            var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
            Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DefaultHttpContext Request(string identifier)
    {
        return RequestBody(
            "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":" +
            System.Text.Json.JsonSerializer.Serialize(identifier) + "}}");
    }

    private static DefaultHttpContext RequestBody(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-request";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = V3ResolveRestRoute.RawTarget;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static byte[] ResponseBytes(DefaultHttpContext context) =>
        ((MemoryStream)context.Response.Body).ToArray();

    private sealed class MountedFixture : IAsyncDisposable
    {
        private readonly byte[] _capabilityManifestBytes;
        private readonly byte[] _corpusBytes;

        private MountedFixture(
            string directory,
            string expressionIri,
            string publisherWid,
            string workTitle,
            string corpusSha256,
            string indexSha256,
            byte[] capabilityManifestBytes,
            byte[] corpusBytes)
        {
            Directory = directory;
            ExpressionIri = expressionIri;
            PublisherWid = publisherWid;
            WorkTitle = workTitle;
            CorpusSha256 = corpusSha256;
            IndexSha256 = indexSha256;
            _capabilityManifestBytes = capabilityManifestBytes;
            _corpusBytes = corpusBytes;
        }

        public string Directory { get; }
        public string ExpressionIri { get; }
        public string PublisherWid { get; }
        public string WorkTitle { get; }
        public string CorpusSha256 { get; }
        public string IndexSha256 { get; }
        public byte[] CorpusBytes => _corpusBytes.ToArray();

        public static async Task<MountedFixture> CreateAsync()
        {
            const string manifestation =
                "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
            const string item =
                "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
            var xml = await File.ReadAllBytesAsync(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "LuAknLegalContent",
                "loi-1991-08-10-n3--2024-02-01--fr.bin"));
            ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
            var luxembourg = await LuxembourgGazetteAcquisitionTests
                .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
            var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
                luxembourgOverride: luxembourg,
                luxembourgStore: store);
            var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
            Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
            var index = LuxembourgIndexBuilder.TryBuild(envelope, out var indexRefusal, out var indexDetail);
            Assert.IsNotNull(index, $"{indexRefusal}: {indexDetail}");
            var article = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation
                .Outcomes.First(static value => value.Article is not null).Article!;
            var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-corpus-mount-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.IndexFileName),
                index.IndexBytes.ToArray());
            var capabilityBytes = index.CapabilityManifestBytes.ToArray();
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName),
                capabilityBytes);
            var corpusBytes = corpus.CanonicalBytes.ToArray();
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.CorpusFileName),
                corpusBytes);
            return new MountedFixture(
                directory,
                article.PublisherExpressionIri,
                "fixture-work-identifier",
                "Règlement sur l'épreuve terminale",
                corpus.ArtifactRef.Sha256,
                index.IndexRef.Sha256,
                capabilityBytes,
                corpusBytes);
        }

        public async Task<string> AddAlternateExpressionForSameWorkAsync()
        {
            var indexPath = Path.Combine(Directory, V3CorpusMount.IndexFileName);
            LuxembourgIndexBuilder.ArticleRow source;
            LuxembourgIndexBuilder.MemberRow[] members;
            LuxembourgIndexBuilder.ArticleRow[] articles;
            LuxembourgIndexBuilder.WorkTitleRow[] titles;
            var alternateExpression = ExpressionIri + "/alternate-expression";
            using (var connection = LuxembourgIndexBuilder.Open(indexPath, SqliteOpenMode.ReadWrite))
            {
                source = ReadArticles(connection).First(value =>
                    string.Equals(value.ExpressionIri, ExpressionIri, StringComparison.Ordinal));
                using (var bindWork = connection.CreateCommand())
                {
                    bindWork.CommandText =
                        "UPDATE articles SET publisher_wid=$wid WHERE expression_iri=$expression";
                    bindWork.Parameters.AddWithValue("$wid", PublisherWid);
                    bindWork.Parameters.AddWithValue("$expression", ExpressionIri);
                    Assert.IsGreaterThan(0, bindWork.ExecuteNonQuery());
                }
                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = """
                        INSERT INTO articles VALUES(
                          $identity,$object,$expression,$publisher,$wid,$date,$language,$text,$tokens)
                        """;
                    insert.Parameters.AddWithValue("$identity", new string('f', 64));
                    insert.Parameters.AddWithValue("$object", source.ObjectRefSha256);
                    insert.Parameters.AddWithValue("$expression", alternateExpression);
                    insert.Parameters.AddWithValue("$publisher", source.PublisherId);
                    insert.Parameters.AddWithValue("$wid", PublisherWid);
                    insert.Parameters.AddWithValue("$date", (object?)source.ApplicabilityDate ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$language", source.Language);
                    insert.Parameters.AddWithValue("$text", source.SearchableText);
                    insert.Parameters.AddWithValue("$tokens", source.TokensJson);
                    Assert.AreEqual(1, insert.ExecuteNonQuery());
                }

                members = ReadMembers(connection);
                articles = ReadArticles(connection);
                titles = ReadWorkTitles(connection);
                using var stamp = connection.CreateCommand();
                stamp.CommandText = "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1";
                stamp.Parameters.AddWithValue(
                    "$digest", LuxembourgIndexBuilder.HashLogicalRows(members, articles, titles));
                Assert.AreEqual(1, stamp.ExecuteNonQuery());
            }

            var indexBytes = await File.ReadAllBytesAsync(indexPath);
            var indexDigest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var manifest = LuxembourgIndexBuilder.MeasureCapabilities(indexDigest, articles, titles);
            using var stream = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
                stream.ToArray());
            return alternateExpression;
        }

        public async Task AddWorkTitleAsync()
        {
            var indexPath = Path.Combine(Directory, V3CorpusMount.IndexFileName);
            LuxembourgIndexBuilder.MemberRow[] members;
            LuxembourgIndexBuilder.ArticleRow[] articles;
            LuxembourgIndexBuilder.WorkTitleRow[] titles;
            using (var connection = LuxembourgIndexBuilder.Open(indexPath, SqliteOpenMode.ReadWrite))
            {
                using (var bindWork = connection.CreateCommand())
                {
                    bindWork.CommandText =
                        "UPDATE articles SET publisher_wid=$wid WHERE expression_iri=$expression";
                    bindWork.Parameters.AddWithValue("$wid", PublisherWid);
                    bindWork.Parameters.AddWithValue("$expression", ExpressionIri);
                    Assert.IsGreaterThan(0, bindWork.ExecuteNonQuery());
                }
                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = "INSERT INTO work_titles VALUES($wid,$expression,$language,$title,$normalized,$date,'title')";
                    insert.Parameters.AddWithValue("$wid", PublisherWid);
                    insert.Parameters.AddWithValue("$expression", ExpressionIri);
                    insert.Parameters.AddWithValue("$language", "fra");
                    insert.Parameters.AddWithValue("$title", WorkTitle);
                    insert.Parameters.AddWithValue("$normalized", LuxembourgIndexBuilder.NormalizeTitle(WorkTitle));
                    insert.Parameters.AddWithValue("$date", "2024-02-01");
                    Assert.AreEqual(1, insert.ExecuteNonQuery());
                }

                members = ReadMembers(connection);
                articles = ReadArticles(connection);
                titles = ReadWorkTitles(connection);
                using var stamp = connection.CreateCommand();
                stamp.CommandText = "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1";
                stamp.Parameters.AddWithValue(
                    "$digest", LuxembourgIndexBuilder.HashLogicalRows(members, articles, titles));
                Assert.AreEqual(1, stamp.ExecuteNonQuery());
            }

            var indexBytes = await File.ReadAllBytesAsync(indexPath);
            var indexDigest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var manifest = LuxembourgIndexBuilder.MeasureCapabilities(indexDigest, articles, titles);
            using var stream = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
                stream.ToArray());
        }

        public async Task<string> AddTwoWorkTitlesAsync()
        {
            const string secondWork = "fixture-work-identifier-two";
            var indexPath = Path.Combine(Directory, V3CorpusMount.IndexFileName);
            LuxembourgIndexBuilder.MemberRow[] members;
            LuxembourgIndexBuilder.ArticleRow[] articles;
            LuxembourgIndexBuilder.WorkTitleRow[] titles;
            using (var connection = LuxembourgIndexBuilder.Open(indexPath, SqliteOpenMode.ReadWrite))
            {
                var source = ReadArticles(connection).First(value =>
                    string.Equals(value.ExpressionIri, ExpressionIri, StringComparison.Ordinal));
                using (var bindWork = connection.CreateCommand())
                {
                    bindWork.CommandText =
                        "UPDATE articles SET publisher_wid=$wid WHERE expression_iri=$expression";
                    bindWork.Parameters.AddWithValue("$wid", PublisherWid);
                    bindWork.Parameters.AddWithValue("$expression", ExpressionIri);
                    Assert.IsGreaterThan(0, bindWork.ExecuteNonQuery());
                }
                using (var insertArticle = connection.CreateCommand())
                {
                    insertArticle.CommandText = "INSERT INTO articles VALUES($identity,$object,$expression,$publisher,$wid,$date,$language,$text,$tokens)";
                    insertArticle.Parameters.AddWithValue("$identity", new string('e', 64));
                    insertArticle.Parameters.AddWithValue("$object", source.ObjectRefSha256);
                    insertArticle.Parameters.AddWithValue("$expression", ExpressionIri + "/second-work");
                    insertArticle.Parameters.AddWithValue("$publisher", source.PublisherId);
                    insertArticle.Parameters.AddWithValue("$wid", secondWork);
                    insertArticle.Parameters.AddWithValue("$date", (object?)source.ApplicabilityDate ?? DBNull.Value);
                    insertArticle.Parameters.AddWithValue("$language", source.Language);
                    insertArticle.Parameters.AddWithValue("$text", source.SearchableText);
                    insertArticle.Parameters.AddWithValue("$tokens", source.TokensJson);
                    Assert.AreEqual(1, insertArticle.ExecuteNonQuery());
                }
                foreach (var row in new[]
                {
                    (PublisherWid, ExpressionIri, WorkTitle, "2024-02-01"),
                    (secondWork, ExpressionIri + "/second-work", WorkTitle + " complément", "2024-01-01"),
                })
                {
                    using var insertTitle = connection.CreateCommand();
                    insertTitle.CommandText = "INSERT INTO work_titles VALUES($wid,$expression,'fra',$title,$normalized,$date,'title')";
                    insertTitle.Parameters.AddWithValue("$wid", row.Item1);
                    insertTitle.Parameters.AddWithValue("$expression", row.Item2);
                    insertTitle.Parameters.AddWithValue("$title", row.Item3);
                    insertTitle.Parameters.AddWithValue("$normalized", LuxembourgIndexBuilder.NormalizeTitle(row.Item3));
                    insertTitle.Parameters.AddWithValue("$date", row.Item4);
                    Assert.AreEqual(1, insertTitle.ExecuteNonQuery());
                }

                members = ReadMembers(connection);
                articles = ReadArticles(connection);
                titles = ReadWorkTitles(connection);
                using var stamp = connection.CreateCommand();
                stamp.CommandText = "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1";
                stamp.Parameters.AddWithValue(
                    "$digest", LuxembourgIndexBuilder.HashLogicalRows(members, articles, titles));
                Assert.AreEqual(1, stamp.ExecuteNonQuery());
            }

            var indexBytes = await File.ReadAllBytesAsync(indexPath);
            var indexDigest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var manifest = LuxembourgIndexBuilder.MeasureCapabilities(indexDigest, articles, titles);
            using var stream = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
                stream.ToArray());
            return secondWork;
        }

        private static LuxembourgIndexBuilder.MemberRow[] ReadMembers(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT object_ref_sha256,source_ordinal,outcome,rights_disposition,stage3_outcomes_json,gaps_json FROM members ORDER BY object_ref_sha256";
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexBuilder.MemberRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
            return values.ToArray();
        }

        private static LuxembourgIndexBuilder.ArticleRow[] ReadArticles(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,expression_iri,publisher_id,publisher_wid,applicability_date,language,searchable_text,tokens_json FROM articles ORDER BY article_identity_sha256";
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexBuilder.ArticleRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetString(8)));
            return values.ToArray();
        }

        private static LuxembourgIndexBuilder.WorkTitleRow[] ReadWorkTitles(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT work_identifier,expression_iri,language,title,normalized_title,document_date,title_kind FROM work_titles ORDER BY work_identifier,expression_iri,language,title,title_kind";
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexBuilder.WorkTitleRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6)));
            return values.ToArray();
        }

        public Task RestoreCapabilityManifestAsync() => File.WriteAllBytesAsync(
            Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
            _capabilityManifestBytes);

        public Task RestoreCorpusAsync() => File.WriteAllBytesAsync(
            Path.Combine(Directory, V3CorpusMount.CorpusFileName),
            _corpusBytes);

        public ValueTask DisposeAsync()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
