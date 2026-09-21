using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The retained EU population report's limitations, checked against the code they describe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="EuStageOnePopulationRun"/> writes its limitations into the
/// retained report deliberately, so the sentences travel with the numbers a reviewer quotes. That
/// makes them evidence, and <b>nothing checked them against the run</b>. One of them drifted: it
/// said the scope-reduction resolver was a test double admitting on SHA-256 shape alone and that no
/// seed's reduction step was proven, while the double it described was declared and never
/// constructed and the adapter had been building the production resolver for some time. A reader
/// quoting the report understated what the run established.
/// </para>
/// <para>
/// <b>What this does and does not cover.</b> It pins the fact the corrected sentence rests on --
/// that <b>no door of the adapter takes a resolver</b>, so the production one it builds for itself
/// is the only one any run can use -- and that the limitation names the residue rather than a
/// double. It also pins that the run owns no resolver, which is the smaller of the two statements:
/// dead code beside a closed door rather than the door itself. <b>It deliberately does not re-prove the
/// residue</b>: that the three digests are still admitted on shape alone is already established
/// behaviourally by
/// <see cref="EuProductionScopeReductionEvidenceResolverTests.ObjectOnlyBindingsAndCompleteEnumerationUseTheRunDerivedIdentitySet"/>,
/// which admits a rule-evaluation binding carrying fabricated but well-formed digests. A second
/// walk over the same ground would agree with the first and guard nothing.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuPopulationReportClaimsTests
{
    /// <summary>
    /// The three bindings the production resolver still admits on shape alone. Named here, so the
    /// limitation cannot quietly drop one and stay green.
    /// </summary>
    private static readonly string[] DigestsStillAdmittedOnShape =
        ["RuleEvaluationSha256", "SelectorEvidenceSha256", "SelectorSetSha256"];

    [TestMethod]
    public void ThePopulationRunDeclaresNoScopeReductionResolverOfItsOwn()
    {
        var owned = typeof(EuStageOnePopulationRun)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static type => typeof(IScopeReductionEvidenceResolver).IsAssignableFrom(type))
            .Select(static type => type.Name)
            .ToArray();

        Assert.IsEmpty(
            owned,
            "The population run declares its own scope-reduction evidence resolver. "
            + "EuQueryExecutionAdapter.RunAsync takes no resolver parameter and builds the "
            + "production one from the real custody store, so a resolver here is either dead code "
            + "whose limitation text outlives it - which is exactly how this report came to "
            + "understate the run - or a double quietly admitting what production would refuse.");
    }

    /// <summary>
    /// The fact the corrected limitation stands on: <b>no door of the adapter takes a scope-reduction
    /// resolver</b>, so the production one it builds for itself is the only one any run can use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the guard that was missing, and a reviewer measured what its absence cost.</b> An
    /// earlier version of this file pinned only that the population run declares no resolver of its
    /// own. That is the wrong end of the problem: <b>a double the adapter cannot accept is dead code
    /// and harmless — the thing to fence is a door that accepts one.</b> Adding
    /// <c>IScopeReductionEvidenceResolver? = null</c> to <c>RunAsync</c>, which the Luxembourg
    /// adapter's test-only overload already has, left every test here green while the retained
    /// limitation quietly became false.
    /// </para>
    /// <para>
    /// Every visibility, and constructors as well as methods, because a door is a door whatever it is
    /// marked and whatever the assembly's friends can reach.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void NoDoorOfTheAdapterTakesAScopeReductionResolver()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var adapter = typeof(EuQueryExecutionAdapter);

        var doors = adapter.GetMethods(All).Cast<MethodBase>()
            .Concat(adapter.GetConstructors(All))
            .Where(static door => door.GetParameters().Any(static parameter =>
                CarriesAResolver(parameter.ParameterType)))
            .Select(static door => door.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(
            doors,
            "A door of EuQueryExecutionAdapter takes a scope-reduction evidence resolver. The "
            + "retained population report says the run uses the production resolver the adapter "
            + "builds from the real custody store; a parameter that accepts one makes that sentence "
            + "false for any caller that passes a double, and every other test here stays green.");
    }

    /// <summary>
    /// Whether a parameter type is, or carries, a scope-reduction resolver.
    /// </summary>
    /// <remarks>
    /// Recursive because <c>IsAssignableFrom</c> alone does not see one inside a delegate, an array or
    /// a collection: a reviewer showed a <c>Func&lt;IScopeReductionEvidenceResolver&gt;</c> parameter
    /// walking past the guard. They did not ask for this - it takes a deliberate second door to use -
    /// but the test was open in front of me and a door is a door however the resolver is wrapped.
    /// </remarks>
    private static bool CarriesAResolver(Type type) =>
        typeof(IScopeReductionEvidenceResolver).IsAssignableFrom(type)
        || (type.HasElementType && CarriesAResolver(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(CarriesAResolver));

    [TestMethod]
    public void TheReductionLimitationNamesTheResidueAndNoLongerClaimsATestDouble()
    {
        var reduction = EuStageOnePopulationRun.PopulationLimitations()
            .OfType<JsonObject>()
            .Where(entry => entry["limitation"]!.GetValue<string>()
                .StartsWith("reductionStep", StringComparison.Ordinal))
            .ToArray();

        Assert.HasCount(1, reduction, "the report must carry exactly one reduction-step limitation");
        var why = reduction[0]["why"]!.GetValue<string>();

        foreach (var digest in DigestsStillAdmittedOnShape)
        {
            StringAssert.Contains(
                why, digest,
                $"The reduction limitation no longer names {digest}. It is still admitted on shape "
                + "alone, so a report that stops naming it claims more than the resolver does.");
        }

        // NOT DoesNotContain. A reviewer proved the previous form could never fail: a StringComparer
        // fits none of MSTest's substring overloads, so the call bound to
        // DoesNotContain(object, IEnumerable, IEqualityComparer, ...), which asks whether any CHAR of
        // `why` equals the STRING "test double". No char does, for any input. The tell was in the
        // text it guarded: this very limitation quotes TEST DOUBLE when it says what it corrects, so
        // a working check would have gone red here and it stayed green.
        //
        // The phrase is legitimately present exactly once, quoting the claim being corrected, so the
        // property is not absence: it is that the only occurrence is the quoted one.
        const string phrase = "test double";
        const string quoting = "an earlier version of this report said";

        var occurrences = 0;
        for (var at = why.IndexOf(phrase, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = why.IndexOf(phrase, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            occurrences++;
        }

        Assert.AreEqual(
            1, occurrences,
            $"The reduction limitation names a '{phrase}' {occurrences} times. It may say so exactly "
            + "once, quoting the claim it corrects; a second mention is the report calling this run's "
            + "resolver a double again, which understates what every seed established.");

        var quoted = why.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
        var said = why.IndexOf(quoting, StringComparison.Ordinal);
        Assert.IsTrue(
            said >= 0 && said < quoted,
            "The limitation's one mention of a test double is not the quoted one. It is allowed only "
            + $"after '{quoting}', where the report is stating what it no longer claims.");

        StringAssert.Contains(
            why, "EuProductionScopeReductionEvidenceResolver",
            "The limitation must name the resolver the run actually uses. Pinning the absence of the "
            + "wrong sentence without pinning the presence of the right one guards half the property.");
    }
}
