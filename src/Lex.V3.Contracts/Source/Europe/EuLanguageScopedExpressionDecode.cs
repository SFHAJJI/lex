using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why <see cref="EuLanguageScopedExpressionDecode.TryDecode"/> refused a retained delivery. Closed.
/// </summary>
/// <remarks>
/// One member per named condition this door can refuse on, and no member for a row it simply does
/// not read. The distinction matters and is not pedantry: family P legitimately delivers rows about
/// objects that have no Expression in this delivery at all, and refusing those would refuse correct
/// publisher data. A row outside this door's own subject set is out of scope, not dropped; a row
/// inside it that disagrees with its own promised shape refuses here by name.
/// </remarks>
public enum EuLanguageScopedExpressionDecodeRefusal
{
    /// <summary>No refusal: every admitted expression was appended.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// A family X row's <c>parent</c>, <c>object</c>, <c>predicate</c>, <c>value_kind</c>,
    /// <c>datatype_iri</c> or <c>language_tag</c> disagreed with the shape family X promises, or its
    /// parent was not a canonicalizable pack root.
    /// </summary>
    [JsonStringEnumMemberName("expression_row_term_kind_mismatch")]
    ExpressionRowTermKindMismatch = 1,

    /// <summary>
    /// A family X row named an Expression for some predicate, but no bound
    /// <c>expression_belongs_to_work</c> row for that same Expression exists among the delivered
    /// rows. X proves its own closure rather than trusting an external enumeration; this door holds
    /// the same rule <see cref="EuCellarObjectDecodeRefusal.ExpressionSubjectNotSelfClosed"/> holds.
    /// </summary>
    [JsonStringEnumMemberName("expression_subject_not_self_closed")]
    ExpressionSubjectNotSelfClosed = 2,

    /// <summary>
    /// An Expression's <c>expression_belongs_to_work</c> value named a different Work from the one
    /// the same row's <c>parent</c> column named. Two independently projected columns describing one
    /// relation must agree, and picking either one silently would publish an identity the publisher
    /// did not state.
    /// </summary>
    [JsonStringEnumMemberName("expression_work_disagrees_with_belongs_to_work")]
    ExpressionWorkDisagreesWithBelongsToWork = 3,

    /// <summary>
    /// An admitted Expression carried no bound <c>expression_uses_language</c> row. A
    /// language-scoped expression with no observed language is not a thing this door can build, and
    /// a default language is the exact invention the contract exists to prevent.
    /// </summary>
    [JsonStringEnumMemberName("expression_language_missing")]
    ExpressionLanguageMissing = 4,

    /// <summary>
    /// One Expression carried two different <c>expression_uses_language</c> values. Choosing one
    /// would be the language-keyed merge B31-L0071 names, performed at decode instead of at write.
    /// </summary>
    [JsonStringEnumMemberName("conflicting_expression_language")]
    ConflictingExpressionLanguage = 5,

    /// <summary>
    /// A family P <c>work_date_document</c> row for a Work this delivery has Expressions of was
    /// bound to something other than a datatyped literal this door can carry verbatim.
    /// </summary>
    [JsonStringEnumMemberName("work_date_row_term_kind_mismatch")]
    WorkDateRowTermKindMismatch = 6,

    /// <summary>
    /// One Work carried two different <c>work_date_document</c> literals. Neither is preferred and
    /// neither is discarded.
    /// </summary>
    [JsonStringEnumMemberName("conflicting_work_date")]
    ConflictingWorkDate = 7,

    /// <summary>
    /// The destination set already holds one of these identities bound to different retained bytes.
    /// Nothing was appended: this is detected before the first append, never halfway through.
    /// </summary>
    [JsonStringEnumMemberName("append_conflicts_with_held_expression")]
    AppendConflictsWithHeldExpression = 8,

    /// <summary>
    /// The Expression-facts delivery would not reopen through
    /// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>. The door's own reason is reported
    /// alongside; nothing is read from rows that did not survive it.
    /// </summary>
    [JsonStringEnumMemberName("expression_rows_refused")]
    ExpressionRowsRefused = 9,

    /// <summary>The object-facts delivery would not reopen. Same door, same discipline.</summary>
    [JsonStringEnumMemberName("date_rows_refused")]
    DateRowsRefused = 10,

    /// <summary>
    /// Which page carried which row cannot be established for this delivery, so no expression can
    /// honestly cite the bytes it came from.
    /// </summary>
    /// <remarks>
    /// Only <see cref="RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal"/> makes page
    /// boundaries derivable: <c>RequireContinuation</c> enforces that every page before the last
    /// holds exactly the row limit, so row <c>i</c> is on page <c>i / limit</c>. Under
    /// <see cref="RepeatedEnumerationTerminalPagePolicy.EmptySuccessorAfterShortPage"/> a prior page
    /// may be short, and the same arithmetic would attribute rows to the wrong page - omitting a
    /// contributing body, which is the worse error, because completeness is what a provenance claim
    /// must have. So it refuses here instead of guessing.
    /// </remarks>
    [JsonStringEnumMemberName("page_attribution_unavailable")]
    PageAttributionUnavailable = 11,

    /// <summary>
    /// A page's custody receipt does not name the bytes that page actually carried, so it cannot be
    /// cited as where anything came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS EXISTS BECAUSE THE REOPEN DOOR DOES NOT CHECK IT.
    /// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> proves the ROWS - it re-derives the row,
    /// cursor and canonical-key digests from the reopened bytes - but it never reads a page's
    /// <c>DurableWriteReceipt</c>, and neither does <c>EnumerationDeliveryComparison.VerifyPages</c>.
    /// The receipt-to-payload binding lives only in that type's private <c>Resolve</c>, which is
    /// reachable from <c>Create</c> and not from <c>TryOpen</c>.
    /// </para>
    /// <para>
    /// <see cref="RepeatedEnumerationResolvedEvidence"/> is a positional record with no construction
    /// gate, so every member is init-settable by any caller. Proven rows could therefore be paired
    /// with a receipt naming bytes nobody wrote - the rows would be honest and the provenance would
    /// not. This door reads that receipt, so this door checks it.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("page_receipt_does_not_bind_its_bytes")]
    PageReceiptDoesNotBindItsBytes = 12,
}

/// <summary>
/// One query family's delivery, in the exact form
/// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> requires to prove it.
/// </summary>
/// <remarks>
/// <para>
/// THIS CARRIES ARGUMENTS, NOT AUTHORITY, AND THE DISTINCTION IS THE WHOLE POINT. Holding one of
/// these proves nothing: every field is re-checked by <c>TryOpen</c>, which re-derives the row,
/// cursor and canonical-key digests from the reopened page bytes and compares them against the
/// proof's and the comparison's own claims. A caller who substitutes pages, or pairs a genuine proof
/// with an unrelated comparison, is refused there rather than believed here.
/// </para>
/// <para>
/// No smaller "already verified" envelope is offered on purpose. A type this file could mint from
/// bare parts would just relocate the substitution door it exists to close, whereas
/// <see cref="EnumerationDeliveryComparison"/> has a private constructor whose only door is its own
/// <c>Create</c>.
/// </para>
/// </remarks>
/// <param name="PagesInOrder">
/// The delivery's reopened pages, in page order. These are also the provenance: the lineage this
/// decode records is built from their write receipts, never from a caller-chosen receipt.
/// </param>
public sealed record EuProofBoundDelivery(
    AbsenceFamilyEnumerationProof Proof,
    EnumerationDeliveryComparison Comparison,
    RepeatedEnumerationInterpretationProfile Profile,
    SourceArtifactRef ProfileRef,
    SourceArtifactRef CountHttpEvidenceRef,
    IReadOnlyList<RepeatedEnumerationResolvedEvidence> PagesInOrder);

/// <summary>
/// Turns one retained EU publisher delivery into language-scoped expressions, one per Expression the
/// publisher actually stated.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS AT ALL, STATED AGAINST THE CODE IT STANDS BESIDE.
/// <see cref="EuCellarObjectDecode"/> reads the same family X delivery and reduces it to a single
/// <c>EuLanguageExpressionObservation</c>: it asks for English, then for French, then falls through.
/// So a Work with an English and a French Expression reports English; two French corrigendum
/// Expressions of one Work report one; an Expression in any of the other twenty-two official
/// languages reports as the fallback rather than as itself. That is precisely B31-L0071's
/// "writer's language-keyed merge", sitting in the reader.
/// </para>
/// <para>
/// The evidence it discards is already measured in this build.
/// <see cref="EuOfficialLanguage"/>'s own remarks record that corrigendum language metadata is
/// retained in every observed language, that 385 corrigenda have no ENG or FRA counterpart at all,
/// and that Cellar carries 94 distinct language values. This door adds the representation those
/// records need. It does not change the older fold, because replacing a reviewed reader is a larger
/// question than adding the carrier it lacks.
/// </para>
/// <para>
/// LANGUAGE IS CARRIED AS THE PUBLISHER'S OWN IRI, NOT MAPPED ONTO THE SCOPE ENUM. This is the whole
/// point and the easiest thing to get wrong. <see cref="EuOfficialLanguage"/> is closed at the
/// official twenty-four; the publisher emits ninety-four. Mapping here would re-erase exactly what
/// those remarks say must not be erased, and it would do it silently, because an unmappable language
/// has nowhere to go but a default. So the authority IRI travels verbatim, and a language this
/// build has no enum member for survives decode intact.
/// </para>
/// <para>
/// OFFLINE, AND EVIDENCE-BOUND BY CONSTRUCTION. This door performs no acquisition, and it does not
/// accept rows. It takes the inputs <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> requires,
/// reopens the delivery through that door, and builds every expression's provenance from the pages
/// it was proven over - having first checked that each of those pages' receipts names the bytes that
/// page carried, because <c>TryOpen</c> proves the rows and does not check the receipts.
/// </para>
/// <para>
/// AN EARLIER VERSION OF THIS PARAGRAPH SAID "there is no parameter by which a caller asserts
/// provenance" AND THAT WAS STILL NOT TRUE. The rows had been made proof-bound, but
/// <see cref="RepeatedEnumerationResolvedEvidence"/> has no construction gate, so a caller could pair
/// genuinely proven rows with a self-minted write receipt and this door would have cited it. That is
/// what <see cref="EuLanguageScopedExpressionDecodeRefusal.PageReceiptDoesNotBindItsBytes"/> closes.
/// The lesson is recorded rather than the sentence quietly corrected: a safety claim written in a
/// comment is worth nothing until the check it describes is pointed at.
/// </para>
/// <para>
/// AN EARLIER HEAD OF THIS FILE DID ACCEPT ROWS, paired with a caller-chosen write receipt, while
/// its own remarks claimed no caller could assert provenance. That claim was false in effect: any
/// well-shaped rows could be presented beside any valid receipt. The same defect had already been
/// found and repaired once in this repository - <c>EuProcedureEventProducer</c>'s remarks record
/// that its <c>DecodeRows</c> was public and took "a caller-supplied row list and a caller-supplied
/// evidence reference, so observations could be minted from rows nobody had proven, citing custody
/// nobody had established" - and it was reintroduced here by not reading that precedent first.
/// </para>
/// <para>
/// WHAT THIS DOES NOT YET DO, so the next slice does not assume it. Nothing here removes or
/// supersedes <see cref="EuCellarObjectDecode"/>'s one-language fold. When this set is wired into
/// production, that fold must not stand in for it or suppress any expression it admits: the two
/// read the same family and only one of them can represent more than a single expression per work.
/// </para>
/// </remarks>
public static class EuLanguageScopedExpressionDecode
{
    /// <summary>
    /// Appends one language-scoped expression per stated Expression, or refuses without appending.
    /// </summary>
    /// <param name="expressionFactRows">The retained family X (Expression-facts) rows.</param>
    /// <param name="expressionFactProfile">The profile family X was projected under.</param>
    /// <param name="objectFactRows">
    /// The retained family P (object-facts) rows. Read only for <c>work_date_document</c> on Works
    /// this delivery has Expressions of; every other row is outside this door's subject set.
    /// </param>
    /// <param name="objectFactProfile">The profile family P was projected under.</param>
    /// <param name="sourceObject">The source object this delivery belongs to.</param>
    /// <param name="retainedTransportBytes">The custody receipt for the exact retained bytes.</param>
    /// <param name="into">
    /// The append-only set to append into. On refusal it is left exactly as it was found.
    /// </param>
    /// <param name="refusal">
    /// The named refusal, or <see cref="EuLanguageScopedExpressionDecodeRefusal.None"/>.
    /// </param>
    /// <param name="offendingIri">The IRI a refusal is about, when the refusal has one.</param>
    /// <returns>The expressions appended, in the order the publisher first stated them.</returns>
    /// <exception cref="ArgumentNullException">
    /// A caller contract violation, not a reviewable data disagreement.
    /// </exception>
    public static IReadOnlyList<LanguageScopedExpression>? TryDecode(
        EuProofBoundDelivery expressionFacts,
        EuProofBoundDelivery? objectFacts,
        SourceObjectRef sourceObject,
        LanguageScopedExpressionSet into,
        out EuLanguageScopedExpressionDecodeRefusal refusal,
        out string? refusalDetail,
        out string? offendingIri)
    {
        ArgumentNullException.ThrowIfNull(expressionFacts);
        ArgumentNullException.ThrowIfNull(sourceObject);
        ArgumentNullException.ThrowIfNull(into);

        refusal = EuLanguageScopedExpressionDecodeRefusal.None;
        refusalDetail = null;
        offendingIri = null;

        // Rows are never taken from a caller. They are reopened from the retained page bytes and
        // re-checked against the proof's delivered row count and canonical-key digest and the
        // comparison's row and cursor digests, which is what makes the lineage below a fact about
        // these bytes rather than a caller's pairing of some rows with some receipt.
        // The receipts were checked above. TryOpen is called here rather than through a local helper
        // on purpose.
        // VerifiedRepeatedEnumerationRowsConstructionSurfaceTests pins that exactly three places in
        // Contracts hand out rows parsed from bytes, and a private wrapper returning rows would be a
        // fourth. It would add no safety - it only forwards - so the pin is respected rather than
        // amended, and the inner reason travels as a detail string exactly as EuProcedureEventProducer
        // reports its own refused reopen.
        var expressionFactProfile = expressionFacts.Profile;
        var expressionFactRows = VerifiedRepeatedEnumerationRows.TryOpen(
            expressionFacts.Proof,
            expressionFacts.Comparison,
            expressionFactProfile,
            expressionFacts.ProfileRef,
            expressionFacts.CountHttpEvidenceRef,
            expressionFacts.PagesInOrder,
            out var expressionRowsRefusal);
        if (expressionFactRows is null)
        {
            refusal = EuLanguageScopedExpressionDecodeRefusal.ExpressionRowsRefused;
            refusalDetail = expressionRowsRefusal.ToString();
            return null;
        }

        IReadOnlyList<RepeatedEnumerationRow> objectFactRows = [];
        RepeatedEnumerationInterpretationProfile? objectFactProfile = null;
        if (objectFacts is not null)
        {
            objectFactProfile = objectFacts.Profile;
            var openedObjectFacts = VerifiedRepeatedEnumerationRows.TryOpen(
                objectFacts.Proof,
                objectFacts.Comparison,
                objectFactProfile,
                objectFacts.ProfileRef,
                objectFacts.CountHttpEvidenceRef,
                objectFacts.PagesInOrder,
                out var dateRowsRefusal);
            if (openedObjectFacts is null)
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.DateRowsRefused;
                refusalDetail = dateRowsRefusal.ToString();
                return null;
            }

            objectFactRows = openedObjectFacts;
        }

        // Now that the rows are proven, the receipts this decode will CITE must name the pages that
        // actually carried them. Deliberately after TryOpen, not before: substituted bytes break the
        // receipt binding too, so checking first would answer every byte substitution with a receipt
        // complaint and leave the row-proof refusals with no case that reaches them. Rows first, then
        // the provenance about those rows.
        if (!EveryPageReceiptBindsItsBytes(expressionFacts, out offendingIri) ||
            (objectFacts is not null && !EveryPageReceiptBindsItsBytes(objectFacts, out offendingIri)))
        {
            refusal = EuLanguageScopedExpressionDecodeRefusal.PageReceiptDoesNotBindItsBytes;
            return null;
        }

        var belongsToWorkIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork);
        var usesLanguageIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage);
        var workDateIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkDateDocument);

        // ---- Family X, shape-checked exactly as the reviewed object decode checks it. ----
        var expressionPages = PageOfEachRow(expressionFacts, expressionFactRows.Count);
        if (expressionPages is null)
        {
            refusal = EuLanguageScopedExpressionDecodeRefusal.PageAttributionUnavailable;
            return null;
        }

        var rows = new List<XRow>(expressionFactRows.Count);
        foreach (var row in expressionFactRows)
        {
            var rowPageIndex = expressionPages[rows.Count];
            var parentTerm = Term(row, expressionFactProfile, "parent");
            var objectTerm = Term(row, expressionFactProfile, "object");
            var predicateTerm = Term(row, expressionFactProfile, "predicate");
            var valueTerm = Term(row, expressionFactProfile, "value");
            var valueKindTerm = Term(row, expressionFactProfile, "value_kind");
            var datatypeTerm = Term(row, expressionFactProfile, "datatype_iri");
            var languageTerm = Term(row, expressionFactProfile, "language_tag");

            if (parentTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || parentTerm.Value is null ||
                objectTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || objectTerm.Value is null ||
                predicateTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || predicateTerm.Value is null ||
                !IsPlainLiteral(valueKindTerm) || !IsPlainLiteral(languageTerm) ||
                !IsDatatypeTermThePublisherCanProduce(datatypeTerm, languageTerm) ||
                (valueTerm.Kind == RepeatedEnumerationRdfTermKind.Unbound) != (valueKindTerm.Value == "unbound"))
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.ExpressionRowTermKindMismatch;
                return null;
            }

            var canonicalParent = EuPackRootCanonicalForm.TryCanonicalize(parentTerm.Value, out _);
            if (canonicalParent is null)
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.ExpressionRowTermKindMismatch;
                offendingIri = parentTerm.Value;
                return null;
            }

            rows.Add(new XRow(
                canonicalParent,
                objectTerm.Value,
                predicateTerm.Value,
                valueTerm,
                valueKindTerm.Value!,
                rowPageIndex));
        }

        // ---- Self closure: an Expression is admitted only where X itself says it belongs. ----
        var selfClosed = new HashSet<string>(
            rows.Where(row => row.PredicateIri == belongsToWorkIri && row.ValueKind != "unbound")
                .Select(static row => row.Expression),
            StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!selfClosed.Contains(row.Expression))
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.ExpressionSubjectNotSelfClosed;
                offendingIri = row.Expression;
                return null;
            }
        }

        // ---- The two columns stating one relation must agree. ----
        foreach (var row in rows)
        {
            if (row.PredicateIri != belongsToWorkIri || row.ValueKind == "unbound")
            {
                continue;
            }

            var statedWork = row.ValueKind == "iri" && row.Value.Value is not null
                ? EuPackRootCanonicalForm.TryCanonicalize(row.Value.Value, out _)
                : null;
            if (statedWork is null || !string.Equals(statedWork, row.Parent, StringComparison.Ordinal))
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.ExpressionWorkDisagreesWithBelongsToWork;
                offendingIri = row.Value.Value ?? row.Expression;
                return null;
            }
        }

        // ---- One identity per (Work, Expression) pair, in first-stated order. ----
        var identities = new List<LanguageScopedExpressionIdentity>();
        var seenIdentities = new HashSet<LanguageScopedExpressionIdentity>();
        foreach (var row in rows)
        {
            var identity = new LanguageScopedExpressionIdentity(row.Parent, row.Expression);
            if (seenIdentities.Add(identity))
            {
                identities.Add(identity);
            }
        }

        // ---- Language, carried verbatim, never merged and never defaulted. ----
        var languages = new Dictionary<LanguageScopedExpressionIdentity, string>();
        foreach (var row in rows)
        {
            if (row.PredicateIri != usesLanguageIri || row.ValueKind == "unbound")
            {
                continue;
            }

            if (row.ValueKind != "iri" || row.Value.Value is null)
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.ExpressionRowTermKindMismatch;
                offendingIri = row.Expression;
                return null;
            }

            var identity = new LanguageScopedExpressionIdentity(row.Parent, row.Expression);
            if (languages.TryGetValue(identity, out var heldLanguage))
            {
                if (!string.Equals(heldLanguage, row.Value.Value, StringComparison.Ordinal))
                {
                    refusal = EuLanguageScopedExpressionDecodeRefusal.ConflictingExpressionLanguage;
                    offendingIri = row.Expression;
                    return null;
                }

                continue;
            }

            languages.Add(identity, row.Value.Value);
        }

        foreach (var identity in identities)
        {
            if (!languages.ContainsKey(identity))
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.ExpressionLanguageMissing;
                offendingIri = identity.PublisherExpressionId;
                return null;
            }
        }

        // ---- The publisher's own work_date_document, for these Works only. ----
        var works = new HashSet<string>(
            identities.Select(static identity => identity.PublisherWorkId), StringComparer.Ordinal);
        var dates = new Dictionary<string, (PublisherCorrigendumDate Date, int PageIndex)>(
            StringComparer.Ordinal);
        int[]? datePages = null;
        if (objectFacts is not null)
        {
            datePages = PageOfEachRow(objectFacts, objectFactRows.Count);
            if (datePages is null)
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.PageAttributionUnavailable;
                return null;
            }
        }

        for (var dateRowIndex = 0; dateRowIndex < objectFactRows.Count; dateRowIndex++)
        {
            var row = objectFactRows[dateRowIndex];
            var objectTerm = Term(row, objectFactProfile!, "object");
            var predicateTerm = Term(row, objectFactProfile!, "predicate");
            var valueTerm = Term(row, objectFactProfile!, "value");
            var valueKindTerm = Term(row, objectFactProfile!, "value_kind");

            if (objectTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || objectTerm.Value is null ||
                predicateTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || predicateTerm.Value is null ||
                !IsPlainLiteral(valueKindTerm))
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.WorkDateRowTermKindMismatch;
                return null;
            }

            if (predicateTerm.Value != workDateIri || valueKindTerm.Value == "unbound")
            {
                // Outside this door's subject set, or asked and unanswered. Neither is a drop: the
                // first is another door's row, and the second is an absence the contract represents
                // as the absence of a date object rather than as a date.
                continue;
            }

            var canonicalWork = EuPackRootCanonicalForm.TryCanonicalize(objectTerm.Value, out _);
            if (canonicalWork is null || !works.Contains(canonicalWork))
            {
                continue;
            }

            if (valueTerm.Kind != RepeatedEnumerationRdfTermKind.Literal || valueTerm.Value is null ||
                string.IsNullOrEmpty(valueTerm.Datatype))
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.WorkDateRowTermKindMismatch;
                offendingIri = canonicalWork;
                return null;
            }

            var observed = new PublisherCorrigendumDate(valueTerm.Value, valueTerm.Datatype);
            if (dates.TryGetValue(canonicalWork, out var held))
            {
                var heldDate = held.Date;
                if (heldDate != observed)
                {
                    refusal = EuLanguageScopedExpressionDecodeRefusal.ConflictingWorkDate;
                    offendingIri = canonicalWork;
                    return null;
                }

                continue;
            }

            dates.Add(canonicalWork, (observed, datePages![dateRowIndex]));
        }

        // ---- Built, then checked against the destination, then appended. Never halfway. ----
        // An expression cites the pages that carried ITS OWN rows, and nothing else. A page that
        // delivered none of this expression's rows did not state its identity or its language, and
        // the contribution vocabulary says that is what the role means - so a delivery-wide lineage
        // would attach a role to bytes that never carried the thing it names. The terminating empty
        // page is therefore cited by nothing: it witnesses that the enumeration finished, which is a
        // property of the proof-bound delivery and is already carried there.
        var pagesByIdentity = new Dictionary<LanguageScopedExpressionIdentity, SortedSet<int>>();
        foreach (var row in rows)
        {
            var identity = new LanguageScopedExpressionIdentity(row.Parent, row.Expression);
            if (!pagesByIdentity.TryGetValue(identity, out var pages))
            {
                pages = [];
                pagesByIdentity.Add(identity, pages);
            }

            pages.Add(row.PageIndex);
        }

        var candidates = new List<LanguageScopedExpression>(identities.Count);
        foreach (var identity in identities)
        {
            var entries = pagesByIdentity[identity]
                .Select(page => new LanguageScopedExpressionLineageEntry(
                    LanguageScopedExpressionContribution.IdentityAndLanguage,
                    expressionFacts.PagesInOrder[page].DurableWriteReceipt))
                .ToList();

            // Date lineage attaches only where a date was actually observed, and names only the page
            // that stated it. An expression whose work the publisher dated nothing for must not cite
            // the date family at all, and the contract refuses that pairing at its own door.
            PublisherCorrigendumDate? observedDate = null;
            if (dates.TryGetValue(identity.PublisherWorkId, out var dated))
            {
                observedDate = dated.Date;
                entries.Add(new LanguageScopedExpressionLineageEntry(
                    LanguageScopedExpressionContribution.PublisherDate,
                    objectFacts!.PagesInOrder[dated.PageIndex].DurableWriteReceipt));
            }

            candidates.Add(LanguageScopedExpression.FromRetainedSource(
                identity,
                languages[identity],
                observedDate,
                sourceObject,
                LanguageScopedExpressionLineage.FromContributions(entries)));
        }

        var heldContent = new Dictionary<LanguageScopedExpressionIdentity, string>();
        foreach (var held in into.Expressions)
        {
            heldContent[held.Identity] = held.CanonicalContentSha256;
        }

        foreach (var candidate in candidates)
        {
            if (heldContent.TryGetValue(candidate.Identity, out var content) &&
                !string.Equals(content, candidate.CanonicalContentSha256, StringComparison.Ordinal))
            {
                refusal = EuLanguageScopedExpressionDecodeRefusal.AppendConflictsWithHeldExpression;
                offendingIri = candidate.Identity.PublisherExpressionId;
                return null;
            }
        }

        foreach (var candidate in candidates)
        {
            if (!into.TryAppend(candidate, out var appendRefusal))
            {
                // Unreachable given the pre-check above, and an exception rather than a refusal
                // member precisely because it would mean this door's own invariant broke, not that
                // the publisher disagreed with itself.
                throw new InvalidOperationException(
                    $"The pre-checked append refused with '{appendRefusal}'.");
            }
        }

        return candidates.AsReadOnly();
    }

    /// <summary>
    /// Whether every page's custody receipt names the exact bytes that page carried.
    /// </summary>
    /// <remarks>
    /// The same content-address check <c>EnumerationDeliveryComparison</c>'s private <c>Resolve</c>
    /// performs when it mints a comparison. It is repeated here rather than relied upon because the
    /// reopen path this door is given does not go through that private door, and a provenance claim
    /// that rests on an unchecked field is not a provenance claim.
    /// </remarks>
    private static bool EveryPageReceiptBindsItsBytes(
        EuProofBoundDelivery delivery, out string? offendingDigest)
    {
        foreach (var page in delivery.PagesInOrder)
        {
            var carried = Convert.ToHexString(
                SHA256.HashData(page.RetainedPayloadBytes.Span)).ToLowerInvariant();
            if (!string.Equals(
                carried, page.DurableWriteReceipt.Reference.ContentSha256, StringComparison.Ordinal))
            {
                offendingDigest = page.DurableWriteReceipt.Reference.ContentSha256;
                return false;
            }
        }

        offendingDigest = null;
        return true;
    }

    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string variableName)
    {
        var index = -1;
        for (var position = 0; position < profile.ProjectionVariables.Count; position++)
        {
            if (string.Equals(
                profile.ProjectionVariables[position], variableName, StringComparison.Ordinal))
            {
                index = position;
                break;
            }
        }

        if (index < 0)
        {
            throw new ArgumentException(
                $"'{variableName}' is not part of this profile's projection.", nameof(variableName));
        }

        if (index >= row.Terms.Count)
        {
            throw new ArgumentException(
                $"A row has {row.Terms.Count} term(s), too few to read '{variableName}' at " +
                $"projection position {index}.",
                nameof(row));
        }

        return row.Terms[index];
    }

    private static bool IsPlainLiteral(RepeatedEnumerationRdfTerm term) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal
        && term.Datatype is null
        && term.Language is null;

    /// <summary>
    /// The publisher omits <c>datatype_iri</c> on a language tagged literal, because the endpoint
    /// does not answer <c>DATATYPE()</c> on one. Admitted only in that exact pairing, exactly as
    /// <see cref="EuCellarObjectDecode"/> admits it, and never filled in.
    /// </summary>
    private static bool IsDatatypeTermThePublisherCanProduce(
        RepeatedEnumerationRdfTerm datatypeTerm,
        RepeatedEnumerationRdfTerm languageTerm) =>
        IsPlainLiteral(datatypeTerm)
        || (datatypeTerm.Kind == RepeatedEnumerationRdfTermKind.Unbound
            && IsPlainLiteral(languageTerm)
            && !string.IsNullOrEmpty(languageTerm.Value));

    private sealed record XRow(
        string Parent,
        string Expression,
        string PredicateIri,
        RepeatedEnumerationRdfTerm Value,
        string ValueKind,
        int PageIndex);

    /// <summary>
    /// Which page delivered each row, or <see langword="null"/> when that cannot be established.
    /// </summary>
    /// <remarks>
    /// Returns indices, never rows. A helper handing back rows would be a fourth place in Contracts
    /// that produces them, and <c>VerifiedRepeatedEnumerationRowsConstructionSurfaceTests</c> pins
    /// that there are exactly three.
    /// </remarks>
    private static int[]? PageOfEachRow(EuProofBoundDelivery delivery, int rowCount)
    {
        if (delivery.Profile.TerminalPagePolicy
            != RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal)
        {
            return null;
        }

        var limit = delivery.PagesInOrder[0].QueryPlan.ResponseCardinality.RowLimit;
        if (limit is not > 0)
        {
            return null;
        }

        var pages = new int[rowCount];
        for (var index = 0; index < rowCount; index++)
        {
            var page = (int)(index / limit.Value);
            if (page >= delivery.PagesInOrder.Count)
            {
                return null;
            }

            pages[index] = page;
        }

        return pages;
    }
}
