using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// Binds one <see cref="LuxembourgDocumentFetchAddress"/> into a real, sendable
/// <see cref="BoundMachineRequest"/> through this codebase's existing
/// <see cref="MachineQueryBinder"/>/<see cref="IMachineQueryRenderer"/> machinery, the same door
/// <c>EuDocumentFetchPlan</c> already opened for the EU document-fetch GET. D1-06c-LU-2, SCOPE_RULING
/// lex-event-20260904T173606578Z-44305cbdf86043ae9a5a502282aebcd5.
/// </summary>
/// <remarks>
/// This route negotiates nothing. Its wire request carries a User-Agent and no Accept, no
/// Accept-Language and no body: the format is decided entirely by which filestore file the address
/// names, which is why <see cref="DocumentFetchParameterContract.LuxembourgDocumentFetch"/> declares
/// no header parameter and the ruling refused a second parallel LU branch in the session.
/// <para>
/// It does declare one non-header parameter, the act's own ELI page path. That is not negotiation:
/// RULING lex-event-20260904T180444431Z-13c6f8f86ddf4f02857cf4001c202143 makes it a required third
/// robots path for every manifestation, it is store-derived (manifestation to expression to work)
/// and cannot be recovered from the filestore path, and carrying it here puts it inside the bound
/// request's own retained canonical bytes so the path robots was evaluated against is part of the
/// evidence rather than only a call argument. <see cref="MachineQueryInputArtifact.Create"/> also
/// requires at least one ordered parameter, so a literally empty declaration was never bindable.
/// </para>
/// </remarks>
public sealed class LuxembourgDocumentFetchPlan
{
    /// <summary>This route's own ordered parameter declaration, the one the session verifies against.</summary>
    public static DocumentFetchParameterContract ParameterContract =>
        DocumentFetchParameterContract.LuxembourgDocumentFetch;

    /// <summary>The parameter name carrying the act's own ELI page path.</summary>
    public static string ActEliPagePathParameterName => ParameterContract.Parameters[0].ParameterName;

    private const string DocumentFetchFamilyMemberKey = "document.fetch";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly LuxembourgDocumentFetchAddress _address;
    private readonly SourceRegistryMemberRef _documentFetchFamilyRef;

    public LuxembourgDocumentFetchPlan(LuxembourgDocumentFetchAddress address)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _documentFetchFamilyRef = new SourceRegistryMemberRef(
            address.ArtifactRef, DocumentFetchFamilyMemberKey);
    }

    public LuxembourgDocumentFetchAddress Address => _address;

    /// <summary>The only path that mints a bound GET request for this address.</summary>
    public LuxembourgDocumentFetchBoundQuery Bind(
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        ArgumentException.ThrowIfNullOrEmpty(machinePlanResourceId);
        ArgumentException.ThrowIfNullOrEmpty(inputResourceId);
        ArgumentNullException.ThrowIfNull(rendererSource);

        var response = new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody, null, null, null);
        var parameters = new[]
        {
            new MachineQueryParameter(
                ActEliPagePathParameterName,
                MachineQueryParameterKind.PublisherLiteral,
                null,
                _address.ActEliPagePath,
                _address.ArtifactRef),
        };

        var input = MachineQueryInputArtifact.Create(
            inputResourceId, _documentFetchFamilyRef, PartitionKey(), response, parameters);
        // No pre-render here. MachineQueryBinder.BindForSend renders through this same renderer
        // and validates the result, so calling RenderInput first and discarding it was a step that
        // could not fail independently of the bind that follows it.
        var renderer = new LuxembourgDocumentFetchRenderer(_address, rendererSource);
        var resourceUri = _address.FetchUri.AbsoluteUri;
        var targetBytes = Encoding.ASCII.GetBytes(_address.FetchUri.PathAndQuery);
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            _documentFetchFamilyRef,
            _address.ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Get,
            resourceUri,
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
        return new LuxembourgDocumentFetchBoundQuery(machinePlan, machinePlanRef, input, request);
    }

    private string PartitionKey() =>
        "lu-document-fetch-" + Convert.ToHexString(SHA256.HashData(
                StrictUtf8.GetBytes(_address.FetchUri.AbsoluteUri + "\n" + _address.ActEliPagePath)))
            .ToLowerInvariant()[..24];

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

/// <summary>The bound Luxembourg document-fetch GET and every artifact that feeds it, for one request.</summary>
public sealed record LuxembourgDocumentFetchBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Renders one Luxembourg document-fetch GET's request target from a bound
/// <see cref="MachineQueryInputArtifact"/>. The target is the address's own fetch URI, never rebuilt
/// from parts at render time, and the one carried parameter is read back and checked against the
/// address this renderer was bound to rather than trusted from a captured closure value.
/// </summary>
internal sealed class LuxembourgDocumentFetchRenderer : IMachineQueryRenderer
{
    private readonly LuxembourgDocumentFetchAddress _address;
    private readonly MachineQueryRendererSource _rendererSource;

    internal LuxembourgDocumentFetchRenderer(
        LuxembourgDocumentFetchAddress address,
        MachineQueryRendererSource rendererSource)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _rendererSource = rendererSource ?? throw new ArgumentNullException(nameof(rendererSource));
        RendererProfileRef = address.ArtifactRef;
    }

    public SourceArtifactRef RendererProfileRef { get; }

    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;

    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _address.CopyCanonicalIdentityBytes();

    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();

    public MachineQueryRenderOutput Render(
        MachineQueryPlan plan,
        MachineQueryInputArtifact orderedParameterSet) => RenderInput(orderedParameterSet);

    internal MachineQueryRenderOutput RenderInput(MachineQueryInputArtifact input)
    {
        if (!LuxembourgDocumentFetchPlan.ParameterContract.TryReadDeclaredValues(input, out var values) ||
            !string.Equals(values[0], _address.ActEliPagePath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The document-fetch input does not carry the act ELI page path this renderer expects.",
                nameof(input));
        }

        return new MachineQueryRenderOutput(_address.FetchUri.AbsoluteUri, ReadOnlySpan<byte>.Empty);
    }
}
