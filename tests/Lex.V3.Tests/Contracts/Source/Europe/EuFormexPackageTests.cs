using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The Formex package: the join that makes an item classification load-bearing.
///
/// The load-bearing test is the sibling. An item set from another manifestation of the same act in
/// the same language agrees about everything <see cref="EuFormexItemSet"/> checks, because work and
/// language are exactly what sibling packages share. Only the observed parent tells them apart, and
/// a package that attached the wrong set would name a real legal text belonging to a different
/// consolidation state.
/// </summary>
[TestClass]
public sealed class EuFormexPackageTests
{
    private const string Work = "5f2552c2-11bd-11e6-ba9a-01aa75ed71a1";
    private const string Expression = Work + ".0022";
    private const string Manifestation = Expression + ".01";
    private const string SiblingManifestation = Expression + ".02";
    private const string Gdpr = "32016R0679";
    private const string Main = "CL2016R0679EN0000020.0001.xml";
    private const string Descriptor = "CL2016R0679EN0000020.0001.doc.xml";

    [TestMethod]
    public void AnAdmittedPackageCarriesItsMainTextAsTheOnlyBodyReference()
    {
        var package = EuFormexPackage.TryAdmit(
            Boundary(), Object(Manifestation, EuWemiRole.Manifestation),
            Object(Expression, EuWemiRole.Expression), Items(Manifestation), "EN", out var refusal);

        Assert.IsNotNull(package, $"refused as {refusal}");
        Assert.AreEqual(EuFormexPackageRefusal.None, refusal, "a true statement on the success path");
        Assert.AreEqual(Gdpr, package.WorkCelex);
        Assert.AreEqual("EN", package.Language);

        Assert.AreEqual(
            package.Items.MainText.ItemRef.CanonicalKey,
            package.BodyRef.CanonicalKey,
            "the body is the main text item and nothing else");
        Assert.AreEqual(Main, package.Items.MainText.StreamName.Value);
        Assert.IsNotNull(package.DescriptorRef);
        Assert.AreNotEqual(
            package.BodyRef.CanonicalKey,
            package.DescriptorRef.CanonicalKey,
            "the descriptor is never the body");
    }

    [TestMethod]
    public void AnItemSetFromASiblingManifestationDoesNotAttach()
    {
        // Same act, same language, different consolidation state. Everything EuFormexItemSet checks
        // agrees, because agreement about work and language is what siblings have. Attaching this
        // would give the package a real legal text belonging to a different state.
        var sibling = Items(SiblingManifestation);

        var package = EuFormexPackage.TryAdmit(
            Boundary(), Object(Manifestation, EuWemiRole.Manifestation),
            Object(Expression, EuWemiRole.Expression), sibling, "EN", out var refusal);

        Assert.IsNull(package);
        Assert.AreEqual(EuFormexPackageRefusal.ManifestationDisagreement, refusal);

        // And the sibling is not defective in itself: it admits under its own manifestation.
        Assert.IsNotNull(EuFormexPackage.TryAdmit(
            Boundary(), Object(SiblingManifestation, EuWemiRole.Manifestation),
            Object(Expression, EuWemiRole.Expression), Items(SiblingManifestation), "EN", out _));
    }

    [TestMethod]
    public void AManifestationOfAnotherExpressionDoesNotAttach()
    {
        const string OtherExpression = Work + ".0099";

        var package = EuFormexPackage.TryAdmit(
            Boundary(), Object(Manifestation, EuWemiRole.Manifestation),
            Object(OtherExpression, EuWemiRole.Expression), Items(Manifestation), "EN", out var refusal);

        Assert.IsNull(package);
        Assert.AreEqual(EuFormexPackageRefusal.ExpressionDisagreement, refusal);
    }

    [TestMethod]
    public void ALanguageOtherThanTheOneOpenedForIsRefused()
    {
        // Language is expression level at this publisher: a French request returns a different
        // manifestation, not a filtered view of this one.
        var package = EuFormexPackage.TryAdmit(
            Boundary(), Object(Manifestation, EuWemiRole.Manifestation),
            Object(Expression, EuWemiRole.Expression), Items(Manifestation), "FR", out var refusal);

        Assert.IsNull(package);
        Assert.AreEqual(EuFormexPackageRefusal.LanguageDisagreement, refusal);
    }

    [TestMethod]
    public void ReferencesAreAdmittedAsExactRolesRatherThanAccepted()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => EuFormexPackage.TryAdmit(
                Boundary(), Object(Expression, EuWemiRole.Expression),
                Object(Expression, EuWemiRole.Expression), Items(Manifestation), "EN", out _),
            "an expression passed as the manifestation must be refused by the boundary");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => EuFormexPackage.TryAdmit(
                null!, Object(Manifestation, EuWemiRole.Manifestation),
                Object(Expression, EuWemiRole.Expression), Items(Manifestation), "EN", out _));

        Assert.ThrowsExactly<ArgumentException>(
            () => EuFormexPackage.TryAdmit(
                Boundary(), Object(Manifestation, EuWemiRole.Manifestation),
                Object(Expression, EuWemiRole.Expression), Items(Manifestation), "", out _));
    }

    [TestMethod]
    public void TheRefusalVocabularyIsClosedAndSpelledForTheWire()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"", "\"expression_disagreement\"", "\"language_disagreement\"",
                "\"manifestation_disagreement\"",
            },
            Enum.GetValues<EuFormexPackageRefusal>().Select(ContractJson.Serialize).ToArray());
    }

    [TestMethod]
    public void ThePackageHasExactlyOneConstructionPath()
    {
        var type = typeof(EuFormexPackage);

        var constructors = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsTrue(constructors.Length > 0);
        Assert.IsTrue(
            constructors.All(constructor => constructor.IsPrivate),
            "a non-private constructor would let a caller mint a body reference nothing admitted");

        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == type
                || (method.ReturnType.IsByRef && method.ReturnType.GetElementType() == type)
                || method.GetParameters().Any(parameter =>
                    parameter.ParameterType.IsByRef
                    && parameter.ParameterType.GetElementType() == type))
            .Select(method => $"{(method.IsStatic ? "static" : "instance")} {method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "static TryAdmit" }, factories);
        Assert.AreEqual(
            0,
            type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length);
    }

    private static EuFormexItemSet Items(string manifestation)
    {
        var set = EuFormexItemSet.TryAdmit(
            new[]
            {
                Item(Main, 1, manifestation),
                Item(Descriptor, 2, manifestation),
            },
            out var refusal);
        Assert.IsNotNull(set, $"fixture set refused as {refusal}");
        return set;
    }

    private static EuFormexItem Item(string streamName, long order, string manifestation)
    {
        var parsed = EuFormexStreamName.TryParse(streamName, Gdpr, out var refusal);
        Assert.IsNotNull(parsed, $"'{streamName}' refused as {refusal}");
        return new EuFormexItem(
            Boundary(), parsed, Object(manifestation + "/DOC_" + order, EuWemiRole.Item), order);
    }

    private static readonly SourceArtifactRef Registry =
        new("urn:uuid:00000000-0000-4000-8000-0000000000aa", new string('a', 64));

    private static readonly SourceArtifactRef Profile =
        new("urn:uuid:00000000-0000-4000-8000-0000000000bb", new string('b', 64));

    private static EuWemiIdentityBoundary Boundary() => new(Registry, Profile);

    private static SourceObjectRef Object(string key, EuWemiRole role)
    {
        SourceObjectKeyRef? parentRef = null;
        var parentRole = EuWemiIdentityBoundary.ParentRoleOf(role);
        if (parentRole is not null)
        {
            var stream = key.IndexOf('/', StringComparison.Ordinal);
            var head = stream < 0 ? key : key[..stream];
            string parentKey;
            if (role == EuWemiRole.Item)
            {
                parentKey = head;
            }
            else
            {
                var cut = head.LastIndexOf('.');
                parentKey = cut < 0 ? head : head[..cut];
            }

            parentRef = new SourceObjectKeyRef(
                new SourceRegistryMemberRef(
                    Registry, EuWemiIdentityBoundary.MemberKeyOf(parentRole.Value)),
                Uri(parentKey), parentKey, Sha256Hex(parentKey));
        }

        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(Registry, EuWemiIdentityBoundary.MemberKeyOf(role)),
            Uri(key),
            key,
            Sha256Hex(key),
            Profile,
            parentRef);
    }

    private static string Uri(string key) =>
        "http://publications.europa.eu/resource/cellar/" + key;

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
