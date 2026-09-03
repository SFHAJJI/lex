using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class ContentDerivedIdentityTests
{
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("machine-query-render-receipt/1\n");

    [TestMethod]
    public void TheSameScopeAndBytesAlwaysGiveTheSameIdentity()
    {
        Assert.AreEqual(
            ContentDerivedIdentity.DeriveUuidUrn("scope/1", Bytes),
            ContentDerivedIdentity.DeriveUuidUrn("scope/1", Bytes));
    }

    [TestMethod]
    public void ThePinnedValueIsTranscribedRatherThanRecomputed()
    {
        // Computed once and written down. Deriving the expectation the way the code derives it
        // would make this agree with itself under any change to the derivation, which is the
        // failure mode this whole lane is about: the test would still pass after the algorithm
        // silently changed, and every identity in the corpus would have moved.
        Assert.AreEqual(
            "urn:uuid:ccb5f469-3195-8c4a-a0c0-1dd6df9f2346",
            ContentDerivedIdentity.DeriveUuidUrn(
                ContentDerivedIdentity.RenderReceiptScope,
                Bytes));
    }

    [TestMethod]
    public void ADifferentScopeGivesADifferentIdentityForTheSameBytes()
    {
        // Why the scope is hashed rather than decorative: two artifact kinds that happen to carry
        // the same canonical bytes must not collide onto one identity.
        Assert.AreNotEqual(
            ContentDerivedIdentity.DeriveUuidUrn("scope/1", Bytes),
            ContentDerivedIdentity.DeriveUuidUrn("scope/2", Bytes));
    }

    [TestMethod]
    public void TheScopeAndTheContentCannotBeRecutIntoEachOther()
    {
        // The separator earns its place here. Without it, scope "ab" with content "c" and scope
        // "a" with content "bc" hash the same input and collide, which is a real way for two
        // different artifacts to be given one name.
        Assert.AreNotEqual(
            ContentDerivedIdentity.DeriveUuidUrn("ab", "c"u8),
            ContentDerivedIdentity.DeriveUuidUrn("a", "bc"u8));
    }

    [TestMethod]
    public void OneChangedByteChangesTheIdentity()
    {
        var altered = Bytes.ToArray();
        altered[^1] ^= 0x01;

        Assert.AreNotEqual(
            ContentDerivedIdentity.DeriveUuidUrn("scope/1", Bytes),
            ContentDerivedIdentity.DeriveUuidUrn("scope/1", altered));
    }

    [TestMethod]
    public void TheIdentityIsAWellFormedVersionEightUuidUrn()
    {
        var urn = ContentDerivedIdentity.DeriveUuidUrn("scope/1", Bytes);

        StringAssert.StartsWith(urn, "urn:uuid:");
        var value = urn["urn:uuid:".Length..];
        Assert.AreEqual(36, value.Length);
        Assert.AreEqual(value, value.ToLowerInvariant(), "identities are exact lowercase URNs");

        // Version 8 is what RFC 9562 reserves for a custom derivation, and the variant bits mark
        // it as an RFC identifier rather than a legacy one. Asserted on the string rather than by
        // re-deriving, so a change to the bit twiddling fails here.
        Assert.AreEqual('8', value[14], "the version nibble must say version 8");
        Assert.IsTrue(
            value[19] is '8' or '9' or 'a' or 'b',
            $"the variant nibble must be RFC 9562's, was '{value[19]}'");

        // And it must survive the validator every artifact reference goes through, which is the
        // only reason the shape matters at all.
        _ = new SourceArtifactRef(urn, new string('a', 64));
    }

    [TestMethod]
    public void AnEmptyScopeIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ContentDerivedIdentity.DeriveUuidUrn("  ", Bytes));
    }

    [TestMethod]
    public void EmptyContentStillDerivesAnIdentity()
    {
        // Not an error: an artifact with no bytes is a legitimate thing to name, and refusing here
        // would push the caller into minting a fresh identity instead, which is the defect.
        var urn = ContentDerivedIdentity.DeriveUuidUrn("scope/1", ReadOnlySpan<byte>.Empty);

        Assert.AreEqual(urn, ContentDerivedIdentity.DeriveUuidUrn("scope/1", ReadOnlySpan<byte>.Empty));
        Assert.AreNotEqual(urn, ContentDerivedIdentity.DeriveUuidUrn("scope/1", Bytes));
    }
}
