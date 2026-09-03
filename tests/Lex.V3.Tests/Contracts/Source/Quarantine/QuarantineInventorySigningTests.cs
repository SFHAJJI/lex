using System.Security.Cryptography;
using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Quarantine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

/// <summary>
/// Objection 1: proves that <see cref="QuarantineInventoryCanonicalizer.GetSigningBytes"/> defines
/// what a quarantined inventory's attestation signs, that
/// <see cref="QuarantineInventoryCanonicalizer.ParseAndVerify"/> checks a signature against exactly
/// those bytes rather than trusting the inventory's own stored claims, and that changing any one of
/// the fields the review named as covered breaks verification. Every key here is a fresh, ephemeral
/// P-256 key generated for this test only -- never the pinned review key backlog Candidate 2
/// section 7.3 step 6 hands to the canon/2 alias builder, which this branch deliberately does not
/// build (see the remarks on <see cref="QuarantineAttestation"/>).
/// </summary>
[TestClass]
public sealed class QuarantineInventorySigningTests
{
    [TestMethod]
    public void ASignatureOverTheCanonicalBytesVerifiesWithTheMatchingPublicKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var inventory = SignedInventory(key, QuarantineFixtures.CoordinateSet());
        using var publicOnly = PublicKeyOnly(key);

        var verified = QuarantineInventoryCanonicalizer.ParseAndVerify(inventory, publicOnly);

        Assert.AreSame(inventory, verified);
    }

    [TestMethod]
    public void AWrongPublicKeyFailsVerification()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var inventory = SignedInventory(signer, QuarantineFixtures.CoordinateSet());
        using var wrongPublicKey = PublicKeyOnly(otherKey);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.ParseAndVerify(inventory, wrongPublicKey));
    }

    /// <summary>
    /// The core mutation proof: sign once, then rebuild an inventory that differs in exactly one
    /// covered field but carries the SAME already-computed signature. Verification must fail for
    /// every field the review named as covered -- this is not a single example but one case per
    /// covered field group.
    /// </summary>
    [TestMethod]
    public void ChangingTheCoordinatesAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var differentCoordinates = QuarantineFixtures.CoordinateSet().Take(2).ToArray();
        var mutated = Reconcile(differentCoordinates, signedAttestation);
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.ParseAndVerify(mutated, publicOnly));
    }

    [TestMethod]
    public void ChangingThePriorIndexPairHashAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var mutated = Reconcile(
            QuarantineFixtures.CoordinateSet(),
            signedAttestation,
            priorIndexPairSha256: new string('3', 64));
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.ParseAndVerify(mutated, publicOnly));
    }

    [TestMethod]
    public void ChangingTheVerifierReceiptAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var mutated = Reconcile(
            QuarantineFixtures.CoordinateSet(),
            signedAttestation,
            receipt: QuarantineFixtures.Receipt(verifierIdentity: "a-different-verifier-run"));
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.ParseAndVerify(mutated, publicOnly));
    }

    [TestMethod]
    public void ChangingThePrimaryReproducerIdentityAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var mutated = Reconcile(
            QuarantineFixtures.CoordinateSet(),
            signedAttestation,
            primaryIdentity: "a-different-primary-run");
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.ParseAndVerify(mutated, publicOnly));
    }

    [TestMethod]
    public void ChangingTheSourceIndexIdentityAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var mutated = Reconcile(
            QuarantineFixtures.CoordinateSet(),
            signedAttestation,
            sourceIndexIdentityRef: SourceArtifactRefFixture());
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.ParseAndVerify(mutated, publicOnly));
    }

    [TestMethod]
    public void TheSigningBytesReferenceTheSchemaIdAndTheReDerivedDigestAsUtf8Text()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var inventory = SignedInventory(key, QuarantineFixtures.CoordinateSet());

        var bytes = QuarantineInventoryCanonicalizer.GetSigningBytes(inventory);
        var text = Encoding.UTF8.GetString(bytes);
        var recomputedDigest = PriorPublicCoordinateSet.CanonicalSha256Hex(inventory.Coordinates);

        StringAssert.Contains(text, QuarantinedPriorCoordinateInventory.SchemaId);
        StringAssert.Contains(text, recomputedDigest);
        Assert.AreEqual(inventory.CoordinateSetSha256, recomputedDigest);
    }

    // ---- fixtures ----

    private static QuarantinedPriorCoordinateInventory SignedInventory(
        ECDsa key,
        IReadOnlyList<PriorPublicCoordinate> coordinates) =>
        Reconcile(coordinates, SignedAttestation(key, coordinates));

    private static QuarantineAttestation SignedAttestation(
        ECDsa key,
        IReadOnlyList<PriorPublicCoordinate> coordinates)
    {
        // Sign-then-rebuild, matching this codebase's existing pattern for artifacts that sign
        // their own canonical bytes (see SyntheticArtifactVerifierTests): the attestation's shape
        // check needs some syntactically valid 86-character placeholder before the real signature
        // exists, and the placeholder's content does not affect GetSigningBytes because the
        // attestation itself is never one of the covered fields (it cannot sign itself).
        var unsigned = Reconcile(coordinates, QuarantineFixtures.Attestation());
        var signature = key.SignData(
            QuarantineInventoryCanonicalizer.GetSigningBytes(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return new QuarantineAttestation(
            QuarantineAttestation.ExpectedPurpose,
            QuarantineAttestation.ExpectedAlgorithm,
            QuarantineAttestation.ExpectedSignatureFormat,
            Base64Url.Encode(signature),
            QuarantineFixtures.Issuer());
    }

    private static QuarantinedPriorCoordinateInventory Reconcile(
        IReadOnlyList<PriorPublicCoordinate> coordinates,
        QuarantineAttestation attestation,
        string? priorIndexPairSha256 = null,
        SourceArtifactRef? sourceIndexIdentityRef = null,
        QuarantineVerifierReceipt? receipt = null,
        string primaryIdentity = "writer-run-a",
        string reviewerIdentity = "reviewer-run-b")
    {
        var primary = MustCreate(QuarantineReproducerRole.Primary, primaryIdentity, coordinates);
        var reviewer = MustCreate(QuarantineReproducerRole.IndependentReviewer, reviewerIdentity, coordinates);

        var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            priorIndexPairSha256 ?? QuarantineFixtures.PriorIndexPairSha256(),
            sourceIndexIdentityRef ?? QuarantineFixtures.SourceIndexIdentity(),
            receipt ?? QuarantineFixtures.Receipt(),
            attestation,
            out var refusal);

        Assert.IsNotNull(inventory, $"fixture setup failed: {refusal}");
        return inventory;
    }

    private static QuarantinePriorCoordinateReproduction MustCreate(
        QuarantineReproducerRole role,
        string identity,
        IReadOnlyList<PriorPublicCoordinate> coordinates)
    {
        var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(role, identity, coordinates, out var refusal);
        Assert.IsNotNull(reproduction, $"fixture setup failed: {refusal}");
        return reproduction;
    }

    private static ECDsa PublicKeyOnly(ECDsa key)
    {
        var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);
        return publicKey;
    }

    private static SourceArtifactRef SourceArtifactRefFixture() =>
        new(
            "urn:uuid:22222222-2222-4222-8222-222222222222",
            new string('9', 64));
}
