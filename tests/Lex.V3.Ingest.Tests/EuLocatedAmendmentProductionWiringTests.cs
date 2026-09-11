using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Facts;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

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
    public void PublicResultFactoriesCannotPairLocatedEvidenceWithACallerChosenCorpus()
    {
        var publicFactories = typeof(EuQueryExecutionResult)
            .GetMethods(BindingFlags.Public | BindingFlags.Static);

        Assert.IsFalse(publicFactories.Any(method => method.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(IReadOnlyList<EuLocatedAmendmentAxiomObservation>))));
    }

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
            observation.AnnotatedSourceIris.Single());
        Assert.AreEqual(Target, observation.AnnotatedTargetIris.Single());

        var locatedOutcome = result.FamilyOutcomes.Single(outcome =>
            observation.InterpretationProfileRefs.Contains(outcome.Proof!.InterpretationProfileRef));
        Assert.AreEqual(Rows(observation.AnnotatedSourceIris.Single()).Length, locatedOutcome.DeliveredRowCount);
        Assert.IsNotNull(result.LocatedAmendmentProduction);
        Assert.HasCount(1, result.LocatedAmendmentProduction.Admitted,
            string.Join("; ", result.LocatedAmendmentProduction.Excluded.Select(item =>
                $"{item.Kind}/{item.ProjectionRefusal}/{item.Detail}")));
        Assert.IsEmpty(result.LocatedAmendmentProduction.Ambiguous);
        Assert.IsEmpty(result.LocatedAmendmentProduction.Excluded);
        Assert.AreEqual(
            TargetBodyScope.BodyOutsideScope,
            result.LocatedAmendmentProduction.Admitted[0].Axiom.Edge.Fact.TargetBodyScope);
    }

    [TestMethod]
    public async Task ProvenLocatedAbsenceRemainsEmptyRatherThanUnobserved()
    {
        var result = await EuAxiomWiringHarness.RunAsync(
            parent => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(parent));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsEmpty(result.LocatedAmendmentObservations);
        Assert.IsNotNull(result.LocatedAmendmentProduction);
        Assert.IsEmpty(result.LocatedAmendmentProduction.Admitted);
        Assert.IsEmpty(result.LocatedAmendmentProduction.Ambiguous);
        Assert.IsEmpty(result.LocatedAmendmentProduction.Excluded);
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
        new (string Predicate, string Value, bool IsIri)[]
        {
            (RdfType, Owl + "Axiom", true),
            (Owl + "annotatedProperty", EuAmendmentRelationVocabulary.AmendsPredicateUri, true),
            (Owl + "annotatedSource", parent, true),
            (Owl + "annotatedTarget", Target, targetIsIri),
            (EuAmendmentRelationVocabulary.ReferenceToModifiedLocationUri,
                "{AN|http://publications.europa.eu/resource/authority/fd_370/AN} 1", false),
            (EuAmendmentRelationVocabulary.Role2Uri,
                "{R|http://publications.europa.eu/resource/authority/fd_375/R}", false),
            (EuAmendmentRelationVocabulary.TypeOfLinkTargetUri, "MS", false),
        }
        .OrderBy(static row => row.Predicate, StringComparer.Ordinal)
        .ThenBy(static row => row.Value, StringComparer.Ordinal)
        .Select(row => EuAcquisitionTestFixture.ReifiedAxiomFactsRow(
            parent, Axiom, row.Predicate, row.Value, row.IsIri))
        .ToArray();
}
