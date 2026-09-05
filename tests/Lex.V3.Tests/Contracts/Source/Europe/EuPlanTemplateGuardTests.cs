using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// One guard over EVERY query template this source sends, closing the eager-IF class repository
/// wide rather than closing the instances we happened to find.
/// </summary>
/// <remarks>
/// <para>
/// The first instance was found by a live probe, the second by reading. A third would have been
/// found by neither. RULING lex-event-20260905T020043766Z-cd0db29d887b4d86b5c44da66d82e2f7 and its
/// fold-in.
/// </para>
/// <para>
/// WHY THE FORM IS BANNED OUTRIGHT rather than reviewed case by case.
/// <c>IF(BOUND(?x), STR(?x), "")</c> reads as a guard and is correct under SPARQL's own lazy IF,
/// but the publisher's engine SELECTS IF's branch correctly and EVALUATES ITS ARGUMENTS EAGERLY, so
/// <c>STR</c> on the unbound term raises anyway, the erroring BIND leaves the variable unbound, and
/// the JSON results format then omits it from the binding entirely. The guard looks present and
/// does nothing. <c>COALESCE</c> is specified to swallow an erroring argument and take the next,
/// and the same probe measured it working over the same batch.
/// </para>
/// <para>
/// This scans the TEMPLATE STRINGS the plans actually carry rather than source files, because the
/// template is what gets sent: a form reintroduced through a helper, a constant or a different file
/// is still caught. The failure names the plan and the query set, which is what a reader needs to
/// find it.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuPlanTemplateGuardTests
{
    /// <summary>
    /// The banned form, matched on the BIND that carries it so this file's own prose describing the
    /// form does not match itself and neither does a doc comment elsewhere.
    /// </summary>
    private const string EagerGuardForm = "BIND(IF(BOUND(";

    [TestMethod]
    public void NoEuQueryTemplateUsesTheEagerBoundGuardThatThisEngineIgnores()
    {
        var offenders = new List<string>();

        var objectFacts = EuObjectFactsDiscoveryPlan.Create();
        foreach (var set in Enum.GetValues<EuObjectFactsQuerySet>())
        {
            var definition = objectFacts.Definition(set);
            Inspect($"EuObjectFactsDiscoveryPlan.{set}.CountTemplate", definition.CountTemplate);
            Inspect($"EuObjectFactsDiscoveryPlan.{set}.PageTemplate", definition.PageTemplate);
        }

        var consolidation = EuConsolidationDiscoveryPlan.Create();
        foreach (var set in Enum.GetValues<EuConsolidationQuerySet>())
        {
            var definition = consolidation.Definition(set);
            Inspect($"EuConsolidationDiscoveryPlan.{set}.CountTemplate", definition.CountTemplate);
            Inspect($"EuConsolidationDiscoveryPlan.{set}.PageTemplate", definition.PageTemplate);
        }

        Assert.IsEmpty(
            offenders,
            "these templates carry a guard this publisher's engine ignores, so the variable they "
            + "claim to make total can still come back unbound: "
            + string.Join("; ", offenders));

        void Inspect(string name, string template)
        {
            if (template.Contains(EagerGuardForm, StringComparison.Ordinal))
            {
                offenders.Add(name);
            }
        }
    }

    /// <summary>
    /// The guard above is only meaningful if the templates it scans are the real ones, so this
    /// asserts the scan actually reached query text rather than empty strings.
    /// </summary>
    [TestMethod]
    public void TheTemplateScanReachesRealQueryText()
    {
        var objectFacts = EuObjectFactsDiscoveryPlan.Create();
        var consolidation = EuConsolidationDiscoveryPlan.Create();

        var templates = Enum.GetValues<EuObjectFactsQuerySet>()
            .SelectMany(set => new[]
            {
                objectFacts.Definition(set).CountTemplate,
                objectFacts.Definition(set).PageTemplate,
            })
            .Concat(Enum.GetValues<EuConsolidationQuerySet>()
                .SelectMany(set => new[]
                {
                    consolidation.Definition(set).CountTemplate,
                    consolidation.Definition(set).PageTemplate,
                }))
            .ToArray();

        Assert.HasCount(12, templates, "four object-facts sets and two census sets, count and page each.");
        foreach (var template in templates)
        {
            StringAssert.Contains(template, "SELECT", "a scanned template must be query text.");
        }

        // And the replacement form really is present where the banned one used to be, so the scan
        // is not passing because the BINDs vanished.
        // FIVE, not six, and the missing one is a fact rather than an omission: the census FAMILY
        // page carries a single BIND, STR(?state), with no UNION and no FILTER NOT EXISTS, so it has
        // no absence branch and no possibly-unbound variable to totalise. The other five pages each
        // derive one cursor key from a variable their own absence branch leaves unbound, and each
        // totalises it with COALESCE.
        Assert.AreEqual(
            5,
            templates.Count(static template =>
                template.Contains("BIND(COALESCE(STR(", StringComparison.Ordinal)),
            "every page template with an absence branch totalises its value-derived cursor key.");
    }
}
