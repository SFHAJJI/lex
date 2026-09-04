using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Luxembourg;

// Queue item 19: the observation-identity, observed-transport and observation-custody types
// formerly declared here (LuxembourgObservationIdentity, LuxembourgObservedTransport,
// LuxembourgObservationCustody) never named Luxembourg or any other publisher in their own fields
// or logic, so they moved to Lex.V3.Contracts.Source.Core as RepeatedEnumerationObservationIdentity,
// RepeatedEnumerationObservedTransport and RepeatedEnumerationObservationCustody respectively, for a
// future EU executor to reuse instead of duplicating. This file keeps only what is genuinely
// Luxembourg-specific: the binding from LuxembourgBoundQueryCount/LuxembourgBoundQueryPage to those
// neutral shapes.

/// <summary>
/// One admitted observation. It cannot exist without both the bound request and the routed
/// evidence for it: the evidence is a constructor parameter, never a later check.
/// </summary>
public sealed class LuxembourgDeliveryObservation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private LuxembourgDeliveryObservation(
        RepeatedEnumerationEvidenceRefs references,
        SourceArtifactRef httpEvidenceRef,
        SourceArtifactRef runIdentity,
        ulong requestOrdinal,
        IReadOnlyList<string> sessionRetainedDigests,
        string responseBodySha256,
        CustodyMembership responseBodyMembership,
        string durableWriteReceiptSha256)
    {
        References = references;
        HttpEvidenceRef = httpEvidenceRef;
        RunIdentity = runIdentity;
        RequestOrdinal = requestOrdinal;
        SessionRetainedDigests = sessionRetainedDigests;
        ResponseBodySha256 = responseBodySha256;
        ResponseBodyMembership = responseBodyMembership;
        DurableWriteReceiptSha256 = durableWriteReceiptSha256;
    }

    /// <summary>The refs Core will resolve. Minted here; never hand-built by a caller.</summary>
    public RepeatedEnumerationEvidenceRefs References { get; }

    public SourceArtifactRef HttpEvidenceRef { get; }

    public SourceArtifactRef RunIdentity { get; }

    public ulong RequestOrdinal { get; }

    /// <summary>The four digests the acquisition session retained for this observation.</summary>
    public IReadOnlyList<string> SessionRetainedDigests { get; }

    /// <summary>
    /// The terminal response body's own digest. The session wrote these bytes and holds a receipt
    /// for them, so this is a retained member of the run like any other, not a bare read.
    /// </summary>
    public string ResponseBodySha256 { get; }

    /// <summary>
    /// The membership of <see cref="ResponseBodySha256"/>, classified from the write receipt the
    /// store issued for those exact bytes rather than asserted. That receipt is bound to this body
    /// twice before it is trusted: by digest, because the hop names it, and by content, because
    /// <c>Create</c> refuses a receipt whose <c>Reference.ContentSha256</c> is not this body's.
    /// </summary>
    public CustodyMembership ResponseBodyMembership { get; }

    /// <summary>
    /// The digest of the body's durable write receipt. The session retains this too (through
    /// <c>RoutedHttpAcquisitionSession.RetainArtifactAsync</c>), so its membership comes from the
    /// session's own map beside the other four.
    /// </summary>
    public string DurableWriteReceiptSha256 { get; }

    /// <summary>This observation's contribution to the run's custody, for the receipt to require.</summary>
    public RepeatedEnumerationObservationCustody Custody => new(
        References, ResponseBodySha256, ResponseBodyMembership, DurableWriteReceiptSha256);

    public static LuxembourgDeliveryObservation ForCount(
        LuxembourgBoundQueryCount bound,
        RepeatedEnumerationObservationIdentity identity,
        RepeatedEnumerationObservedTransport transport,
        RepeatedEnumerationInterpretationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(bound);
        return Create(bound.MachinePlanRef, bound.InputArtifact.ArtifactRef, bound.Request, identity, transport, profile);
    }

    public static LuxembourgDeliveryObservation ForPage(
        LuxembourgBoundQueryPage bound,
        RepeatedEnumerationObservationIdentity identity,
        RepeatedEnumerationObservedTransport transport,
        RepeatedEnumerationInterpretationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(bound);
        return Create(bound.MachinePlanRef, bound.InputArtifact.ArtifactRef, bound.Request, identity, transport, profile);
    }

    private static LuxembourgDeliveryObservation Create(
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
            throw new ArgumentException(
                "A delivery observation admits exactly one HTTP hop.",
                nameof(transport));
        }

        var terminal = transport.HttpEvidence.Hops[0];
        if (terminal.Status != 200 ||
            terminal.StatusDisposition != HttpStatusDisposition.DerivableStatus)
        {
            throw new ArgumentException(
                "A delivery observation admits only a terminal derivable 200.",
                nameof(transport));
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
                "The transport's logical request does not bind the hop it is bundled with.",
                nameof(transport));
        }

        var payloadSha256 = Sha256(transport.RetainedPayloadBytes.Span);
        if (terminal.Sha256 != payloadSha256)
        {
            throw new ArgumentException(
                "The transport's retained payload does not bind the hop it is bundled with.",
                nameof(transport));
        }

        var writeReceiptBytes = StrictUtf8.GetBytes(ContractJson.Serialize(transport.DurableWriteReceipt));
        if (terminal.DurableWriteReceiptSha256 != Sha256(writeReceiptBytes))
        {
            throw new ArgumentException(
                "The transport's write receipt does not bind the hop it is bundled with.",
                nameof(transport));
        }

        // Load-bearing, and not implied by the digest check above. That one proves these bytes are
        // the receipt the hop names; this one proves the receipt is ABOUT this body. Without it the
        // body's custody membership below would be read off a receipt for some other object, which
        // is exactly the substitution a floor claim must not admit.
        if (!string.Equals(
                transport.DurableWriteReceipt.Reference.ContentSha256,
                terminal.Sha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The transport's write receipt is a receipt for different bytes than the retained body.",
                nameof(transport));
        }

        // The binder mints the render receipt's resource id internally at bind time; OpenForSend
        // is the only public door that echoes it back rather than minting a second, different id.
        var opened = MachineQueryBinder.OpenForSend(request);

        var logicalRequestRef = new SourceArtifactRef(identity.LogicalRequestResourceId, logicalRequestSha256);
        var httpEvidenceRef = new SourceArtifactRef(
            identity.HttpEvidenceResourceId,
            Sha256(transport.HttpEvidence.CopyCanonicalBytes()));

        var references = new RepeatedEnumerationEvidenceRefs(
            machinePlanRef,
            inputRef,
            opened.RenderReceiptRef,
            logicalRequestRef,
            httpEvidenceRef);

        var sessionRetainedDigests = new[]
        {
            machinePlanRef.Sha256,
            inputRef.Sha256,
            opened.RenderReceiptRef.Sha256,
            logicalRequestRef.Sha256,
        };
        return new LuxembourgDeliveryObservation(
            references,
            httpEvidenceRef,
            transport.HttpEvidence.RunIdentity,
            transport.HttpEvidence.RequestOrdinal,
            Array.AsReadOnly(sessionRetainedDigests),
            terminal.Sha256,
            CustodyMembershipClassifier.Classify(transport.DurableWriteReceipt),
            terminal.DurableWriteReceiptSha256);
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}

/// <summary>
/// One pass, folded. A page cannot precede its count, and page ordinals are assigned by the fold,
/// so Core's contiguity rule holds by construction rather than by a loop index the caller supplies.
/// </summary>
public sealed class LuxembourgDeliveryPass
{
    private readonly IReadOnlyList<LuxembourgDeliveryObservation> _pages;

    private LuxembourgDeliveryPass(
        LuxembourgDeliveryObservation count,
        long selectedRowCount,
        IReadOnlyList<LuxembourgDeliveryObservation> pages)
    {
        Count = count;
        SelectedRowCount = selectedRowCount;
        _pages = pages;
    }

    public static LuxembourgDeliveryPass BeginWithCount(
        LuxembourgDeliveryObservation count,
        long selectedRowCount)
    {
        ArgumentNullException.ThrowIfNull(count);
        if (selectedRowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedRowCount));
        }

        return new LuxembourgDeliveryPass(count, selectedRowCount, []);
    }

    public LuxembourgDeliveryPass WithPage(LuxembourgDeliveryObservation page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var next = new List<LuxembourgDeliveryObservation>(_pages.Count + 1);
        next.AddRange(_pages);
        next.Add(page);
        return new LuxembourgDeliveryPass(Count, SelectedRowCount, next);
    }

    public LuxembourgDeliveryObservation Count { get; }

    public long SelectedRowCount { get; }

    public IReadOnlyList<LuxembourgDeliveryObservation> Pages => _pages;

    public EnumerationPageSetRefs PageRefs => new(Array.AsReadOnly(
        _pages.Select(static (page, ordinal) => new RepeatedEnumerationPageRef(ordinal, page.References))
            .ToArray()));

    internal IEnumerable<LuxembourgDeliveryObservation> AllObservations() =>
        new[] { Count }.Concat(_pages);
}

/// <summary>
/// The materialized resolver, and the only door to a Core comparison for Luxembourg.
/// </summary>
/// <remarks>
/// Every artifact it hands Core is read back out of custody by digest. Most are then re-parsed or
/// re-derived; the LU invariant plan is the one exception, and is digest-bound rather than
/// re-parsed (see the comment at its reopen for exactly what that does and does not establish).
/// The renderer is REBUILT from the digest-bound invariant plan and its template rather than carried
/// from the bind, so <c>MachineQueryBinder.ReproduceForEvidence</c> is an independent reproduction
/// of the request target and body digests and can genuinely fail. The two artifacts with no
/// independent re-derivation (<see cref="MachineQueryPlan"/>, <see cref="MachineQueryRenderReceipt"/>)
/// are reopened by digest and parsed; Source/Core's own <c>Resolve</c> byte-compares them against
/// their canonicalization, so the digest a caller sees is a fact the store returned rather than a
/// value compared with its own copy.
/// </remarks>
public sealed class LuxembourgDeliveryEvidenceSet : IRepeatedEnumerationEvidenceResolver
{
    private readonly RepeatedEnumerationInterpretationProfile _profile;
    private readonly SourceArtifactRef _profileRef;
    private readonly LuxembourgDeliveryPass _passA;
    private readonly LuxembourgDeliveryPass _passB;
    private readonly IReadOnlyDictionary<string, RepeatedEnumerationResolvedEvidence> _resolved;

    private LuxembourgDeliveryEvidenceSet(
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef profileRef,
        LuxembourgDeliveryPass passA,
        LuxembourgDeliveryPass passB,
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

    public static async Task<LuxembourgDeliveryEvidenceSet> MaterializeAsync(
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef profileRef,
        LuxembourgQueryPlan invariantPlan,
        string invariantPlanResourceId,
        string setId,
        MachineQueryRendererSource rendererSource,
        LuxembourgDeliveryPass passA,
        LuxembourgDeliveryPass passB,
        ICustodyStore custodyStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profileRef);
        ArgumentNullException.ThrowIfNull(invariantPlan);
        ArgumentException.ThrowIfNullOrEmpty(invariantPlanResourceId);
        ArgumentException.ThrowIfNullOrEmpty(setId);
        ArgumentNullException.ThrowIfNull(rendererSource);
        ArgumentNullException.ThrowIfNull(passA);
        ArgumentNullException.ThrowIfNull(passB);
        ArgumentNullException.ThrowIfNull(custodyStore);

        // The invariant plan itself: not part of any RepeatedEnumerationEvidenceRefs (Core has no
        // notion of an LU-specific plan), so it is reopened here, once, rather than inside Resolve.
        // The session retains it under LuxembourgQueryPlanIdentity's canonicalization (the renderer
        // hands the same bytes to the binder as its "renderer profile"), which is a different byte
        // sequence from LuxembourgQueryPlan.GetWireBytes; ParseAndVerify expects the latter, so the
        // reopened bytes are byte-compared against the given plan's own canonicalization instead of
        // being deserialized through ParseAndVerify's wire-format path.
        // Digest-bound, not re-parsed, and the difference matters. ReadByDigestCheckedAsync fails
        // unless the store returns bytes that hash to invariantPlanRef.Sha256, and that digest is
        // SHA-256 over LuxembourgQueryPlanIdentity.GetCanonicalBytes(invariantPlan) (see
        // LuxembourgQueryPlanIdentity.Create). So a successful reopen already proves custody holds
        // this exact plan's canonicalization; the in-memory object below is that same plan, bound
        // to those bytes by digest rather than deserialized out of them. A byte comparison here
        // would only restate what the digest check established, so there is not one: an assertion
        // that cannot fail is worse than none, because it reads as defense.
        var invariantPlanRef = LuxembourgQueryPlanIdentity.Create(invariantPlanResourceId, invariantPlan);
        _ = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, invariantPlanRef.Sha256, cancellationToken)
            .ConfigureAwait(false);

        var digestBoundInvariantPlan = invariantPlan;

        var definition = digestBoundInvariantPlan.SetDefinitions.SingleOrDefault(value => value.SetId == setId)
            ?? throw new ArgumentException("The set identity is not in the reopened LU plan.", nameof(setId));
        if (definition.Acquisition != LuxembourgQuerySetAcquisition.PublisherQuery || definition.TemplateId is null)
        {
            throw new ArgumentException(
                "A local materialization has no repeated-enumeration evidence to resolve.",
                nameof(setId));
        }

        var template = digestBoundInvariantPlan.QueryTemplates.Single(value => value.TemplateId == definition.TemplateId);

        // The renderer source bytes: reopened by the reference the invariant plan names, never
        // trusted from the caller's in-memory copy.
        var rendererSourceBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, rendererSource.Reference.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var reopenedRendererSource = MachineQueryRendererSource.Open(
            rendererSource.Reference, rendererSourceBytes.Span);

        var countRenderer = new LuxembourgSparqlRenderer(
            invariantPlanRef, reopenedRendererSource, digestBoundInvariantPlan, template, LuxembourgQueryRequestKind.Count);
        var pageRenderer = new LuxembourgSparqlRenderer(
            invariantPlanRef, reopenedRendererSource, digestBoundInvariantPlan, template, LuxembourgQueryRequestKind.Page);

        var resolved = new Dictionary<string, RepeatedEnumerationResolvedEvidence>(StringComparer.Ordinal);
        foreach (var observation in passA.AllObservations().Concat(passB.AllObservations()))
        {
            var isCount = observation.References.QueryInputRef == passA.Count.References.QueryInputRef ||
                observation.References.QueryInputRef == passB.Count.References.QueryInputRef;
            var renderer = isCount ? (IMachineQueryRenderer)countRenderer : pageRenderer;
            resolved[observation.HttpEvidenceRef.Sha256] = await ResolveOneAsync(
                    observation, renderer, custodyStore, cancellationToken)
                .ConfigureAwait(false);
        }

        return new LuxembourgDeliveryEvidenceSet(
            profile, profileRef, passA, passB, resolved);
    }

    private static async Task<RepeatedEnumerationResolvedEvidence> ResolveOneAsync(
        LuxembourgDeliveryObservation observation,
        IMachineQueryRenderer renderer,
        ICustodyStore custodyStore,
        CancellationToken cancellationToken)
    {
        var refs = observation.References;

        var planBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, refs.QueryPlanRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var queryPlan = DeserializeCanonical<MachineQueryPlan>(
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

        var inputBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, refs.QueryInputRef.Sha256, cancellationToken)
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

        var receiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, refs.RenderReceiptRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var renderReceipt = DeserializeCanonical<MachineQueryRenderReceipt>(
            receiptBytes.Span,
            MachineQueryRenderReceiptIdentity.CanonicalizationIdentity,
            "the machine query render receipt");
        try
        {
            MachineQueryRenderReceiptIdentity.Validate(refs.RenderReceiptRef, renderReceipt);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained render receipt does not reproduce its own canonical bytes.", exception);
        }

        var logicalRequestBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, refs.LogicalRequestRef.Sha256, cancellationToken)
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

        var httpEvidenceBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, refs.HttpEvidenceRef.Sha256, cancellationToken)
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
            throw new CustodyIntegrityException(
                "The retained HTTP evidence no longer names exactly one hop.");
        }

        var terminal = httpEvidence.Hops[0];
        var writeReceiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, terminal.DurableWriteReceiptSha256, cancellationToken)
            .ConfigureAwait(false);
        var writeReceipt = DeserializeChecked<DurableBlobWriteReceipt>(
            writeReceiptBytes.Span, "the durable write receipt");

        var payload = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, terminal.Sha256, cancellationToken)
            .ConfigureAwait(false);

        return new RepeatedEnumerationResolvedEvidence(
            queryPlan,
            queryInput,
            renderReceipt,
            renderer,
            logicalRequest,
            httpEvidence,
            writeReceipt,
            payload);
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

    /// <summary>
    /// Decodes bytes shaped by <c>ContractCanonicalizer.Canonicalize</c>: an ASCII identity line,
    /// then the canonical (sorted-key) JSON, then a trailing newline. This is what <see
    /// cref="MachineQueryPlanIdentity.GetCanonicalBytes"/> and <see
    /// cref="MachineQueryRenderReceiptIdentity.GetCanonicalBytes"/> both produce and what the
    /// session actually retains as the renderer's "profile" and its render receipt - a different
    /// byte sequence from either type's plain <c>ContractJson.Serialize</c> form, so this must not
    /// be confused with <see cref="DeserializeChecked{T}"/>.
    /// </summary>
    private static T DeserializeCanonical<T>(ReadOnlySpan<byte> bytes, string expectedIdentity, string what)
    {
        string decoded;
        try
        {
            decoded = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new CustodyIntegrityException($"The retained bytes for {what} are not valid UTF-8.", exception);
        }

        var prefix = expectedIdentity + "\n";
        if (!decoded.StartsWith(prefix, StringComparison.Ordinal) ||
            !decoded.EndsWith('\n') ||
            decoded.Length < prefix.Length + 1)
        {
            throw new CustodyIntegrityException(
                $"The retained bytes for {what} do not carry their canonicalization identity.");
        }

        var json = decoded[prefix.Length..^1];
        try
        {
            return ContractJson.Deserialize<T>(json)
                ?? throw new CustodyIntegrityException($"The retained bytes decoded to no {what}.");
        }
        catch (JsonException exception)
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
                "This evidence set was not materialized for the given references.",
                nameof(references));
    }

    /// <summary>
    /// Because this object both produced the reference lists and resolves them, no caller can pair
    /// one set's references with another set's resolver, and no caller obtains a comparison without
    /// stating the run's custody membership. There is deliberately no method returning a bare
    /// <see cref="EnumerationDeliveryComparison"/>.
    /// </summary>
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
            _passA.AllObservations().Concat(_passB.AllObservations())
                .Select(static observation => observation.Custody).ToArray(),
            out refusal);
    }
}
