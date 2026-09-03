using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The resource identities of one observation, minted together and once. No public constructor, no
/// setter, so no caller reuses or reorders them. This is what Core's <c>RequireDistinct</c>
/// (<c>RepeatedEnumerationDeliveryProof.cs:582</c>) defends; here it cannot be reached.
/// </summary>
/// <remarks>
/// There is no <c>RenderReceiptResourceId</c> here. Neither <see cref="LuxembourgQueryPlan.BindCount(string,string,string,string,LuxembourgQueryPass,LuxembourgQueryPartitionRange,MachineQueryRendererSource)"/>
/// nor <c>BindPage</c> accept a render-receipt resource id: the binder mints one internally
/// (<c>MachineQueryPlan.cs</c>, inside <c>OpenedMachineRequest</c>'s constructor) and there is no
/// parameter through which a caller can supply or influence it. The true id is recovered instead by
/// calling <see cref="MachineQueryBinder.OpenForSend"/> on the bound request, which echoes back the
/// exact <c>SourceArtifactRef</c> minted at bind time. A field that is never read is worse than no
/// field: it invites a caller to believe it does something.
/// </remarks>
public sealed class LuxembourgObservationIdentity
{
    private LuxembourgObservationIdentity(
        string machinePlanResourceId,
        string inputResourceId,
        string logicalRequestResourceId,
        string httpEvidenceResourceId)
    {
        MachinePlanResourceId = machinePlanResourceId;
        InputResourceId = inputResourceId;
        LogicalRequestResourceId = logicalRequestResourceId;
        HttpEvidenceResourceId = httpEvidenceResourceId;
    }

    public static LuxembourgObservationIdentity NewObservation() => new(
        NewUrn(), NewUrn(), NewUrn(), NewUrn());

    public string MachinePlanResourceId { get; }

    public string InputResourceId { get; }

    public string LogicalRequestResourceId { get; }

    public string HttpEvidenceResourceId { get; }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}

/// <summary>
/// The four transport facts of one observation, each already read back out of custody by the
/// executor. Nothing here is a claim: every member is re-hashed by Source/Core against the
/// reference minted beside it.
/// </summary>
public sealed record LuxembourgObservedTransport(
    HttpLogicalRequest LogicalRequest,
    RoutedHttpEvidence HttpEvidence,
    DurableBlobWriteReceipt DurableWriteReceipt,
    ReadOnlyMemory<byte> RetainedPayloadBytes);

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
        IReadOnlyList<string> readOnlyDigests)
    {
        References = references;
        HttpEvidenceRef = httpEvidenceRef;
        RunIdentity = runIdentity;
        RequestOrdinal = requestOrdinal;
        SessionRetainedDigests = sessionRetainedDigests;
        ReadOnlyDigests = readOnlyDigests;
    }

    /// <summary>The refs Core will resolve. Minted here; never hand-built by a caller.</summary>
    public RepeatedEnumerationEvidenceRefs References { get; }

    public SourceArtifactRef HttpEvidenceRef { get; }

    public SourceArtifactRef RunIdentity { get; }

    public ulong RequestOrdinal { get; }

    /// <summary>The four digests the acquisition session retained for this observation.</summary>
    public IReadOnlyList<string> SessionRetainedDigests { get; }

    /// <summary>The digests this observation reopened without writing: payload and write receipt.</summary>
    public IReadOnlyList<string> ReadOnlyDigests { get; }

    public static LuxembourgDeliveryObservation ForCount(
        LuxembourgBoundQueryCount bound,
        LuxembourgObservationIdentity identity,
        LuxembourgObservedTransport transport,
        RepeatedEnumerationInterpretationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(bound);
        return Create(bound.MachinePlanRef, bound.InputArtifact.ArtifactRef, bound.Request, identity, transport, profile);
    }

    public static LuxembourgDeliveryObservation ForPage(
        LuxembourgBoundQueryPage bound,
        LuxembourgObservationIdentity identity,
        LuxembourgObservedTransport transport,
        RepeatedEnumerationInterpretationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(bound);
        return Create(bound.MachinePlanRef, bound.InputArtifact.ArtifactRef, bound.Request, identity, transport, profile);
    }

    private static LuxembourgDeliveryObservation Create(
        SourceArtifactRef machinePlanRef,
        SourceArtifactRef inputRef,
        BoundMachineRequest request,
        LuxembourgObservationIdentity identity,
        LuxembourgObservedTransport transport,
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
        var readOnlyDigests = new[] { terminal.Sha256, terminal.DurableWriteReceiptSha256 };

        return new LuxembourgDeliveryObservation(
            references,
            httpEvidenceRef,
            transport.HttpEvidence.RunIdentity,
            transport.HttpEvidence.RequestOrdinal,
            Array.AsReadOnly(sessionRetainedDigests),
            Array.AsReadOnly(readOnlyDigests));
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
/// Every artifact it hands Core is read back out of custody by digest and re-parsed or re-derived.
/// The renderer is REBUILT from the reopened invariant plan and its template rather than carried
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
    /// <see cref="LuxembourgEnumerationReceiptRefusal.DeliveryComparisonRefused"/>. Null otherwise.
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
        var invariantPlanRef = LuxembourgQueryPlanIdentity.Create(invariantPlanResourceId, invariantPlan);
        var invariantPlanBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, invariantPlanRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        if (!invariantPlanBytes.Span.SequenceEqual(LuxembourgQueryPlanIdentity.GetCanonicalBytes(invariantPlan)))
        {
            throw new CustodyIntegrityException(
                "The retained invariant plan bytes do not match the given plan's canonicalization.");
        }

        var reopenedInvariantPlan = invariantPlan;

        var definition = reopenedInvariantPlan.SetDefinitions.SingleOrDefault(value => value.SetId == setId)
            ?? throw new ArgumentException("The set identity is not in the reopened LU plan.", nameof(setId));
        if (definition.Acquisition != LuxembourgQuerySetAcquisition.PublisherQuery || definition.TemplateId is null)
        {
            throw new ArgumentException(
                "A local materialization has no repeated-enumeration evidence to resolve.",
                nameof(setId));
        }

        var template = reopenedInvariantPlan.QueryTemplates.Single(value => value.TemplateId == definition.TemplateId);

        // The renderer source bytes: reopened by the reference the invariant plan names, never
        // trusted from the caller's in-memory copy.
        var rendererSourceBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, rendererSource.Reference.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var reopenedRendererSource = MachineQueryRendererSource.Open(
            rendererSource.Reference, rendererSourceBytes.Span);

        var countRenderer = new LuxembourgSparqlRenderer(
            invariantPlanRef, reopenedRendererSource, reopenedInvariantPlan, template, LuxembourgQueryRequestKind.Count);
        var pageRenderer = new LuxembourgSparqlRenderer(
            invariantPlanRef, reopenedRendererSource, reopenedInvariantPlan, template, LuxembourgQueryRequestKind.Page);

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
    public LuxembourgEnumerationDeliveryReceipt? TryCompareAndReceipt(
        IReadOnlyDictionary<string, CustodyMembership> sessionArtifactMembership,
        IReadOnlyDictionary<string, CustodyMembership> executorWrittenMembership,
        out LuxembourgEnumerationReceiptRefusal refusal)
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
            refusal = LuxembourgEnumerationReceiptRefusal.DeliveryComparisonRefused;
            return null;
        }

        var readOnlyDigests = _passA.AllObservations()
            .Concat(_passB.AllObservations())
            .SelectMany(static observation => observation.ReadOnlyDigests)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return LuxembourgEnumerationDeliveryReceipt.TryCreate(
            delivery,
            sessionArtifactMembership,
            executorWrittenMembership,
            readOnlyDigests,
            out refusal);
    }
}
