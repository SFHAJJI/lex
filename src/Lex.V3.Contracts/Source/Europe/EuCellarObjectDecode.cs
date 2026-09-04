using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why <see cref="EuCellarObjectDecode.TryDecode"/> refused to hand back a snapshot. Closed.
/// </summary>
/// <remarks>
/// Three members beyond <see cref="None"/>, matching the SCOPE_RULING's three named refusal
/// conditions exactly: a root outside the pack is <see cref="ObjectSnapshotRejected"/> (delegated to
/// <see cref="EuCellarObjectSnapshot.TryObserve"/>'s own pack-membership check rather than a second
/// one this door invents), a term kind that disagrees with the family projection's own expectation is
/// <see cref="FamilyRowTermKindMismatch"/>, and a duplicate binding for a predicate this decode treats
/// as single-valued for one root is <see cref="DuplicateSingleValuedBinding"/>. No member here drops a
/// row silently: every family row this door reads is either folded into the one snapshot it builds or
/// is the exact row that produced one of these three refusals.
/// </remarks>
public enum EuCellarObjectDecodeRefusal
{
    /// <summary>No refusal: the snapshot was admitted.</summary>
    None = 0,

    /// <summary>
    /// A family row's <c>base_celex</c>, <c>base</c> or <c>state</c> term disagreed with the shape
    /// the family projection promises: <c>base_celex</c> must be a plain literal, <c>base</c> and
    /// <c>state</c> must be IRIs, and <c>state</c> must reduce to Appendix A's exact lexical form
    /// (<see cref="EuPackRootCanonicalForm"/>) even though it is never checked against pack
    /// membership (a relation target is never restricted to the pack; see
    /// <see cref="EuRelationEdgeObservation"/>'s own remarks). A row failing any of these is refused
    /// here rather than let an ill-shaped term reach a downstream constructor as an uncaught
    /// exception.
    /// </summary>
    FamilyRowTermKindMismatch = 1,

    /// <summary>
    /// This decode treats a root's own CELEX identity and its own canonical Work-root IRI as
    /// single-valued facts, fixed by <paramref name="requestedCelex"/> and by Appendix A's own
    /// admitted-seed map rather than by whichever row happens to be read first. A family row whose
    /// <c>base_celex</c> or <c>base</c> disagrees with that fixed value is a second, conflicting
    /// value bound to a fact this decode cannot aggregate (unlike the relation edges a family's many
    /// rows legitimately carry many of), so it is refused rather than silently overwritten or
    /// silently ignored.
    /// </summary>
    DuplicateSingleValuedBinding = 2,

    /// <summary>
    /// <see cref="EuCellarObjectSnapshot.TryObserve"/> itself refused the assembled observation set,
    /// most notably (Point 9 of the ruling) when the resolved root is not a member of
    /// <see cref="EuAppendixASeedMap.PackRoots"/>. The exact inner reason is reported alongside this
    /// member rather than being collapsed into one opaque code, so a caller can distinguish "outside
    /// the pack" from any other reason the snapshot's own door refuses.
    /// </summary>
    ObjectSnapshotRejected = 3,
}

/// <summary>
/// The decode from a verified EU consolidation-family row set
/// (<see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>'s return, read under
/// <c>EuConsolidationDiscoveryPlan</c>'s own family projection) into the typed
/// <see cref="EuCellarObjectSnapshot"/> observation set for exactly the one Cellar root the rows were
/// enumerated for.
/// </summary>
/// <remarks>
/// <para>
/// Queue item D1-05b (SCOPE_RULING <c>lex-event-20260904T015609998Z-bb7cc08f556347f5a5455a58f810b9ee</c>):
/// "a decode from <c>VerifiedRepeatedEnumerationRows.TryOpen</c>'s rows into the typed EU observations
/// <c>EuCellarObjectSnapshot.TryObserve</c> takes, then one snapshot per cellar root." This type is
/// that decode. It is contracts-only: nothing here calls a store, a publisher endpoint, or
/// <c>Lex.V3.Ingest</c>. D1-05c (the EU executor and adapter, queued separately) is the caller this
/// door is built for.
/// </para>
/// <para>
/// Precision one, the column set. The plan's family query set (<c>EuConsolidationQuerySet.Family</c>
/// in <c>EuConsolidationDiscoveryPlan</c>) projects exactly <c>base_celex</c>, <c>base</c>,
/// <c>state</c>, <c>family_multiplicity</c> and <c>state_key</c>, in that order, from
/// <c>?state &lt;act_consolidated_based_on_resource_legal&gt; ?base</c> bound against one requested
/// seed CELEX. Every variable this door reads is looked up by name against
/// <paramref name="familyProfile"/>'s own <c>ProjectionVariables</c>
/// (<see cref="RepeatedEnumerationRow"/> itself carries no per-term name; a term is keyed only by its
/// position in <c>ProjectionVariables</c>, so <see cref="Term"/> below is the one place a literal
/// index ever occurs, and every call site names its variable rather than its position) - never a
/// literal index into <see cref="RepeatedEnumerationRow.Terms"/>. <c>family_multiplicity</c> and
/// <c>state_key</c> are not read: multiplicity is a count this decode does not need (it observes
/// which states exist, not how many triples asserted each), and <c>state_key</c> is the cursor column
/// the delivery-verification layer already re-derives and checks before these rows ever reach this
/// door.
/// </para>
/// <para>
/// The plan's other query set, <c>TemporalFacts</c>, projects <c>act_consolidated_date</c> (one of the
/// thirteen closed <see cref="EuCdmPredicate"/> members) and three predicates
/// (<c>act_consolidated_layer</c>, <c>_version</c>, <c>_number</c>) that are not CDM predicates this
/// pipeline reads at all - but every one of those facts is asserted on <c>?state</c>, never on
/// <c>?base</c>. This door builds one snapshot per <em>root</em> (<c>base</c>, the Appendix A pack
/// member), not per state, so none of the temporal-facts projection describes the object this door
/// observes; <see cref="EuCdmPredicate.ActConsolidatedDate"/> is therefore honestly
/// <see cref="EuPredicateObservationState.NotObserved"/> below rather than fed from a fact about a
/// different Cellar object. This door's own signature reflects that: it takes only the family row set.
/// A future decode of each discovered state's own facts into its own state-scoped snapshot is separate
/// work this scope ruling does not ask for.
/// </para>
/// <para>
/// Precision two, grouping and absence. One call to <see cref="TryDecode"/> is scoped to exactly one
/// root, matching how the plan itself binds exactly one <c>requested_celex</c> per count-and-page
/// cycle (<c>VALUES ?base_celex { ... }</c> takes one value, never a set). "Grouping is per root"
/// therefore means: every row this call reads is folded into the one snapshot for
/// <paramref name="requestedCelex"/>'s own root. Within that group, <see cref="EuCdmPredicate.ResourceLegalIdCelex"/>
/// is <see cref="EuPredicateObservationState.ObservedPresent"/> (this closure's whole premise is a
/// bound, admitted CELEX); every other of the thirteen closed predicates is
/// <see cref="EuPredicateObservationState.NotObserved"/>, honestly, because this closure never asks
/// them about the root. <see cref="EuRelationFamily.ConsolidatedBasedOn"/> aggregates every distinct
/// <c>state</c> discovered across the group's rows into one collection-shaped observation (Decision
/// 64: many edges, one family observation, never one observation per row); the family's own bounded,
/// re-verified enumeration (already proven complete by the delivery-comparison machinery this door's
/// rows were reopened and re-checked against before ever reaching here) makes
/// <see cref="EuRelationAcquisitionState.Complete"/> the honest acquisition state, including the zero
/// edges case (a root with no consolidated states yet is a real, complete, negative observation, not a
/// coverage gap). The other three relation families this pipeline reads
/// (<see cref="EuRelationFamily.Amends"/>, <see cref="EuRelationFamily.Corrects"/>,
/// <see cref="EuRelationFamily.BasedOn"/>) are <see cref="EuRelationAcquisitionState.Unacquired"/>:
/// this specific closure never asks about them either.
/// </para>
/// <para>
/// The edge direction is deliberately inverted from the publisher's own assertion. The publisher
/// asserts <c>state consolidated_based_on base</c>; the snapshot observes <c>base</c> (the root), so
/// recording the edge as <see cref="EuRelationAuthority.PublisherAsserted"/> would claim the publisher
/// asserted the opposite direction. <see cref="EuRelationAuthority.OntologyAuthorizedInverse"/> is the
/// honest label for "the root has this state as a consolidated derivative", the inverse the ontology
/// authorises rather than a claim about which way the publisher's own triple points.
/// </para>
/// <para>
/// Precision three, refusals. See <see cref="EuCellarObjectDecodeRefusal"/> for the three named
/// conditions. None of the three ever drops a row: a row that cannot be read by name and by expected
/// shape refuses the whole call rather than being skipped so the remaining rows can still produce a
/// technically-successful, silently-incomplete snapshot.
/// </para>
/// <para>
/// What this door does not attempt to decode from the rows, and takes as caller-supplied context
/// instead, because no column of this specific closure's projection carries it: the act form
/// (<paramref name="recordForm"/> - <c>resource_legal_type</c> is a real <see cref="EuCdmPredicate"/>
/// member, but this closure's own SPARQL never selects it) and every evidence reference
/// (<paramref name="evidenceRef"/>, reused for the record, the channel, the relation axis and the
/// unconditionally-required supporting-document evidence alike, since
/// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> hands back verified rows with no finer
/// per-row evidence identity than the interpretation profile they were verified under). Channel,
/// language, format and rights are the other four axes <c>EuCellarObjectSnapshot.TryObserve</c>
/// accepts: channel is always <see cref="EuChannel.CellarSparqlEndpoint"/> here (every row this door
/// ever reads arrived over that one SPARQL endpoint, by construction of the plan itself); language,
/// format, rights and the supporting-document content class are left <c>null</c>, honestly, because
/// this closure evaluates none of those axes for any object it discovers.
/// </para>
/// </remarks>
public static class EuCellarObjectDecode
{
    /// <summary>The only path that decodes a family row set into a snapshot.</summary>
    /// <param name="requestedCelex">
    /// The exact admitted seed CELEX the family rows were enumerated for. Every row's own
    /// <c>base_celex</c> is checked against this fixed value (a caller-independent second value is a
    /// conflicting duplicate, never silently trusted); <see cref="EuAppendixASeedMap.SeedsInCelexOrder"/>
    /// supplies the root's identity only when <paramref name="familyRows"/> is empty, since a root
    /// with rows fixes its own root from the first row's own <c>base</c> term instead (so a corrupted
    /// or hostile delivery naming a root outside the pack is still reachable and refused downstream,
    /// rather than silently overridden by Appendix A's always-in-pack answer).
    /// </param>
    /// <param name="familyRows">
    /// The family query set's rows, already reopened and re-verified by
    /// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>. May be empty: a root with no discovered
    /// consolidated states is a real, complete, negative observation, not a caller error.
    /// </param>
    /// <param name="familyProfile">
    /// The interpretation profile the rows were verified under - the same instance the caller already
    /// holds for its own <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> call. Every variable
    /// this door reads is looked up by name against <see cref="RepeatedEnumerationInterpretationProfile.ProjectionVariables"/>.
    /// </param>
    /// <param name="recordForm">
    /// The root's own act form. Not recoverable from this closure's rows (see the type remarks); the
    /// caller supplies it from wherever it independently resolves <c>resource_legal_type</c>.
    /// </param>
    /// <param name="evidenceRef">
    /// The evidence this decode's observations rest on, reused for every predicate, channel, relation
    /// and axis field <see cref="EuCellarObjectSnapshot.TryObserve"/> requires evidence for. There is
    /// no finer per-row evidence identity available from verified rows alone.
    /// </param>
    /// <param name="refusal">Why no snapshot was returned, when none was.</param>
    /// <param name="snapshotRefusal">
    /// The inner reason, when <paramref name="refusal"/> is
    /// <see cref="EuCellarObjectDecodeRefusal.ObjectSnapshotRejected"/>; otherwise
    /// <see cref="EuCellarObjectSnapshotRefusal.None"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A caller contract violation rather than a reviewable data disagreement: a null argument, or a
    /// <paramref name="requestedCelex"/> that is not one of Appendix A's 82 admitted seeds.
    /// </exception>
    public static EuCellarObjectSnapshot? TryDecode(
        string requestedCelex,
        IReadOnlyList<RepeatedEnumerationRow> familyRows,
        RepeatedEnumerationInterpretationProfile familyProfile,
        EuActForm recordForm,
        SourceArtifactRef evidenceRef,
        out EuCellarObjectDecodeRefusal refusal,
        out EuCellarObjectSnapshotRefusal snapshotRefusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedCelex);
        ArgumentNullException.ThrowIfNull(familyRows);
        ArgumentNullException.ThrowIfNull(familyProfile);
        ContractValidation.RequireDefined(recordForm, nameof(recordForm));
        ArgumentNullException.ThrowIfNull(evidenceRef);

        if (Array.Exists(familyRows.ToArray(), static row => row is null))
        {
            throw new ArgumentException("A family row cannot be null.", nameof(familyRows));
        }

        // requestedCelex must itself be one of Appendix A's 82 admitted seeds: a caller contract
        // violation, not a reviewable data disagreement, because this door only ever serves an
        // already-admitted seed. Its WorkRoot is used only as the zero-rows fallback below, never to
        // override what the rows themselves say: overriding it would make a fabricated out-of-pack
        // `base` term unreachable, since every Appendix A seed's own root is a pack member by
        // construction.
        var seedEntry = EuAppendixASeedMap.SeedsInCelexOrder.FirstOrDefault(
            seed => string.Equals(seed.Celex, requestedCelex, StringComparison.Ordinal));
        if (seedEntry.Celex is null)
        {
            throw new ArgumentException(
                "requestedCelex must be one of Appendix A's 82 admitted seeds.",
                nameof(requestedCelex));
        }

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

            // The row's own base term - not Appendix A's - fixes this call's root, so a corrupted or
            // hostile delivery that names a base outside the 82-root pack is actually reachable here
            // and is refused downstream by EuCellarObjectSnapshot.TryObserve's own pack-membership
            // check, never silently accepted because the caller's requestedCelex happened to be
            // legitimate.
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

        // A root with no discovered consolidated states at all has no row to read a base term from;
        // Appendix A's own seed map is the only source left, and it is always a pack member by
        // construction, so this fallback path never itself reaches ObjectSnapshotRejected on pack
        // membership - there is no row here that could have named a different, wrong root.
        rootIri ??= EuPackRootCanonicalForm.TryCanonicalize(seedEntry.WorkRoot, out _)
            ?? throw new InvalidOperationException(
                "Appendix A's own seed map root failed to canonicalize; this is a defect in that " +
                "map, never a caller input.");

        discoveredStates.Sort(StringComparer.Ordinal);

        // Built with explicit loops calling named static methods, deliberately, rather than a LINQ
        // Select over a branching ternary: a lambda whose two branches capture different variables
        // compiles to a stable single method under Debug but is split into two separate
        // compiler-generated methods under Release's optimizer, which would make the
        // ConstructionSurface pins on the observation records below configuration-dependent. A named
        // method with an ordinary in-body ternary has no such split in either configuration.
        var predicateObservations = new List<EuPredicateObservation>(EuScopeVocabulary.CdmPredicates.Count);
        foreach (var predicate in EuScopeVocabulary.CdmPredicates)
        {
            predicateObservations.Add(BuildPredicateObservation(predicate, requestedCelex, evidenceRef));
        }

        var edges = new List<EuRelationEdgeObservation>(discoveredStates.Count);
        foreach (var state in discoveredStates)
        {
            edges.Add(BuildConsolidatedBasedOnEdge(state, evidenceRef));
        }

        var relationObservations = new List<EuRelationFamilyObservation>(
            EuScopeVocabulary.ReadRelationFamilies.Count);
        foreach (var family in EuScopeVocabulary.ReadRelationFamilies)
        {
            relationObservations.Add(BuildRelationFamilyObservation(family, edges, evidenceRef));
        }

        var channel = BuildChannel(evidenceRef);

        var objectRef = BuildObjectRef(rootIri, evidenceRef);

        var snapshot = EuCellarObjectSnapshot.TryObserve(
            objectRef,
            rootIri,
            recordForm,
            evidenceRef,
            predicateObservations,
            channel,
            null,
            null,
            null,
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

    /// <summary>
    /// Looks up one projection variable's term by name, never by a literal index into
    /// <see cref="RepeatedEnumerationRow.Terms"/>. This is the one place a positional index into
    /// <c>Terms</c> ever occurs in this file; every call site names its variable.
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

        // The door (queue item 17) already shapes every row it hands back to match the profile it
        // was verified under, so this is not reachable from a real delivery; it guards a hand-built
        // or corrupted row against an unexplained ArgumentOutOfRangeException, the same "caller
        // contract violation, not a reviewable data disagreement" treatment TryDecode already gives a
        // null row above.
        if (index >= row.Terms.Count)
        {
            throw new ArgumentException(
                $"A family row has {row.Terms.Count} term(s), too few to read '{variableName}' at " +
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

    /// <summary>
    /// One of the thirteen closed CDM predicates: the root's own CELEX is
    /// <see cref="EuPredicateObservationState.ObservedPresent"/> (this closure's whole premise is a
    /// bound, admitted CELEX); every other predicate is honestly
    /// <see cref="EuPredicateObservationState.NotObserved"/>, because this closure never asks any of
    /// them about the root.
    /// </summary>
    private static EuPredicateObservation BuildPredicateObservation(
        EuCdmPredicate predicate, string requestedCelex, SourceArtifactRef evidenceRef) =>
        predicate == EuCdmPredicate.ResourceLegalIdCelex
            ? new EuPredicateObservation(
                predicate, EuPredicateObservationState.ObservedPresent, [requestedCelex], evidenceRef)
            : new EuPredicateObservation(
                predicate, EuPredicateObservationState.NotObserved, [], evidenceRef);

    /// <summary>
    /// One discovered state, recorded as the ontology-authorized inverse of the publisher's own
    /// <c>state consolidated_based_on base</c> assertion (see the type remarks for why the direction
    /// is inverted).
    /// </summary>
    private static EuRelationEdgeObservation BuildConsolidatedBasedOnEdge(
        string state, SourceArtifactRef evidenceRef) =>
        new(
            EuRelationFamily.ConsolidatedBasedOn,
            EuRelationAuthority.OntologyAuthorizedInverse,
            state,
            evidenceRef);

    /// <summary>
    /// <see cref="EuRelationFamily.ConsolidatedBasedOn"/> aggregates every discovered state into one
    /// <see cref="EuRelationAcquisitionState.Complete"/> observation - complete because the rows this
    /// door reads were already reopened and re-verified as a bounded, proven enumeration before ever
    /// reaching here, including the zero-edges case (a root with no consolidated states yet is a
    /// real, complete, negative observation). The other three relation families this pipeline reads
    /// are <see cref="EuRelationAcquisitionState.Unacquired"/>: this specific closure never asks
    /// about them.
    /// </summary>
    private static EuRelationFamilyObservation BuildRelationFamilyObservation(
        EuRelationFamily family,
        IReadOnlyList<EuRelationEdgeObservation> consolidatedBasedOnEdges,
        SourceArtifactRef evidenceRef) =>
        family == EuRelationFamily.ConsolidatedBasedOn
            ? new EuRelationFamilyObservation(
                family, EuRelationAcquisitionState.Complete, consolidatedBasedOnEdges, evidenceRef)
            : new EuRelationFamilyObservation(
                family, EuRelationAcquisitionState.Unacquired, [], null);

    /// <summary>
    /// Every row this door ever reads arrived over the Cellar SPARQL endpoint, by construction of
    /// the plan itself, so the channel observation is a fixed fact rather than something read from a
    /// row. A named method rather than an inline expression: this is the one call site that makes
    /// <see cref="EuCellarObjectDecode"/> a recognised external producer of
    /// <see cref="EuChannelObservation"/>, alongside <see cref="EuCellarObjectSnapshot"/>'s own field
    /// and property.
    /// </summary>
    private static EuChannelObservation BuildChannel(SourceArtifactRef evidenceRef) => new(
        EuChannel.CellarSparqlEndpoint,
        "eu_consolidation_discovery.channel",
        "eu_cellar_object_decode.channel_cellar_sparql_endpoint",
        evidenceRef);

    private static SourceObjectRef BuildObjectRef(string rootIri, SourceArtifactRef evidenceRef)
    {
        const string CanonicalKeyPrefix = "eu-consolidation-root:";
        var canonicalKey = CanonicalKeyPrefix + rootIri;
        var canonicalKeySha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalKey)));
        var entityKind = new SourceRegistryMemberRef(evidenceRef, "eu_consolidation_root");
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            entityKind,
            rootIri,
            canonicalKey,
            canonicalKeySha256,
            evidenceRef,
            null);
    }
}
