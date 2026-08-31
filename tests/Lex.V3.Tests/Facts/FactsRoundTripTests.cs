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
        Assert.IsTrue(restored.Source.SameIdentity(original.Source));
        Assert.IsTrue(restored.Target.SameIdentity(original.Target));
        Assert.AreEqual(original.Observation.ObservationId, restored.Observation.ObservationId);
        Assert.AreEqual(original.Observation.ObservedAt, restored.Observation.ObservedAt);

        AssertAxiomsAreIdentical(original.QualifiedAxioms, restored.QualifiedAxioms);
    }

    /// <summary>
    /// The whole point of the identity set: a case keeps its Cellar URI, its CELEX number and
    /// its ECLI together, in order, rather than losing two of the three.
    /// </summary>
    [TestMethod]
    public void ACaseKeepsItsCellarUriCelexAndEcliTogether()
    {
        var restored = ContractJson.Deserialize<RelationFact>(
            ContractJson.Serialize(FactsFixtures.CaseFactWithEcli()));

        var target = restored.CarriedTarget;
        Assert.HasCount(4, target.Identifiers);
        Assert.AreEqual(
            FactsFixtures.CellarWorkUri,
            target.Value(FactsIdentifierFamily.CellarWorkUri));
        Assert.AreEqual("62019CJ0311", target.Value(FactsIdentifierFamily.Celex));
        Assert.AreEqual("ECLI:EU:C:2020:1042", target.Value(FactsIdentifierFamily.Ecli));

        // A count plus three values let the fourth family survive as a number. The alias is a
        // distinct publisher fact, so the round trip has to name it and carry its exact value.
        Assert.AreEqual(
            FactsFixtures.CellarPsiUri,
            target.Value(FactsIdentifierFamily.CellarPsiUri));
        Assert.AreNotEqual(
            target.Value(FactsIdentifierFamily.CellarWorkUri),
            target.Value(FactsIdentifierFamily.CellarPsiUri));
        Assert.AreEqual(EcliState.EcliPresent, restored.TargetEcliState);
        Assert.AreEqual("ECLI:EU:C:2020:1042", restored.TargetEcli);
    }

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
    public void DerivedInverseKeepsItsAuthorizationEndpointsAndForwardAssertion()
    {
        var original = FactsFixtures.DerivedInverse();

        var restored = ContractJson.Deserialize<DerivedInverseRelation>(
            ContractJson.Serialize(original));

        Assert.AreEqual(FactsFixtures.JoluxOntology, restored.AuthorizingAxiom.OntologyUri);
        Assert.AreEqual(FactsFixtures.OntologyVersion, restored.AuthorizingAxiom.OntologyVersion);
        Assert.IsTrue(restored.AuthorizingAxiom.Authorizes(
            restored.InverseOfPredicateUri, restored.PredicateUri));
        Assert.AreEqual(FactsFixtures.ConsolidatesPredicate, restored.InverseOfPredicateUri);
        Assert.AreEqual(FactsFixtures.ConsolidatedByPredicate, restored.PredicateUri);
        Assert.IsTrue(restored.Source.SameIdentity(restored.DerivedFrom.Target));
        Assert.IsTrue(restored.Target.SameIdentity(restored.DerivedFrom.Source));
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
        Assert.IsTrue(restored.ContributingAssertions[0].Target.SameIdentity(restored.Target));
    }

    [TestMethod]
    public void RelationFactKeepsItsKindBodyScopeAndEcliState()
    {
        var restored = ContractJson.Deserialize<RelationFact>(
            ContractJson.Serialize(FactsFixtures.AssertedFact()));

        Assert.AreEqual(RelationAssertionKind.PublisherAsserted, restored.Kind);
        Assert.AreEqual(TargetBodyScope.BodyInScopeHeld, restored.TargetBodyScope);
        Assert.AreEqual(EcliState.EcliNotApplicable, restored.TargetEcliState);
        Assert.IsNull(restored.TargetEcli);
        Assert.IsNotNull(restored.PublisherAsserted);
        Assert.IsNull(restored.OntologyAuthorizedInverse);
        Assert.IsNull(restored.LocalInboundView);
    }

    /// <summary>
    /// The edge is kept, the missing ECLI is typed, and no identifier is invented, all three at
    /// once, because any one of them alone is satisfiable by dropping the edge.
    /// </summary>
    [TestMethod]
    public void ACaseRelationWithNoPublisherEcliKeepsTheEdgeAndInventsNothing()
    {
        var restored = ContractJson.Deserialize<RelationFact>(
            ContractJson.Serialize(FactsFixtures.CaseFactWithoutEcli()));

        Assert.AreEqual(EcliState.EcliNotInThisSet, restored.TargetEcliState);
        Assert.IsNull(restored.TargetEcli);
        Assert.IsNotNull(restored.PublisherAsserted);
        Assert.AreEqual("62019CJ0311", restored.CarriedTarget.Value(FactsIdentifierFamily.Celex));
        Assert.IsTrue(restored.CarriedTarget.IsCase);
        Assert.AreEqual(TargetBodyScope.BodyInScopeNotHeld, restored.TargetBodyScope);
    }

    [TestMethod]
    public void APublisherDateKeepsItsLexicalValueDatatypeAndPrecision()
    {
        var restored = ContractJson.Deserialize<PublisherDate>(
            ContractJson.Serialize(FactsFixtures.YearOnlyDate()));

        Assert.AreEqual("2019", restored.RawLexicalValue);
        Assert.AreEqual(PublisherDate.GYear, restored.DatatypeUri);
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
        Assert.AreEqual(FactsFixtures.Authority, restored.ParsedByAuthority);
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

    [TestMethod]
    public void ADriftReportSurvivesWithItsVocabularyAndAdmittedSet()
    {
        var restored = ContractJson.Deserialize<VocabularyDrift>(
            ContractJson.Serialize(FactsFixtures.Drift()));

        Assert.AreEqual(VocabularyKind.DateSemanticRole, restored.Vocabulary);
        Assert.AreEqual("ratification_date", restored.ObservedTerm);
        CollectionAssert.AreEqual(
            ClosedVocabulary.WireNames<DateSemanticRole>(),
            restored.AdmittedTerms.ToArray());
    }

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

        var target = root.GetProperty("target");
        Assert.IsTrue(target.TryGetProperty("publisher", out _));
        Assert.IsTrue(target.TryGetProperty("identifiers", out _));
    }

    /// <summary>
    /// No contract may hard-code a physical storage provider. Cellar identifiers are publisher
    /// URIs, which is the one legitimate reason a document carries an https scheme, so the check
    /// searches the provider-locator shapes and treats publisher URIs separately.
    /// </summary>
    [TestMethod]
    public void NoFactsDocumentCarriesAPhysicalStorageLocator()
    {
        foreach (var json in new[]
                 {
                     ContractJson.Serialize(FactsFixtures.PublisherRelation()),
                     ContractJson.Serialize(FactsFixtures.AssertedFact()),
                     ContractJson.Serialize(FactsFixtures.CaseFactWithEcli()),
                     ContractJson.Serialize(FactsFixtures.DateFact()),
                     ContractJson.Serialize(FactsFixtures.InboundView()),
                 })
        {
            foreach (var forbidden in new[]
                     {
                         "blob.core.windows.net", "s3://", "container", "account_name",
                         "bucket", "connection_string", "file://", "azure",
                     })
            {
                Assert.IsFalse(
                    json.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"a facts document carries the provider locator shape {forbidden}");
            }
        }
    }

    /// <summary>
    /// Facts carry exactly one custody coordinate, <c>source_observation_id</c>, and reach the
    /// durable bytes transitively through it. An earlier candidate embedded a second byte
    /// reference here and my declaration said it had been removed when it had not. No test named
    /// the rule, so 106 green tests said nothing about it. This one names it.
    /// </summary>
    [TestMethod]
    public void NoFactMemberNamesAStorageCoordinate()
    {
        string[] forbidden =
        [
            "transport_bytes", "content_sha256", "byte_length", "blob_ref", "blob_id",
            "container", "storage_account", "account", "bucket", "region", "endpoint",
            "locator", "url", "file_path", "path",
        ];

        string[] documents =
        [
            ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            ContractJson.Serialize(FactsFixtures.DerivedInverse()),
            ContractJson.Serialize(FactsFixtures.InboundView()),
            ContractJson.Serialize(FactsFixtures.AssertedFact()),
            ContractJson.Serialize(FactsFixtures.CaseFactWithEcli()),
            ContractJson.Serialize(FactsFixtures.Drift()),
            ContractJson.Serialize(FactsFixtures.OpenEndedDate()),
        ];

        foreach (var text in documents)
        {
            using var document = JsonDocument.Parse(text);
            foreach (var name in MemberNames(document.RootElement))
            {
                Assert.IsFalse(
                    forbidden.Contains(name, StringComparer.Ordinal),
                    $"a fact member is named {name}, which is a storage coordinate");
            }
        }
    }

    private static IEnumerable<string> MemberNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in MemberNames(property.Value))
                        yield return nested;
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in MemberNames(item))
                        yield return nested;

                break;
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
