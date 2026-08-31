using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// Hostile fixtures and mutation receipts.
/// </summary>
/// <remarks>
/// Each test here names the specific loss it prevents. A mutation receipt is a test whose failure
/// is the evidence: dropping provenance, collapsing a multimap, inventing an inverse, inferring a
/// date role, or defaulting an unknown vocabulary each break a named test below.
/// </remarks>
[TestClass]
public sealed class FactsHostileTests
{
    // ---- unsupported vocabulary, and the refusal to default it ----------------------------

    /// <summary>
    /// MUTATION RECEIPT: defaulting an unknown vocabulary term. If the enum gained an
    /// <c>Unknown</c> member, or the converter fell back to a default, this test fails.
    /// </summary>
    [TestMethod]
    public void AnUnknownEnumWireValueFailsInsteadOfDeserializingToADefault()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.AssertedFact()),
            root => root["kind"] = "publisher_asserted_v2");

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<RelationFact>(document));
    }

    [TestMethod]
    public void AnUnknownDateRoleFailsRatherThanFallingBackToDocumentDate()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.DateFact()),
            root => root["semantic_role"] = "signature_date");

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<PublisherDateFact>(document));
    }

    [TestMethod]
    public void AnUnknownOpenSentinelFailsRatherThanBecomingNotOpen()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.OpenEndedDate()),
            root => root["open_sentinel"] = "indefinite");

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<PublisherDate>(document));
    }

    /// <summary>
    /// The closed vocabulary reader hands back a drift report and no value. There is no overload
    /// that returns a fallback, so an unsupported term cannot enter the graph at all.
    /// </summary>
    [TestMethod]
    public void AnUnsupportedTermYieldsAClosedDriftReportAndNoValue()
    {
        var read = ClosedVocabulary.TryRead<DateSemanticRole>(
            "signature_date",
            VocabularyKind.DateSemanticRole,
            FactsFixtures.Observation(),
            out var value,
            out var drift);

        Assert.IsFalse(read);
        Assert.IsNull(value);
        Assert.IsNotNull(drift);
        Assert.AreEqual(VocabularyKind.DateSemanticRole, drift.Vocabulary);
        Assert.AreEqual("signature_date", drift.ObservedTerm);
        Assert.Contains("document_date", drift.AdmittedTerms);
        Assert.AreEqual(
            FactsFixtures.TransportDigest,
            drift.Observation.TransportBytes.ContentSha256);
    }

    [TestMethod]
    public void AKnownTermIsReadWithoutProducingDrift()
    {
        var read = ClosedVocabulary.TryRead<DateSemanticRole>(
            "entry_into_force",
            VocabularyKind.DateSemanticRole,
            FactsFixtures.Observation(),
            out var value,
            out var drift);

        Assert.IsTrue(read);
        Assert.IsNull(drift);
        Assert.AreEqual(DateSemanticRole.EntryIntoForce, value);
    }

    [TestMethod]
    public void ATermInsideTheAdmittedSetCannotBeReportedAsDrift()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new VocabularyDrift(
            FactsSchemaIds.VocabularyDrift,
            VocabularyKind.DateSemanticRole,
            "document_date",
            ["document_date", "publication_date"],
            FactsFixtures.Observation()));
    }

    // ---- unknown predicates and unmapped members -------------------------------------------

    [TestMethod]
    public void AnUnknownMemberIsRefusedRatherThanIgnored()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["predicate_label"] = "consolidates");

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<PublisherRelation>(document));
    }

    [TestMethod]
    public void ADuplicateJsonMemberIsRefused()
    {
        var json = ContractJson.Serialize(FactsFixtures.PublisherRelation());
        var duplicated = json.Insert(1, "\"schema\":\"lex-v3-publisher-relation/1\",");

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<PublisherRelation>(duplicated));
    }

    [TestMethod]
    public void APredicateThatIsNotAnAbsoluteUriIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherRelation(
            FactsSchemaIds.PublisherRelation,
            FactsFixtures.LuWork(),
            FactsFixtures.LuTarget(),
            "consolidates",
            FactsFixtures.Observation(),
            FactsFixtures.MultimapAxioms()));
    }

    // ---- provenance ------------------------------------------------------------------------

    /// <summary>
    /// MUTATION RECEIPT: dropping provenance. Removing the observation from the wire document
    /// must fail rather than yielding a fact with no evidence behind it.
    /// </summary>
    [TestMethod]
    public void AFactWithItsObservationRemovedIsRefused()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root.Remove("observation"));

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<PublisherRelation>(document));
    }

    [TestMethod]
    public void AnObservationWithItsTransportBytesRemovedIsRefused()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["observation"]!.AsObject().Remove("transport_bytes"));

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<PublisherRelation>(document));
    }

    [TestMethod]
    public void ATransportDigestThatIsNotALowercaseSha256IsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new TransportByteReference(FactsFixtures.TransportDigest.ToUpperInvariant(), 1));
        Assert.ThrowsExactly<ArgumentException>(
            () => new TransportByteReference("abc", 1));
    }

    [TestMethod]
    public void AnObservationTimestampOutsideUtcIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SourceObservationReference(
            "obs-1",
            new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.FromHours(2)),
            FactsFixtures.TransportBytes()));
    }

    // ---- multimaps -------------------------------------------------------------------------

    /// <summary>
    /// MUTATION RECEIPT: collapsing a multimap. Reducing the two axioms that share a remote
    /// identifier to one changes the document, and this test states the count and the members.
    /// </summary>
    [TestMethod]
    public void CollapsingTwoAxiomsSharingARemoteIdentifierChangesTheFact()
    {
        var collapsed = Mutate(
            ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root =>
            {
                var axioms = root["qualified_axioms"]!.AsArray();
                axioms.RemoveAt(1);
            });

        var restored = ContractJson.Deserialize<PublisherRelation>(collapsed);

        Assert.HasCount(1, restored.QualifiedAxioms);
        Assert.AreNotEqual(
            FactsFixtures.PublisherRelation().QualifiedAxioms.Count,
            restored.QualifiedAxioms.Count,
            "a collapsed multimap must not compare equal to the original");
    }

    [TestMethod]
    public void DuplicateRemoteAxiomIdentifiersAreAcceptedRatherThanRejected()
    {
        var relation = FactsFixtures.PublisherRelation();

        Assert.HasCount(2, relation.QualifiedAxioms);
        Assert.AreEqual(
            relation.QualifiedAxioms[0].RemoteAxiomId,
            relation.QualifiedAxioms[1].RemoteAxiomId);
    }

    // ---- inverses --------------------------------------------------------------------------

    /// <summary>
    /// MUTATION RECEIPT: inventing an inverse. An inverse whose inverted predicate does not match
    /// the assertion it claims to derive from is refused, so an edge cannot be reversed against
    /// an unrelated forward fact.
    /// </summary>
    [TestMethod]
    public void AnInverseThatDoesNotMatchItsForwardAssertionIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new DerivedInverseRelation(
            FactsSchemaIds.DerivedInverseRelation,
            FactsFixtures.LuTarget(),
            FactsFixtures.LuWork(),
            FactsFixtures.ConsolidatedByPredicate,
            "http://example.invalid/unrelated",
            FactsFixtures.InverseOfStatement,
            FactsFixtures.PublisherRelation()));
    }

    [TestMethod]
    public void AnInverseWithoutAnAuthorizingOntologyStatementCannotBeBuilt()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new DerivedInverseRelation(
            FactsSchemaIds.DerivedInverseRelation,
            FactsFixtures.LuTarget(),
            FactsFixtures.LuWork(),
            FactsFixtures.ConsolidatedByPredicate,
            FactsFixtures.ConsolidatesPredicate,
            "not-a-uri",
            FactsFixtures.PublisherRelation()));
    }

    [TestMethod]
    public void AnInverseWithItsAuthorizationRemovedFromTheWireIsRefused()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.DerivedInverse()),
            root => root.Remove("authorizing_ontology_statement_uri"));

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<DerivedInverseRelation>(document));
    }

    /// <summary>
    /// A locally derived inbound view relabelled as a publisher assertion is refused, so the
    /// weakest edge cannot be promoted to the strongest claim by editing one string.
    /// </summary>
    [TestMethod]
    public void ALocalViewDeclaredAsAPublisherAssertionIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeHeld,
            EcliState.EcliPresent,
            "ECLI:EU:C:2020:1042",
            null,
            null,
            FactsFixtures.InboundView()));
    }

    [TestMethod]
    public void ARelationFactCarryingTwoEdgeShapesIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeHeld,
            EcliState.EcliMissing,
            null,
            FactsFixtures.PublisherRelation(),
            FactsFixtures.DerivedInverse(),
            null));
    }

    [TestMethod]
    public void ARelationFactCarryingNoEdgeShapeIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeHeld,
            EcliState.EcliMissing,
            null,
            null,
            null,
            null));
    }

    [TestMethod]
    public void AnInboundViewWhoseContributingEdgeCarriesAnotherPredicateIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new LocalInboundView(
            FactsSchemaIds.LocalInboundView,
            FactsFixtures.LuTarget(),
            FactsFixtures.ConsolidatedByPredicate,
            false,
            FactsFixtures.ScopeDigest,
            [FactsFixtures.PublisherRelation()]));
    }

    // ---- ECLI ------------------------------------------------------------------------------

    /// <summary>
    /// MUTATION RECEIPT: inventing an identifier. An edge that states the publisher served no
    /// ECLI cannot also carry one.
    /// </summary>
    [TestMethod]
    public void AnEcliMissingEdgeCarryingAnEcliIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeNotHeld,
            EcliState.EcliMissing,
            "ECLI:EU:C:2020:1042",
            FactsFixtures.PublisherRelation(),
            null,
            null));
    }

    [TestMethod]
    public void AnEcliPresentEdgeWithoutAnEcliIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeHeld,
            EcliState.EcliPresent,
            null,
            FactsFixtures.PublisherRelation(),
            null,
            null));
    }

    // ---- dates -----------------------------------------------------------------------------

    /// <summary>
    /// MUTATION RECEIPT: reading a year-precision literal at day precision. The declared
    /// precision must be the precision present in the lexical value.
    /// </summary>
    [TestMethod]
    public void AYearLiteralDeclaringDayPrecisionIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "2019",
            "http://www.w3.org/2001/XMLSchema#gYear",
            DatePrecision.YearMonthDay,
            DateOpenSentinel.NotOpen));
    }

    [TestMethod]
    public void ADayLiteralDeclaringYearPrecisionIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "2019-07-15",
            "http://www.w3.org/2001/XMLSchema#date",
            DatePrecision.Year,
            DateOpenSentinel.NotOpen));
    }

    [TestMethod]
    public void EachPrecisionIsAcceptedAtItsOwnLexicalForm()
    {
        foreach (var (value, precision) in new[]
                 {
                     ("2019", DatePrecision.Year),
                     ("2019-07", DatePrecision.YearMonth),
                     ("2019-07-15", DatePrecision.YearMonthDay),
                 })
        {
            var date = new PublisherDate(
                FactsSchemaIds.PublisherDate,
                value,
                "http://www.w3.org/2001/XMLSchema#date",
                precision,
                DateOpenSentinel.NotOpen);
            Assert.AreEqual(precision, date.Precision);
        }
    }

    [TestMethod]
    public void ALexicalValueThatIsNotADateFormIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "circa 2019",
            "http://www.w3.org/2001/XMLSchema#date",
            DatePrecision.Year,
            DateOpenSentinel.NotOpen));
    }

    /// <summary>
    /// MUTATION RECEIPT: inferring a date role from order. Two date facts differing only in the
    /// order they were observed must keep the role the publisher stated, which here is that no
    /// role was stated at all.
    /// </summary>
    [TestMethod]
    public void ADateWithNoPublisherRoleKeepsRoleNotStatedRatherThanTakingOneFromPosition()
    {
        var first = FactsFixtures.DateFact();
        var second = FactsFixtures.DateFact(FactsFixtures.OpenEndedDate());

        foreach (var fact in new[] { first, second })
        {
            var restored = ContractJson.Deserialize<PublisherDateFact>(ContractJson.Serialize(fact));
            Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, restored.SemanticRole);
        }
    }

    [TestMethod]
    public void ADateFactWithoutItsParsingAuthorityIsRefused()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.DateFact()),
            root => root.Remove("parsed_by_authority"));

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<PublisherDateFact>(document));
    }

    // ---- body scope ------------------------------------------------------------------------

    [TestMethod]
    public void ATargetOutsideBodyScopeKeepsItsEdgeAndItsOfficialIdentity()
    {
        var fact = new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyOutsideScope,
            EcliState.EcliMissing,
            null,
            FactsFixtures.PublisherRelation(),
            null,
            null);

        var restored = ContractJson.Deserialize<RelationFact>(ContractJson.Serialize(fact));

        Assert.AreEqual(TargetBodyScope.BodyOutsideScope, restored.TargetBodyScope);
        Assert.IsNotNull(restored.PublisherAsserted);
        Assert.AreEqual(
            FactsFixtures.LuTarget().RawValue,
            restored.PublisherAsserted.Target.RawValue);
    }

    [TestMethod]
    public void EveryBodyScopeStateSurvivesTheWire()
    {
        foreach (var scope in Enum.GetValues<TargetBodyScope>())
        {
            var fact = new RelationFact(
                FactsSchemaIds.RelationFact,
                RelationAssertionKind.PublisherAsserted,
                scope,
                EcliState.EcliMissing,
                null,
                FactsFixtures.PublisherRelation(),
                null,
                null);

            var restored = ContractJson.Deserialize<RelationFact>(ContractJson.Serialize(fact));
            Assert.AreEqual(scope, restored.TargetBodyScope);
        }
    }

    // ---- schema identity -------------------------------------------------------------------

    [TestMethod]
    public void AWrongSchemaIdentityIsRefusedOnEveryContract()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherRelation(
            "lex-v3-publisher-relation/2",
            FactsFixtures.LuWork(),
            FactsFixtures.LuTarget(),
            FactsFixtures.ConsolidatesPredicate,
            FactsFixtures.Observation(),
            FactsFixtures.MultimapAxioms()));

        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            "lex-v3-publisher-date/2",
            "2019",
            "http://www.w3.org/2001/XMLSchema#gYear",
            DatePrecision.Year,
            DateOpenSentinel.NotOpen));
    }

    private static string Mutate(string json, Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        mutate(root);
        return root.ToJsonString();
    }
}
