using Lex.V3.Contracts.Source.Quarantine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

/// <summary>
/// Objection 4: drives every validation branch on <see cref="QuarantineVerifierReceipt"/>,
/// <see cref="QuarantineIssuer"/> and <see cref="QuarantineAttestation"/>. None of the three had a
/// dedicated test before this branch, so every throw in their constructors was undriven.
/// </summary>
[TestClass]
public sealed class QuarantineEvidenceValidationTests
{
    // ---- QuarantineVerifierReceipt ----

    [TestMethod]
    public void AReceiptAcceptsATypicalReadOnlyRun()
    {
        var receipt = new QuarantineVerifierReceipt(
            "quarantine-verifier-run-a", operatedReadOnly: true, producedAtUtc: "2026-09-03T10:00:00Z");

        Assert.AreEqual("quarantine-verifier-run-a", receipt.VerifierIdentity);
        Assert.IsTrue(receipt.OperatedReadOnly);
        Assert.AreEqual("2026-09-03T10:00:00Z", receipt.ProducedAtUtc);
    }

    [TestMethod]
    public void AReceiptRefusesAnAssertionThatIsNotReadOnly() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineVerifierReceipt(
            "quarantine-verifier-run-a", operatedReadOnly: false, producedAtUtc: "2026-09-03T10:00:00Z"));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AReceiptVerifierIdentityMustNotBeBlank(string identity) =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineVerifierReceipt(
            identity, operatedReadOnly: true, producedAtUtc: "2026-09-03T10:00:00Z"));

    [TestMethod]
    public void AReceiptVerifierIdentityMustNotExceedTheBound() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineVerifierReceipt(
            new string('a', 257), operatedReadOnly: true, producedAtUtc: "2026-09-03T10:00:00Z"));

    [TestMethod]
    public void AReceiptVerifierIdentityRejectsControlCharacters() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineVerifierReceipt(
            "run\twith\ttab", operatedReadOnly: true, producedAtUtc: "2026-09-03T10:00:00Z"));

    /// <summary>
    /// Note: the receipt and issuer identity predicates used to disagree on whether a space was
    /// allowed (the receipt forbade it, the issuer allowed it). Both now go through the shared
    /// <c>ContractValidation.RequireIdentifier</c>, so a space is accepted here exactly as it
    /// already was for <see cref="QuarantineIssuer"/> -- see
    /// <see cref="AnIssuerAcceptsASpaceInEitherIdentifier"/>.
    /// </summary>
    [TestMethod]
    public void AReceiptVerifierIdentityAcceptsASpaceSameAsIssuerIdentifiers()
    {
        var receipt = new QuarantineVerifierReceipt(
            "quarantine verifier run a", operatedReadOnly: true, producedAtUtc: "2026-09-03T10:00:00Z");
        Assert.AreEqual("quarantine verifier run a", receipt.VerifierIdentity);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("2026-09-03")]
    [DataRow("2026-09-03T10:00:00")]
    [DataRow("2026-09-03T10:00:00+00:00")]
    [DataRow("2026-09-03T10:00:00.000Z")]
    [DataRow("not-a-timestamp")]
    public void AReceiptTimestampMustBeAnExactInstant(string producedAtUtc) =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineVerifierReceipt(
            "quarantine-verifier-run-a", operatedReadOnly: true, producedAtUtc: producedAtUtc));

    // ---- QuarantineIssuer ----

    [TestMethod]
    public void AnIssuerAcceptsItsExpectedRole()
    {
        var issuer = new QuarantineIssuer(QuarantineIssuer.ExpectedRole, "quarantine-reviewer-1", "key-1");
        Assert.AreEqual(QuarantineIssuer.ExpectedRole, issuer.Role);
        Assert.AreEqual("quarantine-reviewer-1", issuer.IssuerId);
        Assert.AreEqual("key-1", issuer.KeyId);
    }

    [TestMethod]
    [DataRow("preview_attestor")]
    [DataRow("")]
    [DataRow("QUARANTINE_REVIEWER")]
    public void AnIssuerRejectsAnyOtherRole(string role) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new QuarantineIssuer(role, "quarantine-reviewer-1", "key-1"));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AnIssuerIdMustNotBeBlank(string issuerId) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new QuarantineIssuer(QuarantineIssuer.ExpectedRole, issuerId, "key-1"));

    [TestMethod]
    public void AnIssuerIdMustNotExceedTheBound() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineIssuer(
            QuarantineIssuer.ExpectedRole, new string('a', 257), "key-1"));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AKeyIdMustNotBeBlank(string keyId) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new QuarantineIssuer(QuarantineIssuer.ExpectedRole, "quarantine-reviewer-1", keyId));

    [TestMethod]
    public void AKeyIdMustNotExceedTheBound() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineIssuer(
            QuarantineIssuer.ExpectedRole, "quarantine-reviewer-1", new string('a', 257)));

    [TestMethod]
    public void AnIssuerAcceptsASpaceInEitherIdentifier()
    {
        var issuer = new QuarantineIssuer(QuarantineIssuer.ExpectedRole, "quarantine reviewer 1", "key one");
        Assert.AreEqual("quarantine reviewer 1", issuer.IssuerId);
        Assert.AreEqual("key one", issuer.KeyId);
    }

    // ---- QuarantineAttestation ----

    [TestMethod]
    public void AnAttestationAcceptsItsExpectedShape()
    {
        var attestation = QuarantineFixtures.Attestation();
        Assert.AreEqual(QuarantineAttestation.ExpectedPurpose, attestation.Purpose);
        Assert.AreEqual(QuarantineAttestation.ExpectedAlgorithm, attestation.Algorithm);
        Assert.AreEqual(QuarantineAttestation.ExpectedSignatureFormat, attestation.SignatureFormat);
        Assert.AreEqual(86, attestation.Signature.Length);
    }

    [TestMethod]
    public void AnAttestationRejectsAnyOtherPurpose() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineAttestation(
            "preview_mechanics_only",
            QuarantineAttestation.ExpectedAlgorithm,
            QuarantineAttestation.ExpectedSignatureFormat,
            new string('A', 86),
            QuarantineFixtures.Issuer()));

    [TestMethod]
    public void AnAttestationRejectsAnyOtherAlgorithm() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineAttestation(
            QuarantineAttestation.ExpectedPurpose,
            "ECDSA-P384-SHA384",
            QuarantineAttestation.ExpectedSignatureFormat,
            new string('A', 86),
            QuarantineFixtures.Issuer()));

    [TestMethod]
    public void AnAttestationRejectsAnyOtherSignatureFormat() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineAttestation(
            QuarantineAttestation.ExpectedPurpose,
            QuarantineAttestation.ExpectedAlgorithm,
            "der",
            new string('A', 86),
            QuarantineFixtures.Issuer()));

    [TestMethod]
    public void AnAttestationRejectsANullSignature() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new QuarantineAttestation(
            QuarantineAttestation.ExpectedPurpose,
            QuarantineAttestation.ExpectedAlgorithm,
            QuarantineAttestation.ExpectedSignatureFormat,
            null!,
            QuarantineFixtures.Issuer()));

    [TestMethod]
    public void AnAttestationRejectsAnEmptySignature() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineAttestation(
            QuarantineAttestation.ExpectedPurpose,
            QuarantineAttestation.ExpectedAlgorithm,
            QuarantineAttestation.ExpectedSignatureFormat,
            string.Empty,
            QuarantineFixtures.Issuer()));

    [TestMethod]
    public void AnAttestationRejectsASignatureOfTheWrongLength() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineAttestation(
            QuarantineAttestation.ExpectedPurpose,
            QuarantineAttestation.ExpectedAlgorithm,
            QuarantineAttestation.ExpectedSignatureFormat,
            new string('A', 85),
            QuarantineFixtures.Issuer()));

    [TestMethod]
    public void AnAttestationRejectsASignatureWithAPaddingCharacter() =>
        Assert.ThrowsExactly<ArgumentException>(() => new QuarantineAttestation(
            QuarantineAttestation.ExpectedPurpose,
            QuarantineAttestation.ExpectedAlgorithm,
            QuarantineAttestation.ExpectedSignatureFormat,
            new string('A', 85) + "=",
            QuarantineFixtures.Issuer()));

    [TestMethod]
    public void AnAttestationRejectsANullIssuer() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new QuarantineAttestation(
            QuarantineAttestation.ExpectedPurpose,
            QuarantineAttestation.ExpectedAlgorithm,
            QuarantineAttestation.ExpectedSignatureFormat,
            new string('A', 86),
            null!));
}
