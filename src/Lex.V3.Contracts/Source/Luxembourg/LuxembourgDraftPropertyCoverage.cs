using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>Why this code concluded a draft holds no value for a property.</summary>
/// <remarks>
/// Closed, and with no <c>None</c> member: an absence with no reason is not a fact anyone can read.
/// One member today, because under the present-facts shape there is exactly one honest way to reach
/// an absence. The enum exists so that a second way cannot arrive untyped.
/// </remarks>
public enum LuxembourgDraftPropertyAbsenceReason
{
    /// <summary>
    /// This batch's enumeration was proven whole and delivered no row for this pair.
    /// </summary>
    [JsonStringEnumMemberName("enumerated_and_not_held")]
    EnumeratedAndNotHeld = 1,
}

/// <summary>
/// One (draft, property) pair this code concluded the publisher holds no value for.
/// </summary>
/// <remarks>
/// <para>
/// NOT A PUBLISHER ASSERTION, AND THE TYPE EXISTS TO MAKE THAT UNSAYABLE. A
/// <see cref="LuxembourgDraftPropertyRecord"/> is something Legilux returned. This is something
/// THIS CODE CONCLUDED, from a complete enumeration that returned nothing for the pair. The two are
/// separate types held in separate collections, because a single type carrying both would satisfy
/// S2-A03's demand that gaps be first-class by breaking S2-A01's demand that assertions be typed as
/// the publisher's.
/// </para>
/// <para>
/// It is never minted from a row. The publisher cannot say "nothing" - the query asks for present
/// facts with a mandatory triple, so a pair it holds nothing for simply produces no row - and a row
/// claiming to BE an absence would be this inference wearing the publisher's clothes. Every attempt
/// to ask the publisher for the absent case instead timed out; that is recorded on the plan.
/// </para>
/// <para>
/// EVERY INSTANCE CITES WHAT PROVES IT. The batch enumeration whose completeness makes the absence
/// meaningful, and the inventory the batch partitions. Without both, an absence is an assertion
/// about a corpus nobody can check.
/// </para>
/// </remarks>
public sealed class LuxembourgDraftPropertyObservedAbsence
{
    /// <summary>
    /// Minted only by <see cref="LuxembourgDraftPropertyCoverage"/>, and only for a pair the
    /// delivery did not carry.
    /// </summary>
    internal LuxembourgDraftPropertyObservedAbsence(
        string draftIri,
        string predicateIri,
        LuxembourgDraftPropertyAbsenceReason reason,
        LuxembourgDraftBatchCitation batch,
        LuxembourgInitialDraftInventoryCitation inventory)
    {
        DraftIri = draftIri;
        PredicateIri = predicateIri;
        Reason = reason;
        Batch = batch;
        Inventory = inventory;
    }

    public string DraftIri { get; }

    public string PredicateIri { get; }

    public LuxembourgDraftPropertyAbsenceReason Reason { get; }

    /// <summary>The batch enumeration whose proven completeness is what makes this an observation.</summary>
    public LuxembourgDraftBatchCitation Batch { get; }

    /// <summary>The proven inventory this batch is a partition of.</summary>
    public LuxembourgInitialDraftInventoryCitation Inventory { get; }
}

/// <summary>Why a pair could not be resolved either way by this delivery.</summary>
public enum LuxembourgDraftPropertyGapReason
{
    /// <summary>
    /// The predicate is declared on another class, so a draft-property delivery could never have
    /// carried it and its absence here evidences nothing.
    /// </summary>
    [JsonStringEnumMemberName("declared_on_another_class")]
    DeclaredOnAnotherClass = 1,
}

/// <summary>
/// One (draft, property) pair this delivery can resolve neither as a value nor as an absence.
/// </summary>
/// <remarks>
/// <para>
/// THE THIRD HONEST ANSWER, and it exists because the other two would both be lies here. There is
/// no delivered value, so it is not a fact; and the delivery could never have carried one, so its
/// silence is not evidence of absence. A delivery can only evidence the absence of something it
/// could have carried.
/// </para>
/// <para>
/// It is typed rather than dropped because a pair that simply vanished would be the false absence
/// read back as "we never asked" - and this family DID ask, of the wrong subject. The gap stands
/// until the traversal that could answer it is proven.
/// </para>
/// </remarks>
public sealed class LuxembourgDraftPropertyUnresolvedGap
{
    internal LuxembourgDraftPropertyUnresolvedGap(
        string draftIri,
        string predicateIri,
        LuxembourgDraftPropertyGapReason reason,
        string declaredOnClassIri,
        LuxembourgDraftBatchCitation batch,
        LuxembourgInitialDraftInventoryCitation inventory)
    {
        DraftIri = draftIri;
        PredicateIri = predicateIri;
        Reason = reason;
        DeclaredOnClassIri = declaredOnClassIri;
        Batch = batch;
        Inventory = inventory;
    }

    public string DraftIri { get; }

    public string PredicateIri { get; }

    public LuxembourgDraftPropertyGapReason Reason { get; }

    /// <summary>The class that does declare it, and therefore where an answer would come from.</summary>
    public string DeclaredOnClassIri { get; }

    public LuxembourgDraftBatchCitation Batch { get; }

    public LuxembourgInitialDraftInventoryCitation Inventory { get; }
}

/// <summary>The proven inventory a draft-graph batch partitions.</summary>
/// <remarks>
/// <para>
/// Carried into the batch run rather than discovered by it. A batch that cannot name the inventory
/// it partitions cannot honestly derive an absence from its own emptiness: "the publisher holds
/// nothing here" only means something against a subject set someone proved.
/// </para>
/// <para>
/// MINTED BY THE RUN THAT EARNED IT, never assembled by a caller. Every field comes from that run's
/// own enumeration proof and delivery, so a citation cannot name an inventory nobody produced -
/// which is what a hand-assembled one could do, and did while this was passed in by hand.
/// </para>
/// <para>
/// <see cref="SelectionDigest"/> digests the addressable population the inventory hands to
/// batching, so it changes when the population changes; <see cref="SubjectCount"/> is that
/// population's size, and the cover checks the batches against both.
/// </para>
/// </remarks>
public sealed record LuxembourgInitialDraftInventoryCitation(
    string FamilyKey,
    SourceArtifactRef AcquisitionRunRef,
    string SelectionDigest,
    int SubjectCount,
    string ObservedAt);

/// <summary>The exact batch enumeration an absence is derived from.</summary>
/// <remarks>
/// <see cref="SelectionDigest"/> is what lets a detached absence record prove the pair was ASKED
/// about: it digests the requested members themselves, so it changes when the batch changes. The
/// acquisition run ref pins which run delivered them.
/// </remarks>
public sealed record LuxembourgDraftBatchCitation(
    SourceArtifactRef AcquisitionRunRef,
    string SelectionDigest,
    int RequestedDraftCount,
    long DeliveredRowCount,
    string PartitionKey,
    string ObservedAt);

/// <summary>Why a completed matrix could not be built over a delivered batch.</summary>
public enum LuxembourgDraftPropertyCoverageRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The stage was reached without a proven, complete enumeration to derive from.</summary>
    [JsonStringEnumMemberName("matrix_completion_over_unproven_enumeration")]
    MatrixCompletionOverUnprovenEnumeration = 1,

    /// <summary>No inventory citation was supplied, so an absence would name no corpus.</summary>
    [JsonStringEnumMemberName("inventory_evidence_not_supplied")]
    InventoryEvidenceNotSupplied = 2,

    /// <summary>The requested batch was not retained in a comparable form.</summary>
    [JsonStringEnumMemberName("requested_batch_not_retained")]
    RequestedBatchNotRetained = 3,

    /// <summary>A delivered record names a draft this batch never asked about.</summary>
    [JsonStringEnumMemberName("delivered_draft_not_requested")]
    DeliveredDraftNotRequested = 4,

    /// <summary>A delivered record names a property this family never asked about.</summary>
    [JsonStringEnumMemberName("delivered_predicate_not_asked_about")]
    DeliveredPredicateNotAskedAbout = 5,

    /// <summary>A present publisher row was folded zero times or more than once.</summary>
    [JsonStringEnumMemberName("present_row_not_consumed_exactly_once")]
    PresentRowNotConsumedExactlyOnce = 6,

    /// <summary>Some requested pair ended with neither a present value nor a derived absence.</summary>
    [JsonStringEnumMemberName("matrix_pair_not_represented")]
    MatrixPairNotRepresented = 7,

    /// <summary>One pair carries both present values and a derived absence.</summary>
    [JsonStringEnumMemberName("pair_holds_present_and_derived_absence")]
    PairHoldsPresentAndDerivedAbsence = 8,

    /// <summary>
    /// The enumeration proof in hand is not this batch's own, or carries no observation instant.
    /// </summary>
    /// <remarks>
    /// The partition key travels out with the bound request and back through the retained delivery,
    /// so comparing it against a key recomputed here from the requested members checks that the
    /// request actually sent named these drafts. It became a real check only when the key started
    /// digesting the batch: while every batch bound the same constant, it compared a constant with
    /// itself and would have read as protection while proving nothing.
    /// </remarks>
    [JsonStringEnumMemberName("absence_evidence_not_from_this_run")]
    AbsenceEvidenceNotFromThisRun = 9,
}

/// <summary>
/// The completed (requested drafts x asked properties) matrix for one delivered batch.
/// </summary>
/// <remarks>
/// <para>
/// COVERAGE IS COUNTED IN DISTINCT PAIRS AND NEVER IN ROWS. A multi-valued property delivers one
/// row per value, so rows exceed pairs whenever any draft holds several values: the retained
/// fifty-draft delivery carried 103 rows over 95 distinct pairs, the eight-row difference being one
/// draft that transposes nine directives. Subtracting a ROW count from a PAIR total would have
/// under-counted the absences by exactly that difference and left eight pairs represented by
/// nothing - which is why the identity below is checked as a set difference and not only as a sum.
/// </para>
/// <para>
/// The requested set cannot be recovered from the delivery. Under the present-facts shape a draft
/// holding none of the five properties delivers no rows at all, so the drafts that were ASKED about
/// have to be carried in from the run. That is the whole reason this type takes them as an input
/// rather than reading them off the records.
/// </para>
/// </remarks>
public sealed class LuxembourgDraftPropertyCoverage
{
    private readonly Dictionary<(string Draft, string Predicate), List<int>> _valueIndexesByPair;
    private readonly IReadOnlyList<LuxembourgDraftPropertyRecordView> _present;

    private LuxembourgDraftPropertyCoverage(
        IReadOnlyList<string> requestedDrafts,
        IReadOnlyList<string> askedPredicates,
        IReadOnlyList<LuxembourgDraftPropertyRecordView> present,
        Dictionary<(string, string), List<int>> valueIndexesByPair,
        IReadOnlyList<LuxembourgDraftPropertyObservedAbsence> derivedAbsences,
        IReadOnlyList<LuxembourgDraftPropertyUnresolvedGap> unresolvedGaps,
        IReadOnlyList<string> draftsOfUnconfirmedClass,
        LuxembourgDraftBatchCitation batch,
        LuxembourgInitialDraftInventoryCitation inventory)
    {
        RequestedDrafts = requestedDrafts;
        AskedPredicates = askedPredicates;
        _present = present;
        _valueIndexesByPair = valueIndexesByPair;
        DerivedAbsences = derivedAbsences;
        UnresolvedGaps = unresolvedGaps;
        DraftsOfUnconfirmedClass = draftsOfUnconfirmedClass;
        Batch = batch;
        Inventory = inventory;
    }

    public IReadOnlyList<string> RequestedDrafts { get; }

    public IReadOnlyList<string> AskedPredicates { get; }

    /// <summary>Pairs the publisher delivered at least one value for.</summary>
    public int PresentPairCount => _valueIndexesByPair.Count;

    /// <summary>Rows the publisher delivered. Not a pair count, and never used as one.</summary>
    public int PublisherRowCount => _present.Count;

    public IReadOnlyList<LuxembourgDraftPropertyObservedAbsence> DerivedAbsences { get; }

    /// <summary>
    /// Pairs this delivery can resolve neither as a value nor as an absence.
    /// </summary>
    /// <remarks>
    /// A predicate declared on another class could never have been carried by a draft-property
    /// delivery, so its silence evidences nothing. The pair is represented here rather than derived
    /// as an absence or dropped.
    /// </remarks>
    public IReadOnlyList<LuxembourgDraftPropertyUnresolvedGap> UnresolvedGaps { get; }

    /// <summary>
    /// Requested drafts this delivery cannot confirm are still <c>InitialDraft</c>, and for which no
    /// absence is derived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PUBLISHER DRIFT IS RECORDED, NEVER CONVERTED INTO ABSENCE. The query joins
    /// <c>?draft a jolux:InitialDraft</c> BEFORE the mandatory value triple, so a subject that has
    /// since left the class delivers zero rows for all five properties - identical, from here, to a
    /// subject still in the class that simply holds none of them. Those are different facts about
    /// the publisher and this delivery cannot tell them apart.
    /// </para>
    /// <para>
    /// So the five pairs of such a draft are represented HERE rather than as five asserted absences.
    /// Deriving absences for them would be the false absence S2-A03 forbids, arriving through drift
    /// rather than through an incomplete enumeration. A draft that delivered even one row is
    /// confirmed: the class triple was satisfied for it.
    /// </para>
    /// <para>
    /// Settling it needs a question this batch does not ask - whether each requested subject still
    /// satisfies the class triple - which is a separate observation and a separate request.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> DraftsOfUnconfirmedClass { get; }

    public LuxembourgDraftBatchCitation Batch { get; }

    public LuxembourgInitialDraftInventoryCitation Inventory { get; }

    /// <summary>Every pair this batch accounts for, present or absent.</summary>
    public int CoveredPairCount => RequestedDrafts.Count * AskedPredicates.Count;

    /// <summary>
    /// The digest of a batch's requested members, over which an absence is meaningful.
    /// </summary>
    /// <remarks>
    /// Computed over the deduplicated, ordinal-sorted, UNPADDED members. The padded form repeats
    /// its last member to fill the query's fixed slot count, so digesting it would give two
    /// different batches the same digest whenever both padded to the same tail.
    /// </remarks>
    public static string SelectionDigestFor(IReadOnlyList<string> requestedDrafts) =>
        LuxembourgDraftGraphDiscoveryPlan.SelectionDigestFor(requestedDrafts);

    /// <summary>
    /// Completes the matrix over a delivered batch, or refuses without minting anything.
    /// </summary>
    /// <remarks>
    /// Every precondition is asked before the first absence is constructed, so a refusal cannot
    /// leave half a matrix behind for a caller to read as a whole one.
    /// </remarks>
    public static LuxembourgDraftPropertyCoverage? TryComplete(
        IReadOnlyList<string> requestedDrafts,
        IReadOnlyList<string> askedPredicates,
        IReadOnlyList<LuxembourgDraftPropertyRecordView> present,
        LuxembourgDraftBatchCitation? batch,
        LuxembourgInitialDraftInventoryCitation? inventory,
        int retainedNotAdmittedRows,
        IReadOnlyDictionary<string, string> predicatesDeclaredElsewhere,
        out LuxembourgDraftPropertyCoverageRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(requestedDrafts);
        ArgumentNullException.ThrowIfNull(askedPredicates);
        ArgumentNullException.ThrowIfNull(present);
        detail = null;

        // AN ABSENCE MAY ONLY BE DERIVED FROM A PROVEN, COMPLETE ENUMERATION. The batch citation is
        // minted from the enumeration proof and cannot be built without one, so this is the
        // structural form of "no absence from a refused or incomplete enumeration": the caller has
        // nothing to pass here unless a proof existed.
        if (batch is null)
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.MatrixCompletionOverUnprovenEnumeration;
            detail = "A derived absence means nothing without the enumeration that proves it.";
            return null;
        }

        if (inventory is null)
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.InventoryEvidenceNotSupplied;
            detail = "An absence names a corpus, so the inventory this batch partitions must be cited.";
            return null;
        }

        // The requested set must be exactly the canonical form the digest was taken over, or the
        // citation on every absence would describe a different question from the one asked.
        if (requestedDrafts.Count is 0 ||
            !string.Equals(SelectionDigestFor(requestedDrafts), batch.SelectionDigest, StringComparison.Ordinal) ||
            requestedDrafts.Count != batch.RequestedDraftCount)
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.RequestedBatchNotRetained;
            detail = "The requested batch does not match the selection its own citation names.";
            return null;
        }

        // THE PROOF IN HAND MUST BE THIS BATCH'S OWN, and an absence must be datable.
        if (!string.Equals(
                LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(requestedDrafts),
                batch.PartitionKey,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(batch.ObservedAt))
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.AbsenceEvidenceNotFromThisRun;
            detail = "The delivery's own partition key does not name these drafts, or the run "
                + "carries no observation instant.";
            return null;
        }

        var requested = requestedDrafts.ToHashSet(StringComparer.Ordinal);
        var asked = askedPredicates.ToHashSet(StringComparer.Ordinal);
        if (requested.Count != requestedDrafts.Count || asked.Count != askedPredicates.Count)
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.RequestedBatchNotRetained;
            detail = "A batch names each draft once and each property once.";
            return null;
        }

        // EVERY PRESENT ROW IS CONSUMED EXACTLY ONCE, tracked per row rather than inferred from a
        // total. A LIST per pair, never a set: the values ARE the fact, and a draft transposing nine
        // directives must keep nine of them.
        var byPair = new Dictionary<(string, string), List<int>>();
        var consumed = new bool[present.Count];
        for (var index = 0; index < present.Count; index++)
        {
            var record = present[index];
            if (!requested.Contains(record.DraftIri))
            {
                refusal = LuxembourgDraftPropertyCoverageRefusal.DeliveredDraftNotRequested;
                detail = $"A delivered row names {record.DraftIri}, which this batch never asked about.";
                return null;
            }

            if (!asked.Contains(record.PredicateIri))
            {
                refusal = LuxembourgDraftPropertyCoverageRefusal.DeliveredPredicateNotAskedAbout;
                detail = $"A delivered row names {record.PredicateIri}, which this family never asked about.";
                return null;
            }

            var key = (record.DraftIri, record.PredicateIri);
            if (!byPair.TryGetValue(key, out var bucket))
            {
                bucket = [];
                byPair[key] = bucket;
            }

            bucket.Add(index);
            consumed[index] = true;
        }

        // CONSERVATION OVER THE WHOLE DELIVERY, not just over the admitted half. The broad
        // acquisition carries every predicate the publisher holds about these subjects, so a
        // delivered row is either admitted here and folded exactly once, or retained by name as
        // evidence this family asserts nothing about. A row that is neither has gone missing
        // between the page and this matrix, and it would be invisible in every count below.
        var folded = byPair.Values.Sum(static value => value.Count);
        if (folded != present.Count || Array.Exists(consumed, static value => !value) ||
            retainedNotAdmittedRows < 0 ||
            batch.DeliveredRowCount != present.Count + retainedNotAdmittedRows)
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.PresentRowNotConsumedExactlyOnce;
            detail = $"The delivery carried {batch.DeliveredRowCount} rows; this matrix folded "
                + $"{folded} admitted and {retainedNotAdmittedRows} were retained unadmitted.";
            return null;
        }

        // A DRAFT THAT DELIVERED NOTHING IS NOT A DRAFT THAT HOLDS NOTHING. The class triple is
        // joined before the value triple, so a subject that left InitialDraft is silent in exactly
        // the same way as one that is still in it and holds none of the five properties. Absence is
        // derived only for the drafts this delivery confirms - the ones that delivered a row.
        var confirmed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (draft, _) in byPair.Keys)
        {
            confirmed.Add(draft);
        }

        var unconfirmed = requestedDrafts.Where(value => !confirmed.Contains(value)).ToArray();

        // EMITTED IN A DETERMINISTIC ORDER so two runs over one batch produce the same records.
        var absences = new List<LuxembourgDraftPropertyObservedAbsence>();
        var absencePairs = new HashSet<(string, string)>();
        var gaps = new List<LuxembourgDraftPropertyUnresolvedGap>();
        var gapPairs = new HashSet<(string, string)>();
        foreach (var draft in requestedDrafts)
        {
            if (!confirmed.Contains(draft))
            {
                continue;
            }

            foreach (var predicate in askedPredicates)
            {
                var key = (draft, predicate);
                if (byPair.ContainsKey(key))
                {
                    continue;
                }

                // A DELIVERY CAN ONLY EVIDENCE THE ABSENCE OF SOMETHING IT COULD HAVE CARRIED. A
                // predicate declared on another class was never askable of this subject, so its
                // silence here is not evidence - it is an unresolved gap, and saying so is the only
                // honest third answer.
                if (predicatesDeclaredElsewhere.TryGetValue(predicate, out var declaringClass))
                {
                    gaps.Add(new LuxembourgDraftPropertyUnresolvedGap(
                        draft, predicate, LuxembourgDraftPropertyGapReason.DeclaredOnAnotherClass,
                        declaringClass, batch, inventory));
                    gapPairs.Add(key);
                    continue;
                }

                absences.Add(new LuxembourgDraftPropertyObservedAbsence(
                    draft,
                    predicate,
                    LuxembourgDraftPropertyAbsenceReason.EnumeratedAndNotHeld,
                    batch,
                    inventory));
                absencePairs.Add(key);
            }
        }

        // A PAIR CANNOT BE BOTH. Checked against the present set rather than trusted to the loop
        // above, because the loop's correctness is exactly what would be broken by a future edit.
        if (absencePairs.Overlaps(byPair.Keys))
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.PairHoldsPresentAndDerivedAbsence;
            detail = "A pair carries both delivered values and a derived absence.";
            return null;
        }

        // EVERY REQUESTED PAIR IS REPRESENTED, recomputed rather than declared. The count identity
        // alone would be satisfied by one duplicate absence beside one missing pair, so the set
        // difference is checked too and the unrepresented pairs are named rather than dropped.
        // EVERY REQUESTED PAIR IS REPRESENTED, as a delivered value, a derived absence, or a draft
        // whose class this delivery could not confirm. Recomputed rather than declared.
        //
        // HONEST ABOUT WHAT THIS CAN CATCH: over sets, absences are the complement of the present
        // pairs by construction, so this identity cannot detect publisher under-delivery - the
        // enumeration proof does that, upstream. What it catches is a coding error in the emission
        // loop, and mutation C6 (absence decided per DRAFT rather than per PAIR - the realistic form
        // of the arithmetic error this design exists to prevent) is killed here.
        var expected = requestedDrafts.Count * askedPredicates.Count;
        var accounted = byPair.Count + absences.Count + gaps.Count
            + (unconfirmed.Length * askedPredicates.Count);
        if (accounted != expected || absencePairs.Count != absences.Count)
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.MatrixPairNotRepresented;
            detail = $"{expected} pairs were asked about and {accounted} were accounted for.";
            return null;
        }

        var unrepresented = new List<string>();
        foreach (var draft in requestedDrafts)
        {
            if (!confirmed.Contains(draft))
            {
                continue;
            }

            foreach (var predicate in askedPredicates)
            {
                var key = (draft, predicate);
                if (!byPair.ContainsKey(key) && !absencePairs.Contains(key) && !gapPairs.Contains(key))
                {
                    unrepresented.Add(draft + " " + predicate);
                }
            }
        }

        if (unrepresented.Count is not 0)
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.MatrixPairNotRepresented;
            detail = "These pairs are represented by nothing: " + string.Join(", ", unrepresented.Take(12));
            return null;
        }

        refusal = LuxembourgDraftPropertyCoverageRefusal.None;
        return new LuxembourgDraftPropertyCoverage(
            requestedDrafts, askedPredicates, present, byPair, absences, gaps, unconfirmed, batch,
            inventory);
    }

    /// <summary>Every delivered value of one pair, in delivery order.</summary>
    /// <remarks>
    /// Returns an empty list ONLY for a pair this batch derived an absence for, and throws for a
    /// pair outside the matrix. A caller must not be able to read "the publisher holds nothing"
    /// out of a question this run never asked.
    /// </remarks>
    public IReadOnlyList<LuxembourgDraftPropertyRecordView> ValuesFor(string draftIri, string predicateIri)
    {
        RequireInMatrix(draftIri, predicateIri);
        return _valueIndexesByPair.TryGetValue((draftIri, predicateIri), out var bucket)
            ? bucket.Select(index => _present[index]).ToArray()
            : [];
    }

    /// <summary>The derived absence for one pair, or null where the publisher delivered values.</summary>
    public LuxembourgDraftPropertyObservedAbsence? DerivedAbsenceFor(string draftIri, string predicateIri)
    {
        RequireInMatrix(draftIri, predicateIri);
        return DerivedAbsences.FirstOrDefault(value =>
            string.Equals(value.DraftIri, draftIri, StringComparison.Ordinal) &&
            string.Equals(value.PredicateIri, predicateIri, StringComparison.Ordinal));
    }

    private void RequireInMatrix(string draftIri, string predicateIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(predicateIri);
        if (!RequestedDrafts.Contains(draftIri, StringComparer.Ordinal) ||
            !AskedPredicates.Contains(predicateIri, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(draftIri),
                "This batch never asked about that pair, and an unasked question has no answer - "
                    + "not even an empty one. Reading one here would be the false absence this "
                    + "whole type exists to prevent.");
        }

        // The same rule for an unresolved gap: no value and no absence, because the delivery could
        // never have carried the predicate. Answering empty would turn "we asked the wrong subject"
        // into "the publisher holds nothing", which is the false absence by a third route.
        if (UnresolvedGaps.Any(value =>
                string.Equals(value.DraftIri, draftIri, StringComparison.Ordinal) &&
                string.Equals(value.PredicateIri, predicateIri, StringComparison.Ordinal)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(predicateIri),
                "That property is declared on another class, so this delivery resolves it neither "
                    + "way. See UnresolvedGaps.");
        }

        // The same rule for a draft whose class this delivery could not confirm: it has no values
        // AND no derived absence, so answering either would let a caller read a fact out of a
        // subject the run never established was still in the class.
        if (DraftsOfUnconfirmedClass.Contains(draftIri, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(draftIri),
                "This delivery could not confirm that draft is still an InitialDraft, so it holds "
                    + "neither values nor an absence for it. See DraftsOfUnconfirmedClass.");
        }
    }

    /// <summary>A one-line measured summary, in the units each number is actually in.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"publisher_rows={PublisherRowCount} distinct_present_pairs={PresentPairCount} "
        + $"derived_absences={DerivedAbsences.Count} covered_pairs={CoveredPairCount} "
        + $"unresolved_gaps={UnresolvedGaps.Count} "
        + $"unconfirmed_drafts={DraftsOfUnconfirmedClass.Count} "
        + $"total_records={PublisherRowCount + DerivedAbsences.Count}");
}

/// <summary>The parts of a delivered record this matrix reads.</summary>
/// <remarks>
/// A narrow view rather than the ingest record itself, so the contracts assembly can own the
/// coverage rule without depending on the producer that decodes rows.
/// </remarks>
public sealed record LuxembourgDraftPropertyRecordView(
    string DraftIri,
    string PredicateIri,
    string? Value,
    string ValueKind);
