using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// D1-05's own consumption of item 14 (<see cref="VerifiedScopeManifest.ParseAndVerify"/>): a
/// reopened manifest that is structurally valid is not necessarily the Union's own manifest, and
/// this is the one additional check that tells the two apart.
/// </summary>
/// <remarks>
/// These tests drive <see cref="EuScopeManifestBindingProof.CheckProfileIdentity"/> directly rather
/// than round-tripping a complete <see cref="ScopeManifest"/> through
/// <see cref="ScopeReducer.Reduce"/>, canonical serialization and
/// <see cref="VerifiedScopeManifest.ParseAndVerify"/>: that full pipeline is item 14's own,
/// separately proven surface (<c>ScopeManifestContractTests</c>,
/// <c>VerifiedScopeManifestSurfaceTests</c>), and this type adds exactly one new comparison on top
/// of an already-verified <see cref="ScopeProfileBinding"/>, which is what is isolated and tested
/// here.
/// </remarks>
[TestClass]
public sealed class EuScopeManifestBindingProofTests
{
    [TestMethod]
    public void TheUnionsOwnBindingIsAdmitted()
    {
        var refusal = EuScopeManifestBindingProof.CheckProfileIdentity(EuScopeProfile.BuildBinding());
        Assert.AreEqual(EuScopeManifestBindingProofRefusal.None, refusal);
    }

    [TestMethod]
    public void ADifferentPublishersProfileIdentityIsRefused()
    {
        var other = OtherPublisherBinding(
            profileResourceId: "urn:uuid:11111111-1111-4111-8111-111111111111",
            selectorTableResourceId: "urn:uuid:22222222-2222-4222-8222-222222222222");

        var refusal = EuScopeManifestBindingProof.CheckProfileIdentity(other);

        Assert.AreEqual(EuScopeManifestBindingProofRefusal.ProfileResourceIdentityMismatch, refusal);
    }

    [TestMethod]
    public void ASharedProfileButDifferentSelectorTableIsRefused()
    {
        // The profile half matches the Union's own by construction (same resource id and, since
        // BuildBinding's digest is a pure function of the fixed EU vocabulary, the same digest too),
        // so only the selector-table half can be responsible for the refusal -- proving the two
        // checks are independent rather than one masking the other.
        var euBinding = EuScopeProfile.BuildBinding();
        var other = OtherPublisherBinding(
            profileResourceId: euBinding.SourceProfileRef.ResourceId,
            selectorTableResourceId: "urn:uuid:33333333-3333-4333-8333-333333333333",
            profileSha256: euBinding.SourceProfileRef.Sha256);

        var refusal = EuScopeManifestBindingProof.CheckProfileIdentity(other);

        Assert.AreEqual(EuScopeManifestBindingProofRefusal.SelectorTableIdentityMismatch, refusal);
    }

    [TestMethod]
    public void ANullBindingThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => EuScopeManifestBindingProof.CheckProfileIdentity(null!));
    }

    /// <summary>
    /// A structurally valid <see cref="ScopeProfileBinding"/> for a fictitious other publisher,
    /// built the same way <see cref="EuScopeProfile.BuildBinding"/> builds the Union's own: one
    /// selector-table-owned member and one profile-owned member, one rule per axis.
    /// </summary>
    private static ScopeProfileBinding OtherPublisherBinding(
        string profileResourceId,
        string selectorTableResourceId,
        string? profileSha256 = null)
    {
        const string digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var profileRef = new SourceArtifactRef(profileResourceId, profileSha256 ?? digest);
        var tableRef = new SourceArtifactRef(selectorTableResourceId, digest);

        var selectorMember = new SourceRegistryMemberRef(tableRef, "selector.only");
        var bodyCandidateMember = new SourceRegistryMemberRef(profileRef, "role.body_candidate");
        var members = new[] { selectorMember, bodyCandidateMember }
            .OrderBy(static m => m.RegistryRef.ResourceId, StringComparer.Ordinal)
            .ThenBy(static m => m.RegistryRef.Sha256, StringComparer.Ordinal)
            .ThenBy(static m => m.MemberKey, StringComparer.Ordinal)
            .ToArray();

        int OrdinalOf(SourceRegistryMemberRef member) => Array.IndexOf(members, member);

        var rules = new[]
        {
            new ScopeRuleBinding(ScopeAxis.Record, OrdinalOf(selectorMember), 0),
            new ScopeRuleBinding(ScopeAxis.Body, OrdinalOf(selectorMember), 1),
            new ScopeRuleBinding(ScopeAxis.Relation, OrdinalOf(selectorMember), 2),
            new ScopeRuleBinding(ScopeAxis.SupportingDocument, OrdinalOf(selectorMember), 3),
        };

        return new ScopeProfileBinding(
            profileRef,
            tableRef,
            members,
            new[] { OrdinalOf(selectorMember) },
            rules,
            OrdinalOf(bodyCandidateMember));
    }
}
