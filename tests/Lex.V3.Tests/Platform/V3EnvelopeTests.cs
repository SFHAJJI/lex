using System.Text.Json;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

[TestClass]
public sealed class V3EnvelopeTests
{
    private readonly V3EnvelopeBuilder _builder = new(V3OperationRegistry.Reviewed);

    [TestMethod]
    public void SuccessIsBoundToRegistryOperationSchemaAndObjectType()
    {
        using var result = JsonDocument.Parse("{\"work_id\":\"lu-legilux:test\"}");
        var envelope = _builder.Success(
            "req_01",
            "resolve",
            V3Verdicts.Answer,
            "lex-v3-resolve-result/1",
            "work_resolution",
            result.RootElement);

        _builder.VerifyBinding(envelope);
        Assert.AreEqual(V3OperationRegistry.Reviewed.Sha256, envelope.RegistrySha256);
        Assert.AreEqual("work_resolution", envelope.Result!.ObjectType);
        Assert.IsNull(envelope.Refusal);
    }

    [TestMethod]
    public void HelpfulRefusalIsBoundToClosedRegistry()
    {
        using var helpful = JsonDocument.Parse("{\"stable_coordinate\":\"/lu-legilux/test\",\"current_digest\":\"abc\"}");
        var envelope = _builder.Refusal(
            "req_02",
            "resolve",
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
            "req", "unknown", V3Verdicts.Answer, "lex-v3-unknown-result/1", "work_record", payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "req", "resolve", "maybe", "lex-v3-resolve-result/1", "work_resolution", payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "req", "resolve", V3Verdicts.Answer, "lex-v3-search-result/1", "work_resolution", payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "req", "resolve", V3Verdicts.Answer, "lex-v3-resolve-result/1", "quote", payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "resolve", V3OperationRegistry.RefusalSchema, "invented", payload.RootElement));
    }

    [TestMethod]
    public void RefusalRequiresHelpfulObjectPayload()
    {
        using var empty = JsonDocument.Parse("{}");
        using var scalar = JsonDocument.Parse("true");

        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "resolve", V3OperationRegistry.RefusalSchema, "identifier_unknown", empty.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Refusal(
            "req", "resolve", V3OperationRegistry.RefusalSchema, "identifier_unknown", scalar.RootElement));
    }

    [TestMethod]
    public void RequestReferenceCannotCarryQueryTextOrClientIdentifiers()
    {
        using var payload = JsonDocument.Parse("{\"work_id\":\"lu-legilux:test\"}");

        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "what is my deadline?",
            "resolve",
            V3Verdicts.Answer,
            "lex-v3-resolve-result/1",
            "work_resolution",
            payload.RootElement));
        Assert.ThrowsExactly<ArgumentException>(() => _builder.Success(
            "client@example.com",
            "resolve",
            V3Verdicts.Answer,
            "lex-v3-resolve-result/1",
            "work_resolution",
            payload.RootElement));
    }
}
