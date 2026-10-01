using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Reads one resource's evidence graph through a run-wide subject lookup.
/// The lookup must come from the proven assertion family, including subjects outside the
/// resource's acquisition partition. This traversal does not establish population completeness.</summary>
internal static class LuxembourgObservationDependencies
{
    internal static IReadOnlyList<LuxembourgObservedAssertion> Collect(
        string subject,
        SourceArtifactRef observationRef,
        Func<string, IReadOnlyList<LuxembourgObservedAssertion>> readSubject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(observationRef);
        ArgumentNullException.ThrowIfNull(readSubject);
        var assertions = new List<LuxembourgObservedAssertion>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        IReadOnlyList<LuxembourgObservedAssertion> rootAssertions = [];
        pending.Enqueue(subject);
        while (pending.TryDequeue(out var resource))
        {
            if (!visited.Add(resource)) continue;
            var rows = Read(resource);
            if (resource == subject) rootAssertions = rows;
            assertions.AddRange(rows);
            foreach (var assertion in rows)
            {
                if (assertion.ObjectKind == LuxembourgAssertionObjectKind.Iri &&
                    assertion.PredicateIri is
                        "http://data.legilux.public.lu/resource/ontology/jolux#isRealizedBy" or
                        "http://data.legilux.public.lu/resource/ontology/jolux#isEmbodiedBy" or
                        "http://data.legilux.public.lu/resource/ontology/jolux#isExemplifiedBy")
                    pending.Enqueue(assertion.ObjectIriOrLexical);
            }
        }

        // Qualification reads the original Act's own assertions, without traversing its WEMI.
        if (rootAssertions.Any(assertion => assertion.PredicateIri ==
                "http://www.w3.org/1999/02/22-rdf-syntax-ns#type" &&
                assertion.ObjectKind == LuxembourgAssertionObjectKind.Iri &&
                assertion.ObjectIriOrLexical == "http://data.legilux.public.lu/resource/ontology/jolux#Consolidation"))
        {
            var parents = rootAssertions.Where(assertion => assertion.PredicateIri ==
                    "http://data.legilux.public.lu/resource/ontology/jolux#isMemberOf" &&
                    assertion.ObjectKind == LuxembourgAssertionObjectKind.Iri)
                .Select(assertion => assertion.ObjectIriOrLexical).Distinct(StringComparer.Ordinal).ToArray();
            if (parents.Length == 1 && visited.Add(parents[0] + "/jo"))
                assertions.AddRange(Read(parents[0] + "/jo"));
        }
        return assertions;

        IReadOnlyList<LuxembourgObservedAssertion> Read(string key)
        {
            var rows = readSubject(key);
            ArgumentNullException.ThrowIfNull(rows);
            foreach (var row in rows)
                if (row.SubjectIri != key || row.ObservationRef != observationRef)
                    throw new InvalidOperationException(
                        "A dependency lookup must preserve the requested subject and run observation.");
            return rows;
        }
    }
}
