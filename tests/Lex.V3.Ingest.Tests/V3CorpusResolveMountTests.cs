using System.Text;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

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
    public async Task MissingOrChangedMountInputsFailClosedBeforeAReaderIsReturned()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));

        await fixture.RestoreCapabilityManifestAsync();
        var indexPath = Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName);
        var bytes = await File.ReadAllBytesAsync(indexPath);
        bytes[^1] ^= 0xff;
        await File.WriteAllBytesAsync(indexPath, bytes);
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));
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
        var bytes = Encoding.UTF8.GetBytes(
            "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":" +
            System.Text.Json.JsonSerializer.Serialize(identifier) + "}}");
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

        private MountedFixture(
            string directory,
            string expressionIri,
            string corpusSha256,
            string indexSha256,
            byte[] capabilityManifestBytes)
        {
            Directory = directory;
            ExpressionIri = expressionIri;
            CorpusSha256 = corpusSha256;
            IndexSha256 = indexSha256;
            _capabilityManifestBytes = capabilityManifestBytes;
        }

        public string Directory { get; }
        public string ExpressionIri { get; }
        public string CorpusSha256 { get; }
        public string IndexSha256 { get; }

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
            var expression = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation
                .Outcomes.First(static value => value.Article is not null).Article!.PublisherExpressionIri;
            var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-corpus-mount-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.IndexFileName),
                index.IndexBytes.ToArray());
            var capabilityBytes = index.CapabilityManifestBytes.ToArray();
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName),
                capabilityBytes);
            return new MountedFixture(
                directory,
                expression,
                corpus.ArtifactRef.Sha256,
                index.IndexRef.Sha256,
                capabilityBytes);
        }

        public Task RestoreCapabilityManifestAsync() => File.WriteAllBytesAsync(
            Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
            _capabilityManifestBytes);

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
