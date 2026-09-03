using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The tie-safe watermark cursor and the boundary crossing it makes checkable.
///
/// The failure this exists to stop is silent in both directions. Paging on a strictly greater date
/// steps past every entry sharing the boundary second, dropping law that changed. Paging on greater
/// or equal re-delivers them, inflating a count that a completeness check then reconciles against
/// itself. Neither reports anything.
/// </summary>
[TestClass]
public sealed class EuWatermarkCursorTests
{
    private const string Boundary = "2026-08-31T09:15:00Z";
    private const string Later = "2026-08-31T09:15:01Z";

    [TestMethod]
    public void ACursorCannotBeOpenedOnAWatermarkAlone()
    {
        // R3 forbids a date-only cursor by name; the refusal says so rather than blaming the input.
        Assert.IsNull(EuWatermarkCursor.TryOpen(Boundary, "", out var dateOnly));
        Assert.AreEqual(EuWatermarkRefusal.DateOnlyCursor, dateOnly);

        Assert.IsNull(EuWatermarkCursor.TryOpen("", "cellar:a", out var absent));
        Assert.AreEqual(EuWatermarkRefusal.WatermarkAbsent, absent);

        var cursor = EuWatermarkCursor.TryOpen(Boundary, "cellar:a", out var refusal);
        Assert.IsNotNull(cursor);
        Assert.AreEqual(EuWatermarkRefusal.None, refusal);
    }

    [TestMethod]
    public void OrderingIsOnTheTupleAndNotOnTheWatermarkAlone()
    {
        var first = Cursor(Boundary, "cellar:a");
        var second = Cursor(Boundary, "cellar:b");

        Assert.IsTrue(first.CompareTo(second) < 0, "same watermark orders by entry key");
        Assert.IsTrue(second.CompareTo(first) > 0);
        Assert.AreEqual(0, first.CompareTo(Cursor(Boundary, "cellar:a")));
        Assert.IsTrue(Cursor(Later, "cellar:a").CompareTo(second) > 0, "watermark dominates");
    }

    [TestMethod]
    public void ATieGroupIsCarriedAcrossTheBoundaryExactlyOnce()
    {
        // The earlier page emitted a and b at the boundary; the reread shows a, b and c, so c is
        // the part of the tie group the earlier page had not reached.
        var crossing = EuBoundaryCrossing.TryCross(
            Cursor(Boundary, "cellar:b"),
            ["cellar:a", "cellar:b"],
            ["cellar:a", "cellar:b", "cellar:c"],
            Cursor(Later, "cellar:z"),
            out var refusal);

        Assert.IsNotNull(crossing, $"refused as {refusal}");
        Assert.AreEqual(EuWatermarkRefusal.None, refusal);
        CollectionAssert.AreEqual(new[] { "cellar:c" }, crossing.CarriedForward.ToArray());
        CollectionAssert.AreEqual(new[] { "cellar:a", "cellar:b" }, crossing.RetainedTieSet.ToArray());
    }

    [TestMethod]
    public void AnEntryTheRereadNoLongerShowsIsASkip()
    {
        // b was emitted at the boundary and the next window does not contain it. That is exactly
        // the entry a strictly-greater date cursor loses, and it is now a typed refusal.
        Assert.IsNull(EuBoundaryCrossing.TryCross(
            Cursor(Boundary, "cellar:b"),
            ["cellar:a", "cellar:b"],
            ["cellar:a", "cellar:c"],
            Cursor(Later, "cellar:z"),
            out var refusal));
        Assert.AreEqual(EuWatermarkRefusal.BoundaryEntrySkipped, refusal);
    }

    [TestMethod]
    public void AnEntryDeliveredTwiceIsRefusedFromEitherSide()
    {
        Assert.IsNull(EuBoundaryCrossing.TryCross(
            Cursor(Boundary, "cellar:a"),
            ["cellar:a"],
            ["cellar:a", "cellar:a"],
            null,
            out var withinReread));
        Assert.AreEqual(EuWatermarkRefusal.BoundaryEntryDuplicated, withinReread);

        Assert.IsNull(EuBoundaryCrossing.TryCross(
            Cursor(Boundary, "cellar:a"),
            ["cellar:a", "cellar:a"],
            ["cellar:a"],
            null,
            out var withinRetained));
        Assert.AreEqual(EuWatermarkRefusal.BoundaryEntryDuplicated, withinRetained);
    }

    [TestMethod]
    public void ANextPageThatDoesNotAdvanceIsRefused()
    {
        // Same watermark and an entry key at or below the cursor is not progress. A date-only
        // comparison reads this as equal and pages forever.
        Assert.IsNull(EuBoundaryCrossing.TryCross(
            Cursor(Boundary, "cellar:b"),
            ["cellar:b"],
            ["cellar:b"],
            Cursor(Boundary, "cellar:a"),
            out var backwards));
        Assert.AreEqual(EuWatermarkRefusal.PageNotOrderedAfterCursor, backwards);

        Assert.IsNull(EuBoundaryCrossing.TryCross(
            Cursor(Boundary, "cellar:b"),
            ["cellar:b"],
            ["cellar:b"],
            Cursor(Boundary, "cellar:b"),
            out var same));
        Assert.AreEqual(EuWatermarkRefusal.PageNotOrderedAfterCursor, same);

        // And a page that legitimately ends the traversal supplies no next position.
        Assert.IsNotNull(EuBoundaryCrossing.TryCross(
            Cursor(Boundary, "cellar:b"), ["cellar:b"], ["cellar:b"], null, out _));
    }

    [TestMethod]
    public void TheRefusalVocabularyIsClosedAndSpelledForTheWire()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"", "\"date_only_cursor\"", "\"watermark_absent\"",
                "\"boundary_entry_skipped\"", "\"boundary_entry_duplicated\"",
                "\"page_not_ordered_after_cursor\"",
            },
            Enum.GetValues<EuWatermarkRefusal>().Select(ContractJson.Serialize).ToArray());
    }

    [TestMethod]
    public void BothTypesHaveExactlyOneConstructionPathEach()
    {
        AssertClosed(typeof(EuWatermarkCursor), "static TryOpen");
        AssertClosed(typeof(EuBoundaryCrossing), "static TryCross");
    }

    private static void AssertClosed(Type type, params string[] expected)
    {
        var constructors = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsTrue(constructors.Length > 0);
        Assert.IsTrue(constructors.All(c => c.IsPrivate), $"{type.Name} constructors must be private");

        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == type
                || (m.ReturnType.IsByRef && m.ReturnType.GetElementType() == type)
                || m.GetParameters().Any(p => p.ParameterType.IsByRef
                    && p.ParameterType.GetElementType() == type))
            .Select(m => $"{(m.IsStatic ? "static" : "instance")} {m.Name}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), factories);
    }

    private static EuWatermarkCursor Cursor(string watermark, string key)
    {
        var cursor = EuWatermarkCursor.TryOpen(watermark, key, out var refusal);
        Assert.IsNotNull(cursor, $"refused as {refusal}");
        return cursor;
    }
}
