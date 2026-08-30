using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class PreviewPayloadBindingTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void ExactEmbeddedCatalogAndRegistryBindingsAreAccepted()
    {
        var parts = CreateParts();
        var envelope = PreviewRefusalEnvelope.Create(
            CreateContext(parts),
            CreateRefusal());

        var payload = new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            parts.Catalog,
            parts.Registry,
            parts.ObjectSet,
            new PreviewEnvelope[] { envelope });

        Assert.HasCount(1, payload.Envelopes);
    }

    [TestMethod]
    public void CatalogIdentityOrDigestMismatchIsRejected()
    {
        var parts = CreateParts();

        Assert.ThrowsExactly<ArgumentException>(() => new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            parts.Catalog,
            parts.Registry,
            parts.ObjectSet,
            new PreviewEnvelope[]
            {
                PreviewRefusalEnvelope.Create(
                    CreateContext(parts, catalogId: "wrong-catalog"),
                    CreateRefusal()),
            }));

        Assert.ThrowsExactly<ArgumentException>(() => new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            parts.Catalog,
            parts.Registry,
            parts.ObjectSet,
            new PreviewEnvelope[]
            {
                PreviewRefusalEnvelope.Create(
                    CreateContext(parts, catalogDigest: Digest),
                    CreateRefusal()),
            }));
    }

    [TestMethod]
    public void RegistryIdentityOrDigestMismatchIsRejected()
    {
        var parts = CreateParts();

        Assert.ThrowsExactly<ArgumentException>(() => new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            parts.Catalog,
            parts.Registry,
            parts.ObjectSet,
            new PreviewEnvelope[]
            {
                PreviewRefusalEnvelope.Create(
                    CreateContext(parts, registryId: "wrong-registry"),
                    CreateRefusal()),
            }));

        Assert.ThrowsExactly<ArgumentException>(() => new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            parts.Catalog,
            parts.Registry,
            parts.ObjectSet,
            new PreviewEnvelope[]
            {
                PreviewRefusalEnvelope.Create(
                    CreateContext(parts, registryDigest: Digest),
                    CreateRefusal()),
            }));
    }

    [TestMethod]
    public void RefusalMustBeAllowedByTheBoundOperation()
    {
        var parts = CreateParts(allowsIdentifierUnknown: false);

        Assert.ThrowsExactly<ArgumentException>(() => new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            parts.Catalog,
            parts.Registry,
            parts.ObjectSet,
            new PreviewEnvelope[]
            {
                PreviewRefusalEnvelope.Create(CreateContext(parts), CreateRefusal()),
            }));
    }

    [TestMethod]
    public void SuccessObjectSetIdentityOrDigestMismatchIsRejected()
    {
        var parts = CreateParts();
        var context = CreateContext(parts);

        Assert.ThrowsExactly<ArgumentException>(() => new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            parts.Catalog,
            parts.Registry,
            parts.ObjectSet,
            new PreviewEnvelope[]
            {
                PreviewSuccessEnvelope.Create(
                    context,
                    new PreviewObjectSetReference("wrong-set", parts.ObjectSetDigest)),
            }));

        Assert.ThrowsExactly<ArgumentException>(() => new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            parts.Catalog,
            parts.Registry,
            parts.ObjectSet,
            new PreviewEnvelope[]
            {
                PreviewSuccessEnvelope.Create(
                    context,
                    new PreviewObjectSetReference(parts.ObjectSet.ObjectSetId, Digest)),
            }));
    }

    [TestMethod]
    public void InPayloadArtifactReferenceIsOpaqueAndNonCircular()
    {
        var json = ContractJson.Serialize(new PreviewArtifactReference("preview-artifact"));

        Assert.AreEqual("{\"artifact_id\":\"preview-artifact\"}", json);
        Assert.IsFalse(json.Contains("payload_sha256", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("manifest_sha256", StringComparison.Ordinal));
    }

    private static PayloadParts CreateParts(bool allowsIdentifierUnknown = true)
    {
        var catalog = new PreviewOperationCatalog(
            V3SchemaIds.PreviewOperationCatalog,
            "preview-catalog",
            new[]
            {
                new PreviewOperationDescriptor(
                    "resolve",
                    new ContractReference("preview-request/test", Digest),
                    new ContractReference("preview-success/test", Digest),
                    allowsIdentifierUnknown
                        ? new[] { RefusalCode.IdentifierUnknown }
                        : Array.Empty<RefusalCode>(),
                    "identifier_ordinal",
                    "preview_mechanics_only",
                    "rest/preview",
                    "mcp/preview",
                    "html/preview"),
            });
        var registry = PreviewRefusalRegistry.StageZero;
        var objectSet = new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "preview-objects",
            Array.Empty<PreviewObject>());
        return new PayloadParts(
            catalog,
            registry,
            objectSet,
            PreviewSchemaExporter.ComputeDocumentSha256(catalog),
            PreviewSchemaExporter.ComputeDocumentSha256(registry),
            PreviewSchemaExporter.ComputeDocumentSha256(objectSet));
    }

    private static PreviewEnvelopeContext CreateContext(
        PayloadParts parts,
        string? catalogId = null,
        string? catalogDigest = null,
        string? registryId = null,
        string? registryDigest = null) => new(
        requestRef: "req_0123456789abcdef0123456789abcdef",
        operation: new PreviewOperationReference(
            "resolve",
            catalogId ?? parts.Catalog.CatalogId,
            catalogDigest ?? parts.CatalogDigest),
        refusalRegistry: new PreviewRefusalRegistryReference(
            registryId ?? parts.Registry.RegistryId,
            V3SchemaIds.PreviewRefusalRegistry,
            registryDigest ?? parts.RegistryDigest),
        snapshot: new PreviewSnapshotReference("preview-snapshot", Digest),
        artifact: new PreviewArtifactReference("preview-artifact"),
        indexFormat: "preview-index/1",
        runtime: new ComponentIdentity("lex-v3-preview-runtime", Digest),
        builder: new ComponentIdentity("lex-v3-preview-builder", Digest),
        capabilities: PreviewCapabilityState.MechanicsOnly,
        freshness: new PreviewFreshness(
            DateTimeOffset.Parse("2026-08-30T18:00:00Z"),
            PreviewUpstreamHealth.NotApplicableSynthetic),
        jurisdiction: "synthetic-preview-no-jurisdiction",
        provisionality: PreviewProvisionality.All,
        source: PreviewSourceContext.SyntheticTest);

    private static IdentifierUnknownRefusal CreateRefusal() => IdentifierUnknownRefusal.Create(
        IdentifierFamily.Eli,
        "eli/synthetic-preview",
        new[] { PublisherId.LuLegilux },
        Array.Empty<HeldRecordCandidate>(),
        new[]
        {
            PublisherSearchAction.Create(PublisherId.LuLegilux),
        },
        new[] { WhatWouldAnswerAction.CorrectedIdentifier });

    private sealed record PayloadParts(
        PreviewOperationCatalog Catalog,
        PreviewRefusalRegistry Registry,
        PreviewObjectSet ObjectSet,
        string CatalogDigest,
        string RegistryDigest,
        string ObjectSetDigest);
}
