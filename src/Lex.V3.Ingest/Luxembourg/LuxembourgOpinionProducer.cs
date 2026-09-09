using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why delivered opinion rows could not be read at all.</summary>
/// <remarks>
/// Every member here means the delivery itself cannot be believed. A row that is well formed but
/// carries no locator this contract can hold is NOT a refusal: it becomes a typed exclusion, because
/// it is an honest publisher fact and refusing the whole page over it would lose the opinions that
/// did carry one.
/// </remarks>
public enum LuxembourgOpinionProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>A delivered row did not have the shape this family's profile projects.</summary>
    [JsonStringEnumMemberName("row_not_admitted")]
    RowNotAdmitted = 1,

    /// <summary>The enumeration itself was refused, so there are no rows to read.</summary>
    [JsonStringEnumMemberName("enumeration_refused")]
    EnumerationRefused = 2,

    /// <summary>The run delivered but its whole enumeration was not proven.</summary>
    [JsonStringEnumMemberName("enumeration_proof_refused")]
    EnumerationProofRefused = 3,

    /// <summary>The proven delivery's own rows could not be reopened and verified.</summary>
    [JsonStringEnumMemberName("verified_rows_refused")]
    VerifiedRowsRefused = 4,
}

/// <summary>
/// One delivered opinion event that carries no link-only record, with the publisher's own reason.
/// </summary>
/// <remarks>
/// <para>
/// This is the majority shape, not the corner. Of 13,009 opinion events only 6,393 carry a resulting
/// document and 9,144 carry a date, so more than half of what this family enumerates cannot become a
/// <see cref="LuxembourgOpinionLinkOnlyRecord"/>. Dropping them would report a corpus half its real
/// size while looking complete; refusing the page over them would deliver nothing at all.
/// </para>
/// <para>
/// <see cref="Refusal"/> is the contract's own typed reason where the record's door produced one,
/// and <see cref="LuxembourgOpinionLocatorRefusal.None"/> where the publisher simply delivered no
/// document or no date. The two are different facts: "we asked and the publisher holds none" against
/// "a locator exists and this product may not carry it", and a reader deciding whether an opinion is
/// reachable needs to tell them apart.
/// </para>
/// </remarks>
public sealed record LuxembourgOpinionExcludedEvent(
    string OpinionIri,
    bool DocumentDelivered,
    bool DateDelivered,
    LuxembourgOpinionLocatorRefusal Refusal);

/// <summary>Admitted opinion records and the events that could not become one. Never a refusal and both.</summary>
public sealed class LuxembourgOpinionProductionResult
{
    private LuxembourgOpinionProductionResult(
        IReadOnlyList<LuxembourgOpinionLinkOnlyRecord>? records,
        IReadOnlyList<LuxembourgOpinionExcludedEvent>? excluded,
        SourceArtifactRef? completionEvidenceRef,
        LuxembourgOpinionProductionRefusal refusal,
        string? detail,
        int productRequestCount)
    {
        Records = records;
        Excluded = excluded;
        CompletionEvidenceRef = completionEvidenceRef;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    public IReadOnlyList<LuxembourgOpinionLinkOnlyRecord>? Records { get; }

    /// <summary>Delivered events carrying no record, kept rather than dropped.</summary>
    public IReadOnlyList<LuxembourgOpinionExcludedEvent>? Excluded { get; }

    public SourceArtifactRef? CompletionEvidenceRef { get; }
    public LuxembourgOpinionProductionRefusal Refusal { get; }
    public string? Detail { get; }

    /// <summary>Publisher requests this run spent, robots excluded. Always populated.</summary>
    public int ProductRequestCount { get; }
    public bool Delivered => Refusal == LuxembourgOpinionProductionRefusal.None;

    internal static LuxembourgOpinionProductionResult Success(
        IReadOnlyList<LuxembourgOpinionLinkOnlyRecord> records,
        IReadOnlyList<LuxembourgOpinionExcludedEvent> excluded,
        SourceArtifactRef completionEvidenceRef,
        int productRequestCount = 0) =>
        new(Array.AsReadOnly(records.ToArray()), Array.AsReadOnly(excluded.ToArray()),
            completionEvidenceRef, LuxembourgOpinionProductionRefusal.None, null, productRequestCount);

    internal static LuxembourgOpinionProductionResult Refused(
        LuxembourgOpinionProductionRefusal refusal, string detail, int productRequestCount = 0)
    {
        if (refusal == LuxembourgOpinionProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(null, null, null, refusal, detail, productRequestCount);
    }

    /// <summary>
    /// Every admitted record. A refused run is never readable as a run that found nothing.
    /// </summary>
    public IReadOnlyList<LuxembourgOpinionLinkOnlyRecord> AdmittedRecords()
    {
        RequireDelivered();
        return Records!;
    }

    /// <summary>
    /// Every delivered event that carries no record. Separate from <see cref="AdmittedRecords"/> so a
    /// caller cannot read the records as the whole of what the family found.
    /// </summary>
    public IReadOnlyList<LuxembourgOpinionExcludedEvent> ExcludedEvents()
    {
        RequireDelivered();
        return Excluded!;
    }

    private void RequireDelivered()
    {
        if (!Delivered || Records is null || Excluded is null || CompletionEvidenceRef is null)
        {
            throw new InvalidOperationException(
                "A refused opinion production has no admitted records.");
        }
    }
}

/// <summary>
/// Turns delivered Conseil d'Etat opinion rows into link-only records, or refuses with a typed reason.
/// </summary>
/// <remarks>
/// <para>
/// THE MARKERS ARE CHECKED AGAINST THE TERMS' WHOLE VALUE SPACE. <c>?document_kind</c> and
/// <c>?date_kind</c> take four values, and an agreement check that asked only "does the marker say
/// unbound" would let a literal carrying an <c>iri</c> marker agree with itself. This seat shipped
/// exactly that collapse on the E6 producer and had it found by attacking its own head, so the
/// marker is compared against the marker the plan's own <c>BIND</c> must have produced for that
/// term's kind.
/// </para>
/// <para>
/// A MALFORMED ROW REFUSES EVERYTHING; AN UNREPRESENTABLE ONE IS KEPT AND TYPED. They are different
/// facts. A term that is not the kind its marker claims means the delivery cannot be believed at
/// all, so nothing from it is admitted. An opinion whose publisher record simply holds no resulting
/// document is a fact about Luxembourg, delivered honestly through the plan's own absence branch, and
/// it is more than half of this family - refusing the page over it would return nothing while looking
/// like a proven empty result.
/// </para>
/// <para>
/// NO OPINION TEXT IS READ, BECAUSE NONE IS DELIVERED. The plan projects a locator and a date and
/// asks for nothing else, and <see cref="LuxembourgOpinionLinkOnlyRecord"/> has nowhere to put a
/// body. This producer therefore has no branch that could carry text even if a row offered it.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionProducer
{
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public LuxembourgOpinionProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal LuxembourgOpinionProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    /// <summary>
    /// Runs the family and reads its proven rows. The producer owns the run, as every other
    /// Luxembourg producer does, so the completion evidence a record cites is the run's own.
    /// </summary>
    public async Task<LuxembourgOpinionProductionResult> RunAsync(
        LuxembourgOpinionRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var run = await _executor.RunLuxembourgOpinionsAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);
        if (run.Receipt is not { } receipt)
        {
            return LuxembourgOpinionProductionResult.Refused(
                LuxembourgOpinionProductionRefusal.EnumerationRefused,
                run.Refusal?.Code.ToString() ?? "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount);
        }

        var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal);
        if (proof is null)
        {
            return LuxembourgOpinionProductionResult.Refused(
                LuxembourgOpinionProductionRefusal.EnumerationProofRefused,
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
            return LuxembourgOpinionProductionResult.Refused(
                LuxembourgOpinionProductionRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount);
        }

        return DecodeRows(rows, profile, proof.AcquisitionRunRef, run.ProductRequestCount);
    }

    /// <summary>Decodes one delivered page set into records and typed exclusions.</summary>
    internal static LuxembourgOpinionProductionResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef completionEvidenceRef,
        int productRequestCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(completionEvidenceRef);

        var records = new List<LuxembourgOpinionLinkOnlyRecord>(rows.Count);
        var excluded = new List<LuxembourgOpinionExcludedEvent>();

        foreach (var row in rows)
        {
            try
            {
                Decode(row, profile, completionEvidenceRef.ResourceId, records, excluded);
            }
            catch (ArgumentException exception)
            {
                return LuxembourgOpinionProductionResult.Refused(
                    LuxembourgOpinionProductionRefusal.RowNotAdmitted,
                    exception.Message,
                    productRequestCount);
            }
        }

        return LuxembourgOpinionProductionResult.Success(
            records, excluded, completionEvidenceRef, productRequestCount);
    }

    private static void Decode(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string observationId,
        List<LuxembourgOpinionLinkOnlyRecord> records,
        List<LuxembourgOpinionExcludedEvent> excluded)
    {
        if (row.Terms.Count != profile.ProjectionVariables.Count || row.Terms.Count != 9)
        {
            throw new ArgumentException("An opinion row has nine exact terms.", nameof(row));
        }

        var opinion = Term(row, profile, "opinion");
        if (opinion.Kind != RepeatedEnumerationRdfTermKind.Iri || string.IsNullOrEmpty(opinion.Value))
        {
            throw new ArgumentException("opinion must be a publisher IRI.", nameof(row));
        }

        var document = Term(row, profile, "document");
        var date = Term(row, profile, "opinion_date");
        RequireMarkerAgrees(document, Term(row, profile, "document_kind"), "document");
        RequireMarkerAgrees(date, Term(row, profile, "date_kind"), "opinion_date");

        var documentDelivered = document.Kind != RepeatedEnumerationRdfTermKind.Unbound;
        var dateDelivered = date.Kind != RepeatedEnumerationRdfTermKind.Unbound;

        // Asked for and honestly absent. Kept as an exclusion carrying which half was missing, with
        // no contract refusal, because the record's door was never reached.
        if (!documentDelivered || !dateDelivered)
        {
            excluded.Add(new LuxembourgOpinionExcludedEvent(
                opinion.Value!, documentDelivered, dateDelivered, LuxembourgOpinionLocatorRefusal.None));
            return;
        }

        var record = LuxembourgOpinionLinkOnlyRecord.TryCreate(
            opinion.Value,
            document.Value,
            date.Value,
            date.Datatype,
            observationId,
            out var refusal);

        if (record is null)
        {
            // The publisher delivered both halves and the contract will not carry them: a locator
            // outside the admitted official origins, a query string the host's robots policy
            // disallows, or a date that is not valid at its own precision. Every one of those is a
            // fact about the publisher rather than a defect in the delivery, so the event is kept
            // with the contract's own reason attached rather than refused or dropped.
            excluded.Add(new LuxembourgOpinionExcludedEvent(opinion.Value!, true, true, refusal));
            return;
        }

        records.Add(record);
    }

    /// <summary>
    /// Refuses when a kind marker disagrees with the term it describes.
    /// </summary>
    /// <remarks>
    /// The marker is never the authority - it is this codebase's own <c>BIND</c> about a row - but a
    /// marker contradicting its term means the delivery is not what either side believes, so the row
    /// is refused rather than read past on the term alone. Compared against the whole four-valued
    /// space rather than a single boolean, for the reason recorded on the type.
    /// </remarks>
    private static void RequireMarkerAgrees(
        RepeatedEnumerationRdfTerm term, RepeatedEnumerationRdfTerm marker, string name)
    {
        var expected = term.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri => "iri",
            RepeatedEnumerationRdfTermKind.Literal => "literal",
            RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
            RepeatedEnumerationRdfTermKind.Unbound => LuxembourgOpinionDiscoveryPlan.UnboundKind,
            _ => throw new ArgumentOutOfRangeException(nameof(term)),
        };

        if (!string.Equals(marker.Value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {name} term and its kind marker disagree about what was delivered.", nameof(marker));
        }
    }

    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string name)
    {
        for (var ordinal = 0; ordinal < profile.ProjectionVariables.Count; ordinal++)
        {
            if (string.Equals(profile.ProjectionVariables[ordinal], name, StringComparison.Ordinal))
            {
                return row.Terms[ordinal];
            }
        }

        throw new ArgumentException($"The delivery profile does not project {name}.", nameof(profile));
    }
}
