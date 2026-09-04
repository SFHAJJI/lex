using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why <see cref="EuCellarObjectDecode.TryDecode"/> refused to hand back snapshots. Closed.
/// </summary>
/// <remarks>
/// D1-05c-1 (SCOPE_RULING <c>lex-event-20260904T040718222Z-7e6f29af07024cf5b2cb716f94f288e3</c>)
/// extends D1-05b's three-member set with seven more, one per new named refusal condition this
/// door's own extension introduces. No member here drops a row silently: every row this door reads
/// from any of its three row sets is either folded into one of the snapshots it builds or is the
/// exact row that produced one of these refusals.
/// </remarks>
public enum EuCellarObjectDecodeRefusal
{
    /// <summary>No refusal: the snapshots were admitted.</summary>
    None = 0,

    /// <summary>
    /// A family row's <c>base_celex</c>, <c>base</c> or <c>state</c> term disagreed with the shape
    /// the family projection promises. Unchanged from D1-05b.
    /// </summary>
    FamilyRowTermKindMismatch = 1,

    /// <summary>
    /// A family row's <c>base_celex</c> or <c>base</c> disagreed with this call's fixed root
    /// identity. Unchanged from D1-05b.
    /// </summary>
    DuplicateSingleValuedBinding = 2,

    /// <summary>
    /// <see cref="EuCellarObjectSnapshot.TryObserve"/> itself refused one object's assembled
    /// observation set. The exact inner reason is reported alongside this member.
    /// </summary>
    ObjectSnapshotRejected = 3,

    /// <summary>
    /// An object-facts (family P) row's <c>object</c>, <c>predicate</c>, <c>value_kind</c>,
    /// <c>datatype_iri</c> or <c>language_tag</c> term disagreed with the shape the family P
    /// projection promises, or P's own delivery for one object omitted an outcome row (a real value
    /// or an explicit unbound row) for one of the predicates this closed door expects every object
    /// in <c>O</c> to carry an outcome for.
    /// </summary>
    ObjectFactRowTermKindMismatch = 4,

    /// <summary>
    /// A family P row named an <c>object</c> that is not a member of this call's own closure
    /// <c>O</c> (the root plus every state <c>familyRows</c> discovered). The offending canonical
    /// IRI is reported through <c>offendingIri</c>.
    /// </summary>
    ObjectFactRowNotInClosure = 5,

    /// <summary>
    /// An Expression-facts (family X) row's <c>parent</c>, <c>object</c>, <c>predicate</c>,
    /// <c>value_kind</c>, <c>datatype_iri</c> or <c>language_tag</c> term disagreed with the shape
    /// the family X projection promises.
    /// </summary>
    ExpressionFactRowTermKindMismatch = 6,

    /// <summary>
    /// A family X row's <c>parent</c> (the Work an Expression belongs to) is not a member of this
    /// call's own closure <c>O</c>. The offending canonical parent IRI is reported through
    /// <c>offendingIri</c>.
    /// </summary>
    ExpressionParentNotInClosure = 7,

    /// <summary>
    /// A family X row named an Expression (<c>object</c>) for some predicate, but no
    /// <c>expression_belongs_to_work</c> row for that same Expression exists among X's own
    /// delivered rows: X proves its own closure rather than trusting an external Expression
    /// enumeration, per the SCOPE_RULING's second X closure rule. The offending Expression IRI is
    /// reported through <c>offendingIri</c>.
    /// </summary>
    ExpressionSubjectNotSelfClosed = 8,

    /// <summary>
    /// Family P's own <c>ConsolidatedBasedOn</c> edges for one object disagree with what
    /// <c>familyRows</c> independently established for that same object: the root carried a
    /// nonempty edge, or a state's edges were not exactly the one edge targeting this call's own
    /// root. Two independently delivered families describing the same relation must agree; this is
    /// the typed refusal when they do not.
    /// </summary>
    ConsolidatedBasedOnEdgeDisagreesWithFamily = 9,

    /// <summary>
    /// The content class family P's own type assertions derive for one object (root or state)
    /// disagrees with the content class its closure position requires (a root must derive
    /// <see cref="EuContentClass.OriginalLegalText"/>, a state must derive
    /// <see cref="EuContentClass.Consolidation"/>), per the SCOPE_RULING's fourth precision: closure
    /// position is a consistency check on the publisher's own assertion, never a silent override of
    /// it.
    /// </summary>
    ContentClassClosurePositionMismatch = 10,
}

/// <summary>
/// The decode from a verified EU consolidation closure - D1-05a's family census plus D1-05c-1's own
/// object-facts, Expression-facts and root-watermark row sets
/// (<see cref="EuObjectFactsDiscoveryPlan"/>) - into one typed <see cref="EuCellarObjectSnapshot"/>
/// per object the closure discovers.
/// </summary>
/// <remarks>
/// <para>
/// D1-05b (SCOPE_RULING <c>lex-event-20260904T015609998Z-bb7cc08f556347f5a5455a58f810b9ee</c>) built
/// the first version of this door: one snapshot, for the root alone, with every predicate but the
/// root's own CELEX honestly <see cref="EuPredicateObservationState.NotObserved"/> because that
/// closure never asked anything else. D1-05c-1 (SCOPE_RULING
/// <c>lex-event-20260904T040718222Z-7e6f29af07024cf5b2cb716f94f288e3</c>) extends this door rather
/// than replacing it: the object universe this call decodes, <c>O</c>, is still exactly the root plus
/// every state <c>familyRows</c> discovers (family P's and family X's own rows never grow or shrink
/// <c>O</c>; a row naming an object outside it is refused, never silently admitted). What changes is
/// that every object in <c>O</c> - the root and every discovered state alike - now gets its own real
/// snapshot built from its own rows in family P (the nine object-authority CDM predicates plus the
/// four read relation families, asked uniformly of every object as subject) and family X (the four
/// Expression-authority CDM predicates, asked of the Expressions of <c>O</c>).
/// </para>
/// <para>
/// The edge-placement move (SCOPE_RULING precision two). D1-05b recorded a discovered state as an
/// <see cref="EuRelationAuthority.OntologyAuthorizedInverse"/> edge on the <em>root's own</em>
/// snapshot: "the root has this state as a consolidated derivative", the inverse of what the
/// publisher actually asserts (<c>state consolidated_based_on base</c>). Candidate 5 R4 is explicit
/// that an inverse may be derived only from an exact mapping frozen in the pinned ontology, and CDM's
/// declared inverse is not established, so that inverse was never authorised. Family P asks
/// <c>act_consolidated_based_on_resource_legal</c> uniformly, object-as-subject, of every object in
/// <c>O</c>: for the root this predicate is asked and (ordinarily) unbound, so the root's own
/// <see cref="EuRelationFamily.ConsolidatedBasedOn"/> family is
/// <see cref="EuRelationAcquisitionState.Complete"/> with zero edges, a real negative fact rather
/// than a coverage gap; for a state this predicate is asked and bound to the state's own base, so
/// that edge is recorded on the <em>state's own</em> snapshot as
/// <see cref="EuRelationAuthority.PublisherAsserted"/> - the direction the publisher actually wrote.
/// No special-casing was needed to reach this shape: it falls out of asking the same four relation
/// predicates the same way of every object in <c>O</c>. D1-05b's own
/// <c>BuildConsolidatedBasedOnEdge</c> inverse builder is retired along with its
/// <c>OntologyAuthorizedInverse</c> authority; nothing this door produces carries that authority any
/// more, for any object.
/// </para>
/// <para>
/// Decision 64's "not observed" rule for family X. Family X only ever describes Expressions, never a
/// Work or a consolidated state directly; asking an Expression-authority predicate of a plain Work
/// object would manufacture a false <see cref="EuPredicateObservationState.ObservedAbsent"/> for a
/// predicate nothing ever asked about that subject. So the four Expression-authority
/// <see cref="EuCdmPredicate"/> members stay <see cref="EuPredicateObservationState.NotObserved"/> on
/// every object's own predicate-observation set, unconditionally: family P's own VALUES predicate
/// list never includes them, so no object-facts row could ever bear one, and this door does not
/// invent one from family X's rows either. Family X's own facts live only in the language
/// observation below.
/// </para>
/// <para>
/// Content class (SCOPE_RULING precision four). Family P's <c>work_has_resource-type</c> rows are
/// the publisher's own type assertion; this door reads whether any of an object's values names the
/// EU Vocabularies consolidated-act resource-type
/// (<c>http://publications.europa.eu/resource/authority/resource-type/CONSOLID_ACT</c>) and derives
/// <see cref="EuContentClass.Consolidation"/> when it does, <see cref="EuContentClass.OriginalLegalText"/>
/// otherwise - the only two content classes this closure's object universe (roots and consolidated
/// states) can ever produce. Closure position is then a consistency check, never the source of
/// truth: a root whose own type assertions derive <c>Consolidation</c>, or a state whose own type
/// assertions derive <c>OriginalLegalText</c>, refuses with
/// <see cref="EuCellarObjectDecodeRefusal.ContentClassClosurePositionMismatch"/> rather than silently
/// picking one disagreeing signal over the other.
/// </para>
/// <para>
/// Language (queue item 18's own line: "the language observation filled from X"). Because
/// <see cref="EuCellarObjectSnapshot.Language"/> still carries only one language of interest pending
/// D1-05d's own widening to every selected Expression language, this door narrows that one language
/// to the two <see cref="EuLanguageBodyDisposition.BodyCandidateLanguages"/> the reviewed scope ever
/// fetches a body for: English if family X observes an English Expression for the object, French if
/// it observes a French one and not an English one, and an explicit
/// <see cref="EuExpressionObservationState.NotObserved"/> English observation when family X observes
/// neither - never <c>ExpressionObservedBodyHeld</c>, since no body-acquisition machinery exists in
/// this closure at all (format stays null; D1-05d's own manifestation slice is what could ever set
/// body-held true). This is a judgement call the SCOPE_RULING text does not itself resolve (which of
/// several observed languages counts as "the" one of interest); the reviewer can revisit it under
/// D1-05d without this door's own row-reading logic changing.
/// </para>
/// <para>
/// Contracts-only: nothing here calls a store, a publisher endpoint, or <c>Lex.V3.Ingest</c>.
/// </para>
/// </remarks>
public static class EuCellarObjectDecode
{
    private const string ConsolidatedActResourceTypeIri =
        "http://publications.europa.eu/resource/authority/resource-type/CONSOLID_ACT";
    private const string EnglishLanguageAuthorityIri =
        "http://publications.europa.eu/resource/authority/language/ENG";
    private const string FrenchLanguageAuthorityIri =
        "http://publications.europa.eu/resource/authority/language/FRA";

    /// <summary>The only path that decodes a closure's row sets into its object snapshots.</summary>
    /// <param name="requestedCelex">The exact admitted seed CELEX this closure was enumerated for.</param>
    /// <param name="familyRows">
    /// D1-05a's family query set's rows: the census that discovers <c>O</c> (the root plus every
    /// state). Already reopened and re-verified. May be empty: a root with no discovered
    /// consolidated states is a real, complete, negative observation.
    /// </param>
    /// <param name="familyProfile">The interpretation profile <paramref name="familyRows"/> were verified under.</param>
    /// <param name="objectFactRows">
    /// Family P's rows: the nine object-authority CDM predicates plus the four read relation
    /// families, for every object in <c>O</c>. Already reopened and re-verified.
    /// </param>
    /// <param name="objectFactProfile">The interpretation profile <paramref name="objectFactRows"/> were verified under.</param>
    /// <param name="expressionFactRows">
    /// Family X's rows: the four Expression-authority CDM predicates, for the Expressions of
    /// <c>O</c>. Already reopened and re-verified. May be empty: an object with no Expression at all
    /// is a real, complete, negative observation.
    /// </param>
    /// <param name="expressionFactProfile">The interpretation profile <paramref name="expressionFactRows"/> were verified under.</param>
    /// <param name="recordForm">
    /// Every object's own act form. Not recoverable from these closures' rows; the caller supplies it
    /// from wherever it independently resolves <c>resource_legal_type</c>. Applied uniformly to every
    /// object this call decodes, exactly as D1-05b applied it to the one root it decoded.
    /// </param>
    /// <param name="evidenceRef">
    /// The evidence every observation in every returned snapshot rests on. There is no finer per-row
    /// evidence identity available from verified rows alone.
    /// </param>
    /// <param name="refusal">Why no snapshots were returned, when none were.</param>
    /// <param name="offendingIri">
    /// The exact canonical IRI a closure-boundary refusal names, when <paramref name="refusal"/> is
    /// <see cref="EuCellarObjectDecodeRefusal.ObjectFactRowNotInClosure"/>,
    /// <see cref="EuCellarObjectDecodeRefusal.ExpressionParentNotInClosure"/> or
    /// <see cref="EuCellarObjectDecodeRefusal.ExpressionSubjectNotSelfClosed"/>; otherwise <c>null</c>.
    /// </param>
    /// <param name="snapshotRefusal">
    /// The inner reason, when <paramref name="refusal"/> is
    /// <see cref="EuCellarObjectDecodeRefusal.ObjectSnapshotRejected"/>; otherwise
    /// <see cref="EuCellarObjectSnapshotRefusal.None"/>.
    /// </param>
    /// <returns>
    /// One <see cref="EuCellarObjectSnapshot"/> per object in <c>O</c>, the root first then every
    /// discovered state in ascending ordinal order, or <c>null</c> when refused.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// A caller contract violation rather than a reviewable data disagreement.
    /// </exception>
    public static IReadOnlyList<EuCellarObjectSnapshot>? TryDecode(
        string requestedCelex,
        IReadOnlyList<RepeatedEnumerationRow> familyRows,
        RepeatedEnumerationInterpretationProfile familyProfile,
        IReadOnlyList<RepeatedEnumerationRow> objectFactRows,
        RepeatedEnumerationInterpretationProfile objectFactProfile,
        IReadOnlyList<RepeatedEnumerationRow> expressionFactRows,
        RepeatedEnumerationInterpretationProfile expressionFactProfile,
        EuActForm recordForm,
        SourceArtifactRef evidenceRef,
        out EuCellarObjectDecodeRefusal refusal,
        out string? offendingIri,
        out EuCellarObjectSnapshotRefusal snapshotRefusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedCelex);
        ArgumentNullException.ThrowIfNull(familyRows);
        ArgumentNullException.ThrowIfNull(familyProfile);
        ArgumentNullException.ThrowIfNull(objectFactRows);
        ArgumentNullException.ThrowIfNull(objectFactProfile);
        ArgumentNullException.ThrowIfNull(expressionFactRows);
        ArgumentNullException.ThrowIfNull(expressionFactProfile);
        ContractValidation.RequireDefined(recordForm, nameof(recordForm));
        ArgumentNullException.ThrowIfNull(evidenceRef);
        offendingIri = null;

        if (Array.Exists(familyRows.ToArray(), static row => row is null))
        {
            throw new ArgumentException("A family row cannot be null.", nameof(familyRows));
        }

        if (Array.Exists(objectFactRows.ToArray(), static row => row is null))
        {
            throw new ArgumentException("An object-fact row cannot be null.", nameof(objectFactRows));
        }

        if (Array.Exists(expressionFactRows.ToArray(), static row => row is null))
        {
            throw new ArgumentException("An expression-fact row cannot be null.", nameof(expressionFactRows));
        }

        var seedEntry = EuAppendixASeedMap.SeedsInCelexOrder.FirstOrDefault(
            seed => string.Equals(seed.Celex, requestedCelex, StringComparison.Ordinal));
        if (seedEntry.Celex is null)
        {
            throw new ArgumentException(
                "requestedCelex must be one of Appendix A's 82 admitted seeds.",
                nameof(requestedCelex));
        }

        // ---- Discover O from the family census. Unchanged from D1-05b. ----
        string? rootIri = null;
        var discoveredStates = new List<string>();
        var seenStates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in familyRows)
        {
            var celexTerm = Term(row, familyProfile, "base_celex");
            var baseTerm = Term(row, familyProfile, "base");
            var stateTerm = Term(row, familyProfile, "state");

            if (celexTerm.Kind != RepeatedEnumerationRdfTermKind.Literal || celexTerm.Value is null ||
                baseTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || baseTerm.Value is null ||
                stateTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || stateTerm.Value is null)
            {
                refusal = EuCellarObjectDecodeRefusal.FamilyRowTermKindMismatch;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            if (!string.Equals(celexTerm.Value, requestedCelex, StringComparison.Ordinal))
            {
                refusal = EuCellarObjectDecodeRefusal.DuplicateSingleValuedBinding;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            var canonicalBase = EuPackRootCanonicalForm.TryCanonicalize(baseTerm.Value, out _);
            if (canonicalBase is null)
            {
                refusal = EuCellarObjectDecodeRefusal.FamilyRowTermKindMismatch;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            if (rootIri is null)
            {
                rootIri = canonicalBase;
            }
            else if (!string.Equals(rootIri, canonicalBase, StringComparison.Ordinal))
            {
                refusal = EuCellarObjectDecodeRefusal.DuplicateSingleValuedBinding;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            var canonicalState = EuPackRootCanonicalForm.TryCanonicalize(stateTerm.Value, out _);
            if (canonicalState is null)
            {
                refusal = EuCellarObjectDecodeRefusal.FamilyRowTermKindMismatch;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            if (seenStates.Add(canonicalState))
            {
                discoveredStates.Add(canonicalState);
            }
        }

        rootIri ??= EuPackRootCanonicalForm.TryCanonicalize(seedEntry.WorkRoot, out _)
            ?? throw new InvalidOperationException(
                "Appendix A's own seed map root failed to canonicalize; this is a defect in that " +
                "map, never a caller input.");

        discoveredStates.Sort(StringComparer.Ordinal);
        var closure = new HashSet<string>(StringComparer.Ordinal) { rootIri };
        closure.UnionWith(discoveredStates);

        // ---- Parse family P (object facts), checked against the closure. ----
        var objectFacts = new List<ObjectFactRow>();
        foreach (var row in objectFactRows)
        {
            var objectTerm = Term(row, objectFactProfile, "object");
            var predicateTerm = Term(row, objectFactProfile, "predicate");
            var valueTerm = Term(row, objectFactProfile, "value");
            var valueKindTerm = Term(row, objectFactProfile, "value_kind");
            var datatypeTerm = Term(row, objectFactProfile, "datatype_iri");
            var languageTerm = Term(row, objectFactProfile, "language_tag");

            if (objectTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || objectTerm.Value is null ||
                predicateTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || predicateTerm.Value is null ||
                !IsPlainLiteral(valueKindTerm) || !IsPlainLiteral(datatypeTerm) || !IsPlainLiteral(languageTerm) ||
                (valueTerm.Kind == RepeatedEnumerationRdfTermKind.Unbound) != (valueKindTerm.Value == "unbound"))
            {
                refusal = EuCellarObjectDecodeRefusal.ObjectFactRowTermKindMismatch;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            var canonicalObject = EuPackRootCanonicalForm.TryCanonicalize(objectTerm.Value, out _);
            if (canonicalObject is null)
            {
                refusal = EuCellarObjectDecodeRefusal.ObjectFactRowTermKindMismatch;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            if (!closure.Contains(canonicalObject))
            {
                refusal = EuCellarObjectDecodeRefusal.ObjectFactRowNotInClosure;
                offendingIri = canonicalObject;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            objectFacts.Add(new ObjectFactRow(
                canonicalObject, predicateTerm.Value, valueTerm, valueKindTerm.Value!));
        }

        // ---- Parse family X (expression facts), checked against the closure and its own closure. ----
        var expressionFacts = new List<ExpressionFactRow>();
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
                !IsPlainLiteral(valueKindTerm) || !IsPlainLiteral(datatypeTerm) || !IsPlainLiteral(languageTerm) ||
                (valueTerm.Kind == RepeatedEnumerationRdfTermKind.Unbound) != (valueKindTerm.Value == "unbound"))
            {
                refusal = EuCellarObjectDecodeRefusal.ExpressionFactRowTermKindMismatch;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            var canonicalParent = EuPackRootCanonicalForm.TryCanonicalize(parentTerm.Value, out _);
            if (canonicalParent is null)
            {
                refusal = EuCellarObjectDecodeRefusal.ExpressionFactRowTermKindMismatch;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            if (!closure.Contains(canonicalParent))
            {
                refusal = EuCellarObjectDecodeRefusal.ExpressionParentNotInClosure;
                offendingIri = canonicalParent;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }

            expressionFacts.Add(new ExpressionFactRow(
                canonicalParent, objectTerm.Value, predicateTerm.Value, valueTerm, valueKindTerm.Value!));
        }

        var belongsToWorkIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork);
        var selfClosedExpressions = new HashSet<string>(
            expressionFacts
                .Where(fact => fact.PredicateIri == belongsToWorkIri &&
                    fact.Value.Kind != RepeatedEnumerationRdfTermKind.Unbound)
                .Select(static fact => fact.Object),
            StringComparer.Ordinal);
        foreach (var fact in expressionFacts)
        {
            if (!selfClosedExpressions.Contains(fact.Object))
            {
                refusal = EuCellarObjectDecodeRefusal.ExpressionSubjectNotSelfClosed;
                offendingIri = fact.Object;
                snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
                return null;
            }
        }

        // ---- Build one snapshot per object in O: the root, then every discovered state. ----
        var snapshots = new List<EuCellarObjectSnapshot>(1 + discoveredStates.Count);
        var objectsInOrder = new List<(string Iri, bool IsRoot)> { (rootIri, true) };
        objectsInOrder.AddRange(discoveredStates.Select(static state => (state, false)));

        foreach (var (objectIri, isRoot) in objectsInOrder)
        {
            var pRows = objectFacts.Where(fact => fact.Object == objectIri).ToArray();
            var xRows = expressionFacts.Where(fact => fact.Parent == objectIri).ToArray();

            var snapshot = BuildOneObject(
                objectIri, isRoot, rootIri, pRows, xRows, recordForm, evidenceRef,
                out refusal, out offendingIri, out snapshotRefusal);
            if (snapshot is null)
            {
                return null;
            }

            snapshots.Add(snapshot);
        }

        refusal = EuCellarObjectDecodeRefusal.None;
        snapshotRefusal = EuCellarObjectSnapshotRefusal.None;
        return Array.AsReadOnly(snapshots.ToArray());
    }

    private static EuCellarObjectSnapshot? BuildOneObject(
        string objectIri,
        bool isRoot,
        string rootIri,
        IReadOnlyList<ObjectFactRow> pRows,
        IReadOnlyList<ExpressionFactRow> xRows,
        EuActForm recordForm,
        SourceArtifactRef evidenceRef,
        out EuCellarObjectDecodeRefusal refusal,
        out string? offendingIri,
        out EuCellarObjectSnapshotRefusal snapshotRefusal)
    {
        offendingIri = null;
        snapshotRefusal = EuCellarObjectSnapshotRefusal.None;

        var predicateObservations = new List<EuPredicateObservation>(EuScopeVocabulary.CdmPredicates.Count);
        foreach (var predicate in EuScopeVocabulary.CdmPredicates)
        {
            if (EuObjectFactsDiscoveryPlan.ExpressionAuthorityPredicates.Contains(predicate))
            {
                predicateObservations.Add(new EuPredicateObservation(
                    predicate, EuPredicateObservationState.NotObserved, [], evidenceRef));
                continue;
            }

            var iri = EuObjectFactsDiscoveryPlan.CdmIri(predicate);
            var matches = pRows.Where(row => row.PredicateIri == iri).ToArray();
            if (!TryBuildPredicateObservation(predicate, matches, evidenceRef, out var observation))
            {
                refusal = EuCellarObjectDecodeRefusal.ObjectFactRowTermKindMismatch;
                return null;
            }

            predicateObservations.Add(observation);
        }

        var relationObservations = new List<EuRelationFamilyObservation>(
            EuScopeVocabulary.ReadRelationFamilies.Count);
        foreach (var family in EuScopeVocabulary.ReadRelationFamilies)
        {
            var iri = EuObjectFactsDiscoveryPlan.RelationIri(family);
            var matches = pRows.Where(row => row.PredicateIri == iri).ToArray();
            if (!TryBuildRelationFamilyObservation(family, matches, evidenceRef, out var observation))
            {
                refusal = EuCellarObjectDecodeRefusal.ObjectFactRowTermKindMismatch;
                return null;
            }

            if (family == EuRelationFamily.ConsolidatedBasedOn)
            {
                var agrees = isRoot
                    ? observation.Edges.Count == 0
                    : observation.Edges.Count == 1 &&
                        string.Equals(observation.Edges[0].TargetWorkRoot, rootIri, StringComparison.Ordinal);
                if (!agrees)
                {
                    refusal = EuCellarObjectDecodeRefusal.ConsolidatedBasedOnEdgeDisagreesWithFamily;
                    return null;
                }
            }

            relationObservations.Add(observation);
        }

        var hasConsolidatedMarker = pRows.Any(row =>
            row.PredicateIri == EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkHasResourceType) &&
            row.ValueKind == "iri" &&
            row.Value.Value == ConsolidatedActResourceTypeIri);
        var derivedContentClass = hasConsolidatedMarker
            ? EuContentClass.Consolidation
            : EuContentClass.OriginalLegalText;
        var expectedContentClass = isRoot ? EuContentClass.OriginalLegalText : EuContentClass.Consolidation;
        if (derivedContentClass != expectedContentClass)
        {
            refusal = EuCellarObjectDecodeRefusal.ContentClassClosurePositionMismatch;
            return null;
        }

        var rights = new EuContentClassObservation(derivedContentClass, evidenceRef);
        var language = BuildLanguageObservation(xRows, evidenceRef);
        var channel = BuildChannel(evidenceRef);
        var objectRef = isRoot
            ? BuildRootObjectRef(objectIri, evidenceRef)
            : BuildStateObjectRef(objectIri, evidenceRef);

        var snapshot = EuCellarObjectSnapshot.TryObserve(
            objectRef,
            rootIri,
            recordForm,
            evidenceRef,
            predicateObservations,
            channel,
            language,
            null,
            rights,
            relationObservations,
            evidenceRef,
            null,
            evidenceRef,
            out snapshotRefusal);

        if (snapshot is null)
        {
            refusal = EuCellarObjectDecodeRefusal.ObjectSnapshotRejected;
            return null;
        }

        refusal = EuCellarObjectDecodeRefusal.None;
        return snapshot;
    }

    private static bool TryBuildPredicateObservation(
        EuCdmPredicate predicate,
        IReadOnlyList<ObjectFactRow> matches,
        SourceArtifactRef evidenceRef,
        out EuPredicateObservation observation)
    {
        observation = null!;
        if (matches.Count == 0)
        {
            // Family P asks every one of the nine object-authority predicates of every object it
            // describes; a complete delivery always carries at least one outcome row (a real value
            // or the explicit unbound marker) per predicate. Zero rows here is a malformed delivery,
            // not a fact.
            return false;
        }

        var unbound = matches.Where(static match => match.Value.Kind == RepeatedEnumerationRdfTermKind.Unbound)
            .ToArray();
        if (unbound.Length != 0)
        {
            if (matches.Count != 1)
            {
                return false;
            }

            observation = new EuPredicateObservation(
                predicate, EuPredicateObservationState.ObservedAbsent, [], evidenceRef);
            return true;
        }

        var values = matches.Select(static match => match.Value.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (values.Length == 0)
        {
            return false;
        }

        observation = new EuPredicateObservation(
            predicate, EuPredicateObservationState.ObservedPresent, values, evidenceRef);
        return true;
    }

    private static bool TryBuildRelationFamilyObservation(
        EuRelationFamily family,
        IReadOnlyList<ObjectFactRow> matches,
        SourceArtifactRef evidenceRef,
        out EuRelationFamilyObservation observation)
    {
        observation = null!;
        if (matches.Count == 0)
        {
            return false;
        }

        var unbound = matches.Where(static match => match.Value.Kind == RepeatedEnumerationRdfTermKind.Unbound)
            .ToArray();
        if (unbound.Length != 0)
        {
            if (matches.Count != 1)
            {
                return false;
            }

            observation = new EuRelationFamilyObservation(
                family, EuRelationAcquisitionState.Complete, [], evidenceRef);
            return true;
        }

        var targets = matches
            .Where(static match => match.ValueKind == "iri" && match.Value.Value is not null)
            .Select(static match => match.Value.Value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (targets.Length != matches.Count)
        {
            return false;
        }

        var edges = new List<EuRelationEdgeObservation>(targets.Length);
        foreach (var target in targets)
        {
            var canonicalTarget = EuPackRootCanonicalForm.TryCanonicalize(target, out _);
            if (canonicalTarget is null)
            {
                return false;
            }

            edges.Add(new EuRelationEdgeObservation(
                family, EuRelationAuthority.PublisherAsserted, canonicalTarget, evidenceRef));
        }

        observation = new EuRelationFamilyObservation(
            family, EuRelationAcquisitionState.Complete, edges, evidenceRef);
        return true;
    }

    private static EuLanguageExpressionObservation BuildLanguageObservation(
        IReadOnlyList<ExpressionFactRow> xRows, SourceArtifactRef evidenceRef)
    {
        var usesLanguageIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage);
        bool HasLanguage(string authorityIri) => xRows.Any(row =>
            row.PredicateIri == usesLanguageIri && row.ValueKind == "iri" && row.Value.Value == authorityIri);

        if (HasLanguage(EnglishLanguageAuthorityIri))
        {
            return new EuLanguageExpressionObservation(
                EuOfficialLanguage.English,
                EuExpressionObservationState.ExpressionObservedBodyNotHeld,
                "eu_object_facts_decode.language",
                "eu_cellar_object_decode.language_english_observed_body_not_held",
                evidenceRef);
        }

        if (HasLanguage(FrenchLanguageAuthorityIri))
        {
            return new EuLanguageExpressionObservation(
                EuOfficialLanguage.French,
                EuExpressionObservationState.ExpressionObservedBodyNotHeld,
                "eu_object_facts_decode.language",
                "eu_cellar_object_decode.language_french_observed_body_not_held",
                evidenceRef);
        }

        return new EuLanguageExpressionObservation(
            EuOfficialLanguage.English,
            EuExpressionObservationState.NotObserved,
            "eu_object_facts_decode.language",
            "eu_cellar_object_decode.language_not_observed",
            evidenceRef);
    }

    private static EuChannelObservation BuildChannel(SourceArtifactRef evidenceRef) => new(
        EuChannel.CellarSparqlEndpoint,
        "eu_consolidation_discovery.channel",
        "eu_cellar_object_decode.channel_cellar_sparql_endpoint",
        evidenceRef);

    private static SourceObjectRef BuildRootObjectRef(string rootIri, SourceArtifactRef evidenceRef) =>
        BuildObjectRef("eu-consolidation-root:", "eu_consolidation_root", rootIri, evidenceRef);

    private static SourceObjectRef BuildStateObjectRef(string stateIri, SourceArtifactRef evidenceRef) =>
        BuildObjectRef("eu-consolidation-state:", "eu_consolidation_state", stateIri, evidenceRef);

    private static SourceObjectRef BuildObjectRef(
        string canonicalKeyPrefix, string entityKindMember, string iri, SourceArtifactRef evidenceRef)
    {
        var canonicalKey = canonicalKeyPrefix + iri;
        var canonicalKeySha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalKey)));
        var entityKind = new SourceRegistryMemberRef(evidenceRef, entityKindMember);
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            entityKind,
            iri,
            canonicalKey,
            canonicalKeySha256,
            evidenceRef,
            null);
    }

    private static bool IsPlainLiteral(RepeatedEnumerationRdfTerm term) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal && term.Datatype is null && term.Language is null;

    /// <summary>
    /// Looks up one projection variable's term by name, never by a literal index. Shared by all
    /// three row sets this door reads.
    /// </summary>
    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string variableName)
    {
        var index = IndexOf(profile.ProjectionVariables, variableName);
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

    private static int IndexOf(IReadOnlyList<string> projection, string variableName)
    {
        for (var i = 0; i < projection.Count; i++)
        {
            if (string.Equals(projection[i], variableName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed record ObjectFactRow(
        string Object, string PredicateIri, RepeatedEnumerationRdfTerm Value, string ValueKind);

    private sealed record ExpressionFactRow(
        string Parent, string Object, string PredicateIri, RepeatedEnumerationRdfTerm Value, string ValueKind);
}
