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

        Assert.HasCount(14, templates, "five object-facts sets and two census sets, count and page each.");
        foreach (var template in templates)
        {
            StringAssert.Contains(template, "SELECT", "a scanned template must be query text.");
        }

        // And the replacement form really is present where the banned one used to be, so the scan
        // is not passing because the BINDs vanished.
        // SIX, not seven, and the missing one is a fact rather than an omission: the census FAMILY
        // page carries a single BIND, STR(?state), with no UNION and no FILTER NOT EXISTS, so it has
        // no absence branch and no possibly-unbound variable to totalise. The other six pages each
        // derive at least one cursor key from a variable their own absence branch leaves unbound,
        // and each totalises it with COALESCE. Family A joined them: its absence branch leaves
        // ?axiom, ?predicate and ?value unbound together, so it totalises three.
        Assert.AreEqual(
            6,
            templates.Count(static template =>
                template.Contains("BIND(COALESCE(STR(", StringComparison.Ordinal)),
            "every page template with an absence branch totalises its value-derived cursor key.");
    }

    /// <summary>
    /// Family A acquires every property of an admitted axiom, and does not choose which ones.
    /// </summary>
    /// <remarks>
    /// The first version of this family pinned the ten properties the probe measured in a
    /// <c>VALUES ?predicate</c> block. That reintroduced at the query the exact false absence the
    /// family exists to avoid: an eleventh or renamed publisher annotation -- including a renamed
    /// <c>quality_issue</c> or <c>error_message</c>, the publisher's own doubt about the date --
    /// would have been dropped before it could become evidence, while the family still enumerated
    /// as complete. S2-A05 requires drift to fail closed into typed evidence, and evidence that was
    /// never acquired cannot fail closed at all. This pins the repair rather than the count:
    /// appending an eleventh constant would have satisfied a "ten properties" assertion while
    /// preserving the defect, so what is asserted here is that the family constrains nothing.
    /// </remarks>
    [TestMethod]
    public void FamilyADoesNotChooseWhichAxiomPropertiesItAcquires()
    {
        var definition = EuObjectFactsDiscoveryPlan.Create()
            .Definition(EuObjectFactsQuerySet.ReifiedAxiomFacts);

        foreach (var template in new[] { definition.CountTemplate, definition.PageTemplate })
        {
            StringAssert.Contains(
                template,
                "?axiom ?predicate ?value",
                "family A must ask for the axiom's properties without naming them.");
            Assert.IsFalse(
                template.Contains("VALUES ?predicate", StringComparison.Ordinal),
                "family A must not constrain which properties of an admitted axiom it acquires: a "
                    + "property the publisher adds or renames would vanish at the query while the "
                    + "family still reported complete.");
        }
    }

    /// <summary>
    /// Family A's positive and absence branches agree on what makes an axiom exist.
    /// </summary>
    /// <remarks>
    /// They did not. The positive branch additionally required <c>rdf:type owl:Axiom</c> while the
    /// FILTER NOT EXISTS branch required only the two annotations, so a node carrying
    /// <c>annotatedSource</c> and an admitted <c>annotatedProperty</c> but missing or misstating
    /// its type matched neither: the positive branch emitted nothing, and the absence branch was
    /// suppressed by the very node it had failed to describe. That parent got no positive row and
    /// no typed absence row -- a silent zero from a publisher shape that was not empty. Whatever
    /// the two branches require, they must require the same thing, or the disagreement is a hole.
    /// </remarks>
    [TestMethod]
    public void FamilyAsPositiveAndAbsenceBranchesRequireTheSameTriples()
    {
        var rows = EuObjectFactsDiscoveryPlan.Create()
            .Definition(EuObjectFactsQuerySet.ReifiedAxiomFacts).CountTemplate;
        var absenceStart = rows.IndexOf("FILTER NOT EXISTS", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, absenceStart, "family A must keep its typed absence branch.");

        var positive = rows[..absenceStart];
        var absence = rows[absenceStart..];

        foreach (var required in new[]
        {
            EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri,
            EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
        })
        {
            StringAssert.Contains(positive, required, "the positive branch must require this.");
            StringAssert.Contains(absence, required, "so must the absence branch, or they disagree.");
        }

        Assert.AreEqual(
            positive.Contains(EuObjectFactsDiscoveryPlan.OwlAxiomClassIri, StringComparison.Ordinal),
            absence.Contains(EuObjectFactsDiscoveryPlan.OwlAxiomClassIri, StringComparison.Ordinal),
            "one branch requires the axiom's declared type and the other does not, so a node with "
                + "both annotations and no type satisfies neither and disappears without a row.");
    }
}
