using System.Net;
using System.Net.Http;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Luxembourg repeated-enumeration executor: the driving algorithm of design section 2, one
/// test per typed refusal in section 3, plus the SR353 structural properties of section 5.
/// </summary>
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.RobotsBootstrapRefused, result.Refusal.Code);
        Assert.AreEqual(0, result.ProductRequestCount);
    }

    [TestMethod]
    public async Task AFilesystemDeploymentSaysSoBeforeTheFirstProductRequest()
    {
        var (request, witness) = BuildRequest();
        var root = Path.Combine(Path.GetTempPath(), "lex-lu-executor-floor-" + Guid.NewGuid().ToString("N"));
        var baselineRoot = Path.Combine(Path.GetTempPath(), "lex-lu-executor-floor-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(baselineRoot);
        try
        {
            var store = new FileSystemCustodyStore(root);
            var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
                throw new AssertFailedException("No product request should follow an unfloored custody observation."));

            var result = await Run(store, request, witness, handler);
            var afterCount = CountFiles(root);

            Assert.IsNull(result.Receipt);
            Assert.IsNotNull(result.Refusal);
            Assert.AreEqual(LuxembourgEnumerationRefusal.CustodyFloorNotObserved, result.Refusal.Code);
            Assert.AreEqual(0, result.ProductRequestCount);
            Assert.IsTrue(result.Refusal.UnenforcedDigests.Count > 0);

            // The baseline: exactly what robots bootstrap alone writes against an identical fresh
            // store, using the same handler and clock so the byte-for-byte retained set matches.
            // Comparing to this rather than to zero is the honest form of "the executor wrote
            // nothing": robots bootstrap unavoidably writes the artifacts the floor check reads.
            var baselineStore = new FileSystemCustodyStore(baselineRoot);
            var baselineHandler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, req) =>
                throw new AssertFailedException("The baseline run must never reach a product request either."));
            using var baselineSession = await LuxembourgAcquisitionTestFixture.StartedSessionAsync(
                witness, baselineHandler, baselineStore, new LuxembourgAcquisitionTestFixture.FixedTimeProvider());
            var baselineCount = CountFiles(baselineRoot);

            Assert.AreEqual(
                baselineCount,
                afterCount,
                "the executor must write nothing beyond what the robots bootstrap itself already wrote");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(baselineRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task APreHeaderTransportFailureRefusesWithoutAProof()
    {
        var (request, witness) = BuildRequest();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, _) =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "simulated pre-header failure"));
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();

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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
        // A count one below the ceiling, followed by an (immediately, honestly mismatched) empty
        // page: this test's only claim is that binding and sending a page happened at all, not
        // that a 999,999-row pass completes inside a unit test.
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(999_999)),
            _ => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
        });

        var result = await Run(store, request, witness, handler);

        // Whatever this run ultimately decides (it cannot complete a 999,999-row pass in a unit
        // test), it must not be partition_required, and a page must actually have been requested.
        Assert.AreNotEqual(LuxembourgEnumerationRefusal.PartitionRequired, result.Refusal?.Code);
        Assert.IsTrue(handler.SendCount >= 3, "robots, count, and at least one page send");
    }

    [TestMethod]
    public async Task AKeyLongerThanACursorPartStopsTheEnumeration()
    {
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
        var oversized = new string('a', 2100);
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var inner = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
        var store = new EvictingCustodyStore(inner);
        var rows = Enumerable.Range(0, 3).Select(Letter).ToArray();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(3)),
            2 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(rows)),
            3 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            4 => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(3)),
            5 => JsonResponse(req, LuxembourgAcquisitionTestFixture.RowsJson(rows)),
            6 => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });

        var result = await Run(store, request, witness, handler);

        Assert.IsNull(result.Receipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgEnumerationRefusal.CustodyMemberMissing, result.Refusal.Code);
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
    /// A store that writes and reads normally, except that one large object (the LU invariant
    /// plan, retained as the renderer's "profile") starts refusing reads once it has already been
    /// read back the exact number of times this fixture's fixed six-send, two-pass shape reads it
    /// during normal send-time reopens and the session's own artifact-membership rereads (measured
    /// empirically against a non-evicting run of this exact fixture shape: 12). The next read is
    /// MaterializeAsync's own reopen, which is exactly what this test targets: everything the run
    /// needed was genuinely written, and then became unreadable before materialization, which
    /// seeding around would make impossible to distinguish from a resolver that never really
    /// reopens anything.
    /// </summary>
    private sealed class EvictingCustodyStore(ICustodyStore inner) : ICustodyStore
    {
        private const int SurvivedReads = 12;

        private readonly object _gate = new();
        private string? _targetDigest;
        private int _readCount;

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
                    _readCount++;
                    if (_readCount > SurvivedReads)
                    {
                        throw new CustodyIntegrityException("simulated post-write eviction");
                    }
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var leavesStore = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
        var rootStore = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore();
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
