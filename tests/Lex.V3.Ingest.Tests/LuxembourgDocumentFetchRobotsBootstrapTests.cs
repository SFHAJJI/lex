using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-06c-LU item 4: the robots bootstrap for the new legilux.public.lu document-fetch host,
/// reusing <see cref="RoutedHttpAcquisitionSession"/>'s existing robots mechanism exactly as it
/// already serves the two SPARQL hosts (see <see cref="RoutedHttpAcquisitionSessionTests"/> for
/// the analogous EU redirect-route proof). The robots.txt text scripted below is the REAL content
/// this session fetched live from https://legilux.public.lu/robots.txt on 2026-09-04 (HTTP 200,
/// Content-Type text/plain, no redirect, 1,199 bytes, SHA-256
/// 0010bf4a1ab5b75e0596da21e02f3c75269e79b2c89b2017a4886a5e9d90aed1), not a hand-written stand-in:
/// every outcome below comes from <c>RobotsExclusionPolicy</c> actually parsing this exact text,
/// never from a hardcoded conditional naming these paths.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgDocumentFetchRobotsBootstrapTests
{
    // Fetched live 2026-09-04 with User-Agent Lex/0.1 (+https://github.com/SFHAJJI/lex), GET only.
    private const string RealRobotsTxt = """
        User-agent: *
        Disallow: /publications-regroupees
        Disallow: /api/rss-adm.xml
        Disallow: /eli/etat/adm/
        Disallow: /reg_ue/
        Disallow: /dir_ue/
        Disallow: /search
        Disallow: /*.svg
        Disallow: /*.docx
        Disallow: /eli/etat/adm/pa/2017/01/16/b326/jo
        Disallow: /eli/etat/adm/pa/2017/01/10/b159/jo
        Disallow: /eli/etat/adm/pa/2017/01/10/b159/jo/fr/html
        Disallow: /eli/etat/adm/pa/2017/01/10/b159/jo/fr/xml
        Disallow: /eli/etat/leg/loi/2007/01/15/n2/jo
        Disallow: /eli/etat/leg/loi/2007/01/15/n2/jo/fr/html
        Disallow: /eli/etat/leg/loi/2007/01/15/n2/jo/fr/xml
        Disallow: /eli/etat/leg/memorial/2007/8/fr/pdf
        Disallow: /eli/etat/adm/amin/2021/05/26/b2350
        # Sitemap directive
        Sitemap: https://legilux.public.lu/sitemap-index.xml
        """;

    [TestMethod]
    public async Task ABroadAdministrativeActsPathIsRefusedByTheRealRobotsText()
    {
        var result = await BootstrapForPathAsync("/eli/etat/adm/pa/2020/10/23/b4077/jo");

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.PublisherDenial, result.Kind);
    }

    [TestMethod]
    public async Task ADocxManifestationIsRefusedByTheRealRobotsText()
    {
        var result = await BootstrapForPathAsync(
            "/filestore/eli/etat/leg/loi/2020/01/01/n1/jo/fr/docx/eli-etat-leg-loi-2020-01-01-n1-jo-fr-docx.docx");

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.PublisherDenial, result.Kind);
    }

    [TestMethod]
    public async Task AnIndividuallyNamedDisallowedDocumentIsRefusedByTheRealRobotsText()
    {
        var result = await BootstrapForPathAsync("/eli/etat/leg/loi/2007/01/15/n2/jo/fr/xml");

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.PublisherDenial, result.Kind);
    }

    [TestMethod]
    public async Task AnOrdinaryFilestoreXmlPathIsAllowedByTheRealRobotsText()
    {
        // The real, live-verified example: fetching this exact path returned HTTP 200, genuine
        // Akoma Ntoso XML, matching the FRBR blocks of the queried work.
        var result = await BootstrapForPathAsync(
            "/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
            + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml");

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        Assert.IsNotNull(result.Session);
        result.Session?.Dispose();
    }

    private static async Task<RoutedHttpAcquisitionSession.StartResult> BootstrapForPathAsync(
        string path)
    {
        var target = "https://legilux.public.lu" + path;
        var request = BoundLuxembourgDocumentFetchRequest(target);
        var handler = new SingleRobotsResponseHandler(RealRobotsTxt);
        return await RoutedHttpAcquisitionSession.StartWithTestTransportAsync(
            request,
            new InMemoryCustodyStore(),
            handler,
            TimeProvider.System,
            CancellationToken.None);
    }

    private static BoundMachineRequest BoundLuxembourgDocumentFetchRequest(string target)
    {
        var targetBytes = Encoding.ASCII.GetBytes(new Uri(target).PathAndQuery);
        var queryFamily = new SourceRegistryMemberRef(
            Artifact("00000000-0000-4000-9000-000000000001"),
            "lu-document-fetch-test-query");
        var cardinality = new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody,
            rowLimit: null,
            expectedPartitionRowCount: null,
            expectedPartitionRowCountEvidenceRef: null);
        var input = MachineQueryInputArtifact.Create(
            "urn:uuid:00000000-0000-4000-9000-000000000002",
            queryFamily,
            "lu-document-fetch-test-partition",
            cardinality,
            new[]
            {
                new MachineQueryParameter(
                    "document",
                    MachineQueryParameterKind.PublisherLiteral,
                    integerValue: null,
                    textValue: target,
                    Artifact("00000000-0000-4000-9000-000000000003")),
            });
        var rendererProfile = Artifact("00000000-0000-4000-9000-000000000004");
        var rendererSource = Artifact("00000000-0000-4000-9000-000000000005");
        var plan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            queryFamily,
            rendererProfile,
            rendererSource,
            HttpRequestMethod.Get,
            target,
            targetBytes.LongLength,
            Sha256(targetBytes),
            cardinality,
            contentType: null,
            charset: null,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            expectedRequestBodyLength: null,
            expectedRequestBodySha256: null);
        var planRef = MachineQueryPlanIdentity.Create(
            "urn:uuid:00000000-0000-4000-9000-000000000006",
            plan);
        return MachineQueryBinder.BindForSend(
            plan,
            planRef,
            input,
            new FixedGetRenderer(rendererProfile, rendererSource, target));
    }

    private static SourceArtifactRef Artifact(string uuid) => new(
        $"urn:uuid:{uuid}",
        Sha256(Encoding.UTF8.GetBytes(uuid)));

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class FixedGetRenderer(
        SourceArtifactRef rendererProfileRef,
        SourceArtifactRef rendererSourceRef,
        string target) : IMachineQueryRenderer
    {
        public SourceArtifactRef RendererProfileRef { get; } = rendererProfileRef;

        public SourceArtifactRef RendererSourceRef { get; } = rendererSourceRef;

        public MachineQueryRenderOutput Render(
            MachineQueryPlan plan,
            MachineQueryInputArtifact orderedParameterSet) => new(target, []);
    }

    /// <summary>
    /// legilux.public.lu serves its robots.txt directly, 200, no redirect (unlike EU's
    /// publications.europa.eu to op.europa.eu hop): one scripted response is the whole route.
    /// </summary>
    private sealed class SingleRobotsResponseHandler(string robotsTxt) : HttpMessageHandler
    {
        private int _sendCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _sendCount) != 1)
            {
                throw new InvalidOperationException(
                    "The robots verdict must decide the run before any product send.");
            }

            var bytes = Encoding.UTF8.GetBytes(robotsTxt);
            var content = new ByteArrayContent(bytes);
            content.Headers.TryAddWithoutValidation(
                "Content-Length",
                bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version11,
                RequestMessage = request,
                Content = content,
            };
            return Task.FromResult(response);
        }
    }

    private sealed class InMemoryCustodyStore : ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _byDigest = new(StringComparer.Ordinal);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen);
            _byDigest[digest] = frozen;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, digest, frozen.LongLength, custodyClass);
            var observedAt = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-0000000000d1"),
                CustodyProtection.LockedTime,
                observedAt,
                observedAt.AddDays(91));
            return Task.FromResult(new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt, reference, policy));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[reference.ContentSha256]);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[contentSha256]);
    }
}
