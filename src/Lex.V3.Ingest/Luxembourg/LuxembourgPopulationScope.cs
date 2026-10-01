using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>A declared acquisition scope, not evidence that any range has been enumerated.</summary>
public sealed class LuxembourgPopulationScope
{
    public const string LegislativePolicy = "lex-lu-legislative-uri-scope/1";
    public const string DeclaredRangePolicy = "lex-lu-declared-uri-ranges/1";
    private const string Legislation = "http://data.legilux.public.lu/eli/etat/leg/";

    public LuxembourgPopulationScope(string policy, IReadOnlyList<LuxembourgActRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        if (policy is not (LegislativePolicy or DeclaredRangePolicy))
            throw new ArgumentException("Unknown Luxembourg population scope policy.", nameof(policy));
        var copy = ranges.ToArray();
        if (copy.Length is < 1 or > 64 || copy.Any(static range => range is null))
            throw new ArgumentException("A population scope needs 1..64 explicit ranges.", nameof(ranges));
        if (copy.Select(static range => range.Name).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Population range names must be unique.", nameof(ranges));
        for (var i = 1; i < copy.Length; i++)
            if (string.CompareOrdinal(copy[i - 1].StartInclusive, copy[i].StartInclusive) >= 0 ||
                string.CompareOrdinal(copy[i - 1].EndExclusive, copy[i].StartInclusive) > 0)
                throw new ArgumentException("Population ranges must be ordered and disjoint.", nameof(ranges));
        if (policy == LegislativePolicy && !copy.SequenceEqual(LegislativeRanges()))
            throw new ArgumentException("The legislative policy binds its exact declared URI families.", nameof(ranges));
        Policy = policy;
        Ranges = Array.AsReadOnly(copy);
    }

    public string Policy { get; }
    public IReadOnlyList<LuxembourgActRange> Ranges { get; }

    public static LuxembourgPopulationScope Legislative { get; } = new(LegislativePolicy, LegislativeRanges());

    public static LuxembourgPopulationScope FromRange(LuxembourgActRange range) =>
        new(DeclaredRangePolicy, [range ?? throw new ArgumentNullException(nameof(range))]);

    internal bool SameScope(LuxembourgPopulationScope other) =>
        Policy == other.Policy && Ranges.SequenceEqual(other.Ranges);

    internal byte[] DeclarationBytes() => Encoding.UTF8.GetBytes(ContractJson.Serialize(new
    {
        schema = "lex-lu-population-scope/1",
        policy = Policy,
        selected_ranges = Ranges,
        unselected_iri_families = "not_enumerated_outside_declared_ranges",
        documents = "selected bodies and accepted publisher documents of admitted objects; existing scope and rights gates apply",
        coverage = "declared_scope_only; no enumeration or legal-family completeness is asserted by this declaration",
    }));

    internal byte[] ManifestBytes(VerifiedLuxembourgSourceProfile profile, SourceArtifactRef scopeDefinition,
        SourceArtifactRef vocabularyObservation) => Encoding.UTF8.GetBytes(ContractJson.Serialize(new
    {
        schema = "lex-lu-population-family-dispositions/1",
        policy = Policy,
        scope_definition = scopeDefinition,
        vocabulary_observation = vocabularyObservation,
        selected_ranges = Ranges,
        publication_families = profile.ObservedIriVocabulary
            .Where(static value => value.Kind == LuxembourgVocabularyKind.TypeDocument)
            .Select(static value => value.FullIri).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(iri => new
            {
                family_iri = iri,
                disposition = "not_enumerated_as_independent_type_family",
                selected_objects = "objects discovered inside the declared ranges retain individual scope and body outcomes",
            }).ToArray(),
        outside_declared_ranges = "not_enumerated_outside_launch_uri_scope",
        unobserved_type_families = "not_enumerated_not_in_observed_vocabulary",
        coverage = "URI range proofs only; no type-family completeness, absence or legal equivalence claim",
    }));

    private static LuxembourgActRange[] LegislativeRanges() =>
    [
        new("legislative-code", Legislation + "code/", Legislation + "code0"),
        new("legislative-loi", Legislation + "loi/", Legislation + "loi0"),
        new("legislative-rgd", Legislation + "rgd/", Legislation + "rgd0"),
    ];
}
