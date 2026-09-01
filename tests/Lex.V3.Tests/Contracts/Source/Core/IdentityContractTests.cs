using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class IdentityContractTests
{
    private const string PublisherUri = "https://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
    private const string CanonicalKey = "jolux|law|2026-01-01|a1";

    [TestMethod]
    public void SourceObjectRefPreservesTheExactProviderNeutralIdentity()
    {
        var identityProfile = ArtifactRef('a', '1');
        var entityKind = RegistryMember('b', '2', "law");

        var source = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            entityKind,
            PublisherUri,
            CanonicalKey,
            Sha256(CanonicalKey),
            identityProfile,
            parentKeyRef: null);

        Assert.AreEqual(SourceCoreSchemaIds.SourceObjectRef, source.Schema);
        Assert.AreEqual(SourceAuthority.Jolux, source.Authority);
        Assert.AreEqual(entityKind, source.EntityKind);
        Assert.AreEqual(PublisherUri, source.PublisherUri);
        Assert.AreEqual(CanonicalKey, source.CanonicalKey);
        Assert.AreEqual(Sha256(CanonicalKey), source.CanonicalKeySha256);
        Assert.AreEqual(identityProfile, source.IdentityProfileRef);
        Assert.IsNull(source.ParentKeyRef);
    }

    [TestMethod]
    public void CanonicalKeyDigestBindsTheExactStrictUtf8BytesWithoutNormalization()
    {
        var composed = "référence";
        var decomposed = "référence";
        Assert.AreNotEqual(composed, decomposed);
        Assert.AreNotEqual(Sha256(composed), Sha256(decomposed));

        _ = ObjectRef(canonicalKey: composed, canonicalKeySha256: Sha256(composed));
        _ = ObjectRef(canonicalKey: decomposed, canonicalKeySha256: Sha256(decomposed));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectRef(
            canonicalKey: composed,
            canonicalKeySha256: Sha256(decomposed)));
    }

    [TestMethod]
    public void DefaultAuthorityAndInvalidUnicodeCanonicalKeyAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ObjectRef(authority: (SourceAuthority)0));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectRef(
            canonicalKey: "\ud800",
            canonicalKeySha256: new string('0', 64)));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-a-digest")]
    [DataRow("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [DataRow("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [DataRow("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [DataRow("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void ArtifactRefRejectsEveryNoncanonicalSha256(string sha256)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SourceArtifactRef(
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            sha256));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")]
    [DataRow("URN:UUID:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")]
    [DataRow("urn:uuid:AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    [DataRow("urn:uuid:not-a-uuid")]
    [DataRow("urn:uuid:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ArtifactRefRequiresAnExactLowercaseUuidUrn(string resourceId)
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new SourceArtifactRef(resourceId, new string('a', 64)));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t")]
    public void RegistryMemberRequiresAnExplicitExactMemberKey(string memberKey)
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new SourceRegistryMemberRef(ArtifactRef('a', '1'), memberKey));
    }

    [TestMethod]
    [DataRow("relative/path")]
    [DataRow("ftp://publisher.example/object")]
    [DataRow("https://user@publisher.example/object")]
    [DataRow("https://publisher.example/object?version=1")]
    [DataRow("https://publisher.example/object#fragment")]
    [DataRow("HTTPS://publisher.example/object")]
    [DataRow("https://publisher.example/%ZZ")]
    [DataRow("https://publisher.example/%")]
    [DataRow("https://publisher.example/%2")]
    [DataRow("")]
    public void PublisherIdentityRequiresAFullHttpUriWithoutSideChannels(string publisherUri)
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ObjectRef(publisherUri: publisherUri));
    }

    [TestMethod]
    public void PublisherUriAndRegistryMemberLexemesArePreservedExactly()
    {
        const string exactMember = "source.Member-01";
        foreach (var exactUri in new[]
                 {
                     "https://publisher.example/A%2Fb",
                     "https://publisher.example/A%2fb",
                 })
        {
            var source = ObjectRef(
                publisherUri: exactUri,
                entityKind: RegistryMember('b', '2', exactMember));

            Assert.AreEqual(exactUri, source.PublisherUri);
            Assert.AreEqual(exactMember, source.EntityKind.MemberKey);
        }
    }

    [TestMethod]
    public void ParentKeyIsNonrecursiveAndBoundToTheChildRegistry()
    {
        var registry = ArtifactRef('b', '2');
        var childKind = new SourceRegistryMemberRef(registry, "manifestation");
        var parent = new SourceObjectKeyRef(
            new SourceRegistryMemberRef(registry, "work"),
            "https://publications.europa.eu/resource/cellar/parent",
            "cellar|work|parent",
            Sha256("cellar|work|parent"));

        var child = ObjectRef(
            authority: SourceAuthority.Cellar,
            entityKind: childKind,
            publisherUri: "https://publications.europa.eu/resource/cellar/child",
            canonicalKey: "cellar|manifestation|child",
            canonicalKeySha256: Sha256("cellar|manifestation|child"),
            parentKeyRef: parent);

        Assert.AreEqual(parent, child.ParentKeyRef);
        CollectionAssert.AreEquivalent(
            new[] { "EntityKind", "PublisherUri", "CanonicalKey", "CanonicalKeySha256" },
            typeof(SourceObjectKeyRef).GetProperties().Select(static value => value.Name).ToArray());
    }

    [TestMethod]
    public void ParentFromAnotherRegistryOrTheChildTupleItselfIsRejected()
    {
        var childKind = RegistryMember('b', '2', "work");
        var otherRegistryParent = new SourceObjectKeyRef(
            RegistryMember('c', '3', "work"),
            "https://publisher.example/parent",
            "parent",
            Sha256("parent"));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectRef(
            entityKind: childKind,
            parentKeyRef: otherRegistryParent));

        var identical = new SourceObjectKeyRef(
            childKind,
            PublisherUri,
            CanonicalKey,
            Sha256(CanonicalKey));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectRef(
            entityKind: childKind,
            parentKeyRef: identical));
    }

    private static SourceObjectRef ObjectRef(
        SourceAuthority authority = SourceAuthority.Jolux,
        SourceRegistryMemberRef? entityKind = null,
        string publisherUri = PublisherUri,
        string canonicalKey = CanonicalKey,
        string? canonicalKeySha256 = null,
        SourceObjectKeyRef? parentKeyRef = null) => new(
            SourceCoreSchemaIds.SourceObjectRef,
            authority,
            entityKind ?? RegistryMember('b', '2', "law"),
            publisherUri,
            canonicalKey,
            canonicalKeySha256 ?? Sha256(canonicalKey),
            ArtifactRef('a', '1'),
            parentKeyRef);

    private static SourceArtifactRef ArtifactRef(char resourceFill, char digestFill) => new(
        $"urn:uuid:{new string(resourceFill, 8)}-{new string(resourceFill, 4)}-4{new string(resourceFill, 3)}-8{new string(resourceFill, 3)}-{new string(resourceFill, 12)}",
        new string(digestFill, 64));

    private static SourceRegistryMemberRef RegistryMember(
        char resourceFill,
        char digestFill,
        string memberKey) => new(ArtifactRef(resourceFill, digestFill), memberKey);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
