using System.Reflection;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Tests.Contracts.Source.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Derivation;

/// <summary>
/// The Stage 3 half of "render the tripwire": from the two proof-bound deliveries the expression
/// derivation takes, every corrected work's corrigendum lines, with reach and date stated per line
/// and every edge cited to the retained page that stated it.
/// </summary>
/// <remarks>
/// The measured shape this exists for is the product specification's own example: the GDPR's
/// corrigendum R(01) exists in Estonian, German, Hungarian and Italian only. A reader of the English
/// text has no served body in which that correction can be read, and the tripwire is the fact that
/// tells them so. Every test below drives the fold offline through the same proof-bound deliveries
/// the decoder takes; no publisher traffic.
/// </remarks>
[TestClass]
public sealed class EuCorrigendumTripwireTests
{
    // Canonical cellar roots, picked from the seed map by index: the map sorts its roots itself and
    // refuses to load out of order, so an index names one root regardless of how the seed lines are
    // written.
    private static string Gdpr => EuAppendixASeedMap.PackRoots[0];
    private static string CorrigendumOne => EuAppendixASeedMap.PackRoots[1];
    private static string CorrigendumTwo => EuAppendixASeedMap.PackRoots[2];
    private static string OtherAct => EuAppendixASeedMap.PackRoots[3];
    private static string BaseAct => EuAppendixASeedMap.PackRoots[5];

    private const string LanguageBase = "http://publications.europa.eu/resource/authority/language/";
    private const string English = LanguageBase + "ENG";
    private const string French = LanguageBase + "FRA";
    private const string Estonian = LanguageBase + "EST";
    private const string German = LanguageBase + "DEU";
    private const string Hungarian = LanguageBase + "HUN";
    private const string Italian = LanguageBase + "ITA";
    private const string Norwegian = LanguageBase + "NOR";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string GdprCorrigendumDate = "2018-05-23";

    private static readonly string BelongsToWorkIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork);
    private static readonly string UsesLanguageIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage);
    private static readonly string WorkDateIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkDateDocument);
    private static readonly string CorrectsIri =
        EuObjectFactsDiscoveryPlan.RelationIri(EuRelationFamily.Corrects);

    private static readonly string[] XProjection =
        ["parent", "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag", "cursor"];
    private static readonly string[] XKey = ["parent", "object", "predicate", "value"];
    private static readonly string[] PProjection =
        ["object", "predicate", "value", "value_kind", "cursor"];
    private static readonly string[] PKey = ["object", "predicate", "value"];
    private static readonly string[] PKeyWithoutValue = ["object", "predicate"];
    private static readonly string[] PKeyWithCursor = ["object", "predicate", "cursor"];

    // ---- The measured shape. ----

    /// <summary>
    /// The product specification's example: one corrigendum, four languages, none of them served.
    /// Four lines, each outside the served languages, each dated exactly as the publisher wrote it,
    /// each citing the page that stated the edge.
    /// </summary>
    [TestMethod]
    public void TheGdprCorrigendumShapeYieldsFourLinesAllOutsideServedLanguages()
    {
        var objectFacts = BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), Date(CorrigendumOne, GdprCorrigendumDate)]);
        var set = Fold(
            Bound(
            [
                .. Expression(CorrigendumOne, "R01.EST", Estonian),
                .. Expression(CorrigendumOne, "R01.DEU", German),
                .. Expression(CorrigendumOne, "R01.HUN", Hungarian),
                .. Expression(CorrigendumOne, "R01.ITA", Italian),
            ]),
            objectFacts);

        Assert.HasCount(1, set.Tripwires);
        var tripwire = set.TripwireFor(Gdpr);
        Assert.IsNotNull(tripwire);
        Assert.HasCount(4, tripwire.Lines);
        Assert.AreEqual(0, tripwire.WithinServedCount, "no served body carries this corrigendum.");
        Assert.AreEqual(4, tripwire.OutsideServedCount);
        Assert.AreEqual(4, tripwire.DatedCount);
        var pageDigests = objectFacts.PagesInOrder.Select(static page => page.DurableWriteReceipt.Reference.ContentSha256).ToArray();
        foreach (var line in tripwire.Lines)
        {
            Assert.AreEqual(Gdpr, line.CorrectedWorkRoot);
            Assert.AreEqual(CorrigendumOne, line.CorrigendumWorkRoot);
            Assert.AreEqual(EuCorrigendumLanguageReach.OutsideServedBodyLanguages, line.Reach);
            Assert.AreEqual(EuCorrigendumDateState.PublisherDated, line.DateState);
            Assert.AreEqual("corrigendum_dated_outside_served_languages", line.ReasonCode);
            Assert.AreEqual(GdprCorrigendumDate, line.PublisherCorrigendumDate!.RawLexical, "carried, never computed.");
            Assert.AreEqual(XsdDate, line.PublisherCorrigendumDate.DatatypeIri);
            Assert.HasCount(1, line.CorrectsPageContentSha256InOrder);
            Assert.Contains(line.CorrectsPageContentSha256InOrder[0], pageDigests, "the edge cites a retained page of this delivery.");
        }

        CollectionAssert.AreEquivalent(
            new[] { Estonian, German, Hungarian, Italian },
            tripwire.Lines.Select(static line => line.LanguageIri).ToArray(),
            "every language the publisher stated, verbatim.");
        Assert.IsEmpty(tripwire.CorrigendaWithoutDerivedExpressions);
        Assert.IsEmpty(set.UnresolvedGaps);
    }

    /// <summary>
    /// Reach is exactly the reviewed body policy: English and French are within it, everything else
    /// is outside it, and the language IRI itself travels untouched - a case-folded IRI is a
    /// different string and is outside.
    /// </summary>
    [TestMethod]
    public void EnglishAndFrenchAreWithinServedAndEverythingElseIsOutside()
    {
        var tripwire = Fold(
            Bound(
            [
                .. Expression(CorrigendumOne, "R01.ENG", English),
                .. Expression(CorrigendumOne, "R01.FRA", French),
                .. Expression(CorrigendumOne, "R01.DEU", German),
                .. Expression(CorrigendumOne, "R01.NOR", Norwegian),
            ]),
            BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)])).TripwireFor(Gdpr)!;

        Assert.AreEqual(2, tripwire.WithinServedCount);
        Assert.AreEqual(2, tripwire.OutsideServedCount);
        var byLanguage = tripwire.Lines.ToDictionary(static line => line.LanguageIri, StringComparer.Ordinal);
        Assert.AreEqual(EuCorrigendumLanguageReach.WithinServedBodyLanguages, byLanguage[English].Reach);
        Assert.AreEqual(EuCorrigendumLanguageReach.WithinServedBodyLanguages, byLanguage[French].Reach);
        Assert.AreEqual(EuCorrigendumLanguageReach.OutsideServedBodyLanguages, byLanguage[German].Reach);
        Assert.AreEqual(
            EuCorrigendumLanguageReach.OutsideServedBodyLanguages, byLanguage[Norwegian].Reach,
            "a language the scope enum has no member for survives as itself and is outside.");
        Assert.AreEqual(
            EuCorrigendumLanguageReach.OutsideServedBodyLanguages,
            EuCorrigendumTripwireSet.ReachOf(LanguageBase + "eng"),
            "ordinal, not case-folded: the publisher's IRI is the key.");
    }

    /// <summary>
    /// Dated and consulted-and-none-stated are two states with two addresses, and the second is
    /// exact: the delivery that stated the edge is the delivery that stated no date. The undated
    /// state invents no date.
    /// </summary>
    [TestMethod]
    public void TheTwoDateStatesAreDistinctAndDistinctlyAddressed()
    {
        var expressionFacts = Bound(Expression(CorrigendumOne, "R01.DEU", German));

        var dated = Fold(expressionFacts, BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), Date(CorrigendumOne, GdprCorrigendumDate)]))
            .TripwireFor(Gdpr)!;
        var undated = Fold(expressionFacts, BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), NoDate(CorrigendumOne)], PKeyWithoutValue))
            .TripwireFor(Gdpr)!;

        Assert.AreEqual(EuCorrigendumDateState.PublisherDated, dated.Lines.Single().DateState);
        Assert.AreEqual(EuCorrigendumDateState.NotStatedByConsultedDelivery, undated.Lines.Single().DateState);
        Assert.IsNull(undated.Lines.Single().PublisherCorrigendumDate, "no default, no sentinel.");
        Assert.AreEqual(1, dated.DatedCount);
        Assert.AreEqual(0, undated.DatedCount);
        Assert.AreNotEqual(dated.TripwireSha256, undated.TripwireSha256, "two different facts, two different addresses.");
    }

    /// <summary>Each reach and date state pair renders its own closed token - pinned pair by pair.</summary>
    [TestMethod]
    public void EachReachAndDateStatePairRendersItsOwnReasonCode()
    {
        var expressionFacts = Bound([.. Expression(CorrigendumOne, "R01.ENG", English), .. Expression(CorrigendumOne, "R01.DEU", German)]);
        var dated = Fold(expressionFacts, BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), Date(CorrigendumOne, GdprCorrigendumDate)]))
            .TripwireFor(Gdpr)!.Lines.ToDictionary(static line => line.LanguageIri, StringComparer.Ordinal);
        var undated = Fold(expressionFacts, BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), NoDate(CorrigendumOne)], PKeyWithoutValue))
            .TripwireFor(Gdpr)!.Lines.ToDictionary(static line => line.LanguageIri, StringComparer.Ordinal);

        Assert.AreEqual("corrigendum_dated_within_served_languages", dated[English].ReasonCode);
        Assert.AreEqual("corrigendum_dated_outside_served_languages", dated[German].ReasonCode);
        Assert.AreEqual("corrigendum_undated_within_served_languages", undated[English].ReasonCode);
        Assert.AreEqual("corrigendum_undated_outside_served_languages", undated[German].ReasonCode);
    }

    // ---- S3-A04: byte-stable content, per-run lineage. ----

    /// <summary>
    /// Two independent executions - different acquisition runs on both families - over the same
    /// publisher statements produce one canonical address and two lineages.
    /// </summary>
    [TestMethod]
    public void TwoIndependentExecutionsAgreeOnContentAndDifferOnLineage()
    {
        var rows = new List<string>();
        rows.AddRange(Expression(CorrigendumOne, "R01.EST", Estonian));
        rows.AddRange(Expression(CorrigendumOne, "R01.DEU", German));
        var objectRows = new[] { Corrects(CorrigendumOne, Gdpr), Date(CorrigendumOne, GdprCorrigendumDate) };

        // Each family runs in its own acquisition session, as in production, so the two families'
        // run identities differ within one fold as well as between the two folds.
        var first = Fold(Bound(rows, runIdentitySeed: 930), BoundObjectFacts(objectRows, runIdentitySeed: 931));
        var second = Fold(Bound(rows, runIdentitySeed: 932), BoundObjectFacts(objectRows, runIdentitySeed: 933));

        // The premise: the two really are different executions.
        Assert.AreNotEqual(
            first.Derivation.EpisodeSha256, second.Derivation.EpisodeSha256,
            "the fixture must mint two acquisition runs.");

        Assert.AreEqual(first.CanonicalSha256, second.CanonicalSha256, "what the publisher said is one fact.");
        CollectionAssert.AreEqual(first.CanonicalBytes.ToArray(), second.CanonicalBytes.ToArray());
        Assert.AreEqual(first.Tripwires.Single().TripwireSha256, second.Tripwires.Single().TripwireSha256);
        Assert.AreNotEqual(first.LineageSha256, second.LineageSha256, "each time it was observed is another.");
    }

    /// <summary>Rows in any order, on either family, fold to one canonical form.</summary>
    [TestMethod]
    public void PermutedInputOrdersFoldToOneCanonicalForm()
    {
        var forward = new List<string>();
        forward.AddRange(Expression(CorrigendumOne, "R01.EST", Estonian));
        forward.AddRange(Expression(CorrigendumOne, "R01.DEU", German));
        forward.AddRange(Expression(CorrigendumTwo, "R02.ITA", Italian));
        var backward = new List<string>();
        backward.AddRange(Expression(CorrigendumTwo, "R02.ITA", Italian));
        backward.AddRange(Expression(CorrigendumOne, "R01.DEU", German));
        backward.AddRange(Expression(CorrigendumOne, "R01.EST", Estonian));
        var objectForward = new[] { Corrects(CorrigendumOne, Gdpr), Corrects(CorrigendumOne, OtherAct), Corrects(CorrigendumTwo, Gdpr) };
        var objectBackward = objectForward.Reverse().ToArray();

        var ascending = Fold(Bound(forward), BoundObjectFacts(objectForward));
        var descending = Fold(Bound(backward), BoundObjectFacts(objectBackward));

        Assert.AreNotEqual(
            ascending.DerivationSha256, descending.DerivationSha256,
            "the premise: the two derivations state their expressions in different orders.");
        Assert.AreEqual(ascending.CanonicalSha256, descending.CanonicalSha256);
        CollectionAssert.AreEqual(ascending.CanonicalBytes.ToArray(), descending.CanonicalBytes.ToArray());
        Assert.HasCount(2, ascending.Tripwires);
        Assert.AreEqual(3, ascending.TripwireFor(Gdpr)!.Lines.Count);
        Assert.AreEqual(2, ascending.TripwireFor(OtherAct)!.Lines.Count);
    }

    /// <summary>Ordinal, not cultural: "B" sorts before "a".</summary>
    [TestMethod]
    public void TheEmittedOrderIsOrdinalNotCultural()
    {
        var rows = new List<string>();
        rows.AddRange(Expression(CorrigendumOne, "a", German));
        rows.AddRange(Expression(CorrigendumOne, "B", German));

        var tripwire = Fold(Bound(rows), BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)])).TripwireFor(Gdpr)!;

        CollectionAssert.AreEqual(
            new[] { CorrigendumOne + ".B", CorrigendumOne + ".a" },
            tripwire.Lines.Select(static line => line.PublisherExpressionId).ToArray());
        var roots = Fold(Bound(rows), BoundObjectFacts([Corrects(CorrigendumOne, OtherAct), Corrects(CorrigendumOne, Gdpr)]))
            .Tripwires.Select(static tripwire => tripwire.CorrectedWorkRoot).ToArray();
        CollectionAssert.AreEqual(roots.OrderBy(static root => root, StringComparer.Ordinal).ToArray(), roots);
    }

    /// <summary>
    /// The corrigendum root is the dominant sort key. The lens noted every fixture's expression
    /// identity began with its own root, so identity alone would have ordered identically; here the
    /// expression identities sort the other way round from the roots.
    /// </summary>
    [TestMethod]
    public void TheCorrigendumRootIsTheDominantSortKey()
    {
        var rows = new List<string>();
        rows.AddRange(ExpressionNamed(CorrigendumOne, "http://z.example.org/expression", German));
        rows.AddRange(ExpressionNamed(CorrigendumTwo, "http://a.example.org/expression", German));
        var ordinalRoots = new[] { CorrigendumOne, CorrigendumTwo }.OrderBy(static root => root, StringComparer.Ordinal).ToArray();

        var lines = Fold(Bound(rows), BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), Corrects(CorrigendumTwo, Gdpr)]))
            .TripwireFor(Gdpr)!.Lines;

        Assert.AreNotEqual(
            string.CompareOrdinal(CorrigendumOne, CorrigendumTwo) < 0,
            string.CompareOrdinal("http://z.example.org/expression", "http://a.example.org/expression") < 0,
            "the premise: root order and identity order disagree.");
        CollectionAssert.AreEqual(ordinalRoots, lines.Select(static line => line.CorrigendumWorkRoot).ToArray());
        CollectionAssert.AreEqual(
            new[] { "http://z.example.org/expression", "http://a.example.org/expression" },
            lines.Select(static line => line.PublisherExpressionId).ToArray(),
            "identity alone would have put these the other way round.");
    }

    // ---- S3-A03: explicit fidelity. ----

    /// <summary>A corrigendum correcting two works is on both of them.</summary>
    [TestMethod]
    public void ACorrigendumCorrectingTwoWorksAppearsOnBoth()
    {
        var set = Fold(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)),
            BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), Corrects(CorrigendumOne, OtherAct)]));

        Assert.HasCount(2, set.Tripwires);
        Assert.AreEqual(CorrigendumOne, set.TripwireFor(Gdpr)!.Lines.Single().CorrigendumWorkRoot);
        Assert.AreEqual(CorrigendumOne, set.TripwireFor(OtherAct)!.Lines.Single().CorrigendumWorkRoot);
        Assert.IsNull(set.TripwireFor(BaseAct), "nothing corrects this one.");
    }

    /// <summary>
    /// A corrigendum the publisher says corrects a work, whose expressions this derivation does not
    /// hold, is listed on that work with the pages that stated the edge - not dropped, and not
    /// stated without lineage.
    /// </summary>
    [TestMethod]
    public void ACorrigendumWithoutDerivedExpressionsIsListedWithItsLineage()
    {
        var expressionFacts = Bound(Expression(CorrigendumOne, "R01.DEU", German));
        var both = BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), Corrects(CorrigendumTwo, Gdpr)]);

        var with = Fold(expressionFacts, both);
        var without = Fold(expressionFacts, BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)]));
        var tripwire = with.TripwireFor(Gdpr)!;

        Assert.HasCount(1, tripwire.Lines);
        var listed = tripwire.CorrigendaWithoutDerivedExpressions.Single();
        Assert.AreEqual(CorrigendumTwo, listed.CorrigendumWorkRoot);
        Assert.HasCount(1, listed.CorrectsPageContentSha256InOrder);
        var pageDigests = both.PagesInOrder.Select(static page => page.DurableWriteReceipt.Reference.ContentSha256).ToArray();
        Assert.Contains(listed.CorrectsPageContentSha256InOrder[0], pageDigests, "the listing cites the page that stated the edge.");
        var lineage = Encoding.UTF8.GetString(with.LineageBytes.Span);
        StringAssert.Contains(lineage, listed.CorrectsPageContentSha256InOrder[0], "and the set's lineage carries it.");
        StringAssert.Contains(lineage, CorrigendumTwo);
        Assert.IsEmpty(without.TripwireFor(Gdpr)!.CorrigendaWithoutDerivedExpressions);
        Assert.AreNotEqual(tripwire.TripwireSha256, without.TripwireFor(Gdpr)!.TripwireSha256, "a listed unknown is content.");
        StringAssert.Contains(tripwire.Describe(), "corrigenda_without_derived_expressions=1");
    }

    /// <summary>
    /// A work the derivation holds expressions of, for which the consulted delivery states neither
    /// an edge nor the "corrects nothing" marker, is an unresolved gap; a work with the marker is a
    /// base act outside the subject set, neither a gap nor a tripwire.
    /// </summary>
    [TestMethod]
    public void AWorkWhoseCorrectsStatementIsMissingIsAnUnresolvedGapNotAnAbsenceClaim()
    {
        var set = Fold(
            Bound(
            [
                .. Expression(CorrigendumOne, "R01.DEU", German),
                .. Expression(BaseAct, "ENG", English),
                .. Expression(OtherAct, "ENG", English),
            ]),
            BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), NoCorrection(BaseAct)], PKeyWithoutValue));

        Assert.HasCount(1, set.Tripwires);
        Assert.IsNotNull(set.TripwireFor(Gdpr));
        var gap = set.UnresolvedGaps.Single();
        Assert.AreEqual(OtherAct, gap.WorkRoot, "nothing was stated about this work's corrections.");
        Assert.AreEqual(EuCorrigendumTripwireGapReason.CorrectsNotStatedByConsultedDelivery, gap.Reason);
        Assert.IsNull(set.TripwireFor(BaseAct), "the marker is a complete answer: a base act, not a corrigendum.");
        Assert.AreEqual(
            $"corrected_works=1 lines=1 (within_served=0 outside_served=1 dated=0) " +
            "corrigenda_without_derived_expressions=0 unresolved_gaps=1",
            set.Describe());
        var canonical = Encoding.UTF8.GetString(set.CanonicalBytes.Span);
        StringAssert.Contains(canonical, OtherAct, "the gap is content: a display must know it does not know.");
    }

    /// <summary>Expressions of a base act are outside the subject set: no line, no gap, nothing.</summary>
    [TestMethod]
    public void ExpressionsOfABaseActAreOutsideTheSubjectSet()
    {
        var set = Fold(
            Bound([.. Expression(BaseAct, "ENG", English), .. Expression(BaseAct, "FRA", French)]),
            BoundObjectFacts([NoCorrection(BaseAct)], PKeyWithoutValue));

        Assert.IsEmpty(set.Tripwires);
        Assert.IsEmpty(set.UnresolvedGaps);
        Assert.AreEqual(
            "corrected_works=0 lines=0 (within_served=0 outside_served=0 dated=0) " +
            "corrigenda_without_derived_expressions=0 unresolved_gaps=0",
            set.Describe());
    }

    /// <summary>A row stating that a work corrects itself is carried exactly as stated.</summary>
    [TestMethod]
    public void ASelfCorrectingRowIsRecordedAsStated()
    {
        var line = Fold(Bound(Expression(CorrigendumOne, "R01.DEU", German)), BoundObjectFacts([Corrects(CorrigendumOne, CorrigendumOne)]))
            .TripwireFor(CorrigendumOne)!.Lines.Single();

        Assert.AreEqual(CorrigendumOne, line.CorrectedWorkRoot);
        Assert.AreEqual(CorrigendumOne, line.CorrigendumWorkRoot);
    }

    /// <summary>
    /// An edge stated on two retained pages cites both pages. Every page that stated it, not a
    /// representative one - the decoder's own rule for a date stated twice.
    /// </summary>
    [TestMethod]
    public void AnEdgeStatedOnTwoPagesCitesBothPages()
    {
        var objectFacts = BoundObjectFacts(
            [Corrects(CorrigendumOne, Gdpr), Date(CorrigendumOne, GdprCorrigendumDate), Corrects(CorrigendumOne, Gdpr)],
            PKeyWithCursor, rowLimitA: 2, rowLimitB: 3);
        Assert.HasCount(2, objectFacts.PagesInOrder, "the premise: a full page and a short terminal page, the edge on each.");

        var line = Fold(Bound(Expression(CorrigendumOne, "R01.DEU", German)), objectFacts).TripwireFor(Gdpr)!.Lines.Single();

        CollectionAssert.AreEquivalent(
            objectFacts.PagesInOrder.Select(static page => page.DurableWriteReceipt.Reference.ContentSha256).ToArray(),
            line.CorrectsPageContentSha256InOrder.ToArray());
        CollectionAssert.AreEqual(
            line.CorrectsPageContentSha256InOrder.OrderBy(static digest => digest, StringComparer.Ordinal).ToArray(),
            line.CorrectsPageContentSha256InOrder.ToArray(),
            "ordinal.");
    }

    /// <summary>The lineage binds the canonical digest, the derivation, its episode and every page; the canonical bytes hold none of the pages.</summary>
    [TestMethod]
    public void TheLineageBindsTheDerivationTheEpisodeAndEveryPage()
    {
        var set = Fold(Bound(Expression(CorrigendumOne, "R01.DEU", German)), BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)]));
        var lineage = Encoding.UTF8.GetString(set.LineageBytes.Span);
        var canonical = Encoding.UTF8.GetString(set.CanonicalBytes.Span);
        var line = set.Tripwires.Single().Lines.Single();

        StringAssert.Contains(lineage, set.CanonicalSha256, "the lineage is bound to what it is the lineage of.");
        StringAssert.Contains(lineage, set.DerivationSha256);
        StringAssert.Contains(lineage, set.Derivation.EpisodeSha256, "and to the run that observed it.");
        StringAssert.Contains(lineage, EuCorrigendumTripwireSet.LineageRecordSchema);
        foreach (var digest in line.LineageContentSha256InOrder.Concat(line.CorrectsPageContentSha256InOrder))
        {
            StringAssert.Contains(lineage, digest);
            Assert.IsFalse(canonical.Contains(digest, StringComparison.Ordinal), "a page digest is lineage, not content.");
        }

        StringAssert.Contains(canonical, line.ExpressionContentSha256, "the expression's own content digest is content.");
        StringAssert.Contains(canonical, EuCorrigendumTripwireSet.Schema);
    }

    // ---- The pairing shape review named, three ways. ----

    /// <summary>
    /// Two structurally valid object-facts deliveries under one interpretation profile with
    /// different Corrects rows: each fold's lines cite exactly their own delivery's pages.
    /// </summary>
    [TestMethod]
    public void TwoDeliveriesUnderOneProfileWithDifferentCorrectsRowsCiteOnlyTheirOwnPages()
    {
        var expressionFacts = Bound([.. Expression(CorrigendumOne, "R01.DEU", German), .. Expression(CorrigendumTwo, "R02.ITA", Italian)]);
        var a = BoundObjectFacts([Corrects(CorrigendumOne, Gdpr), NoCorrection(CorrigendumTwo)], PKeyWithoutValue, runIdentitySeed: 930);
        var b = BoundObjectFacts([Corrects(CorrigendumTwo, Gdpr), NoCorrection(CorrigendumOne)], PKeyWithoutValue, runIdentitySeed: 931);
        Assert.AreEqual(a.ProfileRef.Sha256, b.ProfileRef.Sha256, "the premise: one interpretation profile.");
        var pagesOfA = a.PagesInOrder.Select(static page => page.DurableWriteReceipt.Reference.ContentSha256).ToArray();
        var pagesOfB = b.PagesInOrder.Select(static page => page.DurableWriteReceipt.Reference.ContentSha256).ToArray();
        Assert.IsEmpty(pagesOfA.Intersect(pagesOfB, StringComparer.Ordinal), "and different bytes.");

        var fromA = Fold(expressionFacts, a).TripwireFor(Gdpr)!.Lines.Single();
        var fromB = Fold(expressionFacts, b).TripwireFor(Gdpr)!.Lines.Single();

        Assert.AreEqual(CorrigendumOne, fromA.CorrigendumWorkRoot);
        Assert.AreEqual(CorrigendumTwo, fromB.CorrigendumWorkRoot);
        Assert.IsTrue(fromA.CorrectsPageContentSha256InOrder.All(digest => pagesOfA.Contains(digest, StringComparer.Ordinal)));
        Assert.IsTrue(fromB.CorrectsPageContentSha256InOrder.All(digest => pagesOfB.Contains(digest, StringComparer.Ordinal)));
    }

    /// <summary>
    /// A delivery assembled from one run's proof and comparison with another run's pages does not
    /// fold: the reopen door throws it out, exactly as the decoder's own substitution test pins.
    /// </summary>
    [TestMethod]
    public void AProofPairedWithAnotherDeliverysPagesDoesNotFold()
    {
        var expressionFacts = Bound(Expression(CorrigendumOne, "R01.DEU", German));
        var sameRowsA = BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)], runIdentitySeed: 930);
        var sameRowsB = BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)], runIdentitySeed: 931);
        var otherRows = BoundObjectFacts([Corrects(CorrigendumTwo, Gdpr)], runIdentitySeed: 932);
        Assert.AreEqual(sameRowsA.Proof.CanonicalKeyDigest, sameRowsB.Proof.CanonicalKeyDigest, "the premise: same rows, other run.");

        Assert.ThrowsExactly<ArgumentException>(() => EuCorrigendumTripwireSet.TryDerive(
            expressionFacts,
            new EuProofBoundDelivery(
                sameRowsA.Proof, sameRowsA.Comparison, sameRowsA.Profile, sameRowsA.ProfileRef,
                sameRowsA.CountHttpEvidenceRef, sameRowsB.PagesInOrder),
            out _, out _, out _));
        Assert.ThrowsExactly<ArgumentException>(() => EuCorrigendumTripwireSet.TryDerive(
            expressionFacts,
            new EuProofBoundDelivery(
                sameRowsA.Proof, sameRowsA.Comparison, sameRowsA.Profile, sameRowsA.ProfileRef,
                sameRowsA.CountHttpEvidenceRef, otherRows.PagesInOrder),
            out _, out _, out _));
    }

    /// <summary>
    /// The pairing shape cannot be written: no public member of any tripwire type accepts a
    /// derivation, a snapshot, a relation observation or rows, and every constructor is non-public.
    /// </summary>
    [TestMethod]
    public void ThePairingShapeCannotBeWritten()
    {
        var forbidden = new[]
        {
            typeof(EuLanguageScopedExpressionDerivation),
            typeof(LanguageScopedExpressionSet),
            typeof(LanguageScopedExpression),
            typeof(EuCellarObjectSnapshot),
            typeof(EuRelationFamilyObservation),
            typeof(EuRelationEdgeObservation),
            typeof(RepeatedEnumerationRow),
            typeof(RepeatedEnumerationResolvedEvidence),
        };
        // The fold's own two internal steps take an expression the derivation minted in the same
        // call; they are reachable only from TryDerive and are the allow-list, by exact name.
        var sanctioned = new HashSet<string>(StringComparer.Ordinal)
        {
            typeof(EuCorrigendumTripwireLine).FullName + "::Create",
            typeof(EuCorrigendumTripwire).FullName + "::Create",
        };
        var types = new[]
        {
            typeof(EuCorrigendumTripwireSet),
            typeof(EuCorrigendumTripwire),
            typeof(EuCorrigendumTripwireLine),
            typeof(EuCorrigendumWithoutDerivedExpressions),
            typeof(EuCorrigendumTripwireUnresolvedGap),
        };
        foreach (var type in types)
        {
            const BindingFlags Everything =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            Assert.IsEmpty(
                type.GetConstructors(BindingFlags.Public | BindingFlags.Instance),
                $"{type.Name} must have no public constructor.");
            Assert.IsEmpty(
                type.GetFields(Everything).Where(static field => !field.IsPrivate),
                $"{type.Name} must expose no field beyond private state: a field is a door too.");
            // Private members are the fold's own plumbing, reachable from nothing outside the type;
            // every other visibility is a door and is scanned, internal included.
            foreach (var constructor in type.GetConstructors(Everything).Where(static constructor => !constructor.IsPrivate))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    Assert.IsFalse(
                        forbidden.Any(candidate => Mentions(Unwrap(parameter.ParameterType), candidate)),
                        $"{type.Name}'s constructor accepts {parameter.ParameterType.Name}.");
                }
            }

            foreach (var method in type.GetMethods(Everything).Where(static method => !method.IsPrivate))
            {
                if (sanctioned.Contains(type.FullName + "::" + method.Name))
                {
                    Assert.IsTrue(method.IsAssembly, $"{type.Name}.{method.Name} is the fold's own step and stays internal.");
                    continue;
                }

                foreach (var parameter in method.GetParameters())
                {
                    Assert.IsFalse(
                        forbidden.Any(candidate => Mentions(Unwrap(parameter.ParameterType), candidate)),
                        $"{type.Name}.{method.Name} accepts {parameter.ParameterType.Name}.");
                }
            }
        }

        var door = typeof(EuCorrigendumTripwireSet).GetMethod(nameof(EuCorrigendumTripwireSet.TryDerive))!;
        CollectionAssert.AreEqual(
            new[] { typeof(EuProofBoundDelivery), typeof(EuProofBoundDelivery) },
            door.GetParameters().Where(static parameter => !parameter.IsOut).Select(static parameter => parameter.ParameterType).ToArray(),
            "the only door takes the two proof-bound deliveries and nothing else.");
    }

    /// <summary>A ref or out parameter is a ByRef type; the lens noted an unwrapped guard would wave it through.</summary>
    private static Type Unwrap(Type parameterType) =>
        parameterType.IsByRef ? parameterType.GetElementType()! : parameterType;

    private static bool Mentions(Type parameterType, Type candidate) =>
        candidate.IsAssignableFrom(parameterType) ||
        (parameterType.IsGenericType && parameterType.GetGenericArguments().Any(argument => Mentions(argument, candidate))) ||
        (parameterType.IsArray && Mentions(parameterType.GetElementType()!, candidate));

    // ---- Refusals. ----

    [TestMethod]
    public void ACorrectsRowWithALiteralTargetRefusesNamingTheWork()
    {
        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)),
            BoundObjectFacts([PRow(CorrigendumOne, CorrectsIri, "not an IRI", XsdString)]),
            out var refusal, out _, out var offendingIri);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.CorrectsRowTermKindMismatch, refusal);
        Assert.AreEqual(CorrigendumOne, offendingIri);
    }

    [TestMethod]
    public void ACorrectsRowWhoseTargetIsNotARootRefusesNamingTheTarget()
    {
        const string NotARoot = "ftp://publications.europa.eu/resource/cellar/not-a-root";
        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)),
            BoundObjectFacts([Corrects(CorrigendumOne, NotARoot)]),
            out var refusal, out _, out var offendingIri);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.CorrectsRowTermKindMismatch, refusal);
        Assert.AreEqual(NotARoot, offendingIri);
    }

    [TestMethod]
    public void ACorrectsRowWhoseSubjectIsNotARootRefusesNamingTheSubject()
    {
        const string NotARoot = "ftp://publications.europa.eu/resource/cellar/not-a-root";
        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)),
            BoundObjectFacts([Corrects(NotARoot, Gdpr)]),
            out var refusal, out _, out var offendingIri);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.CorrectsRowTermKindMismatch, refusal);
        Assert.AreEqual(NotARoot, offendingIri);
    }

    /// <summary>Every contradicted work is named, ordinal, in one refusal.</summary>
    [TestMethod]
    public void AnUnboundMarkerBesideAnEdgeRefusesNamingEveryContradictedWork()
    {
        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)),
            BoundObjectFacts(
                [Corrects(CorrigendumTwo, Gdpr), NoCorrection(CorrigendumOne), Corrects(CorrigendumOne, Gdpr), NoCorrection(CorrigendumTwo)],
                PKeyWithCursor),
            out var refusal, out var detail, out var offendingIri);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.CorrectsUnboundMarkerBesideEdges, refusal);
        var expected = new[] { CorrigendumOne, CorrigendumTwo }.OrderBy(static root => root, StringComparer.Ordinal).ToArray();
        Assert.AreEqual(expected[0], offendingIri);
        Assert.AreEqual(string.Join("; ", expected), detail);
    }

    [TestMethod]
    public void ABrokenExpressionDeliveryRefusesThroughTheDerivationByName()
    {
        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound([XRow(CorrigendumOne, CorrigendumOne + ".R01", BelongsToWorkIri, CorrigendumOne)]),
            BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)]),
            out var refusal, out var detail, out var offendingIri);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.ExpressionDerivationRefused, refusal);
        StringAssert.Contains(detail, nameof(EuLanguageScopedExpressionDecodeRefusal.ExpressionLanguageMissing), "the decoder's own name travels.");
        Assert.AreEqual(CorrigendumOne + ".R01", offendingIri);
    }

    [TestMethod]
    public void AForgedPageReceiptRefuses()
    {
        var honest = BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)]);
        var forged = honest with
        {
            PagesInOrder = [honest.PagesInOrder[0] with { DurableWriteReceipt = ForgedReceipt() }],
        };

        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)), forged, out var refusal, out _, out var offendingIri);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.PageReceiptDoesNotBindItsBytes, refusal);
        Assert.AreEqual(new string('f', 64), offendingIri);
    }

    /// <summary>
    /// Bytes that are perfectly well-formed for DIFFERENT rows, standing in for the page the proof
    /// was minted over, do not reopen: the decoder's own substitution case, reached through this
    /// door's own refusal.
    /// </summary>
    [TestMethod]
    public void APageCarryingOtherRowsThanTheProofWasMintedOverRefuses()
    {
        var honest = BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)]);
        var substituted = honest with
        {
            PagesInOrder = [honest.PagesInOrder[0] with
            {
                RetainedPayloadBytes = Encoding.UTF8.GetBytes(RowsJson(PProjection, [Corrects(CorrigendumTwo, Gdpr)], 0)),
            }],
        };

        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)), substituted, out var refusal, out var detail, out _);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.ObjectFactsRowsRefused, refusal);
        Assert.IsFalse(string.IsNullOrWhiteSpace(detail), "the reopen door's own reason travels.");
    }

    /// <summary>
    /// A page whose bytes and receipt agree with EACH OTHER but not with the route that transported
    /// them refuses. The lens's finding: a two-way check admits a genuine plan, request and route
    /// paired with re-serialized bytes and a receipt minted for them; the terminal hop's digests are
    /// the third party that refuses the pairing.
    /// </summary>
    [TestMethod]
    public void ASelfConsistentSubstitutedPageRefuses()
    {
        var honest = BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)]);
        var page = honest.PagesInOrder[0];
        var substitutedBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(page.RetainedPayloadBytes.Span) + " ");
        var substituted = honest with
        {
            PagesInOrder = [page with
            {
                RetainedPayloadBytes = substitutedBytes,
                DurableWriteReceipt = ReceiptNaming(substitutedBytes),
            }],
        };
        Assert.AreEqual(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(substitutedBytes)),
            substituted.PagesInOrder[0].DurableWriteReceipt.Reference.ContentSha256,
            "the premise: the substituted pair is self-consistent.");

        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)), substituted, out var refusal, out _, out var offendingIri);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.PageReceiptDoesNotBindItsBytes, refusal);
        Assert.AreEqual(substituted.PagesInOrder[0].DurableWriteReceipt.Reference.ContentSha256, offendingIri);
    }

    /// <summary>A row whose value is bound while its value_kind says unbound disagrees with itself.</summary>
    [TestMethod]
    public void ACorrectsRowWhoseValueKindDisagreesWithItsValueRefusesNamingTheWork()
    {
        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)),
            BoundObjectFacts([PIriRowClaimingUnbound(CorrigendumOne, CorrectsIri, Gdpr)]),
            out var refusal, out _, out var offendingIri);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.CorrectsRowTermKindMismatch, refusal);
        Assert.AreEqual(CorrigendumOne, offendingIri);
    }

    [TestMethod]
    public void AnUnattributablePagingRefuses()
    {
        var set = EuCorrigendumTripwireSet.TryDerive(
            Bound(Expression(CorrigendumOne, "R01.DEU", German)),
            BoundObjectFacts(
                [Corrects(CorrigendumOne, Gdpr)],
                terminalPagePolicy: RepeatedEnumerationTerminalPagePolicy.EmptySuccessorAfterShortPage),
            out var refusal, out _, out _);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.PageAttributionUnavailable, refusal);
    }

    [TestMethod]
    public void NullsAreCallerContractViolations()
    {
        var expressionFacts = Bound(Expression(CorrigendumOne, "R01.DEU", German));
        var objectFacts = BoundObjectFacts([Corrects(CorrigendumOne, Gdpr)]);

        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireSet.TryDerive(null!, objectFacts, out _, out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireSet.TryDerive(expressionFacts, null!, out _, out _, out _));
        Assert.ThrowsExactly<ArgumentException>(() => EuCorrigendumTripwireSet.ReachOf(" "));
        Assert.ThrowsExactly<ArgumentException>(() => Fold(expressionFacts, objectFacts).TripwireFor(""));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireSet.SpellServedBodyLanguages(null!));
    }

    // ---- The policy binding. ----

    /// <summary>The served-language table is exactly the reviewed body policy, by the publisher's IRIs.</summary>
    [TestMethod]
    public void TheServedLanguageTableIsExactlyTheReviewedBodyPolicy()
    {
        Assert.HasCount(
            EuLanguageBodyDisposition.BodyCandidateLanguages.Count,
            EuCorrigendumTripwireSet.ServedBodyLanguageIris);
        CollectionAssert.AreEqual(new[] { English, French }, EuCorrigendumTripwireSet.ServedBodyLanguageIris.ToArray());
        Assert.AreEqual(EuCorrigendumLanguageReach.WithinServedBodyLanguages, EuCorrigendumTripwireSet.ReachOf(English));
        Assert.AreEqual(EuCorrigendumLanguageReach.WithinServedBodyLanguages, EuCorrigendumTripwireSet.ReachOf(French));
        Assert.AreEqual(EuCorrigendumLanguageReach.OutsideServedBodyLanguages, EuCorrigendumTripwireSet.ReachOf(German));
    }

    /// <summary>
    /// A policy naming a language the table cannot spell refuses rather than misstating reach. The
    /// lens noted the static table could never exercise this; the spelling is a pure function so
    /// it can.
    /// </summary>
    [TestMethod]
    public void ThePolicySpellingFailsClosedOnALanguageItCannotSpell()
    {
        CollectionAssert.AreEqual(
            new[] { English, French },
            EuCorrigendumTripwireSet.SpellServedBodyLanguages([EuOfficialLanguage.French, EuOfficialLanguage.English]).ToArray(),
            "ordinal, whatever order the policy lists them in.");
        var refusal = Assert.ThrowsExactly<InvalidOperationException>(() =>
            EuCorrigendumTripwireSet.SpellServedBodyLanguages(
                [EuOfficialLanguage.English, EuOfficialLanguage.French, EuOfficialLanguage.German]));
        StringAssert.Contains(refusal.Message, nameof(EuOfficialLanguage.German));
    }

    // ---- Fixtures: the fold. ----

    private static EuCorrigendumTripwireSet Fold(EuProofBoundDelivery expressionFacts, EuProofBoundDelivery objectFacts)
    {
        var set = EuCorrigendumTripwireSet.TryDerive(expressionFacts, objectFacts, out var refusal, out var detail, out var offendingIri);
        Assert.IsNotNull(set, $"{refusal} {detail} {offendingIri}");
        return set;
    }

    // ---- Fixtures: the deliveries, built exactly as the decoder's own tests build them. ----

    /// <summary>One Expression's complete family-X rows: belongs-to-work plus a language.</summary>
    private static IReadOnlyList<string> Expression(string workRoot, string expressionSuffix, string languageAuthorityIri) =>
        ExpressionNamed(workRoot, workRoot + "." + expressionSuffix, languageAuthorityIri);

    private static IReadOnlyList<string> ExpressionNamed(string workRoot, string expressionIri, string languageAuthorityIri) =>
        [
            XRow(workRoot, expressionIri, BelongsToWorkIri, workRoot),
            XRow(workRoot, expressionIri, UsesLanguageIri, languageAuthorityIri),
        ];

    private static string Corrects(string corrigendumRoot, string correctedRoot) =>
        PIriRow(corrigendumRoot, CorrectsIri, correctedRoot);

    private static string NoCorrection(string workRoot) => PUnboundRow(workRoot, CorrectsIri);

    private static string Date(string workRoot, string date) => PRow(workRoot, WorkDateIri, date, XsdDate);

    private static string NoDate(string workRoot) => PUnboundRow(workRoot, WorkDateIri);

    private static EuProofBoundDelivery Bound(IReadOnlyList<string> rows, int runIdentitySeed = 930) =>
        BoundPaged(XProjection, XKey, rows, runIdentitySeed, 6, 4, RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal);

    private static EuProofBoundDelivery BoundObjectFacts(
        IReadOnlyList<string> rows,
        string[]? canonicalKey = null,
        int runIdentitySeed = 930,
        int rowLimitA = 6,
        int rowLimitB = 4,
        RepeatedEnumerationTerminalPagePolicy terminalPagePolicy = RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal) =>
        BoundPaged(PProjection, canonicalKey ?? PKey, rows, runIdentitySeed, rowLimitA, rowLimitB, terminalPagePolicy);

    /// <summary>
    /// Paged on both passes, as the decoder's own paged probes are, so a delivery of any size fits:
    /// the fixture's single-page door holds at most seven rows and the GDPR shape alone needs eight.
    /// </summary>
    private static EuProofBoundDelivery BoundPaged(
        string[] projection,
        string[] canonicalKey,
        IReadOnlyList<string> rows,
        int runIdentitySeed,
        int rowLimitA,
        int rowLimitB,
        RepeatedEnumerationTerminalPagePolicy terminalPagePolicy)
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture(
            expectedCount: rows.Count,
            maximumDeliverableRows: 999,
            terminalPagePolicy: terminalPagePolicy,
            projectionVariables: projection,
            canonicalKeyVariables: canonicalKey,
            runIdentitySeed: runIdentitySeed);
        var delivery = fixture.CreatePagedRaw(
            rows.Count,
            rowLimitA,
            rowLimitB,
            (first, take) => RowsJson(projection, [.. rows.Skip(first).Take(take)], first),
            static rowIndex => rowIndex.ToString("D4"));
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            "laws", delivery, CustodyMembership.Floored, out var proofRefusal);
        Assert.IsNotNull(proof, $"the fixture must mint an admitting proof: {proofRefusal}");
        return new EuProofBoundDelivery(
            proof, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, Pages(fixture, delivery));
    }

    private static IReadOnlyList<RepeatedEnumerationResolvedEvidence> Pages(
        RepeatedEnumerationDeliveryProofTests.Fixture fixture,
        EnumerationDeliveryComparison delivery) =>
        [.. delivery.PagesA.Pages
            .OrderBy(static page => page.Ordinal)
            .Select(page => fixture.Resolve(page.Evidence))];

    private static string XRow(string parentIri, string expressionIri, string predicateIri, string valueIri) =>
        Binding(
            ("parent", Uri(parentIri)),
            ("object", Uri(expressionIri)),
            ("predicate", Uri(predicateIri)),
            ("value", Uri(valueIri)),
            ("value_kind", Literal("iri", null)),
            ("datatype_iri", Literal(string.Empty, null)),
            ("language_tag", Literal(string.Empty, null)));

    private static string PRow(string objectIri, string predicateIri, string value, string datatype) =>
        Binding(
            ("object", Uri(objectIri)),
            ("predicate", Uri(predicateIri)),
            ("value", Literal(value, datatype)),
            ("value_kind", Literal("literal", null)));

    private static string PIriRow(string objectIri, string predicateIri, string valueIri) =>
        Binding(
            ("object", Uri(objectIri)),
            ("predicate", Uri(predicateIri)),
            ("value", Uri(valueIri)),
            ("value_kind", Literal("iri", null)));

    /// <summary>A bound IRI value under a value_kind that claims it is unbound: a row disagreeing with itself.</summary>
    private static string PIriRowClaimingUnbound(string objectIri, string predicateIri, string valueIri) =>
        Binding(
            ("object", Uri(objectIri)),
            ("predicate", Uri(predicateIri)),
            ("value", Uri(valueIri)),
            ("value_kind", Literal("unbound", null)));

    /// <summary>An asked-and-unanswered object-facts row: the value variable is simply absent.</summary>
    private static string PUnboundRow(string objectIri, string predicateIri) =>
        Binding(
            ("object", Uri(objectIri)),
            ("predicate", Uri(predicateIri)),
            ("value_kind", Literal("unbound", null)));

    private static string Uri(string value) => $"{{\"type\":\"uri\",\"value\":{Json(value)}}}";

    private static string Literal(string value, string? datatype) =>
        datatype is null
            ? $"{{\"type\":\"literal\",\"value\":{Json(value)}}}"
            : $"{{\"type\":\"literal\",\"value\":{Json(value)},\"datatype\":{Json(datatype)}}}";

    private static string Binding(params (string Name, string Term)[] terms) =>
        string.Join(',', terms.Select(static term => $"{Json(term.Name)}:{term.Term}"));

    /// <summary>Cursors zero-padded, as the decoder's tests pad them: they compare as text.</summary>
    private static string RowsJson(IReadOnlyList<string> projection, IReadOnlyList<string> rows, int firstRowIndex) =>
        "{\"head\":{\"link\":[],\"vars\":[" +
        string.Join(',', projection.Select(Json)) +
        "]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[" +
        string.Join(
            ',',
            rows.Select((row, index) =>
                "{" + row + ",\"cursor\":{\"type\":\"literal\",\"value\":\"" + (firstRowIndex + index).ToString("D4") + "\"}}")) +
        "]}}";

    private static string Json(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// A structurally valid write receipt naming bytes that do not exist, exactly as the decoder's
    /// tests forge one. Nothing in the custody contracts ties a receipt to an actual write.
    /// </summary>
    private static DurableBlobWriteReceipt ForgedReceipt() => ReceiptFor(new string('f', 64), 4);

    /// <summary>A structurally valid receipt that really does name these bytes - and nothing else.</summary>
    private static DurableBlobWriteReceipt ReceiptNaming(byte[] bytes) =>
        ReceiptFor(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length);

    private static DurableBlobWriteReceipt ReceiptFor(string contentSha256, long byteLength)
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, contentSha256, byteLength, CustodyClass.NightlyFloor90d);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                policyKey: null,
                CustodyProtection.NotEnforced,
                DateTimeOffset.Parse(
                    "2026-01-02T03:04:05.0000000+00:00", System.Globalization.CultureInfo.InvariantCulture),
                protectedUntil: null));
    }
}
