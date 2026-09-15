using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Quarantine;

/// <summary>Why a quarantined inventory document was not admissible as V3 input.</summary>
public enum QuarantineInventoryWireRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>Not one valid typed document: malformed JSON, a duplicate or unmapped member, a comment, a trailing comma.</summary>
    [JsonStringEnumMemberName("not_one_valid_typed_document")]
    NotOneValidTypedDocument = 1,

    /// <summary>The document does not declare this reader's schema.</summary>
    [JsonStringEnumMemberName("unexpected_schema")]
    UnexpectedSchema = 2,

    /// <summary>
    /// The document does not carry exactly two reproductions. Section 7.3 step 5 requires the
    /// inventory reproduced independently twice; one is not a reconciliation and three is not a
    /// shape this reader will guess at.
    /// </summary>
    [JsonStringEnumMemberName("reproduction_count_not_two")]
    ReproductionCountNotTwo = 3,

    /// <summary>A reproduction names a role outside the closed two-member vocabulary.</summary>
    [JsonStringEnumMemberName("role_not_recognised")]
    RoleNotRecognised = 4,

    /// <summary>A coordinate field was refused by its own validation.</summary>
    [JsonStringEnumMemberName("coordinate_invalid")]
    CoordinateInvalid = 5,

    /// <summary>A reproduction was refused by <see cref="QuarantinePriorCoordinateReproduction.TryCreate"/>.</summary>
    [JsonStringEnumMemberName("reproduction_refused")]
    ReproductionRefused = 6,

    /// <summary>The two reproductions were refused by <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/>.</summary>
    [JsonStringEnumMemberName("reconciliation_refused")]
    ReconciliationRefused = 7,

    /// <summary>The evidence beside the reproductions was refused by its own constructor.</summary>
    [JsonStringEnumMemberName("evidence_invalid")]
    EvidenceInvalid = 8,
}

/// <summary>
/// Reads the one document V3 accepts as the canon/2 prior-coordinate input, and reconciles it here
/// rather than trusting it. Specified in <c>CANON2-EXTERNAL-RUN.md</c> beside this file.
/// </summary>
/// <remarks>
/// <para>
/// THIS RECONCILES; IT DOES NOT RECEIVE A RECONCILED RESULT. The document carries BOTH reproductions
/// and this reader runs <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/> itself. If it
/// accepted an already-reconciled inventory it would be trusting the one step section 7.3 requires
/// to be independent, and <c>TryReconcile</c> would stop being what its own summary calls "the only
/// path to a quarantined prior-coordinate inventory".
/// </para>
/// <para>
/// The design is self-checking because of what the signature covers. The attestation signs the
/// RECONCILED inventory -- including both reproducers' roles and identities -- so the external tool
/// must reconcile in order to know what to sign, and this reader re-derives the same signing bytes
/// from the two reproductions it was given. A tool whose reconciliation differed in any covered
/// field produces bytes this side will not reproduce, and the signature fails. V3 never has to trust
/// the external reconciliation; it only has to be able to repeat it.
/// </para>
/// <para>
/// NO DIGEST IS READ FROM THE DOCUMENT. There is no field here for a coordinate-set digest, so a
/// producer cannot make two disagreeing walks "agree" by supplying matching strings: each
/// reproduction's digest is derived inside
/// <see cref="QuarantinePriorCoordinateReproduction.TryCreate"/> from its own coordinates alone, and
/// <c>TryReconcile</c> then compares two independently derived values. Because unmapped members are
/// refused, a document that tried to supply one is rejected rather than ignored.
/// </para>
/// <para>
/// IT READS TEXT, NOT BYTES, AND THAT IS STRUCTURAL. <c>NoLawContentCapabilityTests</c> refuses any
/// member in this namespace typed as a byte array, span, memory or byte-shaped container. So the
/// UTF-8 decode belongs to the caller, outside these contracts, where handling bytes is legitimate;
/// nothing here can be handed a payload. Every field this reader does accept is re-validated by the
/// constructor that owns it, so no content can enter through a string either.
/// </para>
/// <para>
/// This reader performs steps 1 to 4 of the specification's admission sequence. Step 5, the
/// trust-store verdict, is <see cref="TrustedQuarantinedPriorCoordinateInventory.TryAdmit"/>: a
/// document that parses and reconciles is still not admissible until a pinned key verifies it.
/// </para>
/// </remarks>
public static class QuarantineInventoryWireReader
{
    /// <summary>The wire document's own schema, distinct from the reconciled inventory's.</summary>
    public const string SchemaId = "lex-v3-quarantined-prior-coordinate-inventory-wire/1";

    private const string PrimaryRole = nameof(QuarantineReproducerRole.Primary);
    private const string IndependentReviewerRole = nameof(QuarantineReproducerRole.IndependentReviewer);

    /// <summary>
    /// Parses <paramref name="json"/> and reconciles the two reproductions it carries, or refuses
    /// by name. The result is a reconciled inventory, never a trusted one.
    /// </summary>
    public static QuarantinedPriorCoordinateInventory? TryParse(
        string json,
        out QuarantineInventoryWireRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(json);
        refusal = QuarantineInventoryWireRefusal.None;
        detail = null;

        WireDocument document;
        try
        {
            document = ContractJson.Deserialize<WireDocument>(json);
        }
        catch (JsonException exception)
        {
            refusal = QuarantineInventoryWireRefusal.NotOneValidTypedDocument;
            detail = exception.Message;
            return null;
        }

        if (!string.Equals(document.Schema, SchemaId, StringComparison.Ordinal))
        {
            refusal = QuarantineInventoryWireRefusal.UnexpectedSchema;
            detail = $"expected {SchemaId}; found '{document.Schema}'";
            return null;
        }

        if (document.Reproductions.Count != 2)
        {
            refusal = QuarantineInventoryWireRefusal.ReproductionCountNotTwo;
            detail = $"the document carries {document.Reproductions.Count} reproductions; exactly two are required";
            return null;
        }

        var reproductions = new QuarantinePriorCoordinateReproduction[2];
        for (var index = 0; index < 2; index++)
        {
            var wire = document.Reproductions[index];
            if (!TryReadRole(wire.Role, out var role))
            {
                refusal = QuarantineInventoryWireRefusal.RoleNotRecognised;
                detail = $"reproductions[{index}] names role '{wire.Role}'";
                return null;
            }

            IReadOnlyList<PriorPublicCoordinate> coordinates;
            try
            {
                coordinates = wire.Coordinates
                    .Select(static coordinate => new PriorPublicCoordinate(
                        coordinate.WorkKey, coordinate.Language, coordinate.ValidFrom, coordinate.Anchor))
                    .ToArray();
            }
            catch (ArgumentException exception)
            {
                refusal = QuarantineInventoryWireRefusal.CoordinateInvalid;
                detail = $"reproductions[{index}]: {exception.Message}";
                return null;
            }

            var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(
                role, wire.ReproducerIdentity, coordinates, out var reproductionRefusal);
            if (reproduction is null)
            {
                refusal = QuarantineInventoryWireRefusal.ReproductionRefused;
                detail = $"reproductions[{index}]: {reproductionRefusal}";
                return null;
            }

            reproductions[index] = reproduction;
        }

        SourceArtifactRef sourceIndexIdentityRef;
        QuarantineVerifierReceipt verifierReceipt;
        QuarantineAttestation attestation;
        try
        {
            sourceIndexIdentityRef = new SourceArtifactRef(
                document.SourceIndexIdentityRef.ResourceId, document.SourceIndexIdentityRef.Sha256);
            verifierReceipt = new QuarantineVerifierReceipt(
                document.VerifierReceipt.VerifierIdentity,
                document.VerifierReceipt.OperatedReadOnly,
                document.VerifierReceipt.ProducedAtUtc);
            attestation = new QuarantineAttestation(
                document.Attestation.Purpose,
                document.Attestation.Algorithm,
                document.Attestation.SignatureFormat,
                document.Attestation.Signature,
                new QuarantineIssuer(
                    document.Attestation.Issuer.Role,
                    document.Attestation.Issuer.IssuerId,
                    document.Attestation.Issuer.KeyId));
        }
        catch (ArgumentException exception)
        {
            refusal = QuarantineInventoryWireRefusal.EvidenceInvalid;
            detail = exception.Message;
            return null;
        }

        // Which reproduction is primary is decided by its own declared role, never by its position
        // in the document: the signed bytes name the roles, so a document that listed them in the
        // other order and a reader that trusted position would reconcile a different pair than the
        // one that was signed.
        var primary = reproductions[0].Role == QuarantineReproducerRole.Primary
            ? reproductions[0]
            : reproductions[1];
        var reviewer = ReferenceEquals(primary, reproductions[0]) ? reproductions[1] : reproductions[0];

        var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            document.PriorIndexPairSha256,
            sourceIndexIdentityRef,
            verifierReceipt,
            attestation,
            out var reconciliationRefusal);
        if (inventory is null)
        {
            refusal = QuarantineInventoryWireRefusal.ReconciliationRefused;
            detail = reconciliationRefusal.ToString();
            return null;
        }

        return inventory;
    }

    private static bool TryReadRole(string value, out QuarantineReproducerRole role)
    {
        if (string.Equals(value, PrimaryRole, StringComparison.Ordinal))
        {
            role = QuarantineReproducerRole.Primary;
            return true;
        }

        if (string.Equals(value, IndependentReviewerRole, StringComparison.Ordinal))
        {
            role = QuarantineReproducerRole.IndependentReviewer;
            return true;
        }

        role = default;
        return false;
    }

    private sealed record WireDocument(
        string Schema,
        string PriorIndexPairSha256,
        WireArtifactRef SourceIndexIdentityRef,
        WireVerifierReceipt VerifierReceipt,
        IReadOnlyList<WireReproduction> Reproductions,
        WireAttestation Attestation);

    private sealed record WireArtifactRef(string ResourceId, string Sha256);

    private sealed record WireVerifierReceipt(
        string VerifierIdentity, bool OperatedReadOnly, string ProducedAtUtc);

    private sealed record WireReproduction(
        string Role, string ReproducerIdentity, IReadOnlyList<WireCoordinate> Coordinates);

    private sealed record WireCoordinate(
        string WorkKey, string Language, string ValidFrom, string? Anchor);

    private sealed record WireAttestation(
        string Purpose,
        string Algorithm,
        string SignatureFormat,
        string Signature,
        WireIssuer Issuer);

    private sealed record WireIssuer(string Role, string IssuerId, string KeyId);
}
