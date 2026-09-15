using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Quarantine;

/// <summary>Why a signed inventory was not admissible under this build's pinned trust.</summary>
public enum TrustedQuarantineInventoryRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The attestation names an issuer this build does not pin for quarantine attestations.</summary>
    [JsonStringEnumMemberName("issuer_not_trusted")]
    IssuerNotTrusted = 1,

    /// <summary>
    /// The key the attestation names does not resolve under that issuer. Covers an unknown key and,
    /// more importantly, a key that exists under a DIFFERENT issuer.
    /// </summary>
    [JsonStringEnumMemberName("key_not_resolved_under_issuer")]
    KeyNotResolvedUnderIssuer = 2,

    /// <summary>The attestation does not verify over the inventory's own canonical bytes under the resolved key.</summary>
    [JsonStringEnumMemberName("signature_does_not_verify")]
    SignatureDoesNotVerify = 3,
}

/// <summary>
/// A quarantined prior-coordinate inventory that verified under a key this build actually pins,
/// resolved under the exact issuer the attestation names. Holding one is the evidence that
/// <see cref="TryAdmit"/> ran to completion; the constructor is private and there is no other door.
/// </summary>
/// <remarks>
/// <para>
/// THE GAP THIS CLOSES. <see cref="QuarantineInventoryCanonicalizer.VerifySignature"/> says in its
/// own remarks that it "checks only that <c>publicKey</c> produced the attached signature over the
/// inventory's own canonical bytes, nothing about whether that key or its issuer ought to be trusted
/// for this purpose", and that it is "deliberately not the trust-store-backed verifier section 7.3
/// step 6 hands to the canon/2 alias builder". So before this type, an attestation from an unknown
/// issuer, signed with a key nobody pinned, verified exactly as a trusted one did: the caller chose
/// the key. This type is what makes the caller stop choosing.
/// </para>
/// <para>
/// NO ISSUER-ROLE REFUSAL EXISTS HERE, AND THAT IS DELIBERATE. <see cref="QuarantineIssuer"/>'s own
/// constructor throws unless the role is exactly <see cref="QuarantineIssuer.ExpectedRole"/>, and a
/// <see cref="QuarantineAttestation"/> cannot be built without one. So a constructed attestation can
/// never carry another role, and a role check here would be a refusal unreachable through the only
/// door onto its own input -- a clause that can never fire, which is worse than no clause because it
/// reads as protection. The role is enforced; it is enforced one type earlier.
/// </para>
/// <para>
/// The key is resolved BEFORE the signature is checked, and the signature is then checked against
/// exactly that resolved key rather than against anything the caller holds. A caller cannot supply
/// its own <see cref="ECDsa"/> here; the only parameters are the inventory and the store. The
/// resolved key is disposed here, since <see cref="IQuarantineTrustStore"/> hands ownership over.
/// </para>
/// </remarks>
public sealed class TrustedQuarantinedPriorCoordinateInventory
{
    private TrustedQuarantinedPriorCoordinateInventory(
        QuarantinedPriorCoordinateInventory inventory,
        string issuerId,
        string keyId)
    {
        Inventory = inventory;
        IssuerId = issuerId;
        KeyId = keyId;
    }

    /// <summary>The admitted inventory, unchanged.</summary>
    public QuarantinedPriorCoordinateInventory Inventory { get; }

    /// <summary>The issuer this build pinned, as resolved rather than as claimed.</summary>
    public string IssuerId { get; }

    /// <summary>The key that actually verified, resolved under <see cref="IssuerId"/>.</summary>
    public string KeyId { get; }

    /// <summary>
    /// The only door. Resolves the attestation's issuer and key against
    /// <paramref name="trustStore"/>, imports the resolved key, and verifies the attestation over
    /// the inventory's own canonical bytes under it.
    /// </summary>
    public static TrustedQuarantinedPriorCoordinateInventory? TryAdmit(
        QuarantinedPriorCoordinateInventory inventory,
        IQuarantineTrustStore trustStore,
        out TrustedQuarantineInventoryRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(trustStore);
        refusal = TrustedQuarantineInventoryRefusal.None;
        detail = null;

        var issuer = inventory.Attestation.Issuer;
        if (!trustStore.ContainsIssuer(issuer.IssuerId))
        {
            refusal = TrustedQuarantineInventoryRefusal.IssuerNotTrusted;
            detail = $"the attestation names issuer '{issuer.IssuerId}', which this build does not pin";
            return null;
        }

        if (!trustStore.TryResolveVerificationKey(issuer.IssuerId, issuer.KeyId, out var publicKey) ||
            publicKey is null)
        {
            refusal = TrustedQuarantineInventoryRefusal.KeyNotResolvedUnderIssuer;
            detail = $"key '{issuer.KeyId}' does not resolve under issuer '{issuer.IssuerId}'";
            return null;
        }

        using (publicKey)
        try
        {
            QuarantineInventoryCanonicalizer.VerifySignature(inventory, publicKey);
        }
        catch (ArgumentException exception)
        {
            // VerifySignature states every one of its own rejections as an ArgumentException: the
            // coordinate-digest cross-check, the signature's base64url shape, and the verification
            // itself. Catching that one type is catching exactly its verdict; a null argument
            // cannot reach here, both are checked above.
            refusal = TrustedQuarantineInventoryRefusal.SignatureDoesNotVerify;
            detail = exception.Message;
            return null;
        }

        return new TrustedQuarantinedPriorCoordinateInventory(inventory, issuer.IssuerId, issuer.KeyId);
    }
}
