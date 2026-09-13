using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// #419 slice 5: the counting rules and gap rules. A frame folds through the manifest into E10's
/// counts; the class is the manifest's reading, exactly LOI and RGD enter the population, every
/// other recognized class is a named exclusion, an unproven act is a typed gap in neither counted
/// bucket, and an unrecognized class refuses the whole fold.
/// </summary>
[TestClass]
public sealed class LuxembourgNeverConsolidatedCoverageTests
{
    private const string Authority = "http://data.legilux.public.lu/resource/authority/resource-type/";
    private static readonly string LoiClass = LuxembourgActClassManifest.LoiClassIri;
    private static readonly string RgdClass = LuxembourgActClassManifest.RgdClassIri;
    private const string AminClass = Authority + "AMIN";
    private const string UnknownClass = Authority + "NOT_A_REAL_CODE";
    private const string StandInClass = "http://data.legilux.public.lu/resource/authority/legal-type/LOI";

    // THE PATHS ARE DELIBERATELY MISLEADING in the test that needs them: the class is the manifest's
    // reading of the class IRI, never the /loi/ or /rgd/ segment of the ELI.
    private const string ActLoiPath1 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo";
    private const string ActRgdPath2 = "http://data.legilux.public.lu/eli/etat/leg/rgd/2026/02/02/a2/jo";
    private const string ActLoiPath3 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/03/03/a3/jo";
    private const string ActRgdPath4 = "http://data.legilux.public.lu/eli/etat/leg/rgd/2026/04/04/a4/jo";
    private const string ActAminPath5 = "http://data.legilux.public.lu/eli/etat/leg/amin/2026/05/05/a5/jo";
    private const string ActNeverHeld = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/09/09/a9/jo";

    [TestMethod]
    public void AnEmptyFrameFoldsToNothingHeldAndClaimsNothing()
    {
        var coverage = Fold(new LuxembourgNeverConsolidatedFrame());

        Assert.AreEqual(0, coverage.HeldActCount);
        Assert.AreEqual(0, coverage.PopulationSize);
        Assert.AreEqual(0, coverage.NeverConsolidatedCount);
        Assert.AreEqual(0, coverage.ConsolidatedCount);
        Assert.AreEqual(0, coverage.NamedExclusionCount);
        Assert.IsTrue(coverage.AllHeldActsSettled, "nothing held, nothing unsettled.");
        Assert.AreEqual(
            "held=0 population=0 (loi=0 rgd=0) never_consolidated=0 (loi=0 rgd=0) "
            + "consolidated=0 unresolved_gaps=0 named_exclusions=0",
            coverage.Describe());
    }

    /// <summary>The class is what the manifest reads off the class IRI, never what the ELI path says.</summary>
    [TestMethod]
    public void TheClassIsTheManifestsReadingNotTheEliPaths()
    {
        // A /loi/ path carrying the RGD class, and a /rgd/ path carrying the LOI class.
        var coverage = Fold(Frame(
            Entry(ActLoiPath1, RgdClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows),
            Entry(ActRgdPath2, LoiClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows)));

        Assert.AreEqual(LuxembourgActClassScope.InScopeRgd, coverage.PlacementFor(ActLoiPath1)!.Scope);
        Assert.AreEqual(LuxembourgActClassScope.InScopeLoi, coverage.PlacementFor(ActRgdPath2)!.Scope);
        Assert.AreEqual(1, coverage.PopulationRgdCount);
        Assert.AreEqual(1, coverage.PopulationLoiCount);
        Assert.AreEqual(1, coverage.NeverConsolidatedRgdCount);
        Assert.AreEqual(1, coverage.NeverConsolidatedLoiCount);
    }

    /// <summary>
    /// Exactly the classes the manifest counts enter the population; every other recognized class
    /// is listed with its class IRI and counted nowhere.
    /// </summary>
    [TestMethod]
    public void ExactlyTheCountedClassesEnterThePopulationAndTheRestAreNamed()
    {
        var coverage = Fold(Frame(
            Entry(ActLoiPath1, LoiClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows),
            Entry(ActRgdPath2, RgdClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows),
            Entry(ActAminPath5, AminClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows)));

        Assert.AreEqual(3, coverage.HeldActCount, "every held act is placed, exclusions included.");
        Assert.AreEqual(2, coverage.PopulationSize);
        Assert.AreEqual(2, coverage.NeverConsolidatedCount);
        Assert.AreEqual(1, coverage.NamedExclusionCount);

        var excluded = coverage.PlacementFor(ActAminPath5)!;
        Assert.AreEqual(LuxembourgNeverConsolidatedMembership.RecognizedOutOfScope, excluded.Membership);
        Assert.AreEqual(LuxembourgActClassScope.RecognizedOutOfScope, excluded.Scope);
        Assert.AreEqual(AminClass, excluded.PublisherClassIri, "named with its class, not dropped.");
        Assert.AreEqual(0, coverage.UnresolvedGaps.Count, "an exclusion is not a gap.");
    }

    /// <summary>
    /// Each disposition has its bucket, in admission order, and
    /// population == never-consolidated + consolidated + gaps.
    /// </summary>
    [TestMethod]
    public void EachDispositionHasItsBucketAndTheInvariantHolds()
    {
        var coverage = Fold(Frame(
            Entry(ActLoiPath1, LoiClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows),
            Entry(ActLoiPath3, LoiClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredRows),
            Entry(ActRgdPath2, RgdClass, LuxembourgNeverConsolidatedDisposition.NoEnumerationCited)));

        CollectionAssert.AreEqual(
            new[]
            {
                LuxembourgNeverConsolidatedMembership.NeverConsolidated,
                LuxembourgNeverConsolidatedMembership.Consolidated,
                LuxembourgNeverConsolidatedMembership.EnumerationNotCited,
            },
            coverage.Placements.Select(static p => p.Membership).ToArray(),
            "buckets follow dispositions, in admission order.");

        Assert.AreEqual(1, coverage.NeverConsolidatedCount);
        Assert.AreEqual(1, coverage.NeverConsolidatedLoiCount);
        Assert.AreEqual(0, coverage.NeverConsolidatedRgdCount);
        Assert.AreEqual(1, coverage.ConsolidatedCount);
        Assert.AreEqual(1, coverage.UnresolvedGaps.Count);
        Assert.AreEqual(3, coverage.PopulationSize);
        Assert.AreEqual(
            coverage.NeverConsolidatedCount + coverage.ConsolidatedCount + coverage.UnresolvedGaps.Count,
            coverage.PopulationSize,
            "the invariant.");
        Assert.IsFalse(coverage.AllHeldActsSettled);

        var gap = coverage.UnresolvedGaps[0];
        Assert.AreEqual(ActRgdPath2, gap.PublisherActIri);
        Assert.AreEqual(LuxembourgActClassScope.InScopeRgd, gap.Scope);
        Assert.AreEqual(LuxembourgNeverConsolidatedGapReason.EnumerationNotCited, gap.Reason);
    }

    /// <summary>A gap is in the population and in neither counted bucket.</summary>
    [TestMethod]
    public void AGapIsInThePopulationAndInNeitherCountedBucket()
    {
        var coverage = Fold(Frame(
            Entry(ActLoiPath1, LoiClass, LuxembourgNeverConsolidatedDisposition.NoEnumerationCited),
            Entry(ActRgdPath2, RgdClass, LuxembourgNeverConsolidatedDisposition.NoEnumerationCited)));

        Assert.AreEqual(2, coverage.PopulationSize);
        Assert.AreEqual(1, coverage.PopulationLoiCount);
        Assert.AreEqual(1, coverage.PopulationRgdCount);
        Assert.AreEqual(0, coverage.NeverConsolidatedCount, "unproven is never counted as never-consolidated.");
        Assert.AreEqual(0, coverage.ConsolidatedCount);
        Assert.AreEqual(2, coverage.UnresolvedGaps.Count);
        Assert.IsFalse(coverage.AllHeldActsSettled);
    }

    /// <summary>Gaps are about the population: an out-of-scope act is never one, whatever its disposition.</summary>
    [TestMethod]
    public void AnOutOfScopeActIsNeverAGapWhateverItsDisposition()
    {
        var coverage = Fold(Frame(
            Entry(ActAminPath5, AminClass, LuxembourgNeverConsolidatedDisposition.NoEnumerationCited)));

        Assert.AreEqual(1, coverage.HeldActCount);
        Assert.AreEqual(0, coverage.PopulationSize);
        Assert.AreEqual(0, coverage.UnresolvedGaps.Count);
        Assert.AreEqual(1, coverage.NamedExclusionCount);
        Assert.AreEqual(
            LuxembourgNeverConsolidatedMembership.RecognizedOutOfScope,
            coverage.PlacementFor(ActAminPath5)!.Membership);
        Assert.IsTrue(coverage.AllHeldActsSettled, "nothing in the population is unsettled.");
    }

    /// <summary>
    /// An unrecognized class is neither a gap nor an exclusion: the whole fold refuses, naming the
    /// act and the class, rather than stepping over it.
    /// </summary>
    [TestMethod]
    public void AnUnrecognizedClassRefusesTheWholeFoldRatherThanSkipping()
    {
        foreach (var unrecognized in new[] { UnknownClass, StandInClass })
        {
            var frame = Frame(
                Entry(ActLoiPath1, LoiClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows),
                Entry(ActRgdPath2, unrecognized, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows));

            var coverage = LuxembourgNeverConsolidatedCoverage.TryComplete(frame, out var refusal, out var detail);

            Assert.IsNull(coverage, $"{unrecognized}: no count over a frame holding an unknown code.");
            Assert.AreEqual(LuxembourgNeverConsolidatedCoverageRefusal.UnrecognizedActClass, refusal);
            StringAssert.Contains(detail, ActRgdPath2);
            StringAssert.Contains(detail, unrecognized);
        }
    }

    /// <summary>Two folds of one frame are the same coverage, member for member.</summary>
    [TestMethod]
    public void TwoFoldsOfOneFrameAreIdentical()
    {
        var frame = Frame(
            Entry(ActLoiPath1, LoiClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows),
            Entry(ActRgdPath2, RgdClass, LuxembourgNeverConsolidatedDisposition.NoEnumerationCited),
            Entry(ActLoiPath3, LoiClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredRows),
            Entry(ActAminPath5, AminClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows));

        var first = Fold(frame);
        var second = Fold(frame);

        Assert.AreEqual(first.Describe(), second.Describe());
        CollectionAssert.AreEqual(
            first.Placements.Select(Key).ToArray(), second.Placements.Select(Key).ToArray());
        CollectionAssert.AreEqual(
            first.UnresolvedGaps.Select(static g => $"{g.PublisherActIri}|{g.Scope}|{g.Reason}").ToArray(),
            second.UnresolvedGaps.Select(static g => $"{g.PublisherActIri}|{g.Scope}|{g.Reason}").ToArray());

        static string Key(LuxembourgNeverConsolidatedPlacement p) =>
            $"{p.PublisherActIri}|{p.PublisherClassIri}|{p.Scope}|{p.Membership}";
    }

    [TestMethod]
    public void PlacementForAnswersOnlyForHeldActs()
    {
        var coverage = Fold(Frame(
            Entry(ActLoiPath1, LoiClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows),
            Entry(ActRgdPath2, RgdClass, LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredRows)));

        var held = coverage.PlacementFor(ActRgdPath2)!;
        Assert.AreEqual(ActRgdPath2, held.PublisherActIri);
        Assert.AreEqual(RgdClass, held.PublisherClassIri);
        Assert.AreEqual(LuxembourgNeverConsolidatedMembership.Consolidated, held.Membership);
        Assert.IsNull(coverage.PlacementFor(ActNeverHeld), "never held, never answered.");
        Assert.ThrowsExactly<ArgumentNullException>(() => coverage.PlacementFor(null!));
    }

    /// <summary>
    /// The 23,370 measurement is audit context. No expected, acceptance or measured figure lives on
    /// the coverage, and the fold takes a frame and nothing else - the #584 invariant, kept.
    /// </summary>
    [TestMethod]
    public void NoAcceptanceFigureLivesOnTheSurface()
    {
        var type = typeof(LuxembourgNeverConsolidatedCoverage);

        var literals = type
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(static f => f.IsLiteral || f.IsInitOnly)
            .Select(static f => f.Name)
            .ToArray();
        CollectionAssert.AreEquivalent(Array.Empty<string>(), literals, "no constant figure of any kind.");

        var suspicious = type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(static m => m.Name)
            .Where(static n => new[] { "Expected", "Acceptance", "Measured", "Target", "Baseline", "Audit" }
                .Any(word => n.Contains(word, StringComparison.Ordinal)))
            .ToArray();
        CollectionAssert.AreEquivalent(Array.Empty<string>(), suspicious);

        var fold = type.GetMethod(nameof(LuxembourgNeverConsolidatedCoverage.TryComplete))!;
        CollectionAssert.AreEqual(
            new[] { typeof(LuxembourgNeverConsolidatedFrame) },
            fold.GetParameters().Where(static p => !p.IsOut).Select(static p => p.ParameterType).ToArray(),
            "the fold takes the frame and nothing a caller could set a figure through.");
    }

    [TestMethod]
    public void EveryVocabularyMemberHasItsExactWireToken()
    {
        CollectionAssert.AreEqual(
            new[] { "\"never_consolidated\"", "\"consolidated\"", "\"enumeration_not_cited\"", "\"recognized_out_of_scope\"" },
            Enum.GetValues<LuxembourgNeverConsolidatedMembership>().Select(static m => ContractJson.Serialize(m)).ToArray());
        CollectionAssert.AreEqual(
            new[] { "\"enumeration_not_cited\"" },
            Enum.GetValues<LuxembourgNeverConsolidatedGapReason>().Select(static m => ContractJson.Serialize(m)).ToArray());
        CollectionAssert.AreEqual(
            new[] { "\"none\"", "\"unrecognized_act_class\"" },
            Enum.GetValues<LuxembourgNeverConsolidatedCoverageRefusal>().Select(static m => ContractJson.Serialize(m)).ToArray());
    }

    [TestMethod]
    public void ANullFrameIsACallerContractViolation() =>
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LuxembourgNeverConsolidatedCoverage.TryComplete(null!, out _, out _));

    // ---- fixtures ----

    private static LuxembourgNeverConsolidatedCoverage Fold(LuxembourgNeverConsolidatedFrame frame)
    {
        var coverage = LuxembourgNeverConsolidatedCoverage.TryComplete(frame, out var refusal, out var detail);
        Assert.IsNotNull(coverage, $"the fold must complete: {refusal} {detail}");
        return coverage!;
    }

    private static LuxembourgNeverConsolidatedFrame Frame(params LuxembourgNeverConsolidatedEntry[] entries)
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        foreach (var entry in entries)
        {
            Assert.IsTrue(frame.TryAdmit(entry, out var refusal), $"the fixture must admit: {refusal}");
        }

        return frame;
    }

    private static LuxembourgNeverConsolidatedEntry Entry(
        string act, string classIri, LuxembourgNeverConsolidatedDisposition disposition)
    {
        var key = LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(act);
        var proof = disposition switch
        {
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows => ProofKeyed(key, 0),
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredRows => ProofKeyed(key, 2),
            _ => null,
        };
        return new LuxembourgNeverConsolidatedEntry(act, new LuxembourgActClassRef(classIri), disposition, proof);
    }

    /// <summary>Mints an admitting proof under the given family key. Same recipe as the frame tests.</summary>
    private static AbsenceFamilyEnumerationProof ProofKeyed(string familyKey, int rows)
    {
        var fixture = rows == 0
            ? new RepeatedEnumerationDeliveryProofTests.Fixture(
                terminalPagePolicy: RepeatedEnumerationTerminalPagePolicy.EmptySuccessorAfterShortPage,
                expectedCount: 0,
                rawRows: EmptyRows,
                runIdentitySeed: 930,
                partitionKey: familyKey)
            : new RepeatedEnumerationDeliveryProofTests.Fixture(
                expectedCount: rows, runIdentitySeed: 930, partitionKey: familyKey);

        var cursors = rows == 0
            ? "ignored"
            : string.Join(',', Enumerable.Range(0, rows).Select(static index => ((char)('a' + index)).ToString()));

        var delivery = fixture.Create(cursors, cursors);
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            familyKey, delivery, CustodyMembership.Floored, out var refusal);
        Assert.IsNotNull(proof, $"the fixture must mint an admitting proof: {refusal}");
        return proof!;
    }

    private const string EmptyRows =
        "{\"head\":{\"link\":[],\"vars\":[\"id\",\"cursor\",\"value\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[]}}";
}
