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
/// the analogous EU redirect-route proof). The robots.txt text scripted below is the REAL, FULL
/// content this session fetched live from https://legilux.public.lu/robots.txt on 2026-09-04
/// (HTTP 200, Content-Type text/plain, no redirect, 1,199 bytes, SHA-256
/// 0010bf4a1ab5b75e0596da21e02f3c75269e79b2c89b2017a4886a5e9d90aed1), not an excerpt and not a
/// hand-written stand-in: <see cref="TheFixtureBytesEqualThePinnedProvenanceDigest"/> proves that
/// digest is exactly what the string below hashes to, so the provenance claim is mechanically
/// checked rather than only stated in this comment. Every outcome below comes from
/// <c>RobotsExclusionPolicy</c> actually parsing this exact text, never from a hardcoded
/// conditional naming these paths.
/// <para>
/// Line-ending policy: the live fetch used LF only (no CR), and this file is itself LF-only per
/// the repository's <c>.gitattributes</c> (<c>* text=auto eol=lf</c>). A C# raw string literal
/// preserves whatever line-ending byte sequence is physically present in its source between the
/// opening and closing delimiters, so the LF-only text below reproduces the fetched bytes exactly
/// with no CRLF translation. A single blank line immediately precedes the closing delimiter to
/// reproduce the real file's own trailing newline (raw string literals otherwise drop the line
/// break immediately before the closing <c>"""</c>).
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgDocumentFetchRobotsBootstrapTests
{
    private const string PinnedRobotsTxtSha256 =
        "0010bf4a1ab5b75e0596da21e02f3c75269e79b2c89b2017a4886a5e9d90aed1";

    // Fetched live 2026-09-04 with User-Agent Lex/0.1 (+https://github.com/SFHAJJI/lex), GET only.
    // Full 1,199-byte body, byte for byte (see the type doc comment for the line-ending policy).
    internal const string RealRobotsTxt = """
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

        Sitemap: https://legilux.public.lu/sitemap-1.xml
        Sitemap: https://legilux.public.lu/sitemap-2.xml
        Sitemap: https://legilux.public.lu/sitemap-3.xml
        Sitemap: https://legilux.public.lu/sitemap-4.xml
        Sitemap: https://legilux.public.lu/sitemap-5.xml
        Sitemap: https://legilux.public.lu/sitemap-6.xml
        Sitemap: https://legilux.public.lu/sitemap-7.xml
        Sitemap: https://legilux.public.lu/sitemap-8.xml
        Sitemap: https://legilux.public.lu/sitemap-9.xml
        Sitemap: https://legilux.public.lu/sitemap-10.xml

        """;

    [TestMethod]
    public void TheFixtureBytesEqualThePinnedProvenanceDigest()
    {
        // Makes the provenance claim in this type's doc comment mechanically checked: if a future
        // edit ever truncates or rewrites RealRobotsTxt back into an excerpt, this test fails
        // rather than merely leaving a comment that is no longer true.
        var utf8Bytes = Encoding.UTF8.GetBytes(RealRobotsTxt);
        Assert.AreEqual(1199, utf8Bytes.Length);

        var actualSha256 = Convert.ToHexString(SHA256.HashData(utf8Bytes)).ToLowerInvariant();

        Assert.AreEqual(64, actualSha256.Length);
        Assert.AreEqual(PinnedRobotsTxtSha256, actualSha256);
    }

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
        const string path = "/eli/etat/leg/loi/2007/01/15/n2/jo/fr/xml";

        var result = await BootstrapForPathAsync(path);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.PublisherDenial, result.Kind);
        Assert.AreEqual(path, result.DeniedRequestPath);
    }

    [TestMethod]
    public async Task AFilestoreXmlPathForAnIndividuallyDisallowedDocumentIsRefusedViaItsDerivedEliPagePath()
    {
        // Design ruling (D1-06c-LU review): the real robots.txt disallows /eli/... PAGE paths,
        // but this route fetches /filestore/... paths, a prefix those /eli/ rules never match on
        // their own. A filestore path's own structure embeds its expression's exact /eli/ page
        // path once /filestore/ and the trailing filename segment are stripped, so the route must
        // also evaluate robots against that derived page path. Here the raw filestore path below
        // matches no Disallow rule by itself, but its derived page path
        // /eli/etat/leg/loi/2007/01/15/n2/jo/fr/xml is individually named by the real robots.txt
        // (line 15), so the fetch must still be refused via the derived-path check alone.
        const string filestorePath =
            "/filestore/eli/etat/leg/loi/2007/01/15/n2/jo/fr/xml/"
            + "eli-etat-leg-loi-2007-01-15-n2-jo-fr-xml.xml";
        const string derivedEliPagePath = "/eli/etat/leg/loi/2007/01/15/n2/jo/fr/xml";

        var result = await BootstrapForPathAsync(filestorePath);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.PublisherDenial, result.Kind);
        Assert.AreEqual(derivedEliPagePath, result.DeniedRequestPath);
        Assert.AreNotEqual(
            filestorePath,
            result.DeniedRequestPath,
            "The raw filestore fetch path itself matches no Disallow rule; only its derived " +
            "/eli/ page path does, so the refusal must name that derived path, not the fetch path.");
    }

    /// <summary>
    /// RULING lex-event-20260904T180444431Z-13c6f8f86ddf4f02857cf4001c202143: the third robots path.
    /// </summary>
    /// <remarks>
    /// THE TEST LESSON, recorded plainly rather than presented as routine coverage. The publisher's
    /// robots.txt groups four lines around loi 2007/01/15/n2: the act page
    /// <c>/eli/etat/leg/loi/2007/01/15/n2/jo</c>, and its html, xml and pdf manifestations. Three of
    /// those four derive correctly from their own filestore path, and
    /// <see cref="AFilestoreXmlPathForAnIndividuallyDisallowedDocumentIsRefusedViaItsDerivedEliPagePath"/>
    /// exercises one of the three. The fourth does not: the publisher wrote the PDF's line as
    /// <c>/eli/etat/leg/memorial/2007/8/fr/pdf</c> while the file actually lives under
    /// <c>memorial/2007/a8</c>, so the two derivable paths both miss it and a literal evaluation
    /// permits a fetch the publisher plainly meant to withhold. LU-1's guard was not wrong; the test
    /// that vouched for it happened to pick the manifestation that works. This test drives the one
    /// that does not.
    /// <para>
    /// The fix adds no name list and does not repair the publisher's file toward intent: the act's
    /// own ELI page path comes from the store (manifestation to expression to work) and is evaluated
    /// as a third path, so this PDF is refused because that act's own <c>/jo</c> page is disallowed
    /// by prefix. The refusal must NAME that path, which is what makes the reason auditable rather
    /// than a bare denial. The rgd 1977/11/16/n3 PDF is the same shape: its file lives under
    /// <c>memorial/1977/a67</c>, outside its own act's ELI path entirely.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ThePdfOfAnIndividuallyDisallowedActIsRefusedViaTheActsOwnEliPagePath()
    {
        // The real filestore path of the loi 2007/01/15/n2 PDF, which lives under the memorial
        // a8 issue rather than under the act's own ELI path.
        const string filestorePath =
            "/filestore/eli/etat/leg/memorial/2007/a8/fr/pdf/"
            + "eli-etat-leg-memorial-2007-a8-fr-pdf.pdf";
        const string derivedPagePath = "/eli/etat/leg/memorial/2007/a8/fr/pdf";
        const string actEliPagePath = "/eli/etat/leg/loi/2007/01/15/n2/jo";

        // First, the finding itself: the two paths this route can derive on its own are both
        // permitted by the real robots.txt, so without the act's page path the fetch would proceed.
        var withoutActPath = await BootstrapForPathAsync(filestorePath);
        Assert.AreEqual(
            OfficialHttpAcquisitionOutcomeKind.ExecutedObservation,
            withoutActPath.Kind,
            "the publisher's own line names memorial/2007/8, not memorial/2007/a8, so a literal "
            + "evaluation of the fetch path and its derived page path permits this fetch.");
        withoutActPath.Session?.Dispose();

        var result = await BootstrapForPathAsync(filestorePath, actEliPagePath);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.PublisherDenial, result.Kind);
        Assert.AreEqual(actEliPagePath, result.DeniedRequestPath);
        Assert.AreNotEqual(filestorePath, result.DeniedRequestPath);
        Assert.AreNotEqual(derivedPagePath, result.DeniedRequestPath);
    }

    /// <summary>
    /// The third path can only ever refuse: supplying an act page path the robots.txt permits
    /// changes nothing, so the mechanism fetches strictly less and never more. Without this, a
    /// third path that somehow admitted a fetch would look the same as one that changed nothing.
    /// </summary>
    [TestMethod]
    public async Task AnAllowedActEliPagePathChangesNothingAboutAnAllowedFetch()
    {
        const string filestorePath =
            "/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
            + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml";

        var result = await BootstrapForPathAsync(
            filestorePath, "/eli/etat/leg/loi/2017/03/14/a439/jo");

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        result.Session?.Dispose();
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
        string path,
        params string[] additionalRobotsPaths)
    {
        var target = "https://legilux.public.lu" + path;
        var request = BoundLuxembourgDocumentFetchRequest(target);
        var handler = new SingleRobotsResponseHandler(RealRobotsTxt);
        return await RoutedHttpAcquisitionSession.StartWithTestTransportAsync(
            request,
            new InMemoryCustodyStore(),
            handler,
            TimeProvider.System,
            additionalRobotsPaths,
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
