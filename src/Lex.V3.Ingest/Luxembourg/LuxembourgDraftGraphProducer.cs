using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why delivered draft-graph rows could not become records.</summary>
public enum LuxembourgDraftGraphProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The enumeration itself was refused, so there are no rows to read.</summary>
    [JsonStringEnumMemberName("enumeration_refused")]
    EnumerationRefused = 1,

    /// <summary>The run delivered but its whole enumeration was not proven.</summary>
    [JsonStringEnumMemberName("enumeration_proof_refused")]
    EnumerationProofRefused = 2,

    /// <summary>The proven pages would not reopen into verified rows.</summary>
    [JsonStringEnumMemberName("verified_rows_refused")]
    VerifiedRowsRefused = 3,

    /// <summary>
    /// A delivered row could not be read at all: a wrong term count, a marker that is not the
    /// query's own plain literal, a marker disagreeing with its term, a qualifier column
    /// contradicting the term it describes, or a cursor key that does not key its own row.
    /// </summary>
    /// <remarks>
    /// A statement about the DELIVERY rather than about the publisher's facts, which is why it
    /// refuses everything: a row this codebase cannot read means the page cannot be trusted.
    /// </remarks>
    [JsonStringEnumMemberName("row_not_admitted")]
    RowNotAdmitted = 4,

    // ORDINAL 5 IS RETIRED AND PERMANENTLY UNALLOCATED. It was "predicate_not_asked_about", and
    // it rested on the plan binding its five predicates from a VALUES block, so that an honest
    // delivery could contain no other. That block is gone: it was measured DROPPING rows the
    // publisher holds. The publisher is now asked for every predicate it has about these subjects,
    // so a delivery containing another is not merely honest but expected, and the ones this family
    // does not admit are named on RetainedNotAdmitted rather than refused.
    //
    // Not reused, so a retained artifact carrying the old name cannot acquire a new meaning.

    /// <summary>
    /// The delivery was read, but the (requested drafts x asked properties) matrix could not be
    /// completed over it.
    /// </summary>
    /// <remarks>
    /// A statement about coverage rather than about any row. The delivered rows may each be
    /// perfectly readable and the matrix still be unprovable - a draft delivered that this batch
    /// never asked about, a row folded twice, a pair left represented by nothing. Refusing is the
    /// only honest option: a partial matrix read as a whole one is a set of absences asserted over
    /// a corpus nobody established.
    /// </remarks>
    [JsonStringEnumMemberName("matrix_completion_refused")]
    MatrixCompletionRefused = 7,

    // ORDINAL 6 IS RETIRED AND PERMANENTLY UNALLOCATED. It was
    // "draft_property_coverage_incomplete", and it refused any draft carrying fewer than five rows.
    // That invariant was true only while the query asked for the absent case: with the mandatory
    // value triple a draft delivers a row per value it HAS, so 50 drafts delivered 95 present pairs
    // and this member would have refused the very first honest batch.
    //
    // Its claim did not disappear, it moved. Coverage is now proven over the whole
    // requested-drafts x asked-predicates matrix by LuxembourgDraftPropertyCoverage, which is the
    // only thing that can prove it: under the present-facts shape a draft holding none of the five
    // properties delivers no rows at all, so the delivered rows cannot say which drafts were asked
    // about. The requested set has to come from the run, not from the answer.
    //
    // Not reused, because a retained artifact carrying the old name must not silently acquire a new
    // meaning when it is read back.
}

/// <summary>
/// One property the publisher holds for one draft, exactly as delivered.
/// </summary>
/// <remarks>
/// <para>
/// A RECORD, NOT AN ACCEPTED FACT, and the distinction is deliberate for the same reason
/// <c>EuProcedureEventObservation</c> gives: there is no accepted Facts type for a draft property,
/// and minting one here would create an accepted surface out of a source reading. This carries the
/// publisher's shape and stops.
/// </para>
/// <para>
/// <see cref="ValueDatatypeIri"/> and <see cref="ValueLanguageTag"/> are retained rather than
/// collapsed. Two literals sharing a lexical form and differing in datatype or language are
/// different facts, which two review rounds on the procedure-event plan settled; a record that kept
/// only the lexical form would be unable to tell them apart after the fact even though the delivery
/// could.
/// </para>
/// <para>
/// <see cref="ValueKind"/> is <c>iri</c> or <c>literal</c> and NEVER <c>unbound</c>. Every record
/// here is a value the publisher actually returned - the query's value triple is mandatory, so a
/// pair the publisher holds nothing for produces no row at all.
/// </para>
/// <para>
/// THE GAP IS NOT MISSING; IT IS SOMEWHERE ELSE. A pair with no delivered value becomes a separate,
/// typed, evidence-bound observed-absence record derived from the batch's completed enumeration.
/// The separation is the point: a row here is something the publisher SAID, and an absence is
/// something THIS CODE CONCLUDED from a complete enumeration. S2-A03 requires the gap to be
/// first-class, and S2-A01 requires the assertion to be typed as the publisher's; one record type
/// carrying both would satisfy the first by breaking the second.
/// </para>
/// </remarks>
public sealed record LuxembourgDraftPropertyRecord(
    string DraftIri,
    string PredicateIri,
    string? Value,
    string ValueKind,
    string ValueDatatypeIri,
    string ValueLanguageTag,
    string SourceObservationId)
{
    /// <summary>What the publisher delivered: an IRI or a literal, and never an absence.</summary>
    /// <remarks>
    /// GUARDED HERE BECAUSE THE TYPE IS PUBLIC. The producer refuses an unbound value when it
    /// decodes, but this record has a public constructor, so any assembly could otherwise mint one
    /// carrying the unbound marker and the acquisition run ref of a real batch - a record
    /// indistinguishable, to every reader downstream, from a publisher-returned absence. That is
    /// the exact fabrication this family's design exists to make impossible, so it is refused at
    /// the only door that can reach it.
    /// </remarks>
    public string ValueKind { get; init; } = RequireDelivered(ValueKind);

    private static string RequireDelivered(string valueKind) =>
        string.Equals(valueKind, LuxembourgDraftGraphDiscoveryPlan.UnboundKind, StringComparison.Ordinal)
            ? throw new ArgumentException(
                "A delivered record carries a value the publisher returned. An absence is derived, "
                    + "typed and cited elsewhere, and may not be minted as a row.",
                nameof(valueKind))
            : valueKind;
}

/// <summary>Admitted draft-property records, or one typed refusal. Never both.</summary>
public sealed class LuxembourgDraftGraphProductionResult
{
    private LuxembourgDraftGraphProductionResult(
        IReadOnlyList<LuxembourgDraftPropertyRecord>? records,
        IReadOnlySet<string>? predicatesAskedAbout,
        SourceArtifactRef? completionEvidenceRef,
        LuxembourgDraftPropertyCoverage? coverage,
        IReadOnlyList<string> retainedNotAdmitted,
        LuxembourgDraftGraphProductionRefusal refusal,
        string? detail,
        int productRequestCount)
    {
        RetainedNotAdmitted = retainedNotAdmitted;
        Records = records;
        PredicatesAskedAbout = predicatesAskedAbout;
        CompletionEvidenceRef = completionEvidenceRef;
        Coverage = coverage;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    public IReadOnlyList<LuxembourgDraftPropertyRecord>? Records { get; }

    /// <summary>The properties this run actually asked every draft about.</summary>
    public IReadOnlySet<string>? PredicatesAskedAbout { get; }

    public SourceArtifactRef? CompletionEvidenceRef { get; }

    /// <summary>
    /// Predicates the publisher delivered for these subjects that this family does not admit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RETAINED EVIDENCE, NEVER PRODUCT FACTS. The broad acquisition asks for every predicate the
    /// publisher holds about the requested subjects, because a publisher-side predicate filter was
    /// measured dropping rows it holds. Admission to the E8 vocabulary is therefore made here, where
    /// it is visible and testable, instead of by the query.
    /// </para>
    /// <para>
    /// These are counted and named so a reader can see what the delivery carried, and they are not
    /// decoded into records: this family asserts nothing about them, and the retained page is the
    /// evidence for anyone who later wants to.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> RetainedNotAdmitted { get; }

    /// <summary>
    /// The completed matrix over this batch: present values and derived absences together.
    /// </summary>
    /// <remarks>
    /// Non-null exactly when <see cref="Delivered"/>. <see cref="Records"/> is what the publisher
    /// said; this is what the run concluded, and the pairs with no row live only here.
    /// </remarks>
    public LuxembourgDraftPropertyCoverage? Coverage { get; }

    public LuxembourgDraftGraphProductionRefusal Refusal { get; }

    public string? Detail { get; }

    public int ProductRequestCount { get; }

    public bool Delivered => Refusal == LuxembourgDraftGraphProductionRefusal.None;

    internal static LuxembourgDraftGraphProductionResult Success(
        IReadOnlyList<LuxembourgDraftPropertyRecord> records,
        IReadOnlySet<string> predicatesAskedAbout,
        SourceArtifactRef completionEvidenceRef,
        LuxembourgDraftPropertyCoverage coverage,
        IReadOnlyList<string> retainedNotAdmitted,
        int productRequestCount = 0) =>
        new(records, predicatesAskedAbout, completionEvidenceRef, coverage, retainedNotAdmitted,
            LuxembourgDraftGraphProductionRefusal.None, null, productRequestCount);

    internal static LuxembourgDraftGraphProductionResult Refused(
        LuxembourgDraftGraphProductionRefusal refusal, string? detail, int productRequestCount = 0) =>
        new(null, null, null, null, [], refusal, detail, productRequestCount);

    /// <summary>Every delivered value of one property this run asked about.</summary>
    /// <remarks>
    /// PRESENT VALUES ONLY, and every one of them is a value the publisher returned. A caller that
    /// reads this and believes it has seen every (draft, property) pair has read a partial answer as
    /// a whole one: the pairs the publisher holds nothing for are not here and are not rows. They
    /// are the run's separate observed-absence records, which is what makes them readable as this
    /// code's conclusion rather than as the publisher's silence rendered into a row.
    /// </remarks>
    public IReadOnlyList<LuxembourgDraftPropertyRecord> For(string predicateIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(predicateIri);
        if (!Delivered || Records is null || PredicatesAskedAbout is null || CompletionEvidenceRef is null)
        {
            throw new InvalidOperationException(
                "A refused draft-graph production has no admitted records.");
        }

        if (!PredicatesAskedAbout.Contains(predicateIri))
        {
            throw new ArgumentOutOfRangeException(
                nameof(predicateIri),
                $"This production never asked about {predicateIri}, so it cannot say what drafts "
                    + "hold for it.");
        }

        return Array.AsReadOnly(Records!
            .Where(value => string.Equals(value.PredicateIri, predicateIri, StringComparison.Ordinal))
            .ToArray());
    }
}

/// <summary>
/// Runs the Luxembourg draft graph and reads its proven rows.
/// </summary>
/// <remarks>
/// <para>
/// THE PRODUCER OWNS THE RUN, so the intermediate is a NAMED one. <see cref="RunAsync"/> is the only
/// public door: it drives the executor, requires a receipt, proves the enumeration, reopens each
/// page's retained bytes and passes them through
/// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> before any row is read, and the completion
/// evidence a record cites is the RUN'S own, taken from the proof rather than accepted from a
/// caller. Decoding is internal. Two sibling families shipped with a public decoder taking caller
/// rows and a caller evidence reference, and both had to be repaired.
/// </para>
/// <para>
/// EVERY TERM IS READ FROM ITSELF, NEVER FROM ITS MARKER, and each qualifier column must agree with
/// the term it describes. The plan binds <c>?datatype_iri</c> and <c>?language_tag</c> from
/// <c>DATATYPE()</c> and <c>LANG()</c> over the very term they qualify, so in an honest delivery
/// they cannot disagree; a row where they do keys as one fact and decodes as another.
/// </para>
/// <para>
/// NO DOCUMENT IS FETCHED. The family asks for metadata and retains
/// <c>parliamentDraftUrl</c> as text. Its values name pages on a host this pipeline does not
/// contact, and nothing here dereferences one.
/// </para>
/// </remarks>
public sealed class LuxembourgDraftGraphProducer
{
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public LuxembourgDraftGraphProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal LuxembourgDraftGraphProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    /// <summary>Runs the family and reads its proven rows. The only public way to obtain records.</summary>
    public async Task<LuxembourgDraftGraphProductionResult> RunAsync(
        LuxembourgDraftGraphRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var run = await _executor.RunLuxembourgDraftGraphAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);
        if (run.Receipt is not { } receipt)
        {
            return LuxembourgDraftGraphProductionResult.Refused(
                LuxembourgDraftGraphProductionRefusal.EnumerationRefused,
                run.Refusal?.Code.ToString() ?? "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount);
        }

        var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal);
        if (proof is null)
        {
            return LuxembourgDraftGraphProductionResult.Refused(
                LuxembourgDraftGraphProductionRefusal.EnumerationProofRefused,
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
            return LuxembourgDraftGraphProductionResult.Refused(
                LuxembourgDraftGraphProductionRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount);
        }

        return DecodeRows(
            rows,
            profile,
            proof.AcquisitionRunRef,
            LuxembourgDraftGraphDiscoveryPlan.RequestedPartitionMembers(request.BatchDrafts),
            request.Inventory,
            // FROM THE DELIVERY, NOT FROM THE REQUEST. Both travel back through the retained
            // receipt, so the coverage compares what was actually sent and when it was observed
            // against what this run believes it asked.
            proof.FamilyKey,
            receipt.Delivery.ObservationTimes.CountA,
            run.ProductRequestCount);
    }

    /// <summary>Decodes one delivered page set into records.</summary>
    /// <remarks>
    /// INTERNAL deliberately. Callers come through <see cref="RunAsync"/>; the tests reach this by
    /// <c>InternalsVisibleTo</c>, which is a test seam and not a second public door.
    /// </remarks>
    internal static LuxembourgDraftGraphProductionResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef completionEvidenceRef,
        IReadOnlyList<string> requestedDrafts,
        LuxembourgInitialDraftInventoryCitation inventory,
        string partitionKey,
        string observedAt,
        int productRequestCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(completionEvidenceRef);
        ArgumentNullException.ThrowIfNull(requestedDrafts);
        ArgumentNullException.ThrowIfNull(inventory);

        var asked = LuxembourgDraftGraphDiscoveryPlan.AskedAbout.ToHashSet(StringComparer.Ordinal);
        var notAdmitted = new List<string>();
        var records = new List<LuxembourgDraftPropertyRecord>(rows.Count);

        foreach (var row in rows)
        {
            LuxembourgDraftPropertyRecord record;
            try
            {
                // ADMISSION BEFORE DECODE, AND THE ORDER IS THE POINT. The broad acquisition
                // deliberately carries every predicate the publisher holds about these subjects, so
                // most delivered rows are ones this family asserts nothing about. Decoding those
                // fully would let a value shape E8 makes no claim about refuse a delivery whose
                // admitted half is perfectly readable - which is exactly what happened on the first
                // broad run: forty plain-literal rows, none of them an admitted predicate, refused
                // a page whose thirty-six admitted rows were clean.
                //
                // The predicate itself is still required to be a readable IRI. A row whose
                // predicate cannot be read is a delivery this codebase cannot trust, admitted or
                // not, and it still refuses.
                var predicateIri = RequireIri(Term(row, profile, "predicate"), "predicate");
                if (!asked.Contains(predicateIri))
                {
                    notAdmitted.Add(predicateIri);
                    continue;
                }

                record = DecodeRow(row, profile, completionEvidenceRef.ResourceId);
            }
            catch (ArgumentException exception)
            {
                return LuxembourgDraftGraphProductionResult.Refused(
                    LuxembourgDraftGraphProductionRefusal.RowNotAdmitted,
                    exception.Message,
                    productRequestCount);
            }

            records.Add(record);
        }

        // ONLY NOW, AND ONLY HERE. Every refusal above returns before this point, so a matrix can
        // only ever be completed over a delivery that was read whole - the ruling's "only after
        // that batch proof is Delivered" made structural rather than sequential.
        //
        // The delivered row count comes from the ROWS and the folded count from the RECORDS, which
        // is what makes the exactly-once check mean anything: comparing the decoded list against
        // itself would pass for a decoder that dropped a row and never notice.
        var coverage = LuxembourgDraftPropertyCoverage.TryComplete(
            requestedDrafts,
            LuxembourgDraftGraphDiscoveryPlan.AskedAbout,
            records.Select(static value => new LuxembourgDraftPropertyRecordView(
                value.DraftIri, value.PredicateIri, value.Value, value.ValueKind)).ToArray(),
            new LuxembourgDraftBatchCitation(
                completionEvidenceRef,
                LuxembourgDraftPropertyCoverage.SelectionDigestFor(requestedDrafts),
                requestedDrafts.Count,
                rows.Count,
                partitionKey,
                observedAt),
            inventory,
            notAdmitted.Count,
            LuxembourgDraftGraphDiscoveryPlan.PredicatesNotDeclaredOnTheDraft,
            out var coverageRefusal,
            out var coverageDetail);
        if (coverage is null)
        {
            return LuxembourgDraftGraphProductionResult.Refused(
                LuxembourgDraftGraphProductionRefusal.MatrixCompletionRefused,
                coverageRefusal + ": " + coverageDetail,
                productRequestCount);
        }

        return LuxembourgDraftGraphProductionResult.Success(
            records, asked, completionEvidenceRef, coverage, notAdmitted, productRequestCount);
    }

    /// <summary>RDF 1.1: a simple literal is an xsd:string, and this engine's DATATYPE says so.</summary>
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    private static LuxembourgDraftPropertyRecord DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string observationId)
    {
        if (row.Terms.Count != profile.ProjectionVariables.Count)
        {
            throw new ArgumentException(
                "A draft-graph row has exactly the profile's terms.", nameof(row));
        }

        var draftTerm = Term(row, profile, "draft");
        var predicateTerm = Term(row, profile, "predicate");
        var valueTerm = Term(row, profile, "value");

        RequireMarkerAgrees(row, profile, "draft_kind", draftTerm);
        RequireMarkerAgrees(row, profile, "value_kind", valueTerm);

        // THE VALUE TRIPLE IS MANDATORY, SO AN UNBOUND VALUE CANNOT HONESTLY ARRIVE. Refused rather
        // than admitted, and the distinction is the whole design: admitting it would mint a record
        // that LOOKS like the publisher reporting an absence, when an absence is this code's
        // conclusion from a complete enumeration and never the publisher's statement. A delivery
        // producing one is not a delivery this plan can have produced, so the page is not trusted.
        if (valueTerm.Kind == RepeatedEnumerationRdfTermKind.Unbound)
        {
            throw new ArgumentException(
                "The value triple is mandatory, so a row carrying no value is not a delivery this "
                    + "plan can have produced.",
                nameof(row));
        }

        var draftIri = RequireIri(draftTerm, "draft");
        var predicateIri = RequireIri(predicateTerm, "predicate");

        // ONE qualifier column may be absent, in ONE case, and it is the case this engine was
        // measured doing. Everywhere else the query answers both columns and the row must carry them.
        var datatype = ReadQualifier(row, profile, "datatype_iri", valueTerm);
        var language = ReadQualifier(row, profile, "language_tag", valueTerm);

        // The qualifier columns must agree with the term they describe. Both are bound from
        // DATATYPE() and LANG() over that very term, so an honest delivery cannot disagree, and a
        // row that does keys as one fact and decodes as another.
        // A NULL DATATYPE ON THE TERM MEANS TWO DIFFERENT THINGS, and reading it as one was a
        // defect the narrow query could never expose.
        //
        // A language-tagged literal carries no datatype in SPARQL JSON, and DATATYPE() raises on it
        // here, so the erroring BIND leaves the column absent and the expected value is empty. That
        // was measured, and it is the case this check was written for.
        //
        // A PLAIN literal also carries no datatype in SPARQL JSON - but DATATYPE() answers it with
        // xsd:string, because under RDF 1.1 a simple literal IS an xsd:string. So the column says
        // xsd:string while the term says nothing, and the two agree in fact while differing in
        // spelling. Treating that as a disagreement refused a real delivery: the first broad run
        // carried forty such rows and the whole page was rejected.
        //
        // Only the narrow five-predicate query could hide this, and only because every value it
        // ever returned was an IRI.
        var expectedDatatype = valueTerm.Kind == RepeatedEnumerationRdfTermKind.Literal
            ? valueTerm.Datatype
                ?? (valueTerm.Language is { Length: > 0 } ? string.Empty : XsdString)
            : string.Empty;
        var expectedLanguage = valueTerm.Kind == RepeatedEnumerationRdfTermKind.Literal
            ? valueTerm.Language ?? string.Empty
            : string.Empty;

        if (!string.Equals(datatype, expectedDatatype, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The datatype_iri column and the value it describes disagree.", nameof(row));
        }

        if (!string.Equals(language, expectedLanguage, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The language_tag column and the value it describes disagree.", nameof(row));
        }

        var valueKind = RequirePlainLiteral(Term(row, profile, "value_kind"), "value_kind");

        _ = RequirePositiveInteger(Term(row, profile, "multiplicity"), "multiplicity");

        // All seven cursor keys. The page's own proof of what it delivered and in what order;
        // checking a subset lets a verified page prove one tuple while this producer emits another.
        RequireKey(row, profile, "key_1", draftIri);
        RequireKey(row, profile, "key_2", MarkerFor(draftTerm));
        RequireKey(row, profile, "key_3", predicateIri);
        RequireKey(row, profile, "key_4", valueTerm.Value ?? string.Empty);
        RequireKey(row, profile, "key_5", MarkerFor(valueTerm));
        RequireKey(row, profile, "key_6", datatype);
        RequireKey(row, profile, "key_7", language);

        return new LuxembourgDraftPropertyRecord(
            draftIri, predicateIri, valueTerm.Value, valueKind, datatype, language, observationId);
    }

    /// <summary>The marker the plan's own BIND must have produced for a term of this kind.</summary>
    private static string MarkerFor(RepeatedEnumerationRdfTerm term) => term.Kind switch
    {
        RepeatedEnumerationRdfTermKind.Iri => "iri",
        RepeatedEnumerationRdfTermKind.Literal => "literal",
        RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
        RepeatedEnumerationRdfTermKind.Unbound => LuxembourgDraftGraphDiscoveryPlan.UnboundKind,
        _ => throw new ArgumentOutOfRangeException(nameof(term)),
    };

    private static void RequireMarkerAgrees(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string markerName,
        RepeatedEnumerationRdfTerm term)
    {
        var marker = RequirePlainLiteral(Term(row, profile, markerName), markerName);
        if (!string.Equals(marker, MarkerFor(term), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {markerName} marker and the term it describes disagree about what was delivered.",
                nameof(row));
        }
    }

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

    /// <summary>
    /// One qualifier column, admitting the single absence this engine was measured producing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE EXCEPTION IS ONE COLUMN ON ONE KIND OF TERM, not a general permission for a qualifier to
    /// go missing. Both columns are bound by <c>IF(isLiteral(?value), ..., "")</c>, which answers an
    /// empty plain literal for anything that is not a literal, and the query's own absence branch
    /// binds both to <c>""</c> outright. So the ONLY row that can honestly arrive without a column is
    /// a language-tagged literal missing <c>datatype_iri</c>, because this Virtuoso will not answer
    /// <c>DATATYPE()</c> with <c>rdf:langString</c> and the erroring BIND drops that one column.
    /// <c>LANG()</c> answers on the same term, so <c>language_tag</c> is still delivered.
    /// </para>
    /// <para>
    /// A FIRST REPAIR HERE WAS TOO WIDE, and the review caught it. Mapping any unbound qualifier to
    /// the empty string admitted an IRI-valued row with either column missing, because that row's
    /// expected qualifier is empty too and the totalised cursor key is empty either way - so the
    /// evidence for "the publisher answered empty" and for "nothing arrived" became the same row. The
    /// retained page says which of those actually happens: of its 41 bindings 23 are IRI-valued and
    /// <c>datatype_iri</c> is present and empty in EVERY one. A missing column on such a row is not
    /// this query's answer and is refused.
    /// </para>
    /// </remarks>
    private static string ReadQualifier(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string name,
        RepeatedEnumerationRdfTerm valueTerm)
    {
        var term = Term(row, profile, name);
        if (term.Kind != RepeatedEnumerationRdfTermKind.Unbound)
        {
            return RequirePlainLiteral(term, name);
        }

        // Keyed on the VALUE TERM's own language rather than on the language COLUMN, so the exception
        // cannot be claimed by a row that simply omitted both.
        if (!string.Equals(name, "datatype_iri", StringComparison.Ordinal) ||
            valueTerm.Kind != RepeatedEnumerationRdfTermKind.Literal ||
            string.IsNullOrEmpty(valueTerm.Language))
        {
            throw new ArgumentException(
                $"{name} is absent, and this query answers it for every term but the datatype of a "
                    + "language-tagged literal.",
                nameof(row));
        }

        return string.Empty;
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
    /// The datatype is checked, not just the digits: <c>COUNT(*)</c> yields an <c>xsd:integer</c>, so
    /// a positive count arriving as an <c>xsd:string</c> is not the term this query produces. The
    /// sibling procedure-event producer accepted any datatype here and was corrected in review.
    /// </remarks>
    private static long RequirePositiveInteger(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Value is null ||
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
