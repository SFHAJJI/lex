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
        var set = new OfficialIdentitySet(
            PublisherId.EuEurLex,
            identifiers);

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
            FactsFixtures.InverseAxiom(),
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
            FactsFixtures.InverseAxiom(),
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
            FactsFixtures.InverseAxiom(),
            forward));
    }

    [TestMethod]
    public void AnInverseAuthorizedByAnAxiomForAnotherPredicatePairIsRefused()
    {
        var forward = FactsFixtures.PublisherRelation();

        Assert.ThrowsExactly<ArgumentException>(() => new DerivedInverseRelation(
            FactsSchemaIds.DerivedInverseRelation,
            forward.Target,
            forward.Source,
            FactsFixtures.ConsolidatedByPredicate,
            FactsFixtures.ConsolidatesPredicate,
            new ObservedInverseAxiom(
                FactsFixtures.JoluxOntology,
                FactsFixtures.OntologyVersion,
                "http://example.invalid/unrelated-forward",
                "http://example.invalid/unrelated-inverse",
                FactsFixtures.ObservationId),
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
            TranspositionEvidence.None,
            "lex-lu-date-reader/1",
            FactsFixtures.ObservationId));
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
            "ratification_date",
            FactsFixtures.ObservationId,
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
            "ratification_date",
            ClosedVocabulary.WireNames<DateSemanticRole>(),
            FactsFixtures.ObservationId));
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
            FactsFixtures.ObservationId));
    }

    [TestMethod]
    public void ATermInsideTheAdmittedSetCannotBeReportedAsDrift()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new VocabularyDrift(
            FactsSchemaIds.VocabularyDrift,
            VocabularyKind.DatePrecision,
            "year",
            ClosedVocabulary.WireNames<DatePrecision>(),
            FactsFixtures.ObservationId));
    }

    [TestMethod]
    public void AnEnumThatIsNotAFactsVocabularyCannotBeReadAtAll()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ClosedVocabulary.TryRead<DayOfWeek>(
            "Monday",
            FactsFixtures.ObservationId,
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
            FactsFixtures.ObservationId,
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
            EcliState.EcliNotInThisSet,
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
            FactsFixtures.ObservationId,
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
    public void AFactWithItsObservationRemovedIsRefused()
    {
        var document = Mutate(
            ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root.Remove("source_observation_id"));
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<PublisherRelation>(document));
    }

    /// <summary>
    /// The observation identity is opaque and required. There is no timestamp to validate here
    /// any more, because a Fact no longer carries one: http_observation/1 owns the instant, and a
    /// second copy in a Fact could disagree with it.
    /// </summary>
    [TestMethod]
    public void AnEmptyOrOversizeObservationIdentityIsRefused()
    {
        foreach (var bad in new[] { "", "   ", new string('o', 201), "obsid" })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => FactsFixtures.PublisherRelation(sourceObservationId: bad), bad);
        }
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
            FactsFixtures.ObservationId,
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

    // ---- round two: the six probes that got through ------------------------------------------

    /// <summary>
    /// MUTATION RECEIPT: a CELEX-only case. Candidate 2 tested position 4 for the letter C; in
    /// 62019CJ0311 position 4 is the digit 9, so this returned false. My own test asserted "a
    /// CELEX sector 6 number is a case" and passed anyway, because the fixture also carried a URI
    /// containing /case/. The assertion was true for a reason it did not name.
    /// </summary>
    [TestMethod]
    public void ACelexOnlyCaseIsRecognisedFromItsSectorAlone()
    {
        var celexOnly = new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.Celex, "62020CJ1042")]);

        Assert.IsTrue(celexOnly.IsCase, "sector 6 is case law and sits at position zero");

        var regulation = new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.Celex, "32016R0679")]);
        Assert.IsFalse(regulation.IsCase, "sector 3 is secondary legislation");
    }

    /// <summary>
    /// MUTATION RECEIPT: self-authenticating case authority. A URI on any host containing a
    /// familiar path segment made an object a case, and any printable string could be tagged ECLI.
    /// </summary>
    [TestMethod]
    public void CaseAuthorityCannotBeManufacturedByAUriPathOrAnEnumTag()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new OfficialIdentifier(
                FactsIdentifierFamily.CellarWorkUri,
                "https://example.invalid/resource/case/fake"),
            "a Cellar URI must be on the publisher own host");

        foreach (var malformed in new[]
                 {
                     "ECLI:EU:C:2020", "not-an-ecli", "ECLI:E:C:2020:1042",
                     "ECLI:EU:C:20:1042", "ECLI:EU::2020:1042",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new OfficialIdentifier(FactsIdentifierFamily.Ecli, malformed),
                malformed);
        }
    }

    [TestMethod]
    public void AMalformedCelexIsRefused()
    {
        foreach (var bad in new[] { "62019", "X2019CJ0311", "62019cj0311", "62019CJKL", "6201CJ0311" })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new OfficialIdentifier(FactsIdentifierFamily.Celex, bad), bad);
        }
    }

    /// <summary>
    /// MUTATION RECEIPT: the open end turned into an ordinary date. Candidate 2 bound the sentinel
    /// one way only, so the exact 9999-12-31 could be declared not_open, which reads as "validity
    /// ended in the year 9999" rather than "validity does not end".
    /// </summary>
    [TestMethod]
    public void TheOpenEndSentinelCannotBeDeclaredAnOrdinaryDate()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
            FactsSchemaIds.PublisherDate,
            PublisherDate.OpenEndedLexicalValue,
            PublisherDate.Date,
            DatePrecision.YearMonthDay,
            DateOpenSentinel.NotOpen));
    }

    /// <summary>
    /// The XSD lexical space admits a trailing timezone. Refusing a value the declared datatype
    /// permits is a loss dressed as strictness.
    /// </summary>
    [TestMethod]
    public void AnXsdTimezoneSuffixIsAcceptedAndAMalformedOneIsNot()
    {
        foreach (var value in new[] { "2020-02-29Z", "2020-02-29+02:00", "2020-02-29-05:00" })
        {
            var date = new PublisherDate(
                FactsSchemaIds.PublisherDate, value, PublisherDate.Date,
                DatePrecision.YearMonthDay, DateOpenSentinel.NotOpen);
            Assert.AreEqual(value, date.RawLexicalValue, value);
        }

        foreach (var bad in new[] { "2020-02-29X", "2020-02-29+2:00", "2020-02-29+15:00" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
                FactsSchemaIds.PublisherDate, bad, PublisherDate.Date,
                DatePrecision.YearMonthDay, DateOpenSentinel.NotOpen), bad);
        }
    }

    /// <summary>
    /// UriKind.Absolute also admits mailto, urn and file, none of which resolve to an authority a
    /// reader can consult.
    /// </summary>
    [TestMethod]
    public void AParsingAuthorityMustBeHttpsRatherThanMerelyAbsolute()
    {
        foreach (var bad in new[]
                 {
                     "mailto:someone@example.invalid",
                     "urn:uuid:0a5d1c2e-4f3b-4d18-9c67-1a8f2b6d5e40",
                     "file:///c:/authority",
                     "http://example.invalid/authority",
                 })
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
                TranspositionEvidence.None,
                bad,
                FactsFixtures.ObservationId), bad);
        }
    }

    /// <summary>
    /// MUTATION RECEIPT: a generic deadline silently promoted. The generic publisher deadline is
    /// not necessarily a transposition deadline.
    /// </summary>
    [TestMethod]
    public void ATranspositionDeadlineRequiresItsEvidenceAndEvidenceRequiresADeadline()
    {
        Assert.ThrowsExactly<ArgumentException>(() => FactsFixtures.DateFact(
            FactsFixtures.DayDate(),
            DateSemanticRole.TranspositionDeadline));

        var justified = FactsFixtures.DateFact(
            FactsFixtures.DayDate(),
            DateSemanticRole.TranspositionDeadline,
            evidence: TranspositionEvidence.NimRecord);
        Assert.AreEqual(TranspositionEvidence.NimRecord, justified.TranspositionEvidence);

        Assert.ThrowsExactly<ArgumentException>(() => FactsFixtures.DateFact(
            FactsFixtures.DayDate(),
            DateSemanticRole.PublicationDate,
            evidence: TranspositionEvidence.DirectiveQualifier));
    }

    [TestMethod]
    public void EntryIntoForceAndApplicationAreSeparateRoles()
    {
        var ev = FactsFixtures.DateFact(FactsFixtures.DayDate(), DateSemanticRole.EntryIntoForce);
        var ma = FactsFixtures.DateFact(FactsFixtures.DayDate(), DateSemanticRole.ApplicationDate);

        Assert.AreNotEqual(ev.SemanticRole, ma.SemanticRole);
        Assert.IsTrue(ClosedVocabulary.WireNames<DateSemanticRole>().Contains("signature_date"));
        Assert.IsTrue(ClosedVocabulary.WireNames<DateSemanticRole>().Contains("application_date"));
        Assert.IsTrue(ClosedVocabulary.WireNames<DateSemanticRole>().Contains("publisher_deadline"));
    }

    /// <summary>
    /// MUTATION RECEIPT: two level claims about one string. Candidate 2 admitted one URI as both
    /// <summary>
    /// The resource family documented itself as covering manifestations and items while its grammar
    /// rejected the publisher's own dotted expression and manifestation identifiers, because
    /// <c>Guid.TryParseExact(.., "D")</c> cannot admit a dotted suffix.
    ///
    /// Every shape below was checked live against the official endpoint on 2026-09-02 rather than
    /// inferred: the bare work, the dotted expression and the dotted manifestation each answered 200
    /// and redirected to their own distinct rdf/object/full, <c>{manifestation}/DOC_1</c> answered
    /// 200 directly, and a third dotted level answered 404. The depth ceiling asserted here is the
    /// publisher's answer, not ours.
    /// </summary>
    [TestMethod]
    public void TheCellarFamiliesAdmitEveryPublisherLevelAndNothingDeeper()
    {
        const string work = FactsFixtures.CellarWorkUri;

        var admittedAsResource = new[]
        {
            work + ".0006",            // expression
            work + ".0006.03",         // manifestation
            work + ".0006.03/DOC_1",   // item, the data stream beneath a manifestation
            work + "/DOC_1",           // already admitted before this repair
        };

        foreach (var value in admittedAsResource)
        {
            Assert.AreEqual(
                value,
                new OfficialIdentifier(FactsIdentifierFamily.CellarResourceUri, value).RawValue,
                $"the resource family refused {value}");

            // and the same value is never also a work, so the level is carried by the shape rather
            // than by whichever family the caller reached for.
            Assert.ThrowsExactly<ArgumentException>(
                () => new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, value),
                $"{value} was admitted as a work");
        }

        // The bare work is the work and only the work, in both directions.
        Assert.AreEqual(
            work, new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, work).RawValue);
        Assert.ThrowsExactly<ArgumentException>(
            () => new OfficialIdentifier(FactsIdentifierFamily.CellarResourceUri, work));

        // Near misses, refused by both families. The first is the publisher's own 404.
        var refused = new (string Value, string Why)[]
        {
            (work + ".0006.03.01", "a third dotted level, which Cellar answers 404"),
            (work + ".006", "an expression suffix one digit short"),
            (work + ".00006", "an expression suffix one digit wide"),
            (work + ".0006.3", "a manifestation suffix one digit short"),
            (work + ".0006.003", "a manifestation suffix one digit wide"),
            (work + ".0006.0a", "a manifestation suffix that is not digits"),
            (work + ".", "an empty suffix"),
            (work + "..0006", "an empty level between the work and the expression"),
            ("http://publications.europa.eu/resource/cellar/not-a-uuid.0006", "a head that is not a UUID"),
        };

        foreach (var (value, why) in refused)
        {
            foreach (var family in new[]
                     {
                         FactsIdentifierFamily.CellarWorkUri,
                         FactsIdentifierFamily.CellarResourceUri,
                     })
            {
                Assert.ThrowsExactly<ArgumentException>(
                    () => new OfficialIdentifier(family, value),
                    $"{family} admitted {value} despite {why}");
            }
        }
    }

    /// work-level and resource-level inside a single identity.
    /// </summary>
    [TestMethod]
    public void OneRawValueCannotBeClaimedUnderTwoFamilies()
    {
        var uri = FactsFixtures.CellarWorkUri;

        Assert.ThrowsExactly<ArgumentException>(() => new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [
                new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, uri),
                new OfficialIdentifier(FactsIdentifierFamily.CellarResourceUri, uri),
            ]));
    }

    /// <summary>
    /// MUTATION RECEIPT: identity depending on RDF row order. The same three members in reverse
    /// order compared unequal, so inverse and inbound validation depended on a serialization order
    /// the publisher does not guarantee.
    /// </summary>
    [TestMethod]
    public void IdentityComparisonIsOrderIndependent()
    {
        var forward = FactsFixtures.EuCaseWithEcli();
        var reversed = new OfficialIdentitySet(
            forward.Publisher,
            forward.Identifiers.Reverse().ToArray());

        Assert.IsTrue(forward.SameIdentity(reversed), "the same members in another order");
        Assert.IsTrue(reversed.SameIdentity(forward), "and symmetrically");
        Assert.AreNotEqual(
            forward.Identifiers[0].Family,
            reversed.Identifiers[0].Family,
            "publisher order is still retained as evidence");
    }

    [TestMethod]
    public void AFamilyMintedByAnotherPublisherIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new OfficialIdentitySet(
            PublisherId.LuLegilux,
            [new OfficialIdentifier(FactsIdentifierFamily.Celex, "32016R0679")]));

        Assert.ThrowsExactly<ArgumentException>(() => new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.Memorial, "A512")]));
    }

    // ---- round three: the accepted scope, the biconditional, and proved absence ---------------

    /// <summary>
    /// MUTATION RECEIPT: a no-loss violation. Candidate 3 refused four identities the accepted V3
    /// scope requires, one of which is already a seed in the accepted 82-seed plan. Over-correcting
    /// from "trusts any label" to "refuses anything I had not thought of" loses publisher facts
    /// just as surely.
    /// </summary>
    [TestMethod]
    public void EveryIdentityTheAcceptedScopeRequiresIsRepresentable()
    {
        foreach (var (family, value, why) in new[]
                 {
                     (FactsIdentifierFamily.Celex, "12012E/TXT", "treaty part, and a seed in the accepted plan"),
                     (FactsIdentifierFamily.Celex, "02016R0679-20160504", "consolidated state"),
                     (FactsIdentifierFamily.Celex, "32016R0679R(02)", "corrigendum"),
                     (FactsIdentifierFamily.Celex, "32016R0679", "base act"),
                     (FactsIdentifierFamily.Eli, "http://data.europa.eu/eli/reg/2016/679/oj", "EU absolute ELI"),
                     (FactsIdentifierFamily.Eli, "eli/etat/leg/loi/2019/07/15/a512/jo", "LU relative ELI"),
                 })
        {
            var identifier = new OfficialIdentifier(family, value);
            Assert.AreEqual(value, identifier.RawValue, $"{value} ({why}) must be representable");
        }
    }

    [TestMethod]
    public void EachCelexProfileIsRecognisedAsWhatItIs()
    {
        Assert.AreEqual(CelexProfile.BaseAct, OfficialIdentifier.ProfileOf("32016R0679"));
        Assert.AreEqual(CelexProfile.ConsolidatedAct, OfficialIdentifier.ProfileOf("02016R0679-20160504"));
        Assert.AreEqual(CelexProfile.Corrigendum, OfficialIdentifier.ProfileOf("32016R0679R(02)"));
        Assert.AreEqual(CelexProfile.TreatyPart, OfficialIdentifier.ProfileOf("12012E/TXT"));
        Assert.IsNull(OfficialIdentifier.ProfileOf("not-a-celex"), "a shape outside the profiles");
        Assert.IsNull(OfficialIdentifier.ProfileOf("32016R0679-2016"), "a malformed consolidation date");
        Assert.IsNull(OfficialIdentifier.ProfileOf("32016R0679R(xx)"), "a malformed corrigendum ordinal");
    }

    /// <summary>
    /// MUTATION RECEIPT: the level claim resting on the caller's tag. Both families called the
    /// identical check, so one URI was admissible as either work or resource level.
    /// </summary>
    [TestMethod]
    public void CellarWorkAndResourceLevelsAreDistinguishedByTheUriItself()
    {
        var work = FactsFixtures.CellarWorkUri;
        var resource = FactsFixtures.CellarWorkUri + "/DOC_1";

        Assert.IsTrue(OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.CellarWorkUri, work));
        Assert.IsFalse(
            OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.CellarResourceUri, work),
            "a work URI is not a resource URI");

        Assert.IsTrue(OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.CellarResourceUri, resource));
        Assert.IsFalse(
            OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.CellarWorkUri, resource),
            "a resource URI is not a work URI");
    }

    /// <summary>
    /// MUTATION RECEIPT: the deadline biconditional stopped one role short. Evidence saying "this
    /// is a transposition deadline" cannot sit on a date declaring it is not one.
    /// </summary>
    [TestMethod]
    public void APublisherDeadlineCannotCarryTranspositionEvidence()
    {
        Assert.ThrowsExactly<ArgumentException>(() => FactsFixtures.DateFact(
            FactsFixtures.DayDate(),
            DateSemanticRole.PublisherDeadline,
            evidence: TranspositionEvidence.DirectiveQualifier));

        var plain = FactsFixtures.DateFact(FactsFixtures.DayDate(), DateSemanticRole.PublisherDeadline);
        Assert.AreEqual(TranspositionEvidence.None, plain.TranspositionEvidence);
    }

    /// <summary>
    /// The state describes the set in front of it, not the publisher.
    /// </summary>
    /// <remarks>
    /// Candidate 3 called this `ecli_missing` and tried to license the absence claim with an
    /// enumeration state and a query digest the caller chose freely. A digest names which query
    /// text was identified; it does not prove the query ran, exhausted its continuations, or
    /// corresponds to the set beside it. The machinery is gone and the name is now exactly what
    /// this contract can establish.
    /// </remarks>
    [TestMethod]
    public void EcliNotInThisSetDescribesTheSetRatherThanThePublisher()
    {
        var fact = FactsFixtures.CaseFactWithoutEcli();

        Assert.AreEqual(EcliState.EcliNotInThisSet, fact.TargetEcliState);
        Assert.IsNull(fact.TargetEcli);
        Assert.IsTrue(fact.CarriedTarget.IsCase, "the state only applies to a case");

        // The wire name says what is claimed, so a reader cannot mistake it for a publisher fact.
        Assert.Contains("ecli_not_in_this_set", ContractJson.Serialize(fact));
        Assert.IsFalse(ClosedVocabulary.WireNames<EcliState>().Contains("ecli_missing"));
    }

    /// <summary>
    /// Nothing in this contract claims an identifier set is complete, because nothing here can
    /// prove it. The type carries the identifiers it holds and no completeness field at all.
    /// </summary>
    [TestMethod]
    public void TheIdentitySetMakesNoCompletenessClaim()
    {
        var properties = typeof(OfficialIdentitySet)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        foreach (var absent in new[] { "Enumeration", "EnumerationQuerySha256", "Complete" })
        {
            Assert.IsFalse(
                properties.Contains(absent),
                $"{absent} would be a completeness claim this contract cannot support");
        }

        CollectionAssert.AreEqual(
            new[] { "Identifiers", "IsCase", "Publisher" },
            properties.Where(n => n is not "EqualityContract").OrderBy(n => n).ToArray());
    }

    /// <summary>
    /// The sentinel is a date value in any lexical form its datatype admits, and the reader and
    /// the schema now agree on that set rather than disagreeing about the zoned form.
    /// </summary>
    [TestMethod]
    public void EveryAdmittedLexicalFormOfTheSentinelIsTheSentinel()
    {
        foreach (var value in new[] { "9999-12-31", "9999-12-31Z", "9999-12-31+02:00" })
        {
            var open = new PublisherDate(
                FactsSchemaIds.PublisherDate, value, PublisherDate.Date,
                DatePrecision.YearMonthDay, DateOpenSentinel.OpenEnded);
            Assert.AreEqual(value, open.RawLexicalValue);

            Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
                FactsSchemaIds.PublisherDate, value, PublisherDate.Date,
                DatePrecision.YearMonthDay, DateOpenSentinel.NotOpen), value);
        }
    }

    // ---- round four: the profiles that were missing and the dates that were not checked ------

    /// <summary>
    /// MUTATION RECEIPT: a required family refused. Sector 7 national implementing measures carry
    /// a country code and a national reference after the act number, and they are part of the
    /// relation and transposition spine rather than a hypothetical family.
    /// </summary>
    [TestMethod]
    public void ASectorSevenNationalImplementingMeasureIsRepresentable()
    {
        const string nim = "72019L1937LUX_202303892";

        Assert.AreEqual(CelexProfile.NationalImplementingMeasure, OfficialIdentifier.ProfileOf(nim));
        var identifier = new OfficialIdentifier(FactsIdentifierFamily.Celex, nim);
        Assert.AreEqual(nim, identifier.RawValue);
    }

    /// <summary>
    /// MUTATION RECEIPT: an impossible consolidation date accepted. Candidate 3 checked that the
    /// suffix was eight digits and my own declaration claimed the date was parsed and checked, so
    /// the claim was as wrong as the code.
    /// </summary>
    [TestMethod]
    public void AConsolidationSuffixMustBeARealCalendarDate()
    {
        Assert.AreEqual(
            CelexProfile.ConsolidatedAct,
            OfficialIdentifier.ProfileOf("02016R0679-20160504"));

        foreach (var impossible in new[]
                 {
                     "02016R0679-20160231", "02016R0679-20161301",
                     "02016R0679-20160000", "02016R0679-20190229",
                 })
        {
            Assert.IsNull(OfficialIdentifier.ProfileOf(impossible), impossible);
            Assert.ThrowsExactly<ArgumentException>(
                () => new OfficialIdentifier(FactsIdentifierFamily.Celex, impossible), impossible);
        }

        // a real leap day still resolves
        Assert.AreEqual(
            CelexProfile.ConsolidatedAct,
            OfficialIdentifier.ProfileOf("02016R0679-20200229"));
    }

    /// <summary>
    /// MUTATION RECEIPT: a caller-shaped lookalike admitted as an official identifier. Candidate 3
    /// accepted any host whose path contained the ELI segment.
    /// </summary>
    [TestMethod]
    public void AnAbsoluteEliMustBeOnAPublisherHost()
    {
        foreach (var good in new[]
                 {
                     "http://data.europa.eu/eli/reg/2016/679/oj",
                     "https://data.legilux.public.lu/eli/etat/leg/loi/2019/07/15/a512/jo",
                 })
        {
            Assert.IsTrue(OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.Eli, good), good);
        }

        foreach (var lookalike in new[]
                 {
                     "https://example.invalid/eli/reg/2016/679/oj",
                     "https://data.europa.eu.example.invalid/eli/reg/2016/679/oj",
                     "https://attacker.test/eli/",
                 })
        {
            Assert.IsFalse(
                OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.Eli, lookalike),
                lookalike);
        }
    }

    [TestMethod]
    public void ACelexWithAnInvalidTrailingSuffixIsRefused()
    {
        foreach (var bad in new[]
                 {
                     "32016R0679XX", "32016R0679-", "32016R0679R()", "32016R0679_",
                     "12012E/TXT/", "72019L1937LU_202303892",
                 })
        {
            Assert.IsNull(OfficialIdentifier.ProfileOf(bad), bad);
        }
    }

    // ---- round five: authority by shape, the alias, and the timezone ceiling -----------------

    /// <summary>
    /// MUTATION RECEIPT: authority still resting on the caller. Both publishers mint an ELI in
    /// different lexical shapes, so binding the family to a publisher was not enough: an EU
    /// identity could carry the Luxembourg relative path and a Luxembourg identity the EU URI.
    /// </summary>
    [TestMethod]
    public void AnEliShapeBelongsToThePublisherThatMintsIt()
    {
        Assert.AreEqual(
            PublisherId.LuLegilux,
            OfficialIdentifier.EliMintedBy("eli/etat/leg/loi/2019/07/15/a512/jo"));
        Assert.AreEqual(
            PublisherId.EuEurLex,
            OfficialIdentifier.EliMintedBy("http://data.europa.eu/eli/reg/2016/679/oj"));
        Assert.IsNull(OfficialIdentifier.EliMintedBy("https://example.invalid/eli/x"));

        // an EU identity carrying the Luxembourg shape
        Assert.ThrowsExactly<ArgumentException>(() => new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.Eli, "eli/etat/leg/loi/2019/07/15/a512/jo")]));

        // and a Luxembourg identity carrying the EU shape
        Assert.ThrowsExactly<ArgumentException>(() => new OfficialIdentitySet(
            PublisherId.LuLegilux,
            [new OfficialIdentifier(FactsIdentifierFamily.Eli, "http://data.europa.eu/eli/reg/2016/679/oj")]));
    }

    /// <summary>
    /// MUTATION RECEIPT: an alias relabelled as the thing itself. The CELEX persistent identifier
    /// is tied to the work by owl:sameAs; the publisher's predicates live on the Cellar UUID work.
    /// Candidate 4 admitted any three-segment resource path as a work, so the alias passed.
    /// </summary>
    [TestMethod]
    public void TheCelexPersistentIdentifierIsNotTheCellarWork()
    {
        Assert.IsFalse(
            OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.CellarWorkUri, FactsFixtures.CellarPsiUri),
            "the PSI alias is not the work");
        Assert.IsTrue(
            OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.CellarPsiUri, FactsFixtures.CellarPsiUri),
            "but it is a fact in its own right");
        Assert.IsTrue(
            OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.CellarWorkUri, FactsFixtures.CellarWorkUri),
            "and the UUID work is the work");
        Assert.IsFalse(
            OfficialIdentifier.IsWellFormed(FactsIdentifierFamily.CellarPsiUri, FactsFixtures.CellarWorkUri),
            "the work is not an alias either");
    }

    /// <summary>
    /// The XSD timezone ceiling is exactly 14:00, and the reader and the schema must agree on it.
    /// </summary>
    [TestMethod]
    public void TheTimezoneCeilingIsExactlyFourteenHours()
    {
        foreach (var ok in new[] { "2019-07-15+14:00", "2019-07-15-14:00", "2019-07-15+13:59" })
        {
            var date = new PublisherDate(
                FactsSchemaIds.PublisherDate, ok, PublisherDate.Date,
                DatePrecision.YearMonthDay, DateOpenSentinel.NotOpen);
            Assert.AreEqual(ok, date.RawLexicalValue, ok);
        }

        foreach (var bad in new[] { "2019-07-15+14:01", "2019-07-15+99:99", "2019-07-15+15:00" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new PublisherDate(
                FactsSchemaIds.PublisherDate, bad, PublisherDate.Date,
                DatePrecision.YearMonthDay, DateOpenSentinel.NotOpen), bad);
        }
    }

    [TestMethod]
    public void AControlCharacterInsideAnEcliIsRefused()
    {
        foreach (var bad in new[]
                 {
                     "ECLI:EU:C" + "\n" + ":2020:1042",  // a real newline, refused by the printable bound
                     "ECLI:EU:C :2020:1042",    // a printable oddity, which the reader used to admit
                     "ECLI:EU:c:2020:1042",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new OfficialIdentifier(FactsIdentifierFamily.Ecli, bad), bad);
        }
    }

    private static string Mutate(string json, Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        mutate(root);
        return root.ToJsonString();
    }
}
