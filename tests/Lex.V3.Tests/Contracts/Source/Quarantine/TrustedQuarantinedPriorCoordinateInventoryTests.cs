using System.Security.Cryptography;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Quarantine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

/// <summary>
/// The trust-store-backed admission <see cref="QuarantineInventoryCanonicalizer.VerifySignature"/>
/// says in its own remarks it deliberately is not. Every key here is a fresh ephemeral P-256 key
/// generated for this test only.
/// </summary>
/// <remarks>
/// Each test drives exactly one clause. The one that matters most is
/// <see cref="AKeyThatResolvesOnlyUnderADifferentIssuerIsRefused"/>: the attestation names a
/// genuinely pinned issuer and a real key, and the key belongs to somebody else. A store asked "does
/// this key exist" rather than "does this issuer hold this key" would admit it.
/// </remarks>
[TestClass]
public sealed class TrustedQuarantinedPriorCoordinateInventoryTests
{
    private const string PinnedIssuer = "quarantine-reviewer-1";
    private const string PinnedKey = "key-1";

    [TestMethod]
    public void AnInventorySignedByAPinnedIssuerIsAdmitted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var inventory = SignedInventory(key);
        var store = new FakeTrustStore(PinnedIssuer, PinnedKey, PublicKeyBytes(key));

        var admitted = TrustedQuarantinedPriorCoordinateInventory.TryAdmit(
            inventory, store, out var refusal, out var detail);

        Assert.IsNotNull(admitted, $"{refusal}: {detail}");
        Assert.AreEqual(TrustedQuarantineInventoryRefusal.None, refusal);
        Assert.AreSame(inventory, admitted!.Inventory);
        Assert.AreEqual(PinnedIssuer, admitted.IssuerId);
        Assert.AreEqual(PinnedKey, admitted.KeyId);
    }

    [TestMethod]
    public void AnIssuerThisBuildDoesNotPinIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var inventory = SignedInventory(key);

        // A store that pins somebody else entirely. The signature is real and would verify.
        var store = new FakeTrustStore("some-other-reviewer", PinnedKey, PublicKeyBytes(key));

        var admitted = TrustedQuarantinedPriorCoordinateInventory.TryAdmit(
            inventory, store, out var refusal, out var detail);

        Assert.IsNull(admitted);
        Assert.AreEqual(TrustedQuarantineInventoryRefusal.IssuerNotTrusted, refusal);
        StringAssert.Contains(detail, PinnedIssuer);
    }

    [TestMethod]
    public void AKeyThatResolvesOnlyUnderADifferentIssuerIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var inventory = SignedInventory(key);

        // The issuer IS pinned and the key IS real -- it just belongs to another issuer. This is the
        // shape a store asked "is this key known" instead of "does this issuer hold it" would admit.
        var store = new FakeTrustStore(
            PinnedIssuer, PinnedKey, PublicKeyBytes(key), keyBelongsToIssuer: "a-different-issuer");

        var admitted = TrustedQuarantinedPriorCoordinateInventory.TryAdmit(
            inventory, store, out var refusal, out var detail);

        Assert.IsNull(admitted);
        Assert.AreEqual(TrustedQuarantineInventoryRefusal.KeyNotResolvedUnderIssuer, refusal);
        StringAssert.Contains(detail, PinnedKey);
    }

    [TestMethod]
    public void ASignatureThatDoesNotVerifyUnderTheResolvedKeyIsRefused()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var resolved = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var inventory = SignedInventory(signer);

        // Pinned issuer, resolvable key, importable bytes -- and the wrong key. Everything earlier
        // in the sequence passes, so this drives the last clause on its own.
        var store = new FakeTrustStore(PinnedIssuer, PinnedKey, PublicKeyBytes(resolved));

        var admitted = TrustedQuarantinedPriorCoordinateInventory.TryAdmit(
            inventory, store, out var refusal, out var detail);

        Assert.IsNull(admitted);
        Assert.AreEqual(TrustedQuarantineInventoryRefusal.SignatureDoesNotVerify, refusal);
        Assert.IsNotNull(detail);
    }

    // ---- Fixtures. ----

    /// <summary>
    /// Holds SPKI bytes on its own side -- where the quarantine namespace's no-content rule does not
    /// apply -- and imports a fresh key per call, since the caller disposes what it is handed.
    /// </summary>
    private sealed class FakeTrustStore(
        string pinnedIssuerId,
        string pinnedKeyId,
        byte[] subjectPublicKeyInfo,
        string? keyBelongsToIssuer = null)
        : IQuarantineTrustStore
    {
        private readonly string _keyIssuer = keyBelongsToIssuer ?? pinnedIssuerId;

        public bool ContainsIssuer(string issuerId) =>
            string.Equals(issuerId, pinnedIssuerId, StringComparison.Ordinal);

        public bool TryResolveVerificationKey(string issuerId, string keyId, out ECDsa? publicKey)
        {
            publicKey = null;
            if (!string.Equals(issuerId, _keyIssuer, StringComparison.Ordinal) ||
                !string.Equals(keyId, pinnedKeyId, StringComparison.Ordinal))
            {
                return false;
            }

            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            publicKey = key;
            return true;
        }
    }

    private static byte[] PublicKeyBytes(ECDsa key) => key.ExportSubjectPublicKeyInfo();

    private static QuarantinedPriorCoordinateInventory SignedInventory(ECDsa key)
    {
        var coordinates = QuarantineFixtures.CoordinateSet();

        // Sign-then-rebuild, the pattern this codebase already uses for artifacts that sign their
        // own canonical bytes: the attestation's shape check needs a syntactically valid
        // placeholder before the real signature exists, and the signature is never one of the
        // covered fields, so the placeholder cannot change what gets signed.
        var unsigned = Reconcile(coordinates, QuarantineFixtures.Attestation());
        var signature = key.SignData(
            QuarantineInventoryCanonicalizer.GetSigningBytes(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return Reconcile(
            coordinates,
            new QuarantineAttestation(
                QuarantineAttestation.ExpectedPurpose,
                QuarantineAttestation.ExpectedAlgorithm,
                QuarantineAttestation.ExpectedSignatureFormat,
                Base64Url.Encode(signature),
                QuarantineFixtures.Issuer()));
    }

    private static QuarantinedPriorCoordinateInventory Reconcile(
        IReadOnlyList<PriorPublicCoordinate> coordinates, QuarantineAttestation attestation)
    {
        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-a", coordinates);
        var reviewer = MustCreate(
            QuarantineReproducerRole.IndependentReviewer, "reviewer-run-b", coordinates);

        return QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            attestation,
            out var refusal)
            ?? throw new AssertFailedException($"Reconciliation refused: {refusal}");
    }

    private static QuarantinePriorCoordinateReproduction MustCreate(
        QuarantineReproducerRole role,
        string identity,
        IReadOnlyList<PriorPublicCoordinate> coordinates) =>
        QuarantinePriorCoordinateReproduction.TryCreate(role, identity, coordinates, out var refusal)
            ?? throw new AssertFailedException($"Reproduction refused: {refusal}");
}
