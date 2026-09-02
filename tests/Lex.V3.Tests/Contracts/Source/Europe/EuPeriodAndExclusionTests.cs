using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Row R2 of #331: every reviewed period and exclusion carries one explicit typed disposition.
/// </summary>
[TestClass]
public sealed class EuPeriodAndExclusionTests
{
    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:00000000-0000-4000-8000-0000000000e1", new string('e', 64));

    private static EuSelectionDisposition Row(EuExcludedSelector selector) =>
        new(selector,
            EuSelectionDisposition.PolicyFor(selector),
            "reviewed_scope_inventory",
            "d1-eu-scope-candidate-2",
            Evidence);

    /// <summary>
    /// The whole reviewed table, pinned against literals rather than against the code under test.
    /// </summary>
    /// <remarks>
    /// This exists because a mutation survived without it. The test below asserts that a row
    /// carries <c>PolicyFor(selector)</c>, which is trivially true whatever PolicyFor returns, so
    /// changing a POINT row to NEVER-INGEST changed nothing any test could see. The rows that did
    /// fail were the three I had happened to pin against literal values. A fixture that derives
    /// its expectation from the module under test agrees with that module by construction.
    /// </remarks>
    [TestMethod]
    public void TheReviewedPolicyForEverySelectorIsPinned()
    {
        var expected = new (EuExcludedSelector Selector, EuSelectionPolicy Policy)[]
        {
            (EuExcludedSelector.NonLuxNationalImplementing, EuSelectionPolicy.Point),
            (EuExcludedSelector.Sector3OutsideReviewedClosure, EuSelectionPolicy.Point),
            (EuExcludedSelector.UnreviewedSectorOrTreatyVersion, EuSelectionPolicy.Point),
            (EuExcludedSelector.DossierContainedSector5Body, EuSelectionPolicy.Point),
            (EuExcludedSelector.WholesaleSector2, EuSelectionPolicy.NeverIngest),
            (EuExcludedSelector.WholesaleSector5, EuSelectionPolicy.NeverIngest),
            (EuExcludedSelector.EuJudgmentText, EuSelectionPolicy.NeverIngestBody),
            (EuExcludedSelector.CellarDoNotIndex, EuSelectionPolicy.NeverIngest),
            (EuExcludedSelector.SyntheticConsolidation, EuSelectionPolicy.NeverIngest),
            (EuExcludedSelector.Akn4EuLegalBody, EuSelectionPolicy.NeverIngest),
            (EuExcludedSelector.EurLexPortalFallback, EuSelectionPolicy.NeverIngest),
            (EuExcludedSelector.InboundTreatyBasedOnExpansion, EuSelectionPolicy.NeverExpand),
        };

        CollectionAssert.AreEqual(
            Enum.GetValues<EuExcludedSelector>(),
            expected.Select(static row => row.Selector).ToArray(),
            "the pinned table and the closed vocabulary no longer list the same selectors");

        foreach (var (selector, policy) in expected)
        {
            Assert.AreEqual(
                policy,
                EuSelectionDisposition.PolicyFor(selector),
                $"{selector} moved away from its reviewed policy");
        }
    }

    [TestMethod]
    public void EveryReviewedExclusionHasExactlyOneTypedDisposition()
    {
        var selectors = Enum.GetValues<EuExcludedSelector>();

        Assert.AreEqual(12, selectors.Length, "the closed exclusion inventory changed size");

        foreach (var selector in selectors)
        {
            var row = Row(selector);
            Assert.AreEqual(selector, row.Selector);
            Assert.AreEqual(EuSelectionDisposition.PolicyFor(selector), row.Policy);
        }
    }

    /// <summary>
    /// The policy is read from the reviewed inventory, never chosen by whoever writes the row.
    /// </summary>
    [TestMethod]
    public void ACallerCannotChooseADifferentPolicyForASelector()
    {
        foreach (var selector in Enum.GetValues<EuExcludedSelector>())
        {
            var accepted = EuSelectionDisposition.PolicyFor(selector);
            foreach (var wrong in Enum.GetValues<EuSelectionPolicy>().Where(p => p != accepted))
            {
                Assert.ThrowsExactly<ArgumentException>(
                    () => new EuSelectionDisposition(
                        selector, wrong, "reason", "rule", Evidence),
                    $"{selector} was accepted as {wrong} rather than {accepted}");
            }
        }
    }

    /// <summary>
    /// The judgment row is body-scoped, and the three compound-row members are separate identities.
    /// </summary>
    [TestMethod]
    public void PartialAndCompoundRowsKeepTheirDistinctions()
    {
        // A whole-object answer here would record that we hold nothing about a judgment, when we
        // hold its metadata and its official locator.
        Assert.AreEqual(
            EuSelectionPolicy.NeverIngestBody,
            EuSelectionDisposition.PolicyFor(EuExcludedSelector.EuJudgmentText));

        // One token for the last table row would lose which of its three a disposition was about.
        foreach (var member in new[]
                 {
                     EuExcludedSelector.SyntheticConsolidation,
                     EuExcludedSelector.Akn4EuLegalBody,
                     EuExcludedSelector.EurLexPortalFallback,
                 })
        {
            Assert.AreEqual(EuSelectionPolicy.NeverIngest, EuSelectionDisposition.PolicyFor(member));
        }

        // The treaty row is about the relation, not the objects. The six treaty identities remain
        // directly selected body candidates and only the inbound walk is refused.
        Assert.AreEqual(
            EuSelectionPolicy.NeverExpand,
            EuSelectionDisposition.PolicyFor(EuExcludedSelector.InboundTreatyBasedOnExpansion));
    }

    /// <summary>
    /// AKN4EU is accounted for here and answered elsewhere, and this fails if that changes.
    /// </summary>
    /// <remarks>
    /// The existing rule is that no manifestation format member is AKN4EU, stated as a deliberate
    /// absence on <see cref="EuManifestationFormat"/>. S8 records the exclusion without minting a
    /// second policy answer, so this asserts the referenced rule rather than restating it: if a
    /// member ever appears, S8 fails here instead of drifting independently.
    /// </remarks>
    [TestMethod]
    public void TheAkn4EuRowIsBoundToTheFormatVocabularyRatherThanRestated()
    {
        Assert.IsFalse(
            Enum.GetNames<EuManifestationFormat>()
                .Any(name => name.Contains("Akn", StringComparison.OrdinalIgnoreCase)),
            "a manifestation format member now names AKN4EU, so the exclusion S8 records and the " +
            "format vocabulary no longer agree");
    }

    /// <summary>
    /// The EUR-Lex portal row is bound to the exact existing channel member.
    /// </summary>
    /// <remarks>
    /// Binding to the member's identity rather than to a fixed disposition, because there is not
    /// one: <see cref="EuChannelDisposition"/> accepts any channel with any admission, unlike
    /// <see cref="EuLanguageBodyDisposition"/>, which pins its accepted inventory in the type.
    /// That gap is reported in the freeze packet rather than closed here, because pinning a
    /// channel's admission is a change to the acquisition profile row and not to this one.
    /// </remarks>
    [TestMethod]
    public void TheEurLexPortalRowIsBoundToTheExactChannelMember()
    {
        Assert.IsTrue(
            Enum.IsDefined(EuChannel.EurLexPortal),
            "the channel S8's portal exclusion refers to no longer exists");

        Assert.AreEqual(
            "eurlex_portal",
            JsonSerializer.Deserialize<string>(ContractJson.Serialize(EuChannel.EurLexPortal)),
            "the channel's wire token moved, so the exclusion and the channel no longer name one " +
            "thing");
    }

    /// <summary>
    /// The do-not-index selector matches one term, and everything else is drift rather than absence.
    /// </summary>
    [TestMethod]
    public void OnlyTheExactDoNotIndexTermMatches()
    {
        Assert.IsTrue(EuDoNotIndexTerm.IsExactTerm("1", EuDoNotIndexTerm.DatatypeIri));

        foreach (var (lexical, datatype, why) in new[]
                 {
                     ("true", EuDoNotIndexTerm.DatatypeIri, "a second valid boolean spelling"),
                     ("1", "http://www.w3.org/2001/XMLSchema#integer", "an untyped numeric one"),
                     ("1", "", "no datatype at all"),
                     ("0", EuDoNotIndexTerm.DatatypeIri, "the negative term"),
                     (" 1", EuDoNotIndexTerm.DatatypeIri, "leading space"),
                 })
        {
            Assert.IsFalse(
                EuDoNotIndexTerm.IsExactTerm(lexical, datatype),
                $"{why} was accepted as the exact do-not-index term");
        }
    }

    [TestMethod]
    public void ThePeriodRuleHasNoFloorAndOneSentinel()
    {
        // The sentinel is pinned through behaviour rather than by comparing the constant to its
        // own literal, which the analyzer refused because the compiler folds it and it can never
        // fail. Changing OpenSentinel makes the next line false, which is the assertion that
        // actually holds the value.
        Assert.IsTrue(EuAcquisitionPeriod.IsOpenEnded("9999-12-31"));
        Assert.IsFalse(EuAcquisitionPeriod.IsOpenEnded("2030-09-15"));

        // No floor, no cutoff, and no retroactive observation. Asserted as the absence of any
        // member that could express one, because the rule is that the type offers no way to say it.
        var names = typeof(EuAcquisitionPeriod).GetMembers().Select(m => m.Name).ToArray();
        foreach (var forbidden in new[] { "Floor", "Earliest", "Cutoff", "Observed", "Retroactive" })
        {
            Assert.IsFalse(
                names.Any(name => name.Contains(forbidden, StringComparison.Ordinal)),
                $"{forbidden} would let this type express an inclusion bound the scope has none of");
        }
    }

    /// <summary>
    /// A partition window is execution mechanics, and cannot be passed where selection is expected.
    /// </summary>
    /// <remarks>
    /// Enforced by type rather than by rule. The way "partition years are never inclusion filters"
    /// gets broken is by handing an execution bound to something that selects, and a separate type
    /// makes that a compile error rather than a policy conversation.
    /// </remarks>
    [TestMethod]
    public void APartitionWindowIsNotAPeriodBound()
    {
        var window = new EuPartitionWindow(2016, 2019);

        Assert.AreEqual(2016, window.FirstYear);
        Assert.AreEqual(2019, window.LastYear);

        // No assertion here that the two types differ. The analyzer refused one, correctly: two
        // distinct typeof values are never equal, so it could not fail and would have been a line
        // that looked like a guarantee while proving nothing. The separation is enforced by the
        // compiler refusing to pass one where the other is expected, which no runtime test can
        // observe and none should pretend to.

        Assert.ThrowsExactly<ArgumentException>(() => new EuPartitionWindow(2019, 2016));
        Assert.ThrowsExactly<ArgumentException>(() => new EuPartitionWindow(0, 2019));
    }

    [TestMethod]
    public void TheWireTokensAreStable()
    {
        CollectionAssert.AreEqual(
            new[] { "point", "never_ingest", "never_ingest_body", "never_expand" },
            Enum.GetValues<EuSelectionPolicy>()
                .Select(p => JsonSerializer.Deserialize<string>(ContractJson.Serialize(p))!)
                .ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "non_lux_national_implementing", "sector3_outside_reviewed_closure",
                "unreviewed_sector_or_treaty_version", "dossier_contained_sector5_body",
                "wholesale_sector2", "wholesale_sector5", "eu_judgment_text",
                "cellar_do_not_index", "synthetic_consolidation", "akn4eu_legal_body",
                "eurlex_portal_fallback", "inbound_treaty_based_on_expansion",
            },
            Enum.GetValues<EuExcludedSelector>()
                .Select(s => JsonSerializer.Deserialize<string>(ContractJson.Serialize(s))!)
                .ToArray());
    }

    [TestMethod]
    public void UndefinedVocabularyAndNullEvidenceAreRefused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => EuSelectionDisposition.PolicyFor((EuExcludedSelector)999));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new EuSelectionDisposition(
                (EuExcludedSelector)999, EuSelectionPolicy.Point, "r", "u", Evidence));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuSelectionDisposition(
                EuExcludedSelector.WholesaleSector2, EuSelectionPolicy.NeverIngest, "r", "u", null!));
    }
}
