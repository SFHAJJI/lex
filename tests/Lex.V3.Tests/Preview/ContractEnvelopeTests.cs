using System.Text;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class ContractEnvelopeTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string RequestRef = "req_0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void SuccessEnvelopeCarriesTheExactHeldEliAndSqlBackedObjectSet()
    {
        var body = "LEX V3 SYNTHETIC PREVIEW\nArticle 1\nThis text is synthetic and has no legal authority.\n";
        var bodySha256 = PreviewSchemaExporter.ComputeSha256(Encoding.UTF8.GetBytes(body));
        var result = new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "s0-05-resolve-result",
            new PreviewObject[]
            {
                new PreviewSyntheticCoordinate(
                    "preview:coordinate:synthetic-preview",
                    synthetic: true,
                    "preview:work:synthetic-preview",
                    "preview:version:synthetic-preview",
                    "preview:article:1",
                    BodyHoldingState.HeldPublic,
                    PreviewBodyDispositionReason.SyntheticFixture,
                    body,
                    bodySha256),
            });

        SyntheticResolveEnvelope envelope = SyntheticResolveSuccessEnvelope.Create(
            CreateContext(),
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            result);

        var json = ContractJson.Serialize(envelope);
        var roundTrip = ContractJson.Deserialize<SyntheticResolveEnvelope>(json);

        Assert.IsInstanceOfType<SyntheticResolveSuccessEnvelope>(roundTrip);
        var success = (SyntheticResolveSuccessEnvelope)roundTrip;
        Assert.AreEqual("ok", success.Status);
        Assert.AreEqual(IdentifierFamily.Eli, success.MatchedIdentifierFamily);
        Assert.AreEqual("eli/synthetic-preview", success.MatchedCoordinate);
        Assert.HasCount(1, success.Result.Objects);
        Assert.IsTrue(success.Synthetic);
    }

    [TestMethod]
    public void RefusalIsExactHelpfulNonAbsenceForTheHistoricalCoordinate()
    {
        var refusal = SyntheticIdentifierUnknownRefusal.Create(
            new[]
            {
                new SyntheticHeldRecordCandidate(
                    IdentifierFamily.Eli,
                    "eli/synthetic-preview",
                    PublisherId.LuLegilux),
            });
        SyntheticResolveEnvelope envelope = SyntheticResolveRefusalEnvelope.Create(
            CreateContext(),
            refusal);

        var json = ContractJson.Serialize(envelope);
        var roundTrip = ContractJson.Deserialize<SyntheticResolveEnvelope>(json);

        Assert.IsInstanceOfType<SyntheticResolveRefusalEnvelope>(roundTrip);
        var rejected = (SyntheticResolveRefusalEnvelope)roundTrip;
        Assert.AreEqual("identifier_unknown", rejected.Status);
        Assert.AreEqual(IdentifierFamily.HistoricalLegalId, rejected.Refusal.CheckedIdentifierFamily);
        Assert.AreEqual(
            "historical_legal_id:synthetic-preview",
            rejected.Refusal.RequestedCoordinate);
        Assert.IsFalse(rejected.Refusal.AssertsAbsenceOfLaw);
        Assert.HasCount(1, rejected.Refusal.OfficialSearchActions);
        Assert.IsNotEmpty(rejected.Refusal.WhatWouldAnswer);
    }

    [TestMethod]
    public void EnvelopeRejectsWrongBranchCoordinatesAndRequestReferences()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SyntheticResolveContext(
            "req_0123456789ABCDEF0123456789ABCDEF",
            CreateOperation(),
            CreateRefusalRegistryReference(),
            new PreviewSnapshotReference("s0-05-snapshot", Digest),
            new SyntheticSliceArtifactReference(Digest),
            new SyntheticSliceIndexReference(
                SyntheticSliceIndexStamp.SchemaIdentity,
                Digest,
                Digest),
            new ComponentIdentity("s0-05-runtime", Digest),
            new ComponentIdentity("s0-05-builder", Digest)));

        Assert.ThrowsExactly<ArgumentException>(() => new SyntheticResolveSuccessEnvelope(
            V3SchemaIds.SyntheticResolveEnvelope,
            synthetic: true,
            "envelope",
            "ok",
            CreateContext(),
            IdentifierFamily.HistoricalLegalId,
            "historical_legal_id:synthetic-preview",
            CreateEmptyObjectSet()));
    }

    [TestMethod]
    public void EnvelopeRejectsUnknownJsonMembers()
    {
        var refusal = SyntheticResolveRefusalEnvelope.Create(
            CreateContext(),
            SyntheticIdentifierUnknownRefusal.Create(Array.Empty<SyntheticHeldRecordCandidate>()));
        var json = ContractJson.Serialize<SyntheticResolveEnvelope>(refusal);
        var mutated = json[..^1] + ",\"extension\":true}";

        Assert.ThrowsExactly<System.Text.Json.JsonException>(() =>
            ContractJson.Deserialize<SyntheticResolveEnvelope>(mutated));
    }

    [TestMethod]
    public void SuccessCannotExposeABodyWithoutTheNoLegalAuthoritySentence()
    {
        const string unsafeBody = "LEX V3 SYNTHETIC PREVIEW\nArticle 1\nSynthetic example only.\n";
        var coordinate = new PreviewSyntheticCoordinate(
            "preview:coordinate:synthetic-preview",
            synthetic: true,
            "preview:work:synthetic-preview",
            "preview:version:synthetic-preview",
            "preview:article:1",
            BodyHoldingState.HeldPublic,
            PreviewBodyDispositionReason.SyntheticFixture,
            unsafeBody,
            PreviewSchemaExporter.ComputeSha256(Encoding.UTF8.GetBytes(unsafeBody)));
        var result = new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "s0-05-unsafe",
            new PreviewObject[] { coordinate });

        Assert.ThrowsExactly<ArgumentException>(() => SyntheticResolveSuccessEnvelope.Create(
            CreateContext(),
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            result));
    }

    private static SyntheticResolveContext CreateContext() => new(
        RequestRef,
        CreateOperation(),
        CreateRefusalRegistryReference(),
        new PreviewSnapshotReference("s0-05-snapshot", Digest),
        new SyntheticSliceArtifactReference(Digest),
        new SyntheticSliceIndexReference(SyntheticSliceIndexStamp.SchemaIdentity, Digest, Digest),
        new ComponentIdentity("s0-05-runtime", Digest),
        new ComponentIdentity("s0-05-builder", Digest));

    private static PreviewOperationReference CreateOperation() => new(
        "resolve",
        SyntheticSliceOperationCatalog.CatalogId,
        Digest);

    private static PreviewRefusalRegistryReference CreateRefusalRegistryReference() => new(
        PreviewRefusalRegistry.StageZero.RegistryId,
        V3SchemaIds.PreviewRefusalRegistry,
        Digest);

    private static PreviewObjectSet CreateEmptyObjectSet() => new(
        V3SchemaIds.PreviewObjectSet,
        "s0-05-empty",
        Array.Empty<PreviewObject>());
}
