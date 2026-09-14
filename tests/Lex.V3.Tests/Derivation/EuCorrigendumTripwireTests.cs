using System.Security.Cryptography;
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
/// The Stage 3 half of "render the tripwire": from a retained expression derivation and the decoded
/// snapshots, every corrected work's corrigendum lines, with reach and date stated per line.
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
    // Every snapshot root must be an Appendix A pack root; the corrected targets need only be
    // canonical. Picked by index so a reordered seed map cannot silently change the fixtures.
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
    private const string GdprCorrigendumDate = "2018-05-23";

    private static readonly string BelongsToWorkIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork);
    private static readonly string UsesLanguageIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage);
    private static readonly string WorkDateIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkDateDocument);

    private static readonly string[] XProjection =
        ["parent", "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag", "cursor"];
    private static readonly string[] XKey = ["parent", "object", "predicate", "value"];
    private static readonly string[] PProjection =
        ["object", "predicate", "value", "value_kind", "cursor"];
    private static readonly string[] PKey = ["object", "predicate", "value"];

    // ---- The measured shape. ----

    /// <summary>
    /// The product specification's example: one corrigendum, four languages, none of them served.
    /// Four lines, each outside the served languages, each dated exactly as the publisher wrote it.
    /// </summary>
    [TestMethod]
    public void TheGdprCorrigendumShapeYieldsFourLinesAllOutsideServedLanguages()
    {
        var derivation = Derive(
            [
                .. Expression(CorrigendumOne, "R01.EST", Estonian),
                .. Expression(CorrigendumOne, "R01.DEU", German),
                .. Expression(CorrigendumOne, "R01.HUN", Hungarian),
                .. Expression(CorrigendumOne, "R01.ITA", Italian),
            ],
            [PRow(CorrigendumOne, WorkDateIri, GdprCorrigendumDate, XsdDate)]);

        var set = Fold(derivation, Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr)));

        Assert.HasCount(1, set.Tripwires);
        var tripwire = set.TripwireFor(Gdpr);
        Assert.IsNotNull(tripwire);
        Assert.HasCount(4, tripwire.Lines);
        Assert.AreEqual(0, tripwire.WithinServedCount, "no served body carries this corrigendum.");
        Assert.AreEqual(4, tripwire.OutsideServedCount);
        Assert.AreEqual(4, tripwire.DatedCount);
        foreach (var line in tripwire.Lines)
        {
            Assert.AreEqual(Gdpr, line.CorrectedWorkRoot);
            Assert.AreEqual(CorrigendumOne, line.CorrigendumWorkRoot);
            Assert.AreEqual(EuCorrigendumLanguageReach.OutsideServedBodyLanguages, line.Reach);
            Assert.AreEqual(EuCorrigendumDateState.PublisherDated, line.DateState);
            Assert.AreEqual("corrigendum_dated_outside_served_languages", line.ReasonCode);
            Assert.AreEqual(GdprCorrigendumDate, line.PublisherCorrigendumDate!.RawLexical, "carried, never computed.");
            Assert.AreEqual(XsdDate, line.PublisherCorrigendumDate.DatatypeIri);
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
        var derivation = Derive(
            [
                .. Expression(CorrigendumOne, "R01.ENG", English),
                .. Expression(CorrigendumOne, "R01.FRA", French),
                .. Expression(CorrigendumOne, "R01.DEU", German),
                .. Expression(CorrigendumOne, "R01.NOR", Norwegian),
            ]);

        var tripwire = Fold(derivation, Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr))).TripwireFor(Gdpr)!;

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
    /// Dated, consulted-and-none-stated, and never-consulted are three states with three addresses.
    /// The undated states invent no date.
    /// </summary>
    [TestMethod]
    public void TheThreeDateStatesAreDistinctAndDistinctlyAddressed()
    {
        var rows = Expression(CorrigendumOne, "R01.DEU", German);
        var snapshot = Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr));

        var dated = Fold(Derive(rows, [PRow(CorrigendumOne, WorkDateIri, GdprCorrigendumDate, XsdDate)]), snapshot).TripwireFor(Gdpr)!;
        var consultedUndated = Fold(Derive(rows, [PRow(OtherAct, WorkDateIri, "2016-01-01", XsdDate)]), snapshot).TripwireFor(Gdpr)!;
        var notConsulted = Fold(Derive(rows), snapshot).TripwireFor(Gdpr)!;

        Assert.AreEqual(EuCorrigendumDateState.PublisherDated, dated.Lines.Single().DateState);
        Assert.AreEqual(EuCorrigendumDateState.NotStatedByConsultedDelivery, consultedUndated.Lines.Single().DateState);
        Assert.AreEqual(EuCorrigendumDateState.DateDeliveryNotConsulted, notConsulted.Lines.Single().DateState);
        Assert.IsNull(consultedUndated.Lines.Single().PublisherCorrigendumDate, "no default, no sentinel.");
        Assert.IsNull(notConsulted.Lines.Single().PublisherCorrigendumDate);
        Assert.AreEqual(1, dated.DatedCount);
        Assert.AreEqual(0, consultedUndated.DatedCount);
        Assert.AreEqual(0, notConsulted.DatedCount);
        Assert.AreEqual(
            3,
            new HashSet<string>(StringComparer.Ordinal)
            {
                dated.TripwireSha256, consultedUndated.TripwireSha256, notConsulted.TripwireSha256,
            }.Count,
            "three different facts, three different addresses.");
    }

    /// <summary>Every reach and date state pair renders to its own closed token.</summary>
    [TestMethod]
    public void EveryReachAndDateStatePairHasItsOwnReasonCode()
    {
        var rows = new List<string>();
        rows.AddRange(Expression(CorrigendumOne, "R01.ENG", English));
        rows.AddRange(Expression(CorrigendumOne, "R01.DEU", German));
        var snapshot = Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr));
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var derivation in new[]
        {
            Derive(rows, [PRow(CorrigendumOne, WorkDateIri, GdprCorrigendumDate, XsdDate)]),
            Derive(rows, [PRow(OtherAct, WorkDateIri, "2016-01-01", XsdDate)]),
            Derive(rows),
        })
        {
            foreach (var line in Fold(derivation, snapshot).TripwireFor(Gdpr)!.Lines)
            {
                codes.Add(line.ReasonCode);
            }
        }

        CollectionAssert.AreEqual(
            new[]
            {
                "corrigendum_date_unknown_outside_served_languages",
                "corrigendum_date_unknown_within_served_languages",
                "corrigendum_dated_outside_served_languages",
                "corrigendum_dated_within_served_languages",
                "corrigendum_undated_outside_served_languages",
                "corrigendum_undated_within_served_languages",
            },
            codes.ToArray());
    }

    // ---- S3-A04: byte-stable content, per-run lineage. ----

    /// <summary>
    /// Two independent executions - different acquisition runs, different evidence references -
    /// over the same publisher statements produce one canonical address and two lineages.
    /// </summary>
    [TestMethod]
    public void TwoIndependentExecutionsAgreeOnContentAndDifferOnLineage()
    {
        var rows = new List<string>();
        rows.AddRange(Expression(CorrigendumOne, "R01.EST", Estonian));
        rows.AddRange(Expression(CorrigendumOne, "R01.DEU", German));
        var dates = new[] { PRow(CorrigendumOne, WorkDateIri, GdprCorrigendumDate, XsdDate) };

        var first = Fold(
            Derive(rows, dates, runIdentitySeed: 930),
            Snapshot(CorrigendumOne, Complete(CorrigendumOne, "run-one", Gdpr), evidenceSeed: "run-one"));
        var second = Fold(
            Derive(rows, dates, runIdentitySeed: 931),
            Snapshot(CorrigendumOne, Complete(CorrigendumOne, "run-two", Gdpr), evidenceSeed: "run-two"));

        // The premise: the two really are different executions.
        Assert.AreNotEqual(
            first.Tripwires.Single().Lines[0].CorrectsEvidenceRefs[0].ResourceId,
            second.Tripwires.Single().Lines[0].CorrectsEvidenceRefs[0].ResourceId,
            "the fixture must vary the evidence the edge came from.");

        Assert.AreEqual(first.CanonicalSha256, second.CanonicalSha256, "what the publisher said is one fact.");
        CollectionAssert.AreEqual(first.CanonicalBytes.ToArray(), second.CanonicalBytes.ToArray());
        Assert.AreEqual(first.Tripwires.Single().TripwireSha256, second.Tripwires.Single().TripwireSha256);
        Assert.AreNotEqual(first.LineageSha256, second.LineageSha256, "each time it was observed is another.");
    }

    /// <summary>Snapshots and expressions in any order fold to one canonical form.</summary>
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
        var one = Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr, OtherAct));
        var two = Snapshot(CorrigendumTwo, Complete(CorrigendumTwo, "r02", Gdpr));

        var ascending = Fold(Derive(forward), one, two);
        var descending = Fold(Derive(backward), two, one);

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

        var tripwire = Fold(Derive(rows), Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr))).TripwireFor(Gdpr)!;

        CollectionAssert.AreEqual(
            new[] { CorrigendumOne + ".B", CorrigendumOne + ".a" },
            tripwire.Lines.Select(static line => line.PublisherExpressionId).ToArray());
        var roots = Fold(Derive(rows), Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", OtherAct, Gdpr)))
            .Tripwires.Select(static tripwire => tripwire.CorrectedWorkRoot).ToArray();
        CollectionAssert.AreEqual(roots.OrderBy(static root => root, StringComparer.Ordinal).ToArray(), roots);
    }

    // ---- S3-A03: explicit fidelity. ----

    /// <summary>A corrigendum correcting two works is on both of them.</summary>
    [TestMethod]
    public void ACorrigendumCorrectingTwoWorksAppearsOnBoth()
    {
        var set = Fold(
            Derive(Expression(CorrigendumOne, "R01.DEU", German)),
            Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr, OtherAct)));

        Assert.HasCount(2, set.Tripwires);
        Assert.AreEqual(CorrigendumOne, set.TripwireFor(Gdpr)!.Lines.Single().CorrigendumWorkRoot);
        Assert.AreEqual(CorrigendumOne, set.TripwireFor(OtherAct)!.Lines.Single().CorrigendumWorkRoot);
        Assert.IsNull(set.TripwireFor(BaseAct), "nothing corrects this one.");
    }

    /// <summary>
    /// A corrigendum the publisher says corrects a work, whose expressions this derivation does not
    /// hold, is listed on that work - not dropped. Dropping it would say "no corrigendum".
    /// </summary>
    [TestMethod]
    public void ACorrigendumWithoutDerivedExpressionsIsListedNotDropped()
    {
        var derivation = Derive(Expression(CorrigendumOne, "R01.DEU", German));
        var one = Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr));
        var two = Snapshot(CorrigendumTwo, Complete(CorrigendumTwo, "r02", Gdpr));

        var with = Fold(derivation, one, two).TripwireFor(Gdpr)!;
        var without = Fold(derivation, one).TripwireFor(Gdpr)!;

        Assert.HasCount(1, with.Lines);
        CollectionAssert.AreEqual(new[] { CorrigendumTwo }, with.CorrigendaWithoutDerivedExpressions.ToArray());
        Assert.IsEmpty(without.CorrigendaWithoutDerivedExpressions);
        Assert.AreNotEqual(with.TripwireSha256, without.TripwireSha256, "a listed unknown is content.");
        StringAssert.Contains(with.Describe(), "corrigenda_without_derived_expressions=1");
    }

    /// <summary>
    /// A Corrects family that is not completely acquired is an unresolved gap naming the object.
    /// The fold never reads edges off it and never says "no corrigendum" for it; a complete family
    /// with zero edges is exactly that and is neither a gap nor a tripwire.
    /// </summary>
    [TestMethod]
    public void AnIncompletelyAcquiredCorrectsFamilyIsAnUnresolvedGapNotAnAbsenceClaim()
    {
        var derivation = Derive(Expression(CorrigendumOne, "R01.DEU", German));
        var unacquired = Snapshot(CorrigendumOne, new EuRelationFamilyObservation(
            EuRelationFamily.Corrects, EuRelationAcquisitionState.Unacquired, [], null), objectSuffix: "/u");
        var incomplete = Snapshot(CorrigendumOne, new EuRelationFamilyObservation(
            EuRelationFamily.Corrects, EuRelationAcquisitionState.Incomplete, [Edge("partial", Gdpr)], null), objectSuffix: "/i");
        var uncertain = Snapshot(CorrigendumTwo, new EuRelationFamilyObservation(
            EuRelationFamily.Corrects, EuRelationAcquisitionState.Uncertain, [Edge("doubtful", Gdpr)], null));
        var completeAndEmpty = Snapshot(BaseAct, Complete(BaseAct, "base"));

        var set = Fold(derivation, uncertain, incomplete, unacquired, completeAndEmpty);

        Assert.IsEmpty(set.Tripwires, "an incompletely acquired edge is not a link, even when it names a target.");
        Assert.HasCount(3, set.UnresolvedGaps);
        CollectionAssert.AreEqual(
            set.UnresolvedGaps.Select(static gap => gap.ObjectPublisherUri).OrderBy(static uri => uri, StringComparer.Ordinal).ToArray(),
            set.UnresolvedGaps.Select(static gap => gap.ObjectPublisherUri).ToArray(),
            "ordinal by object.");
        CollectionAssert.AreEquivalent(
            new[]
            {
                EuRelationAcquisitionState.Unacquired,
                EuRelationAcquisitionState.Incomplete,
                EuRelationAcquisitionState.Uncertain,
            },
            set.UnresolvedGaps.Select(static gap => gap.CorrectsAcquisition).ToArray(),
            "the gap says how far acquisition got.");
        Assert.IsTrue(set.UnresolvedGaps.All(static gap => gap.Reason == EuCorrigendumTripwireGapReason.CorrectsNotCompletelyAcquired));
        Assert.IsFalse(
            set.UnresolvedGaps.Any(gap => gap.ObjectPublisherUri == BaseAct),
            "a complete family with no edges is a complete answer, not a gap.");
    }

    /// <summary>Expressions of a work with no Corrects edge are outside the subject set: no line, no gap.</summary>
    [TestMethod]
    public void ExpressionsOfWorksWithoutCorrectsEdgesAreOutsideTheSubjectSet()
    {
        var derivation = Derive(
            [.. Expression(BaseAct, "ENG", English), .. Expression(BaseAct, "FRA", French)]);

        var set = Fold(derivation, Snapshot(BaseAct, Complete(BaseAct, "base")));

        Assert.IsEmpty(set.Tripwires);
        Assert.IsEmpty(set.UnresolvedGaps);
        Assert.AreEqual(
            "corrected_works=0 lines=0 (within_served=0 outside_served=0 dated=0) " +
            "corrigenda_without_derived_expressions=0 unresolved_gaps=0",
            set.Describe());
    }

    /// <summary>
    /// Two snapshots of one corrigendum work - its root and a consolidated state - each stating the
    /// edge contribute both their evidence references to every line, ordinal.
    /// </summary>
    [TestMethod]
    public void TwoSnapshotsOfOneWorkRootMergeTheirEdgeEvidence()
    {
        var root = Snapshot(CorrigendumOne, Complete(CorrigendumOne, "root", Gdpr));
        var state = Snapshot(CorrigendumOne, Complete(CorrigendumOne, "state", Gdpr), objectSuffix: "/state/2018");

        var line = Fold(Derive(Expression(CorrigendumOne, "R01.DEU", German)), state, root).TripwireFor(Gdpr)!.Lines.Single();

        Assert.HasCount(2, line.CorrectsEvidenceRefs, "both statements of the edge are kept.");
        CollectionAssert.AreEqual(
            line.CorrectsEvidenceRefs.Select(static reference => reference.ResourceId).OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
            line.CorrectsEvidenceRefs.Select(static reference => reference.ResourceId).ToArray());
    }

    /// <summary>The lineage names the derivation, every page digest and every edge's evidence.</summary>
    [TestMethod]
    public void TheLineageBindsTheDerivationAndEveryEdgeEvidence()
    {
        var derivation = Derive(Expression(CorrigendumOne, "R01.DEU", German));
        var set = Fold(derivation, Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr)));
        var lineage = Encoding.UTF8.GetString(set.LineageBytes.Span);
        var canonical = Encoding.UTF8.GetString(set.CanonicalBytes.Span);
        var line = set.Tripwires.Single().Lines.Single();

        StringAssert.Contains(lineage, derivation.DerivationSha256);
        StringAssert.Contains(lineage, set.CanonicalSha256, "the lineage is bound to what it is the lineage of.");
        StringAssert.Contains(lineage, line.CorrectsEvidenceRefs.Single().ResourceId);
        StringAssert.Contains(lineage, line.CorrectsEvidenceRefs.Single().Sha256);
        foreach (var pageDigest in line.LineageContentSha256InOrder)
        {
            StringAssert.Contains(lineage, pageDigest);
        }

        Assert.IsFalse(
            canonical.Contains(line.CorrectsEvidenceRefs.Single().ResourceId, StringComparison.Ordinal),
            "and the per-run reference is NOT in the canonical bytes.");
        StringAssert.Contains(canonical, line.ExpressionContentSha256, "the expression's own content digest is.");
        StringAssert.Contains(canonical, EuCorrigendumTripwireSet.Schema);
        StringAssert.Contains(lineage, EuCorrigendumTripwireSet.LineageRecordSchema);
    }

    // ---- Refusals and caller contract. ----

    /// <summary>Two snapshots naming one object refuse, naming every offender in ordinal order.</summary>
    [TestMethod]
    public void TwoSnapshotsNamingOneObjectRefuseNamingEveryOffender()
    {
        var derivation = Derive(Expression(CorrigendumOne, "R01.DEU", German));
        var one = Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr));
        var oneAgain = Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01-again", Gdpr));
        var two = Snapshot(CorrigendumTwo, Complete(CorrigendumTwo, "r02", Gdpr));
        var twoAgain = Snapshot(CorrigendumTwo, Complete(CorrigendumTwo, "r02-again", OtherAct));

        var set = EuCorrigendumTripwireSet.TryDerive(derivation, [two, one, twoAgain, oneAgain], out var refusal, out var detail);

        Assert.IsNull(set);
        Assert.AreEqual(EuCorrigendumTripwireRefusal.SnapshotDeliveredTwice, refusal);
        var expected = new[] { CorrigendumOne, CorrigendumTwo }.OrderBy(static root => root, StringComparer.Ordinal).ToArray();
        Assert.AreEqual("two snapshots name one object: " + string.Join("; ", expected), detail);
    }

    [TestMethod]
    public void NullsAreCallerContractViolations()
    {
        var derivation = Derive(Expression(CorrigendumOne, "R01.DEU", German));
        var snapshot = Snapshot(CorrigendumOne, Complete(CorrigendumOne, "r01", Gdpr));

        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireSet.TryDerive(null!, [snapshot], out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireSet.TryDerive(derivation, null!, out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireSet.TryDerive(derivation, [snapshot, null!], out _, out _));
        Assert.ThrowsExactly<ArgumentException>(() => EuCorrigendumTripwireSet.ReachOf(" "));
        Assert.ThrowsExactly<ArgumentException>(() => Fold(derivation, snapshot).TripwireFor(""));
    }

    // ---- The policy binding. ----

    /// <summary>
    /// The served-language table is exactly the reviewed body policy, by the publisher's IRIs.
    /// A policy the table could not spell would refuse to load rather than misstate reach.
    /// </summary>
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
    /// The V2-era count of corrigenda outside the served languages is not restated here as a V3
    /// fact. Nothing in this contract counts anything about the publisher.
    /// </summary>
    [TestMethod]
    public void NoAcceptanceFigureLivesOnTheSurface()
    {
        var source = ReadSource("src/Lex.V3.Contracts/Derivation/EuCorrigendumTripwire.cs");
        Assert.IsFalse(source.Contains("385", StringComparison.Ordinal));
    }

    // ---- Fixtures: the fold. ----

    private static EuCorrigendumTripwireSet Fold(
        EuLanguageScopedExpressionDerivation derivation, params EuCellarObjectSnapshot[] snapshots)
    {
        var set = EuCorrigendumTripwireSet.TryDerive(derivation, snapshots, out var refusal, out var detail);
        Assert.IsNotNull(set, $"{refusal} {detail}");
        return set;
    }

    // ---- Fixtures: snapshots. ----

    private static EuRelationFamilyObservation Complete(string corrigendumRoot, string evidenceLabel, params string[] targets) =>
        new(
            EuRelationFamily.Corrects,
            EuRelationAcquisitionState.Complete,
            [.. targets.Select(target => Edge(evidenceLabel + ":" + corrigendumRoot, target))],
            Artifact("completion:" + evidenceLabel + ":" + corrigendumRoot));

    private static EuRelationEdgeObservation Edge(string evidenceLabel, string target) =>
        new(EuRelationFamily.Corrects, EuRelationAuthority.PublisherAsserted, target, Artifact("edge:" + evidenceLabel + ":" + target));

    private static EuCellarObjectSnapshot Snapshot(
        string root,
        EuRelationFamilyObservation corrects,
        string objectSuffix = "",
        string evidenceSeed = "seed")
    {
        var objectUri = root + objectSuffix;
        var relations = EuScopeVocabulary.ReadRelationFamilies
            .Select(family => family == EuRelationFamily.Corrects
                ? corrects
                : new EuRelationFamilyObservation(family, EuRelationAcquisitionState.Unacquired, [], null))
            .ToArray();
        var snapshot = EuCellarObjectSnapshot.TryObserve(
            ObjectRef(objectUri, evidenceSeed),
            root,
            EuActForm.Regulation,
            Artifact(evidenceSeed + ":record:" + objectUri),
            EuScopeVocabulary.CdmPredicates
                .Select(predicate => new EuPredicateObservation(
                    predicate, EuPredicateObservationState.NotObserved, [], Artifact(evidenceSeed + ":p:" + predicate)))
                .ToArray(),
            new EuChannelObservation(EuChannel.CellarSparqlEndpoint, "eu_channel.sparql", "rule.channel", Artifact(evidenceSeed + ":channel")),
            null,
            null,
            null,
            relations,
            Artifact(evidenceSeed + ":relation-axis:" + objectUri),
            null,
            Artifact(evidenceSeed + ":supporting:" + objectUri),
            out var refusal);
        return snapshot ?? throw new InvalidOperationException($"fixture snapshot refused as {refusal}");
    }

    private static SourceObjectRef ObjectRef(string publisherUri, string evidenceSeed) => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        new SourceRegistryMemberRef(Artifact(evidenceSeed + ":registry"), "work"),
        publisherUri,
        "cellar|object|" + publisherUri,
        Digest("cellar|object|" + publisherUri),
        Artifact(evidenceSeed + ":identity-profile"),
        null);

    private static SourceArtifactRef Artifact(string label) =>
        new($"urn:uuid:{DeterministicGuid(label)}", Digest("evidence:" + label));

    private static Guid DeterministicGuid(string label) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("guid:" + label))[..16]);

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // ---- Fixtures: the derivation, built exactly as the decoder's own tests build it. ----

    private static EuLanguageScopedExpressionDerivation Derive(
        IReadOnlyList<string> expressionRows,
        IReadOnlyList<string>? objectRows = null,
        int runIdentitySeed = 930)
    {
        var derivation = EuLanguageScopedExpressionDerivation.TryDerive(
            Bound(expressionRows, runIdentitySeed),
            objectRows is null ? null : BoundObjectFacts(objectRows, runIdentitySeed),
            out var refusal,
            out var decodeRefusal,
            out var detail,
            out var offendingIri);
        Assert.IsNotNull(derivation, $"the fixture must derive: {refusal} {decodeRefusal} {detail} {offendingIri}");
        return derivation;
    }

    /// <summary>One Expression's complete family-X rows: belongs-to-work plus a language.</summary>
    private static IReadOnlyList<string> Expression(string workRoot, string expressionSuffix, string languageAuthorityIri) =>
        [
            XRow(workRoot, workRoot + "." + expressionSuffix, BelongsToWorkIri, workRoot),
            XRow(workRoot, workRoot + "." + expressionSuffix, UsesLanguageIri, languageAuthorityIri),
        ];

    private static EuProofBoundDelivery Bound(IReadOnlyList<string> rows, int runIdentitySeed) =>
        BoundPaged(XProjection, XKey, rows, runIdentitySeed);

    private static EuProofBoundDelivery BoundObjectFacts(IReadOnlyList<string> rows, int runIdentitySeed) =>
        BoundPaged(PProjection, PKey, rows, runIdentitySeed);

    /// <summary>
    /// Paged on both passes, as the decoder's own paged probes are, so a delivery of any size fits:
    /// the fixture's single-page door holds at most seven rows and the GDPR shape alone needs eight.
    /// </summary>
    private static EuProofBoundDelivery BoundPaged(
        string[] projection, string[] canonicalKey, IReadOnlyList<string> rows, int runIdentitySeed)
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture(
            expectedCount: rows.Count,
            maximumDeliverableRows: 999,
            terminalPagePolicy: RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal,
            projectionVariables: projection,
            canonicalKeyVariables: canonicalKey,
            runIdentitySeed: runIdentitySeed);
        var delivery = fixture.CreatePagedRaw(
            rows.Count,
            6,
            4,
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

    private static string ReadSource(string repositoryRelativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("Checkout root not found.");
        return File.ReadAllText(
            Path.Combine(root, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
