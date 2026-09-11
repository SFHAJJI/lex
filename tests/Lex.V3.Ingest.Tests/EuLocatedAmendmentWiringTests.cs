using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuLocatedAmendmentWiringTests
{
    private const string ParentOne =
        "http://publications.europa.eu/resource/cellar/aaaaaaaa-0000-0000-0000-00000000000a";
    private const string ParentTwo =
        "http://publications.europa.eu/resource/cellar/bbbbbbbb-0000-0000-0000-00000000000b";
    private const string Target =
        "http://publications.europa.eu/resource/cellar/cccccccc-0000-0000-0000-00000000000c";

    [TestMethod]
    public void EachLocatedAmendmentBatchRetainsItsOwnProofCoordinate()
    {
        var profile = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.LocatedAmendmentFacts);
        var proofOne = RepeatedEnumerationInterpretationProfileIdentity.Create(
            "urn:uuid:00000000-0000-4000-8000-00000000a801", profile);
        var proofTwo = RepeatedEnumerationInterpretationProfileIdentity.Create(
            "urn:uuid:00000000-0000-4000-8000-00000000a802", profile);
        var batches = new[]
        {
            (Rows: Rows(ParentOne, "urn:axiom:one"), Profile: profile, Proof: proofOne),
            (Rows: Rows(ParentTwo, "urn:axiom:two"), Profile: profile, Proof: proofTwo),
        };

        var observations = EuQueryExecutionAdapter.DecodeLocatedAmendmentBatches(
            batches, out var refusal, out var offendingValue);

        Assert.AreEqual(EuLocatedAmendmentAxiomDecodeRefusal.None, refusal);
        Assert.IsNull(offendingValue);
        Assert.IsNotNull(observations);
        Assert.HasCount(2, observations);
        Assert.AreSame(
            proofOne,
            observations.Single(item => item.AxiomIri == "urn:axiom:one").InterpretationProfileRef);
        Assert.AreSame(
            proofTwo,
            observations.Single(item => item.AxiomIri == "urn:axiom:two").InterpretationProfileRef);
    }

    private static IReadOnlyList<RepeatedEnumerationRow> Rows(string parent, string axiom) =>
    [
        Row(parent, axiom, EuObjectFactsDiscoveryPlan.RdfTypePredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuObjectFactsDiscoveryPlan.OwlAxiomClassIri)),
        Row(parent, axiom, EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri,
            RepeatedEnumerationRdfTerm.Iri(parent)),
        Row(parent, axiom, EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuAmendmentRelationVocabulary.AmendsPredicateUri)),
        Row(parent, axiom, EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
            RepeatedEnumerationRdfTerm.Iri(Target)),
    ];

    private static RepeatedEnumerationRow Row(
        string parent, string axiom, string predicate, RepeatedEnumerationRdfTerm value)
    {
        var kind = value.Kind == RepeatedEnumerationRdfTermKind.Iri ? "iri" : "literal";
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(parent),
            RepeatedEnumerationRdfTerm.Iri(axiom),
            RepeatedEnumerationRdfTerm.Iri(predicate),
            value,
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Datatype ?? "", null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Language ?? "", null, null),
            RepeatedEnumerationRdfTerm.Literal(parent, null, null),
            RepeatedEnumerationRdfTerm.Literal(axiom, null, null),
            RepeatedEnumerationRdfTerm.Literal(predicate, null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Value!, null, null),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Datatype ?? "", null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Language ?? "", null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms), Array.AsReadOnly(terms[7..]), Array.AsReadOnly(terms[7..]));
    }
}
