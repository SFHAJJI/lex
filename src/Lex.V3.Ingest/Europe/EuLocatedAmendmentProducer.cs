using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public enum EuLocatedAmendmentExclusionKind
{
    SourceOutsideVerifiedCorpus = 1,
    PublisherWorkIdentityNotAdmitted = 2,
    ProjectionRefused = 3,
}

/// <summary>
/// One publisher-marked attribution, retaining both the accepted E4 axiom and the exact raw
/// observation that authorized it. Only the corpus-bound producer can mint one.
/// </summary>
public sealed class EuPublisherMarkedAmendmentAttribution
{
    internal EuPublisherMarkedAmendmentAttribution(EuLocatedAmendmentAxiomProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        Axiom = projection.Axiom;
        Observation = projection.Observation;
    }

    public EuLocatedAmendmentAxiom Axiom { get; }
    public EuLocatedAmendmentAxiomObservation Observation { get; }
    public SourceArtifactRef InterpretationProfileRef => Observation.InterpretationProfileRefs[0];
}

/// <summary>A publisher-marked axiom naming more than one candidate amending instrument.</summary>
public sealed class EuLocatedAmendmentAmbiguity
{
    internal EuLocatedAmendmentAmbiguity(EuLocatedAmendmentAxiomObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!observation.IsPublisherSourceAmbiguous)
        {
            throw new ArgumentException(
                "Publisher ambiguity requires more than one retained annotated source.",
                nameof(observation));
        }

        Observation = observation;
    }

    public EuLocatedAmendmentAxiomObservation Observation { get; }
    public IReadOnlyList<string> CandidateInstrumentIris => Observation.AnnotatedSourceIris;
    public IReadOnlyList<SourceArtifactRef> InterpretationProfileRefs => Observation.InterpretationProfileRefs;
}

/// <summary>A publisher-marked axiom retained with the exact reason it was not admitted.</summary>
public sealed class EuLocatedAmendmentExclusion
{
    internal EuLocatedAmendmentExclusion(
        EuLocatedAmendmentAxiomObservation observation,
        EuLocatedAmendmentExclusionKind kind,
        EuLocatedAmendmentAxiomProjectionRefusal? projectionRefusal,
        string? detail)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Observation = observation;
        Kind = kind;
        ProjectionRefusal = projectionRefusal;
        Detail = detail;
    }

    public EuLocatedAmendmentAxiomObservation Observation { get; }
    public EuLocatedAmendmentExclusionKind Kind { get; }
    public EuLocatedAmendmentAxiomProjectionRefusal? ProjectionRefusal { get; }
    public string? Detail { get; }
}

/// <summary>
/// The only honest Stage 2 coverage statement while no complete textual change range exists.
/// It cannot express zero, complete or incomplete coverage, and therefore cannot certify a later
/// derived-attribution proposal.
/// </summary>
public sealed class EuAmendmentAttributionCoverage
{
    private EuAmendmentAttributionCoverage()
    {
    }

    internal static EuAmendmentAttributionCoverage Unmeasured { get; } = new();

    public string State => "unmeasured";
    public string UnmetPrerequisite => "complete_textual_change_range_measurement";
}

/// <summary>Every delivered located axiom partitioned without claiming change-id coverage.</summary>
public sealed class EuLocatedAmendmentProduction
{
    internal EuLocatedAmendmentProduction(
        IReadOnlyList<EuPublisherMarkedAmendmentAttribution> admitted,
        IReadOnlyList<EuLocatedAmendmentAmbiguity> ambiguous,
        IReadOnlyList<EuLocatedAmendmentExclusion> excluded)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(ambiguous);
        ArgumentNullException.ThrowIfNull(excluded);
        Admitted = admitted;
        Ambiguous = ambiguous;
        Excluded = excluded;
    }

    /// <summary>Publisher-marked axioms admitted through the accepted E4 construction boundary.</summary>
    public IReadOnlyList<EuPublisherMarkedAmendmentAttribution> Admitted { get; }

    /// <summary>Publisher-evidenced conflicts retaining every candidate source and raw property.</summary>
    public IReadOnlyList<EuLocatedAmendmentAmbiguity> Ambiguous { get; }

    /// <summary>Every remaining publisher observation, retained with its exact refusal.</summary>
    public IReadOnlyList<EuLocatedAmendmentExclusion> Excluded { get; }

    public EuAmendmentAttributionCoverage Coverage => EuAmendmentAttributionCoverage.Unmeasured;
}

/// <summary>
/// Resolves located-amendment targets only against the corpus set reopened by the same adapter run.
/// Internal so a caller cannot pair publisher observations with an unrelated corpus set.
/// </summary>
internal static class EuLocatedAmendmentProducer
{
    internal static EuLocatedAmendmentProduction Produce(
        IReadOnlyList<EuLocatedAmendmentAxiomObservation> observations,
        CorpusRecordSetWriteResult recordSetResult)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(recordSetResult);
        if (!TryGetCompleteCorpus(recordSetResult, out var corpus))
        {
            throw new ArgumentException(
                "Located amendment projection requires the same writer's complete reopened corpus set.",
                nameof(recordSetResult));
        }

        var records = corpus.Set.Records.ToDictionary(
            static record => record.ObjectRef.PublisherUri,
            StringComparer.Ordinal);
        var admitted = new List<EuPublisherMarkedAmendmentAttribution>();
        var ambiguous = new List<EuLocatedAmendmentAmbiguity>();
        var excluded = new List<EuLocatedAmendmentExclusion>();

        foreach (var observation in observations)
        {
            if (observation.IsPublisherSourceAmbiguous)
            {
                ambiguous.Add(new EuLocatedAmendmentAmbiguity(observation));
                continue;
            }

            var sourceIri = observation.AnnotatedSourceIris[0];
            if (!records.ContainsKey(sourceIri))
            {
                excluded.Add(new EuLocatedAmendmentExclusion(
                    observation,
                    EuLocatedAmendmentExclusionKind.SourceOutsideVerifiedCorpus,
                    null,
                    sourceIri));
                continue;
            }

            var targetIri = observation.AnnotatedTargetIris[0];
            OfficialIdentitySet source;
            OfficialIdentitySet target;
            try
            {
                source = Work(sourceIri);
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

            admitted.Add(new EuPublisherMarkedAmendmentAttribution(projection));
        }

        return new EuLocatedAmendmentProduction(
            admitted.AsReadOnly(), ambiguous.AsReadOnly(), excluded.AsReadOnly());
    }

    internal static bool TryGetCompleteCorpus(
        CorpusRecordSetWriteResult result,
        out VerifiedCorpusRecordSet corpus)
    {
        ArgumentNullException.ThrowIfNull(result);
        corpus = result.VerifiedSet!;
        var completion = result.Completion;
        var records = result.VerifiedSet?.Set.Records;
        if (result.Refusal is not null || result.SetRef is null || result.RetainedFloor is null ||
            completion is not { State: CorpusRecordSetCompletionState.Complete } || records is null ||
            completion.ExpectedObjectCount != records.Count || completion.Entries.Count != records.Count)
        {
            corpus = null!;
            return false;
        }

        for (var index = 0; index < records.Count; index++)
        {
            if (completion.Entries[index].ObjectRef != records[index].ObjectRef ||
                completion.Entries[index].ObjectOrdinal != records[index].ObjectOrdinal)
            {
                corpus = null!;
                return false;
            }
        }

        return true;
    }

    private static OfficialIdentitySet Work(string iri) =>
        new(PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, iri)]);
}
