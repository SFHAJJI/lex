using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>A publisher term with the coordinate of its own verified object-facts batch.</summary>
public sealed record EuWorkFactObservation(
    string PublisherWorkIri,
    string PredicateIri,
    RepeatedEnumerationRdfTerm Value,
    SourceArtifactRef InterpretationProfileRef);

/// <summary>
/// The census relationship and original RDF terms used for identity and temporal projection.
/// These are observations, not a claim that a consolidation date is an applicability date.
/// </summary>
public sealed record EuObservedWorkFacts(
    string SeedCelex,
    string RootWorkIri,
    string PublisherWorkIri,
    IReadOnlyList<EuWorkFactObservation> Observations,
    SourceArtifactRef? CensusInterpretationProfileRef = null);

public sealed partial class EuQueryExecutionAdapter
{
    // Called only after the same run decoded every seed's proven closure. Keep P batches separate:
    // a shared interpretation profile shape does not make their evidence coordinates interchangeable.
    internal static IReadOnlyList<EuObservedWorkFacts> ProjectWorkFacts(
        IReadOnlyDictionary<string, (HashSet<string> Closure, string RootIri)> closures,
        IReadOnlyList<(IReadOnlyList<RepeatedEnumerationRow> Rows,
            RepeatedEnumerationInterpretationProfile Profile, SourceArtifactRef Proof)> batches,
        IReadOnlyDictionary<string, SourceArtifactRef>? censusProofs = null)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal)
        {
            EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalIdCelex),
            EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkDateDocument),
            EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ActConsolidatedDate),
        };
        var byWork = new Dictionary<string, List<EuWorkFactObservation>>(StringComparer.Ordinal);
        foreach (var batch in batches)
        {
            var projection = batch.Profile.ProjectionVariables.ToList();
            var objectIndex = projection.IndexOf("object");
            var predicateIndex = projection.IndexOf("predicate");
            var valueIndex = projection.IndexOf("value");
            if (objectIndex < 0 || predicateIndex < 0 || valueIndex < 0)
                throw new ArgumentException("Work facts require the object-facts projection.", nameof(batches));
            foreach (var row in batch.Rows)
            {
                var predicate = row.Terms[predicateIndex];
                if (predicate.Kind != RepeatedEnumerationRdfTermKind.Iri || !wanted.Contains(predicate.Value!)) continue;
                var subject = row.Terms[objectIndex];
                var work = subject.Kind == RepeatedEnumerationRdfTermKind.Iri
                    ? EuPackRootCanonicalForm.TryCanonicalize(subject.Value!, out _) : null;
                if (work is null) throw new ArgumentException("Work facts contain an invalid publisher identity.", nameof(batches));
                if (!byWork.TryGetValue(work, out var observations)) byWork.Add(work, observations = []);
                observations.Add(new(work, predicate.Value!, row.Terms[valueIndex], batch.Proof));
            }
        }
        return Array.AsReadOnly(closures.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Closure.Order(StringComparer.Ordinal).Select(work =>
                new EuObservedWorkFacts(pair.Key, pair.Value.RootIri, work,
                    Array.AsReadOnly((byWork.GetValueOrDefault(work) ?? [])
                        .Distinct()
                        .OrderBy(value => value.PredicateIri, StringComparer.Ordinal)
                        .ThenBy(value => value.Value.Kind)
                        .ThenBy(value => value.Value.Value, StringComparer.Ordinal)
                        .ThenBy(value => value.Value.Datatype, StringComparer.Ordinal)
                        .ThenBy(value => value.Value.Language, StringComparer.Ordinal)
                        .ThenBy(value => value.InterpretationProfileRef.ResourceId, StringComparer.Ordinal)
                        .ThenBy(value => value.InterpretationProfileRef.Sha256, StringComparer.Ordinal)
                        .ToArray()), censusProofs?.GetValueOrDefault(pair.Key)))).ToArray());
    }
}
