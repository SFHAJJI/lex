using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// Proves that every field and every multimap member survives a serialize and deserialize.
/// </summary>
[TestClass]
public sealed class FactsRoundTripTests
{
    [TestMethod]
    public void PublisherRelationSurvivesWithEveryAxiomAndQualifierInOrder()
    {
        var original = FactsFixtures.PublisherRelation();

        var restored = ContractJson.Deserialize<PublisherRelation>(ContractJson.Serialize(original));

        Assert.AreEqual(original.PredicateUri, restored.PredicateUri);
        Assert.AreEqual(original.Source.RawValue, restored.Source.RawValue);
        Assert.AreEqual(original.Target.RawValue, restored.Target.RawValue);
        Assert.AreEqual(original.Source.Publisher, restored.Source.Publisher);
        Assert.AreEqual(original.Observation.ObservationId, restored.Observation.ObservationId);
        Assert.AreEqual(original.Observation.ObservedAt, restored.Observation.ObservedAt);
        Assert.AreEqual(
            original.Observation.TransportBytes.ContentSha256,
            restored.Observation.TransportBytes.ContentSha256);
        Assert.AreEqual(
            original.Observation.TransportBytes.ByteLength,
            restored.Observation.TransportBytes.ByteLength);

        AssertAxiomsAreIdentical(original.QualifiedAxioms, restored.QualifiedAxioms);
    }

    /// <summary>
    /// The multimap is the field most likely to be quietly collapsed, so it is asserted member by
    /// member rather than by count.
    /// </summary>
    [TestMethod]
    public void TwoAxiomsSharingOneRemoteIdentifierBothSurvive()
    {
        var restored = ContractJson.Deserialize<PublisherRelation>(
            ContractJson.Serialize(FactsFixtures.PublisherRelation()));

        Assert.HasCount(2, restored.QualifiedAxioms);
        Assert.AreEqual("axiom-7731", restored.QualifiedAxioms[0].RemoteAxiomId);
        Assert.AreEqual("axiom-7731", restored.QualifiedAxioms[1].RemoteAxiomId);
        Assert.AreNotEqual(
            restored.QualifiedAxioms[0].Qualifiers[0].RawValue,
            restored.QualifiedAxioms[1].Qualifiers[0].RawValue);
    }

    [TestMethod]
    public void OneAxiomCarryingARepeatedQualifierPredicateKeepsBothValuesInOrder()
    {
        var restored = ContractJson.Deserialize<PublisherRelation>(
            ContractJson.Serialize(FactsFixtures.PublisherRelation()));

        var qualifiers = restored.QualifiedAxioms[0].Qualifiers;
        Assert.HasCount(2, qualifiers);
        Assert.AreEqual(qualifiers[0].PredicateUri, qualifiers[1].PredicateUri);
        Assert.AreEqual("first", qualifiers[0].RawValue);
        Assert.AreEqual("second", qualifiers[1].RawValue);
    }

    [TestMethod]
    public void DerivedInverseKeepsItsAuthorizationAndItsForwardAssertion()
    {
        var original = FactsFixtures.DerivedInverse();

        var restored = ContractJson.Deserialize<DerivedInverseRelation>(
            ContractJson.Serialize(original));

        Assert.AreEqual(FactsFixtures.InverseOfStatement, restored.AuthorizingOntologyStatementUri);
        Assert.AreEqual(FactsFixtures.ConsolidatesPredicate, restored.InverseOfPredicateUri);
        Assert.AreEqual(FactsFixtures.ConsolidatedByPredicate, restored.PredicateUri);
        Assert.AreEqual(
            original.DerivedFrom.Observation.TransportBytes.ContentSha256,
            restored.DerivedFrom.Observation.TransportBytes.ContentSha256);
        AssertAxiomsAreIdentical(
            original.DerivedFrom.QualifiedAxioms,
            restored.DerivedFrom.QualifiedAxioms);
    }

    [TestMethod]
    public void LocalInboundViewKeepsItsIncompleteScopeFlagAndItsContributingEdges()
    {
        var restored = ContractJson.Deserialize<LocalInboundView>(
            ContractJson.Serialize(FactsFixtures.InboundView()));

        Assert.IsFalse(restored.ScopeIsComplete);
        Assert.AreEqual(FactsFixtures.ScopeDigest, restored.ScopeDescriptorSha256);
        Assert.HasCount(1, restored.ContributingAssertions);
        Assert.AreEqual(
            FactsFixtures.ConsolidatesPredicate,
            restored.ContributingAssertions[0].PredicateUri);
    }

    [TestMethod]
    public void RelationFactKeepsItsKindBodyScopeAndEcliState()
    {
        var restored = ContractJson.Deserialize<RelationFact>(
            ContractJson.Serialize(FactsFixtures.AssertedFact()));

        Assert.AreEqual(RelationAssertionKind.PublisherAsserted, restored.Kind);
        Assert.AreEqual(TargetBodyScope.BodyInScopeHeld, restored.TargetBodyScope);
        Assert.AreEqual(EcliState.EcliPresent, restored.TargetEcliState);
        Assert.AreEqual("ECLI:EU:C:2020:1042", restored.TargetEcli);
        Assert.IsNotNull(restored.PublisherAsserted);
        Assert.IsNull(restored.OntologyAuthorizedInverse);
        Assert.IsNull(restored.LocalInboundView);
    }

    /// <summary>
    /// The edge is kept, the missing ECLI is typed, and no identifier is invented. All three at
    /// once, because any one of them alone is satisfiable by dropping the edge.
    /// </summary>
    [TestMethod]
    public void ACaseRelationWithNoPublisherEcliKeepsTheEdgeAndInventsNothing()
    {
        var restored = ContractJson.Deserialize<RelationFact>(
            ContractJson.Serialize(FactsFixtures.CaseFactWithoutEcli()));

        Assert.AreEqual(EcliState.EcliMissing, restored.TargetEcliState);
        Assert.IsNull(restored.TargetEcli);
        Assert.IsNotNull(restored.PublisherAsserted);
        Assert.AreEqual("62019CJ0311", restored.PublisherAsserted.Target.RawValue);
        Assert.AreEqual(IdentifierFamily.Celex, restored.PublisherAsserted.Target.Family);
        Assert.AreEqual(TargetBodyScope.BodyInScopeNotHeld, restored.TargetBodyScope);
    }

    [TestMethod]
    public void APublisherDateKeepsItsLexicalValueDatatypeAndPrecision()
    {
        var restored = ContractJson.Deserialize<PublisherDate>(
            ContractJson.Serialize(FactsFixtures.YearOnlyDate()));

        Assert.AreEqual("2019", restored.RawLexicalValue);
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#gYear", restored.DatatypeUri);
        Assert.AreEqual(DatePrecision.Year, restored.Precision);
        Assert.AreEqual(DateOpenSentinel.NotOpen, restored.OpenSentinel);
    }

    [TestMethod]
    public void AnOpenEndedDateStaysASentinelRatherThanBecomingAYearNineThousand()
    {
        var restored = ContractJson.Deserialize<PublisherDate>(
            ContractJson.Serialize(FactsFixtures.OpenEndedDate()));

        Assert.AreEqual("9999-12-31", restored.RawLexicalValue);
        Assert.AreEqual(DateOpenSentinel.OpenEnded, restored.OpenSentinel);
    }

    [TestMethod]
    public void ADateFactKeepsPublisherTextAndTheAuthorityThatReadIt()
    {
        var original = FactsFixtures.DateFact(
            rawQualifier: "applicable sous reserve de l'article 4",
            comment: "publisher note retained verbatim");

        var restored = ContractJson.Deserialize<PublisherDateFact>(ContractJson.Serialize(original));

        Assert.AreEqual("applicable sous reserve de l'article 4", restored.RawQualifier);
        Assert.AreEqual("publisher note retained verbatim", restored.PublisherComment);
        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, restored.SemanticRole);
        Assert.AreEqual("lex-lu-date-reader/1", restored.ParsedByAuthority);
        Assert.AreEqual(FactsFixtures.ConsolidatesPredicate, restored.SourcePredicateUri);
        Assert.AreEqual("axiom-7731", restored.Axiom.RemoteAxiomId);
        Assert.HasCount(2, restored.Axiom.Qualifiers);
    }

    [TestMethod]
    public void AnAbsentQualifierAndCommentStayAbsentRatherThanBecomingEmptyStrings()
    {
        var restored = ContractJson.Deserialize<PublisherDateFact>(
            ContractJson.Serialize(FactsFixtures.DateFact()));

        Assert.IsNull(restored.RawQualifier);
        Assert.IsNull(restored.PublisherComment);
    }

    /// <summary>
    /// Serialization emits every declared property. A field silently dropped on the wire would
    /// still round-trip if the reader defaulted it, so the JSON itself is inspected.
    /// </summary>
    [TestMethod]
    public void ThePublisherRelationWireDocumentCarriesEveryDeclaredMember()
    {
        using var document = JsonDocument.Parse(
            ContractJson.Serialize(FactsFixtures.PublisherRelation()));
        var root = document.RootElement;

        foreach (var name in new[]
                 {
                     "schema", "source", "target", "predicate_uri", "observation", "qualified_axioms",
                 })
        {
            Assert.IsTrue(root.TryGetProperty(name, out _), $"missing {name}");
        }

        var observation = root.GetProperty("observation");
        Assert.IsTrue(observation.TryGetProperty("observation_id", out _));
        Assert.IsTrue(observation.TryGetProperty("observed_at", out _));
        Assert.IsTrue(observation.TryGetProperty("transport_bytes", out var transport));
        Assert.IsTrue(transport.TryGetProperty("content_sha256", out _));
        Assert.IsTrue(transport.TryGetProperty("byte_length", out _));
    }

    /// <summary>
    /// No contract may hard-code a physical storage provider. The wire document is searched for
    /// the shapes a provider locator would take.
    /// </summary>
    [TestMethod]
    public void NoFactsDocumentCarriesAPhysicalStorageLocator()
    {
        foreach (var json in new[]
                 {
                     ContractJson.Serialize(FactsFixtures.PublisherRelation()),
                     ContractJson.Serialize(FactsFixtures.AssertedFact()),
                     ContractJson.Serialize(FactsFixtures.DateFact()),
                     ContractJson.Serialize(FactsFixtures.InboundView()),
                 })
        {
            foreach (var forbidden in new[]
                     {
                         "blob.core.windows.net", "s3://", "https://", "container", "account_name",
                         "bucket", "connection_string", "file://",
                     })
            {
                Assert.IsFalse(
                    json.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"a facts document carries the provider locator shape {forbidden}");
            }
        }
    }

    private static void AssertAxiomsAreIdentical(
        IReadOnlyList<QualifiedAxiom> expected,
        IReadOnlyList<QualifiedAxiom> actual)
    {
        Assert.HasCount(expected.Count, actual);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.AreEqual(expected[index].RemoteAxiomId, actual[index].RemoteAxiomId);
            Assert.HasCount(expected[index].Qualifiers.Count, actual[index].Qualifiers);
            for (var inner = 0; inner < expected[index].Qualifiers.Count; inner++)
            {
                Assert.AreEqual(
                    expected[index].Qualifiers[inner].PredicateUri,
                    actual[index].Qualifiers[inner].PredicateUri);
                Assert.AreEqual(
                    expected[index].Qualifiers[inner].RawValue,
                    actual[index].Qualifiers[inner].RawValue);
            }
        }
    }
}
