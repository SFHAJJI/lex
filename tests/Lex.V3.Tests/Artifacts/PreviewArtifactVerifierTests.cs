using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Artifacts;

[TestClass]
public sealed class PreviewArtifactVerifierTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string EnvironmentBinding = "/subscriptions/test/preview";

    [TestMethod]
    public async Task ExactSignedStageZeroArtifactVerifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateSignedFixture(key);

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.IsTrue(result.Verified);
        Assert.IsNull(result.Failure);
        Assert.IsNotNull(result.Payload);
        Assert.HasCount(0, result.Payload.OperationCatalog.Entries);
        Assert.AreEqual(1, fixture.Candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task SignatureIsVerifiedBeforePayloadOpen()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateSignedFixture(key);
        var root = JsonNode.Parse(Encoding.UTF8.GetString(fixture.Candidate.Manifest))!.AsObject();
        root["attestation"]!["signature"] = new string('A', 86);
        fixture.Candidate.Manifest = Encoding.UTF8.GetBytes(root.ToJsonString());

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.SignatureInvalid, result.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task NonCanonicalSignatureEncodingIsRejectedBeforePayloadOpen()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateSignedFixture(key);
        var root = JsonNode.Parse(Encoding.UTF8.GetString(fixture.Candidate.Manifest))!.AsObject();
        var signature = root["attestation"]!["signature"]!.GetValue<string>();
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var finalIndex = alphabet.IndexOf(signature[^1], StringComparison.Ordinal);
        Assert.AreEqual(0, finalIndex % 4, "A canonical 64-byte encoding has zero pad bits.");
        root["attestation"]!["signature"] = signature[..^1] + alphabet[finalIndex + 1];
        fixture.Candidate.Manifest = Encoding.UTF8.GetBytes(root.ToJsonString());

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.SignatureInvalid, result.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task ATrustedSecp256k1KeyIsNotAcceptedAsNistP256()
    {
        using var key = ECDsa.Create(ECCurve.CreateFromValue("1.3.132.0.10"));
        var fixture = CreateSignedFixture(key);

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.SignatureInvalid, result.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task ConfiguredTrustStoreNeverAcceptsAnArtifactSuppliedKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateSignedFixture(key, trustKey: false);
        var root = JsonNode.Parse(Encoding.UTF8.GetString(fixture.Candidate.Manifest))!.AsObject();
        root["public_key"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        fixture.Candidate.Manifest = Encoding.UTF8.GetBytes(root.ToJsonString());

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.UnknownMember, result.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.PayloadOpenCount);

        root.Remove("public_key");
        fixture.Candidate.Manifest = Encoding.UTF8.GetBytes(root.ToJsonString());
        var withoutEmbeddedKey = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);
        Assert.AreEqual(ArtifactAdmissionFailureCode.KeyUntrusted, withoutEmbeddedKey.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task ContractGraphIsValidatedBeforeTrustStoreLookup()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        foreach (var member in new[]
                 {
                     "envelope",
                     "object_set",
                     "operation_catalog",
                     "refusal_registry",
                 })
        {
            var fixture = CreateSignedFixture(key);
            var root = JsonNode.Parse(Encoding.UTF8.GetString(fixture.Candidate.Manifest))!.AsObject();
            root["contract_set"]![member]!["sha256"] = Digest;
            fixture.Candidate.Manifest = Encoding.UTF8.GetBytes(root.ToJsonString());

            var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

            Assert.AreEqual(ArtifactAdmissionFailureCode.GraphIncomplete, result.Failure!.Code);
            Assert.AreEqual(0, fixture.TrustStore.LookupCount);
            Assert.AreEqual(0, fixture.Candidate.PayloadOpenCount);
        }
    }

    [TestMethod]
    public async Task PayloadHashIsCheckedBeforePayloadJson()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateSignedFixture(
            key,
            payload: "not-json"u8.ToArray(),
            declaredPayloadSha256: Digest);

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.PayloadDigestMismatch, result.Failure!.Code);
        Assert.AreEqual(1, fixture.Candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task PayloadSizeIsCheckedBeforePayloadJson()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = "not-json"u8.ToArray();
        var fixture = CreateSignedFixture(
            key,
            payload,
            declaredPayloadBytes: payload.Length + 1);

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.PayloadSizeMismatch, result.Failure!.Code);
        Assert.AreEqual(1, fixture.Candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task StageZeroVerifierRejectsAnEarlyOperationCatalog()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var operation = new PreviewOperationDescriptor(
            "resolve",
            new ContractReference("preview-request/test", Digest),
            new ContractReference("preview-success/test", Digest),
            new[] { RefusalCode.IdentifierUnknown },
            "identifier_ordinal",
            "preview_mechanics_only",
            "rest/preview",
            "mcp/preview",
            "html/preview");
        var payload = new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            new PreviewOperationCatalog(
                V3SchemaIds.PreviewOperationCatalog,
                "early-catalog",
                new[] { operation }),
            PreviewRefusalRegistry.StageZero,
            new PreviewObjectSet(
                V3SchemaIds.PreviewObjectSet,
                "empty-objects",
                Array.Empty<PreviewObject>()),
            Array.Empty<PreviewEnvelope>());
        var fixture = CreateSignedFixture(
            key,
            payload: Encoding.UTF8.GetBytes(ContractJson.Serialize(payload)));

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.GraphIncomplete, result.Failure!.Code);
        Assert.AreEqual(1, fixture.Candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task EveryPreviewMarkerAndBindingIsRequiredBeforePayloadOpen()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var attacks = new (Action<JsonObject> Mutate, ArtifactAdmissionFailureCode Expected)[]
        {
            (root => root["schema"] = "lex-v3-release-artifact/test", ArtifactAdmissionFailureCode.GraphSchemaUnsupported),
            (root => root["schema_resource"] = "urn:uuid:00000000-0000-0000-0000-000000000000", ArtifactAdmissionFailureCode.GraphSchemaUnsupported),
            (root => root["schema_sha256"] = Digest, ArtifactAdmissionFailureCode.GraphSchemaUnsupported),
            (root => root["synthetic"] = false, ArtifactAdmissionFailureCode.SyntheticFlagForbidden),
            (root => root["evidence_class"] = "official_release", ArtifactAdmissionFailureCode.SyntheticEvidenceForbidden),
            (root => root["source_kind"] = "official_publisher", ArtifactAdmissionFailureCode.SyntheticSourceForbidden),
            (root => root["environment"]!["class"] = "production", ArtifactAdmissionFailureCode.EnvironmentForbidden),
            (root => root["environment"]!["binding"] = "/subscriptions/test/other", ArtifactAdmissionFailureCode.EnvironmentForbidden),
            (root => root["issuer"]!["role"] = "release_attestor", ArtifactAdmissionFailureCode.IssuerRoleForbidden),
            (root => root["payload"]!["schema"] = "lex-v3-release-payload/test", ArtifactAdmissionFailureCode.GraphSchemaUnsupported),
            (root => root["payload"]!["schema_resource"] = "urn:uuid:00000000-0000-0000-0000-000000000000", ArtifactAdmissionFailureCode.GraphSchemaUnsupported),
            (root => root["payload"]!["schema_sha256"] = Digest, ArtifactAdmissionFailureCode.GraphSchemaUnsupported),
            (root => root["payload"]!["media_type"] = "text/plain", ArtifactAdmissionFailureCode.GraphSchemaUnsupported),
            (root => root["attestation"]!["purpose"] = "release", ArtifactAdmissionFailureCode.AlgorithmUnsupported),
            (root => root["attestation"]!["algorithm"] = "ECDSA-P384-SHA384", ArtifactAdmissionFailureCode.AlgorithmUnsupported),
            (root => root["attestation"]!["signature_format"] = "der", ArtifactAdmissionFailureCode.AlgorithmUnsupported),
        };

        foreach (var attack in attacks)
        {
            var fixture = CreateSignedFixture(key);
            var root = JsonNode.Parse(Encoding.UTF8.GetString(fixture.Candidate.Manifest))!.AsObject();
            attack.Mutate(root);
            fixture.Candidate.Manifest = Encoding.UTF8.GetBytes(root.ToJsonString());

            var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

            Assert.AreEqual(attack.Expected, result.Failure!.Code);
            Assert.AreEqual(0, fixture.Candidate.PayloadOpenCount);
        }
    }

    [TestMethod]
    public void CanonicalSigningBytesBindEverySignedFieldButNotTheSignatureValue()
    {
        var manifest = CreateUnsignedManifest(
            payloadBytes: 0,
            payloadSha256: Digest,
            signature: new string('A', 86));
        var canonical = PreviewArtifactCanonicalizer.GetSigningBytes(manifest);
        var signatureChanged = CopyManifest(
            manifest,
            attestation: CopyAttestation(manifest.Attestation, new string('B', 86)));
        var environmentChanged = CopyManifest(
            manifest,
            environment: new PreviewEnvironment("preview", "/subscriptions/test/preview-two"));

        CollectionAssert.AreEqual(
            canonical,
            PreviewArtifactCanonicalizer.GetSigningBytes(signatureChanged));
        CollectionAssert.AreNotEqual(
            canonical,
            PreviewArtifactCanonicalizer.GetSigningBytes(environmentChanged));
        StringAssert.StartsWith(
            Encoding.UTF8.GetString(canonical),
            V3SchemaIds.PreviewArtifactSignature + "\n");
    }

    [TestMethod]
    public void SigningBytesMatchIndependentFullManifestVector()
    {
        var bytes = PreviewArtifactCanonicalizer.GetSigningBytes(CreateVectorManifest());
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.AreEqual("cf46b1a8ac6c504bb33f01e0a5fbdf5cf0e1157ff0bbd97f737a9cb4036c119b", digest);
        Assert.AreEqual((byte)'\n', bytes[^1]);
        StringAssert.StartsWith(
            Encoding.UTF8.GetString(bytes),
            V3SchemaIds.PreviewArtifactSignature + "\n");
    }

    private static SignedFixture CreateSignedFixture(
        ECDsa key,
        byte[]? payload = null,
        string? declaredPayloadSha256 = null,
        long? declaredPayloadBytes = null,
        bool trustKey = true)
    {
        payload ??= Encoding.UTF8.GetBytes(ContractJson.Serialize(PreviewPayload.CreateStageZero()));
        var actualDigest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var manifest = CreateUnsignedManifest(
            declaredPayloadBytes ?? payload.Length,
            declaredPayloadSha256 ?? actualDigest,
            new string('A', 86));
        var signature = key.SignData(
            PreviewArtifactCanonicalizer.GetSigningBytes(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        manifest = CopyManifest(
            manifest,
            attestation: CopyAttestation(manifest.Attestation, Base64Url.Encode(signature)));

        var trustStore = new TestTrustStore();
        if (trustKey)
        {
            trustStore.Add("preview-issuer", "preview-key", key.ExportSubjectPublicKeyInfo());
        }
        else
        {
            trustStore.AddIssuer("preview-issuer");
        }

        var verifier = PreviewArtifactVerifier.CreateStageZero(EnvironmentBinding, trustStore);
        var candidate = new MutableArtifactCandidate(
            Encoding.UTF8.GetBytes(ContractJson.Serialize(manifest)),
            payload);
        return new SignedFixture(verifier, candidate, trustStore);
    }

    private static PreviewArtifactManifest CreateUnsignedManifest(
        long payloadBytes,
        string payloadSha256,
        string signature) => new(
        V3SchemaIds.PreviewArtifact,
        V3SchemaResourceIds.PreviewArtifact,
        PreviewSchemaExporter.ComputeSha256(
            PreviewSchemaExporter.ExportUtf8(V3SchemaIds.PreviewArtifact)),
        "synthetic_preview",
        synthetic: true,
        "synthetic_test",
        new PreviewEnvironment("preview", EnvironmentBinding),
        new PreviewIssuer("preview_attestor", "preview-issuer", "preview-key"),
        PreviewSchemaExporter.ExportContractSet(),
        new PreviewPayloadDescriptor(
            V3SchemaIds.PreviewPayload,
            V3SchemaResourceIds.PreviewPayload,
            PreviewSchemaExporter.ComputeSha256(
                PreviewSchemaExporter.ExportUtf8(V3SchemaIds.PreviewPayload)),
            payloadSha256,
            payloadBytes,
            "application/json"),
        new PreviewAttestation(
            "preview_mechanics_only",
            "ECDSA-P256-SHA256",
            "ieee-p1363",
            signature));

    private static PreviewArtifactManifest CreateVectorManifest() => new(
        V3SchemaIds.PreviewArtifact,
        V3SchemaResourceIds.PreviewArtifact,
        Digest,
        "synthetic_preview",
        synthetic: true,
        "synthetic_test",
        new PreviewEnvironment("preview", EnvironmentBinding),
        new PreviewIssuer("preview_attestor", "preview-issuer", "preview-key"),
        new PreviewContractSet(
            Tracked(V3SchemaIds.PreviewEnvelope),
            Tracked(V3SchemaIds.PreviewObjectSet),
            Tracked(V3SchemaIds.PreviewOperationCatalog),
            Tracked(V3SchemaIds.PreviewRefusalRegistry)),
        new PreviewPayloadDescriptor(
            V3SchemaIds.PreviewPayload,
            V3SchemaResourceIds.PreviewPayload,
            Digest,
            Digest,
            123,
            "application/json"),
        new PreviewAttestation(
            "preview_mechanics_only",
            "ECDSA-P256-SHA256",
            "ieee-p1363",
            new string('A', 86)));

    private static PreviewTrackedSchemaReference Tracked(string schema) => new(
        schema,
        V3SchemaResourceIds.ForWireSchema(schema),
        Digest);

    private static PreviewArtifactManifest CopyManifest(
        PreviewArtifactManifest manifest,
        PreviewEnvironment? environment = null,
        PreviewAttestation? attestation = null) => new(
        manifest.Schema,
        manifest.SchemaResource,
        manifest.SchemaSha256,
        manifest.EvidenceClass,
        manifest.Synthetic,
        manifest.SourceKind,
        environment ?? manifest.Environment,
        manifest.Issuer,
        manifest.ContractSet,
        manifest.Payload,
        attestation ?? manifest.Attestation);

    private static PreviewAttestation CopyAttestation(
        PreviewAttestation attestation,
        string signature) => new(
        attestation.Purpose,
        attestation.Algorithm,
        attestation.SignatureFormat,
        signature);

    private sealed record SignedFixture(
        PreviewArtifactVerifier Verifier,
        MutableArtifactCandidate Candidate,
        TestTrustStore TrustStore);

    private sealed class TestTrustStore : IPreviewTrustStore
    {
        private readonly Dictionary<(string Issuer, string Key), byte[]> keys = new();
        private readonly HashSet<string> issuers = new(StringComparer.Ordinal);

        public int LookupCount { get; private set; }

        public void Add(string issuerId, string keyId, byte[] subjectPublicKeyInfo)
        {
            issuers.Add(issuerId);
            keys[(issuerId, keyId)] = subjectPublicKeyInfo;
        }

        public void AddIssuer(string issuerId) => issuers.Add(issuerId);

        public bool ContainsIssuer(string issuerId)
        {
            LookupCount++;
            return issuers.Contains(issuerId);
        }

        public bool TryGetSubjectPublicKeyInfo(
            string issuerId,
            string keyId,
            out ReadOnlyMemory<byte> subjectPublicKeyInfo)
        {
            LookupCount++;
            if (keys.TryGetValue((issuerId, keyId), out var value))
            {
                subjectPublicKeyInfo = value;
                return true;
            }

            subjectPublicKeyInfo = default;
            return false;
        }
    }

    private sealed class MutableArtifactCandidate(byte[] manifest, byte[] payload) : IArtifactCandidate
    {
        public byte[] Manifest { get; set; } = manifest;

        public int PayloadOpenCount { get; private set; }

        public ValueTask<Stream> OpenAdmissionManifestAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<Stream>(new MemoryStream(Manifest, writable: false));

        public ValueTask<Stream> OpenPayloadAsync(CancellationToken cancellationToken)
        {
            PayloadOpenCount++;
            return ValueTask.FromResult<Stream>(new MemoryStream(payload, writable: false));
        }
    }
}
