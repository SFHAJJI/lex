using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E6's live half, slice one: the bounded Cellar family that asks which case-law works
/// point at a batch of EU acts.
/// </summary>
/// <remarks>
/// The predicate IRIs below are written out as independent literals rather than read from
/// <see cref="EuCaseLawPredicateVocabulary"/>. Comparing the plan against the same constants the
/// plan is built from would be a comparison between a value and itself, which is the defect this
/// repository has already had to fix once in E6's own test file.
/// </remarks>
[TestClass]
public sealed class EuCaseLawDiscoveryPlanTests
{
    private const string Interpretes =
        "http://publications.europa.eu/ontology/cdm#case-law_interpretes_resource_legal";
    private const string CitesWork = "http://publications.europa.eu/ontology/cdm#work_cites_work";
    private const string RequestsAnnulment =
        "http://publications.europa.eu/ontology/cdm#case-law_requests_annulment_of_resource_legal";
    private const string DeclaresVoid =
        "http://publications.europa.eu/ontology/cdm#case-law_declares_void_resource_legal";
    private const string DeclaresVoidByPreliminaryRuling =
        "http://publications.europa.eu/ontology/cdm#case-law_declares_void_by_preliminary_ruling_resource_legal";
    private const string EcliPredicate =
        "http://publications.europa.eu/ontology/cdm#case-law_ecli";

    private static readonly string[] GdprBatch =
    [
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
    ];

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("eu-case-law-renderer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:9a1c4f2e-6b3d-4a7c-8e51-0d2f6b9c4a13",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }

    /// <summary>
    /// The family asks for exactly the five predicates E6 pins, and for the case's own ECLI.
    /// </summary>
    /// <remarks>
    /// Every one is a predicate the research proves, with a count: interpretes 74 and
    /// work_cites_work 2,257 inbound on the GDPR, requests_annulment 1 on CRD IV. None is invented,
    /// and no sixth predicate is asked for.
    /// </remarks>
    [TestMethod]
    public void TheFamilyAsksExactlyTheFivePinnedPredicatesAndTheCasesOwnEcli()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();

        foreach (var predicate in new[]
                 {
                     Interpretes, CitesWork, RequestsAnnulment,
                     DeclaresVoid, DeclaresVoidByPreliminaryRuling, EcliPredicate,
                 })
        {
            StringAssert.Contains(plan.PageTemplate, predicate, predicate);
            StringAssert.Contains(plan.CountTemplate, predicate, predicate);
        }

        // The near neighbour E6's contract warns against confusing with annulment-of-resource-legal.
        Assert.IsFalse(
            plan.PageTemplate.Contains("communication_case_new_requests_annulment", StringComparison.Ordinal),
            "a different judicial act must not be swept into this family.");

        // The ECLI must be bound FROM that predicate, not merely mentioned somewhere in the query.
        // Checking only that the IRI appears is satisfied by its occurrence in the FILTER NOT EXISTS
        // branch, so a bound branch reading any predicate at all would pass: this pins the triple.
        StringAssert.Contains(plan.PageTemplate, "?case_work <" + EcliPredicate + "> ?ecli .");
        Assert.IsFalse(
            plan.PageTemplate.Contains("?case_work ?any_predicate", StringComparison.Ordinal),
            "the ECLI is read from its own predicate, never from whatever the case happens to carry.");
    }

    /// <summary>
    /// The edge runs from the case to the act, so the case is the subject and the batch binds the
    /// object.
    /// </summary>
    [TestMethod]
    public void TheCaseIsTheSubjectAndTheBatchBindsTheActs()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();

        StringAssert.Contains(plan.PageTemplate, "?case_work ?case_predicate ?eu_work .");
        StringAssert.Contains(plan.PageTemplate, "VALUES ?eu_work {");
    }

    /// <summary>
    /// A case with no ECLI is asked for explicitly and arrives marked, never merely absent.
    /// </summary>
    /// <remarks>
    /// A row that simply failed to match is indistinguishable from a row the publisher never had.
    /// The <c>FILTER NOT EXISTS</c> branch is what makes the absence a delivered fact, which is what
    /// S2-A05 requires and what lets <c>EcliState.EcliNotInThisSet</c> ever be reached honestly.
    /// </remarks>
    [TestMethod]
    public void AnAbsentEcliIsAskedForExplicitlyRatherThanLeftToSilence()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();

        StringAssert.Contains(plan.PageTemplate, "FILTER NOT EXISTS");
        StringAssert.Contains(plan.PageTemplate, "BIND(\"unbound\" AS ?ecli_kind)");
        StringAssert.Contains(plan.PageTemplate, "UNION");
    }

    /// <summary>Paging is keyset only: no OFFSET, and the delivered rows are never de-duplicated.</summary>
    /// <remarks>
    /// The outer projection must not be <c>DISTINCT</c>, because folding delivered rows would hide
    /// real publisher multiplicity. The one permitted <c>DISTINCT</c> is the batch boundary, which
    /// removes transport padding before the join rather than removing publisher facts after it — see
    /// <see cref="PaddingIsFoldedBeforeTheJoinSoItCannotInflateMultiplicity"/>.
    /// </remarks>
    [TestMethod]
    public void PagingIsKeysetOnlyWithNoOffsetAndNoDistinctOverDeliveredRows()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();

        Assert.IsFalse(plan.PageTemplate.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(
            plan.PageTemplate.StartsWith("SELECT DISTINCT", StringComparison.Ordinal),
            "the delivered row set is never folded.");
        Assert.AreEqual(
            1, plan.PageTemplate.Split("SELECT DISTINCT", StringSplitOptions.None).Length - 1,
            "exactly one DISTINCT exists, and it is the batch boundary.");
        StringAssert.Contains(plan.PageTemplate, "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4");
    }

    /// <summary>
    /// Transport padding is folded before the graph join, so a one-act batch cannot report fifty.
    /// </summary>
    /// <remarks>
    /// This is the guard for a defect this plan shipped and had found against it. Padding repeats the
    /// batch's greatest member through the unused slots to keep a constant request shape. SPARQL
    /// solution mappings are a MULTISET — duplicate <c>VALUES</c> rows are preserved and
    /// <c>COUNT(*)</c> counts them — so with the slots bound directly into the pattern, a one-act
    /// request made every matching edge contribute fifty solutions and report
    /// <c>multiplicity = 50</c>. The inflation varied with how full the batch happened to be, which
    /// makes a publisher fact depend on a caller's batching. The <c>SELECT DISTINCT ?eu_work</c>
    /// boundary is what stops padding becoming invented multiplicity.
    /// </remarks>
    [TestMethod]
    public void PaddingIsFoldedBeforeTheJoinSoItCannotInflateMultiplicity()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();

        foreach (var template in new[] { plan.PageTemplate, plan.CountTemplate })
        {
            StringAssert.Contains(template, "SELECT DISTINCT ?eu_work WHERE {");

            // The dedup boundary must enclose the slots: the DISTINCT has to come before the VALUES
            // block, or the padding reaches the join and the multiplicity is inflated again.
            var distinctAt = template.IndexOf("SELECT DISTINCT ?eu_work", StringComparison.Ordinal);
            var valuesAt = template.IndexOf("VALUES ?eu_work {", StringComparison.Ordinal);
            Assert.IsGreaterThan(-1, distinctAt);
            Assert.IsGreaterThan(-1, valuesAt);
            Assert.IsLessThan(valuesAt, distinctAt, "the batch slots must sit inside the DISTINCT boundary.");

            // The aggregate must be outside that boundary, counting publisher edges rather than slots.
            var countAt = template.IndexOf("COUNT(*)", StringComparison.Ordinal);
            Assert.IsGreaterThan(-1, countAt);
            Assert.IsLessThan(distinctAt, countAt, "the aggregate sits outside the dedup boundary.");
        }
    }

    /// <summary>The delivery profile pins the whole row and names the batch as its selection.</summary>
    [TestMethod]
    public void TheDeliveryProfilePinsTheWholeRowAndNamesTheBatchSelection()
    {
        var profile = EuCaseLawDiscoveryPlan.Create().CreateDeliveryProfile();

        Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso, profile.Dialect);
        CollectionAssert.AreEqual(
            new[]
            {
                "case_work", "case_predicate", "eu_work", "ecli", "ecli_kind",
                "multiplicity", "key_1", "key_2", "key_3", "key_4",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4" },
            profile.CanonicalKeyVariables.ToArray());
        CollectionAssert.AreEqual(profile.CanonicalKeyVariables.ToArray(), profile.CursorVariables.ToArray());
        Assert.HasCount(50, profile.SelectionParameterNames);
        Assert.AreEqual("requested_work_01", profile.SelectionParameterNames[0]);
        Assert.AreEqual("requested_work_50", profile.SelectionParameterNames[49]);
        Assert.AreEqual(RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal, profile.TerminalPagePolicy);
    }

    /// <summary>Binding fills every slot and leaves no renderer placeholder in the sent body.</summary>
    [TestMethod]
    public void BindingFillsEverySlotAndSendsNoPlaceholder()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();
        var source = Source();

        var count = plan.BindCount(
            EuCaseLawQueryPass.Pass1,
            GdprBatch,
            "urn:uuid:1f0f6f0e-2c44-4a1e-9d33-51a7c8b0e441",
            "urn:uuid:2b8d5c17-9e64-4f2a-b6c8-73f1a4d90e52",
            source);

        var page = plan.BindPage(
            EuCaseLawQueryPass.Pass2,
            GdprBatch,
            null,
            0,
            count.InputArtifact.ArtifactRef,
            "urn:uuid:3c9e7d28-af75-4b3b-c7d9-84a2b5ea1f63",
            "urn:uuid:4da8e39f-b086-4c4c-d8ea-95b3c6fb2a74",
            source);

        Assert.AreEqual("eu-case-law-links-by-act", count.InputArtifact.PartitionBinding.MemberKey);
        Assert.AreEqual(51, count.InputArtifact.OrderedParameters.Count, "pass plus fifty batch slots.");
        Assert.AreEqual(52, page.InputArtifact.OrderedParameters.Count, "pass, fifty batch slots and cursor presence.");

        var countText = Encoding.UTF8.GetString(count.Request.CopyRequestBody());
        var pageText = Encoding.UTF8.GetString(page.Request.CopyRequestBody());
        StringAssert.Contains(countText, "VALUES ?lex_pass_id { 1 }");
        StringAssert.Contains(pageText, "VALUES ?lex_pass_id { 2 }");
        foreach (var text in new[] { countText, pageText })
        {
            Assert.IsFalse(text.Contains("{pass_id:uint}", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains(":iri}", StringComparison.Ordinal), "every batch slot is filled.");
        }

        Assert.IsFalse(pageText.Contains("{page_limit:uint}", StringComparison.Ordinal));
        StringAssert.Contains(pageText, "LIMIT 547", "the page limit comes from the pass policy.");
    }

    /// <summary>
    /// The batch is fixed at fifty and padded, so one act and fifty acts send the same shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Padding repeats the batch's own greatest member, and it adds no act to the question because
    /// the <c>SELECT DISTINCT ?eu_work</c> subquery folds the duplicate slots before the graph join.
    /// The transport carries fifty slots; the question asks about the acts the caller named.
    /// </para>
    /// <para>
    /// The reason for that boundary, stated here because this is the test a reader reaches first:
    /// SPARQL solution mappings are a MULTISET. Duplicate <c>VALUES</c> rows are preserved and
    /// <c>COUNT(*)</c> counts them, so binding the padded slots straight into the pattern made a
    /// one-act request report <c>multiplicity = 50</c>. An earlier version of this remark claimed
    /// the opposite — that "VALUES set semantics fold the duplicate" — and that false sentence is
    /// what the defect was made of. See
    /// <see cref="PaddingIsFoldedBeforeTheJoinSoItCannotInflateMultiplicity"/>, which guards the
    /// boundary that actually does the folding.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ABatchIsPaddedToAFixedFiftySoOneActAndFiftyLookAlike()
    {
        var padded = EuCaseLawDiscoveryPlan.PadBatch(
            EuCaseLawDiscoveryPlan.CanonicalizeBatch(GdprBatch));

        Assert.HasCount(50, padded);
        Assert.IsTrue(padded.All(value => string.Equals(value, padded[0], StringComparison.Ordinal)),
            "a one-act batch pads with that act.");
    }

    /// <summary>A batch that is empty, over capacity or repeats an act is refused by name.</summary>
    [TestMethod]
    public void AnEmptyOverCapacityOrDuplicateBatchIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => EuCaseLawDiscoveryPlan.CanonicalizeBatch([]));

        Assert.ThrowsExactly<ArgumentException>(
            () => EuCaseLawDiscoveryPlan.CanonicalizeBatch([GdprBatch[0], GdprBatch[0]]));

        var overCapacity = Enumerable.Range(0, 51)
            .Select(index => "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed7"
                + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.ThrowsExactly<ArgumentException>(
            () => EuCaseLawDiscoveryPlan.CanonicalizeBatch(overCapacity));
    }

    /// <summary>A continuation cursor must carry exactly the four canonical keys.</summary>
    [TestMethod]
    public void AContinuationCursorMustCarryExactlyFourParts()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();
        var source = Source();

        Assert.ThrowsExactly<ArgumentException>(() => plan.BindPage(
            EuCaseLawQueryPass.Pass1,
            GdprBatch,
            ["only", "three", "parts"],
            0,
            new SourceArtifactRef(
                "urn:uuid:5eb9f4a0-c197-4d5d-e9fb-a6c4d70c3b85",
                new string('a', 64)),
            "urn:uuid:6fcaa5b1-d2a8-4e6e-fa0c-b7d5e81d4c96",
            "urn:uuid:70dbb6c2-e3b9-4f7f-ab1d-c8e6f92e5da7",
            source));
    }

    /// <summary>A count request cannot carry a cursor.</summary>
    [TestMethod]
    public void ACountRequestCannotCarryACursor()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();
        StringAssert.Contains(plan.CountTemplate, "SELECT (COUNT(*) AS ?count)");
        Assert.IsFalse(plan.CountTemplate.Contains("last_key_1", StringComparison.Ordinal));
    }
}
