using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Source.Quarantine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

/// <summary>
/// The document V3 accepts as the canon/2 prior-coordinate input, read the way the specification
/// beside it says. Every document here is built as the external quarantined tool would build one --
/// V3 has no writer for this form and must not acquire one, since producing it is that tool's job.
/// </summary>
[TestClass]
public sealed class QuarantineInventoryWireReaderTests
{
    private static readonly JsonSerializerOptions WireOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>
    /// The whole chain: a document produced as specified is parsed, reconciled HERE, and verifies
    /// under a key a trust store pins. If any link were trusted rather than repeated, this fails.
    /// </summary>
    [TestMethod]
    public void ADocumentProducedAsSpecifiedParsesReconcilesAndVerifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = SignedDocument(key);

        var inventory = QuarantineInventoryWireReader.TryParse(json, out var refusal, out var detail);

        Assert.IsNotNull(inventory, $"{refusal}: {detail}");
        Assert.AreEqual(QuarantineInventoryWireRefusal.None, refusal);
        Assert.HasCount(2, inventory!.Coordinates);

        // The digest was never in the document: it is derived here from the coordinates alone.
        Assert.AreEqual(
            PriorPublicCoordinateSet.CanonicalSha256Hex(inventory.Coordinates),
            inventory.CoordinateSetSha256);

        var store = new FakeTrustStore("quarantine-reviewer-1", "key-1", key.ExportSubjectPublicKeyInfo());
        var admitted = TrustedQuarantinedPriorCoordinateInventory.TryAdmit(
            inventory, store, out var trustRefusal, out var trustDetail);

        Assert.IsNotNull(admitted, $"{trustRefusal}: {trustDetail}");
    }

    /// <summary>
    /// A document that supplies a coordinate digest is refused rather than having it ignored. This
    /// is the field that would let two disagreeing walks be presented as agreeing.
    /// </summary>
    [TestMethod]
    public void ADocumentThatSuppliesACoordinateDigestIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = SignedDocument(key)
            .Replace("\"schema\":", "\"coordinate_set_sha256\":\"" + new string('a', 64) + "\",\"schema\":",
                StringComparison.Ordinal);

        var inventory = QuarantineInventoryWireReader.TryParse(json, out var refusal, out _);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryWireRefusal.NotOneValidTypedDocument, refusal);
    }

    [TestMethod]
    public void ADocumentDeclaringAnotherSchemaIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = SignedDocument(key).Replace(
            QuarantineInventoryWireReader.SchemaId,
            "lex-v3-quarantined-prior-coordinate-inventory/1",
            StringComparison.Ordinal);

        var inventory = QuarantineInventoryWireReader.TryParse(json, out var refusal, out var detail);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryWireRefusal.UnexpectedSchema, refusal);
        StringAssert.Contains(detail, QuarantineInventoryWireReader.SchemaId);
    }

    [TestMethod]
    public void ADocumentCarryingOneReproductionIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = Document(key, reproductions: [Reproduction("Primary", "writer-run-a", Coordinates())]);

        var inventory = QuarantineInventoryWireReader.TryParse(json, out var refusal, out var detail);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryWireRefusal.ReproductionCountNotTwo, refusal);
        StringAssert.Contains(detail, "1 reproductions");
    }

    [TestMethod]
    public void ANullReproductionIsRefusedByName()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = Document(key, reproductions:
        [
            null!,
            Reproduction("IndependentReviewer", "reviewer-run-b", Coordinates()),
        ]);

        var inventory = QuarantineInventoryWireReader.TryParse(json, out var refusal, out var detail);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryWireRefusal.NotOneValidTypedDocument, refusal);
        StringAssert.Contains(detail, "reproductions[0]");
    }

    [TestMethod]
    public void ANullCoordinateIsRefusedByName()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = Document(key, reproductions:
        [
            Reproduction("Primary", "writer-run-a", [null!]),
            Reproduction("IndependentReviewer", "reviewer-run-b", Coordinates()),
        ]);

        var inventory = QuarantineInventoryWireReader.TryParse(json, out var refusal, out var detail);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryWireRefusal.CoordinateInvalid, refusal);
        StringAssert.Contains(detail, "reproductions[0].coordinates[0]");
    }

    /// <summary>
    /// Two walks that disagree reach the reconciliation refusal through the reader. This is the
    /// content-disagreement check working end to end rather than only in a unit fixture.
    /// </summary>
    [TestMethod]
    public void TwoReproductionsThatDisagreeAreRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var shorter = Coordinates().Take(1).ToArray();
        var json = Document(key, reproductions:
        [
            Reproduction("Primary", "writer-run-a", Coordinates()),
            Reproduction("IndependentReviewer", "reviewer-run-b", shorter),
        ]);

        var inventory = QuarantineInventoryWireReader.TryParse(json, out var refusal, out var detail);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryWireRefusal.ReconciliationRefused, refusal);
        StringAssert.Contains(detail, "ReproductionCountMismatch");
    }

    /// <summary>
    /// Which reproduction is primary is its declared role, never its position. The signed bytes name
    /// the roles, so a reader that trusted order would reconcile a different pair than was signed.
    /// </summary>
    [TestMethod]
    public void ListingTheReviewerFirstStillReconcilesAndVerifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = SignedDocument(key, reviewerFirst: true);

        var inventory = QuarantineInventoryWireReader.TryParse(json, out var refusal, out var detail);

        Assert.IsNotNull(inventory, $"{refusal}: {detail}");
        Assert.AreEqual(QuarantineReproducerRole.Primary, inventory!.PrimaryReproducerRole);
        Assert.AreEqual("writer-run-a", inventory.PrimaryReproducerIdentity);

        var store = new FakeTrustStore("quarantine-reviewer-1", "key-1", key.ExportSubjectPublicKeyInfo());
        Assert.IsNotNull(
            TrustedQuarantinedPriorCoordinateInventory.TryAdmit(inventory, store, out _, out _),
            "the signature must still verify when the document lists the reviewer first");
    }

    [TestMethod]
    public void ACoordinateShapedLikeAPathIsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = SignedDocument(key).Replace(
            "\"work_key\":\"lex-work-a\"", "\"work_key\":\"works/a.xml\"", StringComparison.Ordinal);

        var inventory = QuarantineInventoryWireReader.TryParse(json, out var refusal, out _);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryWireRefusal.CoordinateInvalid, refusal);
    }

    // ---- Fixtures: the external tool's side, never V3's. ----

    private static object[] Coordinates() =>
    [
        new { work_key = "lex-work-a", language = "fr", valid_from = "2019-01-01", anchor = (string?)null },
        new { work_key = "lex-work-b", language = "de", valid_from = "2020-06-30", anchor = "art-3" },
    ];

    private static object Reproduction(string role, string identity, object[] coordinates) =>
        new { role, reproducer_identity = identity, coordinates };

    private static string SignedDocument(ECDsa key, bool reviewerFirst = false)
    {
        var reproductions = reviewerFirst
            ?
            [
                Reproduction("IndependentReviewer", "reviewer-run-b", Coordinates()),
                Reproduction("Primary", "writer-run-a", Coordinates()),
            ]
            : new[]
            {
                Reproduction("Primary", "writer-run-a", Coordinates()),
                Reproduction("IndependentReviewer", "reviewer-run-b", Coordinates()),
            };

        return Document(key, reproductions, sign: true);
    }

    private static string Document(ECDsa key, object[] reproductions, bool sign = false)
    {
        var signature = sign ? RealSignature(key) : new string('A', 86);
        return JsonSerializer.Serialize(
            new
            {
                schema = QuarantineInventoryWireReader.SchemaId,
                prior_index_pair_sha256 = new string('2', 64),
                source_index_identity_ref = new
                {
                    resource_id = "urn:uuid:11111111-1111-4111-8111-111111111111",
                    sha256 = new string('3', 64),
                },
                verifier_receipt = new
                {
                    verifier_identity = "quarantine-verifier-run-a",
                    operated_read_only = true,
                    produced_at_utc = "2026-09-15T00:00:00Z",
                },
                reproductions,
                attestation = new
                {
                    purpose = QuarantineAttestation.ExpectedPurpose,
                    algorithm = QuarantineAttestation.ExpectedAlgorithm,
                    signature_format = QuarantineAttestation.ExpectedSignatureFormat,
                    signature,
                    issuer = new
                    {
                        role = QuarantineIssuer.ExpectedRole,
                        issuer_id = "quarantine-reviewer-1",
                        key_id = "key-1",
                    },
                },
            },
            WireOptions);
    }

    /// <summary>
    /// The external tool must reconcile in order to know what to sign, exactly as the specification
    /// says. This reproduces that: reconcile with a placeholder, sign the resulting inventory's own
    /// canonical bytes, then emit the document carrying the real signature.
    /// </summary>
    private static string RealSignature(ECDsa key)
    {
        var coordinates = new[]
        {
            new PriorPublicCoordinate("lex-work-a", "fr", "2019-01-01", null),
            new PriorPublicCoordinate("lex-work-b", "de", "2020-06-30", "art-3"),
        };
        var primary = QuarantinePriorCoordinateReproduction.TryCreate(
            QuarantineReproducerRole.Primary, "writer-run-a", coordinates, out _)!;
        var reviewer = QuarantinePriorCoordinateReproduction.TryCreate(
            QuarantineReproducerRole.IndependentReviewer, "reviewer-run-b", coordinates, out _)!;

        var placeholder = new QuarantineAttestation(
            QuarantineAttestation.ExpectedPurpose,
            QuarantineAttestation.ExpectedAlgorithm,
            QuarantineAttestation.ExpectedSignatureFormat,
            new string('A', 86),
            new QuarantineIssuer(QuarantineIssuer.ExpectedRole, "quarantine-reviewer-1", "key-1"));

        var unsigned = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            new string('2', 64),
            new Lex.V3.Contracts.Source.Core.SourceArtifactRef(
                "urn:uuid:11111111-1111-4111-8111-111111111111", new string('3', 64)),
            new QuarantineVerifierReceipt("quarantine-verifier-run-a", true, "2026-09-15T00:00:00Z"),
            placeholder,
            out _)!;

        return Base64Url.Encode(key.SignData(
            QuarantineInventoryCanonicalizer.GetSigningBytes(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    private sealed class FakeTrustStore(string issuerId, string keyId, byte[] subjectPublicKeyInfo)
        : IQuarantineTrustStore
    {
        public bool ContainsIssuer(string candidate) =>
            string.Equals(candidate, issuerId, StringComparison.Ordinal);

        public bool TryResolveVerificationKey(string candidateIssuer, string candidateKey, out ECDsa? publicKey)
        {
            publicKey = null;
            if (!string.Equals(candidateIssuer, issuerId, StringComparison.Ordinal) ||
                !string.Equals(candidateKey, keyId, StringComparison.Ordinal))
            {
                return false;
            }

            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            publicKey = key;
            return true;
        }
    }
}
