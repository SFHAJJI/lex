using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuWorkFactsWiringTests
{
    private const string Root = "http://publications.europa.eu/resource/cellar/aaaaaaaa-0000-0000-0000-00000000000a";
    private const string State = "http://publications.europa.eu/resource/cellar/bbbbbbbb-0000-0000-0000-00000000000b";
    private const string DateType = "http://www.w3.org/2001/XMLSchema#date";
    private const string StringType = "http://www.w3.org/2001/XMLSchema#string";

    [TestMethod]
    public void EachWorkPreservesItsOwnBatchEvidenceAndCensusRelationship()
    {
        var profile = Profile();
        var first = Proof(profile, "a801");
        var second = Proof(profile, "a802");
        var observed = EuQueryExecutionAdapter.ProjectWorkFacts(Closure(),
        [
            (Rows(profile, Root, EuCdmPredicate.ResourceLegalIdCelex,
                RepeatedEnumerationRdfTerm.Literal("32016R0679", StringType, null)), profile, first),
            (Rows(profile, State, EuCdmPredicate.ResourceLegalIdCelex,
                RepeatedEnumerationRdfTerm.Literal("02016R0679-20160504", StringType, null)), profile, second),
        ]);
        Assert.HasCount(2, observed);
        var consolidated = observed.Single(work => work.PublisherWorkIri == State);
        Assert.AreEqual("32016R0679", consolidated.SeedCelex);
        Assert.AreEqual(Root, consolidated.RootWorkIri);
        Assert.AreSame(second, consolidated.Observations.Single().InterpretationProfileRef);
        Assert.AreEqual("02016R0679-20160504", consolidated.Observations.Single().Value.Value);
        Assert.AreSame(first, observed.Single(work => work.PublisherWorkIri == Root).Observations.Single().InterpretationProfileRef);
    }

    [TestMethod]
    public void EqualLexicalDatesKeepDifferentRdfTypesAndLanguageTags()
    {
        var profile = Profile();
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Literal("2024-01-01", DateType, null),
            RepeatedEnumerationRdfTerm.Literal("2024-01-01", StringType, null),
            RepeatedEnumerationRdfTerm.Literal("2024-01-01", null, "fr"),
            RepeatedEnumerationRdfTerm.Iri("2024-01-01"),
        };
        var rows = terms.SelectMany(term => Rows(profile, State, EuCdmPredicate.ActConsolidatedDate, term)).ToArray();
        var result = EuQueryExecutionAdapter.ProjectWorkFacts(Closure(), [(rows, profile, Proof(profile, "a811"))]);
        CollectionAssert.AreEquivalent(terms, result.Single(work => work.PublisherWorkIri == State)
            .Observations.Select(observation => observation.Value).ToArray());
    }

    [TestMethod]
    public void AbsentDateAndMissingRowsRemainDistinguishableAndDoNotRemoveAWork()
    {
        var profile = Profile();
        var result = EuQueryExecutionAdapter.ProjectWorkFacts(Closure(),
            [(Rows(profile, State, EuCdmPredicate.ActConsolidatedDate, RepeatedEnumerationRdfTerm.Unbound()),
                profile, Proof(profile, "a821"))]);
        Assert.HasCount(2, result);
        Assert.HasCount(0, result.Single(work => work.PublisherWorkIri == Root).Observations);
        Assert.AreEqual(RepeatedEnumerationRdfTermKind.Unbound,
            result.Single(work => work.PublisherWorkIri == State).Observations.Single().Value.Kind);
    }

    [TestMethod]
    public void SharedWorkKeepsEverySeedAssociationAndProjectionOrderIsStable()
    {
        var profile = Profile();
        var closures = Closure();
        closures.Add("32019R0947", (new HashSet<string>([State], StringComparer.Ordinal), State));
        var rows = Rows(profile, State, EuCdmPredicate.WorkDateDocument,
            RepeatedEnumerationRdfTerm.Literal("2024-01-01", DateType, null));
        var result = EuQueryExecutionAdapter.ProjectWorkFacts(closures, [(rows, profile, Proof(profile, "a831"))]);
        CollectionAssert.AreEqual(new[] { "32016R0679", "32019R0947" },
            result.Where(work => work.PublisherWorkIri == State).Select(work => work.SeedCelex).ToArray());
    }

    private static Dictionary<string, (HashSet<string> Closure, string RootIri)> Closure() =>
        new(StringComparer.Ordinal) { ["32016R0679"] = (new HashSet<string>([State, Root], StringComparer.Ordinal), Root) };
    private static RepeatedEnumerationInterpretationProfile Profile() =>
        EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ObjectFacts);
    private static SourceArtifactRef Proof(RepeatedEnumerationInterpretationProfile profile, string suffix) =>
        RepeatedEnumerationInterpretationProfileIdentity.Create("urn:uuid:00000000-0000-4000-8000-00000000" + suffix, profile);
    private static IReadOnlyList<RepeatedEnumerationRow> Rows(RepeatedEnumerationInterpretationProfile profile,
        string work, EuCdmPredicate predicate, RepeatedEnumerationRdfTerm value)
    {
        var values = new Dictionary<string, RepeatedEnumerationRdfTerm>
        {
            ["object"] = RepeatedEnumerationRdfTerm.Iri(work),
            ["predicate"] = RepeatedEnumerationRdfTerm.Iri(EuObjectFactsDiscoveryPlan.CdmIri(predicate)),
            ["value"] = value,
        };
        return [new RepeatedEnumerationRow(profile.ProjectionVariables.Select(name =>
            values.GetValueOrDefault(name) ?? RepeatedEnumerationRdfTerm.Literal("", null, null)).ToArray(), [], [])];
    }
}
