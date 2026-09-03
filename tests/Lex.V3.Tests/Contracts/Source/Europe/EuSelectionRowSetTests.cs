using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The accounted exclusion scope (S8 selector rows).
///
/// The load-bearing property is totality, and it is load-bearing in one direction only. A set with
/// a spurious row is caught by <see cref="EuSelectionDisposition"/> already, because a selector
/// carries the policy the reviewed inventory gave it. A set with a *missing* row is what nothing
/// upstream can catch: it reads as an accounted scope while the classes it omits are silently
/// unexcluded.
/// </summary>
[TestClass]
public sealed class EuSelectionRowSetTests
{
    [TestMethod]
    public void ACompleteSetIsAdmittedAndAnswersForEverySelector()
    {
        var set = EuSelectionRowSet.TryAdmit(AllRows(), out var refusal);
        Assert.IsNotNull(set, $"the complete set was refused as {refusal}");

        var selectors = Enum.GetValues<EuExcludedSelector>();
        Assert.AreEqual(selectors.Length, set.Rows.Count);

        foreach (var selector in selectors)
        {
            var row = set.For(selector);
            Assert.AreEqual(selector, row.Selector);
            Assert.AreEqual(
                EuSelectionDisposition.PolicyFor(selector),
                row.Policy,
                $"{selector} must carry the reviewed policy, not one chosen at the call site");
        }
    }

    [TestMethod]
    public void RowsAreReturnedInTheOrderTheClosedEnumDeclaresThem()
    {
        var set = EuSelectionRowSet.TryAdmit(AllRows().Reverse().ToArray(), out _);
        Assert.IsNotNull(set);

        CollectionAssert.AreEqual(
            Enum.GetValues<EuExcludedSelector>(),
            set.Rows.Select(row => row.Selector).ToArray(),
            "declaration order, so a caller's argument order cannot become the scope order");
    }

    [TestMethod]
    public void AnyMissingSelectorRefusesTheWholeSet()
    {
        // Every single omission, not one sample: the guard must hold for each member rather than
        // for whichever one a fixture happened to drop.
        foreach (var omitted in Enum.GetValues<EuExcludedSelector>())
        {
            var partial = AllRows().Where(row => row.Selector != omitted).ToArray();
            Assert.IsNull(
                EuSelectionRowSet.TryAdmit(partial, out var refusal),
                $"a set missing {omitted} must not be admitted");
            Assert.AreEqual(EuSelectionRowSetRefusal.SelectorUndecided, refusal);
        }

        Assert.IsNull(
            EuSelectionRowSet.TryAdmit(Array.Empty<EuSelectionDisposition>(), out var empty));
        Assert.AreEqual(EuSelectionRowSetRefusal.SelectorUndecided, empty);
    }

    [TestMethod]
    public void ADuplicateSelectorRefusesTheWholeSet()
    {
        var duplicated = AllRows()
            .Append(Row(EuExcludedSelector.WholesaleSector2))
            .ToArray();

        Assert.IsNull(EuSelectionRowSet.TryAdmit(duplicated, out var refusal));
        Assert.AreEqual(EuSelectionRowSetRefusal.DuplicateSelector, refusal);
    }

    [TestMethod]
    public void ATotalSetTracksTheEnumRatherThanAWrittenCount()
    {
        // If totality were pinned to a literal, adding a thirteenth selector would leave every
        // previously complete set admitted with an undecided class in it. Asserting against
        // Enum.GetValues is what makes the new member fail closed instead.
        var set = EuSelectionRowSet.TryAdmit(AllRows(), out _);
        Assert.IsNotNull(set);
        Assert.AreEqual(Enum.GetValues<EuExcludedSelector>().Length, set.Rows.Count);

        var source = typeof(EuSelectionRowSet)
            .GetMethod(nameof(EuSelectionRowSet.TryAdmit), BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(source, "TryAdmit is the only admission path and must stay public static");
    }

    [TestMethod]
    public void TheSetHasExactlyOneConstructionPath()
    {
        var type = typeof(EuSelectionRowSet);

        Assert.AreEqual(
            0,
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length,
            "a public constructor would let a caller mint a scope that was never total");

        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == type
                || (method.ReturnType.IsByRef && method.ReturnType.GetElementType() == type))
            .Select(method => $"{(method.IsStatic ? "static" : "instance")} {method}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "static Lex.V3.Contracts.Source.Europe.EuSelectionRowSet TryAdmit"
                + "(System.Collections.Generic.IReadOnlyList`1[Lex.V3.Contracts.Source.Europe."
                + "EuSelectionDisposition], Lex.V3.Contracts.Source.Europe."
                + "EuSelectionRowSetRefusal ByRef)",
            },
            factories);

        Assert.AreEqual(
            0,
            type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length,
            "a public field is a construction surface too");
    }

    [TestMethod]
    public void TheRefusalVocabularyIsClosedAndSpelledForTheWire()
    {
        var tokens = Enum.GetValues<EuSelectionRowSetRefusal>()
            .Select(value => ContractJson.Serialize(value))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "\"duplicate_selector\"", "\"selector_undecided\"" },
            tokens);
    }

    private static EuSelectionDisposition[] AllRows() =>
        Enum.GetValues<EuExcludedSelector>().Select(Row).ToArray();

    private static EuSelectionDisposition Row(EuExcludedSelector selector) =>
        new(
            selector,
            EuSelectionDisposition.PolicyFor(selector),
            "reason_" + selector.ToString().ToLowerInvariant(),
            "rule_" + selector.ToString().ToLowerInvariant(),
            new SourceArtifactRef(
                "urn:uuid:00000000-0000-4000-8000-0000000000cc", new string('c', 64)));
}
