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

    /// <summary>
    /// A delivered row named a property this family never asked about.
    /// </summary>
    /// <remarks>
    /// The plan binds its five predicates from a VALUES block, so an honest delivery cannot contain
    /// another. One that does is answering a question nobody asked, and admitting it would let the
    /// asked-about set this production publishes describe a delivery it does not match.
    /// </remarks>
    [JsonStringEnumMemberName("predicate_not_asked_about")]
    PredicateNotAskedAbout = 5,

    /// <summary>
    /// A delivered draft carried rows for some of the asked properties and not others.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plan asks every draft about every one of its five predicates, and its absence branch
    /// means a property the publisher holds nothing for still delivers a row. So an honest delivery
    /// carries FIVE rows per draft, and a draft carrying fewer is a partial answer.
    /// </para>
    /// <para>
    /// Admitting one would be the false absence this family exists to end, arriving by a route the
    /// unbound marker cannot describe: <c>For(draftTransposes)</c> would return an empty list for a
    /// draft whose transposition row simply never came, indistinguishable from a draft the publisher
    /// answered "none" for. An explicit unbound row and a missing row are different facts, and only
    /// the first is an answer.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("draft_property_coverage_incomplete")]
    DraftPropertyCoverageIncomplete = 6,
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
/// <see cref="ValueKind"/> is <c>unbound</c> for a property the publisher holds no value for, and
/// that row is an ANSWER rather than a gap: the plan asks for the absence by name with
/// <c>FILTER NOT EXISTS</c>. A draft with no transposition target is distinguishable here from a
/// draft nobody asked about, which is what S2-A03 requires.
/// </para>
/// </remarks>
public sealed record LuxembourgDraftPropertyRecord(
    string DraftIri,
    string PredicateIri,
    string? Value,
    string ValueKind,
    string ValueDatatypeIri,
    string ValueLanguageTag,
    string SourceObservationId);

/// <summary>Admitted draft-property records, or one typed refusal. Never both.</summary>
public sealed class LuxembourgDraftGraphProductionResult
{
    private LuxembourgDraftGraphProductionResult(
        IReadOnlyList<LuxembourgDraftPropertyRecord>? records,
        IReadOnlySet<string>? predicatesAskedAbout,
        SourceArtifactRef? completionEvidenceRef,
        LuxembourgDraftGraphProductionRefusal refusal,
        string? detail,
        int productRequestCount)
    {
        Records = records;
        PredicatesAskedAbout = predicatesAskedAbout;
        CompletionEvidenceRef = completionEvidenceRef;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    public IReadOnlyList<LuxembourgDraftPropertyRecord>? Records { get; }

    /// <summary>The properties this run actually asked every draft about.</summary>
    public IReadOnlySet<string>? PredicatesAskedAbout { get; }

    public SourceArtifactRef? CompletionEvidenceRef { get; }

    public LuxembourgDraftGraphProductionRefusal Refusal { get; }

    public string? Detail { get; }

    public int ProductRequestCount { get; }

    public bool Delivered => Refusal == LuxembourgDraftGraphProductionRefusal.None;

    internal static LuxembourgDraftGraphProductionResult Success(
        IReadOnlyList<LuxembourgDraftPropertyRecord> records,
        IReadOnlySet<string> predicatesAskedAbout,
        SourceArtifactRef completionEvidenceRef,
        int productRequestCount = 0) =>
        new(records, predicatesAskedAbout, completionEvidenceRef,
            LuxembourgDraftGraphProductionRefusal.None, null, productRequestCount);

    internal static LuxembourgDraftGraphProductionResult Refused(
        LuxembourgDraftGraphProductionRefusal refusal, string? detail, int productRequestCount = 0) =>
        new(null, null, null, refusal, detail, productRequestCount);

    /// <summary>Every delivered value of one property this run asked about.</summary>
    /// <remarks>
    /// Returns the <c>unbound</c> rows too, because they are the run's answer for drafts holding no
    /// such value. A caller wanting only held values filters on <see cref="LuxembourgDraftPropertyRecord.ValueKind"/>,
    /// which is a choice it makes explicitly rather than one this method makes for it.
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

        return DecodeRows(rows, profile, proof.AcquisitionRunRef, run.ProductRequestCount);
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
        int productRequestCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(completionEvidenceRef);

        var asked = LuxembourgDraftGraphDiscoveryPlan.AskedAbout.ToHashSet(StringComparer.Ordinal);
        var records = new List<LuxembourgDraftPropertyRecord>(rows.Count);

        foreach (var row in rows)
        {
            LuxembourgDraftPropertyRecord record;
            try
            {
                record = DecodeRow(row, profile, completionEvidenceRef.ResourceId);
            }
            catch (ArgumentException exception)
            {
                return LuxembourgDraftGraphProductionResult.Refused(
                    LuxembourgDraftGraphProductionRefusal.RowNotAdmitted,
                    exception.Message,
                    productRequestCount);
            }

            if (!asked.Contains(record.PredicateIri))
            {
                return LuxembourgDraftGraphProductionResult.Refused(
                    LuxembourgDraftGraphProductionRefusal.PredicateNotAskedAbout,
                    $"A row names {record.PredicateIri}, which this family never asked about.",
                    productRequestCount);
            }

            records.Add(record);
        }

        // EVERY DELIVERED DRAFT MUST ANSWER FOR EVERY ASKED PROPERTY. The plan's absence branch
        // guarantees a row per (draft, predicate) pair, so a draft carrying fewer than the full set
        // is a partial delivery — and admitting one would let For(predicate) return an empty list
        // for a draft whose row never came, which is indistinguishable from the publisher answering
        // "none". An explicit unbound row and a missing row are different facts.
        //
        // A delivery with no drafts at all stays valid: the class being empty is a complete answer,
        // and this loop has nothing to complain about.
        foreach (var draft in records.GroupBy(static value => value.DraftIri, StringComparer.Ordinal))
        {
            var covered = draft.Select(static value => value.PredicateIri)
                .ToHashSet(StringComparer.Ordinal);
            if (!asked.All(covered.Contains))
            {
                var missing = asked.Except(covered, StringComparer.Ordinal).Order(StringComparer.Ordinal);
                return LuxembourgDraftGraphProductionResult.Refused(
                    LuxembourgDraftGraphProductionRefusal.DraftPropertyCoverageIncomplete,
                    $"The delivery answers for {draft.Key} on only some asked properties; "
                        + "these are missing: " + string.Join(", ", missing) + ".",
                    productRequestCount);
            }
        }

        return LuxembourgDraftGraphProductionResult.Success(
            records, asked, completionEvidenceRef, productRequestCount);
    }

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

        var draftIri = RequireIri(draftTerm, "draft");
        var predicateIri = RequireIri(predicateTerm, "predicate");

        // THE QUALIFIER COLUMNS MAY BE ABSENT, and that is the publisher's own encoding rather than a
        // malformed row. EuPageDecodeClassificationTests retains the page: for a language-tagged
        // literal this engine does not answer DATATYPE() with rdf:langString, so the BIND errors and
        // the column is simply omitted from the binding. Requiring a plain literal here refused 32
        // of 373 rows on that page, and would refuse every language-tagged statusDraft value.
        var datatype = ReadQualifier(row, profile, "datatype_iri");
        var language = ReadQualifier(row, profile, "language_tag");

        // The qualifier columns must agree with the term they describe. Both are bound from
        // DATATYPE() and LANG() over that very term, so an honest delivery cannot disagree, and a
        // row that does keys as one fact and decodes as another.
        // A language-tagged literal carries no datatype in SPARQL JSON, so the term's own datatype is
        // null and the expected column is empty — which is exactly what the engine's erroring BIND
        // leaves behind. The two agree without either being taught about the other.
        var expectedDatatype = valueTerm.Kind == RepeatedEnumerationRdfTermKind.Literal
            ? valueTerm.Datatype ?? string.Empty
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
    /// One qualifier column, reading the publisher's own absence as the empty string.
    /// </summary>
    /// <remarks>
    /// Unbound is a value here rather than a malformation, because this engine leaves the column
    /// unbound for a language-tagged literal. Anything that IS present must still be the query's own
    /// unqualified plain literal: an absent column is the publisher answering nothing, while an
    /// IRI-valued one did not come from this query at all.
    /// </remarks>
    private static string ReadQualifier(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string name)
    {
        var term = Term(row, profile, name);
        return term.Kind == RepeatedEnumerationRdfTermKind.Unbound
            ? string.Empty
            : RequirePlainLiteral(term, name);
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
