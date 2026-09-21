using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Source.Scope;

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
/// <b>What this does and does not cover.</b> It pins that the run owns no resolver and that the
/// limitation names the residue rather than a double. <b>It deliberately does not re-prove the
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

        Assert.DoesNotContain(
            "TEST DOUBLE", why, StringComparer.Ordinal,
            "The reduction limitation calls the resolver a test double. The run constructs none; "
            + "saying so understates what every seed established.");
    }
}
