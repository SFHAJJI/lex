using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why an inventory produced no subjects. Closed.</summary>
public enum LuxembourgInitialDraftInventoryRefusal
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
    /// repeat would break the one property the batching stage rests on — that every inventoried
    /// subject is assigned to exactly one batch — by making "exactly one" ambiguous before batching
    /// even begins. It is refused here rather than deduplicated, because a publisher that delivered
    /// a grouped subject twice did not answer the question this family asked.
    /// </remarks>
    [JsonStringEnumMemberName("subject_delivered_twice")]
    SubjectDeliveredTwice = 5,

    /// <summary>
    /// The publisher delivered a class member no request can name, so no exact inventory exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A blank node's label is scoped to the result set that carried it. SPARQL's JSON results
    /// format says so outright: reuse of a label in another results object does not imply the same
    /// blank node. This family's whole output is an EXACT membership list that a later stage covers
    /// exactly once, and no exact list can be built where a member's identity does not survive the
    /// request that reported it - two passes can agree on the label b0 while meaning different
    /// subjects, and successive pages can skip, repeat or relabel one.
    /// </para>
    /// <para>
    /// The observation is RETAINED on the refusal rather than dropped. The ruling permits refusing
    /// or typing a non-addressable subject and forbids filtering one away; typing alone is not
    /// enough here, because a typed row still sits inside a list that claims to be exact. So the
    /// member is reported, and the inventory does not complete.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("non_addressable_subject_observed")]
    NonAddressableSubjectObserved = 6,
}

/// <summary>
/// One subject of the <c>InitialDraft</c> class, exactly as the publisher delivered it.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is the term's own lexical form and <see cref="Kind"/> is the publisher's word
/// for what it is. A subject that is not <see cref="LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind"/>
/// is a real class member that a later <c>VALUES</c> batch cannot address, so it is carried here as
/// a typed member and refused for batching there — never dropped, because a dropped member would
/// make an inventory that calls itself complete quietly smaller than the class.
/// </remarks>
public sealed record LuxembourgInitialDraftSubject(
    string Value,
    string Kind,
    long Multiplicity,
    string SourceObservationId)
{
    /// <summary>Whether a later request can name this subject.</summary>
    public bool IsAddressable =>
        string.Equals(Kind, LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, StringComparison.Ordinal);
}

/// <summary>The proven membership of the class, or one typed refusal. Never both.</summary>
public sealed class LuxembourgInitialDraftInventoryResult
{
    private LuxembourgInitialDraftInventoryResult(
        IReadOnlyList<LuxembourgInitialDraftSubject>? subjects,
        SourceArtifactRef? completionEvidenceRef,
        LuxembourgInitialDraftInventoryCitation? citation,
        LuxembourgInitialDraftInventoryRefusal refusal,
        string? detail,
        int productRequestCount,
        IReadOnlyList<LuxembourgInitialDraftSubject>? observedNonAddressable = null)
    {
        // SNAPSHOTTED, NOT ALIASED. These were the producer's own List, handed out behind an
        // IReadOnlyList that a caller could cast back to and mutate. Doing so changed the members a
        // later batch derived while the citation kept the digest of the ORIGINAL population - so the
        // batch and the citation it claims to come from could describe two different inventories.
        ObservedNonAddressable = observedNonAddressable is null
            ? []
            : Array.AsReadOnly(observedNonAddressable.ToArray());
        Subjects = subjects is null ? null : Array.AsReadOnly(subjects.ToArray());
        CompletionEvidenceRef = completionEvidenceRef;
        Citation = citation;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    /// <summary>Every delivered subject, in the publisher's own delivery order.</summary>
    /// <remarks>
    /// Every one of them is addressable. A delivery carrying a member no request can name produces
    /// no inventory at all - see
    /// <see cref="LuxembourgInitialDraftInventoryRefusal.NonAddressableSubjectObserved"/> - so this
    /// list never needs filtering by a caller, and a caller that filtered it would be guessing.
    /// </remarks>
    public IReadOnlyList<LuxembourgInitialDraftSubject>? Subjects { get; }

    /// <summary>
    /// The non-addressable members this run saw, retained even though it produced no inventory.
    /// </summary>
    /// <remarks>
    /// Non-empty exactly when the refusal is
    /// <see cref="LuxembourgInitialDraftInventoryRefusal.NonAddressableSubjectObserved"/>. This is
    /// what keeps that refusal from being a silent filter: the members the publisher holds are
    /// named, with their own evidence, by a run that declined to call itself complete.
    /// </remarks>
    public IReadOnlyList<LuxembourgInitialDraftSubject> ObservedNonAddressable { get; }

    public SourceArtifactRef? CompletionEvidenceRef { get; }

    /// <summary>
    /// This inventory as a later batch must cite it, minted here from this run's own proof.
    /// </summary>
    /// <remarks>
    /// Non-null exactly when <see cref="Delivered"/>. It exists because a batch citing an inventory
    /// it assembled itself proves nothing: the fields have to come from the run that enumerated the
    /// class, or "derived from the proven inventory" is a claim with no evidence behind it.
    /// </remarks>
    public LuxembourgInitialDraftInventoryCitation? Citation { get; }
    public LuxembourgInitialDraftInventoryRefusal Refusal { get; }
    public string? Detail { get; }
    public int ProductRequestCount { get; }
    public bool Delivered => Refusal == LuxembourgInitialDraftInventoryRefusal.None;

    internal static LuxembourgInitialDraftInventoryResult Success(
        IReadOnlyList<LuxembourgInitialDraftSubject> subjects,
        SourceArtifactRef completionEvidenceRef,
        LuxembourgInitialDraftInventoryCitation citation,
        int productRequestCount) =>
        new(subjects, completionEvidenceRef, citation,
            LuxembourgInitialDraftInventoryRefusal.None, null, productRequestCount);

    internal static LuxembourgInitialDraftInventoryResult Refused(
        LuxembourgInitialDraftInventoryRefusal refusal,
        string detail,
        int productRequestCount,
        IReadOnlyList<LuxembourgInitialDraftSubject>? observedNonAddressable = null) =>
        new(null, null, null, refusal, detail, productRequestCount, observedNonAddressable);

    /// <summary>
    /// The subjects a later batch can name, deduplicated and in one deterministic order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE INPUT THE BATCHING STAGE MUST COVER EXACTLY ONCE, so it is minted here rather
    /// than recomputed there: two derivations of "the list to batch" is two chances to disagree
    /// about what completeness means. Ordinal order, because the batches must be reproducible from
    /// the same inventory on a later run and the publisher's delivery order is not a promise.
    /// </para>
    /// <para>
    /// Deduplication here would hide a defect, so there is none: <c>DecodeRows</c> refuses a
    /// repeated subject outright. What this filters is only the non-addressable, and those remain
    /// in <see cref="Subjects"/> and in <see cref="NonAddressable"/>.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> AddressableInOrder() =>
        Subjects is null
            ? throw new InvalidOperationException("A refused inventory has no subjects to batch.")
            : Subjects.Select(static value => value.Value)
                .Order(StringComparer.Ordinal)
                .ToArray();

}

/// <summary>
/// Runs the InitialDraft inventory and reads its proven rows.
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
/// WHAT THIS FAMILY MUST NOT DO is infer. If the publisher refuses, the refusal is the answer and
/// no membership may be guessed from it; if it answers, what it answered is the class and nothing
/// widens or narrows it here.
/// </para>
/// </remarks>
public sealed class LuxembourgInitialDraftInventoryProducer
{
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public LuxembourgInitialDraftInventoryProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal LuxembourgInitialDraftInventoryProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    /// <summary>Runs the inventory and reads its proven rows. The only public way to obtain subjects.</summary>
    public async Task<LuxembourgInitialDraftInventoryResult> RunAsync(
        LuxembourgInitialDraftInventoryRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var run = await _executor.RunLuxembourgInitialDraftInventoryAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);
        if (run.Receipt is not { } receipt)
        {
            // THE PUBLISHER'S OWN REASON IS CARRIED, not just this seat's word for it. A refusal
            // reading only "EnumerationRefused" throws away the one thing that makes a live failure
            // debuggable without asking the publisher again, and the executor already retained it.
            return LuxembourgInitialDraftInventoryResult.Refused(
                LuxembourgInitialDraftInventoryRefusal.EnumerationRefused,
                run.Refusal is { } refusal
                    ? refusal.Code + (refusal.CoreRefusalDetail is { Length: > 0 } detail
                        ? ": " + detail
                        : string.Empty)
                    : "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount);
        }

        var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal);
        if (proof is null)
        {
            return LuxembourgInitialDraftInventoryResult.Refused(
                LuxembourgInitialDraftInventoryRefusal.EnumerationProofRefused,
                proofRefusal.ToString(),
                run.ProductRequestCount);
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
            return LuxembourgInitialDraftInventoryResult.Refused(
                LuxembourgInitialDraftInventoryRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount);
        }

        // THE PROOF ITSELF, not the two fields read off it. This used to hand DecodeRows the run
        // reference and the family key as separate values, which is how the citation ended up
        // stating an identity rather than carrying one - and how an outside consumer could state a
        // different identity entirely. Both are still taken from the proof; they are just no longer
        // detachable from it on the way.
        return DecodeRows(
            rows,
            profile,
            proof,
            receipt.Delivery.ObservationTimes.CountA,
            run.ProductRequestCount);
    }

    /// <summary>The separator the inventory selection digest joins on.</summary>
    /// <remarks>
    /// Named rather than written inline: an escape in this position has been mangled by a shell
    /// twice in this file's history, and a digest that silently joins on the wrong byte is a
    /// citation that silently names a different population.
    /// </remarks>
    private const char LineFeed = (char)10;

    /// <summary>Decodes one delivered page set into subjects.</summary>
    /// <remarks>
    /// INTERNAL deliberately. Callers come through <see cref="RunAsync"/>; the tests reach this by
    /// <c>InternalsVisibleTo</c>, which is a test seam and not a second public door. A public decoder
    /// taking a caller's rows and a caller's evidence reference could mint class membership from rows
    /// nobody proved, citing custody nobody established.
    /// </remarks>
    internal static LuxembourgInitialDraftInventoryResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        AbsenceFamilyEnumerationProof proof,
        string observedAt,
        int productRequestCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentException.ThrowIfNullOrWhiteSpace(observedAt);

        var completionEvidenceRef = proof.AcquisitionRunRef;
        var familyKey = proof.FamilyKey;

        var subjects = new List<LuxembourgInitialDraftSubject>(rows.Count);
        var seen = new HashSet<(string Value, string Kind)>();

        foreach (var row in rows)
        {
            LuxembourgInitialDraftSubject subject;
            try
            {
                subject = DecodeRow(row, profile, completionEvidenceRef.ResourceId);
            }
            catch (ArgumentException exception)
            {
                return LuxembourgInitialDraftInventoryResult.Refused(
                    LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted,
                    exception.Message,
                    productRequestCount);
            }

            if (!seen.Add((subject.Value, subject.Kind)))
            {
                return LuxembourgInitialDraftInventoryResult.Refused(
                    LuxembourgInitialDraftInventoryRefusal.SubjectDeliveredTwice,
                    $"The delivery names {subject.Value} more than once, and the query groups by "
                        + "subject, so an honest answer names each exactly once.",
                    productRequestCount);
            }

            subjects.Add(subject);
        }

        // NO EXACT INVENTORY EXISTS OVER AN IDENTITY THAT DOES NOT SURVIVE ITS OWN RESPONSE. The
        // page derives key_1 from STR(?draft), which for a blank node is a label scoped to the
        // result set that carried it - so Source/Core's own refusal of a BlankNode canonical-key
        // component never sees one here, it sees a plain literal. Two passes can then agree on "b0"
        // while meaning different subjects, and successive pages can skip, repeat or relabel a
        // member. Found in review at 031fbd51 with a two-pass delivery of b0 and b1 that this
        // producer called Delivered.
        //
        // The members are named on the refusal rather than dropped, which is how the ruling's
        // no-silent-filter requirement is met by refusing rather than by typing: a typed row would
        // still be sitting inside a list that calls itself exact.
        var nonAddressable = subjects.Where(static value => !value.IsAddressable).ToArray();
        if (nonAddressable.Length != 0)
        {
            return LuxembourgInitialDraftInventoryResult.Refused(
                LuxembourgInitialDraftInventoryRefusal.NonAddressableSubjectObserved,
                "The class holds " + nonAddressable.Length
                    + " member(s) no request can name, so no exact inventory exists over them: "
                    + string.Join(", ", nonAddressable.Select(static value => value.Value)) + ".",
                productRequestCount,
                nonAddressable);
        }

        // THE CITATION IS MINTED HERE, over the population this inventory actually hands to
        // batching. Digesting AddressableInOrder rather than the raw subjects means the digest
        // changes exactly when what the batches must cover changes.
        var addressable = subjects
            .Where(static value => value.IsAddressable)
            .Select(static value => value.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // THE ROWS THIS RUN ACTUALLY RECEIVED, so the door can check they are the ones the proof
        // proves rather than trusting that some proof exists. They came from
        // VerifiedRepeatedEnumerationRows.TryOpen, which already re-derived their count and
        // canonical-key digest against this same proof, so the door's check passes here by
        // construction - and fails for anyone pairing this proof with another delivery.
        var citation = LuxembourgInitialDraftInventoryCitation.MintedOver(
            proof,
            rows,
            addressable,
            observedAt);

        return LuxembourgInitialDraftInventoryResult.Success(
            subjects, completionEvidenceRef, citation, productRequestCount);
    }

    private static LuxembourgInitialDraftSubject DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string observationId)
    {
        if (row.Terms.Count != profile.ProjectionVariables.Count)
        {
            throw new ArgumentException(
                "An inventory row has exactly the profile's terms.", nameof(row));
        }

        var draftTerm = Term(row, profile, "draft");

        // RDF PUTS NO LITERAL IN SUBJECT POSITION, so a literal here is not an exotic member this
        // family should type and carry — it is a delivery that cannot have come from
        // `?draft a <InitialDraft>`. An unbound subject likewise: this query has no absence branch,
        // because a class member that is not there is simply not a row.
        var kind = draftTerm.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri => LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind,
            RepeatedEnumerationRdfTermKind.BlankNode =>
                LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind,
            _ => throw new ArgumentException(
                "A class member arrives as an IRI or a blank node; nothing else can be a subject.",
                nameof(row)),
        };

        if (string.IsNullOrEmpty(draftTerm.Value))
        {
            throw new ArgumentException("A subject term carries its own lexical form.", nameof(row));
        }

        RequireMarkerAgrees(row, profile, "draft_kind", kind);

        var multiplicity = Multiplicity(row, profile);

        // The keys are what the cursor orders by, so a row keyed differently from the terms it
        // delivered would page correctly and decode into a different fact.
        RequireKey(row, profile, "key_1", draftTerm.Value);
        RequireKey(row, profile, "key_2", kind);

        return new LuxembourgInitialDraftSubject(draftTerm.Value, kind, multiplicity, observationId);
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
    /// Both halves are load-bearing, and the sibling opinion family shipped without the second: a
    /// projected column this design groups and keys on is authoritative, so recomputing what it says
    /// and never reading it lets the delivered column contradict both the term and the key.
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
