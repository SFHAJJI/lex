using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Luxembourg repeated-enumeration executor: the driving algorithm of design section 2, one
/// test per typed refusal in section 3, plus the SR353 structural properties of section 5.
/// </summary>
/// <remarks>
/// The ten RED tests preserved at f33a82f3 (against a <c>RepeatedEnumerationTraversalReader</c>
/// this executor's design replaced) and their disposition in this tree, so the mapping survives
/// rather than living only in a review verdict:
/// <list type="bullet">
/// <item><c>BoundCountOpensTheRetainedRendererForOfflineVerification</c> - folded into the
/// source-agnostic <c>OfflineVerificationReproducesEvidenceButCannotMintSendCapability</c>
/// (tests/Lex.V3.Tests/Contracts/Source/Core/MachineQueryPlanContractTests.cs). LU's
/// <c>BindCount</c> mints its <c>BoundMachineRequest</c> through
/// <c>MachineQueryBinder.BindForSend</c>, the same machinery that generic test proves offline
/// verification over, so the property is proven once for every source rather than once per bound
/// request shape.</item>
/// <item><c>BoundPageOpensTheRetainedRendererForOfflineVerification</c> - same successor as above;
/// <c>BindPage</c> also routes through <c>MachineQueryBinder.BindForSend</c>.</item>
/// <item><c>ReadCountParsesTheStrictLuxembourgIntegerBinding</c> - successor
/// <see cref="ACountOneBelowTheCeilingProceedsToPageZero"/>, which asserts
/// <c>SelectedRowCountA == 999_999</c> off a real <c>ParseStrictCount</c> parse.</item>
/// <item><c>ReadCountRejectsTheWrongLuxembourgLiteralWireType</c> - successor
/// <see cref="ACountTermOnTheWrongWireTypeIsRefusedBeforeAnyPageIsBound"/> (already cited in
/// tree).</item>
/// <item><c>ReadPageReturnsTheRowCountAndSixPartFinalCursor</c> - successor
/// <see cref="AWellFormedPassSpendsExactlyItsBudget"/>, a full ten-row page parsed to the right row
/// count and final cursor across two independently-run passes that then agree.</item>
/// <item><c>ReadPageReturnsNoCursorForAnInitialEmptyPage</c> - successor
/// <see cref="ACountOneBelowTheCeilingProceedsToPageZero"/> again: its handler answers every
/// non-count ordinal with <c>EmptyRowsJson()</c>, so each pass's own first page is empty and
/// <c>DeliveredRowCountA == 0</c> with no cursor ever established.</item>
/// <item><c>ReadPageRejectsDuplicateCanonicalKeys</c> - successor
/// <see cref="TwoRowsSharingAllSixKeyPartsStopTheirOwnPage"/>.</item>
/// <item><c>ReadPageRejectsNonIncreasingCursors</c> - successor
/// <see cref="APublisherThatIgnoresTheCursorStopsOnTheSecondPage"/> (the cross-page case;
/// <c>TwoRowsSharingAllSixKeyPartsStopTheirOwnPage</c> above is the within-one-page case).</item>
/// <item><c>ReadPageRejectsQualifiedOrNonPlainCursorTerms</c> - successors
/// <see cref="AQualifiedOrNonPlainCursorTermIsRefusedThroughCore"/> (the datatype and IRI DataRow
/// cases; already cited in tree) plus <see cref="CoreRefusalsAreCarriedNotReclassified"/> (the
/// language-tag case).</item>
/// <item><c>ReadPageRejectsAResponseBeyondTheRowLimit</c> - successor
/// <see cref="APageDeliveringMoreRowsThanItsLimitIsRefusedThroughCore"/> (already cited in
/// tree).</item>
/// </list>
/// </remarks>
[TestClass]
public sealed class LuxembourgRepeatedEnumerationExecutorTests
{
    [TestMethod]
    public async Task ADisallowedRobotsAnswerSpendsNoProductRequest()
    {
        var (request, witness) = BuildRequest();
        var handler = new LuxembourgAcquisitionTestFixture.SequencedHandler((ordinal, req) =>
            ordinal == 0
                ? TextResponse(req, "User-agent: *\nDisallow: /\n")
                : throw new AssertFailedException("No product request should follow a disallowed robots answer."));
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.RobotsBootstrapRefused, result.Refusal.Code);
        Assert.AreEqual(0, result.ProductRequestCount);
    }

    /// <summary>
    /// A filesystem deployment RUNS, and its receipt says under which guarantee it holds.
    /// </summary>
    /// <remarks>
    /// THIS TEST ASSERTED THE OPPOSITE and was named
    /// <c>AFilesystemDeploymentSaysSoBeforeTheFirstProductRequest</c>: the executor refused at
    /// request zero, with a handler that threw if any product request followed, because the robots
    /// bootstrap artifacts on a filesystem store are RetainedUnenforced. That gate stopped every
    /// Luxembourg run outside Azure before it sent anything, and it is what the acceptance canary
    /// hit one level down.
    /// <para>
    /// RULING lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c, extending the
    /// Decision 71 interpretation lex-event-20260904T212914634Z-f166f0b9e11b445795efd40c268bfbb8 to
    /// every custody floor gate: a completeness proof resting on retained artifacts is the
    /// immutability argument one level up and takes the same answer. The membership is recorded and
    /// the run continues. The three genuine custody failures are each refused where the fact is
    /// actually known rather than here: an artifact ABSENT from the map by the session itself,
    /// per send, before capability minting; a membership that is not receipt derived when the
    /// receipt is built; a write error or digest mismatch at the hold.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task AFilesystemDeploymentRunsAndItsReceiptCarriesRetainedUnenforced()
    {
        var (request, witness) = BuildRequest();
        var root = Path.Combine(Path.GetTempPath(), "lex-lu-executor-floor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileSystemCustodyStore(root);
            var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
            {
                1 or 3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(0)),
                _ => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            });

            var result = await Run(store, request, witness, handler);

            // WHAT THIS LANE'S CHANGE PROVES: the run is no longer stopped at request zero. It
            // reaches and sends its product requests, which the old gate refused to allow.
            Assert.AreNotEqual(
                LuxembourgEnumerationRefusal.RobotsBootstrapRefused,
                result.Refusal?.Code,
                "robots is not the point of this test.");
            Assert.IsTrue(
                result.ProductRequestCount > 0,
                "product requests really happen now; the old custody gate refused before the "
                + "first one, which is what stopped every deployment outside Azure.");

            // WHAT IS NOT THIS LANE'S TO FIX, named rather than asserted away, and named
            // CORRECTLY: an earlier version of this remark said the residual gate was
            // RepeatedEnumerationDeliveryReceipt.RequireFlooredRun. It is not, and the executor
            // never reaches that method at all: RequireFlooredRun is called only by
            // TryProveFamilyEnumeration, and only the two ADAPTERS call that. Verified by reading
            // the call sites rather than by the name.
            //
            // THE EXPIRY DATE ARRIVED, AND THE FLIP IS THE ONE THAT WAS WRITTEN DOWN. An unfloored
            // run used to stop at the TUPLE BIND, EnumerationDeliveryComparison.Create in shared
            // Source/Core, which refused any observation whose body receipt was not
            // ImmutableObject1 and LockedTime. Correction
            // lex-event-20260904T230312457Z-5d1f7352c46b4ca0ac8c71ca6a4aa1fb dropped the profile
            // and protection halves and kept the anti-forgery digest comparison and the
            // NightlyFloor90d class check, and lane B has landed it, so nothing stops there now.
            // The assertion below was kept in its old form precisely so this moment would be
            // loud rather than silent, and it is flipped to the receipt carrying
            // RetainedUnenforced that this test's own name has promised all along.
            Assert.IsNull(
                result.Refusal,
                "a filesystem deployment refuses at no gate now: " + result.Refusal?.Code);
            Assert.IsNotNull(result.Receipt, "and the run produces its delivery receipt.");
            Assert.AreEqual(
                CustodyMembership.RetainedUnenforced,
                result.Receipt!.RetainedFloor,
                "AND THE RECEIPT CARRIES THE CLASS THIS TEST IS NAMED FOR. Asserting only that a "
                + "receipt exists would pass if the run reported Floored, which is the strongest "
                + "possible wrong answer on a store that enforces nothing.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task APreHeaderTransportFailureRefusesWithoutAProof()
    {
        var (request, witness) = BuildRequest();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, _) =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "simulated pre-header failure"));
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.ObservationNotExecuted, result.Refusal.Code);
        Assert.AreEqual(1 + 4, handler.SendCount, "one robots send plus the full MaximumAttempts=4 budget");
    }

    [TestMethod]
    public async Task A503OnAPageIsRefusedOnceAndItsBodyIsRecoverable()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var bodies = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        bodies.Enqueue(req => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(2)));
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
            bodies.Count > 0 ? bodies.Dequeue()(req) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Version = HttpVersion.Version11,
                RequestMessage = req,
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("upstream is busy")),
            });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.StatusNotAdmitted, result.Refusal.Code);
        Assert.AreEqual(503, result.Refusal.TerminalStatus);
        Assert.AreEqual(3, handler.SendCount, "robots, the count, and exactly one send for the refused page");
        Assert.IsNotNull(result.Refusal.ResponseBodySha256);
        var recovered = await store.ReadByDigestAsync(result.Refusal.ResponseBodySha256!, CancellationToken.None);
        Assert.AreEqual("upstream is busy", System.Text.Encoding.UTF8.GetString(recovered.Span));
    }

    [TestMethod]
    public async Task ATwoHundredWithTheWrongMediaTypeIsRefusedBeforeParsing()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
        {
            var body = System.Text.Encoding.UTF8.GetBytes(LuxembourgAcquisitionTestFixture.CountJson(2));
            var content = new ByteArrayContent(body);
            content.Headers.TryAddWithoutValidation("Content-Type", "text/html");
            content.Headers.TryAddWithoutValidation(
                "Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version11,
                RequestMessage = req,
                Content = content,
            };
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.MediaTypeNotAdmitted, result.Refusal.Code);
        Assert.AreEqual("text/html", result.Refusal.ObservedMediaType);
        Assert.IsNull(result.Refusal.ObservedCount);
    }

    [TestMethod]
    public async Task ACountBodyWithTwoRowsIsRefusedBeforeAnyPageIsBound()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var twoRowCount =
            "{\"head\":{\"link\":[],\"vars\":[\"count\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
            + "{\"count\":{\"type\":\"typed-literal\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"1\"}},"
            + "{\"count\":{\"type\":\"typed-literal\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"2\"}}]}}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
            JsonResponse(req, twoRowCount));

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CountNotOneNonNegativeInteger, result.Refusal.Code);
        Assert.AreEqual(2, handler.SendCount, "robots plus exactly the one refused count send, no page");
    }

    [TestMethod]
    public async Task ACountAtThePublisherCeilingRefusesWithoutSendingAPage()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
            JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1_000_000)));

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.PartitionRequired, result.Refusal.Code);
        Assert.AreEqual(2, handler.SendCount, "robots plus exactly the one count send, no page");
    }

    [TestMethod]
    public async Task ACountOneBelowTheCeilingProceedsToPageZero()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        // A count one below the ceiling, followed by an (immediately, honestly mismatched) empty
        // page: this test's only claim is that binding and sending a page happened at all, not
        // that a 999,999-row pass completes inside a unit test.
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(999_999)),
            _ => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
        });

        var result = await Run(store, request, witness, handler);

        // Exact values, not "anything but partition_required". The old assertion here was
        // AreNotEqual(PartitionRequired), which passed on twelve other refusal codes and on a
        // delivered result too, so it could not tell the ceiling comparison from anything else.
        //
        // What actually happens, stated: the count is admitted (BelowMaximum, not
        // PartitionRequired), a page IS bound and sent for each pass, and the run delivers a
        // receipt whose custody is sound. The delivery is nonetheless NOT a proven enumeration,
        // because a publisher claiming 999,999 selected rows and then handing back none has not
        // delivered the same selection twice; Source/Core says so as DifferentSelections, which is
        // what stops AbsenceFamilyEnumerationProof.TryCreate downstream. A delivered receipt is a
        // custody fact, never by itself a completeness one.
        Assert.IsNotNull(result.Receipt);
        Assert.IsNull(result.Refusal);
        Assert.AreEqual(5, handler.SendCount, "robots, then a count and one empty page for each pass");
        Assert.AreEqual(
            RepeatedEnumerationThresholdAssessment.BelowMaximum,
            result.Receipt.Delivery.ThresholdAssessment);
        Assert.AreEqual(999_999, result.Receipt.Delivery.SelectedRowCountA);
        Assert.AreEqual(0, result.Receipt.Delivery.DeliveredRowCountA);
        Assert.AreEqual(EnumerationDeliveryOutcome.DifferentSelections, result.Receipt.Delivery.Outcome);
        Assert.IsNull(
            result.Receipt.TryProveFamilyEnumeration(
                result.Receipt.Delivery.PartitionKey, out var proofRefusal));
        Assert.AreEqual(
            Lex.V3.Contracts.Source.Absence.AbsenceFamilyEnumerationProofRefusal
                .PassesDeliveredDifferentSelections,
            proofRefusal);
    }

    [TestMethod]
    public async Task AKeyLongerThanACursorPartStopsTheEnumeration()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        // Derived from the bound the key check actually enforces, not restated as a magic number a
        // future change to that bound would leave stale.
        var oversized = new string('a', LuxembourgQueryText.MaximumKeyPartByteLength + 1);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(oversized)),
            _ => throw new AssertFailedException("No further sends after the oversized key."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.DeliveredKeyNotRepresentable, result.Refusal.Code);
        Assert.AreEqual(3, handler.SendCount, "the oversized page was the last request");
    }

    [TestMethod]
    public async Task ARowOutsideTheRequestedRangeIsARefusalNotAnException()
    {
        var (request, witness) = BuildRequestWithPartition(new LuxembourgQueryPartitionRange(
            "narrow",
            new LuxembourgQueryCursor("a", "", "", "", "", ""),
            new LuxembourgQueryCursor("m", "", "", "", "", "")));
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("z")),
            _ => throw new AssertFailedException("No further sends after the out-of-range row."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.DeliveredRowOutsidePartition, result.Refusal.Code);
    }

    [TestMethod]
    public async Task APublisherThatIgnoresTheCursorStopsOnTheSecondPage()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("b")),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("b")),
            _ => throw new AssertFailedException("No further sends after the repeated page."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CursorDidNotAdvance, result.Refusal.Code);
    }

    [TestMethod]
    public async Task TwoRowsSharingAllSixKeyPartsStopTheirOwnPage()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("b", "b")),
            _ => throw new AssertFailedException("No further sends after the duplicate-key page."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CursorDidNotAdvance, result.Refusal.Code);
    }

    [TestMethod]
    public async Task APublisherThatNeverEndsRefusesInsteadOfProvingATruncatedSet()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        // count says 10 rows; the publisher then serves a non-empty short page forever with
        // strictly increasing keys. L1 = 997, so the budget for 10 selected rows is
        // 10/997 + 2 = 2 pages.
        // Capped, not truly infinite: if the budget guard is deleted, this fixture must fail fast
        // on an assertion rather than hang until a harness timeout (which would be evidence of a
        // slow test, not evidence the guard is real). Ten extra sends past the budget is far more
        // than any correct implementation would ever issue.
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(10)),
            <= 13 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(Letter(ordinal))),
            _ => throw new AssertFailedException(
                "The budget guard did not stop the pass within ten sends of its budget."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.PageBudgetExhausted, result.Refusal.Code);
        Assert.AreEqual(
            1 + 2,
            result.ProductRequestCount,
            "count plus exactly the two-page budget, robots excluded");
    }

    [TestMethod]
    public async Task AWellFormedPassSpendsExactlyItsBudget()
    {
        // The same 10-row, budget-2 shape, but the publisher actually terminates within budget:
        // one short page (below the 997 limit) then the required empty successor.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var rows = Enumerable.Range(0, 10).Select(Letter).ToArray();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(10)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(rows)),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(10)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(rows)),
            6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNotNull(result.Receipt);
        Assert.IsNull(result.Refusal);
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, result.Receipt.Delivery.Outcome);
        Assert.AreEqual(
            3 + 3,
            result.ProductRequestCount,
            "exactly two count+2-page passes, no slack, robots excluded");
    }

    [TestMethod]
    public async Task MaterializationAgainstAStoreThatLostAnObjectRefusesAsCustodyMemberMissing()
    {
        // Seeding around this would destroy the test: the store genuinely wrote everything this
        // run needed, then a member becomes unreadable before MaterializeAsync reopens it. This is
        // the executor-level half of the anti-tautology proof step 5 already drove at the
        // Contracts level (LuxembourgDeliveryEvidenceSetTests): here it must actually surface as
        // custody_member_missing, not merely as a Contracts-level exception.
        var (request, witness) = BuildRequest();
        var inner = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var store = new EvictingCustodyStore(inner);
        var rows = Enumerable.Range(0, 3).Select(Letter).ToArray();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(3)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(rows)),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(3)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(rows)),

            // The last send of the run. Every page has been bound by now, so every send-time
            // reopen of the invariant plan has already happened and the NEXT one belongs to
            // MaterializeAsync. Evicting here states that boundary instead of counting up to it:
            // the previous version of this store evicted after a hand-measured twelve reads, which
            // said nothing about which read it was aiming at and would have silently started
            // evicting mid-pass the first time the run's reopen pattern changed.
            6 => EvictThen(store, () => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson())),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CustodyMemberMissing, result.Refusal.Code);
        Assert.IsTrue(
            store.ReadsBeforeEviction > 0,
            "the run must genuinely have reopened the artifact before it was lost, or this proves nothing");
    }

    private static HttpResponseMessage EvictThen(
        EvictingCustodyStore store, Func<HttpResponseMessage> respond)
    {
        store.EvictNow();
        return respond();
    }

    [TestMethod]
    public async Task CoreRefusalsAreCarriedNotReclassified()
    {
        // A cursor projection carrying a language tag: Source/Core's own VerifyPages rejects any
        // cursor term that is not a plain literal (RepeatedEnumerationDeliveryProof.cs's Cursor
        // check), and every "S"-set projection variable is a cursor variable, so this is reachable
        // from the executor's normal page-admission path (the row parses and advances the cursor
        // structurally correctly) and fails only once Source/Core resolves and verifies it.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var taggedRow =
            "{\"head\":{\"link\":[],\"vars\":[\"key_1\",\"key_2\",\"key_3\",\"key_4\",\"key_5\",\"key_6\"]},"
            + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{"
            + "\"key_1\":{\"type\":\"literal\",\"value\":\"a\",\"xml:lang\":\"fr\"},"
            + "\"key_2\":{\"type\":\"literal\",\"value\":\"\"},\"key_3\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"key_4\":{\"type\":\"literal\",\"value\":\"\"},\"key_5\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"key_6\":{\"type\":\"literal\",\"value\":\"\"}}]}}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, taggedRow),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("a")),
            6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.DeliveryProofRefused, result.Refusal.Code);
        Assert.IsNotNull(result.Refusal.CoreRefusalDetail);
        StringAssert.Contains(result.Refusal.CoreRefusalDetail!, "plain literal");
    }

    [TestMethod]
    public async Task TheExecutorCannotExpressASortedOffsetForAnyPublisherSet()
    {
        // The LU page template has no OFFSET parameter at all (LuxembourgQueryPlan.cs's page
        // template), and the executor's own binder calls (BindCount/BindPage) carry no page count,
        // page limit or offset parameter through which a caller could reintroduce one. This test
        // asserts the property of the REQUESTS this run issues, not of an SR353 response body no
        // observation of which exists anywhere in this repository.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var seenBodies = new List<byte[]>();
        var rows = Enumerable.Range(0, 3).Select(Letter).ToArray();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) =>
        {
            var response = ordinal switch
            {
                1 or 4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(3)),
                2 or 5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(rows)),
                3 or 6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
                _ => throw new AssertFailedException("No further sends after both passes complete."),
            };
            if (req.Content is not null)
            {
                seenBodies.Add(req.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            }

            return response;
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNotNull(result.Receipt);
        Assert.IsTrue(seenBodies.Count > 0);
        foreach (var body in seenBodies)
        {
            var text = System.Text.Encoding.UTF8.GetString(body);
            Assert.IsFalse(
                text.Contains("OFFSET", StringComparison.Ordinal),
                "a sorted offset is the shape SR353 refuses");
        }
    }

    [TestMethod]
    public async Task EveryAttemptOnARefusedPageIsByteIdentical()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var seenPageBodies = new List<byte[]>();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) =>
        {
            if (ordinal == 1)
            {
                return JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1));
            }

            if (req.Content is not null)
            {
                seenPageBodies.Add(req.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Version = HttpVersion.Version11,
                RequestMessage = req,
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("simulated 500")),
            };
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.StatusNotAdmitted, result.Refusal.Code);
        Assert.AreEqual(1, seenPageBodies.Count, "the executor never retries a refused page with a different shape");
    }

    [TestMethod]
    public async Task AFiveHundredOnTheFinalPageProducesNoShorterProof()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("a")),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Version = HttpVersion.Version11,
                RequestMessage = req,
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("simulated 500")),
            },
        });

        var result = await Run(store, request, witness, handler);

        // The dangerous SR353 failure is truncation reading as completeness. A 500 on what would
        // have been the terminal empty page must never be mistaken for that empty page: no receipt,
        // no cover, and the refusal is on the failed page's own terms.
        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.StatusNotAdmitted, result.Refusal.Code);
        Assert.AreEqual(500, result.Refusal.TerminalStatus);
    }

    /// <summary>
    /// A store that writes and reads normally until <see cref="EvictNow"/> is called, after which
    /// one large object (the LU invariant plan, retained as the renderer's "profile") is gone.
    /// </summary>
    /// <remarks>
    /// This used to evict after a hand-tuned twelve reads, a number measured once against this
    /// fixture's exact six-send shape and then written down as a constant. It said nothing about
    /// which read mattered and it would have started evicting in the middle of a pass, silently
    /// changing what the test proved, the first time anything altered how often the run reopens
    /// that artifact. The switch is thrown by the test between the run and materialization
    /// instead, so the boundary is stated by the caller rather than counted, and the test names
    /// the read it targets: MaterializeAsync's own reopen. Seeding around that would make a
    /// resolver that never really reopens anything indistinguishable from a correct one.
    /// </remarks>
    private sealed class EvictingCustodyStore(ICustodyStore inner) : ICustodyStore
    {
        private readonly object _gate = new();
        private string? _targetDigest;
        private bool _evicted;
        private int _readsBeforeEviction;

        /// <summary>Reads of the target artifact the run itself performed, for the test to assert.</summary>
        internal int ReadsBeforeEviction
        {
            get
            {
                lock (_gate)
                {
                    return _readsBeforeEviction;
                }
            }
        }

        internal void EvictNow()
        {
            lock (_gate)
            {
                if (_targetDigest is null)
                {
                    throw new AssertFailedException("Nothing large enough to be the invariant plan was written.");
                }

                _evicted = true;
            }
        }

        public async Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var receipt = await inner.CreateAsync(bytes, custodyClass, cancellationToken);
            lock (_gate)
            {
                // The invariant plan's canonical JSON dwarfs the small fixed-size artifacts
                // retained before it (source profile, run identity, reason registry, adapter
                // execution artifact, policies, logical request), so the first sufficiently large
                // write identifies it without depending on retention order otherwise.
                if (_targetDigest is null && bytes.Length > 4096)
                {
                    _targetDigest = receipt.Reference.ContentSha256;
                }
            }

            return receipt;
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (contentSha256 == _targetDigest)
                {
                    if (_evicted)
                    {
                        throw new CustodyIntegrityException("simulated post-write eviction");
                    }

                    _readsBeforeEviction++;
                }
            }

            return inner.ReadByDigestAsync(contentSha256, cancellationToken);
        }
    }

    [TestMethod]
    public void TheBudgetFormulaIsExactNotSlack()
    {
        // A direct unit check of the formula itself, so "widen the budget by one" is caught even
        // though AWellFormedPassSpendsExactlyItsBudget's own fixture terminates on the required
        // empty successor page regardless of what the budget constant says - only a value
        // assertion on MaximumPagesFor itself, not on request counts an unrelated termination
        // condition already produces, can tell a widened budget from the exact one.
        var (invariantPlan, _, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var budget = LuxembourgEnumerationBudget.FromPlan(invariantPlan);

        Assert.AreEqual(1, budget.MaximumPagesFor(LuxembourgQueryPass.Pass1, 0));
        Assert.AreEqual(2, budget.MaximumPagesFor(LuxembourgQueryPass.Pass1, 10));
        Assert.AreEqual(2, budget.MaximumPagesFor(LuxembourgQueryPass.Pass1, 997));
        Assert.AreEqual(3, budget.MaximumPagesFor(LuxembourgQueryPass.Pass1, 998));
        Assert.AreEqual(2, budget.MaximumPagesFor(LuxembourgQueryPass.Pass2, 613));
        Assert.AreEqual(3, budget.MaximumPagesFor(LuxembourgQueryPass.Pass2, 614));

        // The negative guard, previously undriven: no caller in this codebase can reach a negative
        // selectedRowCount today (ParseStrictCount's NumberStyles.None parse admits no sign), but
        // MaximumPagesFor is public on a public type and the guard is what stops it silently
        // dividing a negative count by the page limit instead of refusing the call outright.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            budget.MaximumPagesFor(LuxembourgQueryPass.Pass1, -1));
    }

    [TestMethod]
    public async Task ACursorPairDifferingOnlyAboveTheBmpAdvancesUnderRawUtf8Order()
    {
        // U+E000 (private-use, BMP) sorts AFTER U+10000 (first supplementary plane character)
        // under UTF-16 code-unit ordering (string.CompareOrdinal), but BEFORE it under raw UTF-8
        // byte order: CursorComparisonUsesUtf8BytesRatherThanUtf16CodeUnits already pins this fact
        // about EnumerationCursorEnvelope.CompareRaw. This test pins that the executor's own
        // cursor-advance check actually goes through that comparator (via
        // LuxembourgQueryCursor.CompareTo) rather than any ordinal string comparison that would
        // invert this pair's order.
        const string firstByUtf8 = "";
        const string secondByUtf8 = "\U00010000";
        Assert.IsTrue(
            EnumerationCursorEnvelope.CompareRaw(firstByUtf8, secondByUtf8) < 0,
            "the vector must increase under raw UTF-8 byte order");
        Assert.IsTrue(
            string.CompareOrdinal(firstByUtf8, secondByUtf8) > 0,
            "the vector must invert under UTF-16 ordinal order");

        var (request, witness) = BuildRequestWithPartition(new LuxembourgQueryPartitionRange(
            "supplementary",
            new LuxembourgQueryCursor("", "", "", "", "", ""),
            new LuxembourgQueryCursor("\U0010FFFF", "", "", "", "", "")));
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(firstByUtf8, secondByUtf8)),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(firstByUtf8, secondByUtf8)),
            6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNotNull(result.Receipt);
        Assert.IsNull(result.Refusal);
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, result.Receipt.Delivery.Outcome);
    }

    [TestMethod]
    public async Task EveryLeafOfOneCoverSharesOneSessionAndOneRunIdentity()
    {
        // Step 8: "every leaf of one chain in ONE session, so the cover's one-run requirement
        // holds by construction rather than by a check across results." Two leaves, one row each,
        // both passes agreeing per leaf (EqualSelections), no root receipt supplied: LeafTilingOnly.
        // The assertion that actually distinguishes "one session" from "one session per leaf" is
        // the shared RunIdentity below - RunIdentity is minted once, in the session's constructor,
        // and stamped on every evidence document that session writes.
        var chain = LuxembourgPartitionChain.Root(LuxembourgAcquisitionTestFixture.FullRange())
            .SplitLeaf(
                "subjects-fixture",
                new LuxembourgQueryCursor("m", "", "", "", "", ""),
                "leaf-a",
                "leaf-b");
        var (rootRequest, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            // leaf-a ([∅, "m")), row "b": pass 1 then pass 2, each count/page/empty-page.
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("b")),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("b")),
            6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            // leaf-b (["m", "￿")), row "n": pass 1 then pass 2, each count/page/empty-page.
            7 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            8 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("n")),
            9 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            10 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            11 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("n")),
            12 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both leaves complete."),
        });

        var results = await RunCover(store, rootRequest, chain, witness, handler);

        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(static r => r.Receipt is not null && r.Refusal is null), "both leaves must deliver");
        var receipts = results.Select(static r => r.Receipt!).ToArray();
        Assert.AreEqual(
            receipts[0].Delivery.RunIdentity,
            receipts[1].Delivery.RunIdentity,
            "one shared session must mint one RunIdentity for every leaf");
        Assert.AreEqual(13, handler.SendCount, "robots plus 2 leaves x 2 passes x 3 sends");

        var cover = LuxembourgPartitionCover.TryCreate(chain, receipts, rootReceipt: null, out var refusal);

        Assert.IsNotNull(cover, $"refusal={refusal}");
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.None, refusal);
        Assert.AreEqual(LuxembourgPartitionCoverBasis.LeafTilingOnly, cover.Basis);
        Assert.AreEqual(2, cover.LeafDeliveredRowCountSum);
    }

    [TestMethod]
    public async Task ARootReceiptThatMatchesTheLeafSumMakesARootCountVerifiedCover()
    {
        // Step 8's second case: a root receipt is supplied whose DeliveredRowCountA equals the sum
        // of the leaves', so the cover's basis is RootCountVerified rather than LeafTilingOnly. The
        // root leg is an ordinary single-partition run (its own session; TryCreate does not require
        // the root's RunIdentity to match the leaves', only its partition key and row count), proven
        // over the same full range the chain was built from.
        var chain = LuxembourgPartitionChain.Root(LuxembourgAcquisitionTestFixture.FullRange())
            .SplitLeaf(
                "subjects-fixture",
                new LuxembourgQueryCursor("m", "", "", "", "", ""),
                "leaf-a",
                "leaf-b");
        var (rootRequest, witness) = BuildRequest();
        var leavesStore = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var leavesHandler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("b")),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("b")),
            6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            7 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            8 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("n")),
            9 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            10 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            11 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("n")),
            12 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both leaves complete."),
        });
        var leafResults = await RunCover(leavesStore, rootRequest, chain, witness, leavesHandler);
        var leafReceipts = leafResults.Select(static r => r.Receipt!).ToArray();
        Assert.IsTrue(leafReceipts.All(static r => r is not null), "both leaves must deliver before the root leg runs");

        // The root leg: the whole range in one pass pair, both rows on one page in ascending order,
        // delivering exactly 2 rows total, matching the leaves' sum.
        var rootStore = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var rootHandler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("b", "n")),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("b", "n")),
            6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after the root leg completes."),
        });
        var rootResult = await Run(rootStore, rootRequest, witness, rootHandler);

        Assert.IsNotNull(rootResult.Receipt);
        Assert.IsNull(rootResult.Refusal);
        Assert.AreEqual(2, rootResult.Receipt.Delivery.DeliveredRowCountA);

        var cover = LuxembourgPartitionCover.TryCreate(chain, leafReceipts, rootResult.Receipt, out var refusal);

        Assert.IsNotNull(cover, $"refusal={refusal}");
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.None, refusal);
        Assert.AreEqual(LuxembourgPartitionCoverBasis.RootCountVerified, cover.Basis);
        Assert.AreEqual(2, cover.LeafDeliveredRowCountSum);
    }

    // ---------------------------------------------------------------------------------------
    // Objection 2: what escaped the executor as an exception instead of becoming a refusal.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public async Task APageBodyThatIsNotJsonAtAllRefusesRatherThanEscaping()
    {
        // The mutation that kills this: delete `or System.Text.Json.JsonException` from the page
        // catch clause. Confirmed to turn this into an unhandled JsonException out of
        // RunPartitionAsync rather than a Refused result.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, "<html><body>502 Bad Gateway</body></html>"),
            _ => throw new AssertFailedException("No further sends after a body that is not a page."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.PageBodyMalformed, result.Refusal.Code);
        Assert.IsNotNull(result.Refusal.ResponseBodySha256);
    }

    [TestMethod]
    public async Task APageBodyWithNoBindingArrayRefusesRatherThanEscaping()
    {
        // Valid JSON, wrong document. This threw a bare FormatException with no Data tag, so the
        // old `when (exception.Data.Contains("oversizedKey"))` filter did not match it and it
        // escaped uncaught. The mutation that kills this: classify every caught FormatException as
        // DeliveredKeyNotRepresentable.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, "{\"head\":{\"link\":[],\"vars\":[]},\"results\":{\"distinct\":false}}"),
            _ => throw new AssertFailedException("No further sends after a document with no bindings."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.PageBodyMalformed, result.Refusal.Code);
    }

    [TestMethod]
    public async Task APageRowMissingAKeyRefusesRatherThanEscaping()
    {
        // The third escaping case, found while verifying the two the verdict named: a bindings
        // array whose row has no key_4. Same untagged FormatException, same escape.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var shortRow =
            "{\"head\":{\"link\":[],\"vars\":[\"key_1\"]},\"results\":{\"distinct\":false,\"ordered\":true,"
            + "\"bindings\":[{\"key_1\":{\"type\":\"literal\",\"value\":\"a\"},"
            + "\"key_2\":{\"type\":\"literal\",\"value\":\"\"},\"key_3\":{\"type\":\"literal\",\"value\":\"\"}}]}}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, shortRow),
            _ => throw new AssertFailedException("No further sends after a row missing a key."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(
            LuxembourgEnumerationRefusal.PageDecodeFailedOnOurSide,
            result.Refusal.Code,
            "a row missing a key is an omitted variable, which is ours to decode, not a page "
            + "the office malformed.");
    }

    [TestMethod]
    public async Task ACountBodyThatIsNotJsonAtAllRefusesRatherThanEscaping()
    {
        // The count route had the same hole in a different clause: catch (FormatException) with
        // JsonDocument.Parse inside it. The mutation that kills this: narrow the count catch back
        // to FormatException alone.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
            JsonResponse(req, "not json at all"));

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CountNotOneNonNegativeInteger, result.Refusal.Code);
        Assert.AreEqual(2, handler.SendCount, "robots and the count; no page is bound off a count that failed");
    }

    // ---------------------------------------------------------------------------------------
    // Residual objection 2 from the second refreeze verdict: JsonElement.TryGetProperty and
    // GetString throw InvalidOperationException on a wrong-kind element, which is neither
    // FormatException nor JsonException, so it was not covered by RunPassAsync's catch filter and
    // escaped RunPartitionAsync/RunCoverAsync as an unhandled exception instead of a typed refusal.
    // One driving test per named shape.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public async Task APageRowThatIsNotAnObjectRefusesRatherThanThrowing()
    {
        // A bindings array element that is not itself an object, e.g. "bindings":[[]]. Before this,
        // binding.TryGetProperty(name, ...) in ParseStrictRows ran unchecked on a JsonElement whose
        // ValueKind could be anything.
        //
        // The mutation that kills this: delete the `binding.ValueKind != JsonValueKind.Object`
        // check from ParseStrictRows. Confirmed: the run then throws InvalidOperationException out
        // of RunPartitionAsync instead of refusing.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(
                req,
                "{\"head\":{\"link\":[],\"vars\":[]},\"results\":{\"distinct\":false,\"ordered\":true,"
                + "\"bindings\":[[]]}}"),
            _ => throw new AssertFailedException("No further sends after a row that is not an object."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.PageBodyMalformed, result.Refusal.Code);
    }

    [TestMethod]
    public async Task ACountTermThatIsNotAnObjectRefusesRatherThanThrowing()
    {
        // A count body whose one binding value is not itself an object, e.g. {"count":"1"}. Before
        // this, term.TryGetProperty("type", ...) in ParseStrictCount ran unchecked on a JsonElement
        // whose ValueKind could be anything.
        //
        // The mutation that kills this: delete the `term.ValueKind != JsonValueKind.Object` check
        // from ParseStrictCount. Confirmed: the run then throws InvalidOperationException out of
        // RunPartitionAsync instead of refusing CountNotOneNonNegativeInteger.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var nonObjectTermCount =
            "{\"head\":{\"link\":[],\"vars\":[\"count\"]},\"results\":{\"distinct\":false,\"ordered\":true,"
            + "\"bindings\":[{\"count\":\"1\"}]}}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
            JsonResponse(req, nonObjectTermCount));

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CountNotOneNonNegativeInteger, result.Refusal.Code);
        Assert.AreEqual(2, handler.SendCount, "robots and the count only");
    }

    [TestMethod]
    [DataRow("type", DisplayName = "a non-string type")]
    [DataRow("datatype", DisplayName = "a non-string datatype")]
    public async Task ACountTermWithANonStringTypeOrDatatypeRefusesRatherThanThrowing(string field)
    {
        // The term is an object and both properties are present, but one of them is a JSON number
        // rather than a string. Before this, type.GetString()/datatype.GetString() in
        // ParseStrictCount ran unchecked on whichever JsonElement TryGetProperty returned.
        //
        // The mutation that kills this: delete the `type.ValueKind != JsonValueKind.String` (or the
        // datatype equivalent) check from ParseStrictCount. Confirmed: the run then throws
        // InvalidOperationException out of RunPartitionAsync instead of refusing.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var typeLiteral = field == "type" ? "1" : "\"typed-literal\"";
        var datatypeLiteral = field == "datatype" ? "1" : "\"http://www.w3.org/2001/XMLSchema#integer\"";
        var nonStringFieldCount =
            "{\"head\":{\"link\":[],\"vars\":[\"count\"]},\"results\":{\"distinct\":false,\"ordered\":true,"
            + "\"bindings\":[{\"count\":{\"type\":" + typeLiteral + ",\"datatype\":" + datatypeLiteral
            + ",\"value\":\"1\"}}]}}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
            JsonResponse(req, nonStringFieldCount));

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CountNotOneNonNegativeInteger, result.Refusal.Code);
        Assert.AreEqual(2, handler.SendCount, "robots and the count only");
    }

    // ---------------------------------------------------------------------------------------
    // Residual from the third refreeze verdict: the same escape as objection 2 above, but one
    // guard earlier. results.TryGetProperty("bindings", ...) ran in both parsers on whichever
    // JsonElement root.TryGetProperty("results", ...) returned, unchecked, so a body such as
    // {"results":5} or {"results":[]} threw InvalidOperationException past both catch filters and
    // the outer catch instead of refusing. Plus the term-kind guard the second refreeze added but
    // never drove.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("5", DisplayName = "results is a number")]
    [DataRow("[]", DisplayName = "results is an array")]
    public async Task ACountResultsThatIsNotAnObjectRefusesRatherThanThrowing(string resultsLiteral)
    {
        // Before this, results.TryGetProperty("bindings", ...) in ParseStrictCount ran unchecked on
        // whichever JsonElement root.TryGetProperty("results", ...) returned.
        //
        // The mutation that kills this: delete the `results.ValueKind != JsonValueKind.Object`
        // check from ParseStrictCount. Confirmed: the run then throws InvalidOperationException out
        // of RunPartitionAsync instead of refusing CountNotOneNonNegativeInteger.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var nonObjectResultsCount =
            "{\"head\":{\"link\":[],\"vars\":[\"count\"]},\"results\":" + resultsLiteral + "}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
            JsonResponse(req, nonObjectResultsCount));

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CountNotOneNonNegativeInteger, result.Refusal.Code);
        Assert.AreEqual(2, handler.SendCount, "robots and the count only");
    }

    [TestMethod]
    [DataRow("5", DisplayName = "results is a number")]
    [DataRow("[]", DisplayName = "results is an array")]
    public async Task APageResultsThatIsNotAnObjectRefusesRatherThanThrowing(string resultsLiteral)
    {
        // Before this, results.TryGetProperty("bindings", ...) in ParseStrictRows ran unchecked on
        // whichever JsonElement root.TryGetProperty("results", ...) returned.
        //
        // The mutation that kills this: delete the `results.ValueKind != JsonValueKind.Object`
        // check from ParseStrictRows. Confirmed: the run then throws InvalidOperationException out
        // of RunPartitionAsync instead of refusing PageBodyMalformed.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var nonObjectResultsPage = "{\"head\":{\"link\":[],\"vars\":[]},\"results\":" + resultsLiteral + "}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, nonObjectResultsPage),
            _ => throw new AssertFailedException("No further sends after a page whose results is not an object."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.PageBodyMalformed, result.Refusal.Code);
    }

    [TestMethod]
    public async Task AKeyTermThatIsAStringRatherThanAnObjectRefusesRatherThanThrowing()
    {
        // ParseStrictRows' key loop checks `term.ValueKind != JsonValueKind.Object` before calling
        // term.TryGetProperty("value", ...), added alongside the row-not-an-object and
        // results-not-an-object guards above, but no test drove this exact branch: every existing
        // malformed-row test either omits a key_N entirely (TryGetProperty returns false, a
        // different branch of the same condition) or gives it a well-formed object whose "value" is
        // wrong. This drives the case where key_1 is present but is a bare JSON string, not an
        // object, so TryGetProperty succeeds and the ValueKind check is the one that has to catch
        // it.
        //
        // The mutation that kills this: delete the `term.ValueKind != JsonValueKind.Object` check
        // from ParseStrictRows' key loop. Confirmed: the run then throws InvalidOperationException
        // out of term.TryGetProperty("value", ...) instead of refusing PageBodyMalformed.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var stringKeyTermRow =
            "{\"head\":{\"link\":[],\"vars\":[\"key_1\",\"key_2\",\"key_3\",\"key_4\",\"key_5\",\"key_6\"]},"
            + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{"
            + "\"key_1\":\"a\","
            + "\"key_2\":{\"type\":\"literal\",\"value\":\"\"},\"key_3\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"key_4\":{\"type\":\"literal\",\"value\":\"\"},\"key_5\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"key_6\":{\"type\":\"literal\",\"value\":\"\"}}]}}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, stringKeyTermRow),
            _ => throw new AssertFailedException("No further sends after a row whose key_1 term is a string."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.PageBodyMalformed, result.Refusal.Code);
    }

    /// <summary>
    /// A ROW WHOSE VARIABLE IS LEGITIMATELY UNBOUND IS OUR DECODE LIMIT, NOT A MALFORMED PAGE, and
    /// the refusal has to say so rather than blaming the office.
    /// </summary>
    /// <remarks>
    /// THE PAGE BELOW IS SYNTHETIC AND IS STATED AS SYNTHETIC. It is not a capture of anything
    /// legilux served; it is hand-built to omit key_6 from one binding while declaring it in head
    /// vars, which is exactly what the SPARQL JSON format does for an unbound variable. Lane B
    /// established the mechanism at the endpoint
    /// (lex-event-20260905T022432768Z-43d363d14f524f87b9f8063afe398de6): the engine picks the
    /// conditional's branch correctly but evaluates both arms, the string conversion raises on the
    /// absent term, the erroring BIND leaves the variable unbound, and the format omits it. Whether
    /// this lane's own template wants COALESCE is a separate question and is not what this test is
    /// about.
    /// <para>
    /// The neighbouring test above is the CONTRAST that makes this one mean something: there key_1
    /// is PRESENT but is a bare string, which is a statement about what the office served and still
    /// refuses as PageBodyMalformed. Present-but-misshapen stays theirs; absent is ours.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task AnUnboundVariableInARowIsOurDecodeFailureAndNotTheOfficesMalformedPage()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };

        // SYNTHETIC: key_6 is declared in head vars and omitted from the binding, the shape SPARQL
        // produces for an unbound variable. Never captured from the publisher.
        var unboundVariableRow =
            "{\"head\":{\"link\":[],\"vars\":[\"key_1\",\"key_2\",\"key_3\",\"key_4\",\"key_5\",\"key_6\"]},"
            + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{"
            + "\"key_1\":{\"type\":\"literal\",\"value\":\"a\"},"
            + "\"key_2\":{\"type\":\"literal\",\"value\":\"\"},\"key_3\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"key_4\":{\"type\":\"literal\",\"value\":\"\"},\"key_5\":{\"type\":\"literal\",\"value\":\"\"}}]}}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, unboundVariableRow),
            _ => throw new AssertFailedException("No further sends after a row with an unbound variable."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(
            LuxembourgEnumerationRefusal.PageDecodeFailedOnOurSide,
            result.Refusal.Code,
            "an omitted variable is a decode limit of ours; naming it PageBodyMalformed would make "
            + "the product state a falsehood about the office.");
        Assert.AreNotEqual(
            LuxembourgEnumerationRefusal.PageBodyMalformed,
            result.Refusal.Code,
            "and specifically not the publisher-facing name it used to carry.");
    }

    // ---------------------------------------------------------------------------------------
    // Objection 3: successors for the preserved RED tests at f33a82f3 that had none.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public async Task ACountTermOnTheWrongWireTypeIsRefusedBeforeAnyPageIsBound()
    {
        // Successor to ReadCountRejectsTheWrongLuxembourgLiteralWireType. The LU dialect is
        // Virtuoso, whose count term is "typed-literal"; a plain "literal" carrying the same
        // lexical value is a different wire shape and is not admitted. Before this, the guard in
        // ParseStrictCount that reads the term type and datatype was undriven.
        //
        // The mutation that kills this: drop `type.GetString() != "typed-literal" ||` from
        // ParseStrictCount's term check. Confirmed: the run then proceeds to bind a page.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        // "literal", not "typed-literal", but carrying the correct xsd:integer datatype. That
        // isolates the wire-type check: a bare plain literal would also be refused, but by the
        // datatype check beside it, so a mutation removing the wire-type comparison survived that
        // version of this test. This is the non-Virtuoso dialect shape the preserved RED test
        // ReadCountRejectsTheWrongLuxembourgLiteralWireType named.
        var plainLiteralCount =
            "{\"head\":{\"link\":[],\"vars\":[\"count\"]},\"results\":{\"distinct\":false,\"ordered\":true,"
            + "\"bindings\":[{\"count\":{\"type\":\"literal\",\"value\":\"1\","
            + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\"}}]}}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
            JsonResponse(req, plainLiteralCount));

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CountNotOneNonNegativeInteger, result.Refusal.Code);
        Assert.AreEqual(2, handler.SendCount, "robots and the count only");
    }

    [TestMethod]
    [DataRow("datatype", DisplayName = "a typed literal")]
    [DataRow("iri", DisplayName = "an IRI term")]
    public async Task AQualifiedOrNonPlainCursorTermIsRefusedThroughCore(string kind)
    {
        // Successor to the datatype and IRI cases of ReadPageRejectsQualifiedOrNonPlainCursorTerms.
        // The language case already had one (CoreRefusalsAreCarriedNotReclassified); these two did
        // not, which is exactly what the verdict said.
        //
        // The rule lives in Source/Core, not here: RepeatedEnumerationDeliveryProof.VerifyPages,
        // "Cursor projections must be plain literals matching the query comparator". A copy of it
        // in ParseStrictRows was written and deleted during this refreeze, because it would have
        // been a second place for one invariant and would have reclassified a Core refusal as an
        // executor one. What this test pins is that the rule is REACHABLE through the executor and
        // that the executor carries Core's own message rather than paraphrasing it.
        //
        // The mutation that kills this: change the executor's DeliveryProofRefused branch to drop
        // set.LastCoreRefusalMessage, and the "plain literal" assertion fails.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var badTerm = kind == "datatype"
            ? "{\"type\":\"typed-literal\",\"value\":\"a\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#string\"}"
            : "{\"type\":\"uri\",\"value\":\"http://data.legilux.public.lu/resource/a\"}";
        var badRow =
            "{\"head\":{\"link\":[],\"vars\":[\"key_1\",\"key_2\",\"key_3\",\"key_4\",\"key_5\",\"key_6\"]},"
            + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{"
            + "\"key_1\":" + badTerm + ","
            + "\"key_2\":{\"type\":\"literal\",\"value\":\"\"},\"key_3\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"key_4\":{\"type\":\"literal\",\"value\":\"\"},\"key_5\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"key_6\":{\"type\":\"literal\",\"value\":\"\"}}]}}";
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 => JsonResponse(req, badRow),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("a")),
            6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.DeliveryProofRefused, result.Refusal.Code);
        Assert.IsNotNull(result.Refusal.CoreRefusalDetail);
        StringAssert.Contains(result.Refusal.CoreRefusalDetail!, "plain literal");
    }

    [TestMethod]
    public async Task APageDeliveringMoreRowsThanItsLimitIsRefusedThroughCore()
    {
        // Successor to ReadPageRejectsAResponseBeyondTheRowLimit. Same reasoning as the test above:
        // the rule is Source/Core's ("The page exceeds its row limit", VerifyPages), the executor
        // does not keep a second copy, and this proves the rule is reachable from a real run.
        //
        // The page limit is read from the plan rather than written as a literal, so a plan change
        // moves the test with it instead of leaving a stale magic number behind.
        var (request, witness) = BuildRequest();
        var limit = LuxembourgEnumerationBudget.FromPlan(request.InvariantPlan)
            .PageRowLimitFor(LuxembourgQueryPass.Pass1);
        var overLimit = Enumerable.Range(0, checked((int)limit) + 1)
            .Select(static ordinal => "k" + ordinal.ToString("D6", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.AreEqual(limit + 1, (uint)overLimit.Length);

        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(overLimit.Length)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(overLimit)),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(overLimit.Length)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(overLimit)),
            6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.DeliveryProofRefused, result.Refusal.Code);
        Assert.IsNotNull(result.Refusal.CoreRefusalDetail);
        StringAssert.Contains(result.Refusal.CoreRefusalDetail!, "row limit");
    }

    // ---------------------------------------------------------------------------------------
    // Objection 6, and the two unreachable constructor invariants in the notes.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void TheFallbackFreeDoubleRefusesTheSharedFixtureTable()
    {
        // Not a tautology: this drives the flag itself, with a digest the shared table really does
        // answer. Without it, every Luxembourg executor test would be running against the same
        // reopen-by-reference shape item 1b deleted from the product path, and "the run held its
        // dependency" would be indistinguishable from "the fixture happened to know it".
        var seeded = MachineRequestTestFixture.ContentTypeRegistry.Sha256;

        var permissive = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
        var served = permissive.ReadByDigestAsync(seeded, CancellationToken.None).GetAwaiter().GetResult();
        Assert.IsTrue(served.Length > 0);
        Assert.AreEqual(1, permissive.FallbackHits);

        var strict = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        Assert.ThrowsExactly<AssertFailedException>(() =>
            strict.ReadByDigestAsync(seeded, CancellationToken.None).GetAwaiter().GetResult());
        Assert.AreEqual(0, strict.FallbackHits);
    }

    [TestMethod]
    public void ARefusalDetailCannotCarryTheNoneCode()
    {
        // The verdict called this invariant unreachable and undriven. Half right: the constructor
        // is internal, so it is reachable from this assembly, and it is driven from here now.
        var thrown = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LuxembourgEnumerationRefusalDetail(
                LuxembourgEnumerationRefusal.None, null, null, null, null, null, null, [], null));
        StringAssert.Contains(thrown.Message, "real refusal code");
    }

    [TestMethod]
    public void ARunResultCannotReportANegativeRequestCount()
    {
        // The other half of the same note. This one is genuinely reachable from outside: both
        // public factories take the count from their caller. The "delivered or refused, never
        // both and never neither" check beside it was NOT reachable and has been removed rather
        // than left standing as defense nothing can trip; LuxembourgConstructionSurfaceTests is
        // what now holds "there is no third door".
        var (request, _) = BuildRequest();
        _ = request;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LuxembourgEnumerationRunResult.Refused(
                new LuxembourgEnumerationRefusalDetail(
                    LuxembourgEnumerationRefusal.PageBudgetExhausted,
                    null, null, null, null, null, null, [], null),
                productRequestCount: -1));
    }

    // ---------------------------------------------------------------------------------------
    // Objection 1(b): the receipt actually reaches AbsenceCut.TryCreateComplete.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public async Task AGenuineDeliveryReceiptMintsACompleteAbsenceCut()
    {
        // The freeze packet this refreeze replaces claimed AbsenceCut.TryCreateComplete was wired
        // in while nothing in the LU code referenced AbsenceCut at all. This is that claim made
        // true, end to end and from a real run: two agreeing passes over a real session, a real
        // delivery receipt, then the receipt's own bridge to a family enumeration proof, then a
        // complete cut that the Absence lane admits no family into without one.
        var partitionId = "lu_subjects_proof";
        var (request, witness) = BuildRequestWithPartition(
            LuxembourgAcquisitionTestFixture.FullRange(partitionId));
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 or 5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("a", "b")),
            3 or 6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNotNull(result.Receipt, $"refusal={result.Refusal?.Code}");
        Assert.AreEqual(CustodyMembership.Floored, result.Receipt.RetainedFloor);
        Assert.AreEqual(partitionId, result.Receipt.Delivery.PartitionKey);

        var proof = result.Receipt.TryProveFamilyEnumeration(partitionId, out var proofRefusal);
        Assert.IsNotNull(proof, $"proof refused as {proofRefusal}");
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.None, proofRefusal);
        Assert.AreEqual(partitionId, proof.FamilyKey);
        Assert.AreEqual(2, proof.DeliveredRowCount);

        var observation = AbsenceFamilyObservation.TryCreate(
            "lu_observation_1",
            partitionId,
            new DateTimeOffset(2026, 9, 3, 0, 0, 30, TimeSpan.Zero),
            AbsenceTimestampPrecision.Second,
            "lex-ops-ntp-1",
            TimeSpan.FromSeconds(30),
            AbsenceObservationProvenance.FreshlyExecuted,
            out var observationRefusal);
        Assert.IsNotNull(observation, $"observation refused as {observationRefusal}");

        var cut = AbsenceCut.TryCreateComplete(
            "lu_run_1",
            AbsenceApplicableSet.ObservedRootSet,
            [observation],
            [proof],
            Artifact('e'),
            Artifact('c'),
            ["https://data.legilux.public.lu/eli/etat/leg/loi/2004/11/12/n1"],
            out var cutRefusal);

        Assert.IsNotNull(cut, $"cut refused as {cutRefusal}");
        Assert.AreEqual(AbsenceCutRefusal.None, cutRefusal);
        Assert.AreEqual(AbsenceRunCompletion.EnumerationComplete, cut.Completion);
        Assert.AreEqual(1, cut.EnumerationProofs.Count);
        Assert.AreSame(proof, cut.EnumerationProofs[0]);
    }

    // ---------------------------------------------------------------------------------------
    // Objection 1(d): a full run to Delivered against a real store, not a seeded double.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public async Task AFullRunDeliversAgainstARealFileSystemStoreOnAnEmptyDirectory()
    {
        // What the verdict asked for and what is actually reachable, stated exactly.
        //
        // Every byte here goes through a real FileSystemCustodyStore rooted at a fresh empty
        // directory: every write is a real file, every read comes back off disk, every digest is
        // recomputed, and there is no seeded table and no fallback of any kind. Both routes run:
        // the count sends for real, the executor retains the count's own evidence document, and
        // the page bind then reopens THAT reference by digest, which is the shape item 4a's EU
        // page proof had to arrange by hand and which this executor does as a matter of course.
        // Materialization afterwards reopens every artifact out of the same directory.
        //
        // The one thing wrapped is the protection each write receipt publishes. This is not
        // cosmetic and is not hidden: FileSystemCustodyStore publishes NotEnforced for every class
        // by design (Decision 71).
        //
        // THIS REMARK USED TO SAY the executor's floor gate refuses custody_floor_not_observed
        // before the first product request, and cited a test by its old name as proof. Both halves
        // are stale. That gate is gone and the member with it, because the extended Decision 71
        // interpretation (lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c) records
        // the class and continues; the test is now
        // AFilesystemDeploymentRunsAndItsReceiptCarriesRetainedUnenforced and proves the opposite,
        // that the run reaches its product requests. A bare FileSystemCustodyStore still cannot
        // reach Delivered TODAY, for a different reason and one owned by another lane: the shared
        // Source/Core tuple bind refuses an observation whose body receipt is not ImmutableObject1
        // and LockedTime. THAT REASON EXPIRES. Correction
        // lex-event-20260904T230312457Z-5d1f7352c46b4ca0ac8c71ca6a4aa1fb drops profile and protection from
        // that bind, keeping the anti-forgery digest comparison and the NightlyFloor90d class
        // check, so once lane B lands it a bare FileSystemCustodyStore does reach Delivered. That
        // is the point of the ruling, not a regression.
        //
        // RESIDUE, stated rather than papered over: the only store in this repository that
        // genuinely publishes enforcement is AzureBlobCustodyStore, which no unit test can reach.
        // So this proves the product route against real durable storage and a real reopen path,
        // and it does NOT prove the classification of a genuinely enforced provider. That gap
        // closes when an Azure-backed integration test exists, not here.
        var root = Path.Combine(Path.GetTempPath(), "lex-lu-executor-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.AreEqual(0, CountFiles(root), "the store must start holding nothing at all");

            var partitionId = "lu_real_store";
            var (request, witness) = BuildRequestWithPartition(
                LuxembourgAcquisitionTestFixture.FullRange(partitionId));
            var store = new EnforcingCustodyStore(new FileSystemCustodyStore(root));
            var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
            {
                1 or 4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(2)),
                2 or 5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson("a", "b")),
                3 or 6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
                _ => throw new AssertFailedException("No further sends after both passes complete."),
            });

            var result = await Run(store, request, witness, handler);

            Assert.IsNotNull(result.Receipt, $"refusal={result.Refusal?.Code} detail={result.Refusal?.CoreRefusalDetail}");
            Assert.IsNull(result.Refusal);
            Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, result.Receipt.Delivery.Outcome);
            Assert.AreEqual(2, result.Receipt.Delivery.DeliveredRowCountA);
            Assert.AreEqual(2, result.Receipt.Delivery.DeliveredRowCountB);
            Assert.AreEqual(CustodyMembership.Floored, result.Receipt.RetainedFloor);
            Assert.AreEqual(7, handler.SendCount, "robots, then a count and two pages for each pass");

            // Readable back off the disk by digest, which is the difference between having
            // retained an artifact and having named one. Every member of the receipt, not a
            // sample: the send closure, the evidence documents, the response bodies and their
            // write receipts are all in RetainedMembership now (objection 4), so this walks the
            // whole thing.
            // Derived from the delivery, not a hand-tuned number. Every reference set the
            // comparison names must be in the receipt's membership, and the membership must hold
            // strictly more than those, because the response bodies and their write receipts are
            // members now too (objection 4) and no reference set names them.
            var referenced = new[] { result.Receipt.Delivery.CountA, result.Receipt.Delivery.CountB }
                .Concat(result.Receipt.Delivery.PagesA.Pages.Select(static page => page.Evidence))
                .Concat(result.Receipt.Delivery.PagesB.Pages.Select(static page => page.Evidence))
                .SelectMany(static refs => new[]
                {
                    refs.QueryPlanRef.Sha256, refs.QueryInputRef.Sha256, refs.RenderReceiptRef.Sha256,
                    refs.LogicalRequestRef.Sha256, refs.HttpEvidenceRef.Sha256,
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (var digest in referenced)
            {
                Assert.IsTrue(
                    result.Receipt.RetainedMembership.ContainsKey(digest),
                    $"the receipt does not classify {digest}, which its own comparison names");
            }

            Assert.IsTrue(
                result.Receipt.RetainedMembership.Count > referenced.Length,
                "the response bodies and their write receipts must be members, not a list beside the floor");
            var bare = new FileSystemCustodyStore(root);
            foreach (var digest in result.Receipt.RetainedMembership.Keys)
            {
                var reopened = await bare.ReadByDigestAsync(digest, CancellationToken.None);
                Assert.AreEqual(
                    digest,
                    Convert.ToHexStringLower(
                        System.Security.Cryptography.SHA256.HashData(reopened.Span)),
                    $"the store does not hold {digest}, which the receipt says it retained");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A real <see cref="FileSystemCustodyStore"/> for every byte: every write is a real file on
    /// disk, every read is a real read off disk, nothing is seeded and nothing falls back. The
    /// ONLY thing it changes is the protection each write receipt publishes, from the filesystem
    /// adapter's honest <see cref="CustodyProtection.NotEnforced"/> to a locked-time claim, so the
    /// executor's floor gate opens and the product route can be exercised at all.
    /// </summary>
    /// <remarks>
    /// Everything a store can get wrong about content is still the real store's job here:
    /// persistence, content addressing, reopening, and refusing a digest it does not hold. What is
    /// substituted is one field of the policy evidence, which is exactly the field no in-repo
    /// store can produce truthfully.
    /// </remarks>
    private sealed class EnforcingCustodyStore(ICustodyStore inner) : ICustodyStore
    {
        private static readonly DateTimeOffset ObservedAt = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);

        public async Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var receipt = await inner.CreateAsync(bytes, custodyClass, cancellationToken);
            return new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                receipt.Reference,
                new CustodyPolicyEvidence(
                    CustodySchemaIds.CustodyPolicyEvidence,
                    receipt.Reference,
                    CustodyVerificationProfile.ImmutableObject1,
                    Guid.Parse("00000000-0000-0000-0000-0000000000d1"),
                    CustodyProtection.LockedTime,
                    ObservedAt,
                    ObservedAt.AddDays(91)));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }

    private static SourceArtifactRef Artifact(char fill) =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000b1", new string(fill, 64));

    private static string Letter(int ordinal)
    {
        // 0-indexed sequence of increasing single-letter keys: a, b, c, ...
        var value = (char)('a' + ordinal);
        return value.ToString();
    }

    private static (LuxembourgPartitionRunRequest Request, BoundMachineRequest Witness) BuildRequest() =>
        BuildRequestWithPartition(LuxembourgAcquisitionTestFixture.FullRange());

    private static (LuxembourgPartitionRunRequest Request, BoundMachineRequest Witness) BuildRequestWithPartition(
        LuxembourgQueryPartitionRange partition)
    {
        var (invariantPlan, invariantPlanResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var rendererSource = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var request = new LuxembourgPartitionRunRequest(
            invariantPlan, invariantPlanResourceId, LuxembourgAcquisitionTestFixture.SubjectsSetId,
            partition, rendererSource);
        var witness = invariantPlan.BindCount(
            invariantPlanResourceId, $"urn:uuid:{Guid.NewGuid():D}", $"urn:uuid:{Guid.NewGuid():D}",
            LuxembourgAcquisitionTestFixture.SubjectsSetId, LuxembourgQueryPass.Pass1, partition, rendererSource);
        return (request, witness.Request);
    }

    /// <summary>
    /// A CustodyRequiredException raised during the run becomes the typed
    /// <see cref="LuxembourgEnumerationRefusal.CustodyMemberMissing"/> rather than escaping.
    /// </summary>
    /// <remarks>
    /// This arm was DRIVEN BY NO TEST while the argument for deleting the old custody floor gate
    /// rested on BOTH its exception kinds surfacing here. An argument for removing a guard that
    /// leans on an untested catch is exactly the shape that let the absent-artifact mutation
    /// survive earlier in this lane, so it is driven now rather than asserted in a comment.
    /// </remarks>
    [TestMethod]
    public async Task ACustodyRequiredFailureDuringTheRunBecomesCustodyMemberMissing()
    {
        var (request, witness) = BuildRequest();
        var root = Path.Combine(Path.GetTempPath(), "lex-lu-custody-required-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // TWO INSTRUMENTS, NOT ONE, because neither alone lands the fault where this test needs it.
        // The HANDLER GATE SELECTS THE PHASE: armed turns true on the first product request, so no
        // threshold can fire inside the robots bootstrap, whose own write volume is an
        // implementation detail that would otherwise give RobotsBootstrapRefused, a true refusal
        // about the wrong thing. THE COUNT SELECTS THE WRITE: calibrated to this fixture's clean
        // run of exactly 50 custody writes, arming after 49 puts the failure in THE 50TH WRITE,
        // during delivery-evidence materialisation, inside the outer try this test is about.
        // Arming earlier lands in the send path and is caught as ObservationNotExecuted instead.
        // Both neighbours were measured, not assumed: 48 and 44 give ObservationNotExecuted, 60
        // gives DeliveryProofRefused, and arming from the first write gives RobotsBootstrapRefused.
        // If that write count ever drifts, this test fails rather than silently arming elsewhere.
        var armed = new StrongBox<bool>(false);
        var store = new CustodyRequiredAfterBootstrapStore(new FileSystemCustodyStore(root), armed)
        {
            ArmAfter = 49,
        };
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) =>
        {
            armed.Value = true;
            return ordinal is 1 or 3
                ? JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(0))
                : JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson());
        });

        try
        {
            var result = await Run(store, request, witness, handler);
            Assert.IsNotNull(result.Refusal, "a custody failure is a typed refusal, never an escape.");
            Assert.AreEqual(LuxembourgEnumerationRefusal.CustodyMemberMissing, result.Refusal!.Code);
            Assert.IsNull(result.Receipt);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Refuses every write once the robots bootstrap has completed, so the failure lands inside the
    /// run rather than before it and reaches the catch this test is about.
    /// </summary>
    private sealed class CustodyRequiredAfterBootstrapStore(ICustodyStore inner, StrongBox<bool> armed)
        : ICustodyStore
    {
        internal int Writes;

        internal int ArmAfter = int.MaxValue;

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref Writes);
            if (armed.Value && n > ArmAfter)
            {
                throw new CustodyRequiredException("the store refused a write during the run.");
            }

            return inner.CreateAsync(bytes, custodyClass, cancellationToken);
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken) =>
            inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }

    /// <summary>
    /// Builds the executor with the test-only handler-injection constructor (internal, same shape
    /// as production's public constructor plus one nullable parameter) and runs it. Together with
    /// <see cref="RunCover"/>, these are the only two places in this file that construct an
    /// executor, so every test drives one of the two production entry points, RunPartitionAsync or
    /// RunCoverAsync, with a fake transport substituted underneath it.
    /// </summary>
    private static Task<LuxembourgEnumerationRunResult> Run(
        ICustodyStore store,
        LuxembourgPartitionRunRequest request,
        BoundMachineRequest witness,
        HttpMessageHandler handler)
    {
        var executor = new LuxembourgRepeatedEnumerationExecutor(
            store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        return executor.RunPartitionAsync(request, witness, CancellationToken.None);
    }

    /// <summary>See <see cref="Run"/>; drives RunCoverAsync instead of RunPartitionAsync.</summary>
    private static Task<IReadOnlyList<LuxembourgEnumerationRunResult>> RunCover(
        ICustodyStore store,
        LuxembourgPartitionRunRequest rootRequest,
        LuxembourgPartitionChain chain,
        BoundMachineRequest witness,
        HttpMessageHandler handler)
    {
        var executor = new LuxembourgRepeatedEnumerationExecutor(
            store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        return executor.RunCoverAsync(rootRequest, chain, witness, CancellationToken.None);
    }

    private static int CountFiles(string root) =>
        Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length : 0;

    private static HttpResponseMessage TextResponse(HttpRequestMessage request, string body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
        content.Headers.TryAddWithoutValidation(
            "Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
    }

    private static HttpResponseMessage JsonResponse(HttpRequestMessage request, string body) =>
        LuxembourgAcquisitionTestFixture.JsonResponse(request, body);
}
