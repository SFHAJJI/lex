using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
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
}

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
/// OFFLINE, AND EVIDENCE-BOUND BY CONSTRUCTION. This door performs no acquisition. It reads rows
/// that were already retained and the custody receipt for the exact bytes they came from, and every
/// expression it builds carries that receipt. There is no parameter by which a caller asserts
/// provenance, because the contract it builds into accepts none.
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
        IReadOnlyList<RepeatedEnumerationRow> expressionFactRows,
        RepeatedEnumerationInterpretationProfile expressionFactProfile,
        IReadOnlyList<RepeatedEnumerationRow> objectFactRows,
        RepeatedEnumerationInterpretationProfile objectFactProfile,
        SourceObjectRef sourceObject,
        DurableBlobWriteReceipt retainedTransportBytes,
        LanguageScopedExpressionSet into,
        out EuLanguageScopedExpressionDecodeRefusal refusal,
        out string? offendingIri)
    {
        ArgumentNullException.ThrowIfNull(expressionFactRows);
        ArgumentNullException.ThrowIfNull(expressionFactProfile);
        ArgumentNullException.ThrowIfNull(objectFactRows);
        ArgumentNullException.ThrowIfNull(objectFactProfile);
        ArgumentNullException.ThrowIfNull(sourceObject);
        ArgumentNullException.ThrowIfNull(retainedTransportBytes);
        ArgumentNullException.ThrowIfNull(into);

        refusal = EuLanguageScopedExpressionDecodeRefusal.None;
        offendingIri = null;

        var belongsToWorkIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork);
        var usesLanguageIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage);
        var workDateIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkDateDocument);

        // ---- Family X, shape-checked exactly as the reviewed object decode checks it. ----
        var rows = new List<XRow>(expressionFactRows.Count);
        foreach (var row in expressionFactRows)
        {
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
                valueKindTerm.Value!));
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
        var dates = new Dictionary<string, PublisherCorrigendumDate>(StringComparer.Ordinal);
        foreach (var row in objectFactRows)
        {
            var objectTerm = Term(row, objectFactProfile, "object");
            var predicateTerm = Term(row, objectFactProfile, "predicate");
            var valueTerm = Term(row, objectFactProfile, "value");
            var valueKindTerm = Term(row, objectFactProfile, "value_kind");

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
            if (dates.TryGetValue(canonicalWork, out var heldDate))
            {
                if (heldDate != observed)
                {
                    refusal = EuLanguageScopedExpressionDecodeRefusal.ConflictingWorkDate;
                    offendingIri = canonicalWork;
                    return null;
                }

                continue;
            }

            dates.Add(canonicalWork, observed);
        }

        // ---- Built, then checked against the destination, then appended. Never halfway. ----
        var candidates = new List<LanguageScopedExpression>(identities.Count);
        foreach (var identity in identities)
        {
            candidates.Add(LanguageScopedExpression.FromRetainedSource(
                identity,
                languages[identity],
                dates.GetValueOrDefault(identity.PublisherWorkId),
                sourceObject,
                retainedTransportBytes));
        }

        var heldBytes = new Dictionary<LanguageScopedExpressionIdentity, string>();
        foreach (var held in into.Expressions)
        {
            heldBytes[held.Identity] = held.CanonicalBytesSha256;
        }

        foreach (var candidate in candidates)
        {
            if (heldBytes.TryGetValue(candidate.Identity, out var bytes) &&
                !string.Equals(bytes, candidate.CanonicalBytesSha256, StringComparison.Ordinal))
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
        string ValueKind);
}
