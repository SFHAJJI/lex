using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why a proof-bound located-amendment observation could not become its accepted E4 axiom.</summary>
public enum EuLocatedAmendmentAxiomProjectionRefusal
{
    None = 0,
    PublisherTargetAmbiguous = 1,
    SourceIdentityDoesNotMatchObservation = 2,
    TargetIdentityDoesNotMatchObservation = 3,
    RequiredQualifierMissing = 4,
    QualifierRepeated = 5,
    QualifierNotPlainLiteral = 6,
    QualifierValueNotAdmitted = 7,
}

/// <summary>
/// The accepted E4 axiom together with the exact proof-bound publisher observation from which its
/// typed qualifiers were read.
/// </summary>
/// <remarks>
/// Keeping the observation beside the accepted axiom preserves unknown publisher properties and
/// RDF term metadata that <see cref="QualifiedAxiom"/>'s string qualifiers cannot represent. The
/// target-body scope remains a separate corpus-dependent input and is supplied only after the
/// corpus record set has been durably reopened.
/// </remarks>
internal sealed class EuLocatedAmendmentAxiomProjection
{
    private const string AnnotatedSourcePredicateIri =
        "http://www.w3.org/2002/07/owl#annotatedSource";
    private const string AnnotatedTargetPredicateIri =
        "http://www.w3.org/2002/07/owl#annotatedTarget";

    private EuLocatedAmendmentAxiomProjection(
        EuLocatedAmendmentAxiom axiom,
        EuLocatedAmendmentAxiomObservation observation)
    {
        Axiom = axiom;
        Observation = observation;
    }

    public EuLocatedAmendmentAxiom Axiom { get; }
    public EuLocatedAmendmentAxiomObservation Observation { get; }
    public SourceArtifactRef InterpretationProfileRef => Observation.InterpretationProfileRefs[0];

    /// <summary>
    /// Reads the accepted typed qualifiers from one decoder-minted observation. The identity sets
    /// must name the exact publisher IRIs in that observation; the source-observation coordinate is
    /// derived from its proof-bound interpretation profile rather than supplied by the caller.
    /// </summary>
    internal static EuLocatedAmendmentAxiomProjection? TryCreate(
        EuLocatedAmendmentAxiomObservation observation,
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        TargetBodyScope targetBodyScope,
        out EuLocatedAmendmentAxiomProjectionRefusal refusal,
        out string? offendingPredicate)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        refusal = EuLocatedAmendmentAxiomProjectionRefusal.None;
        offendingPredicate = null;

        if (observation.AnnotatedTargetIris.Count != 1)
        {
            refusal = EuLocatedAmendmentAxiomProjectionRefusal.PublisherTargetAmbiguous;
            offendingPredicate = AnnotatedTargetPredicateIri;
            return null;
        }

        if (observation.AnnotatedSourceIris.Count != 1 ||
            observation.InterpretationProfileRefs.Count != 1 ||
            !IdentityNames(source, observation.AnnotatedSourceIris[0]))
        {
            refusal = EuLocatedAmendmentAxiomProjectionRefusal.SourceIdentityDoesNotMatchObservation;
            offendingPredicate = AnnotatedSourcePredicateIri;
            return null;
        }

        if (!IdentityNames(target, observation.AnnotatedTargetIris[0]))
        {
            refusal = EuLocatedAmendmentAxiomProjectionRefusal.TargetIdentityDoesNotMatchObservation;
            offendingPredicate = AnnotatedTargetPredicateIri;
            return null;
        }

        if (!RequiredLiteral(observation, EuAmendmentRelationVocabulary.ReferenceToModifiedLocationUri,
                out var location, out refusal))
            return Refused(EuAmendmentRelationVocabulary.ReferenceToModifiedLocationUri,
                out offendingPredicate);
        if (!RequiredLiteral(observation, EuAmendmentRelationVocabulary.Role2Uri,
                out var role, out refusal))
            return Refused(EuAmendmentRelationVocabulary.Role2Uri, out offendingPredicate);
        if (!OptionalLiteral(observation, EuAmendmentRelationVocabulary.StartOfValidityUri,
                out var start, out refusal))
            return Refused(EuAmendmentRelationVocabulary.StartOfValidityUri, out offendingPredicate);
        if (!OptionalLiteral(observation, EuAmendmentRelationVocabulary.EndOfValidityUri,
                out var end, out refusal))
            return Refused(EuAmendmentRelationVocabulary.EndOfValidityUri, out offendingPredicate);
        if (!RequiredLiteral(observation, EuAmendmentRelationVocabulary.TypeOfLinkTargetUri,
                out var linkTargetType, out refusal))
            return Refused(EuAmendmentRelationVocabulary.TypeOfLinkTargetUri, out offendingPredicate);

        if (!QualifiersAreAdmitted(location!, role!, start, end, linkTargetType!, out offendingPredicate))
        {
            refusal = EuLocatedAmendmentAxiomProjectionRefusal.QualifierValueNotAdmitted;
            return null;
        }

        try
        {
            var axiom = EuLocatedAmendmentAxiom.Create(
                source,
                target,
                targetBodyScope,
                location!,
                role!,
                start,
                end,
                linkTargetType!,
                observation.AxiomIri,
                observation.InterpretationProfileRefs[0].ResourceId);
            return new EuLocatedAmendmentAxiomProjection(axiom, observation);
        }
        catch (ArgumentException)
        {
            refusal = EuLocatedAmendmentAxiomProjectionRefusal.QualifierValueNotAdmitted;
            offendingPredicate = null;
            return null;
        }
    }

    private static bool IdentityNames(OfficialIdentitySet identity, string iri) =>
        identity.Publisher == PublisherId.EuEurLex &&
        string.Equals(identity.Value(FactsIdentifierFamily.CellarWorkUri), iri, StringComparison.Ordinal);

    private static bool RequiredLiteral(
        EuLocatedAmendmentAxiomObservation observation,
        string predicate,
        out string? value,
        out EuLocatedAmendmentAxiomProjectionRefusal refusal) =>
        Literal(observation, predicate, required: true, out value, out refusal);

    private static bool OptionalLiteral(
        EuLocatedAmendmentAxiomObservation observation,
        string predicate,
        out string? value,
        out EuLocatedAmendmentAxiomProjectionRefusal refusal) =>
        Literal(observation, predicate, required: false, out value, out refusal);

    private static bool Literal(
        EuLocatedAmendmentAxiomObservation observation,
        string predicate,
        bool required,
        out string? value,
        out EuLocatedAmendmentAxiomProjectionRefusal refusal)
    {
        var matches = observation.RawProperties
            .Where(property => string.Equals(property.PredicateIri, predicate, StringComparison.Ordinal))
            .ToArray();
        value = null;
        if (matches.Length == 0)
        {
            refusal = required
                ? EuLocatedAmendmentAxiomProjectionRefusal.RequiredQualifierMissing
                : EuLocatedAmendmentAxiomProjectionRefusal.None;
            return !required;
        }
        if (matches.Length != 1)
        {
            refusal = EuLocatedAmendmentAxiomProjectionRefusal.QualifierRepeated;
            return false;
        }
        var term = matches[0].Value;
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal ||
            term.Datatype is not null || term.Language is not null || term.Value is null)
        {
            refusal = EuLocatedAmendmentAxiomProjectionRefusal.QualifierNotPlainLiteral;
            return false;
        }
        value = term.Value;
        refusal = EuLocatedAmendmentAxiomProjectionRefusal.None;
        return true;
    }

    private static EuLocatedAmendmentAxiomProjection? Refused(
        string predicate,
        out string? offendingPredicate)
    {
        offendingPredicate = predicate;
        return null;
    }

    private static bool QualifiersAreAdmitted(
        string location,
        string role,
        string? start,
        string? end,
        string linkTargetType,
        out string? offendingPredicate)
    {
        var checks = new (string Predicate, Action Validate)[]
        {
            (EuAmendmentRelationVocabulary.ReferenceToModifiedLocationUri,
                () => EuStructuralLocation.Parse(
                    location, EuAmendmentRelationVocabulary.LocationAuthorityListUri)),
            (EuAmendmentRelationVocabulary.Role2Uri, () =>
            {
                var parsed = EuStructuralLocation.Parse(
                    role, EuAmendmentRelationVocabulary.RoleAuthorityListUri);
                if (parsed.Tokens.Count != 1 || parsed.Tokens[0].Value is not null)
                    throw new ArgumentException("role2 must carry one bare authority-qualified code.");
            }),
            (EuAmendmentRelationVocabulary.StartOfValidityUri,
                () => { if (start is not null) _ = EuValidityDate.Create(start); }),
            (EuAmendmentRelationVocabulary.EndOfValidityUri,
                () => { if (end is not null) _ = EuValidityDate.Create(end); }),
            (EuAmendmentRelationVocabulary.TypeOfLinkTargetUri,
                () => EuAmendmentRelationVocabulary.RequireLinkTargetType(linkTargetType, nameof(linkTargetType))),
        };
        foreach (var check in checks)
        {
            try
            {
                check.Validate();
            }
            catch (ArgumentException)
            {
                offendingPredicate = check.Predicate;
                return false;
            }
        }
        offendingPredicate = null;
        return true;
    }
}
