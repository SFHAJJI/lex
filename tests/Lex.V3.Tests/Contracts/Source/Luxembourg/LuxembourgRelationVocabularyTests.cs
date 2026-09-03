using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The Luxembourg relation-predicate vocabulary: Decision 65's closed eighteen, with Decision 64
/// per-family acquisition state and the Candidate 5 R4 inverse-only-from-pinned-ontology fence.
///
/// Every cardinality and token below is transcribed from Decision 65's text and from the
/// already-merged <c>VerifiedLuxembourgSourceProfile.BuildRequiredVocabulary</c>'s
/// <c>RelationPredicate</c> rows, not computed from the enum under test: asserting
/// <c>Enum.GetValues().Length == LuxembourgRelationVocabulary.Predicates.Count</c> would pass for
/// any pair of equal wrong numbers.
/// </summary>
[TestClass]
public sealed class LuxembourgRelationVocabularyTests
{
    [TestMethod]
    public void TheRelationVocabularyHasExactlyEighteenMembers()
    {
        Assert.AreEqual(18, LuxembourgRelationVocabulary.Predicates.Count);
        Assert.AreEqual(3, LuxembourgRelationVocabulary.Authorities.Count);
        Assert.AreEqual(4, LuxembourgRelationVocabulary.AcquisitionStates.Count);
        Assert.AreEqual(2, LuxembourgRelationVocabulary.TargetStates.Count);
        Assert.AreEqual(1, LuxembourgRelationVocabulary.InvertiblePredicates.Count);
    }

    [TestMethod]
    public void EveryRelationPredicateSerialisesToItsExactPublisherToken()
    {
        // Every one of the eighteen, by hand, in Decision 65's declaration order (Candidate 6's
        // seventeen, then cites).
        AssertTokens<LuxembourgRelationPredicate>(
            "modifies", "repeals", "rectifies", "basedOn", "transposes", "modifiedTempBy",
            "hasIndirectImpact", "legalAnalysisHasLegalResourceImpact",
            "impactFromLegalResource", "impactToLegalResource", "impactToExpression",
            "legalResourceImpactHasDateEntryInForce", "legalResourceImpactHasType",
            "impactConsolidatedBy", "impactConsolidatedByExpression", "basicAct", "consolidates",
            "cites");
        AssertTokens<LuxembourgRelationAuthority>(
            "publisher_asserted", "ontology_authorized_inverse", "local_inbound_view");
        AssertTokens<LuxembourgRelationAcquisitionState>(
            "unacquired", "incomplete", "uncertain", "complete");
        AssertTokens<LuxembourgRelationTargetState>("held", "identified_but_unheld");
        AssertTokens<LuxembourgInvertibleRelationPredicate>("cites");
    }

    [TestMethod]
    public void EveryTokenRoundTripsToItsOwnMember()
    {
        AssertRoundTrip<LuxembourgRelationPredicate>();
        AssertRoundTrip<LuxembourgRelationAuthority>();
        AssertRoundTrip<LuxembourgRelationAcquisitionState>();
        AssertRoundTrip<LuxembourgRelationTargetState>();
        AssertRoundTrip<LuxembourgInvertibleRelationPredicate>();
    }

    [TestMethod]
    public void UnknownVocabularyFailsClosedInEveryClosedSet()
    {
        // A plausible neighbour of a real token, not obvious nonsense.
        AssertScopeDrift<LuxembourgRelationPredicate>("modifiedBy");
        AssertScopeDrift<LuxembourgRelationAuthority>("derived");
        AssertScopeDrift<LuxembourgRelationAcquisitionState>("partial");
        AssertScopeDrift<LuxembourgRelationTargetState>("unheld");
        // The exact near-miss R4 exists to prevent: a caller inventing "modifiedBy" as though it
        // were the pinned inverse of "modifies".
        AssertScopeDrift<LuxembourgInvertibleRelationPredicate>("modifies");
    }

    [TestMethod]
    public void EveryRelationPredicateHasACensusEntryDrivenThroughConstruction()
    {
        // The 44-member census requirement, for this file's eighteen: every predicate must be
        // usable to build a real disposition, not merely be a name in an enum nobody constructs.
        foreach (var family in LuxembourgRelationVocabulary.Predicates)
        {
            var unacquired = new LuxembourgRelationFamilyDisposition(
                family,
                LuxembourgRelationAuthority.PublisherAsserted,
                LuxembourgRelationAcquisitionState.Unacquired,
                completionEvidenceRef: null,
                ontologyInverse: null);
            Assert.AreEqual(family, unacquired.Family);

            var complete = new LuxembourgRelationFamilyDisposition(
                family,
                LuxembourgRelationAuthority.PublisherAsserted,
                LuxembourgRelationAcquisitionState.Complete,
                Evidence("01"),
                ontologyInverse: null);
            Assert.AreEqual(LuxembourgRelationAcquisitionState.Complete, complete.Acquisition);
        }
    }

    [TestMethod]
    public void OnlyCitesCarriesAPinnedOntologyAuthorizedInverse()
    {
        var inverse = new LuxembourgOntologyAuthorizedInverse(
            LuxembourgInvertibleRelationPredicate.Cites,
            OntologyMember("cites_inverse"));
        Assert.AreEqual("cited_by", inverse.InverseLabel);
        Assert.AreEqual(LuxembourgRelationPredicate.Cites, inverse.UnderlyingPredicate);

        var disposition = new LuxembourgRelationFamilyDisposition(
            LuxembourgRelationPredicate.Cites,
            LuxembourgRelationAuthority.OntologyAuthorizedInverse,
            LuxembourgRelationAcquisitionState.Unacquired,
            completionEvidenceRef: null,
            inverse);
        Assert.AreEqual("cited_by", disposition.OntologyInverse!.InverseLabel);
    }

    [TestMethod]
    public void NoOtherPredicateCanClaimAnOntologyAuthorizedInverse()
    {
        // The structural fence: LuxembourgOntologyAuthorizedInverse can only be built from
        // LuxembourgInvertibleRelationPredicate, whose only member is Cites, so every one of the
        // other seventeen families is refused here rather than merely undocumented as invertible.
        // Cites itself is excluded from this loop; it is the one predicate that must succeed, and
        // its own construction is proven above.
        var pinnedInverse = new LuxembourgOntologyAuthorizedInverse(
            LuxembourgInvertibleRelationPredicate.Cites,
            OntologyMember("cites_inverse"));

        foreach (var family in LuxembourgRelationVocabulary.Predicates)
        {
            if (family == LuxembourgRelationPredicate.Cites)
            {
                continue;
            }

            var thrown = Assert.ThrowsExactly<ArgumentException>(
                () => new LuxembourgRelationFamilyDisposition(
                    family,
                    LuxembourgRelationAuthority.OntologyAuthorizedInverse,
                    LuxembourgRelationAcquisitionState.Unacquired,
                    completionEvidenceRef: null,
                    pinnedInverse),
                $"{family} was allowed to claim the cites inverse");
            StringAssert.Contains(thrown.Message, "does not authorize an inverse for");
        }
    }

    [TestMethod]
    public void ConsolidatesNeverCarriesAnOntologyAuthorizedInverseEvenWithoutOne()
    {
        // Decision 58(b): consolidates keeps asserted direction, never amendment attribution.
        // Claiming OntologyAuthorizedInverse authority for it must fail even before any inverse
        // object is supplied, because the missing-inverse guard fires first.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LuxembourgRelationFamilyDisposition(
                LuxembourgRelationPredicate.Consolidates,
                LuxembourgRelationAuthority.OntologyAuthorizedInverse,
                LuxembourgRelationAcquisitionState.Unacquired,
                completionEvidenceRef: null,
                ontologyInverse: null));
    }

    [TestMethod]
    public void AnOntologyInverseCannotBeAttachedToAnyOtherAuthority()
    {
        var pinnedInverse = new LuxembourgOntologyAuthorizedInverse(
            LuxembourgInvertibleRelationPredicate.Cites,
            OntologyMember("cites_inverse"));

        foreach (var authority in new[]
                 {
                     LuxembourgRelationAuthority.PublisherAsserted,
                     LuxembourgRelationAuthority.LocalInboundView,
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new LuxembourgRelationFamilyDisposition(
                    LuxembourgRelationPredicate.Cites,
                    authority,
                    LuxembourgRelationAcquisitionState.Unacquired,
                    completionEvidenceRef: null,
                    pinnedInverse),
                $"{authority} was allowed to carry a pinned inverse");
        }
    }

    [TestMethod]
    public void ACompletedFamilyWithoutEvidenceCannotBeConstructed()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LuxembourgRelationFamilyDisposition(
                LuxembourgRelationPredicate.Repeals,
                LuxembourgRelationAuthority.PublisherAsserted,
                LuxembourgRelationAcquisitionState.Complete,
                completionEvidenceRef: null,
                ontologyInverse: null));
    }

    [TestMethod]
    public void EvidenceCannotBeAttachedToAnUnfinishedAcquisition()
    {
        foreach (var state in new[]
                 {
                     LuxembourgRelationAcquisitionState.Unacquired,
                     LuxembourgRelationAcquisitionState.Incomplete,
                     LuxembourgRelationAcquisitionState.Uncertain,
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new LuxembourgRelationFamilyDisposition(
                    LuxembourgRelationPredicate.Repeals,
                    LuxembourgRelationAuthority.PublisherAsserted,
                    state,
                    Evidence("02"),
                    ontologyInverse: null),
                $"{state} accepted completion evidence");
        }
    }

    [TestMethod]
    public void ALocallyComputedInboundViewIsNeverACompletedPublisherObservation()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgRelationFamilyDisposition(
                LuxembourgRelationPredicate.Modifies,
                LuxembourgRelationAuthority.LocalInboundView,
                LuxembourgRelationAcquisitionState.Complete,
                Evidence("03"),
                ontologyInverse: null));
    }

    [TestMethod]
    public void ADispositionRoundTripsAndRefusesAnUnknownFamily()
    {
        var original = new LuxembourgRelationFamilyDisposition(
            LuxembourgRelationPredicate.BasedOn,
            LuxembourgRelationAuthority.PublisherAsserted,
            LuxembourgRelationAcquisitionState.Unacquired,
            completionEvidenceRef: null,
            ontologyInverse: null);

        var json = ContractJson.Serialize(original);
        StringAssert.Contains(json, "basedOn");
        StringAssert.Contains(json, "publisher_asserted");
        StringAssert.Contains(json, "unacquired");

        var restored = ContractJson.Deserialize<LuxembourgRelationFamilyDisposition>(json);
        Assert.AreEqual(original, restored);

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgRelationFamilyDisposition>(
                """
                {"family":"modifiedBy","authority":"publisher_asserted","acquisition":"unacquired","completion_evidence_ref":null,"ontology_inverse":null}
                """));
    }

    [TestMethod]
    public void ATypedInvariantSurvivesDeserialisation()
    {
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgRelationFamilyDisposition>(
                """
                {"family":"repeals","authority":"publisher_asserted","acquisition":"complete","completion_evidence_ref":null,"ontology_inverse":null}
                """));

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgRelationFamilyDisposition>(
                """
                {"family":"modifies","authority":"local_inbound_view","acquisition":"complete","completion_evidence_ref":null,"ontology_inverse":null}
                """));
    }

    [TestMethod]
    public void InverseLabelsNeverShareAPublisherPredicate()
    {
        // The Stage 2 scope ruling's own addition: "inverse labels never share a publisher
        // predicate." Checked against both the eighteen relation predicates and the twenty-six
        // assertion predicates, because either vocabulary sharing "cited_by" would make it
        // ambiguous which fact a wire document names.
        var relationTokens = LuxembourgRelationVocabulary.Predicates
            .Select(predicate => ContractJson.Serialize(predicate))
            .ToHashSet(StringComparer.Ordinal);
        var assertionTokens = LuxembourgAssertionVocabulary.Predicates
            .Select(predicate => ContractJson.Serialize(predicate))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var predicate in LuxembourgRelationVocabulary.InvertiblePredicates)
        {
            var label = "\"" + LuxembourgRelationOntology.InverseLabel(predicate) + "\"";
            Assert.IsFalse(relationTokens.Contains(label), $"{label} collides with a relation predicate token");
            Assert.IsFalse(assertionTokens.Contains(label), $"{label} collides with an assertion predicate token");
        }
    }

    private static SourceArtifactRef Evidence(string digitPair) =>
        new(
            "urn:uuid:00000000-0000-4000-8000-0000000000" + digitPair,
            new string(digitPair[0], 64));

    private static SourceRegistryMemberRef OntologyMember(string memberKey) =>
        new(Evidence("11"), memberKey);

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
