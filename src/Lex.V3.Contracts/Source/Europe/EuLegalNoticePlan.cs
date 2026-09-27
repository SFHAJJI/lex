using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Binds the one bounded GET of the EUR-Lex legal notice
/// (<see cref="EuLegalNoticeEvidence.RequestedUri"/>) into a real, sendable
/// <see cref="BoundMachineRequest"/> through the same <see cref="MachineQueryBinder"/> door
/// <see cref="EuDocumentFetchPlan"/> uses for a Cellar document. It is the fixed-address sibling of
/// that plan: there is exactly one admitted request, so the target is a constant of this type
/// rather than a per-object address, and the bound input carries one non-header parameter that
/// retains the language selection the pinned URI already states.
/// </summary>
/// <remarks>
/// <para>
/// Decision 88 admits one legal-notice GET per corpus run, as rights evidence only. Decision 23
/// forbids EUR-Lex as a body source. This plan cannot be pointed at any other URI: the request
/// target is the pinned constant, <see cref="OfficialMachineQuerySourceProfiles.ResolveFor(BoundMachineRequestIdentity)"/>
/// resolves the <see cref="OfficialMachineQuerySourceProfileId.EuropeanUnionLegalNotice"/> profile
/// by exact string, and <see cref="EuLegalNoticeEvidence.FromRoute"/> refuses a route whose first
/// hop is anything else.
/// </para>
/// <para>
/// The bound query reuses <see cref="EuDocumentFetchBoundQuery"/>: the legal notice is a document
/// fetch of a fixed address, and every artifact that feeds it (plan, plan identity, input,
/// request) has the same shape and the same consumers.
/// </para>
/// </remarks>
public sealed class EuLegalNoticePlan
{
    /// <summary>This route's own ordered parameter declaration; the one name below is read from it.</summary>
    public static DocumentFetchParameterContract ParameterContract =>
        DocumentFetchParameterContract.EuropeanUnionLegalNotice;

    /// <summary>The parameter name carrying the language selection (<c>en</c>) as provenance.</summary>
    public static string LanguageSelectionParameterName => ParameterContract.Parameters[0].ParameterName;

    /// <summary>
    /// The plan's request-target origin and path, without the query. The
    /// <see cref="MachineQueryPlan"/> target admits no query string; the rendered request carries
    /// the full pinned URI, which the binder admits for a GET whose target extends the plan's own
    /// path with a query (<c>MachineQueryValidation.IsTargetBoundToPlan</c>).
    /// </summary>
    public static string TargetOriginAndPath { get; } =
        new Uri(EuLegalNoticeEvidence.RequestedUri, UriKind.Absolute).GetLeftPart(UriPartial.Path);

    private const string LegalNoticeFamilyMemberKey = "legal-notice.fetch";
    private const string PartitionKey = "eu-legal-notice-en";
    private const string CanonicalizationIdentity = "eu-legal-notice-plan/1";

    private static readonly byte[] CanonicalIdentityBytes = Encoding.UTF8.GetBytes(string.Join(
        '\n',
        CanonicalizationIdentity,
        "requested_uri=" + EuLegalNoticeEvidence.RequestedUri,
        "language_selection=" + EuLegalNoticeEvidence.LanguageSelection));

    /// <summary>
    /// This plan's own content-addressed identity, serving as the renderer profile reference the
    /// same way <see cref="EuDocumentFetchAddress.ArtifactRef"/> does for the per-object route.
    /// </summary>
    public static SourceArtifactRef ArtifactRef { get; } = new(
        "urn:uuid:70712480-66ee-46c8-8a03-2fb651ce9a45",
        Sha256(CanonicalIdentityBytes));

    private readonly SourceRegistryMemberRef _familyRef = new(ArtifactRef, LegalNoticeFamilyMemberKey);

    public static byte[] CopyCanonicalIdentityBytes() => CanonicalIdentityBytes.ToArray();

    /// <summary>The only path that mints the bound legal-notice GET.</summary>
    /// <param name="machinePlanResourceId">A fresh resource id for the minted machine-query plan.</param>
    /// <param name="inputResourceId">A fresh resource id for the minted ordered-parameter input.</param>
    /// <param name="rendererSource">
    /// The renderer-source artifact naming this file's own <see cref="EuLegalNoticeRenderer"/>
    /// code, held with its bytes exactly as every other Europe bind requires.
    /// </param>
    public EuDocumentFetchBoundQuery Bind(
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        ArgumentException.ThrowIfNullOrEmpty(machinePlanResourceId);
        ArgumentException.ThrowIfNullOrEmpty(inputResourceId);
        ArgumentNullException.ThrowIfNull(rendererSource);

        var response = new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null);
        var parameters = new[]
        {
            new MachineQueryParameter(
                LanguageSelectionParameterName,
                MachineQueryParameterKind.PublisherLiteral,
                null,
                EuLegalNoticeEvidence.LanguageSelection,
                ArtifactRef),
        };

        var input = MachineQueryInputArtifact.Create(
            inputResourceId, _familyRef, PartitionKey, response, parameters);
        var renderer = new EuLegalNoticeRenderer(rendererSource);
        _ = renderer.RenderInput(input);
        var targetBytes = Encoding.ASCII.GetBytes(
            new Uri(EuLegalNoticeEvidence.RequestedUri, UriKind.Absolute).PathAndQuery);
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            _familyRef,
            ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Get,
            TargetOriginAndPath,
            targetBytes.LongLength,
            Sha256(targetBytes),
            response,
            null,
            null,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            null,
            null);
        var machinePlanRef = MachineQueryPlanIdentity.Create(machinePlanResourceId, machinePlan);
        var request = MachineQueryBinder.BindForSend(machinePlan, machinePlanRef, input, renderer);
        return new EuDocumentFetchBoundQuery(machinePlan, machinePlanRef, input, request);
    }

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

/// <summary>
/// Renders the legal-notice GET's request target. The target is the pinned constant, never built
/// from parts; the one carried parameter is read back from the reopened input and checked against
/// the constant it must carry, mirroring <see cref="EuDocumentFetchRenderer"/>'s discipline of
/// reading from the input rather than trusting a captured closure value.
/// </summary>
internal sealed class EuLegalNoticeRenderer : IMachineQueryRenderer
{
    private readonly MachineQueryRendererSource _rendererSource;

    internal EuLegalNoticeRenderer(MachineQueryRendererSource rendererSource)
    {
        _rendererSource = rendererSource ?? throw new ArgumentNullException(nameof(rendererSource));
    }

    public SourceArtifactRef RendererProfileRef => EuLegalNoticePlan.ArtifactRef;

    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;

    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => EuLegalNoticePlan.CopyCanonicalIdentityBytes();

    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();

    public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
        RenderInput(orderedParameterSet);

    internal MachineQueryRenderOutput RenderInput(MachineQueryInputArtifact input)
    {
        if (!EuLegalNoticePlan.ParameterContract.TryReadDeclaredValues(input, out var values) ||
            input.OrderedParameters[0].Kind != MachineQueryParameterKind.PublisherLiteral ||
            !string.Equals(values[0], EuLegalNoticeEvidence.LanguageSelection, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The legal-notice input does not carry the single language-selection parameter this renderer expects.",
                nameof(input));
        }

        return new MachineQueryRenderOutput(EuLegalNoticeEvidence.RequestedUri, ReadOnlySpan<byte>.Empty);
    }
}
