using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Binds one <see cref="EuDocumentFetchAddress"/> into a real, sendable
/// <see cref="BoundMachineRequest"/> through this codebase's existing
/// <see cref="MachineQueryBinder"/>/<see cref="IMachineQueryRenderer"/> send machinery -- the same
/// door <see cref="EuWatermarkWitnessPlan.TryBindPage"/> already opened for the SPARQL witness
/// family (SCOPE_RULING lex-event-20260904T092316893Z-6d969a2ba7934aa995907a55914bf3b6), used here
/// for a GET rather than a POST.
/// </summary>
/// <remarks>
/// D1-06c-EU (SCOPE_RULING lex-event-20260904T104723233Z-fa84c4edb4144467a2a63c94ee469cef). The
/// <c>Accept</c> and <c>Accept-Language</c> headers this GET is sent under are not part of the
/// wire request target (they are headers, not path text), so they cannot be recovered from the
/// requested URI alone the way <see cref="OfficialMachineQuerySourceProfile.ResolveFor"/> resolves
/// the SPARQL channels. They are instead carried as two <see cref="MachineQueryParameter"/> entries
/// on the bound input artifact -- exactly the same door <see cref="EuWatermarkWitnessPlan.TryBindPage"/>
/// already uses to carry its own boundary cursor -- so <c>RoutedHttpAcquisitionSession</c> can read
/// them back from the reopened input bytes when it builds the outbound headers, without needing a
/// renderer reference (which it does not otherwise have) or a schema change to the shared
/// <see cref="MachineQueryRenderReceipt"/> (which every other publisher's channel also uses).
/// </remarks>
public sealed class EuDocumentFetchPlan
{
    /// <summary>The parameter name carrying the exact <c>Accept</c> header value.</summary>
    public const string AcceptParameterName = "eu_document_fetch_accept";

    /// <summary>The parameter name carrying the exact <c>Accept-Language</c> header value.</summary>
    public const string AcceptLanguageParameterName = "eu_document_fetch_accept_language";

    private const string DocumentFetchFamilyMemberKey = "document.fetch";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly EuDocumentFetchAddress _address;
    private readonly SourceRegistryMemberRef _documentFetchFamilyRef;

    public EuDocumentFetchPlan(EuDocumentFetchAddress address)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _documentFetchFamilyRef = new SourceRegistryMemberRef(address.ArtifactRef, DocumentFetchFamilyMemberKey);
    }

    public EuDocumentFetchAddress Address => _address;

    /// <summary>
    /// The only path that mints a bound GET request for this address.
    /// </summary>
    /// <param name="machinePlanResourceId">A fresh resource id for the minted machine-query plan.</param>
    /// <param name="inputResourceId">A fresh resource id for the minted ordered-parameter input.</param>
    /// <param name="rendererSource">
    /// The renderer-source artifact naming this file's own <see cref="EuDocumentFetchRenderer"/> code,
    /// held with its bytes exactly as every other Europe bind already requires.
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
                AcceptParameterName,
                MachineQueryParameterKind.PublisherLiteral,
                null,
                _address.Accept,
                _address.ArtifactRef),
            new MachineQueryParameter(
                AcceptLanguageParameterName,
                MachineQueryParameterKind.PublisherLiteral,
                null,
                _address.AcceptLanguage,
                _address.ArtifactRef),
        };

        var input = MachineQueryInputArtifact.Create(
            inputResourceId, _documentFetchFamilyRef, PartitionKey(), response, parameters);
        var renderer = new EuDocumentFetchRenderer(_address, rendererSource);
        var rendered = renderer.RenderInput(input);
        var targetBytes = Encoding.ASCII.GetBytes(new Uri(_address.ResourceUri, UriKind.Absolute).PathAndQuery);
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            _documentFetchFamilyRef,
            _address.ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Get,
            _address.ResourceUri,
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

    private string PartitionKey() =>
        "eu-document-fetch-" + Convert.ToHexString(SHA256.HashData(
                StrictUtf8.GetBytes(_address.ResourceUri + "\n" + _address.Accept + "\n" + _address.AcceptLanguage)))
            .ToLowerInvariant()[..24];

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

/// <summary>The bound document-fetch GET query and every artifact that feeds it, for one request.</summary>
public sealed record EuDocumentFetchBoundQuery(
    MachineQueryPlan MachinePlan,
    SourceArtifactRef MachinePlanRef,
    MachineQueryInputArtifact InputArtifact,
    BoundMachineRequest Request);

/// <summary>
/// Renders one document-fetch GET's request target from a bound <see cref="MachineQueryInputArtifact"/>.
/// The target is fixed (the address's own <see cref="EuDocumentFetchAddress.ResourceUri"/>, never
/// built from parts at render time); the two carried parameters are read back and checked against
/// the address this renderer was bound to, mirroring <c>EuWatermarkWitnessSparqlRenderer</c>'s own
/// discipline of reading from the reopened input rather than trusting a captured closure value.
/// </summary>
internal sealed class EuDocumentFetchRenderer : IMachineQueryRenderer
{
    private readonly EuDocumentFetchAddress _address;
    private readonly MachineQueryRendererSource _rendererSource;

    internal EuDocumentFetchRenderer(EuDocumentFetchAddress address, MachineQueryRendererSource rendererSource)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _rendererSource = rendererSource ?? throw new ArgumentNullException(nameof(rendererSource));
        RendererProfileRef = address.ArtifactRef;
    }

    public SourceArtifactRef RendererProfileRef { get; }

    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;

    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _address.CopyCanonicalIdentityBytes();

    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();

    public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
        RenderInput(orderedParameterSet);

    internal MachineQueryRenderOutput RenderInput(MachineQueryInputArtifact input)
    {
        if (input.OrderedParameters.Count != 2)
        {
            throw new ArgumentException(
                "A document-fetch input carries exactly the accept and accept-language parameters.",
                nameof(input));
        }

        var accept = input.OrderedParameters[0];
        var acceptLanguage = input.OrderedParameters[1];
        if (!string.Equals(accept.Name, EuDocumentFetchPlan.AcceptParameterName, StringComparison.Ordinal) ||
            accept.Kind != MachineQueryParameterKind.PublisherLiteral ||
            !string.Equals(accept.TextValue, _address.Accept, StringComparison.Ordinal) ||
            !string.Equals(
                acceptLanguage.Name,
                EuDocumentFetchPlan.AcceptLanguageParameterName,
                StringComparison.Ordinal) ||
            acceptLanguage.Kind != MachineQueryParameterKind.PublisherLiteral ||
            !string.Equals(acceptLanguage.TextValue, _address.AcceptLanguage, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The document-fetch input does not carry the accept/accept-language pair this renderer expects.",
                nameof(input));
        }

        return new MachineQueryRenderOutput(_address.ResourceUri, ReadOnlySpan<byte>.Empty);
    }
}
