using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Formex item roles (S7).
///
/// The load-bearing test here is the order inversion. Every name in this file was read off live
/// publisher responses on 2026-09-02, and the ordering genuinely reverses between listings: in the
/// original-act listings the descriptor is served first, in the consolidated listing the main text
/// is. A producer keyed on stream order or on the DOC_n segment would have fetched kilobyte
/// descriptors in place of legal texts for part of the corpus, silently, because a descriptor is
/// well-formed XML that parses.
/// </summary>
[TestClass]
public sealed class EuFormexItemRoleTests
{
    private const string ConsolidatedMain = "CL2016R0679EN0000020.0001.xml";
    private const string ConsolidatedDescriptor = "CL2016R0679EN0000020.0001.doc.xml";
    private const string OriginalActMain = "L_2016119EN.01000101.xml";
    private const string Manifestation = "5f2552c2-11bd-11e6-ba9a-01aa75ed71a1.0022.01";

    [TestMethod]
    public void TheGrammarIsReadOffMeasuredPublisherNamesRatherThanAFixture()
    {
        var main = EuFormexStreamName.TryParse(ConsolidatedMain, out _);
        Assert.IsNotNull(main);
        Assert.AreEqual(EuFormexItemRole.MainText, main.Role);
        Assert.AreEqual("32016R0679", main.WorkCelex, "the work CELEX is derived from the grammar");
        Assert.AreEqual("EN", main.Language);
        Assert.AreEqual(ConsolidatedMain, main.Value, "the publisher name is retained unmodified");

        var descriptor = EuFormexStreamName.TryParse(ConsolidatedDescriptor, out _);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual(EuFormexItemRole.Descriptor, descriptor.Role);
        Assert.AreEqual("32016R0679", descriptor.WorkCelex);
    }

    [TestMethod]
    public void AnOriginalActAsPublishedIsRefusedByScopeRatherThanAsUnknown()
    {
        // Point-in-time law is served from consolidations, so this shape is out of scope rather
        // than unrecognised. The distinction matters: one is a decision, the other is a gap.
        Assert.IsNull(EuFormexStreamName.TryParse(OriginalActMain, out var refusal));
        Assert.AreEqual(EuFormexRoleRefusal.OriginalActNaming, refusal);

        Assert.IsNull(EuFormexStreamName.TryParse("L_2016119EN.01000101.doc.xml", out var paired));
        Assert.AreEqual(EuFormexRoleRefusal.OriginalActNaming, paired);
    }

    [TestMethod]
    public void UnknownVocabularyFailsClosed()
    {
        foreach (var name in new[]
        {
            "",
            "CL2016R0679EN0000020.0001.pdf",
            "CL2016R0679EN0000020.xml",
            "CL16R0679EN0000020.0001.xml",
            "CL2016R0679E0000020.0001.xml",
            "xCL2016R0679EN0000020.0001.xml",
            "CL2016R0679EN0000020.0001.xml.bak",
        })
        {
            Assert.IsNull(
                EuFormexStreamName.TryParse(name, out var refusal),
                $"'{name}' must not parse");
            Assert.AreEqual(
                EuFormexRoleRefusal.UnrecognisedStreamName,
                refusal,
                $"'{name}' must fail closed as unrecognised");
        }
    }

    [TestMethod]
    public void RoleSurvivesTheOrderInversionMeasuredBetweenListings()
    {
        // Descriptor first, as the original-act listings served it.
        var descriptorFirst = Admit(
            (ConsolidatedDescriptor, 1L),
            (ConsolidatedMain, 2L));

        // Main first, as the consolidated listing served it. Same set, opposite order.
        var mainFirst = Admit(
            (ConsolidatedMain, 1L),
            (ConsolidatedDescriptor, 2L));

        foreach (var set in new[] { descriptorFirst, mainFirst })
        {
            Assert.AreEqual(
                ConsolidatedMain,
                set.MainText.StreamName.Value,
                "the main text is chosen by grammar, not by the order it arrived in");
            Assert.IsNotNull(set.Descriptor);
            Assert.AreEqual(ConsolidatedDescriptor, set.Descriptor.StreamName.Value);
        }

        // The orders really did differ, so the assertion above is not comparing two identical sets.
        Assert.AreEqual(2L, descriptorFirst.MainText.StreamOrder);
        Assert.AreEqual(1L, mainFirst.MainText.StreamOrder);
    }

    [TestMethod]
    public void ADuplicateRoleBlocksDerivation()
    {
        var items = new[] { Item(ConsolidatedMain, 1), Item(ConsolidatedMain, 2) };
        Assert.IsNull(EuFormexItemSet.TryAdmit(items, out var refusal));
        Assert.AreEqual(EuFormexRoleRefusal.DuplicateRole, refusal);
    }

    [TestMethod]
    public void ASetWithNoMainTextBlocksDerivation()
    {
        Assert.IsNull(
            EuFormexItemSet.TryAdmit(new[] { Item(ConsolidatedDescriptor, 1) }, out var refusal));
        Assert.AreEqual(EuFormexRoleRefusal.MainTextAbsent, refusal);

        Assert.IsNull(EuFormexItemSet.TryAdmit(Array.Empty<EuFormexItem>(), out var empty));
        Assert.AreEqual(EuFormexRoleRefusal.MainTextAbsent, empty);
    }

    [TestMethod]
    public void ItemsDisagreeingAboutLanguageOrWorkBlockDerivation()
    {
        var languages = new[]
        {
            Item(ConsolidatedMain, 1),
            Item("CL2016R0679FR0000020.0001.doc.xml", 2),
        };
        Assert.IsNull(EuFormexItemSet.TryAdmit(languages, out var languageRefusal));
        Assert.AreEqual(EuFormexRoleRefusal.LanguageDisagreement, languageRefusal);

        var works = new[]
        {
            Item(ConsolidatedMain, 1),
            Item("CL2019R0947EN0000020.0001.doc.xml", 2),
        };
        Assert.IsNull(EuFormexItemSet.TryAdmit(works, out var workRefusal));
        Assert.AreEqual(EuFormexRoleRefusal.WorkDisagreement, workRefusal);
    }

    [TestMethod]
    public void AnItemRefIsAdmittedAsAnExactCellarItemRoleRatherThanAccepted()
    {
        var streamName = EuFormexStreamName.TryParse(ConsolidatedMain, out _)!;

        // A real Cellar object of the wrong role. No bare member key authorizes an item role.
        Assert.ThrowsExactly<ArgumentException>(
            () => new EuFormexItem(
                Boundary(),
                streamName,
                Object(Manifestation, EuWemiRole.Manifestation),
                streamOrder: 1));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuFormexItem(null!, streamName, ItemRef(), 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new EuFormexItem(Boundary(), streamName, ItemRef(), -1));
    }

    [TestMethod]
    public void TheStreamNameHasExactlyOneConstructionPath()
    {
        var type = typeof(EuFormexStreamName);

        Assert.AreEqual(
            0,
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length,
            "a public constructor would let a caller mint a role the grammar never admitted");

        // Kind, scope, parameters and return, not bare names: a guard that pins names alone passes
        // while the surface underneath it changes.
        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == type
                || (method.ReturnType.IsByRef && method.ReturnType.GetElementType() == type))
            .Select(method => $"{(method.IsStatic ? "static" : "instance")} {method}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "static Lex.V3.Contracts.Source.Europe.EuFormexStreamName TryParse"
                + "(System.String, Lex.V3.Contracts.Source.Europe.EuFormexRoleRefusal ByRef)",
            },
            factories);

        Assert.AreEqual(
            0,
            type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length,
            "a public field is a construction surface too");
    }

    private static EuFormexItemSet Admit(params (string Name, long Order)[] items)
    {
        var set = EuFormexItemSet.TryAdmit(
            items.Select(entry => Item(entry.Name, entry.Order)).ToArray(),
            out var refusal);
        Assert.IsNotNull(set, $"the set was refused as {refusal}");
        return set;
    }

    private static EuFormexItem Item(string streamName, long order)
    {
        var parsed = EuFormexStreamName.TryParse(streamName, out var refusal);
        Assert.IsNotNull(parsed, $"'{streamName}' was refused as {refusal}");
        return new EuFormexItem(Boundary(), parsed, ItemRef("DOC_" + order), order);
    }

    private static SourceObjectRef ItemRef(string document = "DOC_1") =>
        Object(Manifestation + "/" + document, EuWemiRole.Item);

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
