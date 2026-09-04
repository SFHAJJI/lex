using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The pure function the ruling names as D1-05's fourth point: reduce one
/// <see cref="EuCellarObjectSnapshot"/> into <see cref="EuScopeObjectDispositions"/>'s exact
/// constructor shape, so it feeds <see cref="EuScopeProfile.BuildScopeInput"/> unchanged.
/// </summary>
/// <remarks>
/// <para>
/// No I/O, and no policy invented here that already exists as a named closed function elsewhere:
/// channel admission is read from <see cref="EuChannelDisposition.PolicyFor"/>, and rights basis
/// from <see cref="EuRightsDisposition.BasisFor"/>, exactly as those types already require of any
/// caller. Format admission has no such fixed function (see <see cref="EuFormatObservation"/>'s own
/// remarks) and is carried straight through from what the snapshot observed.
/// </para>
/// <para>
/// The one fact this reduction does encode on its own is R1's language-absence distinction, because
/// it is the fold the ruling calls out by name: <see cref="EuExpressionObservationState.NotObserved"/>
/// reduces to a null <see cref="EuScopeObjectDispositions.LanguageDisposition"/>, which is exactly
/// what makes <see cref="EuScopeProfile.BuildScopeInput"/> publish
/// <c>ScopeSelectorState.PublisherValueAbsent</c> for that selector instead of treating a language
/// nobody has ever seen an Expression for as though it were an observed exclusion.
/// </para>
/// </remarks>
public static class EuScopeSnapshotReduction
{
    /// <summary>Reduce one snapshot into its dispositions. Pure; throws only on a malformed snapshot.</summary>
    public static EuScopeObjectDispositions Reduce(EuCellarObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var channelDisposition = new EuChannelDisposition(
            snapshot.Channel.Channel,
            EuChannelDisposition.PolicyFor(snapshot.Channel.Channel),
            snapshot.Channel.ReasonCode,
            snapshot.Channel.RuleId,
            snapshot.Channel.EvidenceRef);

        var languageDisposition = ReduceLanguage(snapshot.Language);

        // The ordered candidates cross with everything else this observation carries. Dropping them
        // here would leave the manifest row's single address looking like the only one this object
        // has, which is exactly the false reading RULING
        // lex-event-20260904T174138711Z-cdf5cbd17806423cbe05a6234cc4f262 corrects.
        var formatDisposition = snapshot.Format is { } format
            ? new EuFormatDisposition(
                format.Format, format.Admission, format.ReasonCode, format.EvidenceRef,
                format.OrderedCandidates)
            : null;

        var rightsDisposition = snapshot.Rights is { } rights
            ? new EuRightsDisposition(
                rights.ContentClass, EuRightsDisposition.BasisFor(rights.ContentClass), rights.EvidenceRef)
            : null;

        var relationDispositions = snapshot.RelationObservations
            .Select(ReduceRelation)
            .ToArray();

        return new EuScopeObjectDispositions(
            snapshot.ObjectRef,
            snapshot.RecordForm,
            snapshot.RecordEvidenceRef,
            channelDisposition,
            languageDisposition,
            formatDisposition,
            rightsDisposition,
            relationDispositions,
            snapshot.RelationAxisEvidenceRef,
            snapshot.Supporting?.ContentClass,
            snapshot.SupportingEvidenceRef);
    }

    /// <summary>
    /// R1's language-absence fold: <see cref="EuExpressionObservationState.NotObserved"/> becomes a
    /// null disposition (so the selector publishes <c>publisher_value_absent</c>), and the two
    /// observed states become the matching <see cref="EuLanguageBodyState"/>.
    /// </summary>
    private static EuLanguageBodyDisposition? ReduceLanguage(EuLanguageExpressionObservation? language)
    {
        if (language is null || language.State == EuExpressionObservationState.NotObserved)
        {
            return null;
        }

        var bodyState = language.State == EuExpressionObservationState.ExpressionObservedBodyHeld
            ? EuLanguageBodyState.BodyCandidate
            : EuLanguageBodyState.BodyNotHeldPoint;

        return new EuLanguageBodyDisposition(
            language.Language, bodyState, language.ReasonCode, language.RuleId, language.EvidenceRef);
    }

    private static EuRelationFamilyDisposition ReduceRelation(EuRelationFamilyObservation observation)
    {
        var authority = observation.Edges.Count > 0
            ? observation.Edges[0].Authority
            : EuRelationAuthority.PublisherAsserted;

        EuRelationAuthority? conflicting = null;
        foreach (var edge in observation.Edges)
        {
            if (edge.Authority != authority)
            {
                conflicting = edge.Authority;
                break;
            }
        }

        if (conflicting is not null)
        {
            throw new InvalidOperationException(
                $"Relation family {observation.Family} carries edges under two different " +
                $"authorities ({authority} and {conflicting}); a family's disposition names one " +
                "authority, and a mixed set is a snapshot defect, not a reduction choice.");
        }

        if (authority == EuRelationAuthority.OntologyAuthorizedInverse)
        {
            // EuRelationFamilyDisposition requires an ontology authority reference for this
            // authority and this reduction has no ontology-registry binding of its own to supply,
            // exactly as it has no closure-row predicate table of its own (see
            // EuFeedEntryObservation's remarks on the same gap one level up). Declared as an open
            // input rather than invented: production wiring must thread one through before this
            // authority reaches a real edge.
            throw new NotSupportedException(
                "Reducing an ontology-authorized-inverse relation edge requires an ontology " +
                "authority reference this reduction is not given one for; thread it through the " +
                "snapshot before calling Reduce, rather than have this function invent one.");
        }

        // The one authority that would need an ontology-registry reference always throws above
        // before reaching here, so every edge that reaches this call has none to supply: the
        // argument below is not a local this function could ever compute something else for.
        return new EuRelationFamilyDisposition(
            observation.Family,
            authority,
            observation.Acquisition,
            observation.CompletionEvidenceRef,
            ontologyAuthorityRef: null);
    }
}
