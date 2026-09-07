using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The Luxembourg relation-predicate vocabulary: Decision 65's closed eighteen and the Candidate
/// 5 R4 rule that an inverse is derived only from a
/// pinned ontology mapping, otherwise it is a generic locally derived inbound view.
///
/// Every cardinality and token below is transcribed from Decision 65's text and from the
/// already-merged <c>VerifiedLuxembourgSourceProfile.BuildRequiredVocabulary</c>'s
/// <c>RelationPredicate</c> rows, not computed from the enum under test: asserting
/// <c>Enum.GetValues().Length == LuxembourgRelationVocabulary.Predicates.Count</c> would pass for
/// any pair of equal wrong numbers.
///
/// <see cref="CrossCheckAgainstTheMergedSourceProfileTests"/> replaces a hand-transcribed
/// cross-check of this vocabulary against <c>VerifiedLuxembourgSourceProfile.RequiredIriVocabulary</c>
/// with one that actually reads that method's live output, so an edit to either list is caught
/// automatically instead of silently drifting.
/// </summary>
[TestClass]
public sealed class LuxembourgRelationVocabularyTests
{
    private const string N = "Lex.V3.Contracts.Source.Luxembourg.";

    [TestMethod]
    public void TheRelationVocabularyHasExactlyEighteenMembers()
    {
        Assert.AreEqual(18, LuxembourgRelationVocabulary.Predicates.Count);
        Assert.AreEqual(2, LuxembourgRelationVocabulary.Authorities.Count);
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
        AssertTokens<LuxembourgRelationAuthority>("publisher_asserted", "local_inbound_view");
    }

    [TestMethod]
    public void EveryTokenRoundTripsToItsOwnMember()
    {
        AssertRoundTrip<LuxembourgRelationPredicate>();
        AssertRoundTrip<LuxembourgRelationAuthority>();
    }

    [TestMethod]
    public void UnknownVocabularyFailsClosedInEveryClosedSet()
    {
        // A plausible neighbour of a real token, not obvious nonsense.
        AssertScopeDrift<LuxembourgRelationPredicate>("modifiedBy");
        AssertScopeDrift<LuxembourgRelationAuthority>("ontology_authorized_inverse");
    }

    [TestMethod]
    public void CitesCanNameALocallyComputedInboundViewAsCitedBy()
    {
        // Objection 1's resolution: a grep of the entire coordination pack found no accepted text
        // that pins a JOLux inverse for cites. The only pinned inverse pair anywhere in the pack is
        // the EU CDM's work_cites_work / work_cited_by_work
        // (coordination/measurements/D1-EU-CDM-ONTOLOGY-IDENTITY-2026-09-01.md), a different
        // ontology entirely. Decision 65's "Cites and Cited-by" dossier requirement is therefore met
        // the way R4 lines 537-554 name for every unpinned predicate: a generic locally derived
        // inbound view, never labelled with a publisher predicate. cited_by carries no special type
        // of its own; it is LuxembourgRelationAuthority.LocalInboundView, exactly like the other
        // seventeen families, optionally naming the family it transposes.
        var inbound = new LuxembourgLocalInboundView(LuxembourgRelationPredicate.Cites, "cited_by");
        Assert.AreEqual(LuxembourgRelationPredicate.Cites, inbound.DerivedFrom);
        Assert.AreEqual("cited_by", inbound.InverseLabel);
    }

    [TestMethod]
    public void ALocalInboundViewIsRefusedWhenItsLabelCollidesWithAPublisherPredicateToken()
    {
        // Fold-in: the inbound label was checked only by RequireIdentifier (bounded printable
        // ASCII), so new LuxembourgLocalInboundView(Cites, "modifies") constructed even though this
        // type's own documentation says a label is never a publisher predicate, and
        // InverseLabelsNeverShareAPublisherPredicate below checked only a single literal
        // ("cited_by") against the two enums rather than driving the refusal. Driven here with one
        // relation-predicate token and one assertion-predicate token, so both vocabularies are
        // actually exercised as rejected labels, not just asserted absent from a hard-coded string.
        var relationTokenThrown = Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgLocalInboundView(LuxembourgRelationPredicate.Cites, "modifies"));
        StringAssert.Contains(relationTokenThrown.Message, "modifies");

        var assertionTokenThrown = Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgLocalInboundView(LuxembourgRelationPredicate.Cites, "dateApplicability"));
        StringAssert.Contains(assertionTokenThrown.Message, "dateApplicability");
    }

    [TestMethod]
    public void InverseLabelsNeverShareAPublisherPredicate()
    {
        // "cited_by" is a local descriptive label, never a publisher predicate. Checked against
        // both the eighteen relation predicates and the twenty-six assertion predicates, because
        // either vocabulary sharing "cited_by" would make it ambiguous which fact a wire document
        // names.
        //
        // Note: "cited_by" also names an unrelated MCP operation id in
        // V3ContractVocabulary.OperationIds (src/Lex.V3.Contracts/V3ContractVocabulary.cs) -- a
        // different vocabulary in a different namespace that happens to spell one of its members
        // the same way. That coincidence is not checked here and carries no relationship to this
        // vocabulary's label.
        var relationTokens = LuxembourgRelationVocabulary.Predicates
            .Select(predicate => ContractJson.Serialize(predicate))
            .ToHashSet(StringComparer.Ordinal);
        var assertionTokens = LuxembourgAssertionVocabulary.Predicates
            .Select(predicate => ContractJson.Serialize(predicate))
            .ToHashSet(StringComparer.Ordinal);

        const string label = "\"cited_by\"";
        Assert.IsFalse(relationTokens.Contains(label), $"{label} collides with a relation predicate token");
        Assert.IsFalse(assertionTokens.Contains(label), $"{label} collides with an assertion predicate token");
    }

    [TestMethod]
    public void CrossCheckAgainstTheMergedSourceProfileTests()
    {
        // Fold-in: replaces a hand-transcribed comment claiming this vocabulary matches
        // VerifiedLuxembourgSourceProfile's own rows with an executed comparison against that
        // profile's real RequiredIriVocabulary output, so an edit to either list is caught
        // automatically instead of silently drifting.
        const string prefix = "http://data.legilux.public.lu/resource/ontology/jolux#";
        var profileLocalNames = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary
            .Where(value => value.Kind == LuxembourgVocabularyKind.RelationPredicate)
            .Select(value =>
            {
                Assert.IsTrue(
                    value.FullIri.StartsWith(prefix, StringComparison.Ordinal),
                    $"{value.FullIri} is not a JOLux local predicate");
                return value.FullIri[prefix.Length..];
            })
            .ToHashSet(StringComparer.Ordinal);

        var enumLocalNames = LuxembourgRelationVocabulary.Predicates
            .Select(predicate => ContractJson.Serialize(predicate).Trim('"'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.AreEqual(
            profileLocalNames.Count,
            enumLocalNames.Count,
            "LuxembourgRelationPredicate and VerifiedLuxembourgSourceProfile.RequiredIriVocabulary " +
            "disagree on how many relation predicates are settled");
        CollectionAssert.AreEquivalent(
            profileLocalNames.ToArray(),
            enumLocalNames.ToArray(),
            "LuxembourgRelationPredicate and VerifiedLuxembourgSourceProfile.RequiredIriVocabulary " +
            "name a different set of relation predicates");
    }

    [TestMethod]
    public void ALocalInboundViewHasExactlyOneCheckedDoor()
    {
        // Transcribed from ConstructionSurface.Of's actual output, per this project's
        // print-then-transcribe technique (see LuxembourgConstructionSurfaceTests.cs's remarks).
        // The static constructor is the type initializer for the fold-in's
        // PublisherPredicateTokens field (built once from both closed vocabularies), the same shape
        // LuxembourgDeliveryObservation already pins in the sibling file for the same reason: a
        // static field initializer.
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgLocalInboundView::.ctor("
                + N + "LuxembourgLocalInboundView) -> " + N + "LuxembourgLocalInboundView",
                "constructor private static " + N + "LuxembourgLocalInboundView::.cctor() -> "
                + N + "LuxembourgLocalInboundView",
                "constructor public instance " + N + "LuxembourgLocalInboundView::.ctor("
                + N + "LuxembourgRelationPredicate, System.String) -> " + N + "LuxembourgLocalInboundView",
                "method public instance " + N + "LuxembourgLocalInboundView::<Clone>$() -> "
                + N + "LuxembourgLocalInboundView",
            },
            ConstructionSurface.Of(typeof(LuxembourgLocalInboundView)).ToArray());

        // Paired with a ProducersIn assertion so a new Contracts producer cannot silently hand out
        // an inbound view without this accepted construction boundary being reconsidered.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgLocalInboundView).Assembly,
                typeof(LuxembourgLocalInboundView),
                true).ToArray(),
            "a new Contracts producer now hands out a local inbound view");
    }

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
