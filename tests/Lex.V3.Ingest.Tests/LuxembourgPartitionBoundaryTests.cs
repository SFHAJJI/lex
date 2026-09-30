using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPartitionBoundaryTests
{
    [TestMethod]
    public void BoundariesStayStrictlyInsideUnicodeAndCompositeRanges()
    {
        string[] values = ["", "\0", "\0a", "A", "AA", "AB", "http://a", "http://b",
            "é", "\ud7ff", "\ue000", "\uffff", "😀", "\U0010ffff", new string('z', 2047)];
        foreach (var a in values)
        foreach (var b in values)
        for (var component = 0; component < 6; component++)
        {
            var lower = Cursor(a, component);
            var upper = Cursor(b, component);
            if (lower.CompareTo(upper) >= 0) continue;
            var range = new LuxembourgQueryPartitionRange("root", lower, upper);
            var boundary = LuxembourgPartitionBoundary.Between(range);
            if (boundary is null)
            {
                Assert.AreEqual(5, component, "earlier components always leave later tuple space");
                Assert.AreEqual(a + "\0", b, "only adjacent final strings lack an interior cursor");
                continue;
            }
            Assert.IsTrue(boundary.CompareTo(lower) > 0 && boundary.CompareTo(upper) < 0);
            var split = LuxembourgPartitionChain.Root(range).SplitLeaf("root", boundary, "left", "right");
            Assert.AreSame(split.Leaves[0].EndExclusive, split.Leaves[1].StartInclusive);
        }
    }

    [TestMethod]
    public void RepeatedCutsSeparateKeysWithLongSharedPrefixes()
    {
        var a = Cursor("http://data.legilux.public.lu/eli/etat/leg/loi/2025/a", 0);
        var b = Cursor("http://data.legilux.public.lu/eli/etat/leg/loi/2026/a", 0);
        var range = new LuxembourgQueryPartitionRange("root", Cursor("A", 0), Cursor("\uffff", 0));
        for (var i = 0; i < 600; i++)
        {
            var middle = LuxembourgPartitionBoundary.Between(range);
            Assert.IsNotNull(middle);
            if (a.CompareTo(middle) < 0 && b.CompareTo(middle) >= 0) return;
            range = a.CompareTo(middle) >= 0
                ? new("root", middle, range.EndExclusive)
                : new("root", range.StartInclusive, middle);
        }
        Assert.Fail("Cuts must advance through a shared prefix, not append forever below it.");
    }

    private static LuxembourgQueryCursor Cursor(string value, int component)
    {
        var parts = Enumerable.Repeat("", 6).ToArray();
        for (var i = 0; i < component; i++) parts[i] = "same";
        parts[component] = value;
        return new(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
    }
}
