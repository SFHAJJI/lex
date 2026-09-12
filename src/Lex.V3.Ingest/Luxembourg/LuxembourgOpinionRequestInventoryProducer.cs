using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why an OpinionRequest inventory produced no subjects. Closed.</summary>
public enum LuxembourgOpinionRequestInventoryRefusal
{
    /// <summary>No refusal: the inventory was delivered.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The enumeration itself was refused before any row existed.</summary>
    [JsonStringEnumMemberName("enumeration_refused")]
    EnumerationRefused = 1,

    /// <summary>The run delivered but its whole enumeration was not proven.</summary>
    [JsonStringEnumMemberName("enumeration_proof_refused")]
    EnumerationProofRefused = 2,

    /// <summary>
    /// The proven pages would not reopen into verified rows.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="EnumerationProofRefused"/>: the enumeration was proven and the
    /// failure is later, in re-deriving each page's own rows from its retained bytes. Folding the
    /// two would report a proof failure for a delivery whose proof held.
    /// </remarks>
    [JsonStringEnumMemberName("verified_rows_refused")]
    VerifiedRowsRefused = 3,

    /// <summary>
    /// A delivered row could not be read at all: a wrong term count, a subject of a kind RDF cannot
    /// put in subject position, a marker that is not the query's own plain literal, a marker
    /// disagreeing with its term, or a cursor key that does not key the terms it describes.
    /// </summary>
    [JsonStringEnumMemberName("row_not_admitted")]
    RowNotAdmitted = 4,

    /// <summary>
    /// The same subject arrived twice.
    /// </summary>
    /// <remarks>
    /// The query groups by the subject, so an honest delivery names each exactly once. Admitting a
    /// repeat would break the property every later stage rests on - that each inventoried subject is
    /// assigned to exactly one batch - by making "exactly one" ambiguous before batching begins. It
    /// is refused rather than deduplicated, because a publisher that delivered a grouped subject
    /// twice did not answer the question this family asked.
    /// </remarks>
    [JsonStringEnumMemberName("subject_delivered_twice")]
    SubjectDeliveredTwice = 5,

    /// <summary>
    /// The publisher delivered a class member no request can name, so no exact inventory exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A blank node's label is scoped to the result set that carried it; SPARQL's JSON results
    /// format says reuse of a label in another results object does not imply the same blank node.
    /// This family's output is an EXACT membership list that a later cover claims to sweep exactly
    /// once, and no exact list exists where a member's identity does not survive the request that
    /// reported it.
    /// </para>
    /// <para>
    /// The member is RETAINED on the refusal rather than dropped. Typing it alone would not be
    /// enough, because a typed row still sits inside a list that calls itself exact.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("non_addressable_subject_observed")]
    NonAddressableSubjectObserved = 6,
}

/// <summary>
/// One subject of the <c>OpinionRequest</c> class, exactly as the publisher delivered it.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is the term's own lexical form and <see cref="Kind"/> is the publisher's word
/// for what it is. A member that is not addressable is carried here as a typed member and refuses
/// the inventory - never dropped, because a dropped member would make an inventory that calls itself
/// complete quietly smaller than the class.
/// </remarks>
public sealed record LuxembourgOpinionRequestSubject(
    string Value,
    string Kind,
    long Multiplicity,
    string SourceObservationId)
{
    /// <summary>Whether a later request can name this subject.</summary>
    public bool IsAddressable =>
        string.Equals(
            Kind, LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind, StringComparison.Ordinal);
}

/// <summary>The proven membership of the class, or one typed refusal. Never both.</summary>
public sealed class LuxembourgOpinionRequestInventoryResult
{
    private readonly IReadOnlyList<string>? _addressableInOrder;

    private LuxembourgOpinionRequestInventoryResult(
        IReadOnlyList<LuxembourgOpinionRequestSubject>? subjects,
        IReadOnlyList<string>? addressableInOrder,
        SourceArtifactRef? completionEvidenceRef,
        LuxembourgOpinionRequestInventoryCitation? citation,
        LuxembourgOpinionRequestInventoryRefusal refusal,
        string? detail,
        int productRequestCount,
        WireBudgetSnapshot wireBudget,
        IReadOnlyList<LuxembourgOpinionRequestSubject>? observedNonAddressable = null)
    {
        WireBudget = wireBudget ?? throw new ArgumentNullException(nameof(wireBudget));
        // SNAPSHOTTED, NOT ALIASED. A list handed out behind IReadOnlyList can be cast back and
        // mutated, which would change the members a later batch derives while the citation kept the
        // digest of the ORIGINAL population - so the batch and the citation it claims to come from
        // would describe two different inventories.
        ObservedNonAddressable = observedNonAddressable is null
            ? []
            : Array.AsReadOnly(observedNonAddressable.ToArray());
        Subjects = subjects is null ? null : Array.AsReadOnly(subjects.ToArray());
        _addressableInOrder = addressableInOrder is null
            ? null
            : Array.AsReadOnly(addressableInOrder.ToArray());
        CompletionEvidenceRef = completionEvidenceRef;
        Citation = citation;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    /// <summary>Every delivered subject, in the publisher's own delivery order.</summary>
    /// <remarks>
    /// Every one of them is addressable: a delivery carrying a member no request can name produces
    /// no inventory at all, so this list never needs filtering by a caller, and a caller that
    /// filtered it would be guessing.
    /// </remarks>
    public IReadOnlyList<LuxembourgOpinionRequestSubject>? Subjects { get; }

    /// <summary>
    /// The non-addressable members this run saw, retained even though it produced no inventory.
    /// </summary>
    /// <remarks>
    /// Non-empty exactly when the refusal is
    /// <see cref="LuxembourgOpinionRequestInventoryRefusal.NonAddressableSubjectObserved"/>. This is
    /// what keeps that refusal from being a silent filter.
    /// </remarks>
    public IReadOnlyList<LuxembourgOpinionRequestSubject> ObservedNonAddressable { get; }

    public SourceArtifactRef? CompletionEvidenceRef { get; }

    /// <summary>
    /// This inventory as a later batch must cite it, minted here from this run's own proof.
    /// </summary>
    /// <remarks>
    /// Non-null exactly when <see cref="Delivered"/>. A batch citing an inventory it assembled
    /// itself proves nothing: the fields have to come from the run that enumerated the class.
    /// </remarks>
    public LuxembourgOpinionRequestInventoryCitation? Citation { get; }

    public LuxembourgOpinionRequestInventoryRefusal Refusal { get; }

    public string? Detail { get; }

    public int ProductRequestCount { get; }

    /// <summary>
    /// What the shared wire budget stood at when this run ended. Always present.
    /// </summary>
    /// <remarks>
    /// REQUIRED ON EVERY OUTCOME, delivered or refused, which is why it is a constructor parameter
    /// rather than something a caller may set. A refused run still sent requests - often it refused
    /// BECAUSE it had - and a ceiling whose evidence only survives success cannot be reconciled on
    /// the runs that most need reconciling.
    /// </remarks>
    public WireBudgetSnapshot WireBudget { get; }

    public bool Delivered => Refusal == LuxembourgOpinionRequestInventoryRefusal.None;

    internal static LuxembourgOpinionRequestInventoryResult Success(
        IReadOnlyList<LuxembourgOpinionRequestSubject> subjects,
        IReadOnlyList<string> addressableInOrder,
        SourceArtifactRef completionEvidenceRef,
        LuxembourgOpinionRequestInventoryCitation citation,
        int productRequestCount,
        WireBudgetSnapshot wireBudget) =>
        new(subjects, addressableInOrder, completionEvidenceRef, citation,
            LuxembourgOpinionRequestInventoryRefusal.None, null, productRequestCount, wireBudget);

    internal static LuxembourgOpinionRequestInventoryResult Refused(
        LuxembourgOpinionRequestInventoryRefusal refusal,
        string detail,
        int productRequestCount,
        WireBudgetSnapshot wireBudget,
        IReadOnlyList<LuxembourgOpinionRequestSubject>? observedNonAddressable = null) =>
        new(null, null, null, null, refusal, detail, productRequestCount, wireBudget,
            observedNonAddressable);

    /// <summary>
    /// The subjects a later batch must cover exactly once, in one deterministic order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MINTED HERE RATHER THAN RECOMPUTED BY THE COVER: two derivations of "the list to batch" is
    /// two chances to disagree about what completeness means. Ordinal order, because the batches
    /// must be reproducible from the same inventory on a later run and the publisher's delivery
    /// order is not a promise.
    /// </para>
    /// <para>
    /// There is no deduplication here and there must not be: a repeated subject is refused outright
    /// above, and filtering one away here would hide that defect behind a tidy list.
    /// </para>
    /// <para>
    /// RETURNED, NOT RECOMPUTED. This is the exact list the citation was minted over, stored when
    /// the inventory was built. Deriving it a second time here would be two derivations of "the list
    /// to batch", which is two chances to disagree - and a mutation on my own slice showed the
    /// disagreement would have been invisible, because the citation door binds rows by an
    /// order-sensitive digest and so can never see an order the population did not already have.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> AddressableInOrder() =>
        _addressableInOrder
        ?? throw new InvalidOperationException("A refused inventory has no subjects to batch.");
}

/// <summary>
/// Runs the OpinionRequest inventory and reads its proven rows.
/// </summary>
/// <remarks>
/// <para>
/// THE PRODUCER OWNS THE RUN, for the reason S2-A01 gives: what this family publishes are PUBLISHER
/// assertions about class membership, and an assertion resting on rows a caller handed in and
/// custody a caller named is not one. <see cref="RunAsync"/> is the only public door; it drives the
/// executor, requires a receipt, proves the enumeration, reopens each page's retained bytes and
/// passes them through <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> before any row is read,
/// and the completion evidence every subject cites is the RUN'S OWN, taken from the proof.
/// </para>
/// <para>
/// NOTHING IS INFERRED. If the publisher refuses, the refusal is the answer and no membership may be
/// guessed from it; if it answers, what it answered is the class and nothing widens or narrows it.
/// </para>
/// <para>
/// NO OBSERVATION INSTANT IS CARRIED, unlike the InitialDraft inventory beside it. This family's
/// citation deliberately holds none - a caller-supplied instant copied into a property documented as
/// an observation time is a provenance claim the caller cannot make - so a reader needing the time
/// takes it from the run's own retained receipt through the citation's acquisition run reference.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionRequestInventoryProducer
{
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public LuxembourgOpinionRequestInventoryProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal LuxembourgOpinionRequestInventoryProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    /// <summary>Runs the family and reads its proven rows. The only public way to obtain subjects.</summary>
    public async Task<LuxembourgOpinionRequestInventoryResult> RunAsync(
        LuxembourgOpinionRequestInventoryRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var run = await _executor.RunLuxembourgOpinionRequestInventoryAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);

        // READ ONCE, HERE. Everything below this line reads custody, not the wire, so this is the
        // last moment the budget changes on this run's account and the first moment it is complete.
        var wireBudget = WireBudgetSnapshot.Of(request.WireBudget);
        if (run.Receipt is not { } receipt)
        {
            // THE PUBLISHER'S OWN REASON IS CARRIED, not just this seat's word for it. A refusal
            // reading only "EnumerationRefused" throws away the one thing that makes a live failure
            // debuggable without asking the publisher again, and the executor already retained it.
            return LuxembourgOpinionRequestInventoryResult.Refused(
                LuxembourgOpinionRequestInventoryRefusal.EnumerationRefused,
                run.Refusal is { } refusal
                    ? refusal.Code + (refusal.CoreRefusalDetail is { Length: > 0 } detail
                        ? ": " + detail
                        : string.Empty)
                    : "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount,
                wireBudget);
        }

        var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal);
        if (proof is null)
        {
            return LuxembourgOpinionRequestInventoryResult.Refused(
                LuxembourgOpinionRequestInventoryRefusal.EnumerationProofRefused,
                proofRefusal.ToString(),
                run.ProductRequestCount,
                wireBudget);
        }

        var pages = new List<RepeatedEnumerationResolvedEvidence>(receipt.Delivery.PagesA.Pages.Count);
        foreach (var page in receipt.Delivery.PagesA.Pages.OrderBy(static value => value.Ordinal))
        {
            pages.Add(await _reopenGlue.ReopenPageEvidenceAsync(page.Evidence, cancellationToken)
                .ConfigureAwait(false));
        }

        var profile = request.Plan.CreateDeliveryProfile();
        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof,
            receipt.Delivery,
            profile,
            receipt.Delivery.InterpretationProfileRef,
            receipt.Delivery.CountA.HttpEvidenceRef,
            pages,
            out var rowRefusal);
        if (rows is null)
        {
            return LuxembourgOpinionRequestInventoryResult.Refused(
                LuxembourgOpinionRequestInventoryRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount,
                wireBudget);
        }

        // THE PROOF ITSELF, not the two fields read off it: the run reference and the family key
        // stay attached to the evidence they came from on the way to the citation door.
        return DecodeRows(rows, profile, proof, run.ProductRequestCount, wireBudget);
    }

    /// <summary>Decodes one delivered page set into subjects.</summary>
    /// <remarks>
    /// INTERNAL deliberately. Callers come through <see cref="RunAsync"/>; the tests reach this by
    /// <c>InternalsVisibleTo</c>, which is a test seam and not a second public door. A public decoder
    /// taking a caller's rows and a caller's evidence reference could mint class membership from rows
    /// nobody proved, citing custody nobody established.
    /// </remarks>
    internal static LuxembourgOpinionRequestInventoryResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        AbsenceFamilyEnumerationProof proof,
        int productRequestCount,
        WireBudgetSnapshot wireBudget)
    {
        ArgumentNullException.ThrowIfNull(wireBudget);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(proof);

        var completionEvidenceRef = proof.AcquisitionRunRef;

        var subjects = new List<LuxembourgOpinionRequestSubject>(rows.Count);
        var seen = new HashSet<(string Value, string Kind)>();

        foreach (var row in rows)
        {
            LuxembourgOpinionRequestSubject subject;
            try
            {
                subject = DecodeRow(row, profile, completionEvidenceRef.ResourceId);
            }
            catch (ArgumentException exception)
            {
                return LuxembourgOpinionRequestInventoryResult.Refused(
                    LuxembourgOpinionRequestInventoryRefusal.RowNotAdmitted,
                    exception.Message,
                    productRequestCount,
                    wireBudget);
            }

            if (!seen.Add((subject.Value, subject.Kind)))
            {
                return LuxembourgOpinionRequestInventoryResult.Refused(
                    LuxembourgOpinionRequestInventoryRefusal.SubjectDeliveredTwice,
                    $"The delivery names {subject.Value} more than once, and the query groups by "
                        + "subject, so an honest answer names each exactly once.",
                    productRequestCount,
                    wireBudget);
            }

            subjects.Add(subject);
        }

        // NO EXACT INVENTORY EXISTS OVER AN IDENTITY THAT DOES NOT SURVIVE ITS OWN RESPONSE. The
        // page derives key_1 from STR(?request), which for a blank node is a label scoped to the
        // result set that carried it - so Source/Core's own refusal of a BlankNode canonical-key
        // component never sees one here, it sees a plain literal. Two passes can then agree on "b0"
        // while meaning different subjects.
        //
        // The members are named on the refusal rather than dropped, which is how the no-silent-filter
        // requirement is met by refusing rather than by typing.
        var nonAddressable = subjects.Where(static value => !value.IsAddressable).ToArray();
        if (nonAddressable.Length != 0)
        {
            return LuxembourgOpinionRequestInventoryResult.Refused(
                LuxembourgOpinionRequestInventoryRefusal.NonAddressableSubjectObserved,
                "The class holds " + nonAddressable.Length
                    + " member(s) no request can name, so no exact inventory exists over them: "
                    + string.Join(", ", nonAddressable.Select(static value => value.Value)) + ".",
                productRequestCount,
                wireBudget,
                nonAddressable);
        }

        // THE CITATION IS MINTED HERE, over the population this inventory actually hands to batching.
        // Digesting the ordered addressable members means the digest changes exactly when what the
        // batches must cover changes.
        // ORDERED ONCE, HERE, and carried on the result. Ordinal because the batches must be
        // reproducible from the same inventory on a later run, and the publisher's delivery order is
        // not a promise even though a proven delivery happens to be key-ordered.
        var addressable = subjects
            .Select(static value => value.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // THE ROWS THIS RUN ACTUALLY RECEIVED, so the door can check they are the ones the proof
        // proves rather than trusting that some proof exists. They came from
        // VerifiedRepeatedEnumerationRows.TryOpen, which already re-derived their count and
        // canonical-key digest against this same proof, so the door's check passes here by
        // construction - and fails for anyone pairing this proof with another delivery.
        var citation = LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, addressable);

        return LuxembourgOpinionRequestInventoryResult.Success(
            subjects, addressable, completionEvidenceRef, citation, productRequestCount,
            wireBudget);
    }

    private static LuxembourgOpinionRequestSubject DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string observationId)
    {
        if (row.Terms.Count != profile.ProjectionVariables.Count)
        {
            throw new ArgumentException(
                "An inventory row has exactly the profile's terms.", nameof(row));
        }

        var requestTerm = Term(row, profile, "request");

        // RDF PUTS NO LITERAL IN SUBJECT POSITION, so a literal here is not an exotic member this
        // family should type and carry - it is a delivery that cannot have come from
        // `?request a <OpinionRequest>`. An unbound subject likewise: this query has no absence
        // branch, because a class member that is not there is simply not a row.
        var kind = requestTerm.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri =>
                LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind,
            RepeatedEnumerationRdfTermKind.BlankNode =>
                LuxembourgOpinionRequestInventoryDiscoveryPlan.UnsupportedBlankNodeKind,
            _ => throw new ArgumentException(
                "A class member arrives as an IRI or a blank node; nothing else can be a subject.",
                nameof(row)),
        };

        if (string.IsNullOrEmpty(requestTerm.Value))
        {
            throw new ArgumentException("A subject term carries its own lexical form.", nameof(row));
        }

        RequireMarkerAgrees(row, profile, "request_kind", kind);

        var multiplicity = Multiplicity(row, profile);

        // The keys are what the cursor orders by, so a row keyed differently from the terms it
        // delivered would page correctly and decode into a different fact.
        RequireKey(row, profile, "key_1", requestTerm.Value);
        RequireKey(row, profile, "key_2", kind);

        return new LuxembourgOpinionRequestSubject(
            requestTerm.Value, kind, multiplicity, observationId);
    }

    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row, RepeatedEnumerationInterpretationProfile profile, string name)
    {
        var ordinal = profile.ProjectionVariables.ToList().IndexOf(name);
        return ordinal < 0
            ? throw new ArgumentException($"The profile does not project {name}.", nameof(profile))
            : row.Terms[ordinal];
    }

    /// <summary>
    /// The marker column must be the query's own unqualified plain literal AND must agree with the
    /// term it describes.
    /// </summary>
    /// <remarks>
    /// Both halves are load-bearing: a projected column this design groups and keys on is
    /// authoritative, so recomputing what it says and never reading it lets the delivered column
    /// contradict both the term and the key.
    /// </remarks>
    private static void RequireMarkerAgrees(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string name,
        string expected)
    {
        var marker = Term(row, profile, name);
        if (marker.Kind != RepeatedEnumerationRdfTermKind.Literal ||
            marker.Datatype is not null || marker.Language is not null ||
            !string.Equals(marker.Value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {name} marker and the term it describes disagree about what was delivered.",
                nameof(row));
        }
    }

    private static long Multiplicity(
        RepeatedEnumerationRow row, RepeatedEnumerationInterpretationProfile profile)
    {
        var term = Term(row, profile, "multiplicity");
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal ||
            term.Datatype != "http://www.w3.org/2001/XMLSchema#integer" ||
            term.Language is not null ||
            !long.TryParse(term.Value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ||
            value < 1)
        {
            throw new ArgumentException(
                "multiplicity is the grouped count and arrives as a positive xsd:integer.", nameof(row));
        }

        return value;
    }

    private static void RequireKey(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string keyName,
        string expected)
    {
        var term = Term(row, profile, keyName);
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal ||
            term.Value is null || term.Datatype is not null || term.Language is not null ||
            !string.Equals(term.Value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{keyName} does not key the terms this row delivered.", nameof(row));
        }
    }
}
