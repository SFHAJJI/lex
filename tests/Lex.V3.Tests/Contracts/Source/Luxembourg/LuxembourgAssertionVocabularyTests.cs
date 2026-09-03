using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The Luxembourg assertion-predicate vocabulary: Decision 65's closed twenty-six, with the
/// Candidate 5 R4 separation between an Act's own force facts and a Consolidation's applicability
/// interval.
///
/// Every cardinality and token below is transcribed from Decision 65's text and from the
/// already-merged <c>VerifiedLuxembourgSourceProfile.BuildRequiredVocabulary</c>'s
/// <c>AssertionPredicate</c> rows, not computed from the enum under test.
/// <see cref="CrossCheckAgainstTheMergedSourceProfileTests"/> is the executed form of that
/// cross-check, reading the profile's real <c>RequiredIriVocabulary</c> output rather than a
/// hand-transcribed comment.
/// </summary>
[TestClass]
public sealed class LuxembourgAssertionVocabularyTests
{
    private const string N = "Lex.V3.Contracts.Source.Luxembourg.";
    private const string Core = "Lex.V3.Contracts.Source.Core.";

    [TestMethod]
    public void TheAssertionVocabularyHasExactlyTwentySixMembers()
    {
        Assert.AreEqual(26, LuxembourgAssertionVocabulary.Predicates.Count);
        Assert.AreEqual(2, LuxembourgAssertionVocabulary.ActForceDatePredicates.Count);
        Assert.AreEqual(2, LuxembourgAssertionVocabulary.ConsolidationApplicabilityDatePredicates.Count);
        Assert.AreEqual(10, Enum.GetValues<LuxembourgAssertionFactKind>().Length);
    }

    [TestMethod]
    public void EveryAssertionPredicateSerialisesToItsExactPublisherToken()
    {
        // Alphabetical, matching VerifiedLuxembourgSourceProfile.BuildRequiredVocabulary's own
        // declaration order for AssertionPredicate rows (rdf:type first, then the JOLux locals
        // sorted by Unicode scalar value).
        AssertTokens<LuxembourgAssertionPredicate>(
            "rdf:type", "dateApplicability", "dateDocument", "dateEndApplicability",
            "dateEntryInForce", "dateNoLongerInForce", "historicalLegalId", "inForceStatus",
            "isEmbodiedBy", "isExemplifiedBy", "isMemberOf", "isPartOf", "isRealizedBy",
            "language", "legalValue", "license", "previousIsExemplifiedBy", "publicationDate",
            "publisher", "responsibilityOf", "rights", "rightsHolder", "title", "titleShort",
            "typeDocument", "userFormat");
        AssertTokens<LuxembourgActForceDatePredicate>("dateEntryInForce", "dateNoLongerInForce");
        AssertTokens<LuxembourgConsolidationApplicabilityDatePredicate>(
            "dateApplicability", "dateEndApplicability");
        AssertTokens<LuxembourgAssertionFactKind>(
            "act_force", "consolidation_applicability", "descriptive_date", "act_identity",
            "resource_type", "wemi_structural", "expression_language_or_title",
            "manifestation_format", "legal_value_assertion", "rights_and_provenance");
    }

    [TestMethod]
    public void EveryTokenRoundTripsToItsOwnMember()
    {
        AssertRoundTrip<LuxembourgAssertionPredicate>();
        AssertRoundTrip<LuxembourgActForceDatePredicate>();
        AssertRoundTrip<LuxembourgConsolidationApplicabilityDatePredicate>();
        AssertRoundTrip<LuxembourgAssertionFactKind>();
    }

    [TestMethod]
    public void UnknownVocabularyFailsClosedInEveryClosedSet()
    {
        AssertScopeDrift<LuxembourgAssertionPredicate>("dateEntryIntoForce");
        AssertScopeDrift<LuxembourgActForceDatePredicate>("dateApplicability");
        AssertScopeDrift<LuxembourgConsolidationApplicabilityDatePredicate>("dateEntryInForce");
        AssertScopeDrift<LuxembourgAssertionFactKind>("ActForce");
    }

    [TestMethod]
    public void EveryAssertionPredicateHasAPinnedFactKindDrivenThroughConstruction()
    {
        // The census requirement, for this file's twenty-six: FactKindOf fails closed (a default
        // arm throws ArgumentOutOfRangeException for anything unmapped; C# cannot make an enum
        // switch compiler-exhaustive over named members alone) and every predicate can build a
        // real disposition carrying that exact pinned kind.
        foreach (var predicate in LuxembourgAssertionVocabulary.Predicates)
        {
            var kind = LuxembourgAssertionVocabulary.FactKindOf(predicate);
            Assert.IsTrue(Enum.IsDefined(kind), $"{predicate} mapped to an undefined fact kind");

            var disposition = new LuxembourgAssertionFactDisposition(predicate, kind, Evidence("21"));
            Assert.AreEqual(predicate, disposition.Predicate);
            Assert.AreEqual(kind, disposition.FactKind);
        }
    }

    [TestMethod]
    public void FactKindCategoriesPartitionAllTwentySixPredicatesExactly()
    {
        var expected = new Dictionary<LuxembourgAssertionPredicate, LuxembourgAssertionFactKind>
        {
            [LuxembourgAssertionPredicate.DateEntryInForce] = LuxembourgAssertionFactKind.ActForce,
            [LuxembourgAssertionPredicate.DateNoLongerInForce] = LuxembourgAssertionFactKind.ActForce,
            [LuxembourgAssertionPredicate.InForceStatus] = LuxembourgAssertionFactKind.ActForce,
            [LuxembourgAssertionPredicate.DateApplicability] =
                LuxembourgAssertionFactKind.ConsolidationApplicability,
            [LuxembourgAssertionPredicate.DateEndApplicability] =
                LuxembourgAssertionFactKind.ConsolidationApplicability,
            [LuxembourgAssertionPredicate.DateDocument] = LuxembourgAssertionFactKind.DescriptiveDate,
            [LuxembourgAssertionPredicate.PublicationDate] = LuxembourgAssertionFactKind.DescriptiveDate,
            [LuxembourgAssertionPredicate.HistoricalLegalId] = LuxembourgAssertionFactKind.ActIdentity,
            [LuxembourgAssertionPredicate.ResponsibilityOf] = LuxembourgAssertionFactKind.ActIdentity,
            [LuxembourgAssertionPredicate.RdfType] = LuxembourgAssertionFactKind.ResourceType,
            [LuxembourgAssertionPredicate.TypeDocument] = LuxembourgAssertionFactKind.ResourceType,
            [LuxembourgAssertionPredicate.IsPartOf] = LuxembourgAssertionFactKind.WemiStructural,
            [LuxembourgAssertionPredicate.IsMemberOf] = LuxembourgAssertionFactKind.WemiStructural,
            [LuxembourgAssertionPredicate.IsRealizedBy] = LuxembourgAssertionFactKind.WemiStructural,
            [LuxembourgAssertionPredicate.IsEmbodiedBy] = LuxembourgAssertionFactKind.WemiStructural,
            [LuxembourgAssertionPredicate.IsExemplifiedBy] = LuxembourgAssertionFactKind.WemiStructural,
            [LuxembourgAssertionPredicate.PreviousIsExemplifiedBy] = LuxembourgAssertionFactKind.WemiStructural,
            [LuxembourgAssertionPredicate.Language] = LuxembourgAssertionFactKind.ExpressionLanguageOrTitle,
            [LuxembourgAssertionPredicate.Title] = LuxembourgAssertionFactKind.ExpressionLanguageOrTitle,
            [LuxembourgAssertionPredicate.TitleShort] = LuxembourgAssertionFactKind.ExpressionLanguageOrTitle,
            [LuxembourgAssertionPredicate.UserFormat] = LuxembourgAssertionFactKind.ManifestationFormat,
            [LuxembourgAssertionPredicate.LegalValue] = LuxembourgAssertionFactKind.LegalValueAssertion,
            [LuxembourgAssertionPredicate.License] = LuxembourgAssertionFactKind.RightsAndProvenance,
            [LuxembourgAssertionPredicate.Rights] = LuxembourgAssertionFactKind.RightsAndProvenance,
            [LuxembourgAssertionPredicate.RightsHolder] = LuxembourgAssertionFactKind.RightsAndProvenance,
            [LuxembourgAssertionPredicate.Publisher] = LuxembourgAssertionFactKind.RightsAndProvenance,
        };

        Assert.AreEqual(26, expected.Count, "the pinned expectation table itself is incomplete");
        foreach (var predicate in LuxembourgAssertionVocabulary.Predicates)
        {
            Assert.AreEqual(
                expected[predicate],
                LuxembourgAssertionVocabulary.FactKindOf(predicate),
                $"{predicate} does not carry its pinned fact kind");
        }
    }

    [TestMethod]
    public void AnActForceSlotCannotBePopulatedFromAConsolidationIntervalValue()
    {
        // The exact mutation the Stage 2 register names for E3: "no act force slot is ever
        // populated from a consolidation interval." Driven through the runtime-checked
        // disposition, which is the guard a caller holding a flat predicate would hit.
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgAssertionFactDisposition(
                LuxembourgAssertionPredicate.DateApplicability,
                LuxembourgAssertionFactKind.ActForce,
                Evidence("22")));
        StringAssert.Contains(thrown.Message, "is pinned to");
    }

    [TestMethod]
    public void AConsolidationIntervalSlotCannotBePopulatedFromAnActForceValue()
    {
        // The mirror mutation: an act-force value labelled as though it were the consolidation's
        // own documentary interval.
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgAssertionFactDisposition(
                LuxembourgAssertionPredicate.DateEntryInForce,
                LuxembourgAssertionFactKind.ConsolidationApplicability,
                Evidence("23")));
        StringAssert.Contains(thrown.Message, "is pinned to");
    }

    [TestMethod]
    public void EveryOtherPredicateAlsoRefusesEveryWrongFactKind()
    {
        // Exhaustive rather than exemplary: the two tests above prove one predicate each direction
        // and would leave the other twenty-four pairings unchecked, which is exactly the shape that
        // let a false pairing through before this file existed.
        foreach (var predicate in LuxembourgAssertionVocabulary.Predicates)
        {
            var pinned = LuxembourgAssertionVocabulary.FactKindOf(predicate);
            foreach (var wrongKind in Enum.GetValues<LuxembourgAssertionFactKind>())
            {
                if (wrongKind == pinned)
                {
                    continue;
                }

                Assert.ThrowsExactly<ArgumentException>(
                    () => new LuxembourgAssertionFactDisposition(predicate, wrongKind, Evidence("24")),
                    $"{predicate} accepted {wrongKind} instead of its pinned {pinned}");
            }
        }
    }

    [TestMethod]
    public void AnActForceDateFactCanOnlyBeBuiltFromTheActForceDatePredicateType()
    {
        // Structural proof, not merely a runtime one: the constructor parameter is exactly
        // LuxembourgActForceDatePredicate, so LuxembourgConsolidationApplicabilityDatePredicate.
        // DateApplicability is not an expression this constructor call can even type-check with.
        // Reflection confirms the fence is the parameter type itself.
        var ctor = typeof(LuxembourgActForceDateFact).GetConstructors().Single();
        Assert.AreEqual(typeof(LuxembourgActForceDatePredicate), ctor.GetParameters()[0].ParameterType);

        var fact = new LuxembourgActForceDateFact(
            LuxembourgActForceDatePredicate.DateEntryInForce,
            "2020-05-29",
            "http://www.w3.org/2001/XMLSchema#date",
            Evidence("25"));
        Assert.AreEqual(LuxembourgAssertionPredicate.DateEntryInForce, fact.UnderlyingPredicate);
    }

    [TestMethod]
    public void AConsolidationApplicabilityDateFactCanOnlyBeBuiltFromItsOwnPredicateType()
    {
        var ctor = typeof(LuxembourgConsolidationApplicabilityDateFact).GetConstructors().Single();
        Assert.AreEqual(
            typeof(LuxembourgConsolidationApplicabilityDatePredicate),
            ctor.GetParameters()[0].ParameterType);
        Assert.AreNotEqual(
            ctor.GetParameters()[0].ParameterType,
            typeof(LuxembourgActForceDateFact).GetConstructors().Single().GetParameters()[0].ParameterType,
            "the two fact types share one predicate type; the fence would not be structural");

        var fact = new LuxembourgConsolidationApplicabilityDateFact(
            LuxembourgConsolidationApplicabilityDatePredicate.DateApplicability,
            "2023-08-22",
            "http://www.w3.org/2001/XMLSchema#date",
            Evidence("26"));
        Assert.AreEqual(LuxembourgAssertionPredicate.DateApplicability, fact.UnderlyingPredicate);
    }

    [TestMethod]
    public void ADeserialisedConsolidationIntervalTokenIsRefusedOnTheActForceWireToo()
    {
        // The constructor guard must hold on the wire path too, mirroring EU's own
        // ADeserialisedGermanBodyCandidateIsRefusedOnTheWireToo: a document whose predicate token
        // is "dateApplicability" must not deserialise into an Act-force fact, because
        // ExactStringEnumConverter<LuxembourgActForceDatePredicate> has no member for that token at
        // all.
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgActForceDateFact>(
                """
                {"predicate":"dateApplicability","raw_lexical_value":"2023-08-22","datatype_iri":"http://www.w3.org/2001/XMLSchema#date","evidence_ref":{"resource_id":"urn:uuid:00000000-0000-4000-8000-000000000027","sha256":"7777777777777777777777777777777777777777777777777777777777777777"}}
                """));
    }

    [TestMethod]
    public void ADeserialisedActForceTokenIsRefusedOnTheConsolidationIntervalWireToo()
    {
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgConsolidationApplicabilityDateFact>(
                """
                {"predicate":"dateEntryInForce","raw_lexical_value":"2020-05-29","datatype_iri":"http://www.w3.org/2001/XMLSchema#date","evidence_ref":{"resource_id":"urn:uuid:00000000-0000-4000-8000-000000000028","sha256":"8888888888888888888888888888888888888888888888888888888888888888"}}
                """));
    }

    [TestMethod]
    public void ADeserialisedWrongFactKindIsRefusedOnTheDispositionWireToo()
    {
        // Before LuxembourgAssertionFactKind carried wire tokens, "act_force" was an unknown enum
        // value, so this test passed because deserialisation failed before ever reaching
        // LuxembourgAssertionFactDisposition's own pinned-fact-kind guard -- the same shape as a
        // test that passes for the wrong reason everywhere else in this project. Now that ActForce
        // carries "act_force" as its real wire token, this document's members deserialise
        // successfully and the guard itself refuses it: dateApplicability is pinned to
        // ConsolidationApplicability, not ActForce. The inner exception's message proves the real
        // guard fired rather than a generic deserialisation failure.
        var thrown = Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgAssertionFactDisposition>(
                """
                {"predicate":"dateApplicability","fact_kind":"act_force","evidence_ref":{"resource_id":"urn:uuid:00000000-0000-4000-8000-000000000029","sha256":"9999999999999999999999999999999999999999999999999999999999999999"}}
                """));
        Assert.IsInstanceOfType<ArgumentException>(thrown.InnerException);
        StringAssert.Contains(thrown.InnerException!.Message, "is pinned to");
    }

    [TestMethod]
    public void AContradictingUnderlyingPredicateIsRefusedOnTheActForceDateFactWire()
    {
        // UnderlyingPredicate is always re-derivable from Predicate alone
        // (LuxembourgAssertionVocabulary.UnderlyingPredicate is a pure function of it), so the
        // constructor parameter is optional: a normal document need not carry a redundant,
        // always-derivable field. Before this fix the property had no constructor parameter at
        // all -- it was serialised on write and silently dropped on read, so a document whose
        // underlying_predicate contradicted its own predicate was accepted with no complaint.
        var fact = new LuxembourgActForceDateFact(
            LuxembourgActForceDatePredicate.DateEntryInForce,
            "2020-05-29",
            "http://www.w3.org/2001/XMLSchema#date",
            Evidence("30"));
        Assert.AreEqual(LuxembourgAssertionPredicate.DateEntryInForce, fact.UnderlyingPredicate);

        var withoutIt = "{\"predicate\":\"dateEntryInForce\",\"raw_lexical_value\":\"2020-05-29\","
            + "\"datatype_iri\":\"http://www.w3.org/2001/XMLSchema#date\",\"evidence_ref\":"
            + EvidenceRefJson("30") + "}";
        var parsedWithoutIt = ContractJson.Deserialize<LuxembourgActForceDateFact>(withoutIt);
        Assert.AreEqual(LuxembourgAssertionPredicate.DateEntryInForce, parsedWithoutIt.UnderlyingPredicate);

        var agreeing = "{\"predicate\":\"dateEntryInForce\",\"raw_lexical_value\":\"2020-05-29\","
            + "\"datatype_iri\":\"http://www.w3.org/2001/XMLSchema#date\",\"evidence_ref\":"
            + EvidenceRefJson("30") + ",\"underlying_predicate\":\"dateEntryInForce\"}";
        var parsedAgreeing = ContractJson.Deserialize<LuxembourgActForceDateFact>(agreeing);
        Assert.AreEqual(LuxembourgAssertionPredicate.DateEntryInForce, parsedAgreeing.UnderlyingPredicate);

        // A document whose derived slot contradicts its own predicate is explicitly refused, not
        // silently accepted with the wrong value dropped.
        var contradicting = "{\"predicate\":\"dateEntryInForce\",\"raw_lexical_value\":\"2020-05-29\","
            + "\"datatype_iri\":\"http://www.w3.org/2001/XMLSchema#date\",\"evidence_ref\":"
            + EvidenceRefJson("30") + ",\"underlying_predicate\":\"dateNoLongerInForce\"}";
        var thrown = Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgActForceDateFact>(contradicting));
        Assert.IsInstanceOfType<ArgumentException>(thrown.InnerException);
        StringAssert.Contains(thrown.InnerException!.Message, "does not match");
    }

    [TestMethod]
    public void AContradictingUnderlyingPredicateIsRefusedOnTheActForceDateFactWireForItsSecondMember()
    {
        // Fold-in: every test above that actually constructs a LuxembourgActForceDateFact used only
        // DateEntryInForce, so DateNoLongerInForce's arm of UnderlyingPredicate's cross-validation
        // switch never ran. Drive it exactly as the first member is driven above: a plain
        // construction, an agreeing wire document, and a contradicting one.
        var fact = new LuxembourgActForceDateFact(
            LuxembourgActForceDatePredicate.DateNoLongerInForce,
            "2024-01-15",
            "http://www.w3.org/2001/XMLSchema#date",
            Evidence("32"));
        Assert.AreEqual(LuxembourgAssertionPredicate.DateNoLongerInForce, fact.UnderlyingPredicate);

        var agreeing = "{\"predicate\":\"dateNoLongerInForce\",\"raw_lexical_value\":\"2024-01-15\","
            + "\"datatype_iri\":\"http://www.w3.org/2001/XMLSchema#date\",\"evidence_ref\":"
            + EvidenceRefJson("32") + ",\"underlying_predicate\":\"dateNoLongerInForce\"}";
        var parsedAgreeing = ContractJson.Deserialize<LuxembourgActForceDateFact>(agreeing);
        Assert.AreEqual(LuxembourgAssertionPredicate.DateNoLongerInForce, parsedAgreeing.UnderlyingPredicate);

        var contradicting = "{\"predicate\":\"dateNoLongerInForce\",\"raw_lexical_value\":\"2024-01-15\","
            + "\"datatype_iri\":\"http://www.w3.org/2001/XMLSchema#date\",\"evidence_ref\":"
            + EvidenceRefJson("32") + ",\"underlying_predicate\":\"dateEntryInForce\"}";
        var thrown = Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgActForceDateFact>(contradicting));
        Assert.IsInstanceOfType<ArgumentException>(thrown.InnerException);
        StringAssert.Contains(thrown.InnerException!.Message, "does not match");
    }

    [TestMethod]
    public void AContradictingUnderlyingPredicateIsRefusedOnTheConsolidationApplicabilityDateFactWire()
    {
        var fact = new LuxembourgConsolidationApplicabilityDateFact(
            LuxembourgConsolidationApplicabilityDatePredicate.DateApplicability,
            "2023-08-22",
            "http://www.w3.org/2001/XMLSchema#date",
            Evidence("31"));
        Assert.AreEqual(LuxembourgAssertionPredicate.DateApplicability, fact.UnderlyingPredicate);

        var agreeing = "{\"predicate\":\"dateApplicability\",\"raw_lexical_value\":\"2023-08-22\","
            + "\"datatype_iri\":\"http://www.w3.org/2001/XMLSchema#date\",\"evidence_ref\":"
            + EvidenceRefJson("31") + ",\"underlying_predicate\":\"dateApplicability\"}";
        var parsedAgreeing = ContractJson.Deserialize<LuxembourgConsolidationApplicabilityDateFact>(agreeing);
        Assert.AreEqual(LuxembourgAssertionPredicate.DateApplicability, parsedAgreeing.UnderlyingPredicate);

        var contradicting = "{\"predicate\":\"dateApplicability\",\"raw_lexical_value\":\"2023-08-22\","
            + "\"datatype_iri\":\"http://www.w3.org/2001/XMLSchema#date\",\"evidence_ref\":"
            + EvidenceRefJson("31") + ",\"underlying_predicate\":\"dateEndApplicability\"}";
        var thrown = Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgConsolidationApplicabilityDateFact>(contradicting));
        Assert.IsInstanceOfType<ArgumentException>(thrown.InnerException);
        StringAssert.Contains(thrown.InnerException!.Message, "does not match");
    }

    [TestMethod]
    public void AContradictingUnderlyingPredicateIsRefusedOnTheConsolidationApplicabilityDateFactWireForItsSecondMember()
    {
        // Fold-in: the mirror gap on the consolidation side. Every test above that actually
        // constructs a LuxembourgConsolidationApplicabilityDateFact used only DateApplicability, so
        // DateEndApplicability's arm never ran.
        var fact = new LuxembourgConsolidationApplicabilityDateFact(
            LuxembourgConsolidationApplicabilityDatePredicate.DateEndApplicability,
            "2024-06-30",
            "http://www.w3.org/2001/XMLSchema#date",
            Evidence("33"));
        Assert.AreEqual(LuxembourgAssertionPredicate.DateEndApplicability, fact.UnderlyingPredicate);

        var agreeing = "{\"predicate\":\"dateEndApplicability\",\"raw_lexical_value\":\"2024-06-30\","
            + "\"datatype_iri\":\"http://www.w3.org/2001/XMLSchema#date\",\"evidence_ref\":"
            + EvidenceRefJson("33") + ",\"underlying_predicate\":\"dateEndApplicability\"}";
        var parsedAgreeing = ContractJson.Deserialize<LuxembourgConsolidationApplicabilityDateFact>(agreeing);
        Assert.AreEqual(LuxembourgAssertionPredicate.DateEndApplicability, parsedAgreeing.UnderlyingPredicate);

        var contradicting = "{\"predicate\":\"dateEndApplicability\",\"raw_lexical_value\":\"2024-06-30\","
            + "\"datatype_iri\":\"http://www.w3.org/2001/XMLSchema#date\",\"evidence_ref\":"
            + EvidenceRefJson("33") + ",\"underlying_predicate\":\"dateApplicability\"}";
        var thrown = Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgConsolidationApplicabilityDateFact>(contradicting));
        Assert.IsInstanceOfType<ArgumentException>(thrown.InnerException);
        StringAssert.Contains(thrown.InnerException!.Message, "does not match");
    }

    [TestMethod]
    public void CrossCheckAgainstTheMergedSourceProfileTests()
    {
        // Fold-in: replaces a hand-transcribed comment claiming this vocabulary matches
        // VerifiedLuxembourgSourceProfile's own rows with an executed comparison against that
        // profile's real RequiredIriVocabulary output, so an edit to either list is caught
        // automatically instead of silently drifting.
        const string prefix = "http://data.legilux.public.lu/resource/ontology/jolux#";
        const string rdfTypeIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
        var profileLocalNames = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary
            .Where(value => value.Kind == LuxembourgVocabularyKind.AssertionPredicate)
            .Select(value =>
            {
                if (string.Equals(value.FullIri, rdfTypeIri, StringComparison.Ordinal))
                {
                    return "rdf:type";
                }

                Assert.IsTrue(
                    value.FullIri.StartsWith(prefix, StringComparison.Ordinal),
                    $"{value.FullIri} is neither rdf:type nor a JOLux local predicate");
                return value.FullIri[prefix.Length..];
            })
            .ToHashSet(StringComparer.Ordinal);

        var enumLocalNames = LuxembourgAssertionVocabulary.Predicates
            .Select(predicate => ContractJson.Serialize(predicate).Trim('"'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.AreEqual(
            profileLocalNames.Count,
            enumLocalNames.Count,
            "LuxembourgAssertionPredicate and VerifiedLuxembourgSourceProfile.RequiredIriVocabulary " +
            "disagree on how many assertion predicates are settled");
        CollectionAssert.AreEquivalent(
            profileLocalNames.ToArray(),
            enumLocalNames.ToArray(),
            "LuxembourgAssertionPredicate and VerifiedLuxembourgSourceProfile.RequiredIriVocabulary " +
            "name a different set of assertion predicates");
    }

    [TestMethod]
    public void AnActForceDateFactHasExactlyOneCheckedDoor()
    {
        // Transcribed from ConstructionSurface.Of's actual output, per this project's
        // print-then-transcribe technique (see LuxembourgConstructionSurfaceTests.cs's remarks).
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgActForceDateFact::.ctor("
                + N + "LuxembourgActForceDateFact) -> " + N + "LuxembourgActForceDateFact",
                "constructor public instance " + N + "LuxembourgActForceDateFact::.ctor("
                + N + "LuxembourgActForceDatePredicate, System.String, System.String, "
                + Core + "SourceArtifactRef, System.Nullable<" + N + "LuxembourgAssertionPredicate>) -> "
                + N + "LuxembourgActForceDateFact",
                "method public instance " + N + "LuxembourgActForceDateFact::<Clone>$() -> "
                + N + "LuxembourgActForceDateFact",
            },
            ConstructionSurface.Of(typeof(LuxembourgActForceDateFact)).ToArray());

        // Fold-in: paired the way the sibling Luxembourg pin file pairs every Of pin with a
        // ProducersIn assertion. Nothing else in Contracts hands out an Act-force date fact.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgActForceDateFact).Assembly,
                typeof(LuxembourgActForceDateFact),
                true).ToArray(),
            "something in Contracts now hands out an Act-force date fact it did not have to construct");
    }

    [TestMethod]
    public void AConsolidationApplicabilityDateFactHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgConsolidationApplicabilityDateFact::.ctor("
                + N + "LuxembourgConsolidationApplicabilityDateFact) -> "
                + N + "LuxembourgConsolidationApplicabilityDateFact",
                "constructor public instance " + N + "LuxembourgConsolidationApplicabilityDateFact::.ctor("
                + N + "LuxembourgConsolidationApplicabilityDatePredicate, System.String, System.String, "
                + Core + "SourceArtifactRef, System.Nullable<" + N + "LuxembourgAssertionPredicate>) -> "
                + N + "LuxembourgConsolidationApplicabilityDateFact",
                "method public instance " + N + "LuxembourgConsolidationApplicabilityDateFact::<Clone>$() -> "
                + N + "LuxembourgConsolidationApplicabilityDateFact",
            },
            ConstructionSurface.Of(typeof(LuxembourgConsolidationApplicabilityDateFact)).ToArray());

        // Fold-in: paired the way the sibling Luxembourg pin file pairs every Of pin with a
        // ProducersIn assertion. Nothing else in Contracts hands out a consolidation-applicability
        // date fact.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgConsolidationApplicabilityDateFact).Assembly,
                typeof(LuxembourgConsolidationApplicabilityDateFact),
                true).ToArray(),
            "something in Contracts now hands out a consolidation-applicability date fact it did " +
            "not have to construct");
    }

    [TestMethod]
    public void AnAssertionFactDispositionHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgAssertionFactDisposition::.ctor("
                + N + "LuxembourgAssertionFactDisposition) -> " + N + "LuxembourgAssertionFactDisposition",
                "constructor public instance " + N + "LuxembourgAssertionFactDisposition::.ctor("
                + N + "LuxembourgAssertionPredicate, " + N + "LuxembourgAssertionFactKind, "
                + Core + "SourceArtifactRef) -> " + N + "LuxembourgAssertionFactDisposition",
                "method public instance " + N + "LuxembourgAssertionFactDisposition::<Clone>$() -> "
                + N + "LuxembourgAssertionFactDisposition",
            },
            ConstructionSurface.Of(typeof(LuxembourgAssertionFactDisposition)).ToArray());

        // Fold-in: paired the way the sibling Luxembourg pin file pairs every Of pin with a
        // ProducersIn assertion. Nothing else in Contracts hands out an assertion-fact disposition.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgAssertionFactDisposition).Assembly,
                typeof(LuxembourgAssertionFactDisposition),
                true).ToArray(),
            "something in Contracts now hands out an assertion-fact disposition it did not have to construct");
    }

    private static SourceArtifactRef Evidence(string digitPair) =>
        new(
            "urn:uuid:00000000-0000-4000-8000-0000000000" + digitPair,
            new string(digitPair[0], 64));

    private static string EvidenceRefJson(string digitPair) =>
        "{\"resource_id\":\"urn:uuid:00000000-0000-4000-8000-0000000000" + digitPair
        + "\",\"sha256\":\"" + new string(digitPair[0], 64) + "\"}";

    private static void AssertTokens<TEnum>(params string[] expected)
        where TEnum : struct, Enum
    {
        var members = Enum.GetValues<TEnum>();
        Assert.AreEqual(
            expected.Length,
            members.Length,
            $"{typeof(TEnum).Name} has {members.Length} members but {expected.Length} are pinned");
        for (var index = 0; index < members.Length; index++)
        {
            Assert.AreEqual(
                "\"" + expected[index] + "\"",
                ContractJson.Serialize(members[index]),
                $"{typeof(TEnum).Name}.{members[index]} does not carry its pinned token");
        }
    }

    private static void AssertRoundTrip<TEnum>()
        where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var json = ContractJson.Serialize(value);
            Assert.AreEqual(
                value,
                ContractJson.Deserialize<TEnum>(json),
                $"{typeof(TEnum).Name}.{value} did not round-trip through {json}");
        }
    }

    private static void AssertScopeDrift<TEnum>(string hostile)
        where TEnum : struct, Enum
    {
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<TEnum>(JsonSerializer.Serialize(hostile)),
            $"{typeof(TEnum).Name} accepted the unknown token {hostile}");
    }
}
