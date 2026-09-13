using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why the per-act consolidation run produced no answer for its act. Closed.</summary>
public enum LuxembourgConsolidationByActRefusal
{
    /// <summary>No refusal: the consolidations of this act were delivered (possibly none).</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The enumeration itself was refused before any row existed.</summary>
    [JsonStringEnumMemberName("enumeration_refused")]
    EnumerationRefused = 1,

    /// <summary>The run delivered but its whole enumeration was not proven.</summary>
    [JsonStringEnumMemberName("enumeration_proof_refused")]
    EnumerationProofRefused = 2,

    /// <summary>The proven pages would not reopen into verified rows.</summary>
    /// <remarks>
    /// Distinct from <see cref="EnumerationProofRefused"/>: the enumeration was proven and the
    /// failure is later, in re-deriving each page's rows from its retained bytes.
    /// </remarks>
    [JsonStringEnumMemberName("verified_rows_refused")]
    VerifiedRowsRefused = 3,

    /// <summary>
    /// A delivered row could not be read: a wrong term count, a subject of a kind RDF cannot put in
    /// subject position, a marker that is not the query's own plain literal, a marker disagreeing
    /// with its term, a non-positive multiplicity, or a cursor key that does not key its own terms.
    /// </summary>
    [JsonStringEnumMemberName("row_not_admitted")]
    RowNotAdmitted = 4,

    /// <summary>
    /// A delivered row's projected <c>act</c> column is not the act this run asked about.
    /// </summary>
    /// <remarks>
    /// The sixth hostile case the design review named. The act is bound into the query through
    /// <c>VALUES ?act</c> and projected, so every real row carries the act the publisher's own triple
    /// joined against. A row naming another act is a delivery this run cannot read as its act's
    /// consolidations, and it refuses the whole delivery rather than dropping the row - a dropped row
    /// would make an under-count look like a complete answer.
    /// </remarks>
    [JsonStringEnumMemberName("row_names_another_act")]
    RowNamesAnotherAct = 5,

    /// <summary>
    /// The same coordinated text arrived twice.
    /// </summary>
    /// <remarks>
    /// The query groups by the coordinated text and its kind, so an honest delivery names each
    /// exactly once. It is refused rather than deduplicated, because a publisher that grouped a
    /// subject and still delivered it twice did not answer the question this family asked.
    /// </remarks>
    [JsonStringEnumMemberName("consolidation_delivered_twice")]
    ConsolidationDeliveredTwice = 6,
}

/// <summary>
/// One coordinated text that consolidates the act, exactly as the publisher delivered it.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is the term's own lexical form and <see cref="Kind"/> is the publisher's word
/// for what it is. A blank-node coordinated text is still a real consolidation - its presence means
/// the act IS consolidated - so it is carried typed rather than dropped; unlike the inventory
/// families this one addresses nothing downstream by these values, so a non-addressable kind is not
/// a reason to refuse, only a fact to carry.
/// </remarks>
public sealed record LuxembourgActConsolidation(
    string Value,
    string Kind,
    long Multiplicity,
    string SourceObservationId);

/// <summary>The consolidations of one act, or one typed refusal. Never both.</summary>
public sealed class LuxembourgConsolidationByActResult
{
    private LuxembourgConsolidationByActResult(
        string act,
        IReadOnlyList<LuxembourgActConsolidation>? consolidations,
        AbsenceFamilyEnumerationProof? proof,
        LuxembourgConsolidationByActRefusal refusal,
        string? detail,
        int productRequestCount,
        WireBudgetSnapshot wireBudget)
    {
        Act = act;
        // SNAPSHOTTED, NOT ALIASED, for the reason the inventory result is: an IReadOnlyList handed
        // out over the producer's own List can be cast back to and mutated, which would change what
        // a consumer counts while the proof kept describing the original delivery.
        Consolidations = consolidations is null ? null : Array.AsReadOnly(consolidations.ToArray());
        Proof = proof;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
        WireBudget = wireBudget;
    }

    /// <summary>The act this run asked about, in the one admitted spelling.</summary>
    public string Act { get; }

    /// <summary>
    /// Every coordinated text that consolidates the act, in the publisher's own delivery order.
    /// </summary>
    /// <remarks>
    /// An empty list is an answer, not a refusal: it means the publisher returned no subject
    /// asserting <c>consolidates</c> against this act, in this run, under this profile. It is never
    /// the terminal "never consolidated in law" claim - that rests on the frame and a bounded live
    /// acceptance neither of which this slice builds.
    /// </remarks>
    public IReadOnlyList<LuxembourgActConsolidation>? Consolidations { get; }

    /// <summary>
    /// The exact enumeration proof this act's consolidations were read from. Non-null exactly when
    /// delivered.
    /// </summary>
    /// <remarks>
    /// THE PROOF ITSELF, not a reference off it, and this is a repair. Review of the first slice-2
    /// head found the result carried only <see cref="AbsenceFamilyEnumerationProof.AcquisitionRunRef"/>,
    /// which is insufficient for the one consumer this slice feeds: the merged
    /// <c>LuxembourgNeverConsolidatedEntry</c> requires an <see cref="AbsenceFamilyEnumerationProof"/>
    /// and reads its <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/> to admit a
    /// delivered-no-rows or delivered-rows disposition. A run reference cannot reconstruct the
    /// proof's family key, row count, canonical-key digest, profile refs or retention floor, so the
    /// frame could not turn a successful result into an evidence-bound entry without rerunning. The
    /// exact proof passed to <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> is carried here
    /// unchanged, by object identity.
    /// </remarks>
    public AbsenceFamilyEnumerationProof? Proof { get; }

    /// <summary>The acquisition run every consolidation cites, read off <see cref="Proof"/>.</summary>
    /// <remarks>
    /// Derived rather than stored, so it cannot disagree with the proof it is supposed to name.
    /// Non-null exactly when delivered.
    /// </remarks>
    public SourceArtifactRef? CompletionEvidenceRef => Proof?.AcquisitionRunRef;

    public LuxembourgConsolidationByActRefusal Refusal { get; }
    public string? Detail { get; }
    public int ProductRequestCount { get; }

    /// <summary>What the shared wire budget stood at when this run ended. Always present.</summary>
    public WireBudgetSnapshot WireBudget { get; }

    public bool Delivered => Refusal == LuxembourgConsolidationByActRefusal.None;

    internal static LuxembourgConsolidationByActResult Success(
        string act,
        IReadOnlyList<LuxembourgActConsolidation> consolidations,
        AbsenceFamilyEnumerationProof proof,
        int productRequestCount,
        WireBudgetSnapshot wireBudget) =>
        new(act, consolidations, proof,
            LuxembourgConsolidationByActRefusal.None, null, productRequestCount, wireBudget);

    internal static LuxembourgConsolidationByActResult Refused(
        string act,
        LuxembourgConsolidationByActRefusal refusal,
        string detail,
        int productRequestCount,
        WireBudgetSnapshot wireBudget) =>
        new(act, null, null, refusal, detail, productRequestCount, wireBudget);
}

/// <summary>
/// Runs the per-act consolidation family for one act and reads its proven rows.
/// </summary>
/// <remarks>
/// <para>
/// THE PRODUCER OWNS THE RUN, and it owns one thing the inventory producers do not have to: the act
/// the answer is ABOUT. #419's whole subject is the claim "this act was never consolidated", so the
/// answer's binding to an act cannot rest on the run having been asked nicely. The family key
/// carries the act (slice 1), and this producer proves the enumeration against the key it re-derives
/// from the act it was asked about - so a delivery whose partition is another act's key cannot be
/// proven here at all, the consuming-end check that makes the carried act more than a label. Then,
/// because a key is only a name, it requires every delivered row's projected <c>act</c> column to be
/// the act too, before any row becomes a consolidation.
/// </para>
/// <para>
/// WHAT THIS SLICE DOES NOT DO is count, or decide what "never consolidated" means. It reads the
/// consolidations of one act as the publisher delivered them; the counting rules, the class frame
/// and the never-list are slice 3, and the bounded live acceptance that would let a real count be
/// believed is later still. No traffic is authorized by this producer's existence.
/// </para>
/// </remarks>
public sealed class LuxembourgConsolidationByActProducer
{
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public LuxembourgConsolidationByActProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal LuxembourgConsolidationByActProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    /// <summary>Runs the family for one act and reads its consolidations. The only public door.</summary>
    public async Task<LuxembourgConsolidationByActResult> RunAsync(
        LuxembourgConsolidationByActRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var act = request.PublisherActIri;
        var run = await _executor.RunLuxembourgConsolidationByActAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);
        // READ ONCE, HERE, for the reason the inventory producer states: everything below reads
        // custody, not the wire, so this is the last moment the budget changes on this run's account.
        var wireBudget = WireBudgetSnapshot.Of(request.WireBudget);
        if (run.Receipt is not { } receipt)
        {
            return LuxembourgConsolidationByActResult.Refused(
                act,
                LuxembourgConsolidationByActRefusal.EnumerationRefused,
                run.Refusal is { } refusal
                    ? refusal.Code + (refusal.CoreRefusalDetail is { Length: > 0 } detail
                        ? ": " + detail
                        : string.Empty)
                    : "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount,
                wireBudget);
        }

        // THE PROOF IS TAKEN AGAINST THE KEY RE-DERIVED FROM THE ACT, not against whatever partition
        // the delivery happens to carry. slice 1 mints the key from the act; passing that key to
        // TryProveFamilyEnumeration is what makes the binding load-bearing at the consuming end,
        // because AbsenceFamilyEnumerationProof.TryCreate refuses any delivery whose own partition is
        // not the key it is proven under. So a run whose delivery names another act cannot be proven
        // here at all - it refuses as EnumerationProofRefused rather than being read as this act's.
        // A separate partition-equality check ahead of this would be a guard no deletion could
        // establish, since the proof already enforces exactly that equality.
        var expectedKey = LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(act);
        var proof = receipt.TryProveFamilyEnumeration(expectedKey, out var proofRefusal);
        if (proof is null)
        {
            return LuxembourgConsolidationByActResult.Refused(
                act,
                LuxembourgConsolidationByActRefusal.EnumerationProofRefused,
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
            return LuxembourgConsolidationByActResult.Refused(
                act,
                LuxembourgConsolidationByActRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount,
                wireBudget);
        }

        return DecodeRows(
            rows,
            profile,
            proof,
            act,
            wireBudget,
            run.ProductRequestCount);
    }

    /// <summary>Decodes one delivered page set into the act's consolidations.</summary>
    /// <remarks>
    /// INTERNAL deliberately, exactly as the inventory producer's own decoder: callers come through
    /// <see cref="RunAsync"/>, and the tests reach this by <c>InternalsVisibleTo</c>. A public decoder
    /// taking a caller's rows and a caller's act could mint consolidations from rows nobody proved,
    /// for an act nobody enumerated.
    /// </remarks>
    internal static LuxembourgConsolidationByActResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        AbsenceFamilyEnumerationProof proof,
        string act,
        WireBudgetSnapshot wireBudget,
        int productRequestCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentException.ThrowIfNullOrWhiteSpace(act);

        var completionEvidenceRef = proof.AcquisitionRunRef;
        var consolidations = new List<LuxembourgActConsolidation>(rows.Count);
        var seen = new HashSet<(string Value, string Kind)>();

        foreach (var row in rows)
        {
            LuxembourgActConsolidation consolidation;
            try
            {
                consolidation = DecodeRow(row, profile, act, completionEvidenceRef.ResourceId);
            }
            catch (RowNamesAnotherActException exception)
            {
                // A ROW NAMING ANOTHER ACT IS ITS OWN REFUSAL, not a general unreadable row: the
                // design review names it separately because it is the failure a bound-and-projected
                // act exists to catch, and reporting it as "row not admitted" would hide that the
                // family key and the query agreed while the delivered data did not.
                return LuxembourgConsolidationByActResult.Refused(
                    act,
                    LuxembourgConsolidationByActRefusal.RowNamesAnotherAct,
                    exception.Message,
                    productRequestCount,
                    wireBudget);
            }
            catch (ArgumentException exception)
            {
                return LuxembourgConsolidationByActResult.Refused(
                    act,
                    LuxembourgConsolidationByActRefusal.RowNotAdmitted,
                    exception.Message,
                    productRequestCount,
                    wireBudget);
            }

            if (!seen.Add((consolidation.Value, consolidation.Kind)))
            {
                return LuxembourgConsolidationByActResult.Refused(
                    act,
                    LuxembourgConsolidationByActRefusal.ConsolidationDeliveredTwice,
                    $"The delivery names {consolidation.Value} more than once, and the query groups "
                        + "by the coordinated text, so an honest answer names each exactly once.",
                    productRequestCount,
                    wireBudget);
            }

            consolidations.Add(consolidation);
        }

        return LuxembourgConsolidationByActResult.Success(
            act, consolidations, proof, productRequestCount, wireBudget);
    }

    private static LuxembourgActConsolidation DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string act,
        string observationId)
    {
        if (row.Terms.Count != profile.ProjectionVariables.Count)
        {
            throw new ArgumentException(
                "A consolidation row has exactly the profile's terms.", nameof(row));
        }

        // THE ACT COLUMN FIRST, because a row that is not about this act is not a row this run may
        // read at all - not a malformed consolidation, a consolidation of something else. It is
        // projected as an IRI bound through VALUES, so anything but this exact act IRI is refused.
        var actTerm = Term(row, profile, "act");
        if (actTerm.Kind != RepeatedEnumerationRdfTermKind.Iri ||
            !string.Equals(actTerm.Value, act, StringComparison.Ordinal))
        {
            throw new RowNamesAnotherActException(
                $"A delivered row names act '{actTerm.Value}', not the act this run asked about.");
        }

        var subject = Term(row, profile, "consolidation");

        // RDF PUTS NO LITERAL IN SUBJECT POSITION, exactly as the inventory decoder observes: a
        // literal here cannot have come from `?consolidation <consolidates> ?act`, and an unbound
        // subject is not a row this query with no absence branch can produce.
        var kind = subject.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri => LuxembourgConsolidationByActDiscoveryPlan.IriKind,
            RepeatedEnumerationRdfTermKind.BlankNode =>
                LuxembourgConsolidationByActDiscoveryPlan.UnsupportedBlankNodeKind,
            _ => throw new ArgumentException(
                "A coordinated text arrives as an IRI or a blank node; nothing else can be a subject.",
                nameof(row)),
        };

        if (string.IsNullOrEmpty(subject.Value))
        {
            throw new ArgumentException("A subject term carries its own lexical form.", nameof(row));
        }

        RequireMarkerAgrees(row, profile, "consolidation_kind", kind);

        var multiplicity = Multiplicity(row, profile);

        // The cursor keys are what the pages order by, so a row keyed differently from the terms it
        // delivered would page correctly and decode into a different fact.
        RequireKey(row, profile, "key_1", subject.Value);
        RequireKey(row, profile, "key_2", kind);

        return new LuxembourgActConsolidation(subject.Value, kind, multiplicity, observationId);
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
    /// The marker column must be the query's own unqualified plain literal AND agree with the term
    /// it describes.
    /// </summary>
    /// <remarks>
    /// Both halves are load-bearing, exactly as the inventory decoder records: a projected column
    /// this design groups and keys on is authoritative, so recomputing what it says and never
    /// reading it back would let the delivered column contradict both the term and the key.
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

    /// <summary>
    /// A delivered row's projected act is not the act this run asked about.
    /// </summary>
    /// <remarks>
    /// A private nested type, never surfaced: it exists only so <see cref="DecodeRow"/> can signal
    /// the one row failure that maps to its own refusal without threading a discriminator through
    /// the term readers, which every other row failure shares as a plain <see cref="ArgumentException"/>.
    /// </remarks>
    private sealed class RowNamesAnotherActException(string message) : ArgumentException(message);
}
