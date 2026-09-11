using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;

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
/// <remarks>
/// SIX MEMBERS WERE REMOVED RATHER THAN LEFT UNREACHABLE, and each is named so nobody adds it back
/// as protection: a matrix over an unproven enumeration, a batch selection that is not the
/// citation's, a delivered predicate this family never asked about, a retained row carrying an
/// admitted predicate, a row accounted other than once, and evidence from another run. Every one of
/// them described a way a CALLER'S PROJECTION could disagree with the delivery, and there are no
/// caller projections here any more: the rows are the proof's, the admitted and retained halves are
/// derived from the plan, and the partition and profile are bound by the citation door, which
/// throws. A refusal nothing can reach reads as defence and is an untested claim.
/// </remarks>
public enum LuxembourgOpinionRequestCoverageRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// A delivered row's terms and its own proof-covered key disagree.
    /// </summary>
    /// <remarks>
    /// THE CHECK THAT MAKES THE TERMS EVIDENCE. An enumeration proof digests the canonical KEYS, not
    /// the terms beside them, so terms alone are a caller's restatement of a delivery - and a
    /// substituted <c>rdf:type</c> row of the right shape would otherwise confirm a role and mint
    /// an absence from evidence that never delivered it. The keys carry the subject, its kind, the
    /// predicate, the value's delivered digest, the value's kind, its datatype and its language, so
    /// requiring each term to describe its own key leaves nothing in a row unbound.
    /// </remarks>
    [JsonStringEnumMemberName("delivered_row_not_described_by_its_own_key")]
    DeliveredRowNotDescribedByItsOwnKey = 1,

    /// <summary>A delivered row names a request this batch never asked about.</summary>
    [JsonStringEnumMemberName("delivered_request_not_requested")]
    DeliveredRequestNotRequested = 2,

    /// <summary>Some requested pair ended represented by nothing.</summary>
    [JsonStringEnumMemberName("matrix_pair_not_represented")]
    MatrixPairNotRepresented = 3,

    /// <summary>One pair carries both delivered values and a derived conclusion about silence.</summary>
    [JsonStringEnumMemberName("pair_holds_present_and_derived_absence")]
    PairHoldsPresentAndDerivedAbsence = 4,
}

/// <summary>
/// The completed (requested requests x asked properties) matrix for one delivered batch.
/// </summary>
/// <remarks>
/// <para>
/// EVERYTHING IS DERIVED FROM THE DELIVERY THE PROOF PROVES. The only inputs are that proof, the
/// rows it proves, and the batch its inventory issued. There is no admitted list, no retained list,
/// no predicate list and no citation to pass: each was a caller projection that could disagree with
/// the delivery it claimed to describe, and a matrix built on one is a matrix about nothing.
/// </para>
/// <para>
/// THE TERMS ARE MADE EVIDENCE BY THEIR OWN KEYS. A proof digests canonical keys; the terms beside
/// them are not covered, so every row here must describe its own key before it is read - the same
/// discipline the draft producer applies at admission, held here because this is the door that
/// concludes something from a row.
/// </para>
/// <para>
/// COVERAGE IS COUNTED IN DISTINCT PAIRS AND NEVER IN ROWS. A multi-valued property delivers one row
/// per value, so rows exceed pairs whenever any subject holds several. Whether <c>referralDate</c>
/// is multi-valued on this publisher is UNMEASURED, so the shape that survives either answer is the
/// one built here, and the pair identity is checked as a set difference and not only as a sum.
/// </para>
/// <para>
/// THE DRAFT FAMILY'S UNCONFIRMED-SUBJECT ARITHMETIC DOES NOT TRANSFER. There, a subject is
/// confirmed by delivering any row at all, so an unconfirmed one has no present pairs and its pairs
/// can be accounted as a multiplication. Here confirmation comes from a PARTICULAR row, so a subject
/// can carry a delivered value and still be unconfirmed - and multiplying would then count its pairs
/// twice. Every pair is classified individually.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionRequestCoverage
{
    /// <summary>The profile this family's graph deliveries are read under.</summary>
    private static readonly RepeatedEnumerationInterpretationProfile GraphProfile =
        LuxembourgOpinionRequestGraphDiscoveryPlan.Create().CreateDeliveryProfile();

    private const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

    private const string BlankNodeKind =
        LuxembourgOpinionRequestInventoryDiscoveryPlan.UnsupportedBlankNodeKind;

    private const string UnboundKind = LuxembourgOpinionRequestGraphDiscoveryPlan.UnboundKind;

    /// <summary>
    /// The marker the plan's own BIND produces for a literal.
    /// </summary>
    /// <remarks>
    /// The one marker with no constant to alias, so it is pinned against the RENDERED TEMPLATE
    /// beside its three siblings rather than left to agree with itself.
    /// </remarks>
    private const string LiteralKind = "literal";

    private readonly Dictionary<(string Request, string Predicate), List<int>> _valueIndexesByPair;
    private readonly IReadOnlyList<LuxembourgOpinionRequestRecordView> _admitted;
    private readonly HashSet<string> _roleConfirmed;

    private LuxembourgOpinionRequestCoverage(
        IReadOnlyList<string> requestedRequests,
        IReadOnlyList<LuxembourgOpinionRequestRecordView> admitted,
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
        // public IReadOnlyList, which a caller can cast back to IList and write through.
        RequestedRequests = Array.AsReadOnly(requestedRequests.ToArray());
        _admitted = Array.AsReadOnly(admitted.ToArray());
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

    /// <summary>
    /// The properties this family asked about, from the plan that asked them.
    /// </summary>
    /// <remarks>
    /// NOT A PARAMETER, and it was one. A caller-supplied predicate set let a matrix and its derived
    /// absences be minted for a property the exact request-graph plan never designated as asked
    /// about - an absence over a question nobody put to the publisher.
    /// </remarks>
    public static IReadOnlyList<string> AskedPredicates =>
        LuxembourgOpinionRequestGraphDiscoveryPlan.AskedAbout;

    /// <summary>Pairs the publisher delivered at least one value for.</summary>
    public int PresentPairCount => _valueIndexesByPair.Count;

    /// <summary>Rows the publisher delivered and this family admitted. Never used as a pair count.</summary>
    public int AdmittedRowCount => _admitted.Count;

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

    /// <summary>
    /// Completes the matrix over a batch's proven delivery, or refuses without minting anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The citation is minted HERE, from the proof and the inventory-issued batch, rather than
    /// accepted: that door binds the delivery to this family's interpretation profile and to the
    /// partition it claims, and throws when it is neither. So a caller cannot pair a matrix with a
    /// citation of some other delivery, because there is no citation to pass.
    /// </para>
    /// <para>
    /// NO OBSERVATION INSTANT IS CHECKED, and its absence is deliberate. A caller-supplied instant
    /// copied into a property documented as an observation time is a provenance claim the caller
    /// cannot make; an absence minted here is datable through the run's own retained receipt, named
    /// by <see cref="LuxembourgOpinionRequestBatchCitation.AcquisitionRunRef"/>.
    /// </para>
    /// </remarks>
    /// <param name="proof">The batch's own enumeration proof.</param>
    /// <param name="deliveredRows">The rows that proof proves, terms and keys together.</param>
    /// <param name="assignment">The batch the proven inventory issued.</param>
    public static LuxembourgOpinionRequestCoverage? TryComplete(
        AbsenceFamilyEnumerationProof proof,
        IReadOnlyList<RepeatedEnumerationRow> deliveredRows,
        LuxembourgOpinionRequestBatchAssignment assignment,
        out LuxembourgOpinionRequestCoverageRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(deliveredRows);
        ArgumentNullException.ThrowIfNull(assignment);
        detail = null;

        var requestedRequests = assignment.Requests;
        var inventory = assignment.Inventory;

        // THE CITATION IS MINTED, NOT ACCEPTED. Its door binds the rows to the proof by canonical-key
        // digest, the delivery to this family's interpretation profile, and the partition to the one
        // the proof proves - and throws rather than refusing, because a delivery that is not this
        // family's is not a matrix that failed to complete.
        var batch = LuxembourgOpinionRequestBatchCitation.ForDelivery(proof, deliveredRows, assignment);

        var requested = requestedRequests.ToHashSet(StringComparer.Ordinal);
        var admissible = LuxembourgOpinionRequestGraphDiscoveryPlan.DirectlyAdmissiblePredicates
            .ToHashSet(StringComparer.Ordinal);

        var admitted = new List<LuxembourgOpinionRequestRecordView>();
        var retained = new List<LuxembourgOpinionRequestRecordView>();
        var byPair = new Dictionary<(string, string), List<int>>();
        var roleConfirmed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in deliveredRows)
        {
            // EVERY TERM DESCRIBES ITS OWN PROOF-COVERED KEY, or the row is not evidence of
            // anything. Checked before a single field is read.
            if (!DescribesItsOwnKey(row, out var subject, out var predicate, out var value,
                    out var valueKind, out var why))
            {
                refusal = LuxembourgOpinionRequestCoverageRefusal.DeliveredRowNotDescribedByItsOwnKey;
                detail = why;
                return null;
            }

            if (!requested.Contains(subject))
            {
                refusal = LuxembourgOpinionRequestCoverageRefusal.DeliveredRequestNotRequested;
                detail = $"A delivered row names {subject}, which this batch never asked about.";
                return null;
            }

            var view = new LuxembourgOpinionRequestRecordView(subject, predicate, value, valueKind);

            // ADMITTED OR RETAINED BY THE PLAN, never by which list a caller put the row in.
            if (!admissible.Contains(predicate))
            {
                // THE ROLE IS READ OFF THE DELIVERY. A retained rdf:type row whose value IS the IRI
                // of this family's class is the publisher answering what the query only asked; one
                // naming another class, or delivered as a literal spelling the class rather than
                // being it, answers it the other way and confirms nothing.
                if (string.Equals(
                        predicate,
                        LuxembourgOpinionRequestGraphDiscoveryPlan.RdfTypePredicateIri,
                        StringComparison.Ordinal) &&
                    string.Equals(valueKind, IriKind, StringComparison.Ordinal) &&
                    string.Equals(
                        value,
                        LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri,
                        StringComparison.Ordinal))
                {
                    roleConfirmed.Add(subject);
                }

                retained.Add(view);
                continue;
            }

            var key = (subject, predicate);
            if (!byPair.TryGetValue(key, out var bucket))
            {
                bucket = [];
                byPair[key] = bucket;
            }

            // A LIST per pair, never a set: the values ARE the fact, and a subject holding several
            // must keep all of them. The index is into the admitted rows, in delivery order.
            bucket.Add(admitted.Count);
            admitted.Add(view);
        }

        var askedPredicates = AskedPredicates;
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
            requestedRequests, admitted, retained, byPair, roleConfirmed, absences, gaps, unconfirmed,
            batch, inventory);
    }

    /// <summary>
    /// Reads a row's terms only if every one of them describes its own proof-covered key.
    /// </summary>
    /// <remarks>
    /// The plan binds <c>key_1</c> to <c>STR(?request)</c>, <c>key_2</c> to the subject's kind,
    /// <c>key_3</c> to <c>STR(?predicate)</c>, <c>key_4</c> to the publisher's own digest of the
    /// value, <c>key_5</c> to the value's kind, and <c>key_6</c> and <c>key_7</c> to the datatype
    /// and language or the empty string. Every one is checked, so nothing a row says is outside what
    /// the proof digested.
    /// <para>
    /// <c>key_4</c> IS THE PUBLISHER'S CURSOR CODEC AND NOT SHA-256 OF THE VALUE. This endpoint
    /// hashes values double UTF-8 encoded; recomputing through the named codec is what lets a
    /// delivered key be compared to the value beside it at all.
    /// </para>
    /// </remarks>
    private static bool DescribesItsOwnKey(
        RepeatedEnumerationRow row,
        out string subject,
        out string predicate,
        out string? value,
        out string valueKind,
        out string? why)
    {
        subject = string.Empty;
        predicate = string.Empty;
        value = null;
        valueKind = string.Empty;
        why = null;

        if (row is null || row.CanonicalKey.Count != GraphProfile.CanonicalKeyVariables.Count)
        {
            why = "A delivered row does not carry this family's keyset.";
            return false;
        }

        var subjectTerm = Term(row, "request");
        var predicateTerm = Term(row, "predicate");
        var valueTerm = Term(row, "value");

        if (subjectTerm.Kind is not RepeatedEnumerationRdfTermKind.Iri || subjectTerm.Value is null)
        {
            why = "A delivered row's subject is not a readable IRI.";
            return false;
        }

        if (predicateTerm.Kind is not RepeatedEnumerationRdfTermKind.Iri || predicateTerm.Value is null)
        {
            why = "A delivered row's predicate is not a readable IRI.";
            return false;
        }

        var expected = new[]
        {
            subjectTerm.Value,
            MarkerFor(subjectTerm),
            predicateTerm.Value,
            LuxembourgPublisherCursorCodec.ComputeKey(valueTerm.Value ?? string.Empty),
            MarkerFor(valueTerm),
            valueTerm.Datatype ?? string.Empty,
            valueTerm.Language ?? string.Empty,
        };

        for (var index = 0; index < expected.Length; index++)
        {
            if (!string.Equals(row.CanonicalKey[index].Value, expected[index], StringComparison.Ordinal))
            {
                why = $"A delivered row's key_{index + 1} does not describe the term beside it.";
                return false;
            }
        }

        subject = subjectTerm.Value;
        predicate = predicateTerm.Value;
        value = valueTerm.Value;
        valueKind = expected[4];
        return true;
    }

    /// <summary>The marker the plan's own BIND must have produced for a term of this kind.</summary>
    private static string MarkerFor(RepeatedEnumerationRdfTerm term) => term.Kind switch
    {
        RepeatedEnumerationRdfTermKind.Iri => IriKind,
        RepeatedEnumerationRdfTermKind.Literal => LiteralKind,
        RepeatedEnumerationRdfTermKind.BlankNode => BlankNodeKind,
        RepeatedEnumerationRdfTermKind.Unbound => UnboundKind,
        _ => throw new ArgumentOutOfRangeException(nameof(term)),
    };

    private static RepeatedEnumerationRdfTerm Term(RepeatedEnumerationRow row, string name)
    {
        for (var ordinal = 0; ordinal < GraphProfile.ProjectionVariables.Count; ordinal++)
        {
            if (string.Equals(GraphProfile.ProjectionVariables[ordinal], name, StringComparison.Ordinal))
            {
                return row.Terms[ordinal];
            }
        }

        throw new ArgumentException($"The delivery profile does not project {name}.", nameof(name));
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
            ? Array.AsReadOnly(indexes.Select(index => _admitted[index]).ToArray())
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
