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

    /// <summary>
    /// One identity stating one thing, carried by different bytes, converges. It used to refuse.
    /// </summary>
    /// <remarks>
    /// THIS TEST'S ASSERTION WAS INVERTED, AND THE INVERSION IS THE FIX. While the canonical digest
    /// was the retained page body's own content address, the same publisher statement re-fetched
    /// under a different page limit landed in a different body and read as a conflict - two
    /// equivalent executions disagreeing about a work neither of them had observed differently.
    /// Semantic equality is now over what the publisher said, so transport structure cannot
    /// manufacture a conflict.
    /// </remarks>
    [TestMethod]
    public void OneIdentityCarriedByDifferentBytesConvergesRatherThanConflicting()
    {
        var set = new LanguageScopedExpressionSet();
        var held = Expression("rect/1", "en", 'a');
        Assert.IsTrue(set.TryAppend(held, out _));

        Assert.IsTrue(set.TryAppend(Expression("rect/1", "en", 'b'), out var refusal));
        Assert.AreEqual(LanguageScopedExpressionAppendRefusal.None, refusal);
        Assert.HasCount(1, set.Expressions);
        Assert.AreSame(
            held,
            set.Expressions[0],
            "an append-only store never rewrites the provenance it already admitted.");
    }

    /// <summary>The same identity saying something different refuses by name.</summary>
    [TestMethod]
    public void RepresentingOneIdentitySayingSomethingDifferentRefusesByName()
    {
        var set = new LanguageScopedExpressionSet();
        var held = Expression("rect/1", "en", 'a');
        Assert.IsTrue(set.TryAppend(held, out _));

        Assert.IsFalse(set.TryAppend(Expression("rect/1", "de", 'a'), out var refusal));
        Assert.AreEqual(LanguageScopedExpressionAppendRefusal.ConflictingCanonicalContent, refusal);
        Assert.HasCount(1, set.Expressions);
        Assert.AreSame(held, set.Expressions[0]);
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
                typeof(LanguageScopedExpressionLineage),
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
                Lineage('1', withDate: false)));
    }

    /// <summary>Absent evidence is refused rather than defaulted.</summary>
    [TestMethod]
    public void AbsentEvidenceIsRejected()
    {
        var identity = new LanguageScopedExpressionIdentity(Work, "rect/1");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => LanguageScopedExpression.FromRetainedSource(
                null!, "fr", null, SourceObject(), Lineage('1', withDate: false)));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LanguageScopedExpression.FromRetainedSource(
                identity, "fr", null, null!, Lineage('1', withDate: false)));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LanguageScopedExpression.FromRetainedSource(
                identity, "fr", null, SourceObject(), null!));
    }

    /// <summary>The refusal's wire token is exactly what a reader will see.</summary>
    [TestMethod]
    public void TheConflictRefusalHasTheExactWireToken() =>
        Assert.AreEqual(
            "\"conflicting_canonical_content\"",
            ContractJson.Serialize(
                LanguageScopedExpressionAppendRefusal.ConflictingCanonicalContent));

    /// <summary>The set compares on the retained bytes, not on anything a caller states.</summary>
    /// <remarks>
    /// Conflict must follow what the publisher said, not which bytes carried it. Two presentations
    /// differing only in retained bytes agree; one differing in an observed date does not, and a
    /// digest that ignored the date would let a dated and an undated observation of one expression
    /// silently overwrite each other's meaning.
    /// </remarks>
    [TestMethod]
    public void ConflictIsDecidedByAdmittedContentRatherThanByCarryingBytes()
    {
        var set = new LanguageScopedExpressionSet();
        var first = Expression("rect/1", "fr", 'c');
        var sameContentOtherBytes = Expression("rect/1", "fr", 'e');

        Assert.AreEqual(
            first.CanonicalContentSha256,
            sameContentOtherBytes.CanonicalContentSha256,
            "different carrying bytes are not a different statement.");
        Assert.AreNotEqual(
            first.Lineage.Entries[0].ContentSha256,
            sameContentOtherBytes.Lineage.Entries[0].ContentSha256,
            "and this test is only meaningful while those bytes really do differ.");

        var dated = Expression(
            "rect/1", "fr", 'c',
            new PublisherCorrigendumDate("2026-01-02", "http://www.w3.org/2001/XMLSchema#date"));
        Assert.AreNotEqual(
            first.CanonicalContentSha256,
            dated.CanonicalContentSha256,
            "an observed date is part of what was said.");

        Assert.IsTrue(set.TryAppend(first, out _));
        Assert.IsTrue(set.TryAppend(sameContentOtherBytes, out var convergence));
        Assert.AreEqual(LanguageScopedExpressionAppendRefusal.None, convergence);
        Assert.IsFalse(set.TryAppend(dated, out var refusal));
        Assert.AreEqual(LanguageScopedExpressionAppendRefusal.ConflictingCanonicalContent, refusal);
    }

    /// <summary>
    /// Page regrouping, as a property of the lineage itself: the same contributing artifacts
    /// presented in a different order, or repeated, are the same lineage.
    /// </summary>
    /// <remarks>
    /// This is what keeps transport structure out of a value that gets compared and read. A delivery
    /// that arrived over four pages and the same delivery re-fetched over two must not produce two
    /// different provenance claims about identical bytes.
    /// </remarks>
    [TestMethod]
    public void ALineageIsTheSameWhicheverOrderItsArtifactsArriveIn()
    {
        var first = new LanguageScopedExpressionLineageEntry(
            LanguageScopedExpressionContribution.IdentityAndLanguage, Receipt('1'));
        var second = new LanguageScopedExpressionLineageEntry(
            LanguageScopedExpressionContribution.IdentityAndLanguage, Receipt('2'));

        var forwards = LanguageScopedExpressionLineage.FromContributions([first, second]);
        var backwards = LanguageScopedExpressionLineage.FromContributions([second, first, second]);

        CollectionAssert.AreEqual(
            forwards.Entries.Select(static entry => entry.ContentSha256).ToArray(),
            backwards.Entries.Select(static entry => entry.ContentSha256).ToArray(),
            "order of arrival, and a repeat, are not different provenance.");
        Assert.HasCount(2, backwards.Entries, "a repeated artifact is one artifact.");
    }

    /// <summary>Every contributing artifact is kept. A lineage is complete or it is a guess.</summary>
    [TestMethod]
    public void ALineageKeepsEveryDistinctContributingArtifact()
    {
        var lineage = LanguageScopedExpressionLineage.FromContributions([
            new(LanguageScopedExpressionContribution.IdentityAndLanguage, Receipt('1')),
            new(LanguageScopedExpressionContribution.IdentityAndLanguage, Receipt('2')),
            new(LanguageScopedExpressionContribution.PublisherDate, Receipt('3')),
        ]);

        Assert.HasCount(3, lineage.Entries);
        Assert.IsTrue(lineage.CarriesDateContribution);
    }

    /// <summary>An expression nothing witnessed the identity of is not an observation.</summary>
    [TestMethod]
    public void ALineageWithoutAnIdentityContributionIsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(() =>
            LanguageScopedExpressionLineage.FromContributions([
                new(LanguageScopedExpressionContribution.PublisherDate, Receipt('1')),
            ]));

    /// <summary>
    /// Date-lineage loss, refused at the contract's own door: a date with no bytes behind it, and
    /// date bytes behind an expression claiming no date, are both provenance this cannot state.
    /// </summary>
    [TestMethod]
    public void ADateAndItsWitnessingBytesMustBePresentTogether()
    {
        var date = new PublisherCorrigendumDate("2026-01-02", "http://www.w3.org/2001/XMLSchema#date");
        var identity = new LanguageScopedExpressionIdentity(Work, "rect/1");

        Assert.ThrowsExactly<ArgumentException>(
            () => LanguageScopedExpression.FromRetainedSource(
                identity, "fr", date, SourceObject(), Lineage('1', withDate: false)),
            "a date needs the retained bytes that stated it.");

        Assert.ThrowsExactly<ArgumentException>(
            () => LanguageScopedExpression.FromRetainedSource(
                identity, "fr", null, SourceObject(), Lineage('1', withDate: true)),
            "and retained date bytes need a date to witness.");
    }

    /// <summary>Every null argument on the lineage and set doors is a caller contract violation.</summary>
    /// <remarks>
    /// A mechanical sweep found all three of these guards undefended: dropping any of them survived
    /// every test, because nothing here ever passed null to them.
    /// </remarks>
    [TestMethod]
    public void EveryNullArgumentOnTheLineageAndSetDoorsIsRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LanguageScopedExpressionLineage.FromContributions(null!),
            "the lineage door takes no null sequence.");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => LanguageScopedExpressionLineage.FromContributions(
                [new(LanguageScopedExpressionContribution.IdentityAndLanguage, Receipt('1')), null!]),
            "nor a null entry inside one.");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LanguageScopedExpressionSet().TryAppend(null!, out _),
            "and the set appends no null expression.");
    }

    /// <summary>A caller cannot mutate the exposed collection, by any cast.</summary>
    /// <remarks>
    /// The reviewer's finding on the first head: exposing the backing <c>List</c> behind an
    /// <c>IReadOnlyList</c> changes only the compile-time view, so a cast back to <c>List</c> or
    /// <c>ICollection</c> could add, remove or clear. That would break the append-only guarantee
    /// and desynchronize the list from the identity index, leaving a removed expression's identity
    /// still refusing a later, different presentation. Asserting the declared return type proves
    /// nothing here; only attempting the mutation does.
    /// </remarks>
    [TestMethod]
    public void TheExposedCollectionCannotBeMutatedByACaller()
    {
        var set = new LanguageScopedExpressionSet();
        Assert.IsTrue(set.TryAppend(Expression("rect/1", "fr", '1'), out _));
        var exposed = set.Expressions;
        var intruder = Expression("rect/9", "fr", '9');

        Assert.IsFalse(
            exposed is List<LanguageScopedExpression>,
            "the backing list must not be handed out behind a read-only interface.");

        var asCollection = exposed as ICollection<LanguageScopedExpression>;
        Assert.IsNotNull(asCollection);
        Assert.IsTrue(asCollection.IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => asCollection.Add(intruder));
        Assert.ThrowsExactly<NotSupportedException>(() => asCollection.Remove(set.Expressions[0]));
        Assert.ThrowsExactly<NotSupportedException>(asCollection.Clear);

        Assert.HasCount(1, set.Expressions, "and none of that reached the store.");
    }

    /// <summary>The exposed collection is a live view, not a snapshot taken at first access.</summary>
    /// <remarks>
    /// Pinned because the obvious over-correction for the finding above - returning a defensive copy
    /// - would silently make a held reference stale, and a reader of an append-only store reasonably
    /// expects later appends to appear.
    /// </remarks>
    [TestMethod]
    public void TheExposedCollectionIsALiveViewOfLaterAppends()
    {
        var set = new LanguageScopedExpressionSet();
        var exposed = set.Expressions;
        Assert.IsEmpty(exposed);

        Assert.IsTrue(set.TryAppend(Expression("rect/1", "de", '1'), out _));

        Assert.HasCount(1, exposed, "a reference taken before the append must see it.");
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
            Lineage(bytesFill, publisherCorrigendumDate is not null));

    private static LanguageScopedExpressionLineage Lineage(char bytesFill, bool withDate)
    {
        var entries = new List<LanguageScopedExpressionLineageEntry>
        {
            new(LanguageScopedExpressionContribution.IdentityAndLanguage, Receipt(bytesFill)),
        };
        if (withDate)
        {
            entries.Add(new(LanguageScopedExpressionContribution.PublisherDate, Receipt('d')));
        }

        return LanguageScopedExpressionLineage.FromContributions(entries);
    }

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
