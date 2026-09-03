using System.Reflection;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The structural partition cover: a chain built only by splitting a leaf at one interior cursor,
/// so a gap, an overlap or a short anchor is unrepresentable rather than merely checked.
/// </summary>
[TestClass]
public sealed class LuxembourgPartitionChainTests
{
    [TestMethod]
    public void TheChainHasNoConstructorTakingAListOfRanges()
    {
        Assert.AreEqual(0, typeof(LuxembourgPartitionChain).GetConstructors().Length);

        var factories = typeof(LuxembourgPartitionChain).GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .Where(static method => !method.IsSpecialName)
            .ToArray();
        CollectionAssert.AreEquivalent(new[] { "Root" }, factories.Select(static m => m.Name).ToArray());

        var everyPublicMember = typeof(LuxembourgPartitionChain)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Where(static method => !method.IsSpecialName);
        foreach (var method in everyPublicMember)
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.IsFalse(
                    typeof(System.Collections.IEnumerable).IsAssignableFrom(parameter.ParameterType) &&
                    parameter.ParameterType != typeof(string),
                    $"{method.Name}({parameter.Name}) accepts a list-shaped input, which is exactly " +
                    "the surface a gap or overlap check would need.");
            }
        }
    }

    [TestMethod]
    public void SplitBuildsTheBoundaryFromOneSharedCursorObject()
    {
        var chain = LuxembourgPartitionChain.Root(Range("root", "a", "z"));
        var boundary = Cursor("m");

        var split = chain.SplitLeaf("root", boundary, "left", "right");

        Assert.AreEqual(2, split.Leaves.Count);
        Assert.AreSame(split.Leaves[0].EndExclusive, split.Leaves[1].StartInclusive);
        Assert.AreSame(boundary, split.Leaves[0].EndExclusive);
        Assert.AreEqual("left", split.Leaves[0].PartitionId);
        Assert.AreEqual("right", split.Leaves[1].PartitionId);
        Assert.AreSame(chain.RootRange.StartInclusive, split.Leaves[0].StartInclusive);
        Assert.AreSame(chain.RootRange.EndExclusive, split.Leaves[1].EndExclusive);

        // The original chain is untouched: SplitLeaf returns a new chain rather than mutating.
        Assert.AreEqual(1, chain.Leaves.Count);
    }

    [TestMethod]
    public void SplittingTheRightLeafOfAPriorSplitBuildsAThreeLeafChain()
    {
        var chain = LuxembourgPartitionChain.Root(Range("root", "a", "z"))
            .SplitLeaf("root", Cursor("m"), "left", "right");
        var deeper = chain.SplitLeaf("right", Cursor("t"), "right-left", "right-right");

        Assert.AreEqual(3, deeper.Leaves.Count);
        CollectionAssert.AreEqual(
            new[] { "left", "right-left", "right-right" },
            deeper.Leaves.Select(static leaf => leaf.PartitionId).ToArray());
        Assert.AreSame(deeper.Leaves[0].EndExclusive, deeper.Leaves[1].StartInclusive);
        Assert.AreSame(deeper.Leaves[1].EndExclusive, deeper.Leaves[2].StartInclusive);
    }

    [TestMethod]
    [DataRow("a", DisplayName = "at the start boundary")]
    [DataRow("z", DisplayName = "at the end boundary")]
    [DataRow("zz", DisplayName = "past the end boundary")]
    public void ABoundaryNotStrictlyInsideTheLeafIsRefused(string boundary)
    {
        var chain = LuxembourgPartitionChain.Root(Range("root", "a", "z"));
        Assert.ThrowsExactly<ArgumentException>(
            () => chain.SplitLeaf("root", Cursor(boundary), "left", "right"));
    }

    [TestMethod]
    public void SplittingAnUnknownLeafIsRefused()
    {
        var chain = LuxembourgPartitionChain.Root(Range("root", "a", "z"));
        Assert.ThrowsExactly<ArgumentException>(
            () => chain.SplitLeaf("not-a-leaf", Cursor("m"), "left", "right"));
    }

    [TestMethod]
    [DataRow("Left", DisplayName = "uppercase")]
    [DataRow("le ft", DisplayName = "embedded space")]
    [DataRow("", DisplayName = "empty")]
    public void APartitionIdThatIsNotAMachineMemberKeyIsRefusedBeforeConstruction(string id)
    {
        var chain = LuxembourgPartitionChain.Root(Range("root", "a", "z"));
        Assert.ThrowsExactly<ArgumentException>(
            () => chain.SplitLeaf("root", Cursor("m"), id, "right"));
    }

    [TestMethod]
    public void DuplicateChildPartitionIdsAreRefused()
    {
        var chain = LuxembourgPartitionChain.Root(Range("root", "a", "z"));
        Assert.ThrowsExactly<ArgumentException>(
            () => chain.SplitLeaf("root", Cursor("m"), "left", "left"));
    }

    [TestMethod]
    public void AReusedPartitionIdElsewhereInTheChainIsRefused()
    {
        var chain = LuxembourgPartitionChain.Root(Range("root", "a", "z"))
            .SplitLeaf("root", Cursor("m"), "left", "right");
        Assert.ThrowsExactly<ArgumentException>(
            () => chain.SplitLeaf("right", Cursor("t"), "left", "right-right"));
    }

    private static LuxembourgQueryPartitionRange Range(string id, string start, string end) =>
        new(id, Cursor(start), Cursor(end));

    private static LuxembourgQueryCursor Cursor(string key1) => new(key1, "", "", "", "", "");
}
