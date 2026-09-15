using System.Security.Cryptography;

namespace Lex.V3.Contracts.Source.Quarantine;

/// <summary>
/// The pinned set of issuers whose attestations over a quarantined prior-coordinate inventory this
/// build will consider at all, and the public keys each of them may sign with.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the same two questions <c>IPreviewTrustStore</c> asks in <c>Lex.V3.Artifacts</c>,
/// declared again here rather than shared, because <c>Lex.V3.Contracts</c> does not reference
/// <c>Lex.V3.Artifacts</c> and inverting that dependency to reuse two method signatures would be a
/// larger change than restating them.
/// </para>
/// <para>
/// THE KEY IS RESOLVED UNDER AN ISSUER, NEVER ON ITS OWN. <see cref="TryGetSubjectPublicKeyInfo"/>
/// takes both identifiers, so an implementation cannot answer "some issuer holds this key" -- the
/// question it is asked is whether <em>this</em> issuer holds it. Backlog Candidate 2 section 7.3
/// step 6 hands the canon/2 alias builder a trust-store-backed verdict, and a store that resolved a
/// key belonging to a different issuer would let a valid signature be presented under an issuer that
/// never made it.
/// </para>
/// </remarks>
public interface IQuarantineTrustStore
{
    /// <summary>Whether <paramref name="issuerId"/> is pinned for quarantine attestations in this build.</summary>
    bool ContainsIssuer(string issuerId);

    /// <summary>
    /// Resolves the verification key for <paramref name="keyId"/> <em>under</em>
    /// <paramref name="issuerId"/>. Returns false when the issuer is unknown, when the key is
    /// unknown, when the key belongs to another issuer, or when the store holds material it cannot
    /// turn into a usable key. The caller owns and disposes the returned instance.
    /// </summary>
    /// <remarks>
    /// THIS HANDS OUT A CAPABILITY, NEVER BYTES, AND THAT IS NOT A STYLE CHOICE.
    /// <c>NoLawContentCapabilityTests</c> refuses any member anywhere in this namespace whose type
    /// is <see cref="ReadOnlyMemory{T}"/> of byte, a stream, a file handle or a URI -- the
    /// structural half of the CLAUDE.md rule that nothing in these contracts can carry or open law
    /// body content. An earlier shape of this interface returned SubjectPublicKeyInfo bytes and
    /// tripped exactly that sweep. The fix is this signature, not an exclusion: excluding it would
    /// have put the first byte-typed member into the one namespace whose guarantee is that none
    /// exists, and the sweep's own summary names that as the loophole it was written to prevent.
    /// A store that holds SPKI bytes imports them on its own side, where no such guarantee applies.
    /// </remarks>
    bool TryResolveVerificationKey(string issuerId, string keyId, out ECDsa? publicKey);
}
