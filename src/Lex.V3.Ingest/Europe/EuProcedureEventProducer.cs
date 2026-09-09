using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why delivered procedure-event rows could not become E8 observations.</summary>
public enum EuProcedureEventProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The enumeration itself was refused, so there are no rows to read.</summary>
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
    VerifiedRowsRefused = 7,

    /// <summary>
    /// A delivered row could not be read at all: a wrong term count, a marker that is not the
    /// query's own plain literal, a marker disagreeing with its term, or a cursor key that does not
    /// match the terms it is supposed to key.
    /// </summary>
    /// <remarks>
    /// This is a statement about the DELIVERY rather than about the publisher's facts, which is why
    /// it refuses everything. A row this codebase cannot read means the page cannot be trusted, and
    /// admitting its neighbours would hand back an event set that looks complete and is not.
    /// </remarks>
    [JsonStringEnumMemberName("row_not_admitted")]
    RowNotAdmitted = 3,

    /// <summary>
    /// A delivered row named a dossier this run never asked about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FALSE ABSENCE THIS PREVENTS IS THE POINT. Without it the producer admitted every decoded
    /// row and then published the CALLER'S requested set as <c>DossiersAskedAbout</c>, as though that
    /// set were proven coverage of what came back. A run asked about dossier A and delivered an event
    /// of dossier B therefore returned success, and <c>EventsOf(A)</c> answered an evidenced empty
    /// set while the one admitted observation belonged to B. An empty answer that looks proven is
    /// worse than an error, because nothing downstream can tell it from a real absence.
    /// </para>
    /// <para>
    /// Distinct from <see cref="RowNotAdmitted"/> deliberately, and this repository's own rule says
    /// why: a typed reason that misdescribes the fact it reports is read, believed and never
    /// questioned. The row here is perfectly readable — every term, marker and key is well formed.
    /// What is wrong is that it answers a question nobody asked.
    /// </para>
    /// <para>
    /// The executor performs the same membership check on the delivery it drives. This one is not a
    /// duplicate of it: <see cref="EuProcedureEventProducer.DecodeRows"/> is a public entry point
    /// that can be handed rows from anywhere, so a check living only in the executor leaves the
    /// producer trusting its caller about the one thing it must not.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("delivered_dossier_outside_requested_partition")]
    DeliveredDossierOutsideRequestedPartition = 6,

    /// <summary>
    /// One event's rows disagreed about which dossier it belongs to.
    /// </summary>
    /// <remarks>
    /// The rows for one event are grouped by the event, and every one of them carries the dossier.
    /// If two disagree, the publisher has said two different things about one node and this reader
    /// has no basis for choosing; picking either would be inventing an answer.
    /// </remarks>
    [JsonStringEnumMemberName("event_dossier_not_consistent")]
    EventDossierNotConsistent = 4,

    /// <summary>
    /// One event's rows disagreed about its date.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="EventDossierNotConsistent"/> so the reason names the fact it is
    /// about. The date travels on every one of an event's rows because the query groups on it, so
    /// two rows of one event differing there is the same class of contradiction.
    /// </remarks>
    [JsonStringEnumMemberName("event_date_not_consistent")]
    EventDateNotConsistent = 5,
}

/// <summary>
/// One event the publisher delivered honestly that the accepted contract will not carry, with the
/// contract's own reason.
/// </summary>
/// <remarks>
/// <para>
/// This is how "kept and typed, never dropped" stays true of the events as well as their types. An
/// untyped event, an undated one, a type term that is not an IRI, a date whose datatype names no
/// precision — each is a real reading of what the publisher holds, and the plan ASKS for the first
/// two by name rather than inferring them from rows that never came. Sinking a dossier's other
/// events over one of them would lose facts the publisher did deliver.
/// </para>
/// <para>
/// It carries <see cref="Refusal"/> verbatim from
/// <see cref="EuProcedureEventObservation.TryCreate"/> rather than re-deriving one. The contract
/// owns that vocabulary; a second copy here would be free to drift from the copy that decides.
/// </para>
/// </remarks>
public sealed record EuProcedureEventExcludedEvent(
    string EventIri,
    string DossierIri,
    IReadOnlyList<string> ObservedTypeIris,
    EuProcedureEventRefusal Refusal);

/// <summary>Admitted observations and typed exclusions, or one typed refusal. Never both.</summary>
public sealed class EuProcedureEventProductionResult
{
    private EuProcedureEventProductionResult(
        IReadOnlyList<EuProcedureEventObservation>? observations,
        IReadOnlyList<EuProcedureEventExcludedEvent>? excludedEvents,
        IReadOnlySet<string>? dossiersAskedAbout,
        SourceArtifactRef? completionEvidenceRef,
        EuProcedureEventProductionRefusal refusal,
        string? detail,
        int productRequestCount)
    {
        Observations = observations;
        ExcludedEvents = excludedEvents;
        DossiersAskedAbout = dossiersAskedAbout;
        CompletionEvidenceRef = completionEvidenceRef;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    /// <summary>
    /// How many product requests the run this result came from actually sent.
    /// </summary>
    /// <remarks>
    /// Carried on a refusal as well as on a success, because a refused run still spent the
    /// publisher's budget and a receipt that reported nothing for it would understate the traffic
    /// this codebase caused.
    /// </remarks>
    public int ProductRequestCount { get; }

    public IReadOnlyList<EuProcedureEventObservation>? Observations { get; }

    public IReadOnlyList<EuProcedureEventExcludedEvent>? ExcludedEvents { get; }

    /// <summary>
    /// The dossiers this run actually asked about, in the plan's own canonical form.
    /// </summary>
    public IReadOnlySet<string>? DossiersAskedAbout { get; }

    public SourceArtifactRef? CompletionEvidenceRef { get; }

    public EuProcedureEventProductionRefusal Refusal { get; }

    public string? Detail { get; }

    public bool Delivered => Refusal == EuProcedureEventProductionRefusal.None;

    internal static EuProcedureEventProductionResult Success(
        IReadOnlyList<EuProcedureEventObservation> observations,
        IReadOnlyList<EuProcedureEventExcludedEvent> excludedEvents,
        IReadOnlySet<string> dossiersAskedAbout,
        SourceArtifactRef completionEvidenceRef,
        int productRequestCount = 0) =>
        new(observations, excludedEvents, dossiersAskedAbout, completionEvidenceRef,
            EuProcedureEventProductionRefusal.None, null, productRequestCount);

    internal static EuProcedureEventProductionResult Refused(
        EuProcedureEventProductionRefusal refusal, string? detail, int productRequestCount = 0) =>
        new(null, null, null, null, refusal, detail, productRequestCount);

    /// <summary>Every admitted event of one dossier this run asked about.</summary>
    public IReadOnlyList<EuProcedureEventObservation> EventsOf(string dossierIri)
    {
        RequireAskedAbout(dossierIri);
        return Array.AsReadOnly(Observations!
            .Where(value => value.DossierIdentity.Identifiers
                .Any(identifier => string.Equals(identifier.RawValue, dossierIri, StringComparison.Ordinal)))
            .ToArray());
    }

    /// <summary>Every excluded event of one dossier this run asked about, with its reason.</summary>
    public IReadOnlyList<EuProcedureEventExcludedEvent> ExcludedEventsOf(string dossierIri)
    {
        RequireAskedAbout(dossierIri);
        return Array.AsReadOnly(ExcludedEvents!
            .Where(value => string.Equals(value.DossierIri, dossierIri, StringComparison.Ordinal))
            .ToArray());
    }

    /// <summary>
    /// Refuses to answer for a run that was refused, or for a dossier this run never asked about.
    /// </summary>
    /// <remarks>
    /// Both are the same mistake wearing different clothes. An empty list in either case would let a
    /// caller read "this dossier has no events" out of a run that either failed or never looked, and
    /// neither is the proven absence that answer claims to be.
    /// </remarks>
    private void RequireAskedAbout(string dossierIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dossierIri);
        if (!Delivered || Observations is null || ExcludedEvents is null ||
            DossiersAskedAbout is null || CompletionEvidenceRef is null)
        {
            throw new InvalidOperationException(
                "A refused procedure-event production has no admitted events.");
        }

        if (!DossiersAskedAbout.Contains(dossierIri))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dossierIri),
                $"This production never asked about {dossierIri}, so it cannot say which events "
                    + "belong to it.");
        }
    }
}

/// <summary>
/// Groups delivered procedure-event rows back into E8 observations, or refuses with a typed reason.
/// </summary>
/// <remarks>
/// <para>
/// THIS PRODUCER EXISTS BECAUSE THE PLAN REFUSES TO CONCATENATE. One event contributes one row per
/// declared type, so that each type reaches this decoder as its own term and the contract's
/// per-term judgement is reachable from a real delivery. The cost of that decision is paid here:
/// the rows must be grouped back, and grouping is the only place in this family where several
/// delivered rows become one fact.
/// </para>
/// <para>
/// EVERY TERM IS READ FROM ITSELF, NEVER FROM ITS MARKER. The plan binds the kind markers as a
/// convenience, and they are values this codebase computes ABOUT a row rather than the publisher's
/// word for what the row is. A marker that disagrees with its term refuses the row, because a
/// disagreement means one of the two is wrong and neither is safe to prefer silently. The markers
/// are themselves required to be the query's own unqualified plain literals, since a marker
/// delivered as an IRI reading "iri" is not the query's marker at all.
/// </para>
/// <para>
/// THE PRODUCER OWNS THE RUN, and that is what makes the intermediate a NAMED one. Candidate 5 R5.3
/// requires that drafts and legal-analysis records cannot be accepted through an unnamed
/// intermediate. Before this, <c>DecodeRows</c> was public and took a caller-supplied row list and a
/// caller-supplied evidence reference, so observations could be minted from rows nobody had proven,
/// citing custody nobody had established. Now the public door is <see cref="RunAsync"/>: it drives
/// the executor, requires a receipt, proves the enumeration, reopens each page's retained bytes and
/// passes them through <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> before any row is read.
/// The completion evidence a record cites is the RUN'S OWN, taken from the proof rather than
/// accepted from a caller, and decoding is internal.
/// </para>
/// <para>
/// AN HONEST ABSENCE IS AN EXCLUSION, A BROKEN DELIVERY IS A REFUSAL, and the line between them is
/// the whole design. An untyped or undated event is something the plan asked for by name and the
/// publisher answered; it is carried as <see cref="EuProcedureEventExcludedEvent"/> with the
/// contract's own reason. A row this reader cannot parse at all says the page is untrustworthy and
/// refuses the production whole.
/// </para>
/// </remarks>
public sealed class EuProcedureEventProducer
{
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public EuProcedureEventProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal EuProcedureEventProducer(
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
    /// Runs the family and reads its proven rows. The only public way to obtain observations.
    /// </summary>
    /// <remarks>
    /// The dossiers this run asked about are taken from the request's own batch in the plan's
    /// canonical form, never from a second caller-supplied list that could disagree with what was
    /// actually sent.
    /// </remarks>
    public async Task<EuProcedureEventProductionResult> RunAsync(
        EuProcedureEventRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var run = await _executor.RunEuProcedureEventsAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);
        if (run.Receipt is not { } receipt)
        {
            return EuProcedureEventProductionResult.Refused(
                EuProcedureEventProductionRefusal.EnumerationRefused,
                run.Refusal?.Code.ToString() ?? "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount);
        }

        var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal);
        if (proof is null)
        {
            return EuProcedureEventProductionResult.Refused(
                EuProcedureEventProductionRefusal.EnumerationProofRefused,
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
            return EuProcedureEventProductionResult.Refused(
                EuProcedureEventProductionRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount);
        }

        return DecodeRows(
            rows,
            profile,
            EuProcedureEventDiscoveryPlan.RequestedPartitionMembers(request.BatchDossiers),
            proof.AcquisitionRunRef,
            run.ProductRequestCount);
    }

    /// <summary>Decodes one delivered page set into observations.</summary>
    /// <param name="dossiersAskedAbout">
    /// The dossiers the run asked about, in the plan's canonical form — the form the publisher was
    /// asked in and therefore answers in.
    /// </param>
    /// <remarks>
    /// INTERNAL, and that is the point rather than an accident of scoping. A public decoder taking a
    /// caller's rows and a caller's evidence reference is the unnamed intermediate Candidate 5 R5.3
    /// forbids: it can mint observations from rows nobody proved, citing custody nobody established.
    /// Callers come through <see cref="RunAsync"/>; the tests reach this directly by
    /// <c>InternalsVisibleTo</c>, which is a test seam and not a second public door.
    /// </remarks>
    internal static EuProcedureEventProductionResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        IReadOnlyList<string> dossiersAskedAbout,
        SourceArtifactRef completionEvidenceRef,
        int productRequestCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(dossiersAskedAbout);
        ArgumentNullException.ThrowIfNull(completionEvidenceRef);

        List<DecodedRow> decoded;
        try
        {
            decoded = rows.Select(row => DecodeRow(row, profile)).ToList();
        }
        catch (ArgumentException exception)
        {
            return EuProcedureEventProductionResult.Refused(
                EuProcedureEventProductionRefusal.RowNotAdmitted, exception.Message,
                productRequestCount);
        }

        // EVERY DELIVERED DOSSIER MUST BE ONE THAT WAS ASKED ABOUT, checked before any success is
        // returned. DossiersAskedAbout is published as this run's coverage, so admitting a row from
        // outside it makes that publication a claim the delivery does not support.
        var requested = dossiersAskedAbout.ToHashSet(StringComparer.Ordinal);
        foreach (var row in decoded)
        {
            if (!requested.Contains(row.DossierIri))
            {
                return EuProcedureEventProductionResult.Refused(
                    EuProcedureEventProductionRefusal.DeliveredDossierOutsideRequestedPartition,
                    $"A row names dossier {row.DossierIri}, which this run never asked about.",
                    productRequestCount);
            }
        }

        var observations = new List<EuProcedureEventObservation>();
        var excluded = new List<EuProcedureEventExcludedEvent>();

        // Grouped by the event alone, deliberately. Grouping by (event, dossier) would turn a
        // publisher contradiction into two tidy events that each look coherent, which is exactly the
        // disagreement EventDossierNotConsistent exists to report.
        foreach (var group in decoded.GroupBy(static value => value.EventIri, StringComparer.Ordinal))
        {
            var first = group.First();

            if (group.Any(value => !string.Equals(value.DossierIri, first.DossierIri, StringComparison.Ordinal)))
            {
                return EuProcedureEventProductionResult.Refused(
                    EuProcedureEventProductionRefusal.EventDossierNotConsistent,
                    $"The rows for {group.Key} name more than one dossier.",
                    productRequestCount);
            }

            if (group.Any(value =>
                    !string.Equals(value.DateLexical, first.DateLexical, StringComparison.Ordinal) ||
                    !string.Equals(value.DateDatatypeIri, first.DateDatatypeIri, StringComparison.Ordinal)))
            {
                return EuProcedureEventProductionResult.Refused(
                    EuProcedureEventProductionRefusal.EventDateNotConsistent,
                    $"The rows for {group.Key} state more than one date.",
                    productRequestCount);
            }

            // Delivery order, kept exactly. The plan orders by the cursor, whose first key is the
            // event and whose third is the type, so an event's types arrive together and in a
            // stable order; reordering them here would discard something the delivery established.
            var types = group
                .Where(static value => value.TypeIri is not null)
                .Select(static value => value.TypeIri!)
                .ToArray();

            var observation = EuProcedureEventObservation.TryCreate(
                first.EventIri,
                first.DossierIri,
                types,
                first.DateLexical,
                first.DateDatatypeIri,
                completionEvidenceRef.ResourceId,
                out var refusal);

            if (observation is null)
            {
                excluded.Add(new EuProcedureEventExcludedEvent(
                    first.EventIri, first.DossierIri, Array.AsReadOnly(types), refusal));
                continue;
            }

            observations.Add(observation);
        }

        return EuProcedureEventProductionResult.Success(
            observations,
            excluded,
            dossiersAskedAbout.ToHashSet(StringComparer.Ordinal),
            completionEvidenceRef,
            productRequestCount);
    }

    /// <summary>One delivered row, read from its own terms.</summary>
    private sealed record DecodedRow(
        string EventIri,
        string DossierIri,
        string? TypeIri,
        string? DateLexical,
        string? DateDatatypeIri);

    private static DecodedRow DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile)
    {
        if (row.Terms.Count != profile.ProjectionVariables.Count)
        {
            throw new ArgumentException(
                "A procedure-event row has exactly the profile's terms.", nameof(row));
        }

        var eventTerm = Term(row, profile, "event");
        var dossierTerm = Term(row, profile, "dossier");
        var typeTerm = Term(row, profile, "event_type");
        var dateTerm = Term(row, profile, "event_date");

        // Each marker is checked against the plan's own constant for ITS column. The two unbound
        // markers happen to share a spelling today, so one constant would work by luck; naming the
        // wrong one would go on passing until the day they diverged, and then agree with nothing.
        RequireMarkerAgrees(
            row, profile, "event_kind", eventTerm, EuProcedureEventDiscoveryPlan.UnboundTypeKind);
        RequireMarkerAgrees(
            row, profile, "type_kind", typeTerm, EuProcedureEventDiscoveryPlan.UnboundTypeKind);
        RequireMarkerAgrees(
            row, profile, "date_kind", dateTerm, EuProcedureEventDiscoveryPlan.UnboundDateKind);

        var eventIri = RequireIri(eventTerm, "event");
        var dossierIri = RequireIri(dossierTerm, "dossier");

        // A DELIVERED TYPE MUST BE A TERM THIS CONTRACT CAN JUDGE, and a literal one is not a
        // transport defect - it is a real publisher fact the contract names EventTypeNotAnIri. So it
        // is carried through as a non-IRI type rather than refused here, and TryCreate makes that
        // call. Only the blank node is unreadable: it has no lexical identity to carry at all.
        RequireQualifiersAgree(row, profile, "type", typeTerm);
        RequireQualifiersAgree(row, profile, "date", dateTerm);

        string? typeIri = null;
        switch (typeTerm.Kind)
        {
            case RepeatedEnumerationRdfTermKind.Iri:
            case RepeatedEnumerationRdfTermKind.Literal:
                typeIri = typeTerm.Value;
                break;
            case RepeatedEnumerationRdfTermKind.Unbound:
                break;
            default:
                throw new ArgumentException(
                    "A delivered event type must be an IRI or a literal, not a blank node.",
                    nameof(row));
        }

        string? dateLexical = null;
        string? dateDatatypeIri = null;
        if (dateTerm.Kind != RepeatedEnumerationRdfTermKind.Unbound)
        {
            if (dateTerm.Kind != RepeatedEnumerationRdfTermKind.Literal)
            {
                throw new ArgumentException(
                    "A delivered event date must be a publisher literal.", nameof(row));
            }

            dateLexical = dateTerm.Value;

            // Taken from the projected column, because the canonical key is built from that column
            // and reading the two from different places is how a row keys one way and decodes
            // another. RequireQualifiersAgree above has already established that the column and the
            // term's own datatype say the same thing, so this is not a choice between two sources.
            dateDatatypeIri = RequirePlainLiteral(
                Term(row, profile, "date_datatype"), "date_datatype");
        }

        _ = RequirePositiveInteger(Term(row, profile, "multiplicity"), "multiplicity");

        // ALL ELEVEN cursor keys, not just the lexical ones. The keys are the page's own proof of
        // what it delivered and in what order, and checking a subset lets a verified page prove one
        // tuple while this producer emits another.
        //
        // The six kind and qualifier keys matter most, and checking only the five lexical keys was
        // the gap. Those six exist BECAUSE two terms can share every lexical form and still be
        // different facts - that is the whole content of the plan's second review round. A row whose
        // kind key contradicts the kind it delivered is keyed as the term it is not, so it sorts and
        // pages as a different row than the one this producer reads.
        RequireKey(row, profile, "key_1", eventIri);
        RequireKey(row, profile, "key_2", MarkerFor(eventTerm, EuProcedureEventDiscoveryPlan.UnboundTypeKind));
        RequireKey(row, profile, "key_3", typeIri ?? string.Empty);
        RequireKey(row, profile, "key_4", MarkerFor(typeTerm, EuProcedureEventDiscoveryPlan.UnboundTypeKind));
        RequireKey(row, profile, "key_5", QualifierOf(typeTerm, static term => term.Datatype));
        RequireKey(row, profile, "key_6", QualifierOf(typeTerm, static term => term.Language));
        RequireKey(row, profile, "key_7", dossierIri);
        RequireKey(row, profile, "key_8", dateLexical ?? string.Empty);
        RequireKey(row, profile, "key_9", MarkerFor(dateTerm, EuProcedureEventDiscoveryPlan.UnboundDateKind));
        RequireKey(row, profile, "key_10", dateDatatypeIri ?? string.Empty);
        RequireKey(row, profile, "key_11", QualifierOf(dateTerm, static term => term.Language));

        return new DecodedRow(eventIri, dossierIri, typeIri, dateLexical, dateDatatypeIri);
    }

    /// <summary>
    /// The marker the plan's own <c>BIND</c> must have produced for a term of this kind.
    /// </summary>
    /// <remarks>
    /// Compared across the marker's whole value space rather than reduced to "is it unbound". An
    /// earlier producer in this repository collapsed four values to one boolean, so a literal term
    /// carrying an <c>iri</c> marker agreed with itself and passed: half the value space could not
    /// contradict anything, which made the disagreement check weaker than its own name.
    /// </remarks>
    private static string MarkerFor(RepeatedEnumerationRdfTerm term, string unboundMarker) =>
        term.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri => "iri",
            RepeatedEnumerationRdfTermKind.Literal => "literal",
            RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
            RepeatedEnumerationRdfTermKind.Unbound => unboundMarker,
            _ => throw new ArgumentOutOfRangeException(nameof(term)),
        };

    private static void RequireMarkerAgrees(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string markerName,
        RepeatedEnumerationRdfTerm term,
        string unboundMarker)
    {
        var marker = RequirePlainLiteral(Term(row, profile, markerName), markerName);
        if (!string.Equals(marker, MarkerFor(term, unboundMarker), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {markerName} marker and the term it describes disagree about what was delivered.",
                nameof(row));
        }
    }

    /// <summary>
    /// A term's own datatype and language must agree with the columns the plan projects for them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the marker-versus-term rule applied to the qualifiers, and it exists for the same
    /// reason. The plan binds <c>?type_datatype</c> and <c>?date_language</c> from <c>DATATYPE()</c>
    /// and <c>LANG()</c> over the very terms they describe, so in an honest delivery the column and
    /// the term cannot disagree. If they do, one of the two is wrong and neither is safe to prefer
    /// silently.
    /// </para>
    /// <para>
    /// It is load bearing rather than decorative, because those columns are what the CANONICAL KEY
    /// is built from. A term whose datatype differs from its column is a row that keys as one fact
    /// and decodes as another — the page proves a tuple this producer does not emit. That is the
    /// same class of defect the cursor-key check catches, one level further in.
    /// </para>
    /// <para>
    /// An unbound term carries no qualifiers, and the plan's own COALESCE binds both columns to the
    /// empty string for it, so absence must agree with absence too.
    /// </para>
    /// </remarks>
    private static void RequireQualifiersAgree(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string stem,
        RepeatedEnumerationRdfTerm term)
    {
        var datatype = RequirePlainLiteral(
            Term(row, profile, stem + "_datatype"), stem + "_datatype");
        var language = RequirePlainLiteral(
            Term(row, profile, stem + "_language"), stem + "_language");

        // Only a literal carries either, and the plan's IF(isLiteral(...)) says so: every other kind
        // is bound to the empty string.
        var expectedDatatype = term.Kind == RepeatedEnumerationRdfTermKind.Literal
            ? term.Datatype ?? string.Empty
            : string.Empty;
        var expectedLanguage = term.Kind == RepeatedEnumerationRdfTermKind.Literal
            ? term.Language ?? string.Empty
            : string.Empty;

        if (!string.Equals(datatype, expectedDatatype, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {stem}_datatype column and the term it describes disagree, so the row keys as "
                    + "one fact and decodes as another.",
                nameof(row));
        }

        if (!string.Equals(language, expectedLanguage, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {stem}_language column and the term it describes disagree, so the row keys as "
                    + "one fact and decodes as another.",
                nameof(row));
        }
    }

    /// <summary>
    /// One qualifier of a term, in the form the plan's own COALESCE binds it: the empty string for
    /// anything that is not a literal, and for a literal that carries none.
    /// </summary>
    private static string QualifierOf(
        RepeatedEnumerationRdfTerm term,
        Func<RepeatedEnumerationRdfTerm, string?> select) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal
            ? select(term) ?? string.Empty
            : string.Empty;

    private static void RequireKey(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string keyName,
        string expected)
    {
        var actual = RequirePlainLiteral(Term(row, profile, keyName), keyName);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{keyName} does not key the terms this row delivered.", nameof(row));
        }
    }

    private static string RequireIri(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Iri || string.IsNullOrEmpty(term.Value) ||
            term.Datatype is not null || term.Language is not null)
        {
            throw new ArgumentException($"{name} must be a publisher IRI.", name);
        }

        return term.Value;
    }

    private static string RequirePlainLiteral(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Value is null ||
            term.Datatype is not null || term.Language is not null)
        {
            throw new ArgumentException($"{name} must be an unqualified plain literal.", name);
        }

        return term.Value;
    }

    /// <summary>
    /// The grouped count, required to be the term the publisher actually delivers.
    /// </summary>
    /// <remarks>
    /// The datatype is checked, not just the kind and the digits. <c>COUNT(*)</c> yields an
    /// <c>xsd:integer</c>, so a positive count arriving as an <c>xsd:string</c> — or carrying a
    /// language tag — is not the term this query produces, and accepting it means the reader cannot
    /// tell the publisher's count from something that merely looks like one.
    ///
    /// The fixture was wrong in the same place and hid it: it emitted a plain literal, so no test
    /// modelled the delivered shape. That is the second time on this family that a fixture agreeing
    /// with a weaker guard made the guard look sufficient — the date's datatype was the first.
    /// </remarks>
    private static long RequirePositiveInteger(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Value is null ||
            // xsd:integer inline rather than as a named token. A const here would make this
            // producer a closed-surface census candidate holding exactly one datatype IRI, and it is
            // not a vocabulary; the sibling LuxembourgOpinionProducer spells it the same way.
            term.Datatype != "http://www.w3.org/2001/XMLSchema#integer" ||
            term.Language is not null ||
            !long.TryParse(term.Value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new ArgumentException(
                $"{name} must be a positive xsd:integer literal, which is what COUNT(*) delivers.",
                name);
        }

        return value;
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
