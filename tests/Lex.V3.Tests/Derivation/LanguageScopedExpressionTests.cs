using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Derivation;

/// <summary>
/// The language-scoped expression boundary: the defect B31-L0071 names, and the ways a contract
/// could quietly reacquire it.
/// </summary>
/// <remarks>
/// B31-L0071 locates E9's fault in a "writer's language-keyed merge" that left the corpus unable to
/// represent a second same-language expression in one version directory. This build has no language
/// or expression carrier at all, so the work is additive rather than corrective - and the tests that
/// matter most are the ones that would fail if identity ever folded language or date back in.
/// </remarks>
[TestClass]
public sealed class LanguageScopedExpressionTests
{
    private const string Work = "https://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
    private const string ObservedAtText = "2026-01-02T03:04:05.0000000+00:00";

    /// <summary>The defect B31-L0071 names, stated as the property that must hold.</summary>
    [TestMethod]
    public void TwoExpressionsOfOneWorkInOneLanguageCoexist()
    {
        var set = new LanguageScopedExpressionSet();
        var first = Expression("rect/1", "fr", bytesFill: '1');
        var second = Expression("rect/2", "fr", bytesFill: '2');

        Assert.IsTrue(set.TryAppend(first, out var firstRefusal));
        Assert.IsTrue(set.TryAppend(second, out var secondRefusal));

        Assert.AreEqual(LanguageScopedExpressionAppendRefusal.None, firstRefusal);
        Assert.AreEqual(LanguageScopedExpressionAppendRefusal.None, secondRefusal);
        Assert.HasCount(2, set.Expressions, "a language-keyed identity would have folded these.");
    }

    /// <summary>Nor does the corrigendum date collapse two expressions into one.</summary>
    [TestMethod]
    public void TwoExpressionsSharingWorkLanguageAndDateCoexist()
    {
        var set = new LanguageScopedExpressionSet();
        var date = new PublisherCorrigendumDate("2026-01-02", "http://www.w3.org/2001/XMLSchema#date");

        Assert.IsTrue(set.TryAppend(Expression("rect/1", "de", '1', date), out _));
        Assert.IsTrue(set.TryAppend(Expression("rect/2", "de", '2', date), out _));

        Assert.HasCount(2, set.Expressions, "two corrigenda on one date are two expressions.");
    }

    /// <summary>Re-presenting one identity with identical retained bytes changes nothing.</summary>
    [TestMethod]
    public void RepresentingOneIdentityWithIdenticalBytesIsIdempotent()
    {
        var set = new LanguageScopedExpressionSet();
        Assert.IsTrue(set.TryAppend(Expression("rect/1", "en", '7'), out _));

        Assert.IsTrue(
            set.TryAppend(Expression("rect/1", "en", '7'), out var refusal),
            "a replayed run must converge rather than refuse.");
        Assert.AreEqual(LanguageScopedExpressionAppendRefusal.None, refusal);
        Assert.HasCount(1, set.Expressions, "and it must not grow the store.");
    }

    /// <summary>The same identity with different retained bytes refuses by name.</summary>
    [TestMethod]
    public void RepresentingOneIdentityWithDifferentBytesRefusesByName()
    {
        var set = new LanguageScopedExpressionSet();
        var held = Expression("rect/1", "en", 'a');
        Assert.IsTrue(set.TryAppend(held, out _));

        Assert.IsFalse(set.TryAppend(Expression("rect/1", "en", 'b'), out var refusal));
        Assert.AreEqual(LanguageScopedExpressionAppendRefusal.ConflictingCanonicalBytes, refusal);
        Assert.HasCount(1, set.Expressions);
        Assert.AreSame(
            held, set.Expressions[0],
            "an append-only store never replaces what it already admitted.");
    }

    /// <summary>An absent publisher date stays absent, and nothing stands in for it.</summary>
    [TestMethod]
    public void AnAbsentPublisherDateStaysAbsent()
    {
        var expression = Expression("rect/1", "lb", '3', publisherCorrigendumDate: null);

        Assert.IsNull(
            expression.PublisherCorrigendumDate,
            "no default, sentinel or run date may stand in for a date the publisher never gave.");
    }

    /// <summary>A supplied date is carried exactly, lexical form and datatype together.</summary>
    [TestMethod]
    public void ASuppliedPublisherDateIsCarriedVerbatim()
    {
        var expression = Expression(
            "rect/1", "it", '4',
            new PublisherCorrigendumDate("2026-01", "http://www.w3.org/2001/XMLSchema#gYearMonth"));

        Assert.AreEqual("2026-01", expression.PublisherCorrigendumDate!.RawLexical);
        Assert.AreEqual(
            "http://www.w3.org/2001/XMLSchema#gYearMonth",
            expression.PublisherCorrigendumDate.DatatypeIri,
            "the datatype carries the precision; widening it would invent precision.");
    }

    /// <summary>
    /// The only door takes evidence, and offers no parameter by which a caller asserts provenance.
    /// </summary>
    [TestMethod]
    public void TheOnlyDoorTakesEvidenceAndNoProvenanceAssertion()
    {
        var door = typeof(LanguageScopedExpression).GetMethod(
            nameof(LanguageScopedExpression.FromRetainedSource));

        Assert.IsNotNull(door);
        CollectionAssert.AreEqual(
            new[]
            {
                typeof(LanguageScopedExpressionIdentity),
                typeof(string),
                typeof(PublisherCorrigendumDate),
                typeof(SourceObjectRef),
                typeof(DurableBlobWriteReceipt),
            },
            door.GetParameters().Select(static parameter => parameter.ParameterType).ToArray(),
            "a bool asserting publisher backing would be a caller's claim about evidence, "
            + "not evidence.");

        Assert.IsEmpty(
            typeof(LanguageScopedExpression).GetConstructors(),
            "and there is no public constructor beside it.");
    }

    /// <summary>Identity is exactly the publisher work and the publisher expression within it.</summary>
    [TestMethod]
    public void IdentityIsExactlyWorkAndExpression()
    {
        Assert.AreEqual(
            new LanguageScopedExpressionIdentity(Work, "rect/1"),
            new LanguageScopedExpressionIdentity(Work, "rect/1"));
        Assert.AreNotEqual(
            new LanguageScopedExpressionIdentity(Work, "rect/1"),
            new LanguageScopedExpressionIdentity(Work, "rect/2"));
        Assert.AreNotEqual(
            new LanguageScopedExpressionIdentity(Work, "rect/1"),
            new LanguageScopedExpressionIdentity(Work + "/x", "rect/1"));
    }

    /// <summary>Every identifier the contract accepts is bounded printable ASCII.</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("rect/1\n")]
    public void InvalidIdentifiersAreRejected(string invalid)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new LanguageScopedExpressionIdentity(invalid, "rect/1"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new LanguageScopedExpressionIdentity(Work, invalid));
        Assert.ThrowsExactly<ArgumentException>(
            () => new PublisherCorrigendumDate(invalid, "http://www.w3.org/2001/XMLSchema#date"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new PublisherCorrigendumDate("2026-01-02", invalid));
        Assert.ThrowsExactly<ArgumentException>(
            () => LanguageScopedExpression.FromRetainedSource(
                new LanguageScopedExpressionIdentity(Work, "rect/1"),
                invalid,
                null,
                SourceObject(),
                Receipt('1')));
    }

    /// <summary>Absent evidence is refused rather than defaulted.</summary>
    [TestMethod]
    public void AbsentEvidenceIsRejected()
    {
        var identity = new LanguageScopedExpressionIdentity(Work, "rect/1");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => LanguageScopedExpression.FromRetainedSource(
                null!, "fr", null, SourceObject(), Receipt('1')));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LanguageScopedExpression.FromRetainedSource(
                identity, "fr", null, null!, Receipt('1')));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LanguageScopedExpression.FromRetainedSource(
                identity, "fr", null, SourceObject(), null!));
    }

    /// <summary>The refusal's wire token is exactly what a reader will see.</summary>
    [TestMethod]
    public void TheConflictRefusalHasTheExactWireToken() =>
        Assert.AreEqual(
            "\"conflicting_canonical_bytes\"",
            ContractJson.Serialize(
                LanguageScopedExpressionAppendRefusal.ConflictingCanonicalBytes));

    /// <summary>The set compares on the retained bytes, not on anything a caller states.</summary>
    /// <remarks>
    /// Two expressions differing ONLY in their retained content address must conflict. If the set
    /// compared on anything else - identity alone, or a caller-supplied digest - this would pass by
    /// admitting the second silently.
    /// </remarks>
    [TestMethod]
    public void ConflictIsDecidedByTheRetainedContentAddress()
    {
        var set = new LanguageScopedExpressionSet();
        var first = Expression("rect/1", "fr", 'c');
        var second = Expression("rect/1", "fr", 'd');

        Assert.AreNotEqual(first.CanonicalBytesSha256, second.CanonicalBytesSha256);
        Assert.IsTrue(set.TryAppend(first, out _));
        Assert.IsFalse(set.TryAppend(second, out var refusal));
        Assert.AreEqual(LanguageScopedExpressionAppendRefusal.ConflictingCanonicalBytes, refusal);
    }

    private static LanguageScopedExpression Expression(
        string expressionId,
        string language,
        char bytesFill,
        PublisherCorrigendumDate? publisherCorrigendumDate = null) =>
        LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(Work, expressionId),
            language,
            publisherCorrigendumDate,
            SourceObject(),
            Receipt(bytesFill));

    private static SourceObjectRef SourceObject() => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Jolux,
        new SourceRegistryMemberRef(ArtifactRef('b', '2'), "law"),
        Work,
        "jolux|law|2026-01-01|a1",
        Sha256("jolux|law|2026-01-01|a1"),
        ArtifactRef('a', '1'),
        parentKeyRef: null);

    private static DurableBlobWriteReceipt Receipt(char contentFill)
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            new string(contentFill, 64),
            4,
            CustodyClass.NightlyFloor90d);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                policyKey: null,
                CustodyProtection.NotEnforced,
                DateTimeOffset.Parse(ObservedAtText, System.Globalization.CultureInfo.InvariantCulture),
                protectedUntil: null));
    }

    private static SourceArtifactRef ArtifactRef(char resourceFill, char digestFill) => new(
        $"urn:uuid:{new string(resourceFill, 8)}-{new string(resourceFill, 4)}-4{new string(resourceFill, 3)}-8{new string(resourceFill, 3)}-{new string(resourceFill, 12)}",
        new string(digestFill, 64));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
