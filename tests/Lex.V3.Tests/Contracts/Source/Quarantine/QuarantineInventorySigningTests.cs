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
/// <see cref="QuarantineInventoryCanonicalizer.VerifySignature"/> checks a signature against exactly
/// those bytes rather than trusting the inventory's own stored claims, and that changing any one of
/// the fields the review named as covered breaks verification. Every key here is a fresh, ephemeral
/// P-256 key generated for this test only -- never the pinned review key backlog Candidate 2
/// section 7.3 step 6 hands to the canon/2 alias builder, which this branch deliberately does not
/// build (see the remarks on <see cref="QuarantineAttestation"/>).
/// </summary>
/// <remarks>
/// Refreeze fold-ins: <see cref="ChangingTheAttestationIssuerIdAfterSigningBreaksVerification"/> and
/// <see cref="ChangingTheAttestationKeyIdAfterSigningBreaksVerification"/> prove a signature cannot
/// be replayed under a different issuer id or key id; <see cref="GetSigningBytesMatchesAnIndependentlyComputedGoldenVector"/>
/// pins the framing and field order against a literal computed outside this function;
/// <see cref="ChangingTheVerifierReceiptProducedAtUtcAfterSigningBreaksVerification"/>,
/// <see cref="SwappingWhichReproductionIsPrimaryAfterSigningBreaksVerification"/>,
/// <see cref="ChangingTheIndependentReviewerIdentityAfterSigningBreaksVerification"/>,
/// <see cref="ChangingTheSourceIndexIdentityResourceIdAfterSigningBreaksVerification"/> and
/// <see cref="ChangingTheSourceIndexIdentitySha256AfterSigningBreaksVerification"/> each drive one
/// previously-undriven signed field on its own, so a field that changes together with another one
/// elsewhere in the fixture can no longer mask a coverage gap.
/// </remarks>
[TestClass]
public sealed class QuarantineInventorySigningTests
{
    [TestMethod]
    public void ASignatureOverTheCanonicalBytesVerifiesWithTheMatchingPublicKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var inventory = SignedInventory(key, QuarantineFixtures.CoordinateSet());
        using var publicOnly = PublicKeyOnly(key);

        var verified = QuarantineInventoryCanonicalizer.VerifySignature(inventory, publicOnly);

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
            () => QuarantineInventoryCanonicalizer.VerifySignature(inventory, wrongPublicKey));
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
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
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
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
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
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
    }

    /// <summary>
    /// Isolates <c>verifier_receipt.produced_at_utc</c> from <c>verifier_receipt.verifier_identity</c>
    /// (covered separately above): only the timestamp changes here, the verifier identity stays the
    /// fixture default, so this field's own coverage cannot be masked by the identity change.
    /// </summary>
    [TestMethod]
    public void ChangingTheVerifierReceiptProducedAtUtcAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var mutated = Reconcile(
            QuarantineFixtures.CoordinateSet(),
            signedAttestation,
            receipt: new QuarantineVerifierReceipt(
                "quarantine-verifier-run-a",
                operatedReadOnly: true,
                producedAtUtc: "2026-09-03T11:00:00Z"));
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
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
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
    }

    /// <summary>
    /// The independent reviewer's own identity, isolated from the primary's (covered separately
    /// above): only <c>independent_reviewer.reproducer_identity</c> changes here.
    /// </summary>
    [TestMethod]
    public void ChangingTheIndependentReviewerIdentityAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var mutated = Reconcile(
            QuarantineFixtures.CoordinateSet(),
            signedAttestation,
            reviewerIdentity: "a-different-reviewer-run");
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
    }

    /// <summary>
    /// <c>primary.role</c> and <c>independent_reviewer.role</c>, driven together deliberately: with
    /// only two <see cref="QuarantineReproducerRole"/> values and
    /// <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/> requiring the two supplied
    /// reproductions to declare different roles, the pair can only ever be
    /// (Primary, IndependentReviewer) or (IndependentReviewer, Primary) -- there is no legitimately
    /// constructed inventory where one flips without the other. This mints two reproductions whose
    /// roles are swapped relative to their usual identity ("writer-run-a" now reproduces under
    /// <see cref="QuarantineReproducerRole.IndependentReviewer"/>, "reviewer-run-b" now reproduces
    /// under <see cref="QuarantineReproducerRole.Primary"/>) so both role fields' text changes while
    /// both identity strings, in their same slots, stay exactly what was signed -- isolating the
    /// role fields from the identity fields covered elsewhere in this file.
    /// </summary>
    [TestMethod]
    public void SwappingWhichReproductionIsPrimaryAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var coordinates = QuarantineFixtures.CoordinateSet();
        var signedAttestation = SignedAttestation(key, coordinates);

        var primarySlotReproduction = MustCreate(
            QuarantineReproducerRole.IndependentReviewer, "writer-run-a", coordinates);
        var reviewerSlotReproduction = MustCreate(
            QuarantineReproducerRole.Primary, "reviewer-run-b", coordinates);

        var mutated = QuarantinedPriorCoordinateInventory.TryReconcile(
            primarySlotReproduction,
            reviewerSlotReproduction,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            signedAttestation,
            out var refusal);
        Assert.IsNotNull(mutated, $"fixture setup failed: {refusal}");
        Assert.AreEqual(QuarantineReproducerRole.IndependentReviewer, mutated.PrimaryReproducerRole);
        Assert.AreEqual("writer-run-a", mutated.PrimaryReproducerIdentity);
        Assert.AreEqual(QuarantineReproducerRole.Primary, mutated.IndependentReviewerReproducerRole);
        Assert.AreEqual("reviewer-run-b", mutated.IndependentReviewerReproducerIdentity);
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
    }

    /// <summary>
    /// <c>source_index_identity_ref.resource_id</c>, isolated from
    /// <c>source_index_identity_ref.sha256</c> (covered separately below): only the resource id
    /// changes here, so the two fields cannot mask each other the way a single combined mutation
    /// (changing both at once) would.
    /// </summary>
    [TestMethod]
    public void ChangingTheSourceIndexIdentityResourceIdAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var mutated = Reconcile(
            QuarantineFixtures.CoordinateSet(),
            signedAttestation,
            sourceIndexIdentityRef: new SourceArtifactRef(
                "urn:uuid:22222222-2222-4222-8222-222222222222",
                new string('1', 64)));
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
    }

    /// <summary>
    /// <c>source_index_identity_ref.sha256</c>, isolated from
    /// <c>source_index_identity_ref.resource_id</c> (covered separately above): only the sha256
    /// changes here.
    /// </summary>
    [TestMethod]
    public void ChangingTheSourceIndexIdentitySha256AfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var mutated = Reconcile(
            QuarantineFixtures.CoordinateSet(),
            signedAttestation,
            sourceIndexIdentityRef: new SourceArtifactRef(
                "urn:uuid:11111111-1111-4111-8111-111111111111",
                new string('9', 64)));
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
    }

    /// <summary>
    /// Fold-in 2: an attestation's issuer id and key id are covered by
    /// <see cref="QuarantineInventoryCanonicalizer.GetSigningBytes"/>, so a valid signature cannot be
    /// re-attributed to a different issuer id by re-wrapping it in a
    /// <see cref="QuarantineAttestation"/> that names one -- every other field, including the
    /// signature bytes themselves, is identical to what was actually signed.
    /// </summary>
    [TestMethod]
    public void ChangingTheAttestationIssuerIdAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var replayedAttestation = new QuarantineAttestation(
            signedAttestation.Purpose,
            signedAttestation.Algorithm,
            signedAttestation.SignatureFormat,
            signedAttestation.Signature,
            QuarantineFixtures.Issuer(issuerId: "a-different-issuer"));
        var mutated = Reconcile(QuarantineFixtures.CoordinateSet(), replayedAttestation);
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
    }

    /// <summary>
    /// Fold-in 2's other half: the same replay, but only the key id differs from what was signed --
    /// same issuer id, same signature bytes, same everything else.
    /// </summary>
    [TestMethod]
    public void ChangingTheAttestationKeyIdAfterSigningBreaksVerification()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signedAttestation = SignedAttestation(key, QuarantineFixtures.CoordinateSet());
        var replayedAttestation = new QuarantineAttestation(
            signedAttestation.Purpose,
            signedAttestation.Algorithm,
            signedAttestation.SignatureFormat,
            signedAttestation.Signature,
            QuarantineFixtures.Issuer(keyId: "a-different-key"));
        var mutated = Reconcile(QuarantineFixtures.CoordinateSet(), replayedAttestation);
        using var publicOnly = PublicKeyOnly(key);

        Assert.ThrowsExactly<ArgumentException>(
            () => QuarantineInventoryCanonicalizer.VerifySignature(mutated, publicOnly));
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

    /// <summary>
    /// Fold-in 3(a): a golden byte vector. The <c>expectedText</c> literal below was computed
    /// independently of <see cref="QuarantineInventoryCanonicalizer.GetSigningBytes"/> -- by
    /// hand-transcribing this type's documented domain string and length-prefixed
    /// name/value framing, field order, and enum <c>ToString()</c> text, plus a standalone Python
    /// <c>hashlib.sha256</c> computation (not this assembly's SHA-256, and not
    /// <see cref="PriorPublicCoordinateSet.CanonicalSha256Hex"/>) over the documented canonical
    /// coordinate-bytes framing for this fixed fixture's one coordinate -- then transcribing the
    /// result into this literal. Nothing here calls <see cref="QuarantineInventoryCanonicalizer.GetSigningBytes"/>,
    /// <see cref="PriorPublicCoordinateSet.CanonicalBytes"/> or
    /// <see cref="PriorPublicCoordinateSet.CanonicalSha256Hex"/> to build the expectation, so a
    /// regression in this type's framing or field order has something other than its own output to
    /// disagree with.
    /// </summary>
    [TestMethod]
    public void GetSigningBytesMatchesAnIndependentlyComputedGoldenVector()
    {
        var inventory = Reconcile(new[] { QuarantineFixtures.Coordinate() }, QuarantineFixtures.Attestation());

        var actual = QuarantineInventoryCanonicalizer.GetSigningBytes(inventory);

        var expectedText =
            "lex-v3-quarantine-prior-coordinate-inventory-signature/1\n"
            + "6:schema=47:lex-v3-quarantined-prior-coordinate-inventory/1\n"
            + "5:count=1:1\n"
            + "21:coordinate_set_sha256=64:064ebd8517d33415df55e9d02d29260c505b09697a0fb0ab8d7850002e506f0d\n"
            + "23:prior_index_pair_sha256=64:2222222222222222222222222222222222222222222222222222222222222222\n"
            + "37:source_index_identity_ref.resource_id=45:urn:uuid:11111111-1111-4111-8111-111111111111\n"
            + "32:source_index_identity_ref.sha256=64:1111111111111111111111111111111111111111111111111111111111111111\n"
            + "34:verifier_receipt.verifier_identity=25:quarantine-verifier-run-a\n"
            + "35:verifier_receipt.operated_read_only=4:true\n"
            + "32:verifier_receipt.produced_at_utc=20:2026-09-03T10:00:00Z\n"
            + "28:attestation.issuer.issuer_id=21:quarantine-reviewer-1\n"
            + "25:attestation.issuer.key_id=5:key-1\n"
            + "12:primary.role=7:Primary\n"
            + "27:primary.reproducer_identity=12:writer-run-a\n"
            + "25:independent_reviewer.role=19:IndependentReviewer\n"
            + "40:independent_reviewer.reproducer_identity=14:reviewer-run-b\n"
            + "23:coordinates[0].work_key=40:lu-legilux:eli/etat/leg/loi/2020-01-01/1\n"
            + "23:coordinates[0].language=2:fr\n"
            + "25:coordinates[0].valid_from=10:2020-01-01\n"
            + "21:coordinates[0].anchor=7:art_1er\n";
        var expected = Encoding.UTF8.GetBytes(expectedText);

        CollectionAssert.AreEqual(expected, actual);
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
        // attestation's signature is never one of the covered fields (it cannot sign itself) --
        // though its issuer id and key id now are (fold-in 2), so this helper's fixed
        // QuarantineFixtures.Issuer() must stay the same issuer used to build the unsigned form
        // below and whatever the caller later reconciles with the resulting attestation.
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
}
