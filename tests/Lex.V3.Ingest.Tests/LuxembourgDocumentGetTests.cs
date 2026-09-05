using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-06c-LU-2 items 3, 4 and 5: the Luxembourg document GET actually sent through
/// <see cref="RoutedHttpAcquisitionSession"/>, its per-object refusal mapping, and
/// <see cref="CorpusRecordSetWriter.WriteAsync"/> as the run's last step.
/// </summary>
/// <remarks>
/// WHY THESE DRIVE THE PHASE DIRECTLY RATHER THAN THE WHOLE RunAsync, stated plainly because it is
/// the honest answer to the scope ruling's own question about the accepted fraction of a real LU
/// manifest. THAT FRACTION USED TO BE ZERO OF N, structurally, because
/// <c>LuxembourgBodyJoin.ResolveCandidate</c> attached eight unconditional blockers to every
/// candidate and <c>LuxembourgScopeResolver.ResolveBody</c> had no accepting branch at all. It is
/// no longer zero: the owner ingest principle
/// (lex-event-20260904T205636383Z-e92b888b62c24df29fe3f8c1be5016f0) opened the arm, and
/// <see cref="TheAcceptedFractionIsZeroWithoutAWordingManifestationAndOneWithIt"/> now measures the
/// fraction over a real reduction instead of the deleted reflection test that measured nothing.
/// A manifest with an accepted body axis is still built directly here, as the EU lane's own tests
/// build one, so the fetch, the classification and the record set can be exercised without also
/// re-testing the reduction.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgDocumentGetTests
{
    // The real, live-verified filestore XML manifestation: this exact path returned HTTP 200 with
    // genuine Akoma Ntoso, 19,986 bytes, SHA-256
    // 9e43a99e4b9735e383d989989d4005fc9e1676f4094c2633f30b2f056d5e476d. The path is real. Tests
    // that need only SOME body script a one-element <akomaNtoso/>, which is NOT that document and
    // never claims to be; the tests that assert the publisher's own digest load the retained
    // 19,986 bytes from LuxembourgDocumentFetchFixtures instead.
    private const string StoreXmlUri =
        "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
        + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml";

    private const string ActEliPagePath = "/eli/etat/leg/loi/2017/03/14/a439/jo";

    private const string ObjectPublisherUri =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo";

    private static readonly SourceArtifactRef CompleteEnumerationRef = new(
        "urn:uuid:1f0a5c62-9d34-4a71-b0e2-5c3a7d8e4b19",
        Convert.ToHexStringLower(SHA256.HashData("lu-document-get-enumeration"u8.ToArray())));

    private static LuxembourgDocumentFetchAddress Address(
        LuxembourgUserFormatToken token = LuxembourgUserFormatToken.XmlAkomaNtoso,
        string storeUri = StoreXmlUri,
        string actPagePath = ActEliPagePath) =>
        LuxembourgDocumentFetchAddress.Create(
            LuxembourgFileUri.RequireValid(storeUri),
            token,
            LuxembourgLegalValue.Officiel,
            actPagePath);

    // ---------------------------------------------------------------------------------------
    // The proof obligation for deleting the Luxembourg-keyed NotSupportedException.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// READY verdict lex-event-20260904T175623600Z-7d8ea851a9a54278b97e1eb33a0af29e, fold-in two:
    /// the Luxembourg-keyed <c>NotSupportedException</c> in
    /// <c>RoutedHttpAcquisitionSession.CreateMachineRequest</c> was undriven, and deleting it left
    /// the suite green, so its deletion carries a proof obligation of one test sending a real LU GET
    /// end to end through the session. This is that test.
    /// <para>
    /// It sends for real: robots is fetched and parsed from the live 1,199-byte robots.txt, the
    /// route resolves the Luxembourg document-fetch profile, the session builds the outbound GET
    /// from the plan's own declared parameters, and the publisher's response comes back as retained
    /// <see cref="RoutedHttpEvidence"/> with the body in custody. The handler asserts the wire
    /// request itself: GET, the exact www-host path, a User-Agent, and NO Accept or Accept-Language,
    /// because this route negotiates nothing.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task ARealLuxembourgDocumentGetIsSentAndRetainedEndToEndThroughTheSession()
    {
        var body = "<akomaNtoso/>"u8.ToArray();
        var handler = new RobotsThenDocumentHandler(response: (request, _) =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual(
                "https://legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
                + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml",
                request.RequestUri?.AbsoluteUri);
            Assert.IsTrue(request.Headers.Contains("user-agent"));
            Assert.IsFalse(
                request.Headers.Contains("accept"),
                "the Luxembourg route negotiates nothing and must send no Accept.");
            Assert.IsFalse(
                request.Headers.Contains("accept-language"),
                "the Luxembourg route negotiates nothing and must send no Accept-Language.");
            return BinaryResponse(request, HttpStatusCode.OK, body);
        });
        var store = new FlooringCustodyStore();
        var executor = new LuxembourgRepeatedEnumerationExecutor(
            store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);

        var attempt = await SendAsync(executor, Address(), CancellationToken.None);

        Assert.IsNull(attempt.Refusal, attempt.Detail);
        Assert.IsNotNull(attempt.Evidence);
        Assert.AreEqual(200, attempt.Evidence!.Hops[^1].Status);
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(body)),
            attempt.Evidence.Hops[^1].Sha256,
            "the retained hop must name the exact bytes the publisher returned.");
        Assert.IsFalse(attempt.RetryAllowanceSpent);
        Assert.AreEqual(2, handler.SendCount, "one robots request and exactly one product GET.");

        // DEFECT TWO (REVIEW_RESULT lex-event-20260904T200339509Z-8a3db602c17c41389408981d2fb26535):
        // the held format must be recoverable downstream, because the manifest row keeps host and
        // path only and xml and xml-akomantoso share the path segment. It IS recoverable, and this
        // asserts it rather than assuming it. The address's canonical identity carries user_format
        // and legal_value, and the session retains it as a binder artifact before the send, so it
        // is in custody at its own digest. The finding was a COVERAGE gap rather than a behaviour
        // one: nothing had ever checked, so nothing would have noticed it stopping.
        var address = Address();
        var recovered = await store.ReadByDigestAsync(
            address.ArtifactRef.Sha256, CancellationToken.None);
        CollectionAssert.AreEqual(
            address.CopyCanonicalIdentityBytes(),
            recovered.ToArray(),
            "byte for byte, not merely the same length.");
        var recoveredText = Encoding.UTF8.GetString(recovered.Span);
        StringAssert.Contains(
            recoveredText,
            "user_format=xml-akomantoso",
            "the EXACT token is recoverable, not a normalised category, which is the whole point "
            + "given xml and xml-akomantoso share their path segment.");
        StringAssert.Contains(recoveredText, "legal_value=officiel");
    }

    // ---------------------------------------------------------------------------------------
    // Per-object refusal mapping onto the five widened corpus members.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A real 200 becomes a real <c>Held</c> record, with the body under the Decision 71 floor and
    /// the record set written by <see cref="CorpusRecordSetWriter.WriteAsync"/> as the last step.
    /// </summary>
    [TestMethod]
    public async Task AHeldDocumentBecomesAHeldCorpusRecordWrittenByTheRecordSetWriter()
    {
        var body = "<akomaNtoso/>"u8.ToArray();
        var (outcomes, refusal, manifest, manifestRef, store) = await AcquireAsync(
            (request, _) => BinaryResponse(request, HttpStatusCode.OK, body));

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(1, outcomes!);
        Assert.IsNotNull(outcomes[0].Receipt);
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(body)),
            outcomes[0].Receipt!.Reference.ContentSha256);

        var written = await new CorpusRecordSetWriter(store).WriteAsync(
            manifest, manifestRef, RunIdentityRef(), outcomes, CancellationToken.None);

        Assert.IsNull(written.Refusal, written.Refusal?.Detail);
        Assert.IsNotNull(written.VerifiedSet);
        var records = written.VerifiedSet!.Set.Records;
        Assert.HasCount(1, records);
        Assert.AreEqual(CorpusBodyRecordKind.Held, records[0].Body.Kind);
    }

    /// <summary>
    /// The four classified publisher statuses this route can genuinely complete at, each becoming
    /// this one object's own typed cause rather than a whole-run refusal. 404 is the observed one
    /// (a nonexistent filestore path really did answer 404 with a JSON body); 410 and 451 are this
    /// route's own closed readings of statuses the publisher has not been seen to send.
    /// </summary>
    [TestMethod]
    [DataRow(404, CorpusAcquisitionRefusalReason.NotFound)]
    [DataRow(410, CorpusAcquisitionRefusalReason.Gone)]
    [DataRow(451, CorpusAcquisitionRefusalReason.UnexpectedPublisherStatus)]
    public async Task AClassifiedPublisherStatusBecomesThisObjectsOwnRefusal(
        int status, CorpusAcquisitionRefusalReason expected)
    {
        var (outcomes, refusal, _, _, _) = await AcquireAsync(
            (request, _) => BinaryResponse(request, (HttpStatusCode)status, []));

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(1, outcomes!);
        Assert.IsNull(outcomes[0].Receipt);
        Assert.AreEqual(expected, outcomes[0].Refusal);
    }

    /// <summary>
    /// <see cref="CorpusAcquisitionRefusalReason.RetryExhausted"/> is genuinely reachable here, and
    /// only after the allowance is really spent. The session's own <c>PlanItem.IsRetryable</c>
    /// admits a terminal 503, so the driver re-attempts it; this handler answers 503 every time, so
    /// the run spends all four attempts the profile allows and only then classifies. The send count
    /// is asserted, so a driver that stopped after one attempt and still called it "retry
    /// exhausted" would fail here rather than pass.
    /// </summary>
    [TestMethod]
    public async Task ARepeatedRetryableStatusExhaustsTheAllowanceAndOnlyThenBecomesRetryExhausted()
    {
        var (outcomes, refusal, _, _, sends) = await AcquireCountingSendsAsync(
            (request, _) => BinaryResponse(request, HttpStatusCode.ServiceUnavailable, []));

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(1, outcomes!);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.RetryExhausted, outcomes[0].Refusal);
        Assert.AreEqual(
            5,
            sends(),
            "one robots request plus the four product attempts this profile allows.");
    }

    /// <summary>
    /// The publisher's own robots.txt refusing THIS document is this one object's cause, never a
    /// whole-run refusal: an act the publisher withholds must not stop every other act in the same
    /// run from getting a record. The act driven here is loi 2007/01/15/n2, individually disallowed
    /// by the real robots.txt.
    /// </summary>
    [TestMethod]
    public async Task ARobotsDisallowedDocumentBecomesThisObjectsOwnRefusalRatherThanRefusingTheRun()
    {
        var (outcomes, refusal, _, _, _) = await AcquireAsync(
            (request, _) => BinaryResponse(request, HttpStatusCode.OK, "<akomaNtoso/>"u8.ToArray()),
            Address(
                storeUri:
                    "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2007/01/15/n2/jo/fr/"
                    + "xml/eli-etat-leg-loi-2007-01-15-n2-jo-fr-xml.xml",
                actPagePath: "/eli/etat/leg/loi/2007/01/15/n2/jo"));

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(1, outcomes!);
        Assert.IsNull(outcomes[0].Receipt);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.RobotsDisallowed, outcomes[0].Refusal);
    }

    /// <summary>
    /// The gate, and the reason the accepted fraction matters: a Minted row whose body axis is not
    /// AcceptedSelected gets NO fetch attempt at all, not a fetch whose outcome is filtered out
    /// afterwards. The handler here would answer 200 for any product request, so a run that
    /// attempted one would produce a Held outcome; the assertion is that it produces none and the
    /// only send is the robots request.
    /// </summary>
    [TestMethod]
    public async Task ARowWhoseBodyAxisIsNotAcceptedIsNeverFetchedAtAll()
    {
        var handler = new RobotsThenDocumentHandler(response: (request, _) =>
            BinaryResponse(request, HttpStatusCode.OK, "<akomaNtoso/>"u8.ToArray()));
        var store = new FlooringCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store,
            new LuxembourgRepeatedEnumerationExecutor(
                store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler),
            BuildProfile());
        var manifest = BuildManifest(Address(), ScopeDisposition.TypedQuarantine);

        var (outcomes, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest.Manifest,
            new Dictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> { [ObjectRef()] = Address() },
            LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(3101),
            CancellationToken.None);

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.IsEmpty(outcomes!);
        Assert.AreEqual(0, handler.SendCount, "no session is started at all for an excluded row.");
    }

    /// <summary>
    /// ONE WRITTEN SET CARRYING BOTH OUTCOMES: a Held record and a PendingAcquisition record, from
    /// one run over two objects, reopened before anything is asserted. Named in REVIEW_RESULT
    /// lex-event-20260904T200339509Z-8a3db602c17c41389408981d2fb26535 as the row proof owed once
    /// rows could accept.
    /// </summary>
    /// <remarks>
    /// ATTRIBUTION, NOT COEXISTENCE. An earlier revision selected the two records BY KIND, so it
    /// proved only that one set can carry one of each; it could not have failed if the two answers
    /// had been attached to the wrong objects. Each record is now selected by its own object's
    /// PublisherUri and its kind asserted, which is the property actually claimed: one object's
    /// publisher answer does not decide another's.
    /// <para>
    /// WHICH OBJECT CARRIES THE 404 is what the two rows vary, and it is all they vary. The
    /// RECORD ORDER IS THE SAME IN BOTH: the reducer sorts observed objects by the OBJECT REF'S
    /// DIGEST (ScopeReducer, ComputeObjectRefSha256 through ScopeObservedObjectComparer), not by
    /// act number or publisher URI, so for these two acts a440 is records[0] in BOTH cases,
    /// measured rather than assumed. What varying the 404 does buy is the order the RUN meets
    /// the two answers in, since the adapter walks rows by ascending ordinal: with the 404 fixed
    /// on a440 the refusal always arrived BEFORE any successful hold, and a 404 arriving after a
    /// hold was never exercised. Both now run. The first record's PublisherUri is asserted too,
    /// but as a guard on THESE TWO CASES rather than on production: record order is fixed by
    /// construction in two places, ScopeReducer's VerifyObservedObjectTable and the
    /// CorpusRecordSet constructor's strict ordinal ordering, and both mutations tried against
    /// it (reversing the observed-object comparer, and reversing record emission) were refused
    /// there before this assertion ran. It exists so a later edit collapsing the two cases into
    /// one could not pass as if it had not.
    /// </para>
    /// Both bodies are the publisher's own retained bytes: the real Akoma Ntoso document for the
    /// one that is held, and the office's real 404 JSON for the one that is not.
    /// </remarks>
    [TestMethod]
    [DataRow(false, DisplayName = "the held object sorts first")]
    [DataRow(true, DisplayName = "the 404 object sorts first")]
    public async Task OneSetCarriesAHeldRecordAndAPendingAcquisitionRecordFromOneRun(
        bool notFoundSortsFirst)
    {
        const string A440PublisherUri =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a440/jo";
        var a440 = Address(
            storeUri:
                "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a440/jo/fr/"
                + "xml/eli-etat-leg-loi-2017-03-14-a440-jo-fr-xml.xml",
            actPagePath: "/eli/etat/leg/loi/2017/03/14/a440/jo");
        // a440 is the object that sorts first, so putting the 404 on it is what drives the
        // 404-first order and putting the 404 on a439 drives the 404-after-a-hold order.
        var missing = notFoundSortsFirst ? a440 : Address();
        var held = notFoundSortsFirst ? Address() : a440;
        var missingPublisherUri = notFoundSortsFirst ? A440PublisherUri : ObjectPublisherUri;
        var heldPublisherUri = notFoundSortsFirst ? ObjectPublisherUri : A440PublisherUri;

        var xml = LuxembourgDocumentFetchFixtures.XmlBody();
        var notFound = LuxembourgDocumentFetchFixtures.NotFoundBody();

        var store = new FlooringCustodyStore();
        // Keyed off the missing address itself rather than a literal act number, so inverting the
        // order cannot leave the handler answering 404 for the other object.
        var missingFetchPath = missing.FetchUri.AbsolutePath;
        var handler = new RobotsThenDocumentHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return string.Equals(path, missingFetchPath, StringComparison.Ordinal)
                || path.StartsWith(missing.ActEliPagePath, StringComparison.Ordinal)
                    ? BinaryResponse(request, HttpStatusCode.NotFound, notFound)
                    : BinaryResponse(request, HttpStatusCode.OK, xml);
        });
        var adapter = new LuxembourgQueryExecutionAdapter(
            store,
            new LuxembourgRepeatedEnumerationExecutor(
                store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler),
            BuildProfile());

        var (manifest, manifestRef, addresses) = BuildTwoObjectManifest(
            (heldPublisherUri, held), (missingPublisherUri, missing));
        var (outcomes, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            addresses,
            LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(4242),
            CancellationToken.None);

        Assert.IsNull(refusal, $"one object's 404 must not refuse the run: {refusal?.Detail}");
        Assert.HasCount(2, outcomes!);

        var written = await new CorpusRecordSetWriter(store).WriteAsync(
            manifest, manifestRef, RunIdentityRef(), outcomes, CancellationToken.None);
        Assert.IsNull(written.Refusal, written.Refusal?.Detail);

        var records = written.VerifiedSet!.Set.Records;
        Assert.HasCount(2, records);
        Assert.AreEqual(
            notFoundSortsFirst ? missingPublisherUri : heldPublisherUri,
            records[0].ObjectRef.PublisherUri,
            "a440 is records[0] in BOTH cases; the ternary only names which role that first "
            + "record plays here, so a case that stopped varying which object carries the 404 "
            + "could not pass unnoticed.");

        var heldRecord = records.Single(
            r => string.Equals(r.ObjectRef.PublisherUri, heldPublisherUri, StringComparison.Ordinal));
        var pendingRecord = records.Single(
            r => string.Equals(r.ObjectRef.PublisherUri, missingPublisherUri, StringComparison.Ordinal));
        Assert.AreEqual(
            CorpusBodyRecordKind.Held,
            heldRecord.Body.Kind,
            "the object whose manifestation the office served is the one holding bytes.");
        Assert.AreEqual(
            CorpusBodyRecordKind.PendingAcquisition,
            pendingRecord.Body.Kind,
            "and the object the office answered 404 for is the one left pending, in the same set.");
        Assert.AreEqual(
            LuxembourgDocumentFetchFixtures.XmlBodySha256,
            heldRecord.Body.Receipt!.Reference.ContentSha256,
            "the held record names the publisher's own bytes.");
        Assert.AreEqual(
            CorpusAcquisitionRefusalReason.NotFound,
            pendingRecord.Body.PendingAcquisitionReason!.Refusal,
            "and the other names the publisher's own answer, in the same reopened set.");
    }

    // ---------------------------------------------------------------------------------------
    // The whole-run refusals, driven rather than declared.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A robots bootstrap that never completes is a fact about the RUN, not about this document,
    /// so it refuses the whole run rather than recording one object as robots-refused. This drives
    /// both <see cref="LuxembourgDocumentGetAttemptRefusal.RobotsBootstrapNotCompleted"/> and
    /// <see cref="LuxembourgQueryExecutionRefusal.DocumentFetchSessionNotStarted"/>, which were
    /// declared and referenced but never asserted by any test.
    /// </summary>
    [TestMethod]
    public async Task ARobotsBootstrapThatNeverCompletesRefusesTheWholeRunRatherThanOneObject()
    {
        var handler = new RobotsThenDocumentHandler((request, _) =>
            BinaryResponse(request, HttpStatusCode.OK, "<akomaNtoso/>"u8.ToArray()))
        {
            RobotsStatus = HttpStatusCode.ServiceUnavailable,
        };

        var (outcomes, refusal, _, _, _) = await AcquireWithHandlerAsync(handler, Address());

        Assert.IsNotNull(refusal, "robots being unreachable is not one object's own refusal.");
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.DocumentFetchSessionNotStarted, refusal!.Code);
        StringAssert.Contains(refusal.Detail, "RobotsBootstrapNotCompleted");
        Assert.IsEmpty(
            outcomes,
            "and no object is recorded as robots-disallowed, which would blame the publisher for "
            + "our own inability to read its rules.");
    }

    /// <summary>
    /// <see cref="LuxembourgDocumentGetAttemptRefusal.ObservationNotExecuted"/> was declared and
    /// produced on two paths in <see cref="LuxembourgRepeatedEnumerationExecutor"/> but driven by
    /// no test. This drives the real one: every attempt fails before any header arrives, so no
    /// observation is ever executed, the profile's whole retry budget is spent, and the run
    /// refuses. It must NOT become a per-object PendingAcquisition row. A transport failure on our
    /// side is unknown, and unknown is not one of the four reasons a law may go unheld, so blaming
    /// the object would record a cause the publisher never gave.
    /// </summary>
    [TestMethod]
    public async Task ADocumentGetThatNeverExecutesAnObservationRefusesAfterTheWholeRetryBudget()
    {
        var (outcomes, refusal, _, _, sendCount) = await AcquireCountingSendsAsync(
            (_, _) => throw new HttpRequestException(
                HttpRequestError.ConnectionError, "simulated pre-header failure"));

        Assert.IsNotNull(refusal, "a transport failure is not one object's own refusal.");
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.DocumentFetchSessionNotStarted, refusal!.Code);
        StringAssert.Contains(
            refusal.Detail,
            nameof(LuxembourgDocumentGetAttemptRefusal.ObservationNotExecuted),
            "the run's refusal carries the attempt refusal that produced it.");
        Assert.IsEmpty(
            outcomes,
            "and no object is recorded as pending for a cause the publisher never gave.");
        Assert.AreEqual(1 + 4, sendCount(), "one robots send plus the full MaximumAttempts=4 budget.");
    }

    /// <summary>
    /// The document GET's own catch arm, the SECOND place a CustodyRequiredException becomes
    /// <see cref="LuxembourgDocumentGetAttemptRefusal.ObservationNotExecuted"/>, was driven by
    /// nothing: dropping CustodyRequiredException from that filter left the whole suite green.
    /// A store that refuses every write once the run has reached its product request puts the
    /// failure inside that try, and the run answers with a typed refusal carrying the store's
    /// own message rather than letting the exception escape.
    /// </summary>
    /// <remarks>
    /// The message assertion is what separates this arm from the pre-header path in
    /// <see cref="ADocumentGetThatNeverExecutesAnObservationRefusesAfterTheWholeRetryBudget"/>,
    /// which produces the SAME attempt refusal from an OperationalReason and failure-class pair.
    /// Asserting only the refusal name would have let either path satisfy both tests.
    /// </remarks>
    [TestMethod]
    public async Task ACustodyRequiredFailureDuringTheDocumentGetIsATypedRefusalRatherThanAnEscape()
    {
        const string StoreMessage = "the store refused a write during the document get.";
        // TWO INSTRUMENTS, as in the enumeration executor's own custody test. The HANDLER GATE
        // SELECTS THE PHASE: armed turns true on the product request, so nothing can fire inside
        // the robots bootstrap. THE COUNT SELECTS THE WRITE, and it has to, because the SESSION
        // carries its own catch (CustodyRequiredException) that turns any of ITS writes into
        // OperationalReason.CustodyUnavailable and never lets one reach the executor's catch.
        // Passing its two writes through lands the failure on the EXECUTOR's own evidence write,
        // inside the try this test is about.
        //
        // EXACT TO ONE WRITE IN BOTH DIRECTIONS, each measured rather than assumed: 0 and 1 give
        // "code=ObservationNotExecuted detail=CustodyUnavailable/.", the session classifying its
        // own write, which would have passed a test that only checked the refusal's name; 3 walks
        // past the executor entirely onto the adapter's body hold and gives DocumentBodyNotHeld.
        var body = LuxembourgDocumentFetchFixtures.XmlBody();
        var store = new CustodyRequiredAfterProductRequestStore(new FlooringCustodyStore(), StoreMessage)
        {
            PassThroughArmedWrites = 2,
        };
        var handler = new RobotsThenDocumentHandler((request, _) =>
        {
            store.Armed = true;
            return BinaryResponse(request, HttpStatusCode.OK, body);
        });

        var (outcomes, refusal, _, _, _) = await AcquireWithHandlerAsync(handler, Address(), store);

        Assert.IsNotNull(refusal, "a custody failure is a typed refusal, never an escape.");
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.DocumentFetchSessionNotStarted, refusal!.Code);
        StringAssert.Contains(
            refusal.Detail,
            nameof(LuxembourgDocumentGetAttemptRefusal.ObservationNotExecuted),
            "the attempt refusal this catch arm produces.");
        StringAssert.Contains(
            refusal.Detail,
            StoreMessage,
            "and the store's own message, which is what proves the catch arm ran rather than "
            + "the pre-header path that produces the same refusal name.");
        Assert.IsEmpty(outcomes, "no object carries a cause the publisher never gave.");
        Assert.AreEqual(
            3,
            store.ArmedWrites,
            "the calibration self-checks: two session writes, then the executor's evidence write. "
            + "If that shape drifts, this fails rather than arming silently in the wrong phase.");
    }

    /// <summary>
    /// THE SECOND MEMBER OF THE CLASS the manifest reopen belonged to: another
    /// CustodyRestore.ReadByDigestCheckedAsync with no catch, so a store that could not
    /// reproduce the body at its own digest threw straight out of RunAsync. Grepping the
    /// checked reader against catch over the adapter found it in ONE PASS, which is the sweep
    /// that should have run when the first one was typed rather than a cycle later.
    /// </summary>
    [TestMethod]
    public async Task ABodyThatDoesNotReopenAtItsOwnDigestRefusesRatherThanEscaping()
    {
        var body = LuxembourgDocumentFetchFixtures.XmlBody();
        var store = new BodyReopenFailingCustodyStore(new FlooringCustodyStore(), body);
        var handler = new RobotsThenDocumentHandler((request, _) =>
            BinaryResponse(request, HttpStatusCode.OK, body));

        IReadOnlyDictionary<int, CorpusAcquisitionOutcome> outcomes;
        LuxembourgQueryExecutionRefusalDetail? refusal;
        try
        {
            (outcomes, refusal, _, _, _) = await AcquireWithHandlerAsync(handler, Address(), store);
        }
        catch (CustodyIntegrityException exception)
        {
            Assert.Fail(
                "a custody integrity failure on the body reopen escaped RunAsync untyped: "
                + exception.Message);
            throw;
        }

        Assert.IsNotNull(refusal, "a body that will not reopen is a typed refusal, never an escape.");
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.DocumentBodyNotHeld, refusal!.Code);
        StringAssert.Contains(
            refusal.Detail,
            "could not be reopened at its own digest",
            "the refusal names what actually failed, not a generic body failure.");
        Assert.IsEmpty(outcomes, "and no object is recorded as held from bytes we cannot reopen.");
        Assert.AreEqual(
            1,
            store.BodyReads,
            "the calibration self-checks: the adapter's reopen is the FIRST read of that digest "
            + "through this interface, and the run stops there.");
    }

    /// <summary>
    /// A route outcome this route has no reviewed reading for refuses the whole run and names the
    /// real classified cause, rather than being mapped onto an unrelated corpus member. The
    /// Luxembourg profile admits no redirect at all, so a 303 leaves the route incomplete for a
    /// reason the hop-level registry mirror cannot name. Drives
    /// <see cref="LuxembourgQueryExecutionRefusal.AcquisitionOutcomeNotRepresentable"/>.
    /// </summary>
    [TestMethod]
    public async Task AnUnreadableRouteOutcomeRefusesTheWholeRunAndNamesTheCause()
    {
        var (outcomes, refusal, _, _, _) = await AcquireAsync((request, _) =>
        {
            var response = BinaryResponse(request, HttpStatusCode.SeeOther, []);
            response.Headers.TryAddWithoutValidation(
                "Location", "https://legilux.public.lu/filestore/elsewhere.xml");
            return response;
        });

        Assert.IsNotNull(refusal);
        Assert.AreEqual(
            LuxembourgQueryExecutionRefusal.AcquisitionOutcomeNotRepresentable, refusal!.Code);
        StringAssert.Contains(refusal.Detail, "routeOutcome=");
        Assert.IsEmpty(outcomes);
    }

    // ---------------------------------------------------------------------------------------
    // The three retained publisher responses, driven rather than described.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The retained fixtures are the publisher's own bytes and still are. The loader re-hashes on
    /// every load, so this asserts the mechanism as well as the three digests.
    /// </summary>
    [TestMethod]
    public void TheRetainedFixturesCarryThePublisherBytesTheyName()
    {
        Assert.HasCount(
            LuxembourgDocumentFetchFixtures.XmlBodyLength, LuxembourgDocumentFetchFixtures.XmlBody());
        Assert.HasCount(
            LuxembourgDocumentFetchFixtures.PdfBodyLength, LuxembourgDocumentFetchFixtures.PdfBody());
        Assert.HasCount(
            LuxembourgDocumentFetchFixtures.NotFoundBodyLength,
            LuxembourgDocumentFetchFixtures.NotFoundBody());

        StringAssert.StartsWith(
            Encoding.UTF8.GetString(LuxembourgDocumentFetchFixtures.XmlBody().AsSpan(0, 120)),
            "<?xml",
            "the 200 fixture is the real Akoma Ntoso document, not a placeholder.");
        StringAssert.Contains(
            Encoding.UTF8.GetString(LuxembourgDocumentFetchFixtures.NotFoundBody()),
            "\"status\":404",
            "and the 404 fixture is the office's own JSON error body.");
    }

    /// <summary>
    /// A REAL publisher body is fetched and held, and the receipt names the publisher's own digest.
    /// The retained 19,986-byte Akoma Ntoso document of loi 2017/03/14/a439 is served as the
    /// response, so the digest asserted here is the publisher's, not the test's.
    /// </summary>
    [TestMethod]
    public async Task TheRealAkomaNtosoBodyIsHeldUnderTheReceiptCarryingThePublishersOwnDigest()
    {
        var body = LuxembourgDocumentFetchFixtures.XmlBody();

        var (outcomes, refusal, _, _, _) = await AcquireAsync(
            (request, _) => BinaryResponse(request, HttpStatusCode.OK, body));

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.HasCount(1, outcomes);
        var receipt = outcomes.Values.Single().Receipt;
        Assert.IsNotNull(receipt);
        Assert.AreEqual(
            LuxembourgDocumentFetchFixtures.XmlBodySha256,
            receipt!.Reference.ContentSha256,
            "the held body is the publisher's bytes, by the publisher's own digest.");
        Assert.AreEqual(LuxembourgDocumentFetchFixtures.XmlBodyLength, receipt.Reference.ByteLength);
    }

    /// <summary>
    /// The PDF-only arm, on the real act that has no XML at all: rgd 1977/11/16/n3, whose single
    /// pdf manifestation is what the ladder falls through to. Its real 124,932-byte file is served.
    /// </summary>
    [TestMethod]
    public async Task TheRealPdfOnlyActsBodyIsHeldWhenTheLadderFallsThroughToPdf()
    {
        var body = LuxembourgDocumentFetchFixtures.PdfBody();
        var address = Address(
            token: LuxembourgUserFormatToken.Pdf,
            storeUri:
                "http://data.legilux.public.lu/filestore/eli/etat/leg/memorial/1977/a67/fr/pdf/"
                + "eli-etat-leg-memorial-1977-a67-fr-pdf.pdf",
            actPagePath: "/eli/etat/leg/rgd/1977/11/16/n3/jo");

        var (outcomes, refusal, _, _, _) = await AcquireAsync(
            (request, _) => BinaryResponse(request, HttpStatusCode.OK, body), address);

        Assert.IsNull(refusal, refusal?.Detail);
        var receipt = outcomes.Values.Single().Receipt;
        Assert.IsNotNull(receipt);
        Assert.AreEqual(LuxembourgDocumentFetchFixtures.PdfBodySha256, receipt!.Reference.ContentSha256);
    }

    /// <summary>
    /// The office's REAL 404 body drives the not-found arm, and the object gets a
    /// PendingAcquisition record with its typed cause, written by
    /// <see cref="CorpusRecordSetWriter"/> and REOPENED before anything is asserted.
    /// </summary>
    /// <remarks>
    /// Nothing here asserts that a fresh fetch reproduces the fixture's digest, and nothing may:
    /// the body carries a live timestamp and echoes the requested path, so it differs every time.
    /// Three observations of this one shape exist, 209 then 234 then 204 bytes. The fixture is
    /// evidence of one observation.
    /// </remarks>
    [TestMethod]
    public async Task TheRealNotFoundBodyBecomesAPendingAcquisitionRecordWithItsTypedCause()
    {
        var body = LuxembourgDocumentFetchFixtures.NotFoundBody();

        var (outcomes, refusal, manifest, manifestRef, store) = await AcquireAsync(
            (request, _) => BinaryResponse(request, HttpStatusCode.NotFound, body));

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.HasCount(1, outcomes);
        Assert.IsNull(outcomes.Values.Single().Receipt);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.NotFound, outcomes.Values.Single().Refusal);

        var written = await new CorpusRecordSetWriter(store).WriteAsync(
            manifest, manifestRef, RunIdentityRef(), outcomes, CancellationToken.None);
        Assert.IsNull(written.Refusal, written.Refusal?.Detail);

        var record = written.VerifiedSet!.Set.Records.Single();
        Assert.AreEqual(CorpusBodyRecordKind.PendingAcquisition, record.Body.Kind);
        Assert.AreEqual(
            CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused,
            record.Body.PendingAcquisitionReason?.Kind,
            "the record says the acquisition was refused, not that the body was never attempted.");
        Assert.AreEqual(
            CorpusAcquisitionRefusalReason.NotFound,
            record.Body.PendingAcquisitionReason?.Refusal,
            "and it names the publisher's own answer as the cause.");
    }

    // ---------------------------------------------------------------------------------------
    // The accepted fraction of a real LU manifest.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE ACCEPTED FRACTION, AS A NUMBER, over a real-shaped manifest reduced by the production
    /// resolver: zero before the join can reach a wording manifestation, non-zero after.
    /// </summary>
    /// <remarks>
    /// THIS TEST PINNED NOTHING. It was named
    /// <c>TheBodyAxisOfEveryRealLuxembourgManifestAcceptsNothing</c> and its whole body was
    /// reflection asserting that two method names still existed and that an enum had two members.
    /// It would have passed unchanged whatever those methods did, including after the accepting arm
    /// landed, which is exactly the defect this lane keeps finding: a test that reads like a proof
    /// and proves nothing. Named in REVIEW_RESULT
    /// lex-event-20260904T200339509Z-8a3db602c17c41389408981d2fb26535 defect four.
    /// <para>
    /// Both objects below go through the real path: real assertions, the real rights channel built
    /// from jolux:license, the real profile resolution behind its proof door, and the real
    /// ScopeReducer. The only difference between them is what the publisher LISTS, which is the
    /// thing the body axis is supposed to decide on.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheAcceptedFractionIsZeroWithoutAWordingManifestationAndOneWithIt()
    {
        var profile = BuildProfile();
        var observationRef = profile.Snapshot.ObservationRef;

        var withWording = LuxembourgQueryExecutionAdapter.BuildResourceObservation(
            WordingAct,
            ActAssertions(WordingAct, "xml-akomantoso", "xml", observationRef),
            observationRef,
            profile.ScopeBinding.SourceProfileRef);
        var withoutWording = LuxembourgQueryExecutionAdapter.BuildResourceObservation(
            NoWordingAct,
            ActAssertions(NoWordingAct, "docx", "docx", observationRef),
            observationRef,
            profile.ScopeBinding.SourceProfileRef);

        var manifest = ReduceRealManifest(profile, [withWording, withoutWording]);

        var acceptedOrdinals = manifest.Accounting
            .Where(static set => set.Axis == ScopeAxis.Body
                && set.Disposition == ScopeDisposition.AcceptedSelected)
            .SelectMany(static set => set.ObjectOrdinals)
            .ToHashSet();

        Assert.AreEqual(
            2, manifest.Rows.Count, "two objects, so the fraction below is out of two.");
        Assert.HasCount(
            1,
            acceptedOrdinals,
            "ONE of two: the act whose publisher lists a wording manifestation is selected for "
            + "acquisition, and the act listing only docx is not. Before the accepting arm this "
            + "number was zero for every possible manifest.");

        var acceptedUri = manifest.ObservedObjects[acceptedOrdinals.Single()].ObjectRef.PublisherUri;
        Assert.AreEqual(
            WordingAct,
            acceptedUri,
            "and it is the RIGHT one of the two, not merely the right count.");
    }

    private const string WordingAct =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2021/09/09/a676/jo";
    private const string NoWordingAct =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2021/09/10/a677/jo";

    /// <summary>
    /// One act's own publisher assertions: work realized by a French expression which embodies one
    /// manifestation of the given userFormat, exemplified by a filestore file, declaring CC-BY.
    /// Shaped exactly as the Code civil's own retained SPARQL answer shows the store shapes them.
    /// </summary>
    private static LuxembourgObservedAssertion[] ActAssertions(
        string act, string token, string pathSegment, SourceArtifactRef observationRef)
    {
        const string jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
        var expression = act + "/fr";
        var manifestation = expression + "/" + pathSegment;
        var file = act.Replace(
                "http://data.legilux.public.lu/eli/",
                "http://data.legilux.public.lu/filestore/eli/")
            + "/fr/" + pathSegment + "/f." + pathSegment;
        LuxembourgObservedAssertion Iri(string subject, string predicate, string value) =>
            new(subject, predicate, LuxembourgAssertionObjectKind.Iri, value, string.Empty,
                string.Empty, observationRef);
        return
        [
            Iri(act, "http://www.w3.org/1999/02/22-rdf-syntax-ns#type", jolux + "Act"),
            // The publication family is a SEPARATE, EARLIER gate, and this test is not about it.
            // TC is a priority candidate type, which with an Act class is an accepted family
            // outright; an ordinary type such as LOI needs consolidation or as-published
            // qualification the publisher expresses elsewhere, and using one here would have made
            // this test report zero for a reason that has nothing to do with the body axis. That is
            // not hypothetical: it did, and the family read typed_quarantine_role_not_admitted while
            // the body join itself was already producing a candidate with NO blockers.
            Iri(act, jolux + "typeDocument",
                "http://data.legilux.public.lu/resource/authority/resource-type/"
                + VerifiedLuxembourgSourceProfile.PriorityCandidateTypeTc),
            Iri(act, jolux + "isRealizedBy", expression),
            Iri(expression, "http://www.w3.org/1999/02/22-rdf-syntax-ns#type", jolux + "Expression"),
            Iri(expression, jolux + "language",
                "http://publications.europa.eu/resource/authority/language/FRA"),
            Iri(expression, jolux + "isEmbodiedBy", manifestation),
            Iri(manifestation, "http://www.w3.org/1999/02/22-rdf-syntax-ns#type", jolux + "Manifestation"),
            Iri(manifestation, jolux + "userFormat",
                "http://data.legilux.public.lu/resource/authority/user-format/" + token),
            Iri(manifestation, jolux + "isExemplifiedBy", file),
            Iri(manifestation, jolux + "license", "http://creativecommons.org/licenses/by/4.0/"),
        ];
    }

    private static ScopeManifest ReduceRealManifest(
        VerifiedLuxembourgSourceProfile profile,
        IReadOnlyList<LuxembourgResourceObservation> observations)
    {
        var resolution = profile.Resolve(
            LuxembourgProvenResourceObservations.RequireProven(
                AbsenceFixtures.Proof(), observations));
        var resolved = resolution as LuxembourgProfileResolution.Resolved;
        Assert.IsNotNull(
            resolved,
            $"the observations must resolve: {(resolution as LuxembourgProfileResolution.Failed)?.Failure.Code}");
        return profile
            .ReduceScope(resolved!, new PermissiveScopeEvidenceResolver(profile.Snapshot.CompleteEnumerationRef))
            .Manifest;
    }

    // ---------------------------------------------------------------------------------------
    // The dangerous direction of the Decision 71 interpretation.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A DIGEST MISMATCH IS A CUSTODY FAILURE AND STAYS ONE. RULING
    /// lex-event-20260904T212914634Z-f166f0b9e11b445795efd40c268bfbb8 with addendum
    /// lex-event-20260904T213158723Z-699ebe73901142a993731759e1e8e6b7: relaxing the floor lets a
    /// body be held under a weaker guarantee, and the danger it creates is that "we stored it under
    /// a weaker guarantee" and "we failed to store it" stop being different facts. Here the store
    /// accepts the write and then cannot reproduce those exact bytes at their own digest, which is
    /// a real failure. It must refuse, not record a held body with RetainedUnenforced.
    /// </summary>
    [TestMethod]
    public async Task ADigestMismatchAfterTheWriteRefusesRatherThanHoldingUnderAWeakerClass()
    {
        var body = "<akomaNtoso/>"u8.ToArray();
        var store = new HoldFailingCustodyStore(new FlooringCustodyStore(), body, failWrite: false);

        var (outcomes, refusal, _, _, _) = await AcquireWithHandlerAsync(
            new RobotsThenDocumentHandler((request, _) =>
                BinaryResponse(request, HttpStatusCode.OK, body)),
            Address(),
            store);

        Assert.IsNotNull(refusal, "a body whose stored bytes do not reopen at their own digest is not held.");
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.DocumentBodyNotHeld, refusal!.Code);
        StringAssert.Contains(refusal.Detail, "digest");
        Assert.IsEmpty(outcomes, "and no held outcome is recorded for it under any custody class.");
    }

    /// <summary>
    /// A WRITE ERROR IS A CUSTODY FAILURE AND STAYS ONE. The same addendum, the other half: the
    /// store never accepted the bytes at all. A held record naming a receipt that does not exist
    /// would be a false hold, which is worse than the wall the floor relaxation removed.
    /// </summary>
    [TestMethod]
    public async Task AWriteErrorRefusesRatherThanHoldingUnderAWeakerClass()
    {
        var body = "<akomaNtoso/>"u8.ToArray();
        var store = new HoldFailingCustodyStore(new FlooringCustodyStore(), body, failWrite: true);

        var (outcomes, refusal, _, _, _) = await AcquireWithHandlerAsync(
            new RobotsThenDocumentHandler((request, _) =>
                BinaryResponse(request, HttpStatusCode.OK, body)),
            Address(),
            store);

        Assert.IsNotNull(refusal, "a body the store refused to write is not held.");
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.DocumentBodyNotHeld, refusal!.Code);
        StringAssert.Contains(refusal.Detail, "custody write failed");
        Assert.IsEmpty(outcomes);
    }

    /// <summary>
    /// The positive half of the same ruling, so the pair reads as one statement: a store that
    /// honestly declares NotEnforced holds the body, and the record says so. This is the wall the
    /// Code civil canary hit, now passable without weakening what a failure means.
    /// </summary>
    [TestMethod]
    public async Task AnUnenforcedStoreHoldsTheBodyAndTheRecordCarriesRetainedUnenforced()
    {
        var body = "<akomaNtoso/>"u8.ToArray();
        var store = new UnenforcedCustodyStore();

        var (outcomes, refusal, manifest, manifestRef, _) = await AcquireWithHandlerAsync(
            new RobotsThenDocumentHandler((request, _) =>
                BinaryResponse(request, HttpStatusCode.OK, body)),
            Address(),
            store);

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.HasCount(1, outcomes);
        var receipt = outcomes.Values.Single().Receipt;
        Assert.IsNotNull(receipt, "an unenforced store still HOLDS the bytes.");
        Assert.AreEqual(
            CustodyMembership.RetainedUnenforced,
            CustodyMembershipClassifier.Classify(receipt!),
            "and the class is what the store honestly declared, through the one classifier.");

        // THE RECORD-SET HALF, FLIPPED AT THE REBASE EXACTLY AS PLANNED. This assertion was kept
        // deliberately in its old form, asserting that the SET still refused an unenforced store,
        // because CorpusRecordSetWriter's floor gate was shared and the gate ownership ruling
        // (lex-event-20260904T214500631Z-2988b4fbae224252b08849326325a2a6) put it in the lane that
        // merges first. That lane has merged: the writer now routes through CustodyHold and
        // records the observed class on RetainedFloor instead of refusing, so the whole path this
        // test names does work, and the pair finally reads as one statement end to end.
        var written = await new CorpusRecordSetWriter(store).WriteAsync(
            manifest, manifestRef, RunIdentityRef(), outcomes, CancellationToken.None);
        Assert.IsNull(
            written.Refusal,
            "an unenforced store no longer costs the run its corpus records: " + written.Refusal?.Detail);
        Assert.AreEqual(
            CustodyMembership.RetainedUnenforced,
            written.RetainedFloor,
            "and the SET says under which guarantee it is retained, rather than being discarded.");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------------------------------

    private static async Task<LuxembourgDocumentGetAttemptResult> SendAsync(
        LuxembourgRepeatedEnumerationExecutor executor,
        LuxembourgDocumentFetchAddress address,
        CancellationToken cancellationToken)
    {
        var bound = new LuxembourgDocumentFetchPlan(address).Bind(
            $"urn:uuid:{Guid.NewGuid():D}",
            $"urn:uuid:{Guid.NewGuid():D}",
            LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(3001));
        return await executor.RunDocumentGetAsync(
            bound.Request, [address.ActEliPagePath], cancellationToken);
    }

    private static Task<(
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome> Outcomes,
        LuxembourgQueryExecutionRefusalDetail? Refusal,
        ScopeManifest Manifest,
        SourceArtifactRef ManifestRef,
        ICustodyStore Store)> AcquireAsync(
        Func<HttpRequestMessage, int, HttpResponseMessage> response,
        LuxembourgDocumentFetchAddress? address = null) =>
        AcquireWithHandlerAsync(new RobotsThenDocumentHandler(response), address ?? Address());

    private static async Task<(
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome> Outcomes,
        LuxembourgQueryExecutionRefusalDetail? Refusal,
        ScopeManifest Manifest,
        SourceArtifactRef ManifestRef,
        Func<int> SendCount)> AcquireCountingSendsAsync(
        Func<HttpRequestMessage, int, HttpResponseMessage> response)
    {
        var handler = new RobotsThenDocumentHandler(response);
        var result = await AcquireWithHandlerAsync(handler, Address());
        return (result.Outcomes, result.Refusal, result.Manifest, result.ManifestRef, () => handler.SendCount);
    }

    private static async Task<(
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome> Outcomes,
        LuxembourgQueryExecutionRefusalDetail? Refusal,
        ScopeManifest Manifest,
        SourceArtifactRef ManifestRef,
        ICustodyStore Store)> AcquireWithHandlerAsync(
        RobotsThenDocumentHandler handler,
        LuxembourgDocumentFetchAddress address,
        ICustodyStore? custodyStore = null)
    {
        var store = custodyStore ?? new FlooringCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store,
            new LuxembourgRepeatedEnumerationExecutor(
                store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler),
            BuildProfile());
        var verified = BuildManifest(address, ScopeDisposition.AcceptedSelected);
        var (outcomes, refusal) = await adapter.RunDocumentAcquisitionAsync(
            verified.Manifest,
            new Dictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> { [ObjectRef()] = address },
            LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(3201),
            CancellationToken.None);
        return (
            outcomes ?? new Dictionary<int, CorpusAcquisitionOutcome>(),
            refusal,
            verified.Manifest,
            verified.ManifestRef,
            store);
    }

    private static SourceObjectRef ObjectRef() => ObjectRefFor(ObjectPublisherUri);

    private static SourceObjectRef ObjectRefFor(string publisherUri)
    {
        var canonicalKey = "lu-document-get:" + publisherUri;
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(CompleteEnumerationRef, "lu_document_get_root"),
            publisherUri,
            canonicalKey,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey))),
            CompleteEnumerationRef,
            null);
    }

    private static SourceArtifactRef RunIdentityRef() => new(
        $"urn:uuid:{Guid.NewGuid():D}",
        Convert.ToHexStringLower(SHA256.HashData("lu-document-get-run"u8.ToArray())));

    /// <summary>
    /// Two accepted-body objects in one manifest, for the mixed-outcome row proof. Returns the
    /// address map KEYED BY REF rather than a positional array: the reducer sorts observed objects
    /// canonically, so pairing the caller's addresses with the returned order by index was correct
    /// only by the accident that a439 sorts before a440, and would have mispaired silently the
    /// moment a caller inverted the two.
    /// </summary>
    private static (
        ScopeManifest Manifest,
        SourceArtifactRef ManifestRef,
        Dictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> Addresses)
        BuildTwoObjectManifest(
            (string PublisherUri, LuxembourgDocumentFetchAddress Address) first,
            (string PublisherUri, LuxembourgDocumentFetchAddress Address) second)
    {
        var binding = BuildProfile().ScopeBinding;
        var pairs = new[] { first, second };
        var refs = new SourceObjectRef[pairs.Length];
        var inputs = new ScopeObjectReductionInput[pairs.Length];
        for (var index = 0; index < pairs.Length; index++)
        {
            refs[index] = ObjectRefFor(pairs[index].PublisherUri);
            var selectors = new ScopeSelectorEvidence[binding.OrderedSelectorMemberOrdinals.Count];
            for (var s2 = 0; s2 < selectors.Length; s2++)
            {
                selectors[s2] = NotApplicableSelector(binding, ScopeAxis.Record);
            }

            inputs[index] = new ScopeObjectReductionInput(
                refs[index],
                selectors,
                new[]
                {
                    Evaluation(binding, ScopeAxis.Record, ScopeDisposition.AcceptedSelected),
                    Evaluation(binding, ScopeAxis.Body, ScopeDisposition.AcceptedSelected),
                    Evaluation(binding, ScopeAxis.Relation, ScopeDisposition.Point),
                    Evaluation(binding, ScopeAxis.SupportingDocument, ScopeDisposition.Point),
                },
                pairs[index].Address.ToScopeManifestFetchAddress());
        }

        var verified = ScopeReducer.Reduce(
            binding, [], refs, inputs, new PermissiveScopeEvidenceResolver(CompleteEnumerationRef));
        using var buffer = new MemoryStream();
        var canonical = ScopeManifestCanonicalWriter.Write(buffer, verified);
        var addresses = new Dictionary<SourceObjectRef, LuxembourgDocumentFetchAddress>();
        foreach (var observed in verified.Manifest.ObservedObjects)
        {
            addresses[observed.ObjectRef] = pairs
                .Single(pair => string.Equals(
                    pair.PublisherUri, observed.ObjectRef.PublisherUri, StringComparison.Ordinal))
                .Address;
        }

        return (
            verified.Manifest,
            new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", canonical),
            addresses);
    }

    private static (VerifiedScopeManifest Verified, ScopeManifest Manifest, SourceArtifactRef ManifestRef) BuildManifest(
        LuxembourgDocumentFetchAddress address,
        ScopeDisposition bodyDisposition)
    {
        var profile = BuildProfile();
        var binding = profile.ScopeBinding;
        var objectRef = ObjectRef();
        var evaluations = new[]
        {
            Evaluation(binding, ScopeAxis.Record, ScopeDisposition.AcceptedSelected),
            Evaluation(binding, ScopeAxis.Body, bodyDisposition),
            Evaluation(binding, ScopeAxis.Relation, ScopeDisposition.Point),
            Evaluation(binding, ScopeAxis.SupportingDocument, ScopeDisposition.Point),
        };
        // One selector evidence per ordered selector the production binding declares, all typed
        // not-applicable: this file's subject is the document GET, and a row that carried real
        // selector values would only be re-testing the reduction the LU adapter tests already own.
        var selectors = new ScopeSelectorEvidence[binding.OrderedSelectorMemberOrdinals.Count];
        for (var index = 0; index < selectors.Length; index++)
        {
            selectors[index] = NotApplicableSelector(binding, ScopeAxis.Record);
        }

        var input = new ScopeObjectReductionInput(
            objectRef,
            selectors,
            evaluations,
            address.ToScopeManifestFetchAddress());
        var verified = ScopeReducer.Reduce(
            binding,
            [],
            [objectRef],
            [input],
            new PermissiveScopeEvidenceResolver(CompleteEnumerationRef));
        using var buffer = new MemoryStream();
        var canonicalSha256 = ScopeManifestCanonicalWriter.Write(buffer, verified);
        return (
            verified,
            verified.Manifest,
            new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", canonicalSha256));
    }

    private static ScopeRuleEvaluation Evaluation(
        ScopeProfileBinding binding, ScopeAxis axis, ScopeDisposition disposition) =>
        new(
            RuleOrdinal(binding, axis),
            ScopeRuleEvaluationState.Matched,
            ScopeRuleEffect.Positive,
            disposition,
            axis == ScopeAxis.Body && disposition == ScopeDisposition.AcceptedSelected
                ? [binding.BodyCandidateRoleMemberOrdinal]
                : [],
            []);

    private static ScopeSelectorEvidence NotApplicableSelector(ScopeProfileBinding binding, ScopeAxis axis) =>
        new(ScopeSelectorState.SelectorNotApplicable, [], null, null, RuleOrdinal(binding, axis), null);

    private static int RuleOrdinal(ScopeProfileBinding binding, ScopeAxis axis)
    {
        for (var index = 0; index < binding.OrderedRules.Count; index++)
        {
            if (binding.OrderedRules[index].Axis == axis)
            {
                return index;
            }
        }

        throw new AssertFailedException($"the Luxembourg binding declares no rule for axis {axis}.");
    }

    private static HttpResponseMessage BinaryResponse(
        HttpRequestMessage request, HttpStatusCode status, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation(
            "Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        content.Headers.TryAddWithoutValidation("Content-Type", "application/xml");
        return new HttpResponseMessage(status)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
    }

    /// <summary>
    /// Serves the real 1,199-byte legilux.public.lu robots.txt for the robots request and delegates
    /// every product request to the scripted response. The robots text is the same retained fixture
    /// <see cref="LuxembourgDocumentFetchRobotsBootstrapTests"/> pins by digest.
    /// </summary>
    private sealed class RobotsThenDocumentHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
    {
        private int _sendCount;

        internal int SendCount => Volatile.Read(ref _sendCount);

        /// <summary>Lets a test make the robots bootstrap itself fail rather than deny a path.</summary>
        internal HttpStatusCode RobotsStatus { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = Interlocked.Increment(ref _sendCount);
            if (request.RequestUri?.AbsolutePath == "/robots.txt")
            {
                if (RobotsStatus != HttpStatusCode.OK)
                {
                    return Task.FromResult(new HttpResponseMessage(RobotsStatus)
                    {
                        Version = HttpVersion.Version11,
                        RequestMessage = request,
                        Content = new ByteArrayContent([]),
                    });
                }

                var bytes = Encoding.UTF8.GetBytes(LuxembourgDocumentFetchRobotsBootstrapTests.RealRobotsTxt);
                var content = new ByteArrayContent(bytes);
                content.Headers.TryAddWithoutValidation(
                    "Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Version = HttpVersion.Version11,
                    RequestMessage = request,
                    Content = content,
                });
            }

            return Task.FromResult(response(request, ordinal));
        }
    }

    /// <summary>
    /// Fails ONLY the acquisition's own hold, never the session's retention of the same bytes.
    /// </summary>
    /// <remarks>
    /// The precision matters and cost a first attempt. The routed session retains the response body
    /// in custody itself, so the same payload is written and read once before the adapter ever
    /// holds it. A store that failed on the first sight of those bytes broke the SESSION and the
    /// run refused as DocumentFetchSessionNotStarted, which is a true refusal about a different
    /// thing and would have made these two tests pass while proving nothing about the hold. So the
    /// failure is armed only after the payload has been written once, which is exactly the
    /// adapter's own second write.
    /// </remarks>
    private sealed class HoldFailingCustodyStore(
        ICustodyStore inner, byte[] payload, bool failWrite) : ICustodyStore
    {
        private int _writes;
        private string? _armedDigest;

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            if (!bytes.Span.SequenceEqual(payload))
            {
                return inner.CreateAsync(bytes, custodyClass, cancellationToken);
            }

            _writes++;
            if (_writes < 2)
            {
                return inner.CreateAsync(bytes, custodyClass, cancellationToken);
            }

            if (failWrite)
            {
                throw new CustodyRequiredException("the store refused this write.");
            }

            _armedDigest = CustodyDigest.Of(payload);
            return inner.CreateAsync(bytes, custodyClass, cancellationToken);
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken) =>
            string.Equals(contentSha256, _armedDigest, StringComparison.Ordinal)
                ? Task.FromResult<ReadOnlyMemory<byte>>("not the bytes you stored"u8.ToArray())
                : inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }

    /// <summary>
    /// Refuses every write once the run has reached its product request, so the failure lands
    /// inside the document GET's own try rather than in the robots bootstrap before it, where it
    /// would be a true refusal about a different thing.
    /// </summary>
    private sealed class CustodyRequiredAfterProductRequestStore(ICustodyStore inner, string message)
        : ICustodyStore
    {
        internal bool Armed;

        internal int PassThroughArmedWrites;

        internal int ArmedWrites;

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            if (Armed && ArmedWrites++ >= PassThroughArmedWrites)
            {
                throw new CustodyRequiredException(message);
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
    /// An honest unenforced store: it really holds the bytes and declares NotEnforced, exactly as
    /// <c>FileSystemCustodyStore</c> does. Not a failure.
    /// </summary>
    private sealed class UnenforcedCustodyStore : ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _byDigest = new(StringComparer.Ordinal);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen);
            _byDigest[digest] = frozen;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, digest, frozen.LongLength, custodyClass);
            var observedAt = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
            return Task.FromResult(new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                new CustodyPolicyEvidence(
                    CustodySchemaIds.CustodyPolicyEvidence,
                    reference,
                    CustodyVerificationProfile.FileSystemUnenforced1,
                    null,
                    CustodyProtection.NotEnforced,
                    observedAt,
                    null)));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[reference.ContentSha256]);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[contentSha256]);
    }

    /// <summary>
    /// Corrupts one read of the body's digest, chosen BY ORDINAL through this interface rather
    /// than armed on a write, because the read it targets happens before the adapter's own hold
    /// write and there is no write to arm on.
    /// </summary>
    /// <remarks>
    /// MEASURED, AND NOT WHAT A FIRST ATTEMPT ASSUMED. The ordinals of this digest through
    /// ReadByDigestAsync are: ONE, the adapter's checked reopen, which is the escape this
    /// drives; TWO, CustodyHold's own write-then-readback verification, which reports a digest
    /// mismatch as a hold failure carrying ITS message under the SAME DocumentBodyNotHeld code.
    /// The session's retention readback never appears here at all, so the reopen is read one
    /// and not read two. Because both ordinals answer with the same refusal code, the DETAIL is
    /// what discriminates them, which is why the test asserts the message and not just the code.
    /// </remarks>
    private sealed class BodyReopenFailingCustodyStore(ICustodyStore inner, byte[] payload)
        : ICustodyStore
    {
        private readonly string _bodyDigest = CustodyDigest.Of(payload);

        internal int BodyReads;

        internal int FailBodyReadOrdinal = 1;

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken) =>
            inner.CreateAsync(bytes, custodyClass, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken)
        {
            if (!string.Equals(contentSha256, _bodyDigest, StringComparison.Ordinal))
            {
                return inner.ReadByDigestAsync(contentSha256, cancellationToken);
            }

            return ++BodyReads == FailBodyReadOrdinal
                ? Task.FromResult<ReadOnlyMemory<byte>>("not the bytes you stored"u8.ToArray())
                : inner.ReadByDigestAsync(contentSha256, cancellationToken);
        }
    }

    /// <summary>An in-memory store whose receipts always classify as Floored.</summary>
    private sealed class FlooringCustodyStore : ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _byDigest = new(StringComparer.Ordinal);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen);
            _byDigest[digest] = frozen;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, digest, frozen.LongLength, custodyClass);
            var observedAt = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-0000000000d2"),
                CustodyProtection.LockedTime,
                observedAt,
                observedAt.AddDays(91));
            return Task.FromResult(new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt, reference, policy));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[reference.ContentSha256]);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[contentSha256]);
    }

    /// <summary>
    /// Admits any well-formed binding, exactly as the LU adapter tests' own resolver does: this
    /// file's subject is the document GET and the record set, never the reduction's own evidence
    /// admission, which those tests already prove can genuinely refuse.
    /// </summary>
    private sealed class PermissiveScopeEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) && IsSha256(binding.SelectorEvidenceSha256);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            IsSha256(binding.ObjectRefSha256);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) &&
            IsSha256(binding.SelectorSetSha256) &&
            IsSha256(binding.RuleEvaluationSha256);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;

        private static bool IsSha256(string value) =>
            value.Length == 64 &&
            value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }

    /// <summary>
    /// The same minimal, vocabulary-complete profile the LU adapter tests build. Its scope binding
    /// is the real production one, so the manifest this file reduces is shaped by production rules
    /// rather than by a hand-written binding.
    /// </summary>
    private static VerifiedLuxembourgSourceProfile BuildProfile() =>
        LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            new SourceArtifactRef("urn:uuid:10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0", new string('1', 64)),
            CompleteEnumerationRef,
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
            []));
}
