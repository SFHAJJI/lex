using System.Security.Cryptography;
using System.Text;
using Lex.Index;
using Lex.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lex.Tests;

public sealed class ArtifactManifestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-artifacts-{Guid.NewGuid():N}");

    public ArtifactManifestTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Signed_manifest_verifies_every_file_against_a_pinned_root()
    {
        File.WriteAllText(Path.Combine(_dir, "index-a.db"), "SQLite format 3 signed test index");
        File.WriteAllText(Path.Combine(_dir, "vectors.bin"), "compact vectors");
        var privateKey = StampSigner.CreateKeyPem();
        var root = ArtifactManifests.TrustRoot("release-2026", privateKey);
        var manifest = ArtifactManifests.Create(
            _dir,
            ["vectors.bin", "index-a.db"],
            root.KeyId,
            "2026-08-06T00:00:00Z",
            "abc123",
            new Dictionary<string, string> { ["corpus"] = "def456" });
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "index-a.manifest.json");
        var signaturePath = Path.Combine(_dir, "index-a.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        File.WriteAllText(signaturePath, ArtifactManifests.SignBase64(bytes, privateKey));

        var verified = ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [root]);

        Assert.Equal(["index-a.db", "vectors.bin"], verified.Order(StringComparer.Ordinal));
        Assert.Equal("abc123", ArtifactManifests.Parse(bytes).CodeCommit);
    }

    [Fact]
    public void Editing_an_artifact_after_signing_fails_closed()
    {
        var file = Path.Combine(_dir, "index-a.db");
        File.WriteAllText(file, "original");
        var privateKey = StampSigner.CreateKeyPem();
        var root = ArtifactManifests.TrustRoot("release-2026", privateKey);
        var manifest = ArtifactManifests.Create(
            _dir, ["index-a.db"], root.KeyId, "2026-08-06T00:00:00Z", "abc123");
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "index-a.manifest.json");
        var signaturePath = Path.Combine(_dir, "index-a.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        File.WriteAllText(signaturePath, ArtifactManifests.SignBase64(bytes, privateKey));
        File.WriteAllText(file, "tampered");

        Assert.ThrowsAny<CryptographicException>(() =>
            ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [root]));
    }

    [Fact]
    public void A_valid_signature_from_an_unpinned_replacement_key_is_rejected()
    {
        File.WriteAllText(Path.Combine(_dir, "index-a.db"), "original");
        var trustedPrivateKey = StampSigner.CreateKeyPem();
        var attackerPrivateKey = StampSigner.CreateKeyPem();
        var trustedRoot = ArtifactManifests.TrustRoot("trusted", trustedPrivateKey);
        var attackerRoot = ArtifactManifests.TrustRoot("attacker", attackerPrivateKey);
        var manifest = ArtifactManifests.Create(
            _dir, ["index-a.db"], attackerRoot.KeyId, "2026-08-06T00:00:00Z", "abc123");
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "index-a.manifest.json");
        var signaturePath = Path.Combine(_dir, "index-a.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        File.WriteAllText(signaturePath, ArtifactManifests.SignBase64(bytes, attackerPrivateKey));

        Assert.ThrowsAny<CryptographicException>(() =>
            ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [trustedRoot]));
    }

    [Fact]
    public void Artifact_paths_cannot_escape_the_release_directory()
    {
        var key = StampSigner.CreateKeyPem();
        var root = ArtifactManifests.TrustRoot("release-2026", key);
        var manifest = new ArtifactManifest(
            ArtifactManifests.Schema,
            ArtifactManifests.Algorithm,
            root.KeyId,
            "2026-08-06T00:00:00Z",
            "abc123",
            new Dictionary<string, string>(),
            [new ArtifactFile("../outside.db", "sqlite-index", 1, new string('0', 64))]);
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "bad.manifest.json");
        var signaturePath = Path.Combine(_dir, "bad.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        File.WriteAllText(signaturePath, ArtifactManifests.SignBase64(bytes, key));

        Assert.Throws<InvalidDataException>(() =>
            ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [root]));
    }

    [Fact]
    public void Key_vault_base64url_signatures_are_accepted()
    {
        File.WriteAllText(Path.Combine(_dir, "index-eu.db"), "authoritative bytes");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = key.ExportECPrivateKeyPem();
        var root = ArtifactManifests.TrustRoot("key-vault-v2", privateKey);
        var manifest = ArtifactManifests.Create(
            _dir, ["index-eu.db"], root.KeyId, "2026-08-06T00:00:00Z", "abc123");
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "index-eu.manifest.json");
        var signaturePath = Path.Combine(_dir, "index-eu.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        var signature = key.SignData(bytes, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        File.WriteAllText(signaturePath,
            Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

        Assert.Single(ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [root]));
    }

    [Fact]
    public void Runtime_mounts_nothing_when_manifests_are_required_but_absent()
    {
        using var registry = new IndexRegistry(
            Options.Create(new LexOptions { IndexDir = _dir, RequireArtifactManifest = true }),
            NullLogger<IndexRegistry>.Instance);

        Assert.Equal(0, registry.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
