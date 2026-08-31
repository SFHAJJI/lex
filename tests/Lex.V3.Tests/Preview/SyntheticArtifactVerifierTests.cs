using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class SyntheticArtifactVerifierTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string EnvironmentBinding = "/subscriptions/test/s0-05";

    [TestMethod]
    public async Task ExactSignedGraphVerifiesWithOneOpenPerMember()
    {
        using var fixture = CreateFixture();

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.IsTrue(result.Verified);
        Assert.IsNull(result.Failure);
        Assert.AreEqual(1, fixture.Candidate.ManifestOpens);
        Assert.AreEqual(1, fixture.Candidate.ControlOpens);
        Assert.AreEqual(1, fixture.Candidate.SourceOpens);
        Assert.AreEqual(1, fixture.Candidate.DerivedOpens);
        Assert.AreEqual(1, fixture.Candidate.SqliteOpens);
    }

    [TestMethod]
    public async Task SignatureFailureOpensNoControlOrBlob()
    {
        using var fixture = CreateFixture();
        var root = JsonNode.Parse(fixture.Candidate.Manifest)!.AsObject();
        root["attestation"]!["signature"] = new string('A', 86);
        fixture.Candidate.Manifest = Encoding.UTF8.GetBytes(root.ToJsonString());

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.SignatureInvalid, result.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.ControlOpens);
        Assert.AreEqual(0, fixture.Candidate.SourceOpens);
        Assert.AreEqual(0, fixture.Candidate.DerivedOpens);
        Assert.AreEqual(0, fixture.Candidate.SqliteOpens);
    }

    [TestMethod]
    public async Task ValidlySignedIndependentDerivedBytesAreRejected()
    {
        using var fixture = CreateFixture(derivedOverride: "independently hardcoded"u8.ToArray());

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.DerivedContentMismatch, result.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.SqliteOpens);
    }

    [TestMethod]
    public async Task ControlDigestFailureOpensNoBlob()
    {
        using var fixture = CreateFixture();
        fixture.Candidate.Control[0] ^= 1;

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.ControlDigestMismatch, result.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.SourceOpens);
        Assert.AreEqual(0, fixture.Candidate.DerivedOpens);
        Assert.AreEqual(0, fixture.Candidate.SqliteOpens);
    }

    [TestMethod]
    [DataRow((int)SyntheticSliceBlobKind.SourceTransport)]
    [DataRow((int)SyntheticSliceBlobKind.DerivedText)]
    [DataRow((int)SyntheticSliceBlobKind.SqliteIndex)]
    public async Task EveryBlobDigestMismatchIsRejectedAtAdmission(int kindValue)
    {
        using var fixture = CreateFixture();
        var kind = (SyntheticSliceBlobKind)kindValue;
        fixture.Candidate.BytesFor(kind)[^1] ^= 1;

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.BlobDigestMismatch, result.Failure!.Code);
        Assert.AreEqual("synthetic_blob", result.Failure.Stage);
    }

    [TestMethod]
    public async Task BoundEnvironmentMismatchRejectsBeforeControlOrBlob()
    {
        using var fixture = CreateFixture(expectedEnvironmentBinding: "/subscriptions/test/other");

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.EnvironmentForbidden, result.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.ControlOpens);
        Assert.AreEqual(0, fixture.Candidate.SourceOpens);
        Assert.AreEqual(0, fixture.Candidate.DerivedOpens);
        Assert.AreEqual(0, fixture.Candidate.SqliteOpens);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public async Task EveryBoundSchemaTableMemberMismatchRejectsBeforeControlOrBlob(int memberIndex)
    {
        var expectedTable = CreateSchemaTable();
        var members = expectedTable.Members.ToArray();
        var member = members[memberIndex];
        members[memberIndex] = new SyntheticSliceSchemaMember(
            member.Schema,
            member.SchemaResource,
            new string('f', 64),
            member.Bytes);
        using var fixture = CreateFixture(expectedSchemaTable: new SyntheticSliceSchemaTable(members));

        var result = await fixture.Verifier.VerifyAsync(fixture.Candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.GraphSchemaUnsupported, result.Failure!.Code);
        Assert.AreEqual(0, fixture.Candidate.ControlOpens);
        Assert.AreEqual(0, fixture.Candidate.SourceOpens);
        Assert.AreEqual(0, fixture.Candidate.DerivedOpens);
        Assert.AreEqual(0, fixture.Candidate.SqliteOpens);
    }

    private static Fixture CreateFixture(
        byte[]? derivedOverride = null,
        string? expectedEnvironmentBinding = null,
        SyntheticSliceSchemaTable? expectedSchemaTable = null)
    {
        var source = Encoding.UTF8.GetBytes(
            "LEX V3 SYNTHETIC PREVIEW\nArticle 1\nThis text is synthetic and has no legal authority.\n");
        var derived = derivedOverride ?? SyntheticTextNormalizer.Normalize(source);
        var sqlite = new byte[128];
        "SQLite format 3\0"u8.CopyTo(sqlite);
        var table = CreateSchemaTable();
        var control = new SyntheticSliceControl(
            V3SchemaIds.SyntheticSliceControl,
            V3SchemaResourceIds.SyntheticSliceControl,
            SyntheticResolveRequestContract.V1,
            SyntheticSliceOperationCatalog.Create(table.Members[2].Sha256),
            PreviewRefusalRegistry.StageZero,
            table.Members[5],
            SyntheticNormalizationProfile.PlainV1,
            SyntheticSliceScope.CompleteLu,
            new PreviewSnapshotReference("s0-05-snapshot", Digest),
            new ComponentIdentity("s0-05-builder", Digest),
            new SyntheticSliceIndexStamp(
                SyntheticSliceIndexStamp.SchemaIdentity,
                Digest,
                "sqlite-test",
                "sqlite-source-test",
                Digest,
                Digest,
                SyntheticSliceScope.CompleteLu.Sha256,
                Digest),
            new[]
            {
                Descriptor(SyntheticSliceBlobKind.SourceTransport, source),
                Descriptor(SyntheticSliceBlobKind.DerivedText, derived),
                Descriptor(SyntheticSliceBlobKind.SqliteIndex, sqlite),
            });
        var controlBytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(control));

        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = new SyntheticSliceArtifactManifest(
            V3SchemaIds.SyntheticSliceArtifact,
            V3SchemaResourceIds.SyntheticSliceArtifact,
            table.Members[0].Sha256,
            "synthetic_preview",
            synthetic: true,
            "synthetic_test",
            new PreviewEnvironment("preview", EnvironmentBinding),
            new PreviewIssuer("preview_attestor", "s0-05-issuer", "s0-05-key"),
            table,
            new SyntheticSliceControlDescriptor(
                V3SchemaIds.SyntheticSliceControl,
                V3SchemaResourceIds.SyntheticSliceControl,
                table.Members[1].Sha256,
                Sha256(controlBytes),
                controlBytes.Length,
                "application/json"),
            new PreviewAttestation(
                "preview_mechanics_only",
                "ECDSA-P256-SHA256",
                "ieee-p1363",
                new string('A', 86)));
        var signature = key.SignData(
            SyntheticSliceArtifactCanonicalizer.GetSigningBytes(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        manifest = new SyntheticSliceArtifactManifest(
            manifest.Schema,
            manifest.SchemaResource,
            manifest.SchemaSha256,
            manifest.EvidenceClass,
            manifest.Synthetic,
            manifest.SourceKind,
            manifest.Environment,
            manifest.Issuer,
            manifest.SchemaTable,
            manifest.Control,
            new PreviewAttestation(
                manifest.Attestation.Purpose,
                manifest.Attestation.Algorithm,
                manifest.Attestation.SignatureFormat,
                Base64Url.Encode(signature)));

        var publicKey = key.ExportSubjectPublicKeyInfo();
        var trust = new TrustStore("s0-05-issuer", "s0-05-key", publicKey);
        var verifier = new SyntheticSliceArtifactVerifier(
            expectedEnvironmentBinding ?? EnvironmentBinding,
            "s0-05-issuer",
            "s0-05-key",
            Sha256(publicKey),
            expectedSchemaTable ?? table,
            trust);
        var candidate = new MutableCandidate(
            Encoding.UTF8.GetBytes(ContractJson.Serialize(manifest)),
            controlBytes,
            source,
            derived,
            sqlite);
        return new Fixture(verifier, candidate, key);
    }

    private static SyntheticSliceSchemaTable CreateSchemaTable() => new(
        SyntheticSliceSchemaGraph.SchemaIds.Select(
            (schema, index) => new SyntheticSliceSchemaMember(
                schema,
                V3SchemaResourceIds.ForWireSchema(schema),
                index.ToString("x64"),
                100 + index)).ToArray());

    private static SyntheticSliceBlobDescriptor Descriptor(
        SyntheticSliceBlobKind kind,
        byte[] bytes) => new(
        kind,
        Sha256(bytes),
        bytes.Length,
        kind switch
        {
            SyntheticSliceBlobKind.SourceTransport => "application/octet-stream",
            SyntheticSliceBlobKind.DerivedText => "text/plain;charset=utf-8",
            SyntheticSliceBlobKind.SqliteIndex => "application/vnd.sqlite3",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Fixture(
        SyntheticSliceArtifactVerifier Verifier,
        MutableCandidate Candidate,
        ECDsa Key) : IDisposable
    {
        public void Dispose() => Key.Dispose();
    }

    private sealed class TrustStore(string issuer, string keyId, byte[] publicKey) : IPreviewTrustStore
    {
        public bool ContainsIssuer(string issuerId) => string.Equals(issuerId, issuer, StringComparison.Ordinal);

        public bool TryGetSubjectPublicKeyInfo(
            string issuerId,
            string candidateKeyId,
            out ReadOnlyMemory<byte> subjectPublicKeyInfo)
        {
            if (string.Equals(issuerId, issuer, StringComparison.Ordinal) &&
                string.Equals(candidateKeyId, keyId, StringComparison.Ordinal))
            {
                subjectPublicKeyInfo = publicKey;
                return true;
            }

            subjectPublicKeyInfo = default;
            return false;
        }
    }

    private sealed class MutableCandidate(
        byte[] manifest,
        byte[] control,
        byte[] source,
        byte[] derived,
        byte[] sqlite) : ISyntheticSliceCandidate
    {
        public byte[] Manifest { get; set; } = manifest;
        public byte[] Control { get; } = control;
        public byte[] Source { get; } = source;
        public byte[] Derived { get; } = derived;
        public byte[] Sqlite { get; } = sqlite;
        public int ManifestOpens { get; private set; }
        public int ControlOpens { get; private set; }
        public int SourceOpens { get; private set; }
        public int DerivedOpens { get; private set; }
        public int SqliteOpens { get; private set; }

        public ValueTask<Stream> OpenAdmissionManifestAsync(CancellationToken cancellationToken)
        {
            ManifestOpens++;
            return Open(Manifest);
        }

        public ValueTask<Stream> OpenControlAsync(string sha256, CancellationToken cancellationToken)
        {
            ControlOpens++;
            return Open(Control);
        }

        public ValueTask<Stream> OpenBlobAsync(
            SyntheticSliceBlobKind kind,
            string sha256,
            CancellationToken cancellationToken)
        {
            return kind switch
            {
                SyntheticSliceBlobKind.SourceTransport => CountAndOpen(Source, () => SourceOpens++),
                SyntheticSliceBlobKind.DerivedText => CountAndOpen(Derived, () => DerivedOpens++),
                SyntheticSliceBlobKind.SqliteIndex => CountAndOpen(Sqlite, () => SqliteOpens++),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        public byte[] BytesFor(SyntheticSliceBlobKind kind) => kind switch
        {
            SyntheticSliceBlobKind.SourceTransport => Source,
            SyntheticSliceBlobKind.DerivedText => Derived,
            SyntheticSliceBlobKind.SqliteIndex => Sqlite,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        private static ValueTask<Stream> CountAndOpen(byte[] bytes, Action count)
        {
            count();
            return Open(bytes);
        }

        private static ValueTask<Stream> Open(byte[] bytes) =>
            ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
}
