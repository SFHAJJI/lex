using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Quarantine;

/// <summary>
/// Evidence the external, quarantined V2-coordinate tool asserts about its own run: who it was,
/// that it operated read-only, and when it produced the reproduction it is attached to.
/// </summary>
/// <remarks>
/// This is declared evidence, exactly as <c>AbsenceCut</c>'s remarks name what a proof leaves
/// declared rather than checked. Nothing in V3 can independently confirm that the external tool
/// actually ran read-only against the pinned prior index pair, because confirming that would
/// require a V2 index reader in V3, which backlog Candidate 2 section 7.3 forbids outright: "the
/// V3 repository cannot contain or execute a V2 index reader." What V3 checks is that the receipt
/// is present, from a bounded identity, asserts read-only, and is time-stamped -- not that the
/// assertion is true.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuarantineVerifierReceipt
{
    [JsonConstructor]
    public QuarantineVerifierReceipt(string verifierIdentity, bool operatedReadOnly, string producedAtUtc)
    {
        VerifierIdentity = ContractValidation.RequireIdentifier(verifierIdentity, nameof(verifierIdentity));
        if (!operatedReadOnly)
        {
            throw new ArgumentException(
                "A quarantined verifier receipt must assert operatedReadOnly=true: section 7.3 "
                + "forbids network writes, source mutation, signing, publication and production "
                + "access from the tool it describes.",
                nameof(operatedReadOnly));
        }

        OperatedReadOnly = true;
        ProducedAtUtc = RequireTimestamp(producedAtUtc, nameof(producedAtUtc));
    }

    public string VerifierIdentity { get; }

    public bool OperatedReadOnly { get; }

    /// <summary>Exact <c>yyyy-MM-ddTHH:mm:ssZ</c> instant.</summary>
    public string ProducedAtUtc { get; }

    private static string RequireTimestamp(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-ddTHH:mm:ssZ",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out _))
        {
            throw new ArgumentException(
                "A verifier receipt timestamp must be an exact yyyy-MM-ddTHH:mm:ssZ instant.",
                parameterName);
        }

        return value;
    }
}

/// <summary>The dedicated non-production review identity backlog Candidate 2 section 7.3 step 4 requires.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuarantineIssuer
{
    public const string ExpectedRole = "quarantine_reviewer";

    [JsonConstructor]
    public QuarantineIssuer(string role, string issuerId, string keyId)
    {
        if (!string.Equals(role, ExpectedRole, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Quarantine issuer role must be {ExpectedRole}.", nameof(role));
        }

        Role = role;
        IssuerId = ContractValidation.RequireIdentifier(issuerId, nameof(issuerId));
        KeyId = ContractValidation.RequireIdentifier(keyId, nameof(keyId));
    }

    public string Role { get; }

    public string IssuerId { get; }

    public string KeyId { get; }
}

/// <summary>
/// A structurally valid attestation over a quarantined prior-coordinate inventory, in the exact
/// algorithm, wire format and encoding this codebase already uses for signed artifacts: P1363
/// fixed-field ECDSA over P-256, unpadded base64url (see <c>PreviewAttestation</c> in
/// <c>src/Lex.V3.Contracts/PreviewArtifactManifest.cs</c>, and the identical shape in
/// <c>SyntheticSliceArtifact.cs</c>).
/// </summary>
/// <remarks>
/// <para>
/// The purpose and issuer role are quarantine-specific rather than reused from
/// <c>PreviewAttestation</c>/<c>PreviewIssuer</c>, because those two constants are hard-wired to
/// preview semantics ("preview_mechanics_only", "preview_attestor"). Reusing the type outright
/// would either falsely claim preview purpose for an unrelated artifact family or require
/// relaxing <c>PreviewAttestation</c>'s own invariant to fit a caller it was never meant to admit.
/// What is reused, deliberately and exactly, is the signing mechanism: same algorithm constant,
/// same signature format constant, same base64url-encoded-P1363-pair shape check.
/// </para>
/// <para>
/// This type checks only that the signature is shaped correctly; it does not itself construct an
/// <see cref="System.Security.Cryptography.ECDsa"/> or check the signature against a key. What
/// section 7.3 forbids from V3, and what stays absent here, is the V2 index reader that would let
/// V3 independently re-derive coordinates from real V2 bytes -- not signature verification as such.
/// <c>QuarantineInventoryCanonicalizer</c> beside this type defines exactly which bytes this
/// signature covers (<c>GetSigningBytes</c>) and can verify a signature against a caller-supplied
/// public key (<c>ParseAndVerify</c>), so the signable/verifiable form this record's signature is
/// bound to genuinely exists in V3. What is still deliberately out of scope here is the
/// trust-store-backed verifier that decides whether a given issuer and key are the pinned review
/// identity for this purpose: that decision belongs to whichever later package actually consumes a
/// signed inventory (D3-05, the canon/2 alias builder; section 7.3 step 6, "Feed only that signed
/// neutral inventory to the V3 canon/2 alias builder").
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuarantineAttestation
{
    public const string ExpectedPurpose = "quarantine_prior_coordinate_inventory";
    public const string ExpectedAlgorithm = "ECDSA-P256-SHA256";
    public const string ExpectedSignatureFormat = "ieee-p1363";

    [JsonConstructor]
    public QuarantineAttestation(
        string purpose,
        string algorithm,
        string signatureFormat,
        string signature,
        QuarantineIssuer issuer)
    {
        if (!string.Equals(purpose, ExpectedPurpose, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected quarantine attestation purpose.", nameof(purpose));
        }

        if (!string.Equals(algorithm, ExpectedAlgorithm, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected quarantine attestation algorithm.", nameof(algorithm));
        }

        if (!string.Equals(signatureFormat, ExpectedSignatureFormat, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected quarantine signature format.", nameof(signatureFormat));
        }

        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Length != 86 || signature.Any(static value =>
                !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
        {
            throw new ArgumentException(
                "Quarantine signature must be unpadded base64url for 64 P1363 bytes.",
                nameof(signature));
        }

        Purpose = purpose;
        Algorithm = algorithm;
        SignatureFormat = signatureFormat;
        Signature = signature;
        Issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
    }

    public string Purpose { get; }

    public string Algorithm { get; }

    public string SignatureFormat { get; }

    public string Signature { get; }

    public QuarantineIssuer Issuer { get; }
}

public enum QuarantineInventoryRefusal
{
    None = 0,
    ReproductionRolesNotDistinct,
    ReproducerIdentitiesNotDistinct,
    ReproductionCountMismatch,
    ReproductionsDisagree,
    PriorIndexPairHashInvalid,
}

/// <summary>
/// The quarantined prior-coordinate inventory: the signed, read-only record of every coordinate a
/// retired V2 index used to serve a public permalink at. Backlog Candidate 2 Stage 3 D3-04.
/// </summary>
/// <remarks>
/// <para>
/// Everything about producing this evidence from real V2 bytes happens outside V3, in a disposable
/// worktree or private tool pinned to the exact previously promoted signed index pair and public
/// key (section 7.3 steps 1 to 4). V3 has no V2 index reader, must never grow one (Decision 71: V2
/// is being replaced, not evolved, and its coordinates are never V3 evidence on their own), and
/// this type -- like <see cref="PriorPublicCoordinate"/> and
/// <see cref="QuarantinePriorCoordinateReproduction"/> beside it -- has no constructor path,
/// field, or producer that opens a file, a stream, or a network connection. What V3 holds is the
/// reconciliation gate that turns two already-produced, independently identified reproductions
/// into one accepted inventory (section 7.3 step 5) and the typed shape that carries the result
/// (steps 3 and 4) for the future canon/2 alias builder (D3-05) to consume. No verifier for this
/// inventory exists in V3; see the remarks on <see cref="QuarantineAttestation"/> for why that is
/// deliberate rather than an oversight.
/// </para>
/// <para>
/// "Independent" is enforced two ways, not one, because a check that merely compares a value
/// against a copy of itself proves nothing -- this project's own history has caught exactly that
/// shape twice: a hand-listing that asserted one supplied identifier and found three, and a
/// binder that bound once but sent twice and so compared a run with itself. First, the two
/// <see cref="QuarantinePriorCoordinateReproduction"/> arguments below must declare different
/// <see cref="QuarantineReproducerRole"/> values and different
/// <see cref="QuarantinePriorCoordinateReproduction.ReproducerIdentity"/> strings -- refused
/// otherwise -- so passing the same reproduction object twice, or two reproductions minted by the
/// same identity under different role labels, is rejected before any byte comparison runs. Second,
/// each reproduction's <see cref="QuarantinePriorCoordinateReproduction.CanonicalSha256"/> was
/// derived, inside that type's own factory, from its own coordinate list alone; <see cref="TryReconcile"/>
/// compares the two digests exactly as it would compare any two independently supplied values --
/// it never recomputes one from the other and never short-circuits by reusing one side's bytes for
/// the other -- so a mutation that made either side silently echo the other's digest or coordinate
/// list is caught by any fixture whose two reproductions genuinely differ in content.
/// </para>
/// </remarks>
public sealed class QuarantinedPriorCoordinateInventory
{
    public const string SchemaId = "lex-v3-quarantined-prior-coordinate-inventory/1";

    private QuarantinedPriorCoordinateInventory(
        IReadOnlyList<PriorPublicCoordinate> coordinates,
        string coordinateSetSha256,
        string priorIndexPairSha256,
        SourceArtifactRef sourceIndexIdentityRef,
        QuarantineVerifierReceipt verifierReceipt,
        QuarantineAttestation attestation,
        QuarantineReproducerRole primaryReproducerRole,
        string primaryReproducerIdentity,
        QuarantineReproducerRole independentReviewerReproducerRole,
        string independentReviewerReproducerIdentity)
    {
        Coordinates = coordinates;
        CoordinateSetSha256 = coordinateSetSha256;
        PriorIndexPairSha256 = priorIndexPairSha256;
        SourceIndexIdentityRef = sourceIndexIdentityRef;
        VerifierReceipt = verifierReceipt;
        Attestation = attestation;
        PrimaryReproducerRole = primaryReproducerRole;
        PrimaryReproducerIdentity = primaryReproducerIdentity;
        IndependentReviewerReproducerRole = independentReviewerReproducerRole;
        IndependentReviewerReproducerIdentity = independentReviewerReproducerIdentity;
    }

    /// <summary>The complete normalized set of previously public coordinates (section 7.3 step 3).</summary>
    public IReadOnlyList<PriorPublicCoordinate> Coordinates { get; }

    /// <summary>The byte-identical digest both reproductions agreed on. Derived, never supplied.</summary>
    public string CoordinateSetSha256 { get; }

    /// <summary>The prior hash: the SHA-256 of the exact previously promoted signed index pair this inventory was read from (section 7.3 steps 1 and 3).</summary>
    public string PriorIndexPairSha256 { get; }

    /// <summary>The source artifact identity of that same prior signed index pair (section 7.3 step 3).</summary>
    public SourceArtifactRef SourceIndexIdentityRef { get; }

    /// <summary>The external tool's declared evidence about its own run (section 7.3 step 3).</summary>
    public QuarantineVerifierReceipt VerifierReceipt { get; }

    /// <summary>The dedicated non-production review identity's attestation (section 7.3 step 4).</summary>
    public QuarantineAttestation Attestation { get; }

    /// <summary>
    /// The role the primary reproduction declared, taken from that <see cref="QuarantinePriorCoordinateReproduction"/>
    /// exactly as <see cref="TryReconcile"/> received it. Retained (not discarded) so a consumer of
    /// this inventory can see which two identities agreed, not merely that two agreed; covered by
    /// <c>QuarantineInventoryCanonicalizer.GetSigningBytes</c>, so it is under the signature too.
    /// </summary>
    public QuarantineReproducerRole PrimaryReproducerRole { get; }

    /// <summary>The primary reproduction's declared identity. See <see cref="PrimaryReproducerRole"/>.</summary>
    public string PrimaryReproducerIdentity { get; }

    /// <summary>The independent reviewer reproduction's declared role. See <see cref="PrimaryReproducerRole"/>.</summary>
    public QuarantineReproducerRole IndependentReviewerReproducerRole { get; }

    /// <summary>The independent reviewer reproduction's declared identity. See <see cref="PrimaryReproducerRole"/>.</summary>
    public string IndependentReviewerReproducerIdentity { get; }

    /// <summary>
    /// The only path to a quarantined prior-coordinate inventory. Refuses unless the two supplied
    /// reproductions are genuinely independent (distinct role, distinct reproducer identity) and
    /// byte-identical (same count, same canonical digest).
    /// </summary>
    public static QuarantinedPriorCoordinateInventory? TryReconcile(
        QuarantinePriorCoordinateReproduction primary,
        QuarantinePriorCoordinateReproduction independentReviewer,
        string priorIndexPairSha256,
        SourceArtifactRef sourceIndexIdentityRef,
        QuarantineVerifierReceipt verifierReceipt,
        QuarantineAttestation attestation,
        out QuarantineInventoryRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(independentReviewer);
        ArgumentNullException.ThrowIfNull(sourceIndexIdentityRef);
        ArgumentNullException.ThrowIfNull(verifierReceipt);
        ArgumentNullException.ThrowIfNull(attestation);

        if (primary.Role == independentReviewer.Role)
        {
            refusal = QuarantineInventoryRefusal.ReproductionRolesNotDistinct;
            return null;
        }

        if (string.Equals(
                primary.ReproducerIdentity,
                independentReviewer.ReproducerIdentity,
                StringComparison.Ordinal))
        {
            refusal = QuarantineInventoryRefusal.ReproducerIdentitiesNotDistinct;
            return null;
        }

        if (primary.Count != independentReviewer.Count)
        {
            refusal = QuarantineInventoryRefusal.ReproductionCountMismatch;
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(primary.CanonicalSha256),
                Convert.FromHexString(independentReviewer.CanonicalSha256)))
        {
            refusal = QuarantineInventoryRefusal.ReproductionsDisagree;
            return null;
        }

        if (priorIndexPairSha256 is null ||
            priorIndexPairSha256.Length != 64 ||
            priorIndexPairSha256.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            refusal = QuarantineInventoryRefusal.PriorIndexPairHashInvalid;
            return null;
        }

        refusal = QuarantineInventoryRefusal.None;
        return new(
            primary.Coordinates,
            primary.CanonicalSha256,
            priorIndexPairSha256,
            sourceIndexIdentityRef,
            verifierReceipt,
            attestation,
            primary.Role,
            primary.ReproducerIdentity,
            independentReviewer.Role,
            independentReviewer.ReproducerIdentity);
    }
}
