using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-06c-LU-2 items 3, 4 and 5: the Luxembourg document GET actually sent through
/// <see cref="RoutedHttpAcquisitionSession"/>, its per-object refusal mapping, and
/// <see cref="CorpusRecordSetWriter.WriteAsync"/> as the run's last step.
/// </summary>
/// <remarks>
/// WHY THESE DRIVE THE PHASE DIRECTLY RATHER THAN THE WHOLE RunAsync, stated plainly because it is
/// the honest answer to the scope ruling's own question about the accepted fraction of a real LU
/// manifest: that fraction is ZERO of N, structurally.
/// <c>LuxembourgBodyJoin.ResolveCandidate</c> attaches eight unconditional milestone blockers to
/// every body candidate and returns Withheld on every path, and
/// <c>LuxembourgScopeResolver.ResolveBody</c> has no AcceptedCandidate branch at all, so the
/// Body/AcceptedSelected accounting set of every manifest a real LU run can produce is empty and no
/// GET is attempted. <see cref="TheBodyAxisOfEveryRealLuxembourgManifestAcceptsNothing"/> pins that
/// rather than leaving it to this comment. A manifest with a genuinely accepted body axis is
/// therefore built here, exactly as the EU lane's own tests build one, so that the fetch, the
/// classification and the record set can be exercised for real.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgDocumentGetTests
{
    // The real, live-verified filestore XML manifestation: this exact path returned HTTP 200 with
    // genuine Akoma Ntoso, 19,986 bytes, SHA-256
    // 9e43a99e4b9735e383d989989d4005fc9e1676f4094c2633f30b2f056d5e476d. The path is real; the body
    // bytes scripted below are NOT that document (a 19,986-byte fixture buys nothing this test
    // needs), and nothing here claims they are.
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

    // ---------------------------------------------------------------------------------------
    // The accepted fraction of a real LU manifest.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The scope ruling asked what fraction of a real LU manifest is accepted on the body axis. The
    /// answer is zero of N, and it is structural: <c>LuxembourgBodyJoin</c>'s own candidate
    /// resolution returns Withheld unconditionally, and the resolver's own body projection has no
    /// accepting arm. Asserted against the production functions themselves rather than against a
    /// sample run, because a sample could only ever show that no accepted row HAPPENED to appear.
    /// </summary>
    [TestMethod]
    public void TheBodyAxisOfEveryRealLuxembourgManifestAcceptsNothing()
    {
        var resolveBody = typeof(VerifiedLuxembourgSourceProfile).Assembly
            .GetType("Lex.V3.Contracts.Source.Luxembourg.LuxembourgScopeResolver")!
            .GetMethod("ResolveBody", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(resolveBody, "the body projection this assertion is about must still exist.");

        // Every LuxembourgBodyCandidateResolution the production join can build is Withheld, and a
        // Withheld candidate is by construction one with at least one blocker (its own constructor
        // refuses a blocker-free Withheld). So no candidate can ever reach an accepting body arm.
        Assert.AreEqual(
            2,
            Enum.GetValues<LuxembourgBodyCandidateDisposition>().Length,
            "the candidate disposition vocabulary this claim reads is exactly Withheld and AcceptedCandidate.");
        var accepting = typeof(LuxembourgBodyJoin)
            .GetMethod("ResolveCandidate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(accepting);
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

        // The record-set half is NOT this lane's to change. CorpusRecordSetWriter's own floor gate
        // is shared with the EU lane and the gate ownership ruling
        // (lex-event-20260904T214500631Z-2988b4fbae224252b08849326325a2a6) put it in the lane that
        // merges first, so it still refuses an unenforced store. Asserted as it is, so this test
        // states the real boundary rather than implying the whole path works.
        var written = await new CorpusRecordSetWriter(store).WriteAsync(
            manifest, manifestRef, RunIdentityRef(), outcomes, CancellationToken.None);
        Assert.IsNotNull(
            written.Refusal,
            "until the shared writer's gate lands in the EU lane, the SET still refuses on an "
            + "unenforced store even though the BODY above is held.");
        Assert.AreEqual(CorpusRecordSetWriteRefusalKind.RecordSetNotHeld, written.Refusal!.Kind);
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
        AcquireCoreAsync(response, address ?? Address());

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
        ICustodyStore Store)> AcquireCoreAsync(
        Func<HttpRequestMessage, int, HttpResponseMessage> response,
        LuxembourgDocumentFetchAddress address) =>
        await AcquireWithHandlerAsync(new RobotsThenDocumentHandler(response), address);

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

    private static SourceObjectRef ObjectRef()
    {
        var canonicalKey = "lu-document-get:" + ObjectPublisherUri;
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(CompleteEnumerationRef, "lu_document_get_root"),
            ObjectPublisherUri,
            canonicalKey,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey))),
            CompleteEnumerationRef,
            null);
    }

    private static SourceArtifactRef RunIdentityRef() => new(
        $"urn:uuid:{Guid.NewGuid():D}",
        Convert.ToHexStringLower(SHA256.HashData("lu-document-get-run"u8.ToArray())));

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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = Interlocked.Increment(ref _sendCount);
            if (request.RequestUri?.AbsolutePath == "/robots.txt")
            {
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
        VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
            new SourceArtifactRef("urn:uuid:10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0", new string('1', 64)),
            CompleteEnumerationRef,
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
            []));
}
