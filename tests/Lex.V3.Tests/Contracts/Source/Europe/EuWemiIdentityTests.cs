using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The Union WEMI identity boundary.
///
/// The load-bearing tests are the ones where every property except one is correct: a real
/// expression of another work, a same-named member from another registry, a key whose parent is a
/// genuine work that is simply not this one. Those are the cases a reader cannot catch.
/// </summary>
[TestClass]
public sealed class EuWemiIdentityTests
{
    private const string Work = "3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string OtherWork = "1f2c3d4e-5678-4abc-9def-0123456789ab";

    [TestMethod]
    public void TheFourRolesAreClosedAndSpelledAsTheRegistrySpellsThem()
    {
        AssertTokens<EuWemiRole>(
            "eu_cellar_work", "eu_cellar_expression", "eu_cellar_manifestation", "eu_cellar_item");

        foreach (var role in Enum.GetValues<EuWemiRole>())
        {
            Assert.AreEqual(
                "\"" + EuWemiIdentityBoundary.MemberKeyOf(role) + "\"",
                ContractJson.Serialize(role),
                $"the member key and the wire token disagree for {role}");
        }

        AssertScopeDrift<EuWemiRole>("eu_cellar_resource");
        AssertScopeDrift<EuWemiRole>("work");
    }

    [TestMethod]
    public void TheParentChainIsTheFrbrOneAndAWorkIsARoot()
    {
        Assert.IsNull(EuWemiIdentityBoundary.ParentRoleOf(EuWemiRole.Work));
        Assert.AreEqual(EuWemiRole.Work, EuWemiIdentityBoundary.ParentRoleOf(EuWemiRole.Expression));
        Assert.AreEqual(
            EuWemiRole.Expression, EuWemiIdentityBoundary.ParentRoleOf(EuWemiRole.Manifestation));
        Assert.AreEqual(
            EuWemiRole.Manifestation, EuWemiIdentityBoundary.ParentRoleOf(EuWemiRole.Item));

        // A work that names a parent is not a work, whatever it is labelled.
        var boundary = Boundary();
        Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(
                Object(Work, EuWemiRole.Work, parentKey: OtherWork, parentRole: EuWemiRole.Work),
                EuWemiRole.Work,
                "value"));
    }

    [TestMethod]
    public void EveryRoleAdmitsItsOwnShapeAndNoOther()
    {
        // Walked as a matrix rather than sampled. A grammar checked only against its own role
        // cannot show that a work-shaped key is refused when labelled an expression, and that
        // mislabelling is the whole reason the role is a coordinate rather than an attribute.
        var boundary = Boundary();
        var keys = new Dictionary<EuWemiRole, string>
        {
            [EuWemiRole.Work] = Work,
            [EuWemiRole.Expression] = $"{Work}.0006",
            [EuWemiRole.Manifestation] = $"{Work}.0006.03",
            [EuWemiRole.Item] = $"{Work}.0006.03/DOC_1",
        };

        foreach (var declared in Enum.GetValues<EuWemiRole>())
        {
            foreach (var (shape, key) in keys)
            {
                var reference = Object(key, declared, ParentFor(declared, key));
                if (shape == declared)
                {
                    Assert.AreEqual(key, boundary.Require(reference, declared, "value").CanonicalKey);
                }
                else
                {
                    Assert.ThrowsExactly<ArgumentException>(
                        () => boundary.Require(reference, declared, "value"),
                        $"a {shape}-shaped key was admitted as a {declared}");
                }
            }
        }
    }

    [TestMethod]
    public void AnObjectFromAnotherRegistryOrProfileIsRefused()
    {
        // SourceObjectRef guarantees only that a child and its parent share A registry. Matching the
        // member-key string alone therefore admits a same-named member minted anywhere.
        var boundary = Boundary();
        var key = $"{Work}.0006";

        var foreignRegistry = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(Artifact("cc"), "eu_cellar_expression"),
            Uri(key),
            key,
            Sha256Hex(key),
            Artifact("bb"),
            new SourceObjectKeyRef(
                new SourceRegistryMemberRef(Artifact("cc"), "eu_cellar_work"),
                Uri(Work), Work, Sha256Hex(Work)));
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(foreignRegistry, EuWemiRole.Expression, "value"));
        StringAssert.Contains(thrown.Message, "different registry");

        var foreignProfile = Object(key, EuWemiRole.Expression, Work, profile: Artifact("dd"));
        var profileThrown = Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(foreignProfile, EuWemiRole.Expression, "value"));
        StringAssert.Contains(profileThrown.Message, "identity profile");
    }

    [TestMethod]
    public void AGenuineExpressionOfAnotherWorkIsRefused()
    {
        // The case a reader cannot catch. Every property is correct: right authority, right
        // registry, right profile, right role, well-formed key, and a parent that is a real Cellar
        // work. It is simply not this expression's work.
        var boundary = Boundary();
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(
                Object($"{Work}.0006", EuWemiRole.Expression, parentKey: OtherWork),
                EuWemiRole.Expression,
                "value"));
        StringAssert.Contains(thrown.Message, "does not descend from");

        // And the sibling case: a manifestation hung off the wrong expression of the right work.
        Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(
                Object($"{Work}.0006.03", EuWemiRole.Manifestation, parentKey: $"{Work}.0007",
                    parentRole: EuWemiRole.Expression),
                EuWemiRole.Manifestation,
                "value"));

        // A parent equal to the child is not an ancestor either.
        Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(
                Object(Work, EuWemiRole.Expression, parentKey: Work),
                EuWemiRole.Expression,
                "value"));
    }

    [TestMethod]
    public void MalformedAndNearMissKeysAreRefused()
    {
        var boundary = Boundary();
        var hostile = new (string Key, EuWemiRole Role, string Why)[]
        {
            ($"{Work}.6", EuWemiRole.Expression, "three digits short of the publisher width"),
            ($"{Work}.00006", EuWemiRole.Expression, "one digit too wide"),
            ($"{Work}.0006.3", EuWemiRole.Manifestation, "manifestation width is two"),
            ($"{Work}.0006.03.01", EuWemiRole.Manifestation, "one level too deep"),
            ($"{Work}.0006.0a", EuWemiRole.Manifestation, "not digits"),
            ("not-a-uuid.0006", EuWemiRole.Expression, "the head is not a UUID"),
            ($"{Work[..^1]}.0006", EuWemiRole.Expression, "a truncated UUID"),
            ($"{Work}.0006.03/", EuWemiRole.Item, "an empty stream name"),
            ($"{Work}.0006.03/DOC_1/EXTRA", EuWemiRole.Item, "a nested stream path"),
            ($"{Work}.0006/DOC_1", EuWemiRole.Item, "a stream on an expression"),
            ($"{Work}/DOC_1", EuWemiRole.Item, "a stream on a work"),
        };

        foreach (var (key, role, why) in hostile)
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => boundary.Require(
                    Object(key, role, ParentFor(role, key)), role, "value"),
                $"admitted {key} as a {role} despite {why}");
        }
    }

    [TestMethod]
    public void ANonCellarAuthorityIsRefusedWhateverElseIsCorrect()
    {
        var boundary = Boundary();
        var key = $"{Work}.0006";
        var jolux = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(Registry, "eu_cellar_expression"),
            Uri(key), key, Sha256Hex(key), Profile,
            new SourceObjectKeyRef(
                new SourceRegistryMemberRef(Registry, "eu_cellar_work"),
                Uri(Work), Work, Sha256Hex(Work)));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(jolux, EuWemiRole.Expression, "value"));
        StringAssert.Contains(thrown.Message, "Cellar authority");
    }

    [TestMethod]
    public void GuardsThatTheFixtureCannotReachByAccidentAreReachedOnPurpose()
    {
        // Three mutations survived the first run of this file, and all three survived for the same
        // reason: the fixture derives the member key and the parent from the role, so it can only
        // build shapes that are already legal. A fixture that cannot express an illegal shape cannot
        // test the guard that refuses one. These build the illegal shapes by hand.
        var boundary = Boundary();
        var key = $"{Work}.0006";

        // Right registry, right profile, right authority, right grammar, wrong member key.
        var wrongKind = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(Registry, "eu_cellar_manifestation"),
            Uri(key), key, Sha256Hex(key), Profile,
            new SourceObjectKeyRef(
                new SourceRegistryMemberRef(Registry, "eu_cellar_work"),
                Uri(Work), Work, Sha256Hex(Work)));
        var kindThrown = Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(wrongKind, EuWemiRole.Expression, "value"));
        StringAssert.Contains(kindThrown.Message, "eu_cellar_expression");

        // A valid child whose parent key is malformed for the parent role.
        var badParent = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(Registry, "eu_cellar_expression"),
            Uri(key), key, Sha256Hex(key), Profile,
            new SourceObjectKeyRef(
                new SourceRegistryMemberRef(Registry, "eu_cellar_work"),
                Uri("not-a-uuid"), "not-a-uuid", Sha256Hex("not-a-uuid")));
        var parentThrown = Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(badParent, EuWemiRole.Expression, "value"));
        StringAssert.Contains(parentThrown.Message, "parent key");

        // A non-root carrying no parent at all, for every role that has one.
        foreach (var role in Enum.GetValues<EuWemiRole>())
        {
            if (EuWemiIdentityBoundary.ParentRoleOf(role) is null)
            {
                continue;
            }

            var orphanKey = role switch
            {
                EuWemiRole.Expression => $"{Work}.0006",
                EuWemiRole.Manifestation => $"{Work}.0006.03",
                _ => $"{Work}.0006.03/DOC_1",
            };
            var orphan = new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef,
                SourceAuthority.Cellar,
                new SourceRegistryMemberRef(Registry, EuWemiIdentityBoundary.MemberKeyOf(role)),
                Uri(orphanKey), orphanKey, Sha256Hex(orphanKey), Profile,
                parentKeyRef: null);
            var orphanThrown = Assert.ThrowsExactly<ArgumentException>(
                () => boundary.Require(orphan, role, "value"),
                $"a {role} with no parent was admitted");
            StringAssert.Contains(orphanThrown.Message, "must name its parent");
        }
    }

    [TestMethod]
    public void TheBoundaryDecidesIdentityAndNothingElse()
    {
        // Pinned as the exact public surface rather than as forbidden substrings. A name grep is
        // vacuous: it constrains what a future member may be called and not what the type may do,
        // so it passes unchanged if Require grows an acquisition-ledger parameter and starts
        // refusing objects that were never fetched. An exact set fails on any addition.
        var declared = typeof(EuWemiIdentityBoundary)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { ".ctor", "MemberKeyOf", "ParentRoleOf", "Require" },
            declared,
            "the public surface changed; identity is all this type may decide, so an addition " +
            "needs a decision rather than a test update: " + string.Join(", ", declared));

        // And the one method that takes an object takes nothing else, so it cannot consult
        // acquisition, completeness or coverage even if somebody wanted it to.
        var parameters = typeof(EuWemiIdentityBoundary)
            .GetMethod(nameof(EuWemiIdentityBoundary.Require))!
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { typeof(SourceObjectRef), typeof(EuWemiRole), typeof(string) }, parameters);
    }

    [TestMethod]
    public void OneCellarObjectHasExactlyOneAdmittedIdentity()
    {
        // Guid.TryParseExact is case-insensitive on the hex digits and strips leading and trailing
        // white space. I measured it rather than trusting the documentation: uppercase, a leading
        // space, a trailing space, a newline and a non-breaking space all parse, and only the exact
        // lowercase form round-trips through ToString("D").
        //
        // Without the round-trip each of those is a distinct canonical key with a distinct digest,
        // all naming one work, and any index keyed on the canonical key then holds several works.
        var boundary = Boundary();
        var canonical = $"{Work}.0006";

        Assert.AreEqual(
            canonical,
            boundary.Require(
                Object(canonical, EuWemiRole.Expression, Work),
                EuWemiRole.Expression,
                "value").CanonicalKey);

        var upper = $"{Work.ToUpperInvariant()}.0006";
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(
                Object(upper, EuWemiRole.Expression, Work.ToUpperInvariant()),
                EuWemiRole.Expression,
                "value"),
            "an uppercase UUID gave this work a second admitted identity");
        StringAssert.Contains(thrown.Message, "grammar");

        // The whitespace spellings cannot even be built now, because the publisher URI must name
        // the key and a URI may not carry a space. Asserted so the reason is on the record: the
        // round-trip closes the casing half and the URI binding closes the whitespace half.
        foreach (var padded in new[] { " " + Work, Work + " ", Work + "\n" })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => Object(padded, EuWemiRole.Work),
                $"a padded key built a reference at all: \"{padded}\"");
        }
    }

    [TestMethod]
    public void AParentCarryingTheWrongEntityKindIsRefused()
    {
        // A whole-statement deletion of the parent registry-member check left the entire class
        // green, so the guard was there and nothing reached it. Its registry half is genuinely
        // unreachable, because SourceObjectRef already forces a child and its parent to share one
        // registry and the child's was checked first. The member-key half is not: without it a
        // parent shaped like a work but labelled a manifestation is admitted as this expression's
        // work, and the role stops being a coordinate again.
        var boundary = Boundary();
        var key = $"{Work}.0006";

        var mislabelledParent = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(Registry, "eu_cellar_expression"),
            Uri(key),
            key,
            Sha256Hex(key),
            Profile,
            new SourceObjectKeyRef(
                new SourceRegistryMemberRef(Registry, "eu_cellar_manifestation"),
                Uri(Work), Work, Sha256Hex(Work)));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(mislabelledParent, EuWemiRole.Expression, "value"));
        StringAssert.Contains(thrown.Message, "eu_cellar_work");
    }

    [TestMethod]
    public void ThePublisherUriMustNameTheCanonicalKey()
    {
        // The URI is the only field that denotes the publisher's object, and nothing read it.
        // Two different real Cellar objects could carry one canonical key and be admitted as one
        // identity, which is the failure this whole type exists to end rather than relocate.
        var boundary = Boundary();
        var key = $"{Work}.0006";

        var strangerUri = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(Registry, "eu_cellar_expression"),
            Uri($"{OtherWork}.0006"),
            key,
            Sha256Hex(key),
            Profile,
            new SourceObjectKeyRef(
                new SourceRegistryMemberRef(Registry, "eu_cellar_work"),
                Uri(Work), Work, Sha256Hex(Work)));
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(strangerUri, EuWemiRole.Expression, "value"));
        StringAssert.Contains(thrown.Message, "does not name");

        // The parent is bound the same way, so a parent whose URI is a different object is refused
        // even though its key is the right one.
        var strangerParent = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(Registry, "eu_cellar_expression"),
            Uri(key),
            key,
            Sha256Hex(key),
            Profile,
            new SourceObjectKeyRef(
                new SourceRegistryMemberRef(Registry, "eu_cellar_work"),
                Uri(OtherWork), Work, Sha256Hex(Work)));
        Assert.ThrowsExactly<ArgumentException>(
            () => boundary.Require(strangerParent, EuWemiRole.Expression, "value"));
    }

    [TestMethod]
    public void TheBoundaryRequiresItsOwnReferences()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuWemiIdentityBoundary(null!, Profile));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuWemiIdentityBoundary(Registry, null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => Boundary().Require(null!, EuWemiRole.Work, "value"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Boundary().Require(Object(Work, EuWemiRole.Work), (EuWemiRole)99, "value"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => EuWemiIdentityBoundary.MemberKeyOf((EuWemiRole)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => EuWemiIdentityBoundary.ParentRoleOf((EuWemiRole)99));
    }

    private static SourceArtifactRef Registry => Artifact("aa");

    private static SourceArtifactRef Profile => Artifact("bb");

    private static EuWemiIdentityBoundary Boundary() => new(Registry, Profile);

    private static string? ParentFor(EuWemiRole role, string key)
    {
        var parent = EuWemiIdentityBoundary.ParentRoleOf(role);
        if (parent is null)
        {
            return null;
        }

        var stream = key.IndexOf('/', StringComparison.Ordinal);
        var head = stream < 0 ? key : key[..stream];
        if (role == EuWemiRole.Item)
        {
            return head;
        }

        var cut = head.LastIndexOf('.');
        return cut < 0 ? head : head[..cut];
    }

    private static SourceObjectRef Object(
        string key,
        EuWemiRole role,
        string? parentKey = null,
        EuWemiRole? parentRole = null,
        SourceArtifactRef? profile = null)
    {
        var parent = EuWemiIdentityBoundary.ParentRoleOf(role);
        SourceObjectKeyRef? parentRef = null;
        // Attached whenever a caller supplies one, including for a work, so the "a root that names a
        // parent" case is constructible. A fixture that can only build legal shapes cannot test the
        // guard that refuses illegal ones.
        if (parentKey is not null)
        {
            var kind = EuWemiIdentityBoundary.MemberKeyOf(parentRole ?? parent ?? role);
            parentRef = new SourceObjectKeyRef(
                new SourceRegistryMemberRef(Registry, kind),
                Uri(parentKey), parentKey, Sha256Hex(parentKey));
        }

        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(Registry, EuWemiIdentityBoundary.MemberKeyOf(role)),
            Uri(key),
            key,
            Sha256Hex(key),
            profile ?? Profile,
            parentRef);
    }

    private static string Uri(string key) =>
        "http://publications.europa.eu/resource/cellar/" + key;

    private static SourceArtifactRef Artifact(string seed) =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000" + seed, new string(seed[0], 64));

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void AssertTokens<TEnum>(params string[] expected)
        where TEnum : struct, Enum
    {
        var members = Enum.GetValues<TEnum>();
        Assert.AreEqual(expected.Length, members.Length, $"{typeof(TEnum).Name} member count");
        for (var index = 0; index < members.Length; index++)
        {
            Assert.AreEqual("\"" + expected[index] + "\"", ContractJson.Serialize(members[index]));
        }
    }

    private static void AssertScopeDrift<TEnum>(string hostile)
        where TEnum : struct, Enum
    {
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<TEnum>(JsonSerializer.Serialize(hostile)),
            $"{typeof(TEnum).Name} accepted the unknown token {hostile}");
    }
}
