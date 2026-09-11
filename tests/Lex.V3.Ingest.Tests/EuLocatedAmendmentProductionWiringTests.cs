using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuLocatedAmendmentProductionWiringTests
{
    private const string Owl = "http://www.w3.org/2002/07/owl#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string Axiom = "http://publications.europa.eu/.well-known/genid/located-production/1";
    private const string Target =
        "http://publications.europa.eu/resource/cellar/cccccccc-0000-0000-0000-00000000000c";

    [TestMethod]
    public async Task ProvenLocatedRowsReachTheRunWithTheirExactProfileReference()
    {
        var result = await EuAxiomWiringHarness.RunAsync(
            parent => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(parent),
            parent => EuAcquisitionTestFixture.LocatedAmendmentScriptFrom(
                Rows(parent)));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.HasCount(1, result.LocatedAmendmentObservations);
        var observation = result.LocatedAmendmentObservations[0];
        Assert.AreEqual(Axiom, observation.AxiomIri);
        Assert.AreEqual(
            result.CorpusRecordSet!.Set.Records[0].ObjectRef.PublisherUri,
            observation.AnnotatedSourceIri);
        Assert.AreEqual(Target, observation.AnnotatedTargetIris.Single());

        var locatedOutcome = result.FamilyOutcomes.Single(outcome =>
            outcome.Proof?.InterpretationProfileRef == observation.InterpretationProfileRef);
        Assert.AreEqual(Rows(observation.AnnotatedSourceIri).Length, locatedOutcome.DeliveredRowCount);
    }

    [TestMethod]
    public async Task ProvenLocatedAbsenceRemainsEmptyRatherThanUnobserved()
    {
        var result = await EuAxiomWiringHarness.RunAsync(
            parent => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(parent));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsEmpty(result.LocatedAmendmentObservations);
    }

    [TestMethod]
    public async Task AnUnaddressableLocatedTargetRefusesTheRunRatherThanDisappearing()
    {
        var result = await EuAxiomWiringHarness.RunAsync(
            parent => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(parent),
            parent => EuAcquisitionTestFixture.LocatedAmendmentScriptFrom(
                Rows(parent, targetIsIri: false)));

        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.LocatedAmendmentDecodeRefused, result.Refusal.Code);
        StringAssert.Contains(
            result.Refusal.Detail,
            nameof(EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedTargetMissingOrNotAnIri));
        Assert.IsEmpty(result.LocatedAmendmentObservations);
    }

    private static string[] Rows(string parent, bool targetIsIri = true) =>
    [
        EuAcquisitionTestFixture.ReifiedAxiomFactsRow(
            parent, Axiom, RdfType, Owl + "Axiom", valueIsIri: true),
        EuAcquisitionTestFixture.ReifiedAxiomFactsRow(
            parent, Axiom, Owl + "annotatedProperty",
            EuAmendmentRelationVocabulary.AmendsPredicateUri, valueIsIri: true),
        EuAcquisitionTestFixture.ReifiedAxiomFactsRow(
            parent, Axiom, Owl + "annotatedSource", parent, valueIsIri: true),
        EuAcquisitionTestFixture.ReifiedAxiomFactsRow(
            parent, Axiom, Owl + "annotatedTarget", Target, valueIsIri: targetIsIri),
    ];
}
