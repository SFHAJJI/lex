using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// The EU structural counterpart of <c>Lex.V3.Contracts.Source.Luxembourg.LuxembourgDeliveryObservation</c>,
/// <c>LuxembourgDeliveryPass</c> and <c>LuxembourgDeliveryEvidenceSet</c>, kept in
/// <c>Lex.V3.Ingest.Europe</c> rather than a mirrored <c>Source/Europe</c> Contracts file because
/// D1-05c-2's own path claim is Ingest Europe plus tests: nothing here is new Contracts-layer policy,
/// it is the same publisher-neutral Core wiring (<see cref="EnumerationDeliveryComparison"/>,
/// <see cref="RepeatedEnumerationDeliveryReceipt"/>, <see cref="RepeatedEnumerationObservationCustody"/>)
/// LU's own Contracts-layer counterpart already performs, reassembled at the Ingest layer for EU.
/// </summary>
/// <remarks>
/// The one genuine design fork from the LU shape: LU's <c>MaterializeAsync</c> rebuilds a fresh
/// <c>LuxembourgSparqlRenderer</c> from a digest-bound invariant plan and template, an independent
/// re-derivation that can genuinely fail. The two EU SPARQL renderer types
/// (<c>EuConsolidationSparqlRenderer</c>, <c>EuObjectFactsSparqlRenderer</c>) are <c>internal</c> to
/// <c>Lex.V3.Contracts</c> and this path claim does not extend there, so this file cannot construct
/// either one -- and would not want to duplicate their private SPARQL-template logic here even if it
/// could, which would be exactly the "second copy of one invariant" this codebase avoids elsewhere.
/// Instead, each observation captures the exact rendered request target and body
/// <see cref="MachineQueryBinder.OpenForSend"/> already produces from the real renderer closed over
/// inside the opaque <see cref="BoundMachineRequest"/> capability at bind time (a public door, and the
/// same one <c>LuxembourgDeliveryObservation.Create</c> already calls), and replays those exact bytes
/// through a small internal <see cref="IMachineQueryRenderer"/> at resolve time. This is not a weaker
/// check than LU's: <see cref="MachineQueryBinder"/>'s own <c>ValidateAndRender</c> (reached through
/// <c>EnumerationDeliveryComparison.Resolve</c>) still independently verifies the replayed target and
/// body hash to the reopened plan's own <c>ExpectedRequestTargetSha256</c>/<c>ExpectedRequestBodySha256</c>
/// and that the replaying renderer's own <c>RendererProfileRef</c>/<c>RendererSourceRef</c> equal the
/// reopened plan's -- both are true here because the captured bytes and both refs are read from the
/// same successful bind this run actually performed, never invented. What this design does not
/// reproduce is LU's own *independent* re-derivation from a digest-bound template (a second proof
/// that a fresh render matches, not merely that the recorded one is self-consistent); closing that gap
/// without widening this path claim into Contracts is future work, not this slice's.
/// </remarks>
internal sealed class EuDeliveryObservation
{
    private readonly byte[] _requestBody;

    private EuDeliveryObservation(
        RepeatedEnumerationEvidenceRefs references,
        SourceArtifactRef httpEvidenceRef,
        SourceArtifactRef runIdentity,
        ulong requestOrdinal,
        string responseBodySha256,
        CustodyMembership responseBodyMembership,
        string durableWriteReceiptSha256,
        string requestedUri,
        byte[] requestBody)
    {
        References = references;
        HttpEvidenceRef = httpEvidenceRef;
        RunIdentity = runIdentity;
        RequestOrdinal = requestOrdinal;
        ResponseBodySha256 = responseBodySha256;
        ResponseBodyMembership = responseBodyMembership;
        DurableWriteReceiptSha256 = durableWriteReceiptSha256;
        RequestedUri = requestedUri;
        _requestBody = requestBody;
    }

    public RepeatedEnumerationEvidenceRefs References { get; }

    public SourceArtifactRef HttpEvidenceRef { get; }

    public SourceArtifactRef RunIdentity { get; }

    public ulong RequestOrdinal { get; }

    public string ResponseBodySha256 { get; }

    public CustodyMembership ResponseBodyMembership { get; }

    public string DurableWriteReceiptSha256 { get; }

    /// <summary>The exact rendered request target this observation's bind produced. See type remarks.</summary>
    public string RequestedUri { get; }

    /// <summary>The exact rendered request body bytes this observation's bind produced.</summary>
    public byte[] CopyRequestBody() => _requestBody.ToArray();

    public RepeatedEnumerationObservationCustody Custody => new(
        References, ResponseBodySha256, ResponseBodyMembership, DurableWriteReceiptSha256);

    /// <summary>
    /// Admits one observation. Mirrors <c>LuxembourgDeliveryObservation.Create</c>'s own binding
    /// checks exactly: every check there reads only publisher-neutral Core/Http types, so nothing
    /// about it is Luxembourg-specific and nothing here needed to change beyond the parameter shape
    /// (an EU bound query carries its machine-plan and input refs as two separate values rather than
    /// one publisher-specific wrapper type).
    /// </summary>
    public static EuDeliveryObservation ForRequest(
        SourceArtifactRef machinePlanRef,
        SourceArtifactRef inputRef,
        BoundMachineRequest request,
        RepeatedEnumerationObservationIdentity identity,
        RepeatedEnumerationObservedTransport transport,
        RepeatedEnumerationInterpretationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(machinePlanRef);
        ArgumentNullException.ThrowIfNull(inputRef);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(profile);

        if (transport.HttpEvidence.Hops.Count != 1)
        {
            throw new ArgumentException("A delivery observation admits exactly one HTTP hop.", nameof(transport));
        }

        var terminal = transport.HttpEvidence.Hops[0];
        if (terminal.Status != 200 || terminal.StatusDisposition != HttpStatusDisposition.DerivableStatus)
        {
            throw new ArgumentException("A delivery observation admits only a terminal derivable 200.", nameof(transport));
        }

        if (terminal.Headers.ContentType is not RoutedHttpSingleHeader contentType ||
            !string.Equals(contentType.Value, profile.ExpectedMediaType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A delivery observation's media type must equal the profile's expected media type exactly.",
                nameof(transport));
        }

        var logicalRequestBytes = transport.LogicalRequest.CopyCanonicalBytes();
        var logicalRequestSha256 = Sha256(logicalRequestBytes);
        if (terminal.LogicalRequestSha256 != logicalRequestSha256)
        {
            throw new ArgumentException(
                "The transport's logical request does not bind the hop it is bundled with.", nameof(transport));
        }

        var payloadSha256 = Sha256(transport.RetainedPayloadBytes.Span);
        if (terminal.Sha256 != payloadSha256)
        {
            throw new ArgumentException(
                "The transport's retained payload does not bind the hop it is bundled with.", nameof(transport));
        }

        var writeReceiptBytes = new UTF8Encoding(false, true).GetBytes(ContractJson.Serialize(transport.DurableWriteReceipt));
        if (terminal.DurableWriteReceiptSha256 != Sha256(writeReceiptBytes))
        {
            throw new ArgumentException(
                "The transport's write receipt does not bind the hop it is bundled with.", nameof(transport));
        }

        if (!string.Equals(
                transport.DurableWriteReceipt.Reference.ContentSha256, terminal.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The transport's write receipt is a receipt for different bytes than the retained body.",
                nameof(transport));
        }

        var opened = MachineQueryBinder.OpenForSend(request);

        var logicalRequestRef = new SourceArtifactRef(identity.LogicalRequestResourceId, logicalRequestSha256);
        var httpEvidenceRef = new SourceArtifactRef(
            identity.HttpEvidenceResourceId, Sha256(transport.HttpEvidence.CopyCanonicalBytes()));

        var references = new RepeatedEnumerationEvidenceRefs(
            machinePlanRef, inputRef, opened.RenderReceiptRef, logicalRequestRef, httpEvidenceRef);

        return new EuDeliveryObservation(
            references,
            httpEvidenceRef,
            transport.HttpEvidence.RunIdentity,
            transport.HttpEvidence.RequestOrdinal,
            terminal.Sha256,
            CustodyMembershipClassifier.Classify(transport.DurableWriteReceipt),
            terminal.DurableWriteReceiptSha256,
            opened.RequestedUri,
            opened.CopyRequestBody());
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}

/// <summary>
/// One pass, folded. Structurally identical to <c>LuxembourgDeliveryPass</c>: a page cannot precede
/// its count, and page ordinals are assigned by the fold.
/// </summary>
internal sealed class EuDeliveryPass
{
    private readonly IReadOnlyList<EuDeliveryObservation> _pages;

    private EuDeliveryPass(EuDeliveryObservation count, long selectedRowCount, IReadOnlyList<EuDeliveryObservation> pages)
    {
        Count = count;
        SelectedRowCount = selectedRowCount;
        _pages = pages;
    }

    public static EuDeliveryPass BeginWithCount(EuDeliveryObservation count, long selectedRowCount)
    {
        ArgumentNullException.ThrowIfNull(count);
        if (selectedRowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedRowCount));
        }

        return new EuDeliveryPass(count, selectedRowCount, []);
    }

    public EuDeliveryPass WithPage(EuDeliveryObservation page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var next = new List<EuDeliveryObservation>(_pages.Count + 1);
        next.AddRange(_pages);
        next.Add(page);
        return new EuDeliveryPass(Count, SelectedRowCount, next);
    }

    public EuDeliveryObservation Count { get; }

    public long SelectedRowCount { get; }

    public IReadOnlyList<EuDeliveryObservation> Pages => _pages;

    public EnumerationPageSetRefs PageRefs => new(Array.AsReadOnly(
        _pages.Select(static (page, ordinal) => new RepeatedEnumerationPageRef(ordinal, page.References)).ToArray()));

    internal IEnumerable<EuDeliveryObservation> AllObservations() => new[] { Count }.Concat(_pages);
}

/// <summary>
/// The materialized resolver, and the only door to a Core comparison for the EU executor. See the
/// type remarks on <see cref="EuDeliveryObservation"/> for the one design fork from LU's own
/// counterpart (the replay renderer).
/// </summary>
internal sealed class EuDeliveryEvidenceSet : IRepeatedEnumerationEvidenceResolver
{
    private readonly RepeatedEnumerationInterpretationProfile _profile;
    private readonly SourceArtifactRef _profileRef;
    private readonly EuDeliveryPass _passA;
    private readonly EuDeliveryPass _passB;
    private readonly IReadOnlyDictionary<string, RepeatedEnumerationResolvedEvidence> _resolved;

    private EuDeliveryEvidenceSet(
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef profileRef,
        EuDeliveryPass passA,
        EuDeliveryPass passB,
        IReadOnlyDictionary<string, RepeatedEnumerationResolvedEvidence> resolved)
    {
        _profile = profile;
        _profileRef = profileRef;
        _passA = passA;
        _passB = passB;
        _resolved = resolved;
    }

    /// <summary>
    /// Core's own message when the most recent <see cref="TryCompareAndReceipt"/> call refused with
    /// <see cref="RepeatedEnumerationReceiptRefusal.DeliveryComparisonRefused"/>. Null otherwise.
    /// </summary>
    public string? LastCoreRefusalMessage { get; private set; }

    public static async Task<EuDeliveryEvidenceSet> MaterializeAsync(
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef profileRef,
        EuDeliveryPass passA,
        EuDeliveryPass passB,
        ICustodyStore custodyStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profileRef);
        ArgumentNullException.ThrowIfNull(passA);
        ArgumentNullException.ThrowIfNull(passB);
        ArgumentNullException.ThrowIfNull(custodyStore);

        var resolved = new Dictionary<string, RepeatedEnumerationResolvedEvidence>(StringComparer.Ordinal);
        foreach (var observation in passA.AllObservations().Concat(passB.AllObservations()))
        {
            resolved[observation.HttpEvidenceRef.Sha256] = await ResolveOneAsync(observation, custodyStore, cancellationToken)
                .ConfigureAwait(false);
        }

        return new EuDeliveryEvidenceSet(profile, profileRef, passA, passB, resolved);
    }

    private static async Task<RepeatedEnumerationResolvedEvidence> ResolveOneAsync(
        EuDeliveryObservation observation, ICustodyStore custodyStore, CancellationToken cancellationToken)
    {
        var refs = observation.References;

        var planBytes = await CustodyRestore.ReadByDigestCheckedAsync(custodyStore, refs.QueryPlanRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var queryPlan = RepeatedEnumerationDeliveryReopenGlue.DecodeCanonical<MachineQueryPlan>(
            planBytes.Span, MachineQueryPlanIdentity.CanonicalizationIdentity, "the machine query plan");
        try
        {
            MachineQueryPlanIdentity.Validate(refs.QueryPlanRef, queryPlan);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained machine query plan does not reproduce its own canonical bytes.", exception);
        }

        var inputBytes = await CustodyRestore.ReadByDigestCheckedAsync(custodyStore, refs.QueryInputRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        MachineQueryInputArtifact queryInput;
        try
        {
            queryInput = MachineQueryInputArtifact.ParseAndVerify(refs.QueryInputRef, inputBytes.Span);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained machine query input does not bind its reference.", exception);
        }

        var receiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(custodyStore, refs.RenderReceiptRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var renderReceipt = RepeatedEnumerationDeliveryReopenGlue.DecodeCanonical<MachineQueryRenderReceipt>(
            receiptBytes.Span, MachineQueryRenderReceiptIdentity.CanonicalizationIdentity, "the machine query render receipt");
        try
        {
            MachineQueryRenderReceiptIdentity.Validate(refs.RenderReceiptRef, renderReceipt);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained render receipt does not reproduce its own canonical bytes.", exception);
        }

        var logicalRequestBytes = await CustodyRestore.ReadByDigestCheckedAsync(custodyStore, refs.LogicalRequestRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        HttpLogicalRequest logicalRequest;
        try
        {
            logicalRequest = HttpLogicalRequest.ParseAndVerify(logicalRequestBytes.Span);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained logical request does not parse as its exact canonical form.", exception);
        }

        var httpEvidenceBytes = await CustodyRestore.ReadByDigestCheckedAsync(custodyStore, refs.HttpEvidenceRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        RoutedHttpEvidence httpEvidence;
        try
        {
            httpEvidence = RoutedHttpEvidence.ParseAndVerify(httpEvidenceBytes.Span);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained HTTP evidence does not parse as its exact canonical form.", exception);
        }

        if (httpEvidence.Hops.Count != 1)
        {
            throw new CustodyIntegrityException("The retained HTTP evidence no longer names exactly one hop.");
        }

        var terminal = httpEvidence.Hops[0];
        var writeReceiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(custodyStore, terminal.DurableWriteReceiptSha256, cancellationToken)
            .ConfigureAwait(false);
        var writeReceipt = DeserializeChecked<DurableBlobWriteReceipt>(writeReceiptBytes.Span, "the durable write receipt");

        var payload = await CustodyRestore.ReadByDigestCheckedAsync(custodyStore, terminal.Sha256, cancellationToken)
            .ConfigureAwait(false);

        var renderer = new EuReplayRenderer(
            queryPlan.RendererProfileRef, queryPlan.RendererSourceRef, observation.RequestedUri, observation.CopyRequestBody());

        return new RepeatedEnumerationResolvedEvidence(
            queryPlan, queryInput, renderReceipt, renderer, logicalRequest, httpEvidence, writeReceipt, payload);
    }

    private static T DeserializeChecked<T>(ReadOnlySpan<byte> bytes, string what)
    {
        try
        {
            var json = new UTF8Encoding(false, true).GetString(bytes);
            return ContractJson.Deserialize<T>(json)
                ?? throw new CustodyIntegrityException($"The retained bytes decoded to no {what}.");
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException($"The retained bytes are not {what}.", exception);
        }
    }

    public RepeatedEnumerationResolvedEvidence Resolve(RepeatedEnumerationEvidenceRefs references)
    {
        ArgumentNullException.ThrowIfNull(references);
        return _resolved.TryGetValue(references.HttpEvidenceRef.Sha256, out var value)
            ? value
            : throw new ArgumentException(
                "This evidence set was not materialized for the given references.", nameof(references));
    }

    public RepeatedEnumerationDeliveryReceipt? TryCompareAndReceipt(
        IReadOnlyDictionary<string, CustodyMembership> sessionArtifactMembership,
        IReadOnlyDictionary<string, CustodyMembership> executorWrittenMembership,
        out RepeatedEnumerationReceiptRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(sessionArtifactMembership);
        ArgumentNullException.ThrowIfNull(executorWrittenMembership);

        LastCoreRefusalMessage = null;
        EnumerationDeliveryComparison delivery;
        try
        {
            delivery = EnumerationDeliveryComparison.Create(
                _profile,
                _profileRef,
                _passA.Count.References,
                _passA.PageRefs,
                _passB.Count.References,
                _passB.PageRefs,
                this);
        }
        catch (ArgumentException exception)
        {
            LastCoreRefusalMessage = exception.Message;
            refusal = RepeatedEnumerationReceiptRefusal.DeliveryComparisonRefused;
            return null;
        }

        return RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery,
            sessionArtifactMembership,
            executorWrittenMembership,
            _passA.AllObservations().Concat(_passB.AllObservations()).Select(static observation => observation.Custody).ToArray(),
            out refusal);
    }
}

/// <summary>
/// Replays the exact request target and body a real EU renderer already produced at bind time
/// (captured through the public <see cref="MachineQueryBinder.OpenForSend"/> door), rather than
/// re-deriving SPARQL text from a template this path claim cannot construct. See the type remarks on
/// <see cref="EuDeliveryObservation"/> for why this is a faithful replay rather than a bypass:
/// <see cref="MachineQueryBinder"/>'s own <c>ValidateAndRender</c> still independently checks the
/// replayed bytes against the reopened plan's own expected target and body digests.
/// </summary>
internal sealed class EuReplayRenderer(
    SourceArtifactRef rendererProfileRef,
    SourceArtifactRef rendererSourceRef,
    string requestedUri,
    byte[] requestBody) : IMachineQueryRenderer
{
    public SourceArtifactRef RendererProfileRef { get; } = rendererProfileRef;

    public SourceArtifactRef RendererSourceRef { get; } = rendererSourceRef;

    public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
        new(requestedUri, requestBody);
}
