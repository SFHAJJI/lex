using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>Why a reopened scope manifest could not be admitted as the Union's own. Closed.</summary>
public enum EuScopeManifestBindingProofRefusal
{
    /// <summary>No refusal.</summary>
    None = 0,

    /// <summary>
    /// The reopened manifest's <c>ScopeProfileBinding.SourceProfileRef</c> does not match
    /// <see cref="EuScopeProfile.BuildBinding"/>'s own identity. The bytes parsed and verified
    /// structurally (item 14's own door already proved that), but they are not necessarily the
    /// Union's manifest -- Luxembourg's own binding is a different, equally valid
    /// <c>scope/1</c> instance, and a caller that opened the wrong artifact must not be told it
    /// opened the right one.
    /// </summary>
    ProfileResourceIdentityMismatch = 1,

    /// <summary>The reopened manifest's selector-table identity does not match the Union's own.</summary>
    SelectorTableIdentityMismatch = 2,
}

/// <summary>
/// D1-05's consumption of the ScopeManifest reader door (item 14,
/// <see cref="VerifiedScopeManifest.ParseAndVerify"/>): reopen a durably written scope manifest and
/// require its profile binding to be the Union's own, never a different publisher's.
/// </summary>
/// <remarks>
/// <para>
/// D1-Core Candidate 3 section 4 and D1-01 Candidate 5 R1 both say "each publisher has one
/// canonical <c>scope/1</c> manifest": the schema is shared, but <see cref="EuScopeProfile"/> and
/// <see cref="Luxembourg.VerifiedLuxembourgSourceProfile"/> each mint their own distinct
/// <c>SourceProfileRef</c> and <c>SelectorTableRef</c> identity. Item 14's door,
/// <see cref="VerifiedScopeManifest.ParseAndVerify"/>, proves a byte sequence is a structurally
/// valid <c>lex-v3-source-scope-manifest/1</c> document; it does not, and structurally cannot, know
/// which publisher's reviewed policy produced it, because that is exactly the fact
/// <see cref="Source.Scope.ScopeProfileBinding"/> carries as data rather than as a wire-level type
/// discriminator. A caller that reopens the wrong durable artifact -- Luxembourg's manifest handed
/// to the EU pipeline by a wiring mistake, for instance -- would otherwise sail through item 14's
/// door with a fully verified, fully structural, wrong-publisher manifest. This type is the one
/// additional check D1-05 needs on top of item 14 to close that gap for its own publisher.
/// </para>
/// <para>
/// Every other structural invariant (digest match, strict UTF-8, canonical round trip, the fourteen
/// <see cref="ScopeManifestReaderOnlyInvariant"/> checks) is item 14's own job and is not repeated
/// here: <see cref="VerifiedScopeManifest.ParseAndVerify"/>'s own exceptions propagate unchanged, so
/// a structurally invalid manifest never reaches this type's own check at all.
/// </para>
/// <para>
/// This door deliberately returns <see cref="Source.Scope.ScopeManifest"/>, the content, rather than
/// the <see cref="VerifiedScopeManifest"/> wrapper item 14 itself returns. Item 14's own construction
/// surface is pinned across the whole <c>Lex.V3.Contracts</c> assembly
/// (<c>VerifiedScopeManifestSurfaceTests.EveryOtherProducerOfAVerifiedManifestInContractsIsPinned</c>),
/// a file this package's path claim does not extend to; minting a second producer of that guarded
/// type here would silently go unpinned by anything in this path claim and would require editing a
/// file outside it to re-pin, which the path claim forbids. Unwrapping to the plain content type
/// avoids the conflict entirely rather than reaching past the claim: this function still only ever
/// hands out a manifest it obtained by calling <see cref="VerifiedScopeManifest.ParseAndVerify"/>
/// itself, so the content a caller receives was still verified: what is not preserved past this
/// door is the wrapper's own type-level proof of that, which is item 14's concern to guard, not this
/// one's to duplicate.
/// </para>
/// <para>
/// Decision 78 gap, stated honestly rather than assumed closed: this type takes
/// <paramref name="canonicalBytes"/> as given and performs no retention of its own. Decision 78
/// requires the session that produced a routed evidence document to retain it, under its own
/// digest, before reporting the route; that retention path is queue item 1c
/// (<c>RoutedHttpAcquisitionSession</c> and its own retention call), which lives outside
/// <c>Source/Europe</c> and outside this package's path claim. Whether the bytes this function is
/// handed were actually retained under <paramref name="artifactRef"/>'s digest before this function
/// runs is a fact this contract has no way to check and does not claim to have checked.
/// </para>
/// </remarks>
public static class EuScopeManifestBindingProof
{
    /// <summary>
    /// Reopen a durably written scope manifest and require it to be the Union's own. Item 14's own
    /// exceptions (<see cref="ArgumentException"/> and its subtypes) propagate unchanged for every
    /// structural failure; the typed refusal below covers only the one additional EU-specific check.
    /// </summary>
    /// <param name="artifactRef">The manifest's own pinned digest.</param>
    /// <param name="canonicalBytes">The manifest's canonical bytes, from wherever they were retained.</param>
    /// <param name="observationResolver">The resolver item 14's own verification pass requires.</param>
    /// <param name="refusal">Why the reopened manifest was not admitted as the Union's own, when it was not.</param>
    public static ScopeManifest? TryOpenAsEuManifest(
        SourceArtifactRef artifactRef,
        ReadOnlySpan<byte> canonicalBytes,
        IScopeReductionEvidenceResolver observationResolver,
        out EuScopeManifestBindingProofRefusal refusal)
    {
        var verified = VerifiedScopeManifest.ParseAndVerify(artifactRef, canonicalBytes, observationResolver);

        refusal = CheckProfileIdentity(verified.Manifest.Profile);
        return refusal == EuScopeManifestBindingProofRefusal.None ? verified.Manifest : null;
    }

    /// <summary>
    /// The actual identity check, isolated from <see cref="VerifiedScopeManifest.ParseAndVerify"/>
    /// so a test can drive it against a hand-built <see cref="ScopeProfileBinding"/> rather than
    /// having to construct and canonically serialize a complete, independently verifiable
    /// <see cref="Source.Scope.ScopeManifest"/> just to reach this one comparison. Internal rather
    /// than private for exactly that reason.
    /// </summary>
    internal static EuScopeManifestBindingProofRefusal CheckProfileIdentity(ScopeProfileBinding actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        var expected = EuScopeProfile.BuildBinding();

        if (actual.SourceProfileRef != expected.SourceProfileRef)
        {
            return EuScopeManifestBindingProofRefusal.ProfileResourceIdentityMismatch;
        }

        return actual.SelectorTableRef != expected.SelectorTableRef
            ? EuScopeManifestBindingProofRefusal.SelectorTableIdentityMismatch
            : EuScopeManifestBindingProofRefusal.None;
    }
}
