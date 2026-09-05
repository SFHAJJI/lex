using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The Cellar last-modification witness: the query plan, the traversal step that reads one of its
/// pages, and the tie-safe cursor and boundary crossing the two are built on.
///
/// Three groups of tests carry the design. The joins that a well formed page and a well formed
/// crossing cannot make on their own, because a crossing computed from a neighbouring observation
/// attaches silently and a full page inside one tie group looks exactly like an ordinary page while
/// the traversal has stopped moving. The tie-safety refusals themselves, each driven rather than
/// merely present, because a cursor that cannot fail is the failure this whole plan exists to
/// prevent. And the traversal across a daylight saving transition, where 44 percent of the
/// predicate carries the other offset and lexical order and chronological order genuinely disagree.
/// </summary>
[TestClass]
public sealed class EuWatermarkWitnessPlanTests
{

    /// <summary>
    /// One real Appendix A pack root, so the fixture plan is frozen over a batch the plan
    /// will actually canonicalize rather than a placeholder it would refuse.
    /// </summary>
    private const string PackObjectForTests =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    // Watermarks in the publisher's own lexical forms, all of them measured shapes.
    private const string Earlier = "2026-09-03T12:09:18.036+02:00";
    private const string Boundary = "2026-09-03T12:10:17.601+02:00";
    private const string Later = "2026-09-03T12:11:18.036+02:00";

    // The 2026 spring transition, from the lexical profile measurement. Winter is the last observed
    // +01:00 value in the predicate. Diverge is the same local text under +02:00: it sorts after
    // Winter and names the instant an hour before it, which is the divergence in one pair. Second
    // and Milli name the same instant at the two observed precisions, and Second sorts first
    // because + is 0x2B and . is 0x2E.
    private const string Winter = "2026-03-29T01:52:39.176+01:00";
    private const string Diverge = "2026-03-29T01:52:39.176+02:00";
    private const string Second = "2026-03-29T03:00:00+02:00";
    private const string Milli = "2026-03-29T03:00:00.000+02:00";

    // Shapes no bounded observation covers. Z is 0x5A and sorts above both . and +. Two of them,
    // one either side of the fixture boundary, because a crossing still has to be well ordered
    // before the shape check is the thing that refuses it.
    private const string UnmeasuredAbove = "2026-09-03T13:10:17.601Z";
    private const string UnmeasuredBelow = "2026-09-03T09:10:17.601Z";

    private const string KeyA = "http://publications.europa.eu/resource/cellar/aaaaaaaa";
    private const string KeyB = "http://publications.europa.eu/resource/cellar/bbbbbbbb";
    private const string KeyC = "http://publications.europa.eu/resource/cellar/cccccccc";
    private const string KeyX = "http://publications.europa.eu/resource/cellar/xxxxxxxx";
    private const string KeyY = "http://publications.europa.eu/resource/cellar/yyyyyyyy";
    private const string KeyZ = "http://publications.europa.eu/resource/cellar/zzzzzzzz";

    private const string N = "Lex.V3.Contracts.Source.Europe.";

    [TestMethod]
    public void AFrozenPlanRendersTheExactWitnessQuery()
    {
        // THE VALUES BLOCK IS BUILT FROM THE CAPACITY SYMBOL, never from a literal count, so this
        // pin follows EuObjectFactsDiscoveryPlan.BatchCapacity if D1-05g moves it rather than going
        // red for a reason that is not a defect. The one fixture object repeated to capacity is the
        // padding doing its job: a fixed-shape query whatever the batch holds.
        var expectedValues = string.Concat(Enumerable.Repeat(
            "    <" + PackObjectForTests + ">\n",
            EuWatermarkWitnessPlan.BatchCapacity));
        var ExpectedQuery =
            "SELECT ?entry ?entry_key ?watermark WHERE {\n" +
            "  VALUES ?entry {\n" +
            expectedValues +
            "  }\n" +
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
        // requires keyset paging rather than a retry. The plan has to be unable to emit the failing
        // shape, not merely disinclined to.
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
        // Every page re-reads the boundary position itself, so a one row page is spent on a row
        // already delivered. That is a property of the boundary rule and holds whatever the corpus
        // does; it is not an assumption about how large a tie group is.
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
            [PackObjectForTests],
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
            [PackObjectForTests],
            out var refusal);

        Assert.IsNull(plan);
        Assert.AreEqual(EuWatermarkPlanRefusal.PredicateNotTheWatermarkPredicate, refusal);
    }

    [TestMethod]
    public void EveryMeasuredWatermarkShapeIsAdmitted()
    {
        // The measurement found 35.9 million values with a fraction and +02:00, 28.4 million with a
        // fraction and +01:00, and 61,169 with no fraction at all, the newest of those from the day
        // it was taken. A plan that admits only one of these refuses 44 percent of the predicate
        // and no cut can complete.
        Assert.AreEqual(
            EuWatermarkLexicalShape.FractionalSecondsSignedOffset,
            EuWatermarkWitnessPlan.ClassifyShape(Boundary));
        Assert.AreEqual(
            EuWatermarkLexicalShape.FractionalSecondsSignedOffset,
            EuWatermarkWitnessPlan.ClassifyShape(Winter));
        Assert.AreEqual(
            EuWatermarkLexicalShape.WholeSecondsSignedOffset,
            EuWatermarkWitnessPlan.ClassifyShape(Second));

        // Sign and offset minutes do not change the ordering relation, so the admitted members are
        // stated by shape rather than by the offset tokens that happened to be observed.
        Assert.AreEqual(
            EuWatermarkLexicalShape.FractionalSecondsSignedOffset,
            EuWatermarkWitnessPlan.ClassifyShape("2012-05-08T19:50:24.046-03:30"));

        // And each admitted shape can start a plan, which reports which one it carries.
        foreach (var (lexical, shape) in new[]
                 {
                     (Boundary, EuWatermarkLexicalShape.FractionalSecondsSignedOffset),
                     (Winter, EuWatermarkLexicalShape.FractionalSecondsSignedOffset),
                     (Second, EuWatermarkLexicalShape.WholeSecondsSignedOffset),
                 })
        {
            var plan = EuWatermarkWitnessPlan.TryFreeze(
                EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
                EuWatermarkWitnessPlan.WatermarkPredicateIri,
                3,
                At(lexical, KeyA),
            [PackObjectForTests],
                out var refusal);

            Assert.IsNotNull(plan, $"{lexical} refused as {refusal}");
            Assert.AreEqual(shape, plan.StartPositionShape, lexical);
        }

        CollectionAssert.AreEqual(
            new[]
            {
                EuWatermarkLexicalShape.FractionalSecondsSignedOffset,
                EuWatermarkLexicalShape.WholeSecondsSignedOffset,
            },
            EuWatermarkWitnessPlan.AdmittedShapes.ToArray());
    }

    [TestMethod]
    public void AWatermarkShapeNoObservationCoversCannotStartAPlan()
    {
        // Each of these is a form xsd:dateTime allows and the bounded observation did not find.
        // Refusing them is an evidence boundary, not an ordering repair: ordinal comparison would
        // order them perfectly well, and that is exactly the problem, because it would order them
        // somewhere nobody has looked.
        string[] unmeasured =
        [
            UnmeasuredAbove,                    // a Z terminator, which sorts above . and +
            "2026-09-03",                       // date only, the cursor failure R3 forbids
            "2026-09-03T12:10:17.601",          // no offset, so it names no instant
            "2026-9-03T12:10:17.601+02:00",     // unpadded month, so the field widths move
            "2026-09-03T12:10:17.+02:00",       // a decimal point with no digits
            "2026-09-03T12:10:17.601+2:00",     // a short offset token
            "not a dateTime at all",
        ];

        foreach (var lexical in unmeasured)
        {
            Assert.AreEqual(
                EuWatermarkLexicalShape.OutsideTheMeasuredSet,
                EuWatermarkWitnessPlan.ClassifyShape(lexical),
                lexical);

            var plan = EuWatermarkWitnessPlan.TryFreeze(
                EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
                EuWatermarkWitnessPlan.WatermarkPredicateIri,
                3,
                At(lexical, KeyA),
            [PackObjectForTests],
                out var refusal);

            Assert.IsNull(plan, lexical);
            Assert.AreEqual(
                EuWatermarkPlanRefusal.StartPositionShapeWithoutFrozenOrderSemantics,
                refusal,
                lexical);
        }
    }

    [TestMethod]
    public void APositionWhoseShapeWasNeverMeasuredCannotBeRendered()
    {
        var plan = Plan(3);

        Assert.IsNull(plan.RenderPage(At(UnmeasuredAbove, KeyA), out var refusal));
        Assert.AreEqual(EuWatermarkPlanRefusal.PositionShapeWithoutFrozenOrderSemantics, refusal);

        // Both offsets and both precisions render from one plan. A plan that froze the shape of its
        // own start position would refuse two of these three, which is the corpus-scale defect the
        // lexical profile measurement found.
        foreach (var lexical in new[] { Winter, Second, Later })
        {
            Assert.IsNotNull(
                plan.RenderPage(At(lexical, KeyC), out var admitted), lexical);
            Assert.AreEqual(EuWatermarkPlanRefusal.None, admitted, lexical);
        }
    }

    [TestMethod]
    public void TheQueryPlanIdentityIsTheQueryAndNotTheCutBoundary()
    {
        // The literal is computed outside this assembly, by hashing the identity block byte for
        // byte, so a silent change to how that block is built fails here rather than agreeing with
        // itself. Everything below it compares two digests from the same producer and would not.
        // RECOMPUTED INDEPENDENTLY when the plan gained its VALUES restriction, by rebuilding the
        // identity block in another language from the source's own structure and hashing it there,
        // never by copying what the code emitted. It moved because SchemaId is /2, the template
        // carries the VALUES block, and a batch_capacity line joined the block: three intended
        // changes, and this pin is what makes them announce themselves.
        const string ExpectedForLimitFour =
            "f52ffe5614dbb50709c8978f273aae1cdd176a290b523ddb08929ee4815b7c17";

        Assert.AreEqual(ExpectedForLimitFour, Plan(4).QueryPlanIdentityDigest);

        var fromOneBoundary = Plan(3);
        var fromAnotherShape = EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            3,
            At(Second, KeyX),
            [PackObjectForTests],
            out _);

        Assert.IsNotNull(fromAnotherShape);
        Assert.AreNotEqual(
            fromOneBoundary.StartPositionShape, fromAnotherShape.StartPositionShape);
        Assert.AreEqual(
            fromOneBoundary.QueryPlanIdentityDigest,
            fromAnotherShape.QueryPlanIdentityDigest,
            "two cuts running the same plan must be able to say so, whatever they start from");

        // A different page limit is a different query, so it is a different plan identity.
        Assert.AreNotEqual(
            fromOneBoundary.QueryPlanIdentityDigest, Plan(4).QueryPlanIdentityDigest);
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

        // The reread is delivered again and is not learned again.
        CollectionAssert.AreEqual(
            new[] { KeyX },
            step.NewlyDelivered.Select(row => row.CanonicalEntryKey).ToArray());
    }

    [TestMethod]
    public void AFullPageEntirelyInsideTheTieGroupIsAStallAndNotAnEnding()
    {
        // The reread returns the whole group sharing the boundary watermark first. When that group
        // is at least as large as the page there is no room for anything new, so the next request
        // returns these same rows, and so does the one after that. Every page here is well formed.
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
        Assert.AreEqual(0, step.NewlyDelivered.Count);
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
    public void ADeliveredRowWhoseShapeWasNeverMeasuredStopsTheTraversal()
    {
        // A shape appears that no observation covers. The traversal stops with a named cause,
        // because R3 lets a witness run only on order semantics a bounded observation froze.
        var plan = Plan(3);
        var crossing = Crossing(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(UnmeasuredAbove, KeyX));

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyA), At(Boundary, KeyB), At(UnmeasuredAbove, KeyX)],
            out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.WatermarkShapeWithoutFrozenOrderSemantics, refusal);
    }

    [TestMethod]
    public void ACrossingCursorWhoseShapeWasNeverMeasuredStopsTheTraversal()
    {
        // The other half of the same check, and the page here is deliberately inside the measured
        // set so that only the crossing's own cursor can produce this refusal. An earlier version
        // of this test put the whole page outside too, and a mutation that deleted the cursor half
        // survived it: the page half was doing all the work.
        var plan = Plan(3);
        var crossing = Crossing(
            At(UnmeasuredBelow, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Later, KeyX));

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Boundary, KeyA), At(Boundary, KeyB)],
            out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.WatermarkShapeWithoutFrozenOrderSemantics, refusal);
    }

    [TestMethod]
    public void ATraversalAcrossADaylightSavingTransitionReachesEveryEntryExactlyOnce()
    {
        // Three pages spanning the 2026 spring transition, carrying both observed offsets and both
        // observed precisions. The previous cut ended at Winter having emitted A and B there.
        var plan = Plan(3);

        var first = Step(
            plan,
            Crossing(At(Winter, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Diverge, KeyC)),
            [At(Winter, KeyA), At(Winter, KeyB), At(Diverge, KeyC)]);
        var second = Step(
            plan,
            Crossing(At(Diverge, KeyC), [KeyC], [KeyC], At(Second, KeyX)),
            [At(Diverge, KeyC), At(Second, KeyX), At(Milli, KeyY)]);
        var third = Step(
            plan,
            Crossing(At(Milli, KeyY), [KeyY], [KeyY, KeyZ], firstBeyondBoundary: null),
            [At(Milli, KeyY), At(Milli, KeyZ)]);

        // Nothing was skipped and nothing was learned twice across the transition. Every page
        // re-read its own boundary group, and none of those rereads counts as a delivery.
        var learned = first.NewlyDelivered
            .Concat(second.NewlyDelivered)
            .Concat(third.NewlyDelivered)
            .Select(row => row.CanonicalEntryKey)
            .ToArray();

        CollectionAssert.AreEqual(new[] { KeyC, KeyX, KeyY, KeyZ }, learned);

        // Winter and Diverge sat on one accepted page under different offsets, and the pair whose
        // instants run the other way was carried without a refusal.
        Assert.AreEqual(Winter, first.DeliveredPage[0].WatermarkLexical);
        Assert.AreEqual(Diverge, first.DeliveredPage[2].WatermarkLexical);

        // Both precisions were carried too, and the whole-second value sorted first inside the
        // second the two of them share.
        Assert.AreEqual(
            EuWatermarkLexicalShape.WholeSecondsSignedOffset,
            EuWatermarkWitnessPlan.ClassifyShape(second.DeliveredPage[1].WatermarkLexical));
        Assert.AreEqual(
            EuWatermarkLexicalShape.FractionalSecondsSignedOffset,
            EuWatermarkWitnessPlan.ClassifyShape(second.DeliveredPage[2].WatermarkLexical));

        // The third page carried the entry its reread found and then reported no successor.
        CollectionAssert.AreEqual(new[] { KeyZ }, third.Crossing.CarriedForward.ToArray());
        Assert.IsNull(third.NextPosition);
    }

    [TestMethod]
    public void TheChronologicalOrderOfATransitionPairIsNotAnOrderThisPlanAccepts()
    {
        // Winter is 2026-03-29T01:52:39.176+01:00 and Diverge is the same local text under +02:00,
        // so Diverge names the instant an hour earlier while sorting after it. A page in
        // chronological order is therefore out of order here, and is refused. The lexical order is
        // the only order this contract has, which is why nothing in it may be worded as an instant.
        Assert.IsTrue(
            string.CompareOrdinal(Winter, Diverge) < 0,
            "the lexical order puts the winter value first");

        var plan = Plan(3);
        var crossing = Crossing(At(Winter, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Diverge, KeyC));

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            [At(Diverge, KeyC), At(Winter, KeyA), At(Winter, KeyB)],
            out var refusal);

        Assert.IsNull(step);
        Assert.AreEqual(EuWatermarkStepRefusal.PageNotStrictlyAscending, refusal);
    }

    [TestMethod]
    public void ADateOnlyCursorCannotBeOpened()
    {
        // The refusal the whole plan exists for. A cursor built from a watermark alone pages on
        // lastModificationDate against a value many entries share, and steps past or re-delivers
        // every one of them without reporting anything.
        var cursor = EuWatermarkCursor.TryOpen(Boundary, string.Empty, out var refusal);

        Assert.IsNull(cursor);
        Assert.AreEqual(EuWatermarkRefusal.DateOnlyCursor, refusal);

        var opened = EuWatermarkCursor.TryOpen(Boundary, KeyA, out var admitted);
        Assert.IsNotNull(opened);
        Assert.AreEqual(EuWatermarkRefusal.None, admitted, "a true statement on the success path");
        Assert.AreEqual(Boundary, opened.WatermarkLexical);
        Assert.AreEqual(KeyA, opened.CanonicalEntryKey);
    }

    [TestMethod]
    public void ACursorWithNoWatermarkCannotBeOpened()
    {
        var cursor = EuWatermarkCursor.TryOpen(string.Empty, KeyA, out var refusal);

        Assert.IsNull(cursor);
        Assert.AreEqual(EuWatermarkRefusal.WatermarkAbsent, refusal);
    }

    [TestMethod]
    public void ATieMemberTheRereadNoLongerShowsIsRefusedAsSkipped()
    {
        // Three entries share this watermark. The earlier page emitted two of them; the reread
        // comes back with one. The missing one is exactly what a strictly-greater cursor loses
        // silently, and here it is a refusal instead.
        var crossing = EuBoundaryCrossing.TryCross(
            At(Boundary, KeyB),
            [KeyA, KeyB],
            [KeyA],
            firstBeyondBoundary: null,
            out var refusal);

        Assert.IsNull(crossing);
        Assert.AreEqual(EuWatermarkRefusal.BoundaryEntrySkipped, refusal);
    }

    [TestMethod]
    public void ATieSetThatAlreadyNamesAnEntryTwiceIsRefusedAsDuplicated()
    {
        var crossing = EuBoundaryCrossing.TryCross(
            At(Boundary, KeyA),
            [KeyA, KeyA],
            [KeyA],
            firstBeyondBoundary: null,
            out var refusal);

        Assert.IsNull(crossing);
        Assert.AreEqual(EuWatermarkRefusal.BoundaryEntryDuplicated, refusal);
    }

    [TestMethod]
    public void ATieMemberTheRereadDeliversTwiceIsRefusedAsDuplicated()
    {
        // The inclusive reread exists to see the tie group again, not to deliver it twice.
        var crossing = EuBoundaryCrossing.TryCross(
            At(Boundary, KeyB),
            [KeyA, KeyB],
            [KeyA, KeyB, KeyA],
            firstBeyondBoundary: null,
            out var refusal);

        Assert.IsNull(crossing);
        Assert.AreEqual(EuWatermarkRefusal.BoundaryEntryDuplicated, refusal);
    }

    [TestMethod]
    public void ANextPositionThatDoesNotSortAboveTheCursorIsRefused()
    {
        // Inside a tie group the watermarks are equal, so only the entry key separates the
        // positions. A next position at the same watermark with a lower key means the traversal
        // went backwards, and a watermark-only comparison would have called it unchanged.
        Assert.IsNull(EuBoundaryCrossing.TryCross(
            At(Boundary, KeyB),
            [KeyA, KeyB],
            [KeyA, KeyB],
            At(Boundary, KeyA),
            out var backwards));
        Assert.AreEqual(EuWatermarkRefusal.PageNotOrderedAfterCursor, backwards);

        // Standing still is refused for the same reason.
        Assert.IsNull(EuBoundaryCrossing.TryCross(
            At(Boundary, KeyB),
            [KeyA, KeyB],
            [KeyA, KeyB],
            At(Boundary, KeyB),
            out var stationary));
        Assert.AreEqual(EuWatermarkRefusal.PageNotOrderedAfterCursor, stationary);

        // And a next position one key further along the same tie group is accepted, so the check is
        // about the tuple and not about the watermark having to change.
        Assert.IsNotNull(EuBoundaryCrossing.TryCross(
            At(Boundary, KeyB),
            [KeyA, KeyB],
            [KeyA, KeyB, KeyC],
            At(Boundary, KeyC),
            out var forwards));
        Assert.AreEqual(EuWatermarkRefusal.None, forwards);
    }

    [TestMethod]
    public void ACrossingRetainsItsTieSetAndNamesWhatTheRereadCarriedForward()
    {
        var crossing = EuBoundaryCrossing.TryCross(
            At(Boundary, KeyB),
            [KeyA, KeyB],
            [KeyA, KeyB, KeyC],
            At(Later, KeyX),
            out var refusal);

        Assert.IsNotNull(crossing, $"refused as {refusal}");
        Assert.AreEqual(EuWatermarkRefusal.None, refusal);
        CollectionAssert.AreEqual(new[] { KeyA, KeyB }, crossing.RetainedTieSet.ToArray());
        CollectionAssert.AreEqual(new[] { KeyC }, crossing.CarriedForward.ToArray());
        Assert.AreEqual(Boundary, crossing.Cursor.WatermarkLexical);

        // A reread that adds nothing carries nothing forward, so the two lists are not the same
        // list under different names.
        var unchanged = EuBoundaryCrossing.TryCross(
            At(Boundary, KeyB), [KeyA, KeyB], [KeyA, KeyB], At(Later, KeyX), out _);
        Assert.IsNotNull(unchanged);
        Assert.AreEqual(0, unchanged.CarriedForward.Count);
        Assert.AreEqual(2, unchanged.RetainedTieSet.Count);
    }

    /// <summary>
    /// All four types the tie-safety guarantee rests on, pinned the same way through
    /// <see cref="ConstructionSurface.Of"/> rather than a bespoke reflection filter: the cursor and
    /// the crossing it is built from, and the plan and the step that use them. A second door onto
    /// any one of the four would hold a value none of R3's tie-safety checks ran against.
    /// </summary>
    [TestMethod]
    public void TheCursorHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuWatermarkCursor::.ctor(System.String, "
                + "System.String) -> " + N + "EuWatermarkCursor",
                "method public static " + N + "EuWatermarkCursor::TryOpen(System.String, "
                + "System.String, out " + N + "EuWatermarkRefusal&) -> " + N + "EuWatermarkCursor?",
            },
            ConstructionSurface.Of(typeof(EuWatermarkCursor)).ToArray());
    }

    [TestMethod]
    public void TheCrossingHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuBoundaryCrossing::.ctor(" + N
                + "EuWatermarkCursor, System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<System.String>) -> " + N
                + "EuBoundaryCrossing",
                "method public static " + N + "EuBoundaryCrossing::TryCross(" + N
                + "EuWatermarkCursor, System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, " + N
                + "EuWatermarkCursor?, out " + N + "EuWatermarkRefusal&) -> " + N
                + "EuBoundaryCrossing?",
            },
            ConstructionSurface.Of(typeof(EuBoundaryCrossing)).ToArray());
    }

    [TestMethod]
    public void ThePlanHasExactlyOneConstructionPath()
    {
        // The plan alone among the four carries this fourth line: its private static readonly
        // UTF8Encoding and its AdmittedShapes initializer both need a type initializer, which the
        // compiler emits as a static constructor. It cannot itself hand out a plan (the runtime
        // calls it once, automatically, and it returns nothing), but ConstructionSurface pins every
        // constructor the type declares without distinguishing instance from static, so it is
        // pinned here rather than filtered, exactly as the fixture in ConstructionSurfaceTests
        // demonstrates for LeakyThing.
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance Lex.V3.Contracts.Source.Europe.EuWatermarkWitnessPlan::.ctor(System.String, "
                + "System.String, System.Int32, Lex.V3.Contracts.Source.Europe.EuWatermarkCursor, "
                + "Lex.V3.Contracts.Source.Europe.EuWatermarkLexicalShape, System.String, System.String, "
                + "System.Byte[], System.String[], "
                + "System.String) -> Lex.V3.Contracts.Source.Europe.EuWatermarkWitnessPlan",
                "constructor private static Lex.V3.Contracts.Source.Europe.EuWatermarkWitnessPlan::.cctor() -> Lex.V3.Contracts.Source.Europe.EuWatermarkWitnessPlan",
                "method public static Lex.V3.Contracts.Source.Europe.EuWatermarkWitnessPlan::TryFreeze(System.String, "
                + "System.String, System.Int32, Lex.V3.Contracts.Source.Europe.EuWatermarkCursor, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "out Lex.V3.Contracts.Source.Europe.EuWatermarkPlanRefusal&) -> Lex.V3.Contracts.Source.Europe.EuWatermarkWitnessPlan?",
            },
            ConstructionSurface.Of(typeof(EuWatermarkWitnessPlan)).ToArray());
    }

    [TestMethod]
    public void TheStepHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuWatermarkTraversalStep::.ctor(" + N
                + "EuWatermarkWitnessPlan, " + N + "EuBoundaryCrossing, System.Collections.Generic"
                + ".IReadOnlyList<" + N + "EuWatermarkCursor>, System.Collections.Generic"
                + ".IReadOnlyList<" + N + "EuWatermarkCursor>, " + N + "EuWatermarkCursor?, "
                + "System.Int32) -> " + N + "EuWatermarkTraversalStep",
                "method public static " + N + "EuWatermarkTraversalStep::TryAdvance(" + N
                + "EuWatermarkWitnessPlan, " + N + "EuBoundaryCrossing, System.Collections.Generic"
                + ".IReadOnlyList<" + N + "EuWatermarkCursor>, out " + N + "EuWatermarkStepRefusal&)"
                + " -> " + N + "EuWatermarkTraversalStep?",
            },
            ConstructionSurface.Of(typeof(EuWatermarkTraversalStep)).ToArray());
    }

    [TestMethod]
    public void TheRefusalVocabulariesAreClosedAndSpelledForTheWire()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"date_only_cursor\"",
                "\"watermark_absent\"",
                "\"boundary_entry_skipped\"",
                "\"boundary_entry_duplicated\"",
                "\"page_not_ordered_after_cursor\"",
            },
            Enum.GetValues<EuWatermarkRefusal>()
                .Select(value => ContractJson.Serialize(value)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"endpoint_not_the_official_cellar_endpoint\"",
                "\"predicate_not_the_watermark_predicate\"",
                "\"page_limit_below_minimum\"",
                "\"page_limit_above_sorted_result_window\"",
                "\"start_position_shape_without_frozen_order_semantics\"",
                "\"position_shape_without_frozen_order_semantics\"",
                "\"batch_names_no_objects\"",
                "\"batch_above_capacity\"",
                "\"batch_member_not_canonical_or_duplicated\"",
            },
            Enum.GetValues<EuWatermarkPlanRefusal>()
                .Select(value => ContractJson.Serialize(value)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"crossing_cursor_not_in_retained_tie_set\"",
                "\"page_exceeds_plan_limit\"",
                "\"watermark_shape_without_frozen_order_semantics\"",
                "\"page_not_strictly_ascending\"",
                "\"page_below_boundary_watermark\"",
                "\"crossing_does_not_describe_this_page\"",
                "\"traversal_cannot_advance\"",
            },
            Enum.GetValues<EuWatermarkStepRefusal>()
                .Select(value => ContractJson.Serialize(value)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "\"outside_the_measured_set\"",
                "\"fractional_seconds_signed_offset\"",
                "\"whole_seconds_signed_offset\"",
            },
            Enum.GetValues<EuWatermarkLexicalShape>()
                .Select(value => ContractJson.Serialize(value)).ToArray());
    }

    [TestMethod]
    public void NeitherTheContractNorItsRefusalsCanWordTheBoundaryAsAnInstant()
    {
        // The boundary is a position in a string order. Across a transition the lexical order and
        // the chronological order genuinely disagree, so a receipt that named an instant would be
        // wrong by up to an hour in the wrong direction.

        // Structurally: no member of any of these types can hold or return a moment.
        Type[] temporal =
        [
            typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
            typeof(DateOnly), typeof(TimeOnly),
        ];
        Type[] guarded =
        [
            typeof(EuWatermarkWitnessPlan), typeof(EuWatermarkTraversalStep),
            typeof(EuWatermarkCursor), typeof(EuBoundaryCrossing),
        ];

        var carriers = new List<string>();
        foreach (var type in guarded)
        {
            foreach (var member in ConstructionSurface.DeclaredMembersTransitive(type))
            {
                foreach (var candidate in SignatureTypes(member))
                {
                    if (temporal.Any(moment => ConstructionSurface.Carries(candidate, moment)))
                    {
                        carriers.Add($"{type.Name}::{member.Name} -> {candidate.Name}");
                    }
                }
            }
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), carriers.Distinct().ToArray());

        // And on the wire: no refusal token claims a moment either. This cannot police prose, only
        // the closed vocabularies a receipt is built from.
        string[] forbidden = ["instant", "utc", "chronolog", "since", "elapsed", "moment"];
        var tokens = Enum.GetValues<EuWatermarkRefusal>().Select(ContractJson.Serialize)
            .Concat(Enum.GetValues<EuWatermarkPlanRefusal>().Select(ContractJson.Serialize))
            .Concat(Enum.GetValues<EuWatermarkStepRefusal>().Select(ContractJson.Serialize))
            .Concat(Enum.GetValues<EuWatermarkLexicalShape>().Select(ContractJson.Serialize))
            .ToArray();

        Assert.IsTrue(tokens.Length > 20, "the vocabulary being scanned is not empty");
        foreach (var token in tokens)
        {
            foreach (var word in forbidden)
            {
                Assert.IsFalse(
                    token.Contains(word, StringComparison.Ordinal),
                    $"{token} words the boundary as a moment");
            }
        }
    }

    private static IEnumerable<Type> SignatureTypes(MemberInfo member)
    {
        switch (member)
        {
            case PropertyInfo property:
                yield return property.PropertyType;
                break;
            case FieldInfo field:
                yield return field.FieldType;
                break;
            case MethodInfo method:
                yield return method.ReturnType;
                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                break;
            case ConstructorInfo constructor:
                foreach (var parameter in constructor.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                break;
        }
    }

    private static EuWatermarkTraversalStep Step(
        EuWatermarkWitnessPlan plan,
        EuBoundaryCrossing crossing,
        IReadOnlyList<EuWatermarkCursor> deliveredPage)
    {
        var step = EuWatermarkTraversalStep.TryAdvance(
            plan, crossing, deliveredPage, out var refusal);
        Assert.IsNotNull(step, $"the traversal refused as {refusal}");
        Assert.AreEqual(EuWatermarkStepRefusal.None, refusal);
        return step;
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
            [PackObjectForTests],
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
