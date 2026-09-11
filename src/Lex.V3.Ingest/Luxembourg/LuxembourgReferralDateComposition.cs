using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// Resolves every requested draft's referral date over two independently proven families.
/// </summary>
/// <remarks>
/// <para>
/// THE COMPOSITE NEITHER FAMILY CAN BE. The OpinionRequest family is request-scoped: it enumerates a
/// class and reads what its members hold, and never learns which draft reaches which request. The
/// draft-graph family holds the edges and never learns what their targets are. This is the only
/// place that cites both, and it is deliberately separate from either acquisition - making the
/// draft edge an input to the request producer would let a draft-side fact change what that producer
/// acquires.
/// </para>
/// <para>
/// EVERY INPUT TO <see cref="LuxembourgReferralDateStep.Resolve"/> IS DERIVED HERE, none is passed
/// through. That door takes six booleans and a value list, and a caller able to state any of them
/// could state a conclusion: #556 removed exactly that shape from the coverage door and the same
/// rule applies to its consumer. What a caller supplies is a delivered draft-graph production and a
/// request batch cover; everything else is read out of them.
/// </para>
/// <para>
/// THE EDGE IS RETAINED EVIDENCE, AND THAT IS ENOUGH. <c>hasOpinion</c> is admitted by no family:
/// the draft graph does not accept it and the opinion link-only family never projects the draft. But
/// the draft graph asks the publisher for EVERY predicate it holds, so the triple arrives as a
/// retained row - and the retained path verifies each row's <c>key_4</c> against the publisher's own
/// digest of its value before retaining it, so a retained row whose key names another value refuses
/// the whole production. A delivered result therefore carries edges whose keys were checked, bound
/// to the citation that proves their delivery, without this file re-asserting either.
/// </para>
/// </remarks>
public static class LuxembourgReferralDateComposition
{
    private const string HasOpinion = LuxembourgOpinionLinkOnlyVocabulary.HasOpinionPredicateIri;

    private const string ReferralDate =
        LuxembourgOpinionRequestGraphDiscoveryPlan.ReferralDatePredicateIri;

    private const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

    /// <summary>
    /// One step per draft-to-target edge, plus one per draft that reached nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DRAFT THAT REACHED NOTHING STILL GETS A STEP. Emitting only the edges would make a draft
    /// with no <c>hasOpinion</c> row indistinguishable from one nobody asked about, which is the
    /// difference <see cref="LuxembourgReferralDateState.DraftSideGap"/> exists to record.
    /// </para>
    /// <para>
    /// DRIFT IS CHECKED BEFORE THE EDGES. A <c>referralDate</c> asserted directly of a draft is
    /// retained by the draft graph as declared-on-another-class, and it is a fact about the
    /// publisher's graph rather than a broken edge - so it is reported as drift for that draft
    /// whatever its edges say.
    /// </para>
    /// </remarks>
    /// <param name="draftProduction">
    /// One delivered draft-graph batch. Refused productions are refused here: a production that did
    /// not complete carries no citation, so nothing it holds is bound to a proven delivery.
    /// </param>
    /// <param name="requestCover">
    /// The proven request inventory swept exactly once. The COVER and not a batch, because
    /// <see cref="LuxembourgReferralDateStep.Resolve"/>'s completeness question is about the class,
    /// and one batch cannot answer it.
    /// </param>
    public static IReadOnlyList<LuxembourgReferralDateStep> Over(
        LuxembourgDraftGraphProductionResult draftProduction,
        LuxembourgOpinionRequestBatchCover requestCover)
    {
        ArgumentNullException.ThrowIfNull(draftProduction);
        ArgumentNullException.ThrowIfNull(requestCover);

        if (draftProduction.Coverage is not { } draftCoverage)
        {
            throw new ArgumentException(
                "A refused draft-graph production proves no edge, so it composes nothing.",
                nameof(draftProduction));
        }

        // THE POPULATION COMES FROM THE COVER'S OWN BATCHES, which are the inventory's assignment.
        // Collecting it from anywhere else would let the class a target is measured against differ
        // from the class the cover proved.
        var byRequest = new Dictionary<string, LuxembourgOpinionRequestCoverage>(StringComparer.Ordinal);
        foreach (var batch in requestCover.Batches)
        {
            foreach (var request in batch.RequestedRequests)
            {
                byRequest[request] = batch;
            }
        }

        var steps = new List<LuxembourgReferralDateStep>();
        foreach (var draft in draftCoverage.RequestedDrafts)
        {
            // DRIFT FIRST. referralDate is declared on OpinionRequest, so a triple asserting it of
            // an InitialDraft says that subject holds a class role nothing proved. The draft graph
            // retains it rather than admitting it; reading it here as anything but drift would be
            // this family widening its own authority through the back door.
            if (draftProduction.RetainedNotAdmitted.Any(row =>
                    string.Equals(row.DraftIri, draft, StringComparison.Ordinal) &&
                    string.Equals(row.PredicateIri, ReferralDate, StringComparison.Ordinal)))
            {
                steps.Add(LuxembourgReferralDateStep.DirectTripleIsDrift(draft));
                continue;
            }

            var edges = draftProduction.RetainedNotAdmitted
                .Where(row =>
                    string.Equals(row.DraftIri, draft, StringComparison.Ordinal) &&
                    string.Equals(row.PredicateIri, HasOpinion, StringComparison.Ordinal))
                .ToArray();

            if (edges.Length is 0)
            {
                steps.Add(LuxembourgReferralDateStep.DraftReachedNothing(draft));
                continue;
            }

            foreach (var edge in edges)
            {
                steps.Add(StepFor(draft, edge, byRequest));
            }
        }

        return Array.AsReadOnly(steps.ToArray());
    }

    private static LuxembourgReferralDateStep StepFor(
        string draft,
        LuxembourgDraftRetainedEvidenceRow edge,
        Dictionary<string, LuxembourgOpinionRequestCoverage> byRequest)
    {
        // A TARGET THAT IS NOT A BARE IRI HAS NO NAME TO TRAVERSE TO, so the step is resolved with
        // the lexical form the publisher did deliver and Resolve refuses it as such. Naming the raw
        // value rather than dropping the edge keeps the refusal readable.
        var target = edge.Value ?? string.Empty;
        if (target.Length is 0)
        {
            return LuxembourgReferralDateStep.DraftReachedNothing(draft);
        }

        var coverage = byRequest.GetValueOrDefault(target);

        return LuxembourgReferralDateStep.Resolve(
            draft,
            target,

            // STRUCTURAL, NOT RE-ASSERTED. The draft graph verifies every retained row's key_4
            // against the publisher's own digest of its value BEFORE retaining it, and a row that
            // fails refuses the whole production. A delivered production therefore has no unverified
            // retained row in it, and a check here could not fail.
            edgeRowKeyVerified: true,

            // STRUCTURAL FOR THE SAME REASON. The row and the citation that proves its delivery come
            // out of one production result; there is no pairing for a caller to get wrong.
            edgeBoundToDraftCitation: true,

            targetIsBareIri: string.Equals(edge.ValueKind, IriKind, StringComparison.Ordinal),
            targetInProvenInventory: coverage is not null,

            // THE COVER EXISTING IS THE COMPLETENESS CLAIM. It is minted only when every batch the
            // inventory issued was delivered exactly once, so a caller holding one has the class.
            requestGraphCitationComplete: true,

            deliveredTypeRowPresent: coverage is not null && coverage.RoleConfirmedFor(target),
            referralDateValues: coverage is null
                ? []
                : coverage.ValuesFor(target, ReferralDate)
                    .Select(static value => value.Value ?? string.Empty)
                    .ToArray());
    }
}
