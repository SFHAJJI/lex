using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The query plan half of the Cellar last-modification witness, and the traversal step that reads
/// one of its pages.
///
/// The load-bearing tests are the two joins that a well formed page and a well formed crossing
/// cannot make on their own: a crossing computed from a neighbouring observation attaches silently
/// unless the step compares it against this page's own boundary rows, and a full page that is
/// entirely inside the boundary tie group looks exactly like an ordinary page while the traversal
/// has stopped moving. The measured tie groups on 2026-09-03 held three to five entries each, so
/// the second is a live condition rather than a hypothetical one.
/// </summary>
[TestClass]
public sealed class EuWatermarkWitnessPlanTests
{
    // Three consecutive watermarks in the publisher's own lexical form, taken from the bounded
    // observation of 2026-09-03: xsd:dateTime, millisecond precision, explicit +02:00 offset.
    private const string Earlier = "2026-09-03T12:09:18.036+02:00";
    private const string Boundary = "2026-09-03T12:10:17.601+02:00";
    private const string Later = "2026-09-03T12:11:18.036+02:00";

    private const string KeyA = "http://publications.europa.eu/resource/cellar/aaaaaaaa";
    private const string KeyB = "http://publications.europa.eu/resource/cellar/bbbbbbbb";
    private const string KeyC = "http://publications.europa.eu/resource/cellar/cccccccc";
    private const string KeyX = "http://publications.europa.eu/resource/cellar/xxxxxxxx";

    [TestMethod]
    public void AFrozenPlanRendersTheExactWitnessQuery()
    {
        const string ExpectedQuery =
            "SELECT ?entry ?entry_key ?watermark WHERE {\n" +
            "  ?entry <http://publications.europa.eu/ontology/cdm/cmr#lastModificationDate> " +
            "?watermark_value .\n" +
            "  FILTER(isIRI(?entry))\n" +
            "  BIND(STR(?entry) AS ?entry_key)\n" +
            "  BIND(STR(?watermark_value) AS ?watermark)\n" +
            "  FILTER(?watermark >= \"2026-09-03T12:10:17.601+02:00\")\n" +
            "}\n" +
            "ORDER BY ?watermark ?entry_key\n" +
            "LIMIT 4\n";

        var plan = Plan(4);
        var query = plan.RenderPage(plan.StartPosition, out var refusal);

        Assert.AreEqual(EuWatermarkPlanRefusal.None, refusal, "a true statement on the success path");
        Assert.AreEqual(ExpectedQuery, query);
    }

    [TestMethod]
    public void TheQueryPagesByKeysetAndCannotExpressASortedOffset()
    {
        // R3.2 records sorted OFFSET + LIMIT beyond 10000 as a permanent platform constraint that
        // requires keyset paging rather than a retry. The plan has to be unable to emit the
        // failing shape, not merely disinclined to.
        var query = Plan(4).RenderPage(At(Boundary, KeyA), out _);

        Assert.IsNotNull(query);
        Assert.IsFalse(
            query.Contains("OFFSET", StringComparison.OrdinalIgnoreCase),
            "a sorted offset is the shape SR353 refuses");
        Assert.IsTrue(query.Contains("ORDER BY ?watermark ?entry_key\n", StringComparison.Ordinal));
        Assert.IsTrue(query.Contains("FILTER(?watermark >= ", StringComparison.Ordinal));
    }

    [TestMethod]
    public void APageLimitOutsideTheSortedWindowIsRefused()
    {
        Assert.IsNotNull(TryFreeze(EuWatermarkWitnessPlan.SortedResultWindowRows, out var atEdge));
        Assert.AreEqual(EuWatermarkPlanRefusal.None, atEdge);

        Assert.IsNull(TryFreeze(EuWatermarkWitnessPlan.SortedResultWindowRows + 1, out var over));
        Assert.AreEqual(EuWatermarkPlanRefusal.PageLimitAboveSortedResultWindow, over);

        // The 1,000,000 selected-row ceiling of R3.2 is unreachable for this plan rather than
        // guarded at run time, and this is where that is asserted: a limit at the ceiling is
        // refused long before a delivered page could ever be ambiguous.
        Assert.IsNull(TryFreeze(1_000_000, out var atCeiling));
        Assert.AreEqual(EuWatermarkPlanRefusal.PageLimitAboveSortedResultWindow, atCeiling);
    }

    [TestMethod]
    public void APageLimitOfOneIsRefusedBecauseTheRereadAlwaysFillsIt()
    {
        // The inclusive reread spends the first row of every page on the boundary position itself,
        // so a one row page can never carry anything new and the traversal could never advance.
        Assert.IsNull(TryFreeze(1, out var one));
        Assert.AreEqual(EuWatermarkPlanRefusal.PageLimitBelowMinimum, one);

        Assert.IsNotNull(TryFreeze(EuWatermarkWitnessPlan.MinimumPageLimit, out var two));
        Assert.AreEqual(EuWatermarkPlanRefusal.None, two);
    }

    [TestMethod]
    public void AStoreOtherThanTheOfficialCellarEndpointCannotWitnessChange()
    {
        var plan = EuWatermarkWitnessPlan.TryFreeze(
            "https://eur-lex.europa.eu/webapi/rdf/sparql",
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            3,
            At(Boundary, KeyA),
            out var refusal);

        Assert.IsNull(plan);
        Assert.AreEqual(EuWatermarkPlanRefusal.EndpointNotTheOfficialCellarEndpoint, refusal);
    }

    [TestMethod]
    public void APredicateOtherThanTheWatermarkCannotWitnessChange()
    {
        // Plausible and wrong: the work-date predicate is in the same ontology and carries a
        // dateTime, but no bounded observation established its order semantics.
        var plan = EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            "http://publications.europa.eu/ontology/cdm#work_date_document",
            3,
            At(Boundary, KeyA),
            out var refusal);

        Assert.IsNull(plan);
        Assert.AreEqual(EuWatermarkPlanRefusal.PredicateNotTheWatermarkPredicate, refusal);
    }

    [TestMethod]
    public void AWatermarkWithNoOrderableLexicalShapeCannotStartAPlan()
    {
        // Each of these is a value some publisher or some caller could supply, and each one puts
        // the lexical order out of step with the chronological one.
        string[] unorderable =
        [
            "2026-09-03",                       // date only, the cursor failure R3 forbids
            "2026-09-03T12:10:17.601",          // no offset, so it names no instant
            "2026-9-03T12:10:17.601+02:00",     // unpadded month, so the field widths move
            "2026-09-03T12:10:17.+02:00",       // a decimal point with no digits
            "2026-09-03T12:10:17.601+2:00",     // a short offset token
            "not a dateTime at all",
        ];

        foreach (var lexical in unorderable)
        {
            var plan = EuWatermarkWitnessPlan.TryFreeze(
                EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
                EuWatermarkWitnessPlan.WatermarkPredicateIri,
                3,
                At(lexical, KeyA),
                out var refusal);

            Assert.IsNull(plan, lexical);
            Assert.AreEqual(
                EuWatermarkPlanRefusal.StartPositionNotLexicallyOrderable, refusal, lexical);
        }

        // And a Z offset is orderable, so the shape check is not simply refusing everything.
        Assert.IsNotNull(EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            3,
            At("2026-09-03T10:10:17.601Z", KeyA),
            out _));
    }

    [TestMethod]
    public void APositionOutsideThePlansLexicalProfileCannotBeRendered()
    {
        var plan = Plan(3);
        Assert.AreEqual(3, plan.WatermarkFractionalDigits);
        Assert.AreEqual("+02:00", plan.WatermarkOffsetToken);

        // A changed offset. These two are one hour apart as instants and invert as strings, which
        // is what a daylight saving transition does to this traversal.
        Assert.IsNull(plan.RenderPage(At("2026-10-25T02:00:00.000+01:00", KeyA), out var offset));
        Assert.AreEqual(EuWatermarkPlanRefusal.PositionNotInPlanLexicalProfile, offset);

        // A changed precision.
        Assert.IsNull(plan.RenderPage(At("2026-09-03T12:10:17.6+02:00", KeyA), out var precision));
        Assert.AreEqual(EuWatermarkPlanRefusal.PositionNotInPlanLexicalProfile, precision);

        // A value with no readable shape at all reaches the same refusal, because the consequence
        // is the same: the order the endpoint applies is not the order the cursor compares in.
        Assert.IsNull(plan.RenderPage(At("2026-09-03", KeyA), out var unshaped));
        Assert.AreEqual(EuWatermarkPlanRefusal.PositionNotInPlanLexicalProfile, unshaped);

        // A different watermark inside the profile renders, so the check is about the profile and
        // not about being the start position.
        Assert.IsNotNull(plan.RenderPage(At(Later, KeyC), out var same));
        Assert.AreEqual(EuWatermarkPlanRefusal.None, same);
    }

    [TestMethod]
    public void TheQueryPlanIdentityIsTheQueryAndNotTheCutBoundary()
    {
        var fromOneBoundary = Plan(3);
        var fromAnother = EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            3,
            At(Later, KeyX),
            out _);

        Assert.IsNotNull(fromAnother);
        Assert.AreNotEqual(
            fromOneBoundary.StartPosition.WatermarkLexical,
            fromAnother.StartPosition.WatermarkLexical);
        Assert.AreEqual(
            fromOneBoundary.QueryPlanIdentityDigest,
            fromAnother.QueryPlanIdentityDigest,
            "two cuts running the same plan must be able to say so");

        // A different page limit is a different query, so it is a different plan identity.
        var wider = Plan(4);
        Assert.AreNotEqual(
            fromOneBoundary.QueryPlanIdentityDigest,
            wider.QueryPlanIdentityDigest);

        Assert.AreEqual(64, fromOneBoundary.QueryPlanIdentityDigest.Length);
        Assert.IsTrue(fromOneBoundary.QueryPlanIdentityDigest.All(
            character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f'));
    }

    [TestMethod]
    public void APageThatCarriesTheTieGroupAndThenMovesOnAdvances()
    {
        var plan = Plan(3);
        var crossing = Crossing(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Later, KeyX));

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyA), At(Boundary, KeyB), At(Later, KeyX)],
            out var refusal);

        Assert.IsNotNull(step, $"refused as {refusal}");
        Assert.AreEqual(EuWatermarkStepRefusal.None, refusal);
        Assert.AreEqual(1, step.RowsBeyondBoundary);
        Assert.IsNotNull(step.NextPosition);
        Assert.AreEqual(Later, step.NextPosition.WatermarkLexical);
        Assert.AreEqual(KeyX, step.NextPosition.CanonicalEntryKey);
        Assert.AreEqual(3, step.DeliveredPage.Count);
    }

    [TestMethod]
    public void AFullPageEntirelyInsideTheTieGroupIsAStallAndNotAnEnding()
    {
        // The reread returns the whole tie group first. When the group is at least as large as the
        // page there is no room for anything new, so the next request returns these same rows, and
        // so does the one after that. Every page here is well formed.
        var plan = Plan(3);
        var crossing = Crossing(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB, KeyC], firstBeyondBoundary: null);

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyA), At(Boundary, KeyB), At(Boundary, KeyC)],
            out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.TraversalCannotAdvance, refusal);
    }

    [TestMethod]
    public void AShortPageInsideTheTieGroupIsConsistentWithTheEndOfTheTraversal()
    {
        // The same shape one row shorter than the limit. The page is not proof of termination, and
        // the step says so by reporting no next position rather than a terminal verdict.
        var plan = Plan(3);
        var crossing = Crossing(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB], firstBeyondBoundary: null);

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan, crossing, [At(Boundary, KeyA), At(Boundary, KeyB)], out var refusal);

        Assert.IsNotNull(step, $"refused as {refusal}");
        Assert.AreEqual(EuWatermarkStepRefusal.None, refusal);
        Assert.IsNull(step.NextPosition);
        Assert.AreEqual(0, step.RowsBeyondBoundary);
    }

    [TestMethod]
    public void ACrossingReconciledFromAnotherObservationDoesNotAttach()
    {
        // Both objects are valid on their own. The crossing reconciled a reread that carried a
        // third entry at the boundary; this page carries two. Nothing inside either type can see
        // the disagreement, and the consequence of missing it is that the entry the crossing says
        // was carried forward was never delivered to anybody.
        var plan = Plan(3);
        var crossing = Crossing(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB, KeyC], At(Later, KeyX));

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyA), At(Boundary, KeyB), At(Later, KeyX)],
            out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.CrossingDoesNotDescribeThisPage, refusal);

        // And the page is not defective in itself: it advances under the crossing that does
        // describe it.
        Assert.IsNotNull(EuWatermarkTraversalStep.TryAdvance(
            plan,
            Crossing(At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Later, KeyX)),
            [At(Boundary, KeyA), At(Boundary, KeyB), At(Later, KeyX)],
            out _));
    }

    [TestMethod]
    public void ACrossingWhoseCursorIsMissingFromItsOwnTieSetIsRefused()
    {
        // EuBoundaryCrossing does not require its cursor to be a member of the tie set it
        // reconciled, so an empty tie set reconciles against an empty reread and would arrive here
        // looking like a clean end of traversal.
        var plan = Plan(3);
        var crossing = Crossing(At(Boundary, KeyB), [], [], firstBeyondBoundary: null);

        var step = EuWatermarkTraversalStep.TryAdvance(plan, crossing, [], out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.CrossingCursorNotInRetainedTieSet, refusal);
    }

    [TestMethod]
    public void APageTheEndpointDidNotOrderIsRefused()
    {
        var plan = Plan(3);
        var crossing = Crossing(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Later, KeyX));

        // Descending in the tuple.
        Assert.IsNull(EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyB), At(Boundary, KeyA), At(Later, KeyX)],
            out var descending));
        Assert.AreEqual(EuWatermarkStepRefusal.PageNotStrictlyAscending, descending);

        // One entry delivered twice inside one page, which the boundary crossing only looks for
        // inside the tie group.
        Assert.IsNull(EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyA), At(Later, KeyX), At(Later, KeyX)],
            out var duplicated));
        Assert.AreEqual(EuWatermarkStepRefusal.PageNotStrictlyAscending, duplicated);
    }

    [TestMethod]
    public void APageCarryingRowsBelowTheBoundaryIsRefused()
    {
        var plan = Plan(3);
        var crossing = Crossing(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Later, KeyX));

        // The inclusive filter reads from the boundary watermark upward, so a row below it means
        // the endpoint answered a question the plan did not ask.
        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Earlier, KeyC), At(Boundary, KeyA), At(Boundary, KeyB)],
            out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.PageBelowBoundaryWatermark, refusal);
    }

    [TestMethod]
    public void APageLongerThanThePlanAskedForIsRefused()
    {
        var plan = Plan(2);
        var crossing = Crossing(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Later, KeyX));

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyA), At(Boundary, KeyB), At(Later, KeyX)],
            out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.PageExceedsPlanLimit, refusal);
    }

    [TestMethod]
    public void ADeliveredRowOutsideThePlansLexicalProfileStopsTheTraversal()
    {
        // The publisher's offset changed between pages. From here on the endpoint's order and the
        // cursor's order are different relations, so the traversal stops with a named cause rather
        // than continuing to compare values it can no longer compare.
        var plan = Plan(3);
        var crossing = Crossing(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB], At("2026-10-25T02:00:00.000+01:00", KeyX));

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyA), At(Boundary, KeyB), At("2026-10-25T02:00:00.000+01:00", KeyX)],
            out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.WatermarkNotInPlanLexicalProfile, refusal);
    }

    [TestMethod]
    public void ACrossingCursorOutsideThePlansLexicalProfileStopsTheTraversal()
    {
        // The other half of the same check, and the page here is deliberately inside the profile
        // so that only the crossing's own cursor can produce this refusal. An earlier version of
        // this test put the whole page outside the profile too, and a mutation that deleted the
        // cursor half survived it: the page half was doing all the work.
        var plan = Plan(3);
        var outside = "2026-09-03T12:10:17.6+02:00";
        var crossing = Crossing(At(outside, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Later, KeyX));

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyA), At(Boundary, KeyB)],
            out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.WatermarkNotInPlanLexicalProfile, refusal);
    }

    [TestMethod]
    public void ThePlanHasExactlyOneConstructionPath()
    {
        AssertOneConstructionPath(
            typeof(EuWatermarkWitnessPlan),
            "static Lex.V3.Contracts.Source.Europe.EuWatermarkWitnessPlan TryFreeze"
            + "(System.String, System.String, Int32, Lex.V3.Contracts.Source.Europe."
            + "EuWatermarkCursor, Lex.V3.Contracts.Source.Europe.EuWatermarkPlanRefusal ByRef)");
    }

    [TestMethod]
    public void TheStepHasExactlyOneConstructionPath()
    {
        AssertOneConstructionPath(
            typeof(EuWatermarkTraversalStep),
            "static Lex.V3.Contracts.Source.Europe.EuWatermarkTraversalStep TryAdvance"
            + "(Lex.V3.Contracts.Source.Europe.EuWatermarkWitnessPlan, Lex.V3.Contracts.Source."
            + "Europe.EuBoundaryCrossing, System.Collections.Generic.IReadOnlyList`1"
            + "[Lex.V3.Contracts.Source.Europe.EuWatermarkCursor], Lex.V3.Contracts.Source.Europe."
            + "EuWatermarkStepRefusal ByRef)");
    }

    [TestMethod]
    public void TheRefusalVocabulariesAreClosedAndSpelledForTheWire()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"endpoint_not_the_official_cellar_endpoint\"",
                "\"predicate_not_the_watermark_predicate\"",
                "\"page_limit_below_minimum\"",
                "\"page_limit_above_sorted_result_window\"",
                "\"start_position_not_lexically_orderable\"",
                "\"position_not_in_plan_lexical_profile\"",
            },
            Enum.GetValues<EuWatermarkPlanRefusal>()
                .Select(value => ContractJson.Serialize(value)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"crossing_cursor_not_in_retained_tie_set\"",
                "\"page_exceeds_plan_limit\"",
                "\"watermark_not_in_plan_lexical_profile\"",
                "\"page_not_strictly_ascending\"",
                "\"page_below_boundary_watermark\"",
                "\"crossing_does_not_describe_this_page\"",
                "\"traversal_cannot_advance\"",
            },
            Enum.GetValues<EuWatermarkStepRefusal>()
                .Select(value => ContractJson.Serialize(value)).ToArray());
    }

    private static void AssertOneConstructionPath(Type type, string expectedFactory)
    {
        // Every constructor private, not merely no public one: this assembly grants
        // InternalsVisibleTo to both test assemblies, so an internal constructor is a friend door.
        var constructors = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsTrue(constructors.Length > 0);
        Assert.IsTrue(
            constructors.All(constructor => constructor.IsPrivate),
            "a non-private constructor would mint a value that was never checked");

        // By-ref parameters too, or a bool-returning TryX with an out parameter of the guarded
        // type is invisible to a filter that only reads return types.
        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == type
                || (method.ReturnType.IsByRef && method.ReturnType.GetElementType() == type)
                || method.GetParameters().Any(parameter =>
                    parameter.ParameterType.IsByRef
                    && parameter.ParameterType.GetElementType() == type))
            .Select(method => $"{(method.IsStatic ? "static" : "instance")} {method}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { expectedFactory }, factories);

        // A public field carrying the guarded type is a construction surface too. The plan's public
        // constants are fields as far as reflection is concerned, so the filter is on what a field
        // can hold rather than on the count.
        var fields = type
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(field => ConstructionSurface.Carries(field.FieldType, type))
            .Select(field => field.Name)
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), fields);
    }

    private static EuWatermarkWitnessPlan Plan(int pageLimit) =>
        TryFreeze(pageLimit, out var refusal)
        ?? throw new InvalidOperationException($"the fixture plan refused as {refusal}");

    private static EuWatermarkWitnessPlan? TryFreeze(
        int pageLimit,
        out EuWatermarkPlanRefusal refusal) =>
        EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            pageLimit,
            At(Boundary, KeyA),
            out refusal);

    private static EuWatermarkCursor At(string watermarkLexical, string canonicalEntryKey) =>
        EuWatermarkCursor.TryOpen(watermarkLexical, canonicalEntryKey, out var refusal)
        ?? throw new InvalidOperationException($"the fixture cursor refused as {refusal}");

    private static EuBoundaryCrossing Crossing(
        EuWatermarkCursor cursor,
        IReadOnlyList<string> retainedTieSet,
        IReadOnlyList<string> rereadAtBoundary,
        EuWatermarkCursor? firstBeyondBoundary) =>
        EuBoundaryCrossing.TryCross(
            cursor, retainedTieSet, rereadAtBoundary, firstBeyondBoundary, out var refusal)
        ?? throw new InvalidOperationException($"the fixture crossing refused as {refusal}");
}
