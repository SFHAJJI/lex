using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

[TestClass]
public sealed class V3EnvelopeTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private readonly V3EnvelopeBuilder _builder = new(V3OperationRegistry.Reviewed);

    [TestMethod]
    public void SuccessIsBoundToRegistryOperationSchemaAndObjectType()
    {
        using var result = JsonDocument.Parse("{\"work_id\":\"lu-legilux:test\"}");
        var envelope = _builder.Success(
            "req_01",
            "resolve",
            SuccessContext(),
            V3Verdicts.Answer,
            "lex-v3-resolve-result/1",
            "work_resolution",
            result.RootElement);

        _builder.VerifyBinding(envelope);
        Assert.AreEqual(V3OperationRegistry.Reviewed.Sha256, envelope.RegistrySha256);
        Assert.AreEqual("work_resolution", envelope.Result!.ObjectType);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, envelope.Context.TimelineSemantics);
        Assert.AreEqual("lu", envelope.Context.Jurisdiction);
        Assert.AreEqual("current", envelope.Context.Freshness.UpstreamHealth);
        Assert.IsNull(envelope.Refusal);
    }

    [TestMethod]
    public void HelpfulRefusalIsBoundToClosedRegistry()
    {
        using var helpful = JsonDocument.Parse(
            "{\"requested_digest\":\"old\",\"current_digest\":\"new\","
            + "\"stable_coordinate\":\"/lu-legilux/test\","
            + "\"current_hash_pinned_url\":\"/lu-legilux/test/date--new\"}");
        var envelope = _builder.Refusal(
            "req_02",
            "resolve",
            RefusalContext(),
            V3OperationRegistry.RefusalSchema,
            "pinned_digest_mismatch",
            helpful.RootElement);

        _builder.VerifyBinding(envelope);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("pinned_digest_mismatch", envelope.Refusal!.Code);
        Assert.IsNull(envelope.Result);
    }

    [TestMethod]
    public void BuilderRejectsUnknownOperationVerdictSchemasObjectTypesAndRefusals()
    {
        using var payload = JsonDocument.Parse("{\"value\":true}");

        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "req", "unknown", SuccessContext(), V3Verdicts.Answer, "lex-v3-unknown-result/1", "work_record", payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "req", "resolve", SuccessContext(), "maybe", "lex-v3-resolve-result/1", "work_resolution", payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "req", "resolve", SuccessContext(), V3Verdicts.Answer, "lex-v3-search-result/1", "work_resolution", payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "req", "resolve", SuccessContext(), V3Verdicts.Answer, "lex-v3-resolve-result/1", "quote", payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "resolve", RefusalContext(), V3OperationRegistry.RefusalSchema, "invented", payload.RootElement));
    }

    [TestMethod]
    public void RefusalRequiresHelpfulObjectPayload()
    {
        using var empty = JsonDocument.Parse("{}");
        using var scalar = JsonDocument.Parse("true");
        using var missingOneRequiredField = JsonDocument.Parse(
            "{\"requested_identifier\":\"unknown\","
            + "\"official_search_actions\":[\"search the official register\"]}");

        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "resolve", RefusalContext(), V3OperationRegistry.RefusalSchema, "identifier_unknown", empty.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "resolve", RefusalContext(), V3OperationRegistry.RefusalSchema, "identifier_unknown", scalar.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "resolve", RefusalContext(), V3OperationRegistry.RefusalSchema,
            "identifier_unknown", missingOneRequiredField.RootElement));
    }

    [TestMethod]
    public void RequestReferenceCannotCarryQueryTextOrClientIdentifiers()
    {
        using var payload = JsonDocument.Parse("{\"work_id\":\"lu-legilux:test\"}");

        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "what is my deadline?",
            "resolve",
            SuccessContext(),
            V3Verdicts.Answer,
            "lex-v3-resolve-result/1",
            "work_resolution",
            payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "client@example.com",
            "resolve",
            SuccessContext(),
            V3Verdicts.Answer,
            "lex-v3-resolve-result/1",
            "work_resolution",
            payload.RootElement));
    }

    [TestMethod]
    public void ContextRejectsPublisherTimelineAndJurisdictionDrift()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new V3EnvelopeContext(
            PublisherId.LuLegilux,
            "success",
            TimelineSemantics.OfficialConsolidationState,
            new V3SnapshotReference("snapshot", Digest),
            "lu",
            false,
            new V3Freshness(DateTimeOffset.UtcNow, "current")));
        Assert.ThrowsExactly<ArgumentException>(() => new V3EnvelopeContext(
            PublisherId.EuEurLex,
            "success",
            TimelineSemantics.OfficialConsolidationState,
            new V3SnapshotReference("snapshot", Digest),
            "lu",
            false,
            new V3Freshness(DateTimeOffset.UtcNow, "current")));
    }

    [TestMethod]
    public void BuilderRejectsStatusAndEnvelopeBranchMismatchInBothDirections()
    {
        using var result = JsonDocument.Parse("{\"work_id\":\"lu-legilux:test\"}");
        using var refusal = JsonDocument.Parse(
            "{\"requested_identifier\":\"unknown\","
            + "\"official_search_actions\":[\"search the official register\"],"
            + "\"what_would_answer\":\"a reviewed identifier mapping\"}");

        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "req", "resolve", RefusalContext(), V3Verdicts.Answer,
            "lex-v3-resolve-result/1", "work_resolution", result.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "resolve", SuccessContext(), V3OperationRegistry.RefusalSchema,
            "identifier_unknown", refusal.RootElement));
    }

    [TestMethod]
    public void FreshnessRejectsUnknownUpstreamHealth()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new V3Freshness(DateTimeOffset.UtcNow, "banana"));
    }

    [TestMethod]
    public void AnchorRefusalRequiresItsExactHelpfulFieldsAndNoFallbackRule()
    {
        using var incomplete = JsonDocument.Parse("{\"requested_anchor\":\"art_1\"}");
        using var wrongRule = JsonDocument.Parse(
            "{\"requested_anchor\":\"art_1\",\"nearest_anchors\":[\"art_1er\"],"
            + "\"do_not_fall_back_to_full_text_search\":false}");
        using var complete = JsonDocument.Parse(
            "{\"requested_anchor\":\"art_1\",\"nearest_anchors\":[\"art_1er\"],"
            + "\"do_not_fall_back_to_full_text_search\":true}");

        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "article_history", RefusalContext(), V3OperationRegistry.RefusalSchema,
            "anchor_not_in_version", incomplete.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "article_history", RefusalContext(), V3OperationRegistry.RefusalSchema,
            "anchor_not_in_version", wrongRule.RootElement));
        var envelope = _builder.Refusal(
            "req", "article_history", RefusalContext(), V3OperationRegistry.RefusalSchema,
            "anchor_not_in_version", complete.RootElement);
        _builder.VerifyBinding(envelope);
    }

    private static V3EnvelopeContext SuccessContext() => Context("success");

    private static V3EnvelopeContext RefusalContext() => Context("refusal");

    private static V3EnvelopeContext Context(string status) => new(
        PublisherId.LuLegilux,
        status,
        TimelineSemantics.PublisherApplicability,
        new V3SnapshotReference("snapshot", Digest),
        "lu",
        false,
        new V3Freshness(DateTimeOffset.Parse("2026-09-17T00:00:00Z"), "current"));
}
