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

/// <summary>The proven inventory a draft-graph batch partitions.</summary>
/// <remarks>
/// Carried into the batch run rather than discovered by it. A batch that cannot name the inventory
/// it partitions cannot honestly derive an absence from its own emptiness: "the publisher holds
/// nothing here" only means something against a subject set someone proved.
/// </remarks>
public sealed record LuxembourgInitialDraftInventoryCitation(
    string FamilyKey,
    SourceArtifactRef AcquisitionRunRef,
    string SelectionDigest);

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
    long DeliveredRowCount);

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
        LuxembourgDraftBatchCitation batch,
        LuxembourgInitialDraftInventoryCitation inventory)
    {
        RequestedDrafts = requestedDrafts;
        AskedPredicates = askedPredicates;
        _present = present;
        _valueIndexesByPair = valueIndexesByPair;
        DerivedAbsences = derivedAbsences;
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
    public static string SelectionDigestFor(IReadOnlyList<string> requestedDrafts)
    {
        ArgumentNullException.ThrowIfNull(requestedDrafts);
        var joined = string.Join('\n', requestedDrafts);
        return Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(joined)));
    }

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

        var folded = byPair.Values.Sum(static value => value.Count);
        if (folded != present.Count || Array.Exists(consumed, static value => !value) ||
            batch.DeliveredRowCount != present.Count)
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.PresentRowNotConsumedExactlyOnce;
            detail = $"The delivery carried {batch.DeliveredRowCount} rows and this matrix folded {folded}.";
            return null;
        }

        // EMITTED IN A DETERMINISTIC ORDER so two runs over one batch produce the same records.
        var absences = new List<LuxembourgDraftPropertyObservedAbsence>();
        var absencePairs = new HashSet<(string, string)>();
        foreach (var draft in requestedDrafts)
        {
            foreach (var predicate in askedPredicates)
            {
                var key = (draft, predicate);
                if (byPair.ContainsKey(key))
                {
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
        var expected = requestedDrafts.Count * askedPredicates.Count;
        if (absences.Count + byPair.Count != expected || absencePairs.Count != absences.Count)
        {
            refusal = LuxembourgDraftPropertyCoverageRefusal.MatrixPairNotRepresented;
            detail = $"{expected} pairs were asked about and "
                + $"{byPair.Count + absences.Count} were accounted for.";
            return null;
        }

        var unrepresented = new List<string>();
        foreach (var draft in requestedDrafts)
        {
            foreach (var predicate in askedPredicates)
            {
                var key = (draft, predicate);
                if (!byPair.ContainsKey(key) && !absencePairs.Contains(key))
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
            requestedDrafts, askedPredicates, present, byPair, absences, batch, inventory);
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
    }

    /// <summary>A one-line measured summary, in the units each number is actually in.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"publisher_rows={PublisherRowCount} distinct_present_pairs={PresentPairCount} "
        + $"derived_absences={DerivedAbsences.Count} covered_pairs={CoveredPairCount} "
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
