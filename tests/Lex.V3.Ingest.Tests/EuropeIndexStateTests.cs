using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuropeIndexStateTests
{
    private const string Root = "http://publications.europa.eu/resource/cellar/aaaaaaaa-0000-0000-0000-00000000000a";
    private const string State = "http://publications.europa.eu/resource/cellar/bbbbbbbb-0000-0000-0000-00000000000b";
    private const string Other = "http://publications.europa.eu/resource/cellar/cccccccc-0000-0000-0000-00000000000c";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";

    [TestMethod]
    public void OriginalActDocumentDateDoesNotBecomeAConsolidationOrApplicabilityDate()
    {
        var original = Work(Root, Date("2016-04-27"));
        var row = EuropeIndexBuilder.ProjectStates([original]).Single();
        Assert.AreEqual(EuropeIndexStateDateStatus.OriginalWording, row.DateStatus);
        Assert.IsNull(row.PublisherConsolidationDate);
        StringAssert.Contains(row.FactsJson, "2016-04-27");
    }

    [TestMethod]
    public void TwoWorksOnOneDateAreBothAmbiguousAndNeitherDisappears()
    {
        var rows = EuropeIndexBuilder.ProjectStates([Work(State, Date("2024-01-01")), Work(Other, Date("2024-01-01"))]);
        Assert.HasCount(2, rows);
        Assert.IsTrue(rows.All(row => row.DateStatus == EuropeIndexStateDateStatus.AmbiguousVersion));
        Assert.IsTrue(rows.All(row => row.PublisherConsolidationDate == "2024-01-01"));
    }

    [TestMethod]
    public void MultipleDatesOnOneWorkDoNotSelectAnArbitraryVersion()
    {
        var row = EuropeIndexBuilder.ProjectStates([Work(State, Date("2024-01-01"), Date("2024-02-01"))]).Single();
        Assert.AreEqual(EuropeIndexStateDateStatus.AmbiguousVersion, row.DateStatus);
        Assert.IsNull(row.PublisherConsolidationDate);
    }

    [TestMethod]
    public void MissingAbsentAndInvalidDatesHaveDifferentTypedOutcomes()
    {
        Assert.AreEqual(EuropeIndexStateDateStatus.ObservationMissing,
            EuropeIndexBuilder.ProjectStates([Work(State)]).Single().DateStatus);
        Assert.AreEqual(EuropeIndexStateDateStatus.PublisherDateAbsent,
            EuropeIndexBuilder.ProjectStates([Work(State, RepeatedEnumerationRdfTerm.Unbound())]).Single().DateStatus);
        foreach (var term in new[] { Date("2024-02-30"),
                     RepeatedEnumerationRdfTerm.Literal("2024-01-01", null, "fr"),
                     RepeatedEnumerationRdfTerm.Literal("2024-01-01", "http://www.w3.org/2001/XMLSchema#string", null),
                     RepeatedEnumerationRdfTerm.Iri("2024-01-01") })
            Assert.AreEqual(EuropeIndexStateDateStatus.PublisherDateUnusable,
                EuropeIndexBuilder.ProjectStates([Work(State, term)]).Single().DateStatus);
    }

    [TestMethod]
    public void OneTypedPublisherDateAndItsEvidenceRemainAnObservedCoordinate()
    {
        var row = EuropeIndexBuilder.ProjectStates([Work(State, Date("2024-01-01"))]).Single();
        Assert.AreEqual(EuropeIndexStateDateStatus.ObservedConsolidationDate, row.DateStatus);
        Assert.AreEqual("2024-01-01", row.PublisherConsolidationDate);
        Assert.IsNull(row.PublisherWorkCelex, "No CELEX fact was observed; the seed must not stand in for this work.");
        StringAssert.Contains(row.FactsJson, "urn:uuid:00000000-0000-4000-8000-00000000a801");
    }

    [TestMethod]
    public void DuplicateWorkCoordinatesRefuseInsteadOfSilentlyReducingPopulation() =>
        Assert.ThrowsExactly<InvalidDataException>(() => EuropeIndexBuilder.ProjectStates([Work(State), Work(State)]));

    private static RepeatedEnumerationRdfTerm Date(string value) => RepeatedEnumerationRdfTerm.Literal(value, XsdDate, null);
    private static EuObservedWorkFacts Work(string work, params RepeatedEnumerationRdfTerm[] dates)
    {
        var proof = new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-00000000a801", new string('a', 64));
        return new("32016R0679", Root, work, dates.Select(date => new EuWorkFactObservation(work,
            EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ActConsolidatedDate), date, proof)).ToArray(), proof);
    }
}
