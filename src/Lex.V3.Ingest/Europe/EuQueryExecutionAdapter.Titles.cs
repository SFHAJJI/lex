using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// One title the publisher states for one English or French expression of a census work (EU index /7, Release 3's item 1):
/// a <c>cdm:expression_title</c> or <c>cdm:expression_title_short</c> literal of family X, exactly as served
/// (<see cref="RawLexical"/>, with the literal's language tag or datatype as the term carried it), the expression's work (the
/// family's own <c>?parent</c> join, canonical) and language (its <c>expression_uses_language</c> authority IRI), and the
/// coordinate of the verified family-X batch it was read from (<see cref="InterpretationProfileRef"/>). A publisher's title,
/// never a date and never Lex's words.
/// </summary>
public sealed record EuObservedExpressionTitle(
    string PublisherWorkIri,
    string PublisherExpressionIri,
    string LanguageIri,
    string PredicateIri,
    string RawLexical,
    string? RawLanguageTag,
    string? RawDatatypeIri,
    SourceArtifactRef InterpretationProfileRef);

public sealed partial class EuQueryExecutionAdapter
{
    /// <summary>The two authority languages a title is held for: the languages Lex acquires EU expressions in (Decision 89).</summary>
    internal static readonly IReadOnlySet<string> TitleLanguageIris = new HashSet<string>(StringComparer.Ordinal)
    {
        "http://publications.europa.eu/resource/authority/language/ENG",
        "http://publications.europa.eu/resource/authority/language/FRA",
    };

    /// <summary>
    /// The titles family X delivered for the English and French expressions of every seed's proven closure, from the same checked
    /// rows the decode read: per batch, each expression's work (its <c>?parent</c>, canonical, in a closure) and language (its one
    /// bound <c>expression_uses_language</c>), then every bound literal of its title predicates. Called only after the same run
    /// decoded every seed's closure. Not part of the run's result digest: the rows it reads are the checked family-X pages the
    /// checkpoint already pins, so a replay recomputes it from them, and the digest keeps its bytes (the run-16 checkpoint).
    /// An expression with two languages, or a title row that is not a literal, is not read (the decode refuses the first).
    /// </summary>
    internal static IReadOnlyList<EuObservedExpressionTitle> ProjectExpressionTitles(
        IReadOnlyDictionary<string, (HashSet<string> Closure, string RootIri)> closures,
        IReadOnlyList<(IReadOnlyList<RepeatedEnumerationRow> Rows,
            RepeatedEnumerationInterpretationProfile Profile, SourceArtifactRef Proof)> batches)
    {
        ArgumentNullException.ThrowIfNull(closures);
        ArgumentNullException.ThrowIfNull(batches);
        var works = closures.Values.SelectMany(static value => value.Closure).ToHashSet(StringComparer.Ordinal);
        var title = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionTitle);
        var shortTitle = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionTitleShort);
        var usesLanguage = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage);
        var titles = new HashSet<EuObservedExpressionTitle>();
        foreach (var batch in batches)
        {
            var projection = batch.Profile.ProjectionVariables.ToList();
            var parentIndex = projection.IndexOf("parent");
            var objectIndex = projection.IndexOf("object");
            var predicateIndex = projection.IndexOf("predicate");
            var valueIndex = projection.IndexOf("value");
            if (parentIndex < 0 || objectIndex < 0 || predicateIndex < 0 || valueIndex < 0)
                throw new ArgumentException("Expression titles require the expression-facts projection.", nameof(batches));

            var languages = new Dictionary<(string Work, string Expression), HashSet<string>>();
            var candidates = new List<(string Work, string Expression, string Predicate, RepeatedEnumerationRdfTerm Value)>();
            foreach (var row in batch.Rows)
            {
                var parent = row.Terms[parentIndex];
                var expression = row.Terms[objectIndex];
                var predicate = row.Terms[predicateIndex];
                if (parent.Kind != RepeatedEnumerationRdfTermKind.Iri || expression.Kind != RepeatedEnumerationRdfTermKind.Iri ||
                    predicate.Kind != RepeatedEnumerationRdfTermKind.Iri)
                    continue;
                var work = EuPackRootCanonicalForm.TryCanonicalize(parent.Value!, out _);
                if (work is null || !works.Contains(work)) continue;
                var value = row.Terms[valueIndex];
                var key = (work, expression.Value!);
                if (predicate.Value == usesLanguage && value.Kind == RepeatedEnumerationRdfTermKind.Iri)
                {
                    if (!languages.TryGetValue(key, out var held)) languages[key] = held = new HashSet<string>(StringComparer.Ordinal);
                    held.Add(value.Value!);
                }
                else if ((predicate.Value == title || predicate.Value == shortTitle) && value.Kind == RepeatedEnumerationRdfTermKind.Literal)
                {
                    candidates.Add((work, expression.Value!, predicate.Value!, value));
                }
            }

            foreach (var candidate in candidates)
            {
                if (!languages.TryGetValue((candidate.Work, candidate.Expression), out var held) || held.Count != 1) continue;
                var language = held.Single();
                if (!TitleLanguageIris.Contains(language)) continue;
                titles.Add(new EuObservedExpressionTitle(candidate.Work, candidate.Expression, language, candidate.Predicate,
                    candidate.Value.Value!, candidate.Value.Language, candidate.Value.Datatype, batch.Proof));
            }
        }

        return Array.AsReadOnly(titles
            .OrderBy(static value => value.PublisherWorkIri, StringComparer.Ordinal)
            .ThenBy(static value => value.PublisherExpressionIri, StringComparer.Ordinal)
            .ThenBy(static value => value.PredicateIri, StringComparer.Ordinal)
            .ThenBy(static value => value.RawLexical, StringComparer.Ordinal)
            .ThenBy(static value => value.RawLanguageTag, StringComparer.Ordinal)
            .ThenBy(static value => value.RawDatatypeIri, StringComparer.Ordinal)
            .ThenBy(static value => value.InterpretationProfileRef.ResourceId, StringComparer.Ordinal)
            .ThenBy(static value => value.InterpretationProfileRef.Sha256, StringComparer.Ordinal)
            .ToArray());
    }
}
