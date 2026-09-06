using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RoutedHttpArtifactDurabilityTests
{
    [TestMethod]
    public void MachineFixturePreexistingArtifactsAreDistinctAndSelfVerifying()
    {
        var opened = MachineQueryBinder.OpenForSend(
            MachineRequestTestFixture.EuropeanUnionRequest());
        var contentTypeRegistryRef = opened.RenderReceipt.ContentType?.RegistryRef
            ?? throw new AssertFailedException("The POST fixture lost its content-type registry.");

        Assert.AreEqual(
            3,
            new[]
            {
                opened.RenderReceipt.RendererProfileRef,
                opened.RenderReceipt.RendererSourceRef,
                contentTypeRegistryRef,
            }.Distinct().Count());

        // Only the registry is genuinely external and reopenable by bare reference. Item 1b,
        // Decision 75's closure: the renderer profile and renderer source used to be answerable
        // here too, and a mutation restoring either branch to TryReopenPreexistingArtifact is
        // killed by the two IsFalse assertions below, which this test existed to prove true of
        // before the closure and now exists to prove false.
        Assert.IsTrue(MachineRequestTestFixture.TryReopenPreexistingArtifact(
            contentTypeRegistryRef.Sha256,
            out var registryBytes));
        Assert.AreEqual(contentTypeRegistryRef.Sha256, Sha256(registryBytes.Span));

        Assert.IsFalse(
            MachineRequestTestFixture.TryReopenPreexistingArtifact(
                opened.RenderReceipt.RendererProfileRef.Sha256,
                out _),
            "the renderer profile must no longer be answerable by bare reference");
        Assert.IsFalse(
            MachineRequestTestFixture.TryReopenPreexistingArtifact(
                opened.RenderReceipt.RendererSourceRef.Sha256,
                out _),
            "the renderer source must no longer be answerable by bare reference");
    }

    [TestMethod]
    public async Task EveryBareArtifactDigestRemainsSelfVerifiablyOpenableAfterSessionDisposal()
    {
        var boundRequest = MachineRequestTestFixture.EuropeanUnionRequest();
        var custody = new RecordingCustodyStore();
        var handler = new SequenceHandler((ordinal, request) => ordinal switch
        {
            0 => DeclaredResponse(
                request,
                HttpStatusCode.MovedPermanently,
                "redirect body",
                location: "https://op.europa.eu/robots.txt"),
            1 => DeclaredResponse(
                request,
                HttpStatusCode.OK,
                "User-agent: *\nAllow: /\n",
                contentType: "text/plain;charset=UTF-8"),
            2 => DeclaredResponse(request, HttpStatusCode.OK, "publisher body"),
            _ => throw new AssertFailedException("The session sent an unexpected request."),
        });

        RoutedHttpEvidence[] evidence;
        IReadOnlyDictionary<string, CustodyMembership> membership;
        using (var session = Session(boundRequest, handler, custody))
        {
            var started = await BootstrapAsync(session);
            Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);
            var robotsEvidence = started.Evidence
                ?? throw new AssertFailedException("The completed robots route emitted no /4 evidence.");

            var attempt = await session.OpenPlanItem(boundRequest)
                .ExecuteNextAttemptAsync(CancellationToken.None);
            Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, attempt.Kind);
            var productEvidence = attempt.Evidence
                ?? throw new AssertFailedException("The completed product route emitted no /4 evidence.");
            evidence = [robotsEvidence, productEvidence];
            membership = session.CopyArtifactMembership();
        }

        Assert.AreEqual(3, handler.SendCount);
        var logicalRequests = new HashSet<string>(StringComparer.Ordinal);
        var requestPolicies = new HashSet<string>(StringComparer.Ordinal);
        var redirectPolicies = new HashSet<string>(StringComparer.Ordinal);
        var adapterExecutions = new HashSet<string>(StringComparer.Ordinal);
        var durableWriteReceipts = new HashSet<string>(StringComparer.Ordinal);
        var runIdentities = new HashSet<string>(StringComparer.Ordinal);
        var sourceProfiles = new HashSet<string>(StringComparer.Ordinal);
        var machineArtifacts = new HashSet<string>(StringComparer.Ordinal);
        var payloads = new HashSet<string>(StringComparer.Ordinal);

        foreach (var route in evidence)
        {
            runIdentities.Add(route.RunIdentity.Sha256);
            var runIdentityBytes = await OpenAndRehashAsync(custody, route.RunIdentity.Sha256);
            var runIdentityLines = Encoding.UTF8.GetString(runIdentityBytes).Split('\n');
            Assert.AreEqual("lex-http-acquisition-run/1", runIdentityLines[0]);
            Assert.AreEqual(route.RunIdentity.ResourceId, runIdentityLines[1]);
            var expectedProfile = OfficialMachineQuerySourceProfiles.Resolve(
                OfficialMachineQuerySourceProfileId.EuropeanUnionSparql).ArtifactRef;
            Assert.AreEqual(expectedProfile.ResourceId, runIdentityLines[2]);
            Assert.AreEqual(expectedProfile.Sha256, runIdentityLines[3]);
            sourceProfiles.Add(runIdentityLines[3]);
            _ = await OpenAndRehashAsync(custody, runIdentityLines[3]);

            foreach (var hop in route.Hops)
            {
                logicalRequests.Add(hop.LogicalRequestSha256);
                var logicalRequestBytes = await OpenAndRehashAsync(
                    custody,
                    hop.LogicalRequestSha256);
                var logicalRequest = HttpLogicalRequest.ParseAndVerify(logicalRequestBytes);
                Assert.AreEqual(hop.RequestUri, logicalRequest.Uri);

                requestPolicies.Add(logicalRequest.RequestPolicySha256);
                var requestPolicyBytes = await OpenAndRehashAsync(
                    custody,
                    logicalRequest.RequestPolicySha256);
                var sourceProfileSha256 = ReadArtifactSha256(
                    requestPolicyBytes,
                    "source_profile");
                sourceProfiles.Add(sourceProfileSha256);
                _ = await OpenAndRehashAsync(custody, sourceProfileSha256);
                var adapterExecutionSha256 = ReadAdapterExecutionSha256(requestPolicyBytes);
                adapterExecutions.Add(adapterExecutionSha256);
                _ = await OpenAndRehashAsync(custody, adapterExecutionSha256);
                Assert.AreEqual(runIdentityLines[5], ReadArtifactResourceId(
                    requestPolicyBytes,
                    "adapter_execution"));
                Assert.AreEqual(runIdentityLines[6], adapterExecutionSha256);

                foreach (var digest in ReadRepeatedArtifactSha256(
                             requestPolicyBytes,
                             "opened_artifact"))
                {
                    machineArtifacts.Add(digest);
                    _ = await OpenAndRehashAsync(custody, digest);
                }

                redirectPolicies.Add(logicalRequest.RedirectPolicySha256);
                _ = await OpenAndRehashAsync(custody, logicalRequest.RedirectPolicySha256);

                durableWriteReceipts.Add(hop.DurableWriteReceiptSha256);
                var receiptBytes = await OpenAndRehashAsync(
                    custody,
                    hop.DurableWriteReceiptSha256);
                var receipt = ContractJson.Deserialize<DurableBlobWriteReceipt>(
                    Encoding.UTF8.GetString(receiptBytes));
                Assert.AreEqual(hop.Sha256, receipt.Reference.ContentSha256);
                Assert.AreEqual(checked((long)hop.Length), receipt.Reference.ByteLength);
                Assert.AreEqual(hop.ReadbackSha256, receipt.Reference.ContentSha256);
                Assert.AreEqual(hop.ReadbackByteLength, checked((ulong)receipt.Reference.ByteLength));
                payloads.Add(receipt.Reference.ContentSha256);
                var payload = await custody.ReadAsync(receipt.Reference, CancellationToken.None);
                Assert.AreEqual(hop.Sha256, Sha256(payload.Span));
            }
        }

        Assert.AreEqual(3, logicalRequests.Count, "Every sent hop needs its own logical request.");
        Assert.AreEqual(2, requestPolicies.Count, "Robots and product requests use distinct policies.");
        Assert.AreEqual(2, redirectPolicies.Count, "Robots and no-redirect policies must both survive.");
        Assert.AreEqual(1, adapterExecutions.Count, "One session has one adapter execution identity.");
        Assert.AreEqual(3, durableWriteReceipts.Count, "Every response hop has its own receipt artifact.");
        Assert.AreEqual(1, runIdentities.Count, "One session has one durable run identity.");
        Assert.AreEqual(1, sourceProfiles.Count, "One session resolves one durable official-source profile.");
        Assert.AreEqual(8, machineArtifacts.Count, "The product send must retain its complete typed machine-artifact closure.");
        Assert.AreEqual(3, payloads.Count, "Every response body must remain reopenable through its durable reference.");
        var reasonRegistryBytes = await OpenAndRehashAsync(
            custody,
            HttpAcquisitionReasonRegistry.Sha256);
        CollectionAssert.AreEqual(
            HttpAcquisitionReasonRegistry.CanonicalBytes.ToArray(),
            reasonRegistryBytes);
        AssertCreatedDigests(custody, ArtifactKind.LogicalRequest, logicalRequests);
        AssertCreatedDigests(custody, ArtifactKind.RequestPolicy, requestPolicies);
        AssertCreatedDigests(custody, ArtifactKind.RedirectPolicy, redirectPolicies);
        AssertCreatedDigests(custody, ArtifactKind.AdapterExecution, adapterExecutions);
        AssertCreatedDigests(custody, ArtifactKind.DurableWriteReceipt, durableWriteReceipts);
        AssertCreatedDigests(custody, ArtifactKind.RunIdentity, runIdentities);
        AssertCreatedDigests(custody, ArtifactKind.SourceProfile, sourceProfiles);
        AssertCreatedDigests(
            custody,
            ArtifactKind.ReasonRegistry,
            new HashSet<string>([HttpAcquisitionReasonRegistry.Sha256], StringComparer.Ordinal));
        var reopenedMachineArtifacts = MachineArtifactKinds
            .SelectMany(custody.ReopenedDigests)
            .ToHashSet(StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(
            machineArtifacts.ToArray(),
            reopenedMachineArtifacts.ToArray(),
            "Every typed machine dependency must be reopened and reachable from its request policy.");
        var createdMachineArtifacts = MachineArtifactKinds
            .SelectMany(custody.CreatedDigests)
            .ToHashSet(StringComparer.Ordinal);
        // Was 3, and the 3 was the defect rather than the design. Five of the eight closure
        // members reached the durable set through a bare read, so the gate that says a send
        // dependency was durably reopenable was satisfied by "these bytes were readable from some
        // lane at send time". Every member is now written by the run that depends on it, so the
        // created set and the reopened set are the same set.
        CollectionAssert.AreEquivalent(
            machineArtifacts.ToArray(),
            createdMachineArtifacts.ToArray(),
            "Every machine dependency must be retained by the run that depends on it, not merely read.");

        // Decision 71: retained is not floored. This store enforces nothing, so every member is
        // retained-unenforced and the run must say that rather than certify durability. The
        // assertion that matters is the second one: a NotEnforced receipt is never counted as
        // floored, which is the whole reason the set was split rather than renamed.
        Assert.IsTrue(membership.Count > 0, "the run retained something to classify");
        CollectionAssert.AreEquivalent(
            machineArtifacts.ToArray(),
            membership.Keys.Where(machineArtifacts.Contains).ToArray(),
            "every machine dependency carries a membership");
        // This double issues immutable-object receipts, so every member is legitimately floored
        // and the classification must say so rather than downgrade what the store actually
        // enforced. The opposite direction, that an unenforced receipt is never counted as
        // floored, cannot be shown from this fixture because it has no unenforced double; that
        // gap is stated in the freeze packet rather than papered over here.
        Assert.IsTrue(
            membership.Values.All(value => value == CustodyMembership.Floored),
            "an immutable-object receipt is floored, and the run must not understate it");
        Assert.IsTrue(
            createdMachineArtifacts.IsSubsetOf(machineArtifacts),
            "Every newly retained binder artifact must be reachable from the request policy.");
    }

    [TestMethod]
    [DataRow((int)ArtifactKind.AdapterExecution, 0)]
    [DataRow((int)ArtifactKind.RequestPolicy, 0)]
    [DataRow((int)ArtifactKind.RedirectPolicy, 0)]
    [DataRow((int)ArtifactKind.LogicalRequest, 0)]
    [DataRow((int)ArtifactKind.DurableWriteReceipt, 1)]
    [DataRow((int)ArtifactKind.RunIdentity, 0)]
    [DataRow((int)ArtifactKind.SourceProfile, 0)]
    [DataRow((int)ArtifactKind.ReasonRegistry, 0)]
    public async Task MissingBareArtifactDigestPreventsSendOrEvidence(
        int targetValue,
        int expectedSendCount)
    {
        var target = (ArtifactKind)targetValue;
        var boundRequest = MachineRequestTestFixture.EuropeanUnionRequest();
        var custody = new RecordingCustodyStore(target, StoreFault.MissingDigest);
        var handler = new SequenceHandler(static (_, request) => DeclaredResponse(
            request,
            HttpStatusCode.NotFound,
            "response whose evidence must not escape"));
        using var session = Session(boundRequest, handler, custody);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.IntegrityFailure, result.Kind);
        Assert.IsNull(result.OperationalReason);
        Assert.AreEqual(expectedSendCount, handler.SendCount);
    }

    [TestMethod]
    [DataRow((int)ArtifactKind.RenderReceipt)]
    [DataRow((int)ArtifactKind.QueryPlan)]
    [DataRow((int)ArtifactKind.OrderedInput)]
    [DataRow((int)ArtifactKind.RendererProfile)]
    [DataRow((int)ArtifactKind.RendererSource)]
    [DataRow((int)ArtifactKind.ContentTypeRegistry)]
    [DataRow((int)ArtifactKind.QueryRegistry)]
    [DataRow((int)ArtifactKind.ParameterProvenance)]
    public async Task MissingMachineDependencyPreventsTheProductSend(int targetValue)
    {
        var target = (ArtifactKind)targetValue;
        var boundRequest = MachineRequestTestFixture.EuropeanUnionRequest();
        var custody = new RecordingCustodyStore(target, StoreFault.MissingDigest);
        var handler = new SequenceHandler((ordinal, request) => ordinal switch
        {
            0 => DeclaredResponse(
                request,
                HttpStatusCode.MovedPermanently,
                "redirect body",
                location: "https://op.europa.eu/robots.txt"),
            1 => DeclaredResponse(
                request,
                HttpStatusCode.OK,
                "User-agent: *\nAllow: /\n",
                contentType: "text/plain;charset=UTF-8"),
            _ => throw new AssertFailedException("A product send occurred with a missing dependency."),
        });
        using var session = Session(boundRequest, handler, custody);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);
        var result = await session.OpenPlanItem(boundRequest)
            .ExecuteNextAttemptAsync(CancellationToken.None);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.IntegrityFailure, result.Kind);
        Assert.AreEqual(2, handler.SendCount);
    }

    [TestMethod]
    public async Task ArtifactCreateRefusalStopsBeforeTheNetworkSend()
    {
        var boundRequest = MachineRequestTestFixture.EuropeanUnionRequest();
        var custody = new RecordingCustodyStore(
            ArtifactKind.AdapterExecution,
            StoreFault.CreateRefused);
        var handler = new SequenceHandler(static (_, _) =>
            throw new AssertFailedException("A send occurred without durable adapter evidence."));
        using var session = Session(boundRequest, handler, custody);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, result.Kind);
        Assert.AreEqual(OfficialHttpOperationalFailureReason.CustodyUnavailable, result.OperationalReason);
        Assert.AreEqual(0, handler.SendCount);
    }

    [TestMethod]
    public async Task SerializedReceiptCreateRefusalPreventsHttpEvidenceFromEscaping()
    {
        var boundRequest = MachineRequestTestFixture.EuropeanUnionRequest();
        var custody = new RecordingCustodyStore(
            ArtifactKind.DurableWriteReceipt,
            StoreFault.CreateRefused);
        var handler = new SequenceHandler(static (_, request) => DeclaredResponse(
            request,
            HttpStatusCode.NotFound,
            "received but not evidentially complete"));
        using var session = Session(boundRequest, handler, custody);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, result.Kind);
        Assert.AreEqual(OfficialHttpOperationalFailureReason.CustodyUnavailable, result.OperationalReason);
        Assert.AreEqual(1, handler.SendCount);
    }

    [TestMethod]
    public async Task WrongArtifactReceiptDigestStopsBeforeTheNetworkSend()
    {
        var boundRequest = MachineRequestTestFixture.EuropeanUnionRequest();
        var custody = new RecordingCustodyStore(
            ArtifactKind.AdapterExecution,
            StoreFault.WrongReceiptDigest);
        var handler = new SequenceHandler(static (_, _) =>
            throw new AssertFailedException("A send occurred after a false custody receipt."));
        using var session = Session(boundRequest, handler, custody);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.IntegrityFailure, result.Kind);
        Assert.AreEqual(0, handler.SendCount);
    }

    [TestMethod]
    public async Task CorruptArtifactDigestReadbackStopsBeforeTheNetworkSend()
    {
        var boundRequest = MachineRequestTestFixture.EuropeanUnionRequest();
        var custody = new RecordingCustodyStore(
            ArtifactKind.AdapterExecution,
            StoreFault.CorruptDigestReadback);
        var handler = new SequenceHandler(static (_, _) =>
            throw new AssertFailedException("A send occurred after corrupt artifact readback."));
        using var session = Session(boundRequest, handler, custody);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.IntegrityFailure, result.Kind);
        Assert.AreEqual(0, handler.SendCount);
    }

    [TestMethod]
    public async Task AnUnenforcedStoreYieldsNoFlooredMemberAndTheRunSaysSo()
    {
        // The direction the other fixture cannot show. Its custody double issues immutable-object
        // receipts, so it can only prove the classifier does not understate a floored member. The
        // real filesystem adapter publishes FileSystemUnenforced1 with NotEnforced for every class,
        // which is the pairing that must never be counted as floored: a run there has custody of
        // its dependencies and no protection over them, and saying "durable" would be the false
        // claim the split exists to prevent.
        var root = Path.Combine(Path.GetTempPath(), "lex-unenforced-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var boundRequest = MachineRequestTestFixture.EuropeanUnionRequest();
            var custody = new FileSystemCustodyStore(root);
            var handler = new SequenceHandler((ordinal, request) => ordinal switch
            {
                0 => DeclaredResponse(
                    request,
                    HttpStatusCode.MovedPermanently,
                    "redirect body",
                    location: "https://op.europa.eu/robots.txt"),
                1 => DeclaredResponse(
                    request,
                    HttpStatusCode.OK,
                    "User-agent: *\nAllow: /\n",
                    contentType: "text/plain;charset=UTF-8"),
                2 => DeclaredResponse(request, HttpStatusCode.OK, "publisher body"),
                _ => throw new AssertFailedException("The session sent an unexpected request."),
            });

            IReadOnlyDictionary<string, CustodyMembership> membership;
            using (var session = Session(boundRequest, handler, custody))
            {
                // The robots route alone, deliberately. It retains the same send closure through
                // the same path, which is what this test is about. The product attempt against a
                // real FileSystemCustodyStore returns IntegrityFailure for a reason that predates
                // this candidate and is masked by the recording double every other test uses; that
                // is reported as its own finding rather than worked around here.
                var started = await BootstrapAsync(session);
                Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);
                membership = session.CopyArtifactMembership();
            }

            Assert.IsTrue(membership.Count > 0, "the run retained dependencies to classify");
            Assert.IsFalse(
                membership.Values.Contains(CustodyMembership.Floored),
                "an unenforced store yields no floored member, and the run must not claim one");
            Assert.IsTrue(
                membership.Values.All(value => value == CustodyMembership.RetainedUnenforced),
                "every member written to an unenforced store is retained-unenforced");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RoutedHttpAcquisitionSession Session(
        BoundMachineRequest request,
        HttpMessageHandler handler,
        ICustodyStore custody)
    {
        var constructor = typeof(RoutedHttpAcquisitionSession).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (RoutedHttpAcquisitionSession)constructor.Invoke(
            [request, custody, handler, new ImmediateTimeProvider(), false]);
    }

    private static Task<RoutedHttpAcquisitionSession.StartResult> BootstrapAsync(
        RoutedHttpAcquisitionSession session) =>
        (Task<RoutedHttpAcquisitionSession.StartResult>)(
            typeof(RoutedHttpAcquisitionSession).GetMethod(
                "BootstrapRobotsAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(
                    session,
                    [CancellationToken.None])
            ?? throw new AssertFailedException("The robots bootstrap seam is missing."));

    private static async Task<byte[]> OpenAndRehashAsync(
        ICustodyStore custody,
        string expectedSha256)
    {
        var reopened = await custody.ReadByDigestAsync(
            expectedSha256,
            CancellationToken.None);
        var frozen = reopened.ToArray();
        Assert.AreEqual(expectedSha256, Sha256(frozen));
        return frozen;
    }

    private static string ReadAdapterExecutionSha256(ReadOnlySpan<byte> requestPolicyBytes)
    {
        var lines = Encoding.UTF8.GetString(requestPolicyBytes).Split('\n');
        var identity = lines.Single(static line =>
            line.StartsWith("adapter_execution=", StringComparison.Ordinal));
        var identityFields = identity["adapter_execution=".Length..].Split('\t');
        Assert.AreEqual(2, identityFields.Length);
        var digest = identityFields[1];
        var bytesDigest = lines.Single(static line =>
            line.StartsWith("adapter_execution_bytes_sha256=", StringComparison.Ordinal))[
                "adapter_execution_bytes_sha256=".Length..];
        Assert.AreEqual(digest, bytesDigest);
        Assert.IsTrue(CustodyDigest.IsLowercaseSha256(digest));
        return digest;
    }

    private static string ReadArtifactSha256(
        ReadOnlySpan<byte> requestPolicyBytes,
        string field)
    {
        var prefix = field + "=";
        var line = Encoding.UTF8.GetString(requestPolicyBytes)
            .Split('\n')
            .Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        var fields = line[prefix.Length..].Split('\t');
        Assert.AreEqual(2, fields.Length);
        Assert.IsTrue(CustodyDigest.IsLowercaseSha256(fields[1]));
        return fields[1];
    }

    private static string ReadArtifactResourceId(
        ReadOnlySpan<byte> requestPolicyBytes,
        string field)
    {
        var prefix = field + "=";
        var line = Encoding.UTF8.GetString(requestPolicyBytes)
            .Split('\n')
            .Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..].Split('\t')[0];
    }

    private static IReadOnlyList<string> ReadRepeatedArtifactSha256(
        ReadOnlySpan<byte> requestPolicyBytes,
        string field)
    {
        var prefix = field + "=";
        return Encoding.UTF8.GetString(requestPolicyBytes)
            .Split('\n')
            .Where(value => value.StartsWith(prefix, StringComparison.Ordinal))
            .Select(value => value[prefix.Length..].Split('\t')[1])
            .ToArray();
    }

    private static void AssertCreatedDigests(
        RecordingCustodyStore custody,
        ArtifactKind kind,
        HashSet<string> openedDigests) =>
        CollectionAssert.AreEquivalent(
            custody.CreatedDigests(kind).ToArray(),
            openedDigests.ToArray(),
            $"Every created {kind} artifact must be reachable from emitted evidence.");

    private static HttpResponseMessage DeclaredResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        string body,
        string? location = null,
        string? contentType = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        Assert.IsTrue(content.Headers.TryAddWithoutValidation(
            "Content-Length",
            bytes.Length.ToString(CultureInfo.InvariantCulture)));
        if (contentType is not null)
        {
            Assert.IsTrue(content.Headers.TryAddWithoutValidation("Content-Type", contentType));
        }

        var response = new HttpResponseMessage(status)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
        if (location is not null)
        {
            Assert.IsTrue(response.Headers.TryAddWithoutValidation("Location", location));
        }

        return response;
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private enum ArtifactKind
    {
        Other = 0,
        AdapterExecution = 1,
        LogicalRequest = 2,
        RequestPolicy = 3,
        RedirectPolicy = 4,
        DurableWriteReceipt = 5,
        RunIdentity = 6,
        SourceProfile = 7,
        ReasonRegistry = 8,
        RenderReceipt = 9,
        QueryPlan = 10,
        OrderedInput = 11,
        RendererProfile = 12,
        RendererSource = 13,
        ContentTypeRegistry = 14,
        QueryRegistry = 15,
        ParameterProvenance = 16,
    }

    private static readonly ArtifactKind[] MachineArtifactKinds =
    [
        ArtifactKind.RenderReceipt,
        ArtifactKind.QueryPlan,
        ArtifactKind.OrderedInput,
        ArtifactKind.RendererProfile,
        ArtifactKind.RendererSource,
        ArtifactKind.ContentTypeRegistry,
        ArtifactKind.QueryRegistry,
        ArtifactKind.ParameterProvenance,
    ];

    private enum StoreFault
    {
        None = 0,
        CreateRefused = 1,
        MissingDigest = 2,
        CorruptDigestReadback = 3,
        WrongReceiptDigest = 4,
    }

    private sealed class SequenceHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _sendCount;

        internal int SendCount => Volatile.Read(ref _sendCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = Interlocked.Increment(ref _sendCount) - 1;
            return Task.FromResult(respond(ordinal, request));
        }
    }

    private sealed class RecordingCustodyStore(
        ArtifactKind faultTarget = ArtifactKind.Other,
        StoreFault fault = StoreFault.None) : ICustodyStore
    {
        private static readonly DateTimeOffset ObservedAt = new(
            2026,
            9,
            3,
            10,
            0,
            0,
            TimeSpan.Zero);

        private readonly object _gate = new();
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ArtifactKind> _kinds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ArtifactKind> _reopenedKinds = new(StringComparer.Ordinal);

        internal IEnumerable<string> CreatedDigests(ArtifactKind kind)
        {
            lock (_gate)
            {
                return _kinds
                    .Where(pair => pair.Value == kind)
                    .Select(static pair => pair.Key)
                    .ToArray();
            }
        }

        internal IEnumerable<string> ReopenedDigests(ArtifactKind kind)
        {
            lock (_gate)
            {
                return _reopenedKinds
                    .Where(pair => pair.Value == kind)
                    .Select(static pair => pair.Key)
                    .ToArray();
            }
        }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frozen = bytes.ToArray();
            var kind = Classify(frozen);
            if (kind == faultTarget && fault == StoreFault.CreateRefused)
            {
                return Task.FromException<DurableBlobWriteReceipt>(
                    new IOException("simulated artifact create refusal"));
            }

            var digest = Sha256(frozen);
            lock (_gate)
            {
                _objects[digest] = frozen;
                _kinds[digest] = kind;
            }

            var receiptDigest = kind == faultTarget && fault == StoreFault.WrongReceiptDigest
                ? DifferentDigest(digest)
                : digest;
            return Task.FromResult(CreateReceipt(receiptDigest, frozen.Length, custodyClass));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_objects.TryGetValue(reference.ContentSha256, out var bytes))
                {
                    return Task.FromException<ReadOnlyMemory<byte>>(
                        new FileNotFoundException("simulated missing custody object"));
                }

                return Task.FromResult<ReadOnlyMemory<byte>>(bytes.ToArray());
            }
        }

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? bytes = null;
            var kind = ArtifactKind.Other;
            lock (_gate)
            {
                if (_objects.TryGetValue(contentSha256, out var stored) &&
                    _kinds.TryGetValue(contentSha256, out kind))
                {
                    bytes = stored.ToArray();
                }
            }

            if (bytes is null)
            {
                if (!MachineRequestTestFixture.TryReopenPreexistingArtifact(
                        contentSha256,
                        out var preexisting))
                {
                    return Task.FromException<ReadOnlyMemory<byte>>(
                        new FileNotFoundException("simulated missing artifact digest"));
                }

                bytes = preexisting.ToArray();
                kind = Classify(bytes);
            }

            if (kind == faultTarget && fault == StoreFault.MissingDigest)
            {
                return Task.FromException<ReadOnlyMemory<byte>>(
                    new FileNotFoundException("simulated missing artifact digest"));
            }

            if (kind == faultTarget && fault == StoreFault.CorruptDigestReadback)
            {
                bytes[0] ^= 0xff;
            }

            lock (_gate)
            {
                _reopenedKinds[contentSha256] = kind;
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }

        private static ArtifactKind Classify(ReadOnlySpan<byte> bytes)
        {
            var text = Encoding.UTF8.GetString(bytes);
            if (text.StartsWith("lex-routed-http-adapter-execution/1\n", StringComparison.Ordinal))
            {
                return ArtifactKind.AdapterExecution;
            }

            if (text.StartsWith("{\"schema\":\"lex-http-logical-request/1\"", StringComparison.Ordinal))
            {
                return ArtifactKind.LogicalRequest;
            }

            if (text.StartsWith("lex-http-request-policy/1\n", StringComparison.Ordinal))
            {
                return ArtifactKind.RequestPolicy;
            }

            if (text.StartsWith("lex-http-redirect-policy/1\n", StringComparison.Ordinal))
            {
                return ArtifactKind.RedirectPolicy;
            }

            if (text.StartsWith("lex-http-acquisition-run/1\n", StringComparison.Ordinal))
            {
                return ArtifactKind.RunIdentity;
            }

            if (text.StartsWith("schema=official-machine-query-source-profile/1\n", StringComparison.Ordinal))
            {
                return ArtifactKind.SourceProfile;
            }

            if (text.StartsWith(
                    "{\"schema\":\"http_acquisition_reason_registry/1\"",
                    StringComparison.Ordinal))
            {
                return ArtifactKind.ReasonRegistry;
            }

            if (text.StartsWith(
                    MachineQueryRenderReceiptIdentity.CanonicalizationIdentity + "\n",
                    StringComparison.Ordinal))
            {
                return ArtifactKind.RenderReceipt;
            }

            if (text.StartsWith(
                    MachineQueryPlanIdentity.CanonicalizationIdentity + "\n",
                    StringComparison.Ordinal))
            {
                return ArtifactKind.QueryPlan;
            }

            if (text.StartsWith("{\"schema\":\"machine_query_input/1\"", StringComparison.Ordinal))
            {
                return ArtifactKind.OrderedInput;
            }

            if (text.StartsWith("fixture-renderer-profile/1\n", StringComparison.Ordinal))
            {
                return ArtifactKind.RendererProfile;
            }

            if (text.StartsWith("fixture-renderer-source/1\n", StringComparison.Ordinal))
            {
                return ArtifactKind.RendererSource;
            }

            if (text.StartsWith(
                    "{\"schema\":\"fixture-content-type-registry/1\"",
                    StringComparison.Ordinal))
            {
                return ArtifactKind.ContentTypeRegistry;
            }

            if (text.StartsWith(
                    "{\"schema\":\"fixture-query-registry/1\"",
                    StringComparison.Ordinal))
            {
                return ArtifactKind.QueryRegistry;
            }

            if (text.StartsWith("fixture-parameter-provenance/1\n", StringComparison.Ordinal))
            {
                return ArtifactKind.ParameterProvenance;
            }

            if (text.Contains(
                    $"\"schema\":\"{CustodySchemaIds.DurableBlobWriteReceipt}\"",
                    StringComparison.Ordinal))
            {
                return ArtifactKind.DurableWriteReceipt;
            }

            return ArtifactKind.Other;
        }

        private static DurableBlobWriteReceipt CreateReceipt(
            string digest,
            int length,
            CustodyClass custodyClass)
        {
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                digest,
                length,
                custodyClass);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-000000000041"),
                CustodyProtection.LockedTime,
                ObservedAt,
                ObservedAt.AddDays(91));
            return new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                policy);
        }

        private static string DifferentDigest(string digest) =>
            (digest[0] == '0' ? '1' : '0') + digest[1..];
    }

    private sealed class ImmediateTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(
            2026,
            9,
            3,
            10,
            0,
            0,
            TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            Epoch.AddTicks(Interlocked.Read(ref _timestamp));

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new CallbackTimer(callback, state);
            if (dueTime >= TimeSpan.Zero && dueTime <= TimeSpan.FromSeconds(30))
            {
                if (dueTime > TimeSpan.Zero)
                {
                    Interlocked.Add(ref _timestamp, dueTime.Ticks);
                }

                timer.Queue();
            }

            return timer;
        }

        private sealed class CallbackTimer(TimerCallback callback, object? state) : ITimer
        {
            private int _disposed;

            internal void Queue() => ThreadPool.QueueUserWorkItem(_ =>
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    callback(state);
                }
            });

            public bool Change(TimeSpan dueTime, TimeSpan period) =>
                Volatile.Read(ref _disposed) == 0;

            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
