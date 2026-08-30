using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class ContractVocabularyTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void CoreAndCompositionInventoriesAreExact()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "envelope",
                "quote",
                "version_state",
                "timeline",
                "provision_history",
                "diff",
                "relation_edge",
                "classification",
                "refusal",
                "provenance_chain",
                "coverage_report",
            },
            V3ContractVocabulary.CoreObjectTypes.ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "work_record",
                "work_resolution",
                "change_list",
                "resolution_verdict",
                "evidence_bundle",
                "answer_dossier",
                "handoff_card",
            },
            V3ContractVocabulary.CompositionTypes.ToArray());
    }

    [TestMethod]
    public void OperationInventoryIsExactWhileStageZeroCatalogIsEmpty()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "resolve",
                "search",
                "browse",
                "concepts",
                "dossier",
                "manifestation",
                "as_of",
                "as_observed",
                "knowable_on",
                "timeline",
                "article_history",
                "diff",
                "status_on",
                "in_force_on",
                "changes_in_period",
                "relations",
                "classification",
                "cited_by",
                "citation",
                "transposition",
                "provenance",
                "verify",
                "evidence_bundle",
                "events",
                "answer_drift",
                "coverage",
                "ask",
            },
            V3ContractVocabulary.OperationIds.ToArray());

        Assert.AreEqual(V3SchemaIds.PreviewOperationCatalog, PreviewOperationCatalog.StageZero.Schema);
        Assert.HasCount(0, PreviewOperationCatalog.StageZero.Entries);
    }

    [TestMethod]
    public void PublisherContextsPreserveEachPublishersTimelineSemantics()
    {
        var lu = PublisherContext.Create(
            "lu",
            PublisherId.LuLegilux,
            TimelineSemantics.PublisherApplicability,
            Digest);
        var eu = PublisherContext.Create(
            "eu",
            PublisherId.EuEurLex,
            TimelineSemantics.OfficialConsolidationState,
            Digest);

        var contexts = PublisherContextSet.Create(new[] { lu, eu });

        Assert.HasCount(2, contexts);
        Assert.AreEqual(PublisherId.LuLegilux, contexts[0].Publisher);
        Assert.AreEqual(PublisherId.EuEurLex, contexts[1].Publisher);
        Assert.ThrowsExactly<ArgumentException>(() => PublisherContext.Create(
            "false-clock",
            PublisherId.LuLegilux,
            TimelineSemantics.OfficialConsolidationState,
            Digest));
        Assert.ThrowsExactly<ArgumentException>(() => PublisherContextSet.Create(new[] { eu, lu }));
        Assert.ThrowsExactly<ArgumentException>(() => PublisherContextSet.Create(new[] { lu, lu }));
        Assert.ThrowsExactly<ArgumentException>(() => PublisherContextSet.Create(Array.Empty<PublisherContext>()));
    }

    [TestMethod]
    public void RetrievalMetadataOnlyCannotBecomeABodyHoldingState()
    {
        var json = ContractJson.Serialize(new HoldingProbe(
            BodyHoldingState.NotHeld,
            RetrievalOutcome.MetadataOnly));

        StringAssert.Contains(json, "\"body_holding_state\":\"not_held\"");
        StringAssert.Contains(json, "\"retrieval_outcome\":\"metadata_only\"");

        var mutated = json.Replace("\"not_held\"", "\"metadata_only\"", StringComparison.Ordinal);
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HoldingProbe>(mutated));
    }

    [TestMethod]
    public void IdentifierUnknownPayloadIsCompleteAndStrict()
    {
        var refusal = IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/lu/loi/2099/01/01/n1",
            new[] { PublisherId.LuLegilux },
            Array.Empty<HeldRecordCandidate>(),
            new[]
            {
                PublisherSearchAction.Create(
                    PublisherId.LuLegilux,
                    new Uri("https://legilux.public.lu/search")),
            },
            new[] { WhatWouldAnswerAction.CorrectedIdentifier });

        Assert.IsFalse(refusal.AssertsAbsenceOfLaw);
        var json = ContractJson.Serialize(refusal);
        var roundTrip = ContractJson.Deserialize<IdentifierUnknownRefusal>(json);
        Assert.AreEqual(RefusalCode.IdentifierUnknown, roundTrip.Code);

        var unknownMember = JsonNode.Parse(json)!.AsObject();
        unknownMember["details"] = "not allowed";
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<IdentifierUnknownRefusal>(unknownMember.ToJsonString()));

        var missingPayload = JsonNode.Parse(json)!.AsObject();
        missingPayload.Remove("what_would_answer");
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<IdentifierUnknownRefusal>(missingPayload.ToJsonString()));

        Assert.ThrowsExactly<ArgumentException>(() => IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/lu/loi/2099/01/01/n1",
            new[] { PublisherId.LuLegilux },
            Array.Empty<HeldRecordCandidate>(),
            Array.Empty<PublisherSearchAction>(),
            new[] { WhatWouldAnswerAction.CorrectedIdentifier }));
    }

    private sealed record HoldingProbe(
        BodyHoldingState BodyHoldingState,
        RetrievalOutcome RetrievalOutcome);
}
