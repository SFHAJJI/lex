using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public enum EuLocatedAmendmentExclusionKind
{
    SourceOutsideVerifiedCorpus = 1,
    PublisherWorkIdentityNotAdmitted = 2,
    ProjectionRefused = 3,
}

/// <summary>A publisher-marked axiom whose distinct targets prevent one accepted edge.</summary>
public sealed record EuLocatedAmendmentAmbiguity(EuLocatedAmendmentAxiomObservation Observation);

/// <summary>A publisher-marked axiom retained with the exact reason it was not admitted.</summary>
public sealed record EuLocatedAmendmentExclusion(
    EuLocatedAmendmentAxiomObservation Observation,
    EuLocatedAmendmentExclusionKind Kind,
    EuLocatedAmendmentAxiomProjectionRefusal? ProjectionRefusal,
    string? Detail);

/// <summary>Every delivered located axiom partitioned without claiming change-id coverage.</summary>
public sealed record EuLocatedAmendmentProduction(
    IReadOnlyList<EuLocatedAmendmentAxiomProjection> Admitted,
    IReadOnlyList<EuLocatedAmendmentAmbiguity> Ambiguous,
    IReadOnlyList<EuLocatedAmendmentExclusion> Excluded);

/// <summary>
/// Resolves located-amendment targets only against the corpus set reopened by the same adapter run.
/// Internal so a caller cannot pair publisher observations with an unrelated corpus set.
/// </summary>
internal static class EuLocatedAmendmentProducer
{
    internal static EuLocatedAmendmentProduction Produce(
        IReadOnlyList<EuLocatedAmendmentAxiomObservation> observations,
        VerifiedCorpusRecordSet corpus)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(corpus);

        var records = corpus.Set.Records.ToDictionary(
            static record => record.ObjectRef.PublisherUri,
            StringComparer.Ordinal);
        var admitted = new List<EuLocatedAmendmentAxiomProjection>();
        var ambiguous = new List<EuLocatedAmendmentAmbiguity>();
        var excluded = new List<EuLocatedAmendmentExclusion>();

        foreach (var observation in observations)
        {
            if (observation.IsPublisherTargetAmbiguous)
            {
                ambiguous.Add(new EuLocatedAmendmentAmbiguity(observation));
                continue;
            }

            if (!records.ContainsKey(observation.AnnotatedSourceIri))
            {
                excluded.Add(new EuLocatedAmendmentExclusion(
                    observation,
                    EuLocatedAmendmentExclusionKind.SourceOutsideVerifiedCorpus,
                    null,
                    observation.AnnotatedSourceIri));
                continue;
            }

            var targetIri = observation.AnnotatedTargetIris[0];
            OfficialIdentitySet source;
            OfficialIdentitySet target;
            try
            {
                source = Work(observation.AnnotatedSourceIri);
                target = Work(targetIri);
            }
            catch (ArgumentException exception)
            {
                excluded.Add(new EuLocatedAmendmentExclusion(
                    observation,
                    EuLocatedAmendmentExclusionKind.PublisherWorkIdentityNotAdmitted,
                    null,
                    exception.ParamName));
                continue;
            }

            var scope = records.TryGetValue(targetIri, out var targetRecord)
                ? targetRecord.Body.Kind == CorpusBodyRecordKind.Held
                    ? TargetBodyScope.BodyInScopeHeld
                    : TargetBodyScope.BodyInScopeNotHeld
                : TargetBodyScope.BodyOutsideScope;
            var projection = EuLocatedAmendmentAxiomProjection.TryCreate(
                observation, source, target, scope, out var refusal, out var offendingPredicate);
            if (projection is null)
            {
                excluded.Add(new EuLocatedAmendmentExclusion(
                    observation,
                    EuLocatedAmendmentExclusionKind.ProjectionRefused,
                    refusal,
                    offendingPredicate));
                continue;
            }

            admitted.Add(projection);
        }

        return new EuLocatedAmendmentProduction(
            admitted.AsReadOnly(), ambiguous.AsReadOnly(), excluded.AsReadOnly());
    }

    private static OfficialIdentitySet Work(string iri) =>
        new(PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, iri)]);
}
