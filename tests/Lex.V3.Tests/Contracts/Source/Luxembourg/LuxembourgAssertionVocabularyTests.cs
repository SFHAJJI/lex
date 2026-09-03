using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
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
/// </summary>
[TestClass]
public sealed class LuxembourgAssertionVocabularyTests
{
    [TestMethod]
    public void TheAssertionVocabularyHasExactlyTwentySixMembers()
    {
        Assert.AreEqual(26, LuxembourgAssertionVocabulary.Predicates.Count);
        Assert.AreEqual(2, LuxembourgAssertionVocabulary.ActForceDatePredicates.Count);
        Assert.AreEqual(2, LuxembourgAssertionVocabulary.ConsolidationApplicabilityDatePredicates.Count);
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
        AssertTokens<LuxembourgActForceStatus>(
            "in-force", "no-longer-in-force", "not-yet-in-force", "no-longer-in-force-implicit");
        AssertTokens<LuxembourgConsolidationApplicabilityStatus>(
            "applicable", "not-applicable", "not-yet-applicable");
    }

    [TestMethod]
    public void EveryTokenRoundTripsToItsOwnMember()
    {
        AssertRoundTrip<LuxembourgAssertionPredicate>();
        AssertRoundTrip<LuxembourgActForceDatePredicate>();
        AssertRoundTrip<LuxembourgConsolidationApplicabilityDatePredicate>();
        AssertRoundTrip<LuxembourgActForceStatus>();
        AssertRoundTrip<LuxembourgConsolidationApplicabilityStatus>();
        AssertRoundTrip<LuxembourgAssertionFactKind>();
    }

    [TestMethod]
    public void UnknownVocabularyFailsClosedInEveryClosedSet()
    {
        AssertScopeDrift<LuxembourgAssertionPredicate>("dateEntryIntoForce");
        AssertScopeDrift<LuxembourgActForceDatePredicate>("dateApplicability");
        AssertScopeDrift<LuxembourgConsolidationApplicabilityDatePredicate>("dateEntryInForce");
        AssertScopeDrift<LuxembourgActForceStatus>("applicable");
        AssertScopeDrift<LuxembourgConsolidationApplicabilityStatus>("in-force");
    }

    [TestMethod]
    public void ActForceAndConsolidationApplicabilityStatusVocabulariesShareNoToken()
    {
        var actTokens = Enum.GetValues<LuxembourgActForceStatus>()
            .Select(value => ContractJson.Serialize(value))
            .ToHashSet(StringComparer.Ordinal);
        var consolidationTokens = Enum.GetValues<LuxembourgConsolidationApplicabilityStatus>()
            .Select(value => ContractJson.Serialize(value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsFalse(
            actTokens.Overlaps(consolidationTokens),
            "an Act force-status token collided with a Consolidation applicability-status token");
    }

    [TestMethod]
    public void EveryAssertionPredicateHasAPinnedFactKindDrivenThroughConstruction()
    {
        // The 44-member census requirement, for this file's twenty-six: FactKindOf is total (no
        // default arm; the build fails if a member is missed) and every predicate can build a real
        // disposition carrying that exact pinned kind.
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
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgAssertionFactDisposition>(
                """
                {"predicate":"dateApplicability","fact_kind":"act_force","evidence_ref":{"resource_id":"urn:uuid:00000000-0000-4000-8000-000000000029","sha256":"9999999999999999999999999999999999999999999999999999999999999999"}}
                """));
    }

    private static SourceArtifactRef Evidence(string digitPair) =>
        new(
            "urn:uuid:00000000-0000-4000-8000-0000000000" + digitPair,
            new string(digitPair[0], 64));

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
