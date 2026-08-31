using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// Hostile fixtures and mutation receipts, one per named invariant.
/// </summary>
/// <remarks>
/// Each test names the specific loss it prevents. Where a test replaces a hole Codex proved by
/// isolated probe, it says so, because the value of the test is that the probe now fails.
/// </remarks>
[TestClass]
public sealed class FactsHostileTests
{
    // ---- O1: identity is a set, and a lossy one is refused ---------------------------------

    [TestMethod]
    public void AnEmptyIdentitySetIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new OfficialIdentitySet(PublisherId.EuEurLex, []));
    }

    [TestMethod]
    public void AnIdentitySetRepeatingOneFamilyIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [
                new OfficialIdentifier(FactsIdentifierFamily.Celex, "62019CJ0311"),
                new OfficialIdentifier(FactsIdentifierFamily.Celex, "62019CJ0312"),
            ]));
    }

    [TestMethod]
    public void ACellarUriFamilyCarryingSomethingThatIsNotAUriIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, "62019CJ0311"));
    }

    /// <summary>
    /// Mutating the identity set is not possible through the exposed list: it is copied on the
    /// way in, so a caller holding the original array cannot change a fact after the fact.
    /// </summary>
    [TestMethod]
    public void TheIdentitySetIsDefensivelyCopied()
    {
        var identifiers = new List<OfficialIdentifier>
        {
            new(FactsIdentifierFamily.Celex, "62019CJ0311"),
        };
        var set = new OfficialIdentitySet(PublisherId.EuEurLex, identifiers);

        identifiers.Add(new OfficialIdentifier(FactsIdentifierFamily.Ecli, "ECLI:EU:C:2020:1042"));

        Assert.HasCount(1, set.Identifiers);
        Assert.IsNull(set.Value(FactsIdentifierFamily.Ecli));
    }

    // ---- O2: endpoints are bound ------------------------------------------------------------

    /// <summary>
    /// MUTATION RECEIPT: inventing an inverse. Codex's probe built an inverse whose endpoints
    /// were unrelated to the forward assertion it named, and Candidate 1 accepted it.
    /// </summary>
    [TestMethod]
    public void AnInverseWhoseSourceIsNotTheForwardTargetIsRefused()
    {
        var forward = FactsFixtures.PublisherRelation();

        Assert.ThrowsExactly<ArgumentException>(() => new DerivedInverseRelation(
            FactsSchemaIds.DerivedInverseRelation,
            FactsFixtures.EuCaseWithEcli(),
            forward.Source,
            FactsFixtures.ConsolidatedByPredicate,
            FactsFixtures.ConsolidatesPredicate,
            FactsFixtures.InverseOfStatement,
            forward));
    }

    [TestMethod]
    public void AnInverseWhoseTargetIsNotTheForwardSourceIsRefused()
    {
        var forward = FactsFixtures.PublisherRelation();

        Assert.ThrowsExactly<ArgumentException>(() => new DerivedInverseRelation(
            FactsSchemaIds.DerivedInverseRelation,
            forward.Target,
            FactsFixtures.EuCaseWithEcli(),
            FactsFixtures.ConsolidatedByPredicate,
            FactsFixtures.ConsolidatesPredicate,
            FactsFixtures.InverseOfStatement,
            forward));
    }

    [TestMethod]
    public void AnInverseThatDoesNotMatchItsForwardPredicateIsRefused()
    {
        var forward = FactsFixtures.PublisherRelation();

        Assert.ThrowsExactly<ArgumentException>(() => new DerivedInverseRelation(
            FactsSchemaIds.DerivedInverseRelation,
            forward.Target,
            forward.Source,
            FactsFixtures.ConsolidatedByPredicate,
            "http://example.invalid/unrelated",
            FactsFixtures.InverseOfStatement,
            forward));
    }

    [TestMethod]
    public void AnInverseWithoutAnAuthorizingOntologyStatementCannotBeBuilt()
    {
        var forward = FactsFixtures.PublisherRelation();

        Assert.ThrowsExactly<ArgumentException>(() => new DerivedInverseRelation(
            FactsSchemaIds.DerivedInverseRelation,
            forward.Target,
            forward.Source,
            FactsFixtures.ConsolidatedByPredicate,
            FactsFixtures.ConsolidatesPredicate,
            "not-a-uri",
            forward));
    }

    /// <summary>
    /// Every endpoint binding must also hold on deserialization, not only construction, since a
    /// document never passes through the fluent path.
    /// </summary>
    [TestMethod]
    public void AnInverseWithSwappedEndpointsIsRefusedOnTheWireToo()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.DerivedInverse()),
            root =>
            {
                var source = root["source"]!.DeepClone();
                root["source"] = root["target"]!.DeepClone();
                root["target"] = source;
            });

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<DerivedInverseRelation>(document));
    }

    /// <summary>
    /// MUTATION RECEIPT: a fabricated inbound count. Codex's probe attached a contributor that
    /// pointed somewhere else entirely, and Candidate 1 accepted it.
    /// </summary>
    [TestMethod]
    public void AnInboundViewWhoseContributorTargetsSomethingElseIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new LocalInboundView(
            FactsSchemaIds.LocalInboundView,
            FactsFixtures.EuCaseWithEcli(),
            FactsFixtures.ConsolidatesPredicate,
            false,
            FactsFixtures.ScopeDigest,
            [FactsFixtures.PublisherRelation()]));
    }

    [TestMethod]
    public void AnInboundViewWhoseContributorCarriesAnotherPredicateIsRefused()
    {
        var contributor = FactsFixtures.PublisherRelation();

        Assert.ThrowsExactly<ArgumentException>(() => new LocalInboundView(
            FactsSchemaIds.LocalInboundView,
            contributor.Target,
            FactsFixtures.ConsolidatedByPredicate,
            false,
            FactsFixtures.ScopeDigest,
            [contributor]));
    }

    [TestMethod]
    public void AnInboundViewWithAForeignContributorIsRefusedOnTheWireToo()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.InboundView()),
            root =>
            {
                var foreign = JsonNode.Parse(
                    ContractJson.Serialize(
                        FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())))!;
                root["contributing_assertions"] = new JsonArray(foreign);
            });

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LocalInboundView>(document));
    }

    // ---- O3: dates are bound to their datatype, calendar and sentinel -----------------------

    [TestMethod]
    public void AnImpossibleCalendarDayIsRefused()
    {
        foreach (var value in new[] { "2019-02-30", "2019-04-31", "2019-13-01", "2019-00-10" })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new PublisherDate(
                    FactsSchemaIds.PublisherDate,
                    value,
                    PublisherDate.Date,
                    DatePrecision.YearMonthDay,
                    DateOpenSentinel.NotOpen),
                value);
        }
    }

    [TestMethod]
    public void ALeapDayIsAcceptedInALeapYearAndRefusedOtherwise()
    {
        var leap = new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "2020-02-29",
            PublisherDate.Date,
            DatePrecision.YearMonthDay,
            DateOpenSentinel.NotOpen);
        Assert.AreEqual("2020-02-29", leap.RawLexicalValue);

        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "2019-02-29",
            PublisherDate.Date,
            DatePrecision.YearMonthDay,
            DateOpenSentinel.NotOpen));
    }

    [TestMethod]
    public void ADatatypeThatDisagreesWithTheDeclaredPrecisionIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "2019",
            PublisherDate.Date,
            DatePrecision.Year,
            DateOpenSentinel.NotOpen));

        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "2019-07-15",
            PublisherDate.GYear,
            DatePrecision.YearMonthDay,
            DateOpenSentinel.NotOpen));
    }

    [TestMethod]
    public void AnUnknownDatatypeIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "2019-07-15",
            "http://www.w3.org/2001/XMLSchema#dateTime",
            DatePrecision.YearMonthDay,
            DateOpenSentinel.NotOpen));
    }

    /// <summary>
    /// MUTATION RECEIPT: an arbitrary date labelled open-ended. Only the one documented sentinel
    /// may carry that state, otherwise 1970-01-01 could be read as "still in force".
    /// </summary>
    [TestMethod]
    public void AnArbitraryDateLabelledOpenEndedIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "1970-01-01",
            PublisherDate.Date,
            DatePrecision.YearMonthDay,
            DateOpenSentinel.OpenEnded));
    }

    [TestMethod]
    public void EachPrecisionIsAcceptedAtItsOwnDatatypeAndLexicalForm()
    {
        foreach (var (value, datatype, precision) in new[]
                 {
                     ("2019", PublisherDate.GYear, DatePrecision.Year),
                     ("2019-07", PublisherDate.GYearMonth, DatePrecision.YearMonth),
                     ("2019-07-15", PublisherDate.Date, DatePrecision.YearMonthDay),
                 })
        {
            var date = new PublisherDate(
                FactsSchemaIds.PublisherDate,
                value,
                datatype,
                precision,
                DateOpenSentinel.NotOpen);
            Assert.AreEqual(precision, date.Precision);
        }
    }

    [TestMethod]
    public void AnUndefinedEnumValuePassedDirectlyIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            "2019",
            PublisherDate.GYear,
            (DatePrecision)42,
            DateOpenSentinel.NotOpen));

        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            (TargetBodyScope)99,
            EcliState.EcliNotApplicable,
            FactsFixtures.PublisherRelation(),
            null,
            null));
    }

    /// <summary>
    /// An open end says validity has no end. Attaching it to a document date would be a claim
    /// the publisher never made.
    /// </summary>
    [TestMethod]
    public void AnOpenEndedDateCarryingAnIncompatibleRoleIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => FactsFixtures.DateFact(
                FactsFixtures.OpenEndedDate(),
                DateSemanticRole.DocumentDate));

        var allowed = FactsFixtures.DateFact(
            FactsFixtures.OpenEndedDate(),
            DateSemanticRole.EndOfValidity);
        Assert.AreEqual(DateSemanticRole.EndOfValidity, allowed.SemanticRole);
    }

    [TestMethod]
    public void AParsingAuthorityThatIsNotAnAbsoluteUriIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDateFact(
            FactsSchemaIds.PublisherDateFact,
            FactsFixtures.LuWork(),
            FactsFixtures.YearOnlyDate(),
            FactsFixtures.ConsolidatesPredicate,
            FactsFixtures.MultimapAxioms()[0],
            null,
            null,
            DateSemanticRole.RoleNotStatedByPublisher,
            "lex-lu-date-reader/1",
            FactsFixtures.Observation()));
    }

    /// <summary>
    /// MUTATION RECEIPT: inferring a date role from order.
    /// </summary>
    [TestMethod]
    public void ADateWithNoPublisherRoleKeepsRoleNotStatedRatherThanTakingOneFromPosition()
    {
        foreach (var fact in new[]
                 {
                     FactsFixtures.DateFact(),
                     FactsFixtures.DateFact(FactsFixtures.DayDate()),
                 })
        {
            var restored = ContractJson.Deserialize<PublisherDateFact>(ContractJson.Serialize(fact));
            Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, restored.SemanticRole);
        }
    }

    // ---- O4: a drift report cannot name the wrong vocabulary --------------------------------

    /// <summary>
    /// MUTATION RECEIPT: mislabelled drift. Codex's probe read a date role while the report
    /// claimed the vocabulary was relation predicates. The kind is now derived from the enum, so
    /// the caller cannot choose it at all.
    /// </summary>
    [TestMethod]
    public void ADriftReportNamesTheVocabularyItActuallyConsulted()
    {
        var read = ClosedVocabulary.TryRead<DateSemanticRole>(
            "signature_date",
            FactsFixtures.Observation(),
            out var value,
            out var drift);

        Assert.IsFalse(read);
        Assert.IsNull(value);
        Assert.IsNotNull(drift);
        Assert.AreEqual(VocabularyKind.DateSemanticRole, drift.Vocabulary);
        CollectionAssert.AreEqual(
            ClosedVocabulary.WireNames<DateSemanticRole>(),
            drift.AdmittedTerms.ToArray());
    }

    [TestMethod]
    public void ADriftReportWhoseAdmittedSetIsNotTheNamedVocabularyIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new VocabularyDrift(
            FactsSchemaIds.VocabularyDrift,
            VocabularyKind.RelationAssertionKind,
            "signature_date",
            ClosedVocabulary.WireNames<DateSemanticRole>(),
            FactsFixtures.Observation()));
    }

    [TestMethod]
    public void ADriftReportWithARepeatedAdmittedTermIsRefused()
    {
        var duplicated = ClosedVocabulary.WireNames<DatePrecision>().ToList();
        duplicated.Add(duplicated[0]);

        Assert.ThrowsExactly<ArgumentException>(() => new VocabularyDrift(
            FactsSchemaIds.VocabularyDrift,
            VocabularyKind.DatePrecision,
            "century",
            duplicated,
            FactsFixtures.Observation()));
    }

    [TestMethod]
    public void ATermInsideTheAdmittedSetCannotBeReportedAsDrift()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new VocabularyDrift(
            FactsSchemaIds.VocabularyDrift,
            VocabularyKind.DatePrecision,
            "year",
            ClosedVocabulary.WireNames<DatePrecision>(),
            FactsFixtures.Observation()));
    }

    [TestMethod]
    public void AnEnumThatIsNotAFactsVocabularyCannotBeReadAtAll()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ClosedVocabulary.TryRead<DayOfWeek>(
            "Monday",
            FactsFixtures.Observation(),
            out _,
            out _));
    }

    [TestMethod]
    public void EveryVocabularyKindResolvesToItsOwnAdmittedSet()
    {
        foreach (var kind in Enum.GetValues<VocabularyKind>())
        {
            var terms = ClosedVocabulary.AdmittedTermsFor(kind);
            Assert.IsGreaterThan(0, terms.Length, kind.ToString());
            Assert.AreEqual(terms.Length, terms.Distinct(StringComparer.Ordinal).Count());
        }

        Assert.AreEqual(
            Enum.GetValues<VocabularyKind>().Length,
            Lex.V3.Contracts.Facts.FactsVocabularies.AllKinds.Count);
    }

    [TestMethod]
    public void AKnownTermIsReadWithoutProducingDrift()
    {
        var read = ClosedVocabulary.TryRead<DateSemanticRole>(
            "entry_into_force",
            FactsFixtures.Observation(),
            out var value,
            out var drift);

        Assert.IsTrue(read);
        Assert.IsNull(drift);
        Assert.AreEqual(DateSemanticRole.EntryIntoForce, value);
    }

    // ---- O5: ECLI belongs to the target identity set -----------------------------------------

    /// <summary>
    /// MUTATION RECEIPT: an invented identifier. Codex attached an unrelated ECLI to a LU ELI
    /// target and Candidate 1 accepted it, because the ECLI was a loose string belonging to
    /// nothing.
    /// </summary>
    [TestMethod]
    public void AnEcliCannotBeAttachedToATargetWhoseIdentitySetDoesNotCarryIt()
    {
        // The only way to claim an ECLI now is to put it in the identity set, and doing so on a
        // LU statute makes it a case by its own evidence, which is the honest consequence.
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeHeld,
            EcliState.EcliPresent,
            FactsFixtures.PublisherRelation(),
            null,
            null));
    }

    [TestMethod]
    public void AnEcliMissingStateOnANonCaseTargetIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeHeld,
            EcliState.EcliMissing,
            FactsFixtures.PublisherRelation(),
            null,
            null));
    }

    [TestMethod]
    public void ANotApplicableStateOnACaseTargetIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeNotHeld,
            EcliState.EcliNotApplicable,
            FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithoutEcli()),
            null,
            null));
    }

    [TestMethod]
    public void AnEcliPresentStateWithNoEcliInTheSetIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeNotHeld,
            EcliState.EcliPresent,
            FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithoutEcli()),
            null,
            null));
    }

    [TestMethod]
    public void ACaseIsRecognisedFromItsIdentifiersRatherThanDeclared()
    {
        Assert.IsTrue(FactsFixtures.EuCaseWithEcli().IsCase, "an ECLI proves a case");
        Assert.IsTrue(FactsFixtures.EuCaseWithoutEcli().IsCase, "a CELEX sector 6 number is a case");
        Assert.IsFalse(FactsFixtures.LuWork().IsCase, "a LU statute is not a case");
    }

    // ---- shape and identity ------------------------------------------------------------------

    [TestMethod]
    public void ALocalViewDeclaredAsAPublisherAssertionIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeHeld,
            EcliState.EcliNotApplicable,
            null,
            null,
            FactsFixtures.InboundView()));
    }

    [TestMethod]
    public void ARelationFactCarryingTwoOrZeroEdgeShapesIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeHeld,
            EcliState.EcliNotApplicable,
            FactsFixtures.PublisherRelation(),
            FactsFixtures.DerivedInverse(),
            null));

        Assert.ThrowsExactly<ArgumentException>(() => new RelationFact(
            FactsSchemaIds.RelationFact,
            RelationAssertionKind.PublisherAsserted,
            TargetBodyScope.BodyInScopeHeld,
            EcliState.EcliNotApplicable,
            null,
            null,
            null));
    }

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
            PublisherDate.GYear,
            DatePrecision.Year,
            DateOpenSentinel.NotOpen));
    }

    // ---- provenance, vocabulary and members ---------------------------------------------------

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

    /// <summary>MUTATION RECEIPT: dropping provenance.</summary>
    [TestMethod]
    public void AFactWithItsObservationOrTransportBytesRemovedIsRefused()
    {
        foreach (var mutate in new Action<JsonObject>[]
                 {
                     root => root.Remove("observation"),
                     root => root["observation"]!.AsObject().Remove("transport_bytes"),
                 })
        {
            var document = Mutate(ContractJson.Serialize(FactsFixtures.PublisherRelation()), mutate);
            Assert.ThrowsExactly<JsonException>(
                () => ContractJson.Deserialize<PublisherRelation>(document));
        }
    }

    [TestMethod]
    public void ATransportDigestThatIsNotALowercaseSha256IsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new TransportByteReference(FactsFixtures.TransportDigest.ToUpperInvariant(), 1));
        Assert.ThrowsExactly<ArgumentException>(() => new TransportByteReference("abc", 1));
    }

    [TestMethod]
    public void ANegativeTransportLengthIsRefused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new TransportByteReference(FactsFixtures.TransportDigest, -1));
    }

    [TestMethod]
    public void AnObservationTimestampOutsideUtcIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SourceObservationReference(
            "obs-1",
            new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.FromHours(2)),
            FactsFixtures.TransportBytes()));
    }

    /// <summary>MUTATION RECEIPT: collapsing a multimap.</summary>
    [TestMethod]
    public void CollapsingTwoAxiomsSharingARemoteIdentifierChangesTheFact()
    {
        var collapsed = Mutate(
            ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["qualified_axioms"]!.AsArray().RemoveAt(1));

        var restored = ContractJson.Deserialize<PublisherRelation>(collapsed);

        Assert.HasCount(1, restored.QualifiedAxioms);
        Assert.AreNotEqual(
            FactsFixtures.PublisherRelation().QualifiedAxioms.Count,
            restored.QualifiedAxioms.Count,
            "a collapsed multimap must not compare equal to the original");
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

    [TestMethod]
    public void EveryBodyScopeStateSurvivesTheWire()
    {
        foreach (var scope in Enum.GetValues<TargetBodyScope>())
        {
            var fact = new RelationFact(
                FactsSchemaIds.RelationFact,
                RelationAssertionKind.PublisherAsserted,
                scope,
                EcliState.EcliNotApplicable,
                FactsFixtures.PublisherRelation(),
                null,
                null);

            var restored = ContractJson.Deserialize<RelationFact>(ContractJson.Serialize(fact));
            Assert.AreEqual(scope, restored.TargetBodyScope);
        }
    }

    private static string Mutate(string json, Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        mutate(root);
        return root.ToJsonString();
    }
}
