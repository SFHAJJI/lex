using Lex.V3.Contracts;
using System.Globalization;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

internal static class EuObservedWorkIdentity
{
    internal static bool IsProven(EuQueryExecutionResult run, string work) =>
        EuAppendixASeedMap.SeedsInCelexOrder.Any(seed => seed.WorkRoot == work) ||
        run.ObservedWorkFacts.Any(facts => facts.PublisherWorkIri == work &&
            facts.CensusInterpretationProfileRef is not null &&
            EuAppendixASeedMap.SeedsInCelexOrder.Any(seed => seed.Celex == facts.SeedCelex && seed.WorkRoot == facts.RootWorkIri));

    internal static string? Resolve(EuQueryExecutionResult run, string publisherWorkIri, bool originalOnly = false) =>
        Resolve(run.ObservedWorkFacts, publisherWorkIri, originalOnly);

    internal static string? Resolve(IReadOnlyList<EuObservedWorkFacts> works, string publisherWorkIri, bool originalOnly = false)
    {
        var originals = EuAppendixASeedMap.SeedsInCelexOrder
            .Where(seed => string.Equals(seed.WorkRoot, publisherWorkIri, StringComparison.Ordinal)).Take(2).ToArray();
        if (originals.Length == 1) return originals[0].Celex;
        if (originalOnly) return null;
        var matches = works.Where(work => string.Equals(work.PublisherWorkIri, publisherWorkIri, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0 || matches.Any(work => work.RootWorkIri == publisherWorkIri)) return null;
        var celexPredicate = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalIdCelex);
        var terms = matches.SelectMany(work => work.Observations)
            .Where(observation => observation.PredicateIri == celexPredicate)
            .Select(observation => observation.Value).Distinct().ToArray();
        if (terms.Length != 1 || terms[0].Kind != RepeatedEnumerationRdfTermKind.Literal ||
            terms[0].Language is not null || terms[0].Datatype != "http://www.w3.org/2001/XMLSchema#string") return null;
        var celex = terms[0].Value!;
        if (celex.Length < 11 || celex[0] != '0' || celex[^9] != '-' ||
            !DateOnly.TryParseExact(celex[^8..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return null;
        // The reviewed seed admits the complete core, including treaty /TXT and agreement
        // parentheticals. The observed identifier is preserved; none is synthesized.
        var core = celex[1..^9];
        if (matches.Any(work => !EuAppendixASeedMap.SeedsInCelexOrder.Any(seed =>
                seed.Celex == work.SeedCelex && seed.WorkRoot == work.RootWorkIri && seed.Celex[1..] == core))) return null;
        return celex;
    }
}
