using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>Why this code concluded an OpinionRequest holds no value for a property.</summary>
/// <remarks>
/// Closed, and with no <c>None</c> member: an absence with no reason is not a fact anyone can read.
/// One member, because under the present-facts shape there is exactly one honest way to reach an
/// absence here. The enum exists so that a second way cannot arrive untyped.
/// </remarks>
public enum LuxembourgOpinionRequestAbsenceReason
{
    /// <summary>
    /// This batch's enumeration was proven whole over a subject the publisher itself typed
    /// <c>OpinionRequest</c>, and delivered no row for this pair.
    /// </summary>
    /// <remarks>
    /// BOTH HALVES ARE REQUIRED. A complete enumeration alone is what the draft family has, and it
    /// is not enough here: the plan refuses to read the query's own class filter as evidence of the
    /// subject's role, so the delivered <c>rdf:type</c> row is what makes the silence readable.
    /// </remarks>
    [JsonStringEnumMemberName("typed_by_the_publisher_enumerated_and_not_held")]
    TypedByThePublisherEnumeratedAndNotHeld = 1,
}

/// <summary>
/// One (request, property) pair this code concluded the publisher holds no value for.
/// </summary>
/// <remarks>
/// NOT A PUBLISHER ASSERTION, AND THE TYPE EXISTS TO MAKE THAT UNSAYABLE. A delivered row is
/// something Legilux returned; this is something THIS CODE CONCLUDED from a complete enumeration
/// that returned nothing for the pair. They are separate types in separate collections, because a
/// single type carrying both would satisfy S2-A03's demand that gaps be first-class by breaking
/// S2-A01's demand that assertions be typed as the publisher's.
/// </remarks>
public sealed class LuxembourgOpinionRequestObservedAbsence
{
    internal LuxembourgOpinionRequestObservedAbsence(
        string requestIri,
        string predicateIri,
        LuxembourgOpinionRequestAbsenceReason reason,
        LuxembourgOpinionRequestBatchCitation batch,
        LuxembourgOpinionRequestInventoryCitation inventory)
    {
        RequestIri = requestIri;
        PredicateIri = predicateIri;
        Reason = reason;
        Batch = batch;
        Inventory = inventory;
    }

    public string RequestIri { get; }

    public string PredicateIri { get; }

    public LuxembourgOpinionRequestAbsenceReason Reason { get; }

    /// <summary>The delivery whose completeness this absence rests on.</summary>
    public LuxembourgOpinionRequestBatchCitation Batch { get; }

    /// <summary>The proven population the absence is meaningful over.</summary>
    public LuxembourgOpinionRequestInventoryCitation Inventory { get; }
}

/// <summary>Why a pair can be resolved neither as a value nor as an absence.</summary>
/// <remarks>
/// One member, and it is NOT the draft family's. There, a gap arises from a predicate declared on
/// another class, whose silence evidences nothing. Every predicate this family asks about is
/// declared on the class it asks about, so that source does not exist here; if one is ever found to
/// be declared elsewhere it belongs on a not-declared-here list, and this enum gains a member.
/// </remarks>
public enum LuxembourgOpinionRequestGapReason
{
    /// <summary>
    /// The delivery carried no <c>rdf:type</c> row naming this subject an <c>OpinionRequest</c>.
    /// </summary>
    /// <remarks>
    /// The subject's role is what makes silence readable, and the query having filtered on the class
    /// is not that role being answered. Without the delivered row the pair is unresolved: neither a
    /// value nor an absence, and saying so is the only honest third answer.
    /// </remarks>
    [JsonStringEnumMemberName("request_role_not_confirmed_by_delivery")]
    RequestRoleNotConfirmedByDelivery = 1,
}

/// <summary>One (request, property) pair this delivery can resolve neither way.</summary>
public sealed class LuxembourgOpinionRequestUnresolvedGap
{
    internal LuxembourgOpinionRequestUnresolvedGap(
        string requestIri,
        string predicateIri,
        LuxembourgOpinionRequestGapReason reason,
        LuxembourgOpinionRequestBatchCitation batch,
        LuxembourgOpinionRequestInventoryCitation inventory)
    {
        RequestIri = requestIri;
        PredicateIri = predicateIri;
        Reason = reason;
        Batch = batch;
        Inventory = inventory;
    }

    public string RequestIri { get; }

    public string PredicateIri { get; }

    public LuxembourgOpinionRequestGapReason Reason { get; }

    public LuxembourgOpinionRequestBatchCitation Batch { get; }

    public LuxembourgOpinionRequestInventoryCitation Inventory { get; }
}

/// <summary>
/// A narrow view of one delivered row, admitted or retained.
/// </summary>
/// <remarks>
/// A view rather than the ingest record, so the contracts assembly can own the coverage rule without
/// depending on the producer that decodes rows. The same shape carries both halves of the delivery:
/// what separates them is which list a row arrives in, and the rules below check that the split the
/// caller made is the split this family's vocabulary implies.
/// </remarks>
public sealed record LuxembourgOpinionRequestRecordView(
    string RequestIri,
    string PredicateIri,
    string? Value,
    string ValueKind);

/// <summary>Why a delivered batch does not complete into a readable matrix.</summary>
public enum LuxembourgOpinionRequestCoverageRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The stage was reached without a proven, complete enumeration to derive from.</summary>
    [JsonStringEnumMemberName("matrix_completion_over_unproven_enumeration")]
    MatrixCompletionOverUnprovenEnumeration = 1,

    /// <summary>The requested batch was not retained in a comparable form.</summary>
    [JsonStringEnumMemberName("requested_batch_not_retained")]
    RequestedBatchNotRetained = 2,

    /// <summary>A delivered record names a request this batch never asked about.</summary>
    [JsonStringEnumMemberName("delivered_request_not_requested")]
    DeliveredRequestNotRequested = 3,

    /// <summary>An admitted record names a property this family never asked about.</summary>
    [JsonStringEnumMemberName("delivered_predicate_not_asked_about")]
    DeliveredPredicateNotAskedAbout = 4,

    /// <summary>
    /// A retained row carries a predicate this family admits, so the delivery was split wrongly.
    /// </summary>
    /// <remarks>
    /// Every predicate this family admits is declared on the class it asks about, so a row carrying
    /// one is a fact about its subject and belongs in the admitted half. Retaining it would keep
    /// whole-delivery conservation balanced while the matrix never saw the value - an absence
    /// derived beside a delivered row nobody counted.
    /// </remarks>
    [JsonStringEnumMemberName("retained_row_carries_an_admissible_predicate")]
    RetainedRowCarriesAnAdmissiblePredicate = 5,

    /// <summary>A delivered row was folded zero times or more than once.</summary>
    [JsonStringEnumMemberName("delivered_row_not_accounted_exactly_once")]
    DeliveredRowNotAccountedExactlyOnce = 6,

    /// <summary>Some requested pair ended represented by nothing.</summary>
    [JsonStringEnumMemberName("matrix_pair_not_represented")]
    MatrixPairNotRepresented = 7,

    /// <summary>One pair carries both delivered values and a derived conclusion about silence.</summary>
    [JsonStringEnumMemberName("pair_holds_present_and_derived_absence")]
    PairHoldsPresentAndDerivedAbsence = 8,

    /// <summary>The enumeration proof in hand is not this batch's own.</summary>
    /// <remarks>
    /// The partition key travels out with the bound request and back through the retained delivery,
    /// so comparing it against a key recomputed here from the requested members checks that the
    /// request actually sent named these requests.
    /// </remarks>
    [JsonStringEnumMemberName("absence_evidence_not_from_this_run")]
    AbsenceEvidenceNotFromThisRun = 9,
}

/// <summary>
/// The completed (requested requests x asked properties) matrix for one delivered batch.
/// </summary>
/// <remarks>
/// <para>
/// COVERAGE IS COUNTED IN DISTINCT PAIRS AND NEVER IN ROWS. A multi-valued property delivers one row
/// per value, so rows exceed pairs whenever any subject holds several. Whether <c>referralDate</c>
/// is multi-valued on this publisher is UNMEASURED - no reviewed template could ask until this
/// family's - so the shape that survives either answer is the one built here, and the pair identity
/// is checked as a set difference rather than only as a sum.
/// </para>
/// <para>
/// THE ROLE IS DERIVED FROM THE DELIVERY, NOT ACCEPTED FROM THE CALLER. Which subjects the publisher
/// typed <c>OpinionRequest</c> is read out of the retained <c>rdf:type</c> rows here. A boolean or a
/// subject list taken as a parameter would let a caller confirm a role no row delivered, which is
/// precisely the claim this family's plan says the query's class filter may not make on the
/// publisher's behalf.
/// </para>
/// <para>
/// THE DRAFT FAMILY'S UNCONFIRMED-SUBJECT ARITHMETIC DOES NOT TRANSFER. There, a subject is
/// confirmed by delivering any row at all, so an unconfirmed one has no present pairs and its pairs
/// can be accounted as a multiplication. Here confirmation comes from a PARTICULAR row, so a subject
/// can carry a delivered value and still be unconfirmed - and multiplying would then count its pairs
/// twice, once as present and once as unconfirmed. Every pair is therefore classified individually.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionRequestCoverage
{
    private readonly Dictionary<(string Request, string Predicate), List<int>> _valueIndexesByPair;
    private readonly IReadOnlyList<LuxembourgOpinionRequestRecordView> _present;
    private readonly HashSet<string> _roleConfirmed;

    private LuxembourgOpinionRequestCoverage(
        IReadOnlyList<string> requestedRequests,
        IReadOnlyList<string> askedPredicates,
        IReadOnlyList<LuxembourgOpinionRequestRecordView> present,
        IReadOnlyList<LuxembourgOpinionRequestRecordView> retained,
        Dictionary<(string, string), List<int>> valueIndexesByPair,
        HashSet<string> roleConfirmed,
        IReadOnlyList<LuxembourgOpinionRequestObservedAbsence> derivedAbsences,
        IReadOnlyList<LuxembourgOpinionRequestUnresolvedGap> unresolvedGaps,
        IReadOnlyList<string> requestsOfUnconfirmedRole,
        LuxembourgOpinionRequestBatchCitation batch,
        LuxembourgOpinionRequestInventoryCitation inventory)
    {
        // SNAPSHOTTED AGAIN HERE, and not only at the door. Everything below is published through a
        // public IReadOnlyList, which a caller can cast back to IList and write through; the lists
        // this file builds itself are no safer than the caller's once handed out under that type.
        RequestedRequests = Array.AsReadOnly(requestedRequests.ToArray());
        AskedPredicates = Array.AsReadOnly(askedPredicates.ToArray());
        _present = Array.AsReadOnly(present.ToArray());
        RetainedRows = Array.AsReadOnly(retained.ToArray());
        _valueIndexesByPair = valueIndexesByPair;
        _roleConfirmed = roleConfirmed;
        DerivedAbsences = Array.AsReadOnly(derivedAbsences.ToArray());
        UnresolvedGaps = Array.AsReadOnly(unresolvedGaps.ToArray());
        RequestsOfUnconfirmedRole = Array.AsReadOnly(requestsOfUnconfirmedRole.ToArray());
        Batch = batch;
        Inventory = inventory;
    }

    public IReadOnlyList<string> RequestedRequests { get; }

    public IReadOnlyList<string> AskedPredicates { get; }

    /// <summary>Pairs the publisher delivered at least one value for.</summary>
    public int PresentPairCount => _valueIndexesByPair.Count;

    /// <summary>Rows the publisher delivered and this family admitted. Never used as a pair count.</summary>
    public int AdmittedRowCount => _present.Count;

    /// <summary>
    /// Every delivered row this family asserts nothing from, kept by name.
    /// </summary>
    /// <remarks>
    /// The acquisition asks for every predicate the publisher holds, so most rows are ones E8 makes
    /// no claim about. They are conserved here rather than counted away, and the <c>rdf:type</c>
    /// rows among them are what confirm each subject's role.
    /// </remarks>
    public IReadOnlyList<LuxembourgOpinionRequestRecordView> RetainedRows { get; }

    public IReadOnlyList<LuxembourgOpinionRequestObservedAbsence> DerivedAbsences { get; }

    /// <summary>Pairs this delivery can resolve neither as a value nor as an absence.</summary>
    public IReadOnlyList<LuxembourgOpinionRequestUnresolvedGap> UnresolvedGaps { get; }

    /// <summary>
    /// Requested subjects this delivery does not confirm the publisher types <c>OpinionRequest</c>.
    /// </summary>
    /// <remarks>
    /// Recorded, never converted into absence. Such a subject may still have delivered values, which
    /// remain readable as delivered; what it has not delivered is the row that would make its
    /// SILENCE readable.
    /// </remarks>
    public IReadOnlyList<string> RequestsOfUnconfirmedRole { get; }

    public LuxembourgOpinionRequestBatchCitation Batch { get; }

    public LuxembourgOpinionRequestInventoryCitation Inventory { get; }

    /// <summary>Every pair this batch accounts for, present, absent or unresolved.</summary>
    public int CoveredPairCount => RequestedRequests.Count * AskedPredicates.Count;

    /// <summary>The digest of a batch's requested members, over which an absence is meaningful.</summary>
    public static string SelectionDigestFor(IReadOnlyList<string> requestedRequests) =>
        LuxembourgOpinionRequestGraphDiscoveryPlan.SelectionDigestFor(requestedRequests);

    /// <summary>
    /// Completes the matrix over a delivered batch, or refuses without minting anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every precondition is asked before the first absence is constructed, so a refusal cannot
    /// leave half a matrix behind for a caller to read as a whole one.
    /// </para>
    /// <para>
    /// NO OBSERVATION INSTANT IS CHECKED, and its absence is deliberate rather than forgotten. The
    /// draft family requires one on its batch citation; this family's citation carries none, because
    /// a caller-supplied instant copied into a property documented as an observation time is a
    /// provenance claim the caller cannot make. An absence minted here is datable through the run's
    /// own retained receipt, named by
    /// <see cref="LuxembourgOpinionRequestBatchCitation.AcquisitionRunRef"/>.
    /// </para>
    /// </remarks>
    /// <param name="assignment">The batch the proven inventory issued, carrying its own members.</param>
    /// <param name="askedPredicatesInput">The properties this run asked about.</param>
    /// <param name="presentInput">Delivered rows this family admitted as facts.</param>
    /// <param name="retainedInput">Delivered rows retained as evidence and asserted nothing from.</param>
    /// <param name="batch">The citation minted from this batch's own enumeration proof.</param>
    public static LuxembourgOpinionRequestCoverage? TryComplete(
        LuxembourgOpinionRequestBatchAssignment assignment,
        IReadOnlyList<string> askedPredicatesInput,
        IReadOnlyList<LuxembourgOpinionRequestRecordView> presentInput,
        IReadOnlyList<LuxembourgOpinionRequestRecordView> retainedInput,
        LuxembourgOpinionRequestBatchCitation? batch,
        out LuxembourgOpinionRequestCoverageRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(askedPredicatesInput);
        ArgumentNullException.ThrowIfNull(presentInput);
        ArgumentNullException.ThrowIfNull(retainedInput);
        detail = null;

        var requestedRequests = assignment.Requests;
        var inventory = assignment.Inventory;

        // SNAPSHOT BEFORE ANYTHING READS THEM, which is why these arrive under Input names and are
        // never touched again. The value index built below is POSITIONAL into the admitted rows, so
        // a caller who removed a row afterwards would not merely change a count - it would silently
        // repoint every value lookup past it.
        var askedPredicates = Array.AsReadOnly(askedPredicatesInput.ToArray());
        var present = Array.AsReadOnly(presentInput.ToArray());
        var retained = Array.AsReadOnly(retainedInput.ToArray());

        // AN ABSENCE MAY ONLY BE DERIVED FROM A PROVEN, COMPLETE ENUMERATION. The batch citation is
        // minted from the enumeration proof and cannot be built without one, so this is the
        // structural form of "no absence from a refused or incomplete enumeration": the caller has
        // nothing to pass here unless a proof existed.
        if (batch is null)
        {
            refusal = LuxembourgOpinionRequestCoverageRefusal.MatrixCompletionOverUnprovenEnumeration;
            detail = "A derived absence means nothing without the enumeration that proves it.";
            return null;
        }

        // The requested set must be exactly the canonical form the digest was taken over, or the
        // citation on every absence would describe a different question from the one asked.
        if (requestedRequests.Count is 0 ||
            !string.Equals(
                SelectionDigestFor(requestedRequests), batch.SelectionDigest, StringComparison.Ordinal) ||
            requestedRequests.Count != batch.RequestedCount)
        {
            refusal = LuxembourgOpinionRequestCoverageRefusal.RequestedBatchNotRetained;
            detail = "The requested batch does not match the selection its own citation names.";
            return null;
        }

        // THE PROOF IN HAND MUST BE THIS BATCH'S OWN.
        if (!string.Equals(
                LuxembourgOpinionRequestGraphDiscoveryPlan.PartitionKeyFor(requestedRequests),
                batch.PartitionKey,
                StringComparison.Ordinal))
        {
            refusal = LuxembourgOpinionRequestCoverageRefusal.AbsenceEvidenceNotFromThisRun;
            detail = "The delivery's own partition key does not name these requests.";
            return null;
        }

        var requested = requestedRequests.ToHashSet(StringComparer.Ordinal);
        var asked = askedPredicates.ToHashSet(StringComparer.Ordinal);
        if (requested.Count != requestedRequests.Count ||
            asked.Count != askedPredicates.Count ||
            askedPredicates.Count is 0)
        {
            refusal = LuxembourgOpinionRequestCoverageRefusal.RequestedBatchNotRetained;
            detail = "A batch names each request once and each property once.";
            return null;
        }

        // EVERY ADMITTED ROW IS FOLDED EXACTLY ONCE, tracked per row rather than inferred from a
        // total. A LIST per pair, never a set: the values ARE the fact, and a subject holding
        // several must keep all of them.
        var byPair = new Dictionary<(string, string), List<int>>();
        for (var index = 0; index < present.Count; index++)
        {
            var record = present[index];
            if (!requested.Contains(record.RequestIri))
            {
                refusal = LuxembourgOpinionRequestCoverageRefusal.DeliveredRequestNotRequested;
                detail = $"An admitted row names {record.RequestIri}, which this batch never asked "
                    + "about.";
                return null;
            }

            if (!asked.Contains(record.PredicateIri))
            {
                refusal = LuxembourgOpinionRequestCoverageRefusal.DeliveredPredicateNotAskedAbout;
                detail = $"An admitted row names {record.PredicateIri}, which this family never "
                    + "asked about.";
                return null;
            }

            var key = (record.RequestIri, record.PredicateIri);
            if (!byPair.TryGetValue(key, out var bucket))
            {
                bucket = [];
                byPair[key] = bucket;
            }

            bucket.Add(index);
        }

        // THE ROLE IS READ OFF THE DELIVERY. A retained rdf:type row whose value is the IRI of this
        // family's class is the publisher answering what the query only asked; a type row naming
        // some other class answers it the other way and confirms nothing.
        var admissible = LuxembourgOpinionRequestGraphDiscoveryPlan.DirectlyAdmissiblePredicates
            .ToHashSet(StringComparer.Ordinal);
        var roleConfirmed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in retained)
        {
            if (!requested.Contains(row.RequestIri))
            {
                refusal = LuxembourgOpinionRequestCoverageRefusal.DeliveredRequestNotRequested;
                detail = $"A retained row names {row.RequestIri}, which this batch never asked about.";
                return null;
            }

            if (admissible.Contains(row.PredicateIri))
            {
                refusal = LuxembourgOpinionRequestCoverageRefusal.RetainedRowCarriesAnAdmissiblePredicate;
                detail = $"A retained row carries {row.PredicateIri}, which this family admits, so "
                    + "the delivery was split wrongly.";
                return null;
            }

            if (string.Equals(
                    row.PredicateIri,
                    LuxembourgOpinionRequestGraphDiscoveryPlan.RdfTypePredicateIri,
                    StringComparison.Ordinal) &&
                string.Equals(
                    row.ValueKind,
                    LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind,
                    StringComparison.Ordinal) &&
                string.Equals(
                    row.Value,
                    LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri,
                    StringComparison.Ordinal))
            {
                roleConfirmed.Add(row.RequestIri);
            }
        }

        // CONSERVATION OVER THE WHOLE DELIVERY, not just over the admitted half, and over NAMED rows
        // rather than a number. The broad acquisition carries every predicate the publisher holds
        // about these subjects, so a delivered row is either admitted here and folded exactly once,
        // or retained by name as evidence this family asserts nothing about. A row that is neither
        // has gone missing between the page and this matrix, and would be invisible in every count
        // below - including the type rows the roles above are read from.
        var folded = byPair.Values.Sum(static value => value.Count);
        if (folded != present.Count ||
            batch.DeliveredRowCount != present.Count + retained.Count)
        {
            refusal = LuxembourgOpinionRequestCoverageRefusal.DeliveredRowNotAccountedExactlyOnce;
            detail = $"The delivery carried {batch.DeliveredRowCount} rows; this matrix folded "
                + $"{folded} admitted and {retained.Count} were retained.";
            return null;
        }

        var unconfirmed = requestedRequests.Where(value => !roleConfirmed.Contains(value)).ToArray();

        // EMITTED IN A DETERMINISTIC ORDER so two runs over one batch produce the same records, and
        // CLASSIFIED PAIR BY PAIR. A subject can be role-unconfirmed and still have delivered a
        // value, so a present pair is settled before the role is consulted; only the pairs with no
        // value reach the question of what silence is worth.
        var absences = new List<LuxembourgOpinionRequestObservedAbsence>();
        var absencePairs = new HashSet<(string, string)>();
        var gaps = new List<LuxembourgOpinionRequestUnresolvedGap>();
        var gapPairs = new HashSet<(string, string)>();
        foreach (var request in requestedRequests)
        {
            foreach (var predicate in askedPredicates)
            {
                var key = (request, predicate);
                if (byPair.ContainsKey(key))
                {
                    continue;
                }

                // A DELIVERY EVIDENCES THE ABSENCE OF SOMETHING IT COULD HAVE CARRIED, AND ONLY OF A
                // SUBJECT WHOSE ROLE THE PUBLISHER STATED. Without the delivered type row the
                // silence is unreadable: a subject that never held this property and a subject whose
                // role this run failed to observe are silent in exactly the same way.
                if (!roleConfirmed.Contains(request))
                {
                    gaps.Add(new LuxembourgOpinionRequestUnresolvedGap(
                        request,
                        predicate,
                        LuxembourgOpinionRequestGapReason.RequestRoleNotConfirmedByDelivery,
                        batch,
                        inventory));
                    gapPairs.Add(key);
                    continue;
                }

                absences.Add(new LuxembourgOpinionRequestObservedAbsence(
                    request,
                    predicate,
                    LuxembourgOpinionRequestAbsenceReason.TypedByThePublisherEnumeratedAndNotHeld,
                    batch,
                    inventory));
                absencePairs.Add(key);
            }
        }

        // A PAIR CANNOT BE BOTH. Checked against the present set rather than trusted to the loop
        // above, because the loop's correctness is exactly what would be broken by a future edit.
        if (absencePairs.Overlaps(byPair.Keys) || gapPairs.Overlaps(byPair.Keys))
        {
            refusal = LuxembourgOpinionRequestCoverageRefusal.PairHoldsPresentAndDerivedAbsence;
            detail = "A pair carries both delivered values and a derived conclusion about silence.";
            return null;
        }

        // EVERY REQUESTED PAIR IS REPRESENTED, recomputed rather than declared, and as a SET
        // DIFFERENCE as well as a sum: the count identity alone would be satisfied by one duplicate
        // absence beside one missing pair.
        //
        // HONEST ABOUT WHAT THIS CAN CATCH: over sets, the three classes are a partition of the
        // requested pairs by construction, so this identity cannot detect publisher under-delivery -
        // the enumeration proof does that, upstream. What it catches is a coding error in the
        // emission loop, including the realistic one of deciding per SUBJECT rather than per PAIR.
        var expected = requestedRequests.Count * askedPredicates.Count;
        var accounted = byPair.Count + absences.Count + gaps.Count;
        if (accounted != expected ||
            absencePairs.Count != absences.Count ||
            gapPairs.Count != gaps.Count)
        {
            refusal = LuxembourgOpinionRequestCoverageRefusal.MatrixPairNotRepresented;
            detail = $"{expected} pairs were asked about and {accounted} were accounted for.";
            return null;
        }

        var unrepresented = new List<string>();
        foreach (var request in requestedRequests)
        {
            foreach (var predicate in askedPredicates)
            {
                var key = (request, predicate);
                if (!byPair.ContainsKey(key) && !absencePairs.Contains(key) && !gapPairs.Contains(key))
                {
                    unrepresented.Add(request + " " + predicate);
                }
            }
        }

        if (unrepresented.Count is not 0)
        {
            refusal = LuxembourgOpinionRequestCoverageRefusal.MatrixPairNotRepresented;
            detail = "These pairs are represented by nothing: "
                + string.Join(", ", unrepresented.Take(12));
            return null;
        }

        refusal = LuxembourgOpinionRequestCoverageRefusal.None;
        return new LuxembourgOpinionRequestCoverage(
            requestedRequests, askedPredicates, present, retained, byPair, roleConfirmed, absences,
            gaps, unconfirmed, batch, inventory);
    }

    /// <summary>
    /// Whether the publisher's own delivery typed this subject an <c>OpinionRequest</c>.
    /// </summary>
    /// <remarks>
    /// What a traversal step must be given for its delivered-type-row requirement. Throws for a
    /// subject outside this batch: a caller must not read "not typed" out of a subject this run
    /// never asked about.
    /// </remarks>
    public bool RoleConfirmedFor(string requestIri)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestIri);
        if (!RequestedRequests.Contains(requestIri, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "This batch never asked about " + requestIri + ", so its role is not this "
                + "delivery's to report.",
                nameof(requestIri));
        }

        return _roleConfirmed.Contains(requestIri);
    }

    /// <summary>Every delivered value of one pair, in delivery order.</summary>
    /// <remarks>
    /// Returns an empty list for a pair this batch derived an absence for or left unresolved, and
    /// throws for a pair outside the matrix. A caller must not be able to read "the publisher holds
    /// nothing" out of a question this run never asked; the two empty answers inside the matrix are
    /// told apart through <see cref="DerivedAbsenceFor"/> and <see cref="UnresolvedGaps"/>.
    /// </remarks>
    public IReadOnlyList<LuxembourgOpinionRequestRecordView> ValuesFor(
        string requestIri,
        string predicateIri)
    {
        RequireInMatrix(requestIri, predicateIri);
        return _valueIndexesByPair.TryGetValue((requestIri, predicateIri), out var indexes)
            ? Array.AsReadOnly(indexes.Select(index => _present[index]).ToArray())
            : [];
    }

    /// <summary>The absence derived for one pair, or null if the pair is not an absence.</summary>
    public LuxembourgOpinionRequestObservedAbsence? DerivedAbsenceFor(
        string requestIri,
        string predicateIri)
    {
        RequireInMatrix(requestIri, predicateIri);
        return DerivedAbsences.FirstOrDefault(value =>
            string.Equals(value.RequestIri, requestIri, StringComparison.Ordinal) &&
            string.Equals(value.PredicateIri, predicateIri, StringComparison.Ordinal));
    }

    private void RequireInMatrix(string requestIri, string predicateIri)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestIri);
        ArgumentException.ThrowIfNullOrEmpty(predicateIri);
        if (!RequestedRequests.Contains(requestIri, StringComparer.Ordinal) ||
            !AskedPredicates.Contains(predicateIri, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "This batch never asked about " + requestIri + " " + predicateIri + ", so this "
                + "delivery says nothing either way about it.",
                nameof(requestIri));
        }
    }

    /// <summary>A one-line measured summary, in the units each number is actually in.</summary>
    public string Describe() =>
        $"requests={RequestedRequests.Count} covered_pairs={CoveredPairCount} "
        + $"present_pairs={PresentPairCount} admitted_rows={AdmittedRowCount} "
        + $"retained_rows={RetainedRows.Count} derived_absences={DerivedAbsences.Count} "
        + $"unresolved_gaps={UnresolvedGaps.Count} unconfirmed_role={RequestsOfUnconfirmedRole.Count}";
}
