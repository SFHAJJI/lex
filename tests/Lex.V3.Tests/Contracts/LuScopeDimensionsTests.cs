using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class LuScopeDimensionsTests
{
    [TestMethod]
    public void TerminalStateWireVocabularyIsExact()
    {
        AssertWireValue(LuScopeTerminalState.AcceptedMetadata, "accepted_metadata");
        AssertWireValue(LuScopeTerminalState.AcceptedCandidate, "accepted_candidate");
        AssertWireValue(LuScopeTerminalState.Point, "point");
        AssertWireValue(LuScopeTerminalState.NeverIngest, "never_ingest");
        AssertWireValue(LuScopeTerminalState.TypedQuarantine, "typed_quarantine");
        AssertWireValue(LuScopeTerminalState.MissingPublisherValue, "missing_publisher_value");
        AssertWireValue(LuScopeTerminalState.NotApplicable, "not_applicable");

        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeTerminalState>("\"ACCEPTED_METADATA\""));
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeTerminalState>("\"accepted_metadata \""));
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeTerminalState>("\"answer\""));
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeTerminalState>("0"));
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Serialize((LuScopeTerminalState)999));
    }

    [TestMethod]
    public void DispositionRoundTripHasExactlyFourRequiredMembers()
    {
        var disposition = Disposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata",
            new[] { "evidence-1", "evidence-2" });

        var json = ContractJson.Serialize(disposition);
        var node = JsonNode.Parse(json)!.AsObject();

        CollectionAssert.AreEquivalent(
            new[] { "state", "reason_code", "rule_id", "evidence_ids" },
            node.Select(static member => member.Key).ToArray());
        var roundTrip = ContractJson.Deserialize<LuScopeDimensionDisposition>(json);
        Assert.AreEqual(disposition.State, roundTrip.State);
        Assert.AreEqual(disposition.ReasonCode, roundTrip.ReasonCode);
        Assert.AreEqual(disposition.RuleId, roundTrip.RuleId);
        CollectionAssert.AreEqual(disposition.EvidenceIds.ToArray(), roundTrip.EvidenceIds.ToArray());

        node["unexpected_member"] = "accepted";
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeDimensionDisposition>(node.ToJsonString()));
    }

    [TestMethod]
    [DataRow("state")]
    [DataRow("reason_code")]
    [DataRow("rule_id")]
    [DataRow("evidence_ids")]
    public void DispositionRejectsEveryMissingMember(string member)
    {
        var node = JsonNode.Parse(ContractJson.Serialize(Disposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata")))!.AsObject();
        node.Remove(member);

        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeDimensionDisposition>(node.ToJsonString()));
    }

    [TestMethod]
    [DataRow("state")]
    [DataRow("reason_code")]
    [DataRow("rule_id")]
    [DataRow("evidence_ids")]
    public void DispositionRejectsEveryNullMember(string member)
    {
        var node = JsonNode.Parse(ContractJson.Serialize(Disposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata")))!.AsObject();
        node[member] = null;
        Assert.IsTrue(node.ContainsKey(member));

        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeDimensionDisposition>(node.ToJsonString()));
    }

    [TestMethod]
    public void DispositionPreservesStrictNonblankEvidenceOrder()
    {
        var evidence = new List<string> { "evidence-1", "evidence-2", "évidence-3" };
        var disposition = Disposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata",
            evidence);

        evidence[0] = "changed-after-construction";

        CollectionAssert.AreEqual(
            new[] { "evidence-1", "evidence-2", "évidence-3" },
            disposition.EvidenceIds.ToArray());
        Assert.HasCount(0, Disposition(
            LuScopeTerminalState.Point,
            "point_missing_prerequisite").EvidenceIds);
        Assert.ThrowsExactly<ArgumentException>(() => Disposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata",
            new[] { "evidence-2", "evidence-1" }));
        Assert.ThrowsExactly<ArgumentException>(() => Disposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata",
            new[] { "evidence-1", "evidence-1" }));
        Assert.ThrowsExactly<ArgumentException>(() => Disposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata",
            new string[] { null! }));
        Assert.ThrowsExactly<ArgumentException>(() => Disposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata",
            new[] { " " }));
    }

    [TestMethod]
    public void DispositionRequiresExplicitCodesAndNotApplicableReason()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new LuScopeDimensionDisposition(
            LuScopeTerminalState.AcceptedMetadata,
            string.Empty,
            "record-1",
            Array.Empty<string>()));
        Assert.ThrowsExactly<ArgumentException>(() => new LuScopeDimensionDisposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata",
            string.Empty,
            Array.Empty<string>()));
        Assert.ThrowsExactly<ArgumentException>(() => new LuScopeDimensionDisposition(
            LuScopeTerminalState.AcceptedMetadata,
            "\t",
            "record-1",
            Array.Empty<string>()));
        Assert.ThrowsExactly<ArgumentException>(() => new LuScopeDimensionDisposition(
            LuScopeTerminalState.AcceptedMetadata,
            "accepted_bounded_metadata",
            " ",
            Array.Empty<string>()));
        Assert.ThrowsExactly<ArgumentException>(() => Disposition(
            LuScopeTerminalState.NotApplicable,
            "missing_assertion"));
        Assert.ThrowsExactly<ArgumentException>(() => Disposition(
            LuScopeTerminalState.NotApplicable,
            "not_applicable_"));
        Assert.ThrowsExactly<ArgumentException>(() => Disposition(
            LuScopeTerminalState.NotApplicable,
            "not_applicable_ "));
        Assert.ThrowsExactly<ArgumentException>(() => Disposition(
            LuScopeTerminalState.NotApplicable,
            "not_applicable_\t"));
        Assert.ThrowsExactly<ArgumentException>(() => Disposition(
            LuScopeTerminalState.AcceptedMetadata,
            "not_applicable_out_of_scope"));

        var notApplicable = Disposition(
            LuScopeTerminalState.NotApplicable,
            "not_applicable_no_assertion");
        Assert.AreEqual("not_applicable_no_assertion", notApplicable.ReasonCode);
    }

    [TestMethod]
    public void DimensionSetIsClosedCompleteAndIndependent()
    {
        var dimensions = CompleteDimensions();
        var json = ContractJson.Serialize(dimensions);
        var node = JsonNode.Parse(json)!.AsObject();

        CollectionAssert.AreEquivalent(
            DimensionMembers,
            node.Select(static member => member.Key).ToArray());
        var roundTrip = ContractJson.Deserialize<LuScopeDimensions>(json);
        Assert.AreEqual(LuScopeTerminalState.AcceptedMetadata, roundTrip.Record.State);
        Assert.AreEqual(LuScopeTerminalState.NeverIngest, roundTrip.Body.State);
        Assert.AreEqual(LuScopeTerminalState.AcceptedMetadata, roundTrip.Relation.State);

        node["aggregate_verdict"] = "accepted";
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeDimensions>(node.ToJsonString()));
    }

    [TestMethod]
    [DynamicData(nameof(DimensionMemberCases))]
    public void DimensionSetRejectsEveryMissingDimension(string member)
    {
        var node = JsonNode.Parse(ContractJson.Serialize(CompleteDimensions()))!.AsObject();
        node.Remove(member);

        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeDimensions>(node.ToJsonString()));
    }

    [TestMethod]
    [DynamicData(nameof(DimensionMemberCases))]
    public void DimensionSetRejectsEveryNullDimension(string member)
    {
        var node = JsonNode.Parse(ContractJson.Serialize(CompleteDimensions()))!.AsObject();
        node[member] = null;
        Assert.IsTrue(node.ContainsKey(member));

        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<LuScopeDimensions>(node.ToJsonString()));
    }

    public static IEnumerable<object[]> DimensionMemberCases() =>
        DimensionMembers.Select(static member => new object[] { member });

    private static readonly string[] DimensionMembers =
    {
        "record",
        "body",
        "relation",
        "supporting_document",
        "publication_family",
        "language",
        "format",
        "authenticity",
        "rights",
        "transport",
    };

    private static LuScopeDimensions CompleteDimensions() => new(
        Disposition(LuScopeTerminalState.AcceptedMetadata, "accepted_bounded_metadata"),
        Disposition(LuScopeTerminalState.NeverIngest, "never_ingest_robots_denial"),
        Disposition(LuScopeTerminalState.AcceptedMetadata, "accepted_asserted_relation"),
        Disposition(LuScopeTerminalState.Point, "point_unclassified_support"),
        Disposition(LuScopeTerminalState.AcceptedCandidate, "accepted_exact_family"),
        Disposition(LuScopeTerminalState.MissingPublisherValue, "missing_language"),
        Disposition(LuScopeTerminalState.MissingPublisherValue, "missing_format"),
        Disposition(LuScopeTerminalState.Point, "point_authenticity_not_asserted"),
        Disposition(LuScopeTerminalState.TypedQuarantine, "typed_quarantine_rights_conflict"),
        Disposition(LuScopeTerminalState.NeverIngest, "never_ingest_transport_denial"));

    private static LuScopeDimensionDisposition Disposition(
        LuScopeTerminalState state,
        string reasonCode,
        IReadOnlyList<string>? evidenceIds = null) =>
        new(state, reasonCode, "rule-1", evidenceIds ?? Array.Empty<string>());

    private static void AssertWireValue(LuScopeTerminalState state, string wireValue)
    {
        Assert.AreEqual($"\"{wireValue}\"", ContractJson.Serialize(state));
        Assert.AreEqual(state, ContractJson.Deserialize<LuScopeTerminalState>($"\"{wireValue}\""));
    }
}
