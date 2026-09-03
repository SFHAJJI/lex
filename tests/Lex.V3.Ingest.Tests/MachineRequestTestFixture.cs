using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Tests;

internal static class MachineRequestTestFixture
{
    private const string EuQueryUri = "https://publications.europa.eu/webapi/rdf/sparql";
    private static readonly byte[] RendererProfileCanonicalBytes = Encoding.UTF8.GetBytes(
        "fixture-renderer-profile/1\nprofile=eu-test\n");
    private static readonly byte[] RendererSourceCanonicalBytes = Encoding.UTF8.GetBytes(
        "fixture-renderer-source/1\nrenderer=fixed\n");
    private static readonly byte[] ContentTypeRegistryCanonicalBytes = Encoding.UTF8.GetBytes(
        "{\"schema\":\"fixture-content-type-registry/1\",\"members\":[\"application/sparql-query\"]}\n");
    private static readonly byte[] QueryRegistryCanonicalBytes = Encoding.UTF8.GetBytes(
        "{\"schema\":\"fixture-query-registry/1\",\"members\":[\"eu-test-query\"]}\n");
    private static readonly byte[] ParameterProvenanceCanonicalBytes = Encoding.UTF8.GetBytes(
        "fixture-parameter-provenance/1\nparameter=limit\n");
    private static readonly SourceArtifactRef RendererProfile = Artifact(
        "00000000-0000-4000-8000-0000000000ab",
        RendererProfileCanonicalBytes);
    private static readonly SourceArtifactRef RendererSource = Artifact(
        "00000000-0000-4000-8000-0000000000ac",
        RendererSourceCanonicalBytes);
    /// <summary>
    /// internal rather than private: the one digest a test can use to prove that
    /// RecordingCustodyStore.RefuseFallback really refuses this table rather than merely not
    /// reaching it (TheFallbackFreeDoubleRefusesTheSharedFixtureTable).
    /// </summary>
    internal static readonly SourceArtifactRef ContentTypeRegistry = Artifact(
        "00000000-0000-4000-8000-0000000000b0",
        ContentTypeRegistryCanonicalBytes);
    private static readonly SourceArtifactRef QueryRegistry = Artifact(
        "00000000-0000-4000-8000-0000000000aa",
        QueryRegistryCanonicalBytes);
    private static readonly SourceArtifactRef ParameterProvenance = Artifact(
        "00000000-0000-4000-8000-0000000000ad",
        ParameterProvenanceCanonicalBytes);

    internal static BoundMachineRequest EuropeanUnionRequest()
    {
        var queryFamily = new SourceRegistryMemberRef(QueryRegistry, "eu-test-query");
        var cardinality = new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody,
            null,
            null,
            null);
        var input = MachineQueryInputArtifact.Create(
            "urn:uuid:00000000-0000-4000-8000-0000000000ae",
            queryFamily,
            "eu-test-partition",
            cardinality,
            [
                new MachineQueryParameter(
                    "limit",
                    MachineQueryParameterKind.BoundedInteger,
                    1,
                    null,
                    ParameterProvenance),
            ]);
        var body = Encoding.UTF8.GetBytes("SELECT * WHERE { ?s ?p ?o } LIMIT 1");
        var targetBytes = Encoding.ASCII.GetBytes(new Uri(EuQueryUri).PathAndQuery);
        var plan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            queryFamily,
            RendererProfile,
            RendererSource,
            HttpRequestMethod.Post,
            EuQueryUri,
            targetBytes.LongLength,
            Sha256(targetBytes),
            cardinality,
            new SourceRegistryMemberRef(ContentTypeRegistry, "application/sparql-query"),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            body.LongLength,
            Sha256(body));
        var planRef = MachineQueryPlanIdentity.Create(
            "urn:uuid:00000000-0000-4000-8000-0000000000af",
            plan);
        return MachineQueryBinder.BindForSend(
            plan,
            planRef,
            input,
            new FixedRenderer(RendererProfile, RendererSource, EuQueryUri, body));
    }

    /// <summary>
    /// Item 1b, Decision 75's closure. Renderer profile and renderer source used to be answerable
    /// here, standing in for what a renderer had not yet been made to produce itself. Both are
    /// removed: <see cref="FixedRenderer"/> now produces them, so a send retains and reopens what
    /// it retained rather than ever reaching this table for either. What remains is genuinely
    /// external in the sense this table's name always claimed: registries and provenance that no
    /// renderer is responsible for producing, and that a pipeline step ahead of this one is
    /// expected to have already placed in the store.
    /// </summary>
    internal static bool TryReopenPreexistingArtifact(
        string contentSha256,
        out ReadOnlyMemory<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(contentSha256);
        if (string.Equals(contentSha256, ContentTypeRegistry.Sha256, StringComparison.Ordinal))
        {
            canonicalBytes = ContentTypeRegistryCanonicalBytes.ToArray();
            return true;
        }

        if (string.Equals(contentSha256, QueryRegistry.Sha256, StringComparison.Ordinal))
        {
            canonicalBytes = QueryRegistryCanonicalBytes.ToArray();
            return true;
        }

        if (string.Equals(contentSha256, ParameterProvenance.Sha256, StringComparison.Ordinal))
        {
            canonicalBytes = ParameterProvenanceCanonicalBytes.ToArray();
            return true;
        }

        canonicalBytes = default;
        return false;
    }

    private static SourceArtifactRef Artifact(string uuid, ReadOnlySpan<byte> canonicalBytes) =>
        new($"urn:uuid:{uuid}", Sha256(canonicalBytes));

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class FixedRenderer(
        SourceArtifactRef rendererProfileRef,
        SourceArtifactRef rendererSourceRef,
        string target,
        byte[] body) : IMachineQueryRenderer
    {
        public SourceArtifactRef RendererProfileRef { get; } = rendererProfileRef;

        public SourceArtifactRef RendererSourceRef { get; } = rendererSourceRef;

        // Statement bodies deliberately, not conditional expressions. bytes is null ? null : bytes
        // compiles and hands back a present, empty ReadOnlyMemory when the array conversion runs
        // on null, which read as this renderer producing zero bytes rather than declining to
        // produce any. That exact shape produced a real defect earlier in this project.
        public ReadOnlyMemory<byte>? CopyRendererProfileBytes()
        {
            return RendererProfileCanonicalBytes;
        }

        public ReadOnlyMemory<byte>? CopyRendererSourceBytes()
        {
            return RendererSourceCanonicalBytes;
        }

        public MachineQueryRenderOutput Render(
            MachineQueryPlan plan,
            MachineQueryInputArtifact orderedParameterSet) =>
            new(target, body);
    }
}
