using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Contracts.Source.Quarantine;

/// <summary>
/// Defines exactly what a <see cref="QuarantinedPriorCoordinateInventory"/>'s
/// <see cref="QuarantineAttestation"/> signs, and verifies that binding. Backlog Candidate 2
/// section 7.3 step 4 requires the dedicated review identity to sign the inventory; before this
/// type existed nothing defined the bytes it signed and <c>SchemaId</c> was referenced nowhere, so
/// the signature on a <see cref="QuarantineAttestation"/> was an 86-character string bound to
/// nothing -- it could neither be produced correctly (step 4) nor checked meaningfully when fed
/// forward (step 6).
/// </summary>
/// <remarks>
/// <para>
/// The covered fields, in the exact order this type writes them: <see cref="QuarantinedPriorCoordinateInventory.SchemaId"/>,
/// the coordinate count, the coordinate set digest, the prior index pair digest, the source
/// artifact identity (resource id and sha256), the verifier receipt (identity, read-only
/// assertion, timestamp), the attestation's own issuer id and key id, both reproducer
/// attributions -- primary then independent reviewer, each as (role, identity) -- and finally
/// every coordinate in <see cref="PriorPublicCoordinateSet.Ordered"/> canonical order. The
/// reproducer pair is what keeps a consumer of a signed inventory from only learning that two
/// reproductions agreed, without learning which two identities did. The issuer id and key id are
/// what keep a consumer from being able to detach a valid signature from the issuer it names and
/// reattach it to a different one: see the paragraph below on why those two fields are covered
/// while the signature bytes themselves never can be.
/// </para>
/// <para>
/// The attestation's <see cref="QuarantineIssuer.IssuerId"/> and <see cref="QuarantineIssuer.KeyId"/>
/// are covered, but <see cref="QuarantineAttestation.Signature"/> itself is deliberately excluded --
/// a signature cannot be part of the bytes it signs. Without binding issuer id and key id into the
/// signed bytes, a signature produced by one issuer/key pair over an otherwise-identical inventory
/// could be re-attached to a <see cref="QuarantineAttestation"/> claiming a different issuer id or
/// key id and would still verify, because nothing about which issuer or key made the claim would
/// have been checked. Binding them here means <see cref="VerifySignature"/> fails whenever the
/// attestation's issuer id or key id does not match what was actually signed, exactly as it already
/// fails for every other covered field.
/// </para>
/// <para>
/// The coordinate count is included as a documented label, not as an independent decision element:
/// <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/> already refuses a count mismatch
/// before any digest comparison runs, and every coordinate field in the digest below is
/// length-prefixed, so two coordinate sets of different size can never collide on the same digest
/// either. Nothing about whether a signature verifies can turn on this field alone; it is here so a
/// reader of the signed bytes does not have to count the coordinate list to see how large it is.
/// </para>
/// <para>
/// The per-coordinate loop at the end of <see cref="GetSigningBytes"/> is, by design, redundant
/// with the coordinate set digest above from a verification-coverage standpoint: both are computed
/// from the exact same <see cref="PriorPublicCoordinateSet.Ordered"/> canonical ordering of
/// <see cref="QuarantinedPriorCoordinateInventory.Coordinates"/>, so any coordinate mutation that
/// changes the loop's bytes changes the digest too, and SHA-256's collision resistance means no
/// coordinate mutation can change the digest without also changing the loop. The loop's own
/// presence in the signed bytes is not load-bearing for catching a mutation beyond what the digest
/// already proves; it exists so a human auditing the signed bytes can read the actual coordinates
/// directly, without a side channel back to the inventory object.
/// </para>
/// <para>
/// The count and the coordinate set digest are re-derived from
/// <see cref="QuarantinedPriorCoordinateInventory.Coordinates"/> on every call to
/// <see cref="GetSigningBytes"/> -- never read from the inventory's own stored
/// <see cref="QuarantinedPriorCoordinateInventory.CoordinateSetSha256"/> field. The whole point of a
/// signable form is that it is bound to the content it describes, not to a cached claim about that
/// content; <see cref="VerifySignature"/> additionally cross-checks the re-derived digest against the
/// stored one before it will even attempt signature verification.
/// </para>
/// </remarks>
public static class QuarantineInventoryCanonicalizer
{
    private const string Domain = "lex-v3-quarantine-prior-coordinate-inventory-signature/1";

    /// <summary>
    /// The exact bytes a <see cref="QuarantineAttestation"/> over <paramref name="inventory"/> must
    /// sign. Deterministic and order-independent in the coordinate list supplied to
    /// <see cref="QuarantinePriorCoordinateReproduction.TryCreate"/> (coordinates are re-sorted into
    /// <see cref="PriorPublicCoordinateSet.Ordered"/> order here), but sensitive to every other
    /// field named in the remarks above: changing any one of them changes these bytes.
    /// </summary>
    public static byte[] GetSigningBytes(QuarantinedPriorCoordinateInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var recomputedDigest = PriorPublicCoordinateSet.CanonicalSha256Hex(inventory.Coordinates);
        var ordered = PriorPublicCoordinateSet.Ordered(inventory.Coordinates);

        var builder = new StringBuilder(Domain).Append('\n');
        Append(builder, "schema", QuarantinedPriorCoordinateInventory.SchemaId);
        Append(builder, "count", ordered.Count.ToString(CultureInfo.InvariantCulture));
        Append(builder, "coordinate_set_sha256", recomputedDigest);
        Append(builder, "prior_index_pair_sha256", inventory.PriorIndexPairSha256);
        Append(builder, "source_index_identity_ref.resource_id", inventory.SourceIndexIdentityRef.ResourceId);
        Append(builder, "source_index_identity_ref.sha256", inventory.SourceIndexIdentityRef.Sha256);
        Append(builder, "verifier_receipt.verifier_identity", inventory.VerifierReceipt.VerifierIdentity);
        Append(
            builder,
            "verifier_receipt.operated_read_only",
            inventory.VerifierReceipt.OperatedReadOnly ? "true" : "false");
        Append(builder, "verifier_receipt.produced_at_utc", inventory.VerifierReceipt.ProducedAtUtc);
        Append(builder, "attestation.issuer.issuer_id", inventory.Attestation.Issuer.IssuerId);
        Append(builder, "attestation.issuer.key_id", inventory.Attestation.Issuer.KeyId);
        Append(builder, "primary.role", inventory.PrimaryReproducerRole.ToString());
        Append(builder, "primary.reproducer_identity", inventory.PrimaryReproducerIdentity);
        Append(builder, "independent_reviewer.role", inventory.IndependentReviewerReproducerRole.ToString());
        Append(
            builder,
            "independent_reviewer.reproducer_identity",
            inventory.IndependentReviewerReproducerIdentity);

        for (var index = 0; index < ordered.Count; index++)
        {
            var coordinate = ordered[index];
            var prefix = "coordinates[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            Append(builder, prefix + ".work_key", coordinate.WorkKey);
            Append(builder, prefix + ".language", coordinate.Language);
            Append(builder, prefix + ".valid_from", coordinate.ValidFrom);
            Append(builder, prefix + ".anchor", coordinate.Anchor ?? string.Empty);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Verifies that <paramref name="inventory"/>'s <see cref="QuarantinedPriorCoordinateInventory.Attestation"/>
    /// signs exactly <see cref="GetSigningBytes"/> over <paramref name="inventory"/> itself, using
    /// <paramref name="publicKey"/>. Re-derives the coordinate count and digest from
    /// <see cref="QuarantinedPriorCoordinateInventory.Coordinates"/> and rejects the inventory
    /// outright if that re-derived digest disagrees with the stored
    /// <see cref="QuarantinedPriorCoordinateInventory.CoordinateSetSha256"/>, rather than trusting
    /// the stored value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named <c>VerifySignature</c>, not <c>ParseAndVerify</c>: unlike its siblings elsewhere in
    /// this assembly that check a signature (for example <c>RoutedHttpEvidence.ParseAndVerify</c>,
    /// <c>MachineQueryInputArtifact.ParseAndVerify</c> and <c>LuxembourgQueryPlan.ParseAndVerify</c>,
    /// each of which takes a <c>ReadOnlySpan&lt;byte&gt;</c> of canonical bytes and parses an object
    /// out of it before verifying), this method takes an already-constructed
    /// <paramref name="inventory"/> and parses nothing. No wire (canonical-bytes) form of a
    /// quarantined prior-coordinate inventory exists yet anywhere in V3: the external quarantined
    /// tool's actual output shape, and the parser that would turn it into a
    /// <see cref="QuarantinedPriorCoordinateInventory"/>, are the carried condition for the canon/2
    /// alias builder (D3-05), not this type. So this checks a signature against an already-constructed
    /// in-memory object -- built only through <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/>,
    /// the type's one checked door -- rather than parsing bytes the way its Contracts siblings do.
    /// </para>
    /// <para>
    /// This is deliberately not the trust-store-backed verifier section 7.3 step 6 hands to the
    /// canon/2 alias builder (D3-05): it checks only that <paramref name="publicKey"/> produced the
    /// attached signature over the inventory's own canonical bytes, nothing about whether that key
    /// or its issuer ought to be trusted for this purpose. See the remarks on
    /// <see cref="QuarantineAttestation"/>.
    /// </para>
    /// <para>
    /// The coordinate-count/digest cross-check below, and the <see cref="FormatException"/> catch
    /// around <see cref="DecodeSignature"/>, can never actually fire when <paramref name="inventory"/>
    /// came from <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/> (the type's only
    /// producer): the digest stored there is derived, in <c>TryReconcile</c>, from the very
    /// coordinates it is compared against here, so the two can never disagree; and
    /// <see cref="QuarantineAttestation"/>'s own constructor already enforces the exact
    /// signature shape <see cref="DecodeSignature"/> checks, so a well-formed
    /// <see cref="QuarantineAttestation"/> can never fail that decode. Both checks are kept anyway,
    /// as defence in depth against a future caller that reaches this method with an inventory built
    /// some other way -- not because either branch is reachable through the private constructor as
    /// this type stands today.
    /// </para>
    /// </remarks>
    public static QuarantinedPriorCoordinateInventory VerifySignature(
        QuarantinedPriorCoordinateInventory inventory,
        ECDsa publicKey)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(publicKey);

        var recomputedDigest = PriorPublicCoordinateSet.CanonicalSha256Hex(inventory.Coordinates);
        if (inventory.Coordinates.Count == 0 ||
            !string.Equals(recomputedDigest, inventory.CoordinateSetSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The inventory's coordinate set digest does not match its own coordinates.",
                nameof(inventory));
        }

        byte[] signature;
        try
        {
            signature = DecodeSignature(inventory.Attestation.Signature);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "The attestation signature is not well-formed unpadded base64url.",
                nameof(inventory),
                exception);
        }

        if (!publicKey.VerifyData(
                GetSigningBytes(inventory),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw new ArgumentException(
                "The attestation signature does not verify against the inventory's canonical bytes.",
                nameof(inventory));
        }

        return inventory;
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        builder
            .Append(Encoding.UTF8.GetByteCount(name).ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(name)
            .Append('=')
            .Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');
    }

    /// <summary>
    /// Decodes the exact unpadded-base64url-for-64-P1363-bytes shape
    /// <see cref="QuarantineAttestation"/>'s own constructor already validates. There is no
    /// matching encode helper in production code: producing a signature is the external quarantined
    /// tool's job (section 7.3 step 4), never V3's; only checking one is.
    /// </summary>
    private static byte[] DecodeSignature(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 86 || value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new FormatException("The value is not unpadded base64url for 64 P1363 bytes.");
        }

        var base64 = value.Replace('-', '+').Replace('_', '/') + "==";
        return Convert.FromBase64String(base64);
    }
}
