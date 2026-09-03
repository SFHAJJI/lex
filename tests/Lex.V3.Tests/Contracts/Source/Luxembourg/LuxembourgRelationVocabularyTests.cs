using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The Luxembourg relation-predicate vocabulary: Decision 65's closed eighteen, with Decision 64
/// per-family acquisition state and the Candidate 5 R4 rule that an inverse is derived only from a
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
    private const string Core = "Lex.V3.Contracts.Source.Core.";

    [TestMethod]
    public void TheRelationVocabularyHasExactlyEighteenMembers()
    {
        Assert.AreEqual(18, LuxembourgRelationVocabulary.Predicates.Count);
        Assert.AreEqual(2, LuxembourgRelationVocabulary.Authorities.Count);
        Assert.AreEqual(4, LuxembourgRelationVocabulary.AcquisitionStates.Count);
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
        AssertTokens<LuxembourgRelationAcquisitionState>(
            "unacquired", "incomplete", "uncertain", "complete");
    }

    [TestMethod]
    public void EveryTokenRoundTripsToItsOwnMember()
    {
        AssertRoundTrip<LuxembourgRelationPredicate>();
        AssertRoundTrip<LuxembourgRelationAuthority>();
        AssertRoundTrip<LuxembourgRelationAcquisitionState>();
    }

    [TestMethod]
    public void UnknownVocabularyFailsClosedInEveryClosedSet()
    {
        // A plausible neighbour of a real token, not obvious nonsense.
        AssertScopeDrift<LuxembourgRelationPredicate>("modifiedBy");
        AssertScopeDrift<LuxembourgRelationAuthority>("ontology_authorized_inverse");
        AssertScopeDrift<LuxembourgRelationAcquisitionState>("partial");
    }

    [TestMethod]
    public void EveryRelationPredicateHasACensusEntryDrivenThroughConstruction()
    {
        // The census requirement, for this file's eighteen: every predicate must be usable to
        // build a real disposition, not merely be a name in an enum nobody constructs.
        foreach (var family in LuxembourgRelationVocabulary.Predicates)
        {
            var unacquired = new LuxembourgRelationFamilyDisposition(
                family,
                LuxembourgRelationAuthority.PublisherAsserted,
                LuxembourgRelationAcquisitionState.Unacquired,
                completionEvidenceRef: null,
                inboundView: null);
            Assert.AreEqual(family, unacquired.Family);

            var complete = new LuxembourgRelationFamilyDisposition(
                family,
                LuxembourgRelationAuthority.PublisherAsserted,
                LuxembourgRelationAcquisitionState.Complete,
                Evidence("01"),
                inboundView: null);
            Assert.AreEqual(LuxembourgRelationAcquisitionState.Complete, complete.Acquisition);
        }
    }

    [TestMethod]
    public void IncompleteAndUncertainAcquisitionStatesConstructSuccessfully()
    {
        // Fold-in: every acquisition state must be driven through a successful, real
        // construction, not merely exist as an enum member. Unacquired and Complete are already
        // exercised above; this is Incomplete and Uncertain.
        var incomplete = new LuxembourgRelationFamilyDisposition(
            LuxembourgRelationPredicate.Repeals,
            LuxembourgRelationAuthority.PublisherAsserted,
            LuxembourgRelationAcquisitionState.Incomplete,
            completionEvidenceRef: null,
            inboundView: null);
        Assert.AreEqual(LuxembourgRelationAcquisitionState.Incomplete, incomplete.Acquisition);

        var uncertain = new LuxembourgRelationFamilyDisposition(
            LuxembourgRelationPredicate.Repeals,
            LuxembourgRelationAuthority.PublisherAsserted,
            LuxembourgRelationAcquisitionState.Uncertain,
            completionEvidenceRef: null,
            inboundView: null);
        Assert.AreEqual(LuxembourgRelationAcquisitionState.Uncertain, uncertain.Acquisition);
    }

    [TestMethod]
    public void CitesCanCarryALocallyComputedInboundViewNamingCitedBy()
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

        var disposition = new LuxembourgRelationFamilyDisposition(
            LuxembourgRelationPredicate.Cites,
            LuxembourgRelationAuthority.LocalInboundView,
            LuxembourgRelationAcquisitionState.Unacquired,
            completionEvidenceRef: null,
            inbound);
        Assert.AreEqual("cited_by", disposition.InboundView!.InverseLabel);
        Assert.AreEqual(LuxembourgRelationPredicate.Cites, disposition.InboundView!.DerivedFrom);
    }

    [TestMethod]
    public void AnyOtherFamilyCanAlsoCarryALocallyComputedInboundViewWithNoLabelRequired()
    {
        // "Like every other family": an inbound view is optional, and no family other than Cites
        // has a dossier-named label, so LocalInboundView authority constructs successfully with no
        // view attached at all.
        var disposition = new LuxembourgRelationFamilyDisposition(
            LuxembourgRelationPredicate.Modifies,
            LuxembourgRelationAuthority.LocalInboundView,
            LuxembourgRelationAcquisitionState.Unacquired,
            completionEvidenceRef: null,
            inboundView: null);
        Assert.IsNull(disposition.InboundView);
    }

    [TestMethod]
    public void AMismatchedInboundViewDerivedFromIsRefused()
    {
        // R4: "each derived inverse is exactly one transpose with a derived_from edge." An inbound
        // view built for Cites cannot be attached to a disposition describing a different family.
        var citesView = new LuxembourgLocalInboundView(LuxembourgRelationPredicate.Cites, "cited_by");
        foreach (var family in LuxembourgRelationVocabulary.Predicates)
        {
            if (family == LuxembourgRelationPredicate.Cites)
            {
                continue;
            }

            var thrown = Assert.ThrowsExactly<ArgumentException>(
                () => new LuxembourgRelationFamilyDisposition(
                    family,
                    LuxembourgRelationAuthority.LocalInboundView,
                    LuxembourgRelationAcquisitionState.Unacquired,
                    completionEvidenceRef: null,
                    citesView),
                $"{family} accepted an inbound view derived from a different family");
            StringAssert.Contains(thrown.Message, "does not match");
        }
    }

    [TestMethod]
    public void AMismatchedInboundViewDerivedFromIsRefusedOnTheWireToo()
    {
        // The constructor guard must hold on the wire path too.
        var thrown = Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgRelationFamilyDisposition>(
                """
                {"family":"modifies","authority":"local_inbound_view","acquisition":"unacquired","completion_evidence_ref":null,"inbound_view":{"derived_from":"cites","inverse_label":"cited_by"}}
                """));
        Assert.IsInstanceOfType<ArgumentException>(thrown.InnerException);
        StringAssert.Contains(thrown.InnerException!.Message, "does not match");
    }

    [TestMethod]
    public void AnInboundViewCannotBeAttachedToPublisherAssertedAuthority()
    {
        var citesView = new LuxembourgLocalInboundView(LuxembourgRelationPredicate.Cites, "cited_by");
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => new LuxembourgRelationFamilyDisposition(
                LuxembourgRelationPredicate.Cites,
                LuxembourgRelationAuthority.PublisherAsserted,
                LuxembourgRelationAcquisitionState.Unacquired,
                completionEvidenceRef: null,
                citesView));
        StringAssert.Contains(thrown.Message, "Only a locally computed inbound view authority");
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
                inboundView: null));
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
                    inboundView: null),
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
                inboundView: null));
    }

    [TestMethod]
    public void ADispositionRoundTripsAndRefusesAnUnknownFamily()
    {
        var original = new LuxembourgRelationFamilyDisposition(
            LuxembourgRelationPredicate.BasedOn,
            LuxembourgRelationAuthority.PublisherAsserted,
            LuxembourgRelationAcquisitionState.Unacquired,
            completionEvidenceRef: null,
            inboundView: null);

        var json = ContractJson.Serialize(original);
        StringAssert.Contains(json, "basedOn");
        StringAssert.Contains(json, "publisher_asserted");
        StringAssert.Contains(json, "unacquired");

        var restored = ContractJson.Deserialize<LuxembourgRelationFamilyDisposition>(json);
        Assert.AreEqual(original, restored);

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgRelationFamilyDisposition>(
                """
                {"family":"modifiedBy","authority":"publisher_asserted","acquisition":"unacquired","completion_evidence_ref":null,"inbound_view":null}
                """));
    }

    [TestMethod]
    public void ATypedInvariantSurvivesDeserialisation()
    {
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgRelationFamilyDisposition>(
                """
                {"family":"repeals","authority":"publisher_asserted","acquisition":"complete","completion_evidence_ref":null,"inbound_view":null}
                """));

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<LuxembourgRelationFamilyDisposition>(
                """
                {"family":"modifies","authority":"local_inbound_view","acquisition":"complete","completion_evidence_ref":null,"inbound_view":null}
                """));
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

        // Fold-in, paired the way the sibling Luxembourg pin file
        // (LuxembourgConstructionSurfaceTests.cs) pairs every ConstructionSurface.Of pin with a
        // ProducersIn assertion: the disposition's own InboundView property (and its compiler-
        // generated backing field) is the one place elsewhere in Contracts that hands out an
        // already-constructed view, and it only ever re-exposes a view this type's own constructor
        // already checked.
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "LuxembourgRelationFamilyDisposition::<InboundView>k__BackingField -> "
                + N + "LuxembourgLocalInboundView",
                "property public instance " + N + "LuxembourgRelationFamilyDisposition::InboundView() -> "
                + N + "LuxembourgLocalInboundView",
            },
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgLocalInboundView).Assembly,
                typeof(LuxembourgLocalInboundView),
                true).ToArray(),
            "something other than the disposition that already validated a view now hands one out");
    }

    [TestMethod]
    public void ARelationFamilyDispositionHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgRelationFamilyDisposition::.ctor("
                + N + "LuxembourgRelationFamilyDisposition) -> " + N + "LuxembourgRelationFamilyDisposition",
                "constructor public instance " + N + "LuxembourgRelationFamilyDisposition::.ctor("
                + N + "LuxembourgRelationPredicate, " + N + "LuxembourgRelationAuthority, "
                + N + "LuxembourgRelationAcquisitionState, " + Core + "SourceArtifactRef, "
                + N + "LuxembourgLocalInboundView) -> " + N + "LuxembourgRelationFamilyDisposition",
                "method public instance " + N + "LuxembourgRelationFamilyDisposition::<Clone>$() -> "
                + N + "LuxembourgRelationFamilyDisposition",
            },
            ConstructionSurface.Of(typeof(LuxembourgRelationFamilyDisposition)).ToArray());

        // Fold-in: paired the way the sibling Luxembourg pin file pairs every Of pin with a
        // ProducersIn assertion. Nothing else in Contracts hands out a disposition.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgRelationFamilyDisposition).Assembly,
                typeof(LuxembourgRelationFamilyDisposition),
                true).ToArray(),
            "something in Contracts now hands out a disposition it did not have to construct");
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
