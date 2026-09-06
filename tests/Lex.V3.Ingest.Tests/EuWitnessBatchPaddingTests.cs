using System;
using System.Collections.Generic;
using System.Linq;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The witness batch asks about each of its members exactly once, whatever the batch's size.
/// </summary>
/// <remarks>
/// <para>
/// PROVENANCE, because it decides how much these tests are worth. This was not found by reading.
/// The complete 82-seed population run of S1-A09 measured it against the live endpoint on
/// 2026-09-06: Appendix A's treaty seeds refused <c>witness_traversal_refused</c> with
/// <c>PageNotStrictlyAscending</c>, and the retained page under the run's own custody root carried
/// the same row FIFTY TIMES. <see cref="EuStageOneAcquisitionCanary"/> passes over this at both its
/// own scale and its own seeds, which is why nothing held it before.
/// </para>
/// <para>
/// THE MECHANISM, stated once here and not repeated in each test.
/// <c>EuWatermarkWitnessPlan</c> renders a fixed template with
/// <see cref="EuWatermarkWitnessPlan.BatchCapacity"/> VALUES slots, and a batch shorter than that
/// used to fill the unused slots by repeating its greatest member. SPARQL defines VALUES as a
/// multiset, and this endpoint honours that: the repeated subject multiplies every solution it
/// matches. Families P, X, W and M pad identically and never noticed, because each of their rows
/// queries ends in a GROUP BY that collapses the duplication before the caller sees it. The witness
/// template groups nothing, so it inherited the mechanism without the protection.
/// </para>
/// <para>
/// WHY A TREATY SEED IS WHERE IT FIRES FIRST. The six treaty seeds have no consolidated states, so
/// the batch holds exactly ONE object. That object is then both the greatest member, repeated into
/// all 49 unused slots, AND the object the bootstrap bound sits on, so the boundary filter admits
/// every copy rather than discarding it as it does for a padded member below the bound. That
/// coincidence is why eight objects across two seeds never expressed the condition.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuWitnessBatchPaddingTests
{
    /// <summary>
    /// A one-member batch names its member once and fills the rest with the padding sentinel.
    /// </summary>
    /// <remarks>
    /// This is the direct form of the defect and is red under the repeated-member padding, where
    /// the single member occupies all fifty slots. Asserted on the RENDERED QUERY as well as on the
    /// plan's own entry list, because the rendered bytes are what the endpoint multiplies and a
    /// plan that merely recorded the right list while rendering the wrong text would still fail
    /// against the publisher.
    /// </remarks>
    [TestMethod]
    public void AOneMemberBatchAsksAboutItsMemberOnceAndPadsTheRestWithTheSentinel()
    {
        var root = Root(0);
        var plan = Freeze([root], Cursor(BoundaryWatermark, root));

        Assert.HasCount(
            EuWatermarkWitnessPlan.BatchCapacity,
            plan.PaddedEntries,
            "the rendered query has a fixed slot count, so the entry list must fill every slot.");
        Assert.AreEqual(
            1,
            plan.PaddedEntries.Count(entry => string.Equals(entry, root, StringComparison.Ordinal)),
            "the batch's own member occupies more than one VALUES slot. SPARQL treats VALUES as a "
                + "multiset and this endpoint honours that, so every extra slot multiplies the "
                + "rows the publisher returns for it.");
        Assert.AreEqual(
            EuWatermarkWitnessPlan.BatchCapacity - 1,
            plan.PaddedEntries.Count(entry => string.Equals(
                entry, EuWatermarkWitnessPlan.BatchPaddingEntryIri, StringComparison.Ordinal)),
            "every unused slot must carry the padding sentinel.");

        var rendered = plan.RenderForWatermarkLexical(
            BoundaryWatermark, plan.PaddedEntries, out var renderRefusal)
            ?? throw new InvalidOperationException($"the fixture plan refused to render as {renderRefusal}.");
        Assert.AreEqual(
            1,
            Occurrences(rendered, "<" + root + ">"),
            "the rendered query names the batch's own member more than once, which is what the "
                + "endpoint multiplied.");
    }

    /// <summary>
    /// A batch at full capacity carries no sentinel and still names each member exactly once.
    /// </summary>
    /// <remarks>
    /// The pair to the test above, and it is what stops the fix being written as "always emit the
    /// sentinel". A full batch has no unused slot, so a sentinel appearing in one would mean a real
    /// member had been dropped in favour of a subject the endpoint knows nothing about, which is
    /// silent under-observation rather than a loud refusal.
    /// </remarks>
    [TestMethod]
    public void AFullBatchCarriesNoSentinelAndKeepsEveryMember()
    {
        var roots = Enumerable.Range(0, EuWatermarkWitnessPlan.BatchCapacity)
            .Select(Root)
            .ToArray();
        var plan = Freeze(roots, Cursor(BoundaryWatermark, roots[0]));

        Assert.AreEqual(
            0,
            plan.PaddedEntries.Count(entry => string.Equals(
                entry, EuWatermarkWitnessPlan.BatchPaddingEntryIri, StringComparison.Ordinal)),
            "a batch at capacity has no unused slot, so a sentinel here means a real member was "
                + "dropped and that object is silently never asked about.");
        CollectionAssert.AreEquivalent(
            roots,
            plan.PaddedEntries.ToArray(),
            "a full batch must carry exactly its own members.");
    }

    /// <summary>
    /// The padding sentinel is not, and cannot become, one of Appendix A's own 82 roots.
    /// </summary>
    /// <remarks>
    /// A sentinel colliding with a real pack root would silently drop that root from every short
    /// batch it was padded into, which is the same under-observation the test above guards, arriving
    /// by a different door. Checked against the pack rather than asserted about the constant's
    /// spelling.
    /// </remarks>
    [TestMethod]
    public void ThePaddingSentinelIsNotAPackRoot()
    {
        Assert.DoesNotContain(
            EuWatermarkWitnessPlan.BatchPaddingEntryIri,
            EuAppendixASeedMap.PackRoots,
            "the padding sentinel is one of Appendix A's own roots, so padding a short batch would "
                + "drop that root from the batch it belongs to.");
        Assert.IsNull(
            EuPackRootCanonicalForm.TryCanonicalize(
                EuWatermarkWitnessPlan.BatchPaddingEntryIri, out _),
            "the padding sentinel canonicalizes as a pack root, so something downstream could take "
                + "it for a publisher object rather than a slot filler.");
    }

    /// <summary>
    /// The strictly-ascending page check still refuses a genuinely duplicated page.
    /// </summary>
    /// <remarks>
    /// The fix removes duplication THIS CODE injected; it must not blunt the guard against
    /// duplication anyone else injects. That is the whole reason the repair binds an inert subject
    /// rather than adding a GROUP BY to the witness template: grouping would have collapsed a
    /// duplicate the PUBLISHER sent, and telling those apart is exactly this check's job. The page
    /// scripted here is the shape the live endpoint really returned for a treaty seed, reduced to
    /// the two adjacent identical rows the check compares.
    /// </remarks>
    [TestMethod]
    public async System.Threading.Tasks.Task ADuplicatedPageIsStillRefusedRatherThanCountedTwice()
    {
        var root = Root(0);
        var duplicated = EuAcquisitionTestFixture.WitnessRowsJson(
        [
            EuAcquisitionTestFixture.WitnessRow(root, BoundaryWatermark),
            EuAcquisitionTestFixture.WitnessRow(root, BoundaryWatermark),
        ]);
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript("Witness", [duplicated, duplicated]),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await executor.RunWitnessTraversalAsync(
            [Freeze([root], Cursor(BoundaryWatermark, root))],
            EuAcquisitionTestFixture.BuildRendererSource(8301),
            EuAcquisitionTestFixture.SourceWitness(),
            System.Threading.CancellationToken.None);

        Assert.IsNotNull(
            result.Refusal,
            "a page delivering one entry twice must refuse. If this passes, the traversal counted "
                + "the same entry twice and the witness would report movement that never happened.");
        Assert.IsNull(result.Entries, "a refused traversal carries no canonical entry set.");
        Assert.Contains(
            nameof(EuWatermarkStepRefusal.PageNotStrictlyAscending),
            result.Refusal!.Detail ?? string.Empty,
            "the refusal must name the duplicated page rather than some later consequence of it. "
                + $"It said: {result.Refusal.Detail}");
    }

    /// <summary>
    /// The exact watermark the live treaty-seed run bound its boundary to, kept as the fixture's
    /// own so the scripted rows are the shape the publisher really served.
    /// </summary>
    private const string BoundaryWatermark = "2023-03-03T20:43:13.158+01:00";

    private static string Root(int index) =>
        EuPackRootCanonicalForm.TryCanonicalize(
            EuAppendixASeedMap.SeedsInCelexOrder[index].WorkRoot, out _)!;

    private static EuWatermarkCursor Cursor(string watermarkLexical, string entryKey) =>
        EuWatermarkCursor.TryOpen(watermarkLexical, entryKey, out var refusal)
        ?? throw new InvalidOperationException($"the fixture cursor refused as {refusal}.");

    private static EuWatermarkWitnessPlan Freeze(
        IReadOnlyList<string> batch, EuWatermarkCursor start) =>
        EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.SortedResultWindowRows,
            start,
            batch,
            out var refusal)
        ?? throw new InvalidOperationException($"the fixture batch refused as {refusal}.");

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
