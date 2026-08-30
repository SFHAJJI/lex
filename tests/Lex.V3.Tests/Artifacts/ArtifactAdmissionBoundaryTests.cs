using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Artifacts;

[TestClass]
public sealed class ArtifactAdmissionBoundaryTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void ManifestDepthAcceptsEightAndRejectsNine()
    {
        var atBoundary = AdmissionHeaderReader.Read(NestedObjects(PreviewContractLimits.MaximumManifestDepth));
        var overBoundary = AdmissionHeaderReader.Read(NestedObjects(PreviewContractLimits.MaximumManifestDepth + 1));

        Assert.AreEqual(ArtifactAdmissionFailureCode.UnknownMember, atBoundary.Failure!.Code);
        Assert.AreEqual(ArtifactAdmissionFailureCode.MalformedHeader, overBoundary.Failure!.Code);
    }

    [TestMethod]
    public void ManifestPropertyCountAcceptsSixtyFourAndRejectsSixtyFive()
    {
        var atBoundary = ManifestWithPropertyCount(PreviewContractLimits.MaximumManifestProperties);
        var overBoundary = ManifestWithPropertyCount(PreviewContractLimits.MaximumManifestProperties + 1);

        var atResult = AdmissionHeaderReader.Read(ToUtf8(atBoundary));
        var overResult = AdmissionHeaderReader.Read(ToUtf8(overBoundary));

        Assert.AreEqual(ArtifactAdmissionFailureCode.UnknownMember, atResult.Failure!.Code);
        Assert.AreEqual(ArtifactAdmissionFailureCode.MalformedHeader, overResult.Failure!.Code);
    }

    [TestMethod]
    public void ManifestPropertyNameAcceptsSixtyFourUtf8BytesAndRejectsSixtyFive()
    {
        var atBoundaryName = new string('\u00e9', 32);
        var overBoundaryName = new string('\u00e9', 32) + "a";

        var atResult = AdmissionHeaderReader.Read(SinglePropertyObject(atBoundaryName));
        var overResult = AdmissionHeaderReader.Read(SinglePropertyObject(overBoundaryName));

        Assert.AreEqual(PreviewContractLimits.MaximumManifestPropertyNameBytes, Encoding.UTF8.GetByteCount(atBoundaryName));
        Assert.AreEqual(PreviewContractLimits.MaximumManifestPropertyNameBytes + 1, Encoding.UTF8.GetByteCount(overBoundaryName));
        Assert.AreEqual(ArtifactAdmissionFailureCode.UnknownMember, atResult.Failure!.Code);
        Assert.AreEqual(ArtifactAdmissionFailureCode.MalformedHeader, overResult.Failure!.Code);
    }

    [TestMethod]
    public void ManifestStringAcceptsFourThousandNinetySixUtf8BytesAndRejectsPlusOne()
    {
        var atBoundary = CreateProductionShapedHeader();
        atBoundary["evidence_class"] = new string('a', PreviewContractLimits.MaximumManifestStringBytes);
        var overBoundary = CreateProductionShapedHeader();
        overBoundary["evidence_class"] = new string('a', PreviewContractLimits.MaximumManifestStringBytes + 1);

        var atResult = AdmissionHeaderReader.Read(ToUtf8(atBoundary));
        var overResult = AdmissionHeaderReader.Read(ToUtf8(overBoundary));

        Assert.IsNull(atResult.Failure);
        Assert.AreEqual(ArtifactAdmissionFailureCode.MalformedHeader, overResult.Failure!.Code);
    }

    [TestMethod]
    public void DeclaredPayloadBytesAcceptsMaximumAndRejectsPlusOne()
    {
        var atBoundary = CreateProductionShapedHeader();
        atBoundary["payload"]!["bytes"] = PreviewContractLimits.MaximumPayloadBytes;
        var overBoundary = CreateProductionShapedHeader();
        overBoundary["payload"]!["bytes"] = (long)PreviewContractLimits.MaximumPayloadBytes + 1;

        var atResult = AdmissionHeaderReader.Read(ToUtf8(atBoundary));
        var overResult = AdmissionHeaderReader.Read(ToUtf8(overBoundary));

        Assert.IsNull(atResult.Failure);
        Assert.AreEqual(ArtifactAdmissionFailureCode.MalformedHeader, overResult.Failure!.Code);
    }

    [TestMethod]
    public async Task PayloadStreamAcceptsMaximumBytesAndRejectsPlusOne()
    {
        await using var atBoundary = new MemoryStream(
            new byte[PreviewContractLimits.MaximumPayloadBytes],
            writable: false);
        await using var overBoundary = new MemoryStream(
            new byte[PreviewContractLimits.MaximumPayloadBytes + 1],
            writable: false);

        var atResult = await BoundedStreamReader.ReadAsync(
            atBoundary,
            PreviewContractLimits.MaximumPayloadBytes,
            CancellationToken.None);
        var overResult = await BoundedStreamReader.ReadAsync(
            overBoundary,
            PreviewContractLimits.MaximumPayloadBytes,
            CancellationToken.None);

        Assert.IsFalse(atResult.ExceededLimit);
        Assert.AreEqual(PreviewContractLimits.MaximumPayloadBytes, atResult.Bytes.Length);
        Assert.IsTrue(overResult.ExceededLimit);
        Assert.HasCount(0, overResult.Bytes);
    }

    [TestMethod]
    public void PayloadDepthAcceptsThirtyTwoAndRejectsThirtyThree()
    {
        Assert.IsTrue(StrictPayloadReader.IsStructurallyValid(
            NestedArrays(PreviewContractLimits.MaximumPayloadDepth)));
        Assert.IsFalse(StrictPayloadReader.IsStructurallyValid(
            NestedArrays(PreviewContractLimits.MaximumPayloadDepth + 1)));
    }

    [TestMethod]
    public void PayloadTokenCountAcceptsOneHundredThousandAndRejectsPlusOne()
    {
        Assert.IsTrue(StrictPayloadReader.IsStructurallyValid(
            PayloadWithTokenCount(PreviewContractLimits.MaximumPayloadTokens)));
        Assert.IsFalse(StrictPayloadReader.IsStructurallyValid(
            PayloadWithTokenCount(PreviewContractLimits.MaximumPayloadTokens + 1)));
    }

    [TestMethod]
    public void PayloadObjectAcceptsOneHundredTwentyEightMembersAndRejectsPlusOne()
    {
        Assert.IsTrue(StrictPayloadReader.IsStructurallyValid(
            ObjectWithMembers(PreviewContractLimits.MaximumObjectMembers)));
        Assert.IsFalse(StrictPayloadReader.IsStructurallyValid(
            ObjectWithMembers(PreviewContractLimits.MaximumObjectMembers + 1)));
    }

    [TestMethod]
    public void PayloadArrayAcceptsFourThousandNinetySixItemsAndRejectsPlusOne()
    {
        Assert.IsTrue(StrictPayloadReader.IsStructurallyValid(
            ArrayWithItems(PreviewContractLimits.MaximumArrayItems)));
        Assert.IsFalse(StrictPayloadReader.IsStructurallyValid(
            ArrayWithItems(PreviewContractLimits.MaximumArrayItems + 1)));
    }

    [TestMethod]
    public void PayloadPropertyNameAcceptsOneHundredTwentyEightUtf8BytesAndRejectsPlusOne()
    {
        var atBoundaryName = new string('\u00e9', 64);
        var overBoundaryName = new string('\u00e9', 64) + "a";

        Assert.AreEqual(PreviewContractLimits.MaximumPayloadPropertyNameBytes, Encoding.UTF8.GetByteCount(atBoundaryName));
        Assert.AreEqual(PreviewContractLimits.MaximumPayloadPropertyNameBytes + 1, Encoding.UTF8.GetByteCount(overBoundaryName));
        Assert.IsTrue(StrictPayloadReader.IsStructurallyValid(SinglePropertyObject(atBoundaryName)));
        Assert.IsFalse(StrictPayloadReader.IsStructurallyValid(SinglePropertyObject(overBoundaryName)));
    }

    [TestMethod]
    public void PayloadStringAcceptsOneMegabyteUtf8AndRejectsPlusOne()
    {
        var atBoundaryValue = new string('\u00e9', PreviewContractLimits.MaximumPayloadStringBytes / 2);
        var overBoundaryValue = atBoundaryValue + "a";

        Assert.AreEqual(PreviewContractLimits.MaximumPayloadStringBytes, Encoding.UTF8.GetByteCount(atBoundaryValue));
        Assert.AreEqual(PreviewContractLimits.MaximumPayloadStringBytes + 1, Encoding.UTF8.GetByteCount(overBoundaryValue));
        Assert.IsTrue(StrictPayloadReader.IsStructurallyValid(JsonSerializer.SerializeToUtf8Bytes(atBoundaryValue)));
        Assert.IsFalse(StrictPayloadReader.IsStructurallyValid(JsonSerializer.SerializeToUtf8Bytes(overBoundaryValue)));
    }

    [TestMethod]
    public void PreviewObjectSetAcceptsTwoHundredFiftySixObjectsAndRejectsPlusOne()
    {
        var atBoundary = CreateObjects(PreviewContractLimits.MaximumObjects);
        var overBoundary = CreateObjects(PreviewContractLimits.MaximumObjects + 1);

        var accepted = new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "objects-at-boundary",
            atBoundary);

        Assert.HasCount(PreviewContractLimits.MaximumObjects, accepted.Objects);
        Assert.ThrowsExactly<ArgumentException>(() => new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "objects-over-boundary",
            overBoundary));
    }

    [TestMethod]
    public void PreviewPayloadAcceptsSixteenEnvelopesAndRejectsPlusOne()
    {
        var (catalog, registry, objectSet, envelope) = CreateEnvelopeParts();
        var atBoundary = Enumerable
            .Repeat<PreviewEnvelope>(envelope, PreviewContractLimits.MaximumEnvelopes)
            .ToArray();
        var overBoundary = Enumerable
            .Repeat<PreviewEnvelope>(envelope, PreviewContractLimits.MaximumEnvelopes + 1)
            .ToArray();

        var accepted = new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            catalog,
            registry,
            objectSet,
            atBoundary);

        Assert.HasCount(PreviewContractLimits.MaximumEnvelopes, accepted.Envelopes);
        Assert.ThrowsExactly<ArgumentException>(() => new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            catalog,
            registry,
            objectSet,
            overBoundary));
    }

    [TestMethod]
    public void ProductionArtifactAssemblyEmbedsNoPreviewDataOrTrustRoot()
    {
        var assembly = typeof(ProductionArtifactAdmission).Assembly;
        var definedTypes = assembly.DefinedTypes.ToArray();
        var forbiddenNamedTypes = definedTypes
            .Where(static type =>
                type.Name.Contains("Fixture", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("SourceAdapter", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("SigningKey", StringComparison.OrdinalIgnoreCase))
            .Select(static type => type.FullName)
            .ToArray();
        var concreteCandidates = definedTypes
            .Where(static type => !type.IsAbstract && typeof(IArtifactCandidate).IsAssignableFrom(type.AsType()))
            .Select(static type => type.FullName)
            .ToArray();
        var concreteTrustStores = definedTypes
            .Where(static type => !type.IsAbstract && typeof(IPreviewTrustStore).IsAssignableFrom(type.AsType()))
            .Select(static type => type.FullName)
            .ToArray();
        var embeddedByteFixtures = definedTypes
            .SelectMany(static type => type.DeclaredFields)
            .Where(static field =>
                field.IsStatic &&
                (field.FieldType == typeof(byte[]) || field.FieldType == typeof(ReadOnlyMemory<byte>)))
            .Select(static field => $"{field.DeclaringType!.FullName}.{field.Name}")
            .ToArray();

        Assert.HasCount(0, assembly.GetManifestResourceNames());
        Assert.HasCount(0, forbiddenNamedTypes);
        Assert.HasCount(0, concreteCandidates);
        Assert.HasCount(0, concreteTrustStores);
        Assert.HasCount(0, embeddedByteFixtures);
    }

    private static JsonObject CreateProductionShapedHeader() => new()
    {
        ["schema"] = "lex-v3-release-artifact/test-only",
        ["schema_resource"] = "urn:uuid:00000000-0000-0000-0000-000000000001",
        ["schema_sha256"] = Digest,
        ["evidence_class"] = "official_release",
        ["synthetic"] = false,
        ["source_kind"] = "official_publisher",
        ["environment"] = new JsonObject
        {
            ["class"] = "production",
            ["binding"] = "/subscriptions/test/production",
        },
        ["issuer"] = new JsonObject
        {
            ["role"] = "release_attestor",
            ["issuer_id"] = "release-issuer",
            ["key_id"] = "release-key",
        },
        ["contract_set"] = new JsonObject
        {
            ["envelope"] = Contract(V3SchemaIds.PreviewEnvelope),
            ["object_set"] = Contract(V3SchemaIds.PreviewObjectSet),
            ["operation_catalog"] = Contract(V3SchemaIds.PreviewOperationCatalog),
            ["refusal_registry"] = Contract(V3SchemaIds.PreviewRefusalRegistry),
        },
        ["payload"] = new JsonObject
        {
            ["schema"] = "lex-v3-release-payload/test-only",
            ["schema_resource"] = "urn:uuid:00000000-0000-0000-0000-000000000002",
            ["schema_sha256"] = Digest,
            ["sha256"] = Digest,
            ["bytes"] = 0,
            ["media_type"] = "application/json",
        },
        ["attestation"] = new JsonObject
        {
            ["purpose"] = "release",
            ["algorithm"] = "ECDSA-P256-SHA256",
            ["signature_format"] = "ieee-p1363",
            ["signature"] = new string('A', 86),
        },
    };

    private static JsonObject Contract(string schema) => new()
    {
        ["schema"] = schema,
        ["schema_resource"] = V3SchemaResourceIds.ForWireSchema(schema),
        ["sha256"] = Digest,
    };

    private static PreviewObject[] CreateObjects(int count) => Enumerable
        .Range(0, count)
        .Select(index => (PreviewObject)new PreviewSyntheticCoordinate(
            $"object-{index:D3}",
            synthetic: true,
            $"preview:work:{index:D3}",
            $"preview:version:{index:D3}",
            $"preview:anchor:{index:D3}",
            BodyHoldingState.NotHeld,
            PreviewBodyDispositionReason.UnknownPendingEvidence,
            body: null,
            bodySha256: null))
        .ToArray();

    private static (
        PreviewOperationCatalog Catalog,
        PreviewRefusalRegistry Registry,
        PreviewObjectSet ObjectSet,
        PreviewRefusalEnvelope Envelope) CreateEnvelopeParts()
    {
        var catalog = new PreviewOperationCatalog(
            V3SchemaIds.PreviewOperationCatalog,
            "boundary-catalog",
            new[]
            {
                new PreviewOperationDescriptor(
                    "resolve",
                    new ContractReference("preview-request/test", Digest),
                    new ContractReference("preview-success/test", Digest),
                    new[] { RefusalCode.IdentifierUnknown },
                    "identifier_ordinal",
                    "preview_mechanics_only",
                    "rest/preview",
                    "mcp/preview",
                    "html/preview"),
            });
        var registry = PreviewRefusalRegistry.StageZero;
        var objectSet = new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "boundary-objects",
            Array.Empty<PreviewObject>());
        var context = new PreviewEnvelopeContext(
            "req_0123456789abcdef0123456789abcdef",
            new PreviewOperationReference(
                "resolve",
                catalog.CatalogId,
                PreviewSchemaExporter.ComputeDocumentSha256(catalog)),
            new PreviewRefusalRegistryReference(
                registry.RegistryId,
                V3SchemaIds.PreviewRefusalRegistry,
                PreviewSchemaExporter.ComputeDocumentSha256(registry)),
            new PreviewSnapshotReference("boundary-snapshot", Digest),
            new PreviewArtifactReference("boundary-artifact"),
            "preview-index/1",
            new ComponentIdentity("boundary-runtime", Digest),
            new ComponentIdentity("boundary-builder", Digest),
            PreviewCapabilityState.MechanicsOnly,
            new PreviewFreshness(
                DateTimeOffset.Parse("2026-08-30T18:00:00Z"),
                PreviewUpstreamHealth.NotApplicableSynthetic),
            "synthetic-preview-no-jurisdiction",
            PreviewProvisionality.All,
            PreviewSourceContext.SyntheticTest);
        var refusal = IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            new[] { PublisherId.LuLegilux },
            Array.Empty<HeldRecordCandidate>(),
            new[]
            {
                PublisherSearchAction.Create(PublisherId.LuLegilux),
            },
            new[] { WhatWouldAnswerAction.CorrectedIdentifier });
        return (catalog, registry, objectSet, PreviewRefusalEnvelope.Create(context, refusal));
    }

    private static JsonObject ManifestWithPropertyCount(int target)
    {
        var root = CreateProductionShapedHeader();
        var current = CountProperties(root);
        for (var index = 0; current + index < target; index++)
        {
            root[$"extra_{index}"] = true;
        }

        Assert.AreEqual(target, CountProperties(root));
        return root;
    }

    private static int CountProperties(JsonNode node)
    {
        return node switch
        {
            JsonObject value => value.Count + value.Sum(static property =>
                property.Value is null ? 0 : CountProperties(property.Value)),
            JsonArray value => value.Sum(static item => item is null ? 0 : CountProperties(item)),
            _ => 0,
        };
    }

    private static byte[] NestedArrays(int depth) =>
        Encoding.UTF8.GetBytes(new string('[', depth) + "0" + new string(']', depth));

    private static byte[] NestedObjects(int depth)
    {
        var builder = new StringBuilder((depth * 6) + 1);
        for (var index = 0; index < depth; index++)
        {
            builder.Append("{\"a\":");
        }

        builder.Append('0').Append('}', depth);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] ObjectWithMembers(int count)
    {
        var builder = new StringBuilder(count * 10);
        builder.Append('{');
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append('"').Append('p').Append(index).Append("\":0");
        }

        builder.Append('}');
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] ArrayWithItems(int count)
    {
        var builder = new StringBuilder((count * 2) + 1);
        builder.Append('[');
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append('0');
        }

        builder.Append(']');
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] PayloadWithTokenCount(int targetTokenCount)
    {
        const int innerArrayCount = 25;
        var remainingPrimitiveTokens = targetTokenCount - 2 - (innerArrayCount * 2);
        var builder = new StringBuilder(remainingPrimitiveTokens * 2);
        builder.Append('[');
        for (var arrayIndex = 0; arrayIndex < innerArrayCount; arrayIndex++)
        {
            if (arrayIndex > 0)
            {
                builder.Append(',');
            }

            var itemCount = Math.Min(PreviewContractLimits.MaximumArrayItems, remainingPrimitiveTokens);
            remainingPrimitiveTokens -= itemCount;
            builder.Append('[');
            for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                if (itemIndex > 0)
                {
                    builder.Append(',');
                }

                builder.Append('0');
            }

            builder.Append(']');
        }

        builder.Append(']');
        Assert.AreEqual(0, remainingPrimitiveTokens);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] SinglePropertyObject(string propertyName)
    {
        var propertyJson = JsonSerializer.Serialize(propertyName);
        return Encoding.UTF8.GetBytes($"{{{propertyJson}:0}}");
    }

    private static byte[] ToUtf8(JsonObject value) =>
        Encoding.UTF8.GetBytes(value.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
}
