using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Ingest;
using Lex.Law;

namespace Lex.Tests;

public sealed class PrivateEvidenceBundleTests : IDisposable
{
    private const string CodeCommit = "0123456789abcdef0123456789abcdef01234567";
    private const string BaselineCorpus =
        "1111111111111111111111111111111111111111111111111111111111111111";
    private const string EnumerationScope =
        "2222222222222222222222222222222222222222222222222222222222222222";
    private const string EndpointPolicy =
        "3333333333333333333333333333333333333333333333333333333333333333";
    private const string AcquisitionPolicy =
        "4444444444444444444444444444444444444444444444444444444444444444";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-private-evidence-{Guid.NewGuid():N}");

    public PrivateEvidenceBundleTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Local_staging_cannot_construct_or_return_durable_evidence()
    {
        Assert.Empty(typeof(EvidenceRef).GetConstructors());
        var constructor = Assert.Single(
            typeof(EvidenceRef).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic),
            candidate => candidate.GetParameters().Select(parameter =>
                    parameter.ParameterType).SequenceEqual(
                [typeof(string), typeof(string), typeof(long)]));
        Assert.True(constructor.IsPrivate);
        Assert.False(typeof(IRawResponseSink).IsAssignableFrom(
            typeof(PrivateEvidenceBundle)));
        Assert.DoesNotContain(typeof(PrivateEvidenceBundle).GetMethods(), method =>
            ReturnsType(method.ReturnType, typeof(EvidenceRef)));
        Assert.False(typeof(EvidenceRef).IsAssignableFrom(
            typeof(CompleteStagedResponseEvidence)));
        Assert.False(typeof(EvidenceRef).IsAssignableFrom(
            typeof(RejectedStagedResponseEvidence)));
        Assert.DoesNotContain(
            typeof(EvidenceRef).Assembly
                .GetCustomAttributes(typeof(InternalsVisibleToAttribute), false)
                .Cast<InternalsVisibleToAttribute>(),
            attribute => attribute.AssemblyName.StartsWith(
                "Lex.Ingest", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(IRawResponseSink).Assembly.GetTypes(), type =>
            !type.IsAbstract
            && !type.IsInterface
            && typeof(IRawResponseSink).IsAssignableFrom(type));

        Assert.Empty(typeof(SourceRequestIdentity).GetConstructors());
        Assert.Equal("Create", Assert.Single(
            typeof(SourceRequestIdentity).GetMethods(
                BindingFlags.Public | BindingFlags.Static),
            method => method.ReturnType == typeof(SourceRequestIdentity)).Name);
        Assert.Empty(typeof(BoundedResponseMetadata).GetConstructors());
        Assert.Equal("Create", Assert.Single(
            typeof(BoundedResponseMetadata).GetMethods(
                BindingFlags.Public | BindingFlags.Static),
            method => method.ReturnType == typeof(BoundedResponseMetadata)).Name);
        Assert.DoesNotContain(
            typeof(RecordedSourceRequest).GetMethods(),
            method => ReturnsType(
                method.ReturnType, typeof(SourceRequestIdentity)));
        Assert.DoesNotContain(
            typeof(RecordedResponseMetadata).GetMethods(),
            method => ReturnsType(
                method.ReturnType, typeof(BoundedResponseMetadata)));

        var beginAttempt = Assert.Single(
            typeof(PrivateEvidenceBundle).GetMethods(),
            method => method.Name == nameof(PrivateEvidenceBundle.BeginAttempt));
        var localCapture = Assert.Single(
            typeof(PrivateEvidenceBundle).GetMethods(),
            method => method.Name == nameof(PrivateEvidenceBundle.CaptureAsync));
        var durableCapture = Assert.Single(
            typeof(IRawResponseSink).GetMethods(),
            method => method.Name == nameof(IRawResponseSink.CaptureAsync));
        Assert.Equal(
            [typeof(SourceRequestIdentity)],
            beginAttempt.GetParameters().Select(parameter =>
                parameter.ParameterType));
        Assert.Contains(
            localCapture.GetParameters(),
            parameter => parameter.ParameterType
                         == typeof(BoundedResponseMetadata));
        Assert.Contains(
            durableCapture.GetParameters(),
            parameter => parameter.ParameterType
                         == typeof(SourceRequestIdentity));
        Assert.Contains(
            durableCapture.GetParameters(),
            parameter => parameter.ParameterType
                         == typeof(BoundedResponseMetadata));
        Assert.DoesNotContain(
            beginAttempt.GetParameters()
                .Concat(localCapture.GetParameters())
                .Concat(durableCapture.GetParameters()),
            parameter => parameter.ParameterType
                         == typeof(RecordedSourceRequest)
                         || parameter.ParameterType
                         == typeof(RecordedResponseMetadata));
    }

    [Fact]
    public async Task Persisted_uri_evidence_strips_secrets_and_binds_full_target_digest()
    {
        const string request =
            "https://request-user:request-pass@legilux.public.lu/source?query-sentinel=medical#request-fragment";
        const string effective =
            "https://effective-user:effective-pass@legilux.public.lu/final?effective-sentinel=employment#effective-fragment";
        var sourceRequest = Request(0, request);
        var response = Response(effective);
        var staging = EmptyDirectory("uri-privacy");

        using (var bundle = PrivateEvidenceBundle.Create(
                   staging, Plan(sourceRequest)))
        {
            var attempt = bundle.BeginAttempt(sourceRequest);
            Assert.Same(sourceRequest, attempt.Request);
            await bundle.CaptureAsync(
                attempt,
                response,
                new MemoryStream([1, 2, 3], writable: false));
            await bundle.SealAsync();
        }

        Assert.Equal("https://legilux.public.lu/source", sourceRequest.RequestUri);
        Assert.Equal(Sha256(request), sourceRequest.RequestUriSha256);
        Assert.Equal("https://legilux.public.lu/final", response.EffectiveSourceUri);
        Assert.Equal(Sha256(effective), response.EffectiveSourceUriSha256);

        var emitted = ReadEmittedText(staging);
        Assert.DoesNotContain("request-user", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("request-pass", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("query-sentinel", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("request-fragment", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("effective-user", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("effective-pass", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("effective-sentinel", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("effective-fragment", emitted, StringComparison.Ordinal);
        Assert.Contains(Sha256(request), emitted, StringComparison.Ordinal);
        Assert.Contains(Sha256(effective), emitted, StringComparison.Ordinal);

        using var reopened = PrivateEvidenceBundle.Open(
            staging, Plan(sourceRequest));
        var reopenedRecord = Assert.Single(reopened.Records);
        var recorded = reopenedRecord.Request;
        Assert.IsType<RecordedSourceRequest>(recorded);
        Assert.IsType<RecordedResponseMetadata>(reopenedRecord.Response);
        Assert.IsType<RecordedSourceRequest>(
            Assert.Single(reopened.Attempts).Request);
        Assert.Equal(sourceRequest.RequestId, recorded.RequestId);
        Assert.Equal(sourceRequest.RequestUriSha256, recorded.RequestUriSha256);
        Assert.NotEqual(Sha256(recorded.RequestUri), recorded.RequestUriSha256);
        Assert.IsNotType<SourceRequestIdentity>(recorded);
    }

    [Fact]
    public async Task Oversized_capture_retains_exact_cap_plus_one_as_rejected_evidence()
    {
        var request = Request(0, maximumResponseBytes: 3);
        var staging = EmptyDirectory("oversized");

        using var bundle = PrivateEvidenceBundle.Create(staging, Plan(request));
        var attempt = bundle.BeginAttempt(request);
        var outcome = await bundle.CaptureAsync(
            attempt,
            Response(),
            new MemoryStream([1, 2, 3, 4, 5], writable: false));
        var rejected = Assert.IsType<RejectedStagedResponseEvidence>(outcome);

        Assert.Equal(StagedResponseRejectionReason.BodyTooLarge, rejected.Reason);
        Assert.Equal(4, rejected.ByteLength);
        Assert.False(rejected.BodyComplete);
        Assert.DoesNotContain(typeof(RejectedStagedResponseEvidence).GetMethods(),
            method => method.Name == "OpenBody");
        var retained = await File.ReadAllBytesAsync(Assert.Single(
            Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.ObjectsDirectoryName))));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, retained);

        var receipt = await bundle.SealAsync();
        Assert.Single(receipt.Records);
        Assert.IsType<RejectedStagedResponseEvidence>(receipt.Records[0].Evidence);
    }

    [Fact]
    public async Task Incomplete_and_interrupted_responses_are_retained_but_not_derivable()
    {
        var incompleteRequest = Request(0);
        var interruptedRequest = Request(1, physicalAttempt: 2);
        var staging = EmptyDirectory("incomplete");

        using var bundle = PrivateEvidenceBundle.Create(
            staging, Plan(incompleteRequest, interruptedRequest));
        var incompleteAttempt = bundle.BeginAttempt(incompleteRequest);
        var incomplete = Assert.IsType<RejectedStagedResponseEvidence>(
            await bundle.CaptureAsync(
                incompleteAttempt,
                Response(bodyComplete: false),
                new MemoryStream([1, 2], writable: false)));
        var interruptedAttempt = bundle.BeginAttempt(interruptedRequest);
        var interrupted = Assert.IsType<RejectedStagedResponseEvidence>(
            await bundle.CaptureAsync(
                interruptedAttempt,
                Response(),
                new ThrowAfterPrefixStream([3, 4])));

        Assert.Equal(StagedResponseRejectionReason.ResponseIncomplete,
            incomplete.Reason);
        Assert.Equal(StagedResponseRejectionReason.TransportInterrupted,
            interrupted.Reason);
        Assert.False(incomplete.BodyComplete);
        Assert.False(interrupted.BodyComplete);
        await bundle.SealAsync();
    }

    [Fact]
    public async Task Inline_transport_read_failures_are_retained_as_interrupted()
    {
        Func<Exception>[] failures =
        [
            () => new HttpRequestException("injected transport failure"),
            () => new System.Net.Sockets.SocketException(10060),
            () => new TimeoutException("injected timeout"),
        ];
        for (var index = 0; index < failures.Length; index++)
        {
            var staging = EmptyDirectory($"inline-transport-{index}");
            using var bundle = PrivateEvidenceBundle.Create(
                staging, DynamicPlan());
            var attempt = bundle.BeginAttempt(Request(0));

            var rejected = Assert.IsType<RejectedStagedResponseEvidence>(
                await bundle.CaptureAsync(
                    attempt,
                    Response(),
                    new ThrowAfterPrefixStream([1, 2], failures[index]())));

            Assert.Equal(
                StagedResponseRejectionReason.TransportInterrupted,
                rejected.Reason);
            Assert.Equal(2, rejected.ByteLength);
        }
    }

    [Fact]
    public async Task Final_rehash_fails_closed_on_object_mutation()
    {
        var request = Request(0);
        var plan = Plan(request);
        var staging = EmptyDirectory("mutated-object");

        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            var attempt = bundle.BeginAttempt(request);
            await bundle.CaptureAsync(
                attempt,
                Response(),
                new MemoryStream([1, 2, 3], writable: false));
            var objectPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.ObjectsDirectoryName)));
            await File.WriteAllBytesAsync(objectPath, [3, 2, 1]);
            await Assert.ThrowsAsync<InvalidDataException>(() => bundle.SealAsync());
            Assert.False(File.Exists(Path.Combine(
                staging, PrivateEvidenceBundle.CommitMarkerFileName)));
        }
    }

    [Fact]
    public async Task Interrupted_seal_never_commits_an_inexact_file_set()
    {
        var request = Request(0);
        var plan = Plan(request);
        var staging = EmptyDirectory("interrupted-seal-inexact");
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            var attempt = bundle.BeginAttempt(request);
            await bundle.CaptureAsync(
                attempt,
                Response(),
                new MemoryStream([1, 2, 3], writable: false));
            await bundle.SealAsync();
        }
        var commit = Path.Combine(
            staging, PrivateEvidenceBundle.CommitMarkerFileName);
        File.Delete(commit);
        await File.WriteAllTextAsync(Path.Combine(staging, "foreign"), "x");

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));
        Assert.False(File.Exists(commit));
    }

    [Fact]
    public async Task Manifest_without_commit_never_cleans_temporary_tampering()
    {
        var request = Request(0);
        var plan = DynamicPlan();
        var staging = EmptyDirectory("manifest-temp-tamper");
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            var attempt = bundle.BeginAttempt(request);
            await bundle.CaptureAsync(
                attempt,
                Response(),
                new MemoryStream([1], writable: false));
            await bundle.SealAsync();
        }
        var commit = Path.Combine(
            staging, PrivateEvidenceBundle.CommitMarkerFileName);
        var tamper = Path.Combine(
            staging,
            PrivateEvidenceBundle.ObjectsDirectoryName,
            ".capture-tamper.tmp");
        File.Delete(commit);
        await File.WriteAllBytesAsync(tamper, [9]);

        PrivateEvidenceBundle? unexpectedlyOpened = null;
        var error = Record.Exception(() =>
            unexpectedlyOpened = PrivateEvidenceBundle.Open(staging, plan));
        unexpectedlyOpened?.Dispose();
        Assert.Contains(
            "retained for forensics",
            Assert.IsType<InvalidDataException>(error).Message,
            StringComparison.Ordinal);
        Assert.True(File.Exists(tamper));
        Assert.False(File.Exists(commit));
    }

    [Fact]
    public async Task Strict_json_rejects_duplicate_members()
    {
        var plan = Plan(Request(0));
        var staging = EmptyDirectory("duplicate-json");
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            await bundle.SealAsync();
        }
        var planPath = Path.Combine(staging, PrivateEvidenceBundle.PlanFileName);
        var json = File.ReadAllText(planPath);
        File.WriteAllText(planPath, json.Replace(
            "{\"schema\":",
            "{\"schema\":\"duplicate\",\"schema\":",
            StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));
    }

    [Fact]
    public async Task Commit_without_manifest_is_not_a_sealed_bundle()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("commit-without-manifest");
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
            await bundle.SealAsync();
        File.Delete(Path.Combine(
            staging, PrivateEvidenceBundle.ManifestFileName));

        var error = Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));

        Assert.Contains("retained for forensics", error.Message,
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.CommitMarkerFileName)));
    }

    [Fact]
    public async Task Seal_requires_terminal_runtime_inventory_and_commit_is_last()
    {
        var first = Request(0);
        var second = Request(1, physicalAttempt: 2);
        var staging = EmptyDirectory("exact-plan");

        using var bundle = PrivateEvidenceBundle.Create(
            staging, Plan(first, second));
        var firstAttempt = bundle.BeginAttempt(first);
        await bundle.CaptureAsync(
            firstAttempt,
            Response(),
            new MemoryStream([1], writable: false));
        var secondAttempt = bundle.BeginAttempt(second);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bundle.SealAsync());
        Assert.False(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.ManifestFileName)));
        Assert.False(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.CommitMarkerFileName)));

        bundle.RecordNoResponse(secondAttempt);
        await bundle.SealAsync();
        Assert.True(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.CommitMarkerFileName)));

        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(staging, PrivateEvidenceBundle.ManifestFileName)));
        Assert.Equal(
            [first.RequestId],
            manifest.RootElement.GetProperty("records").EnumerateArray()
                .Select(record => record.GetProperty("request")
                    .GetProperty("request_id").GetString()!).ToArray());
    }

    [Fact]
    public void Bundle_identity_binds_stable_policies_not_runtime_attempts()
    {
        var request = Request(0);
        var baseline = Plan(request);
        var changedCorpus = Plan(
            [request], baselineCorpus: new string('4', 64));
        var changedScope = Plan(
            [request], enumerationScope: new string('5', 64));
        var changedPolicy = Plan(
            [request], endpointPolicy: new string('6', 64));
        var changedAcquisitionPolicy = Plan(
            [request], acquisitionPolicy: new string('7', 64));
        var changedRequest = Plan(Request(
            0, "https://legilux.public.lu/different"));

        Assert.NotEqual(baseline.BundleId, changedCorpus.BundleId);
        Assert.NotEqual(baseline.BundleId, changedScope.BundleId);
        Assert.NotEqual(baseline.BundleId, changedPolicy.BundleId);
        Assert.NotEqual(baseline.BundleId, changedAcquisitionPolicy.BundleId);
        Assert.Equal(baseline.BundleId, changedRequest.BundleId);
        Assert.Equal(AcquisitionPolicy, baseline.AcquisitionPolicySha256);
    }

    [Fact]
    public async Task Runtime_attempts_append_before_send_without_unused_slots()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("dynamic-attempts");
        PrivateEvidenceStageReceipt sealedReceipt;
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            var first = bundle.BeginAttempt(Request(0));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.AttemptsDirectoryName),
                "*.start.json"));
            Assert.DoesNotContain(
                "\"requests\"",
                File.ReadAllText(Path.Combine(
                    staging, PrivateEvidenceBundle.PlanFileName)),
                StringComparison.Ordinal);
            Assert.Empty(bundle.Records);
            await bundle.CaptureAsync(
                first,
                Response(),
                new MemoryStream([1, 2, 3], writable: false));

            var retry = bundle.BeginAttempt(Request(1, physicalAttempt: 2));
            bundle.RecordNoResponse(retry);
            sealedReceipt = await bundle.SealAsync();
        }

        Assert.Equal(2, sealedReceipt.Attempts.Count);
        Assert.Equal(
            [
                PrivateEvidenceAttemptDisposition.Response,
                PrivateEvidenceAttemptDisposition.NoResponse,
            ],
            sealedReceipt.Attempts.Select(attempt => attempt.Disposition));
        Assert.Equal(
            sealedReceipt.Attempts[^1].AttemptSha256,
            sealedReceipt.AttemptChainSha256);
        Assert.Matches("^[0-9a-f]{64}$", sealedReceipt.AttemptInventorySha256);

        using var reopened = PrivateEvidenceBundle.Open(staging, plan);
        Assert.Equal(
            sealedReceipt.Attempts.Select(attempt => attempt.TerminalSha256),
            reopened.Attempts.Select(attempt => attempt.TerminalSha256));
    }

    [Fact]
    public void Unsealed_restart_refuses_tail_and_head_rollback_without_mutation()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("attempt-head-tail-deletion");
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
            _ = bundle.BeginAttempt(Request(0));

        var start = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(staging, PrivateEvidenceBundle.AttemptsDirectoryName),
            "*.start.json"));
        File.Delete(start);
        WriteAttemptHead(staging, plan, 0, null);
        var before = Directory.EnumerateFileSystemEntries(
            staging, "*", SearchOption.AllDirectories).Order().ToArray();

        var error = Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));

        Assert.Contains("retained for forensics", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(before, Directory.EnumerateFileSystemEntries(
            staging, "*", SearchOption.AllDirectories).Order());
    }

    [Fact]
    public async Task Sealed_reopen_rejects_missing_tail_with_unchanged_head()
    {
        var (staging, plan) = await SealedTwoAttemptBundle(
            "sealed-missing-tail", responses: false);
        var starts = Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.AttemptsDirectoryName),
            "*.start.json").Order().ToArray();
        File.Delete(starts[^1]);

        var error = Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));
        Assert.Contains("attempt head", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sealed_reopen_rejects_multiple_tail_inventory()
    {
        var (staging, plan) = await SealedTwoAttemptBundle(
            "sealed-multiple-tail", responses: false);
        var starts = Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.AttemptsDirectoryName),
            "*.start.json").Order().ToArray();
        using var second = JsonDocument.Parse(File.ReadAllBytes(starts[^1]));
        var predecessor = second.RootElement.GetProperty(
            "attempt_sha256").GetString()!;
        var extra = Request(2, physicalAttempt: 3);
        var extraSha256 = WriteAttemptStart(
            staging, plan, extra, predecessor);
        WriteAttemptHead(staging, plan, 3, extraSha256);

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));
    }

    [Fact]
    public async Task Sealed_reopen_rejects_coherent_tail_and_head_rollback()
    {
        var (staging, plan) = await SealedTwoAttemptBundle(
            "sealed-tail-head-rollback", responses: false);
        var attempts = Path.Combine(
            staging, PrivateEvidenceBundle.AttemptsDirectoryName);
        var starts = Directory.EnumerateFiles(
            attempts, "*.start.json").Order().ToArray();
        var terminals = Directory.EnumerateFiles(
            attempts, "*.terminal.json").Order().ToArray();
        File.Delete(starts[^1]);
        File.Delete(terminals[^1]);
        using var first = JsonDocument.Parse(File.ReadAllBytes(starts[0]));
        WriteAttemptHead(staging, plan, 1,
            first.RootElement.GetProperty("attempt_sha256").GetString());

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));
    }

    [Fact]
    public async Task Sealed_reopen_rejects_non_tail_terminal_and_receipt_deletion()
    {
        var (staging, plan) = await SealedTwoAttemptBundle(
            "sealed-nontail-deletion", responses: true);
        File.Delete(Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.AttemptsDirectoryName),
            "*.terminal.json").Order().First());
        File.Delete(Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.ReceiptsDirectoryName),
            "*.json").Order().First());

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));
    }

    [Fact]
    public async Task Sealed_reopen_rejects_total_attempt_and_head_wipe()
    {
        var (staging, plan) = await SealedTwoAttemptBundle(
            "sealed-total-attempt-wipe", responses: false);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(
                     staging, PrivateEvidenceBundle.AttemptsDirectoryName)))
            File.Delete(path);
        WriteAttemptHead(staging, plan, 0, null);

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));
    }

    [Fact]
    public void Unsealed_restart_does_not_create_a_missing_pending_body()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("unsealed-no-empty-body");
        var request = Request(0);
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            var attempt = bundle.BeginAttempt(request);
            WriteCaptureIntent(
                staging, attempt.AttemptSha256, request, Response());
        }
        var body = PendingBodyPath(staging, request);
        Assert.False(File.Exists(body));

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));

        Assert.False(File.Exists(body));
    }

    [Fact]
    public async Task Seal_final_verification_rejects_attempt_head_mutation()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("seal-head-guard");
        using var bundle = PrivateEvidenceBundle.Create(staging, plan);
        var attempt = bundle.BeginAttempt(Request(0));
        bundle.RecordNoResponse(attempt);
        WriteAttemptHead(staging, plan, 0, null);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            bundle.SealAsync());
        Assert.False(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.ManifestFileName)));
    }

    [Fact]
    public void PublisherEvidenceSession_must_enforce_retry_redirect_policy_before_send()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("attempt-coordinate-sequence");
        using var bundle = PrivateEvidenceBundle.Create(staging, plan);
        var first = bundle.BeginAttempt(Request(
            0, physicalAttempt: 2, redirectHop: 3));
        bundle.RecordNoResponse(first);

        var reset = bundle.BeginAttempt(Request(
            1,
            physicalAttempt: 1,
            redirectHop: 0));
        bundle.RecordNoResponse(reset);
        var upperBound = bundle.BeginAttempt(Request(
            2,
            physicalAttempt: SourceRequestIdentity.MaximumPhysicalAttemptCoordinate,
            redirectHop: SourceRequestIdentity.MaximumRedirectHopCoordinate));
        bundle.RecordNoResponse(upperBound);

        // The bundle records bounded coordinates without inferring a chain.
        // PublisherEvidenceSession must bind the stable original request and
        // enforce retry and redirect policy before any physical send.
        Assert.Equal(
            [
                (2, 3),
                (1, 0),
                (
                    SourceRequestIdentity.MaximumPhysicalAttemptCoordinate,
                    SourceRequestIdentity.MaximumRedirectHopCoordinate),
            ],
            bundle.Attempts.Select(attempt => (
                attempt.Request.PhysicalAttempt,
                attempt.Request.RedirectHop)));
    }

    [Fact]
    public void Attempt_coordinates_are_individually_bounded_recorded_facts()
    {
        Assert.Throws<InvalidDataException>(() => Request(
            0, physicalAttempt: 0));
        Assert.Throws<InvalidDataException>(() => Request(
            0,
            physicalAttempt:
                SourceRequestIdentity.MaximumPhysicalAttemptCoordinate + 1));
        Assert.Throws<InvalidDataException>(() => Request(
            0, redirectHop: -1));
        Assert.Throws<InvalidDataException>(() => Request(
            0,
            redirectHop:
                SourceRequestIdentity.MaximumRedirectHopCoordinate + 1));

        RecordedSourceRequest Persisted(int physicalAttempt, int redirectHop)
        {
            const string redactedUri = "https://legilux.public.lu/source";
            var rawTargetSha256 = Sha256(
                "https://legilux.public.lu/source?private=bound-only");
            var requestId = Sha256(string.Join('\n',
                "lex-source-request/2",
                "legilux",
                "filestore",
                "GET",
                redactedUri,
                rawTargetSha256,
                string.Empty,
                "0",
                "1024",
                physicalAttempt.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                redirectHop.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
            // A persisted redacted URI cannot recompute the digest of the raw
            // target. Restoration verifies the stored, self-consistent claim.
            return RecordedSourceRequest.FromPersistedClaim(
                requestId,
                "legilux",
                "filestore",
                SourceRequestMethod.Get,
                redactedUri,
                rawTargetSha256,
                requestBodySha256: null,
                ordinal: 0,
                maximumResponseBytes: 1024,
                physicalAttempt,
                redirectHop);
        }

        Assert.Throws<InvalidDataException>(() => Persisted(
            SourceRequestIdentity.MaximumPhysicalAttemptCoordinate + 1, 0));
        Assert.Throws<InvalidDataException>(() => Persisted(
            1, SourceRequestIdentity.MaximumRedirectHopCoordinate + 1));
        var valid = Persisted(1, 0);
        Assert.Equal(1, valid.PhysicalAttempt);
        Assert.Equal(0, valid.RedirectHop);
    }

    [Fact]
    public async Task Bundle_cap_rejects_before_append_and_maximum_bundle_reopens()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("attempt-cap");
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            for (var ordinal = 0;
                 ordinal < PrivateEvidenceBundle.MaximumAttemptsPerBundle;
                 ordinal++)
            {
                var attempt = bundle.BeginAttempt(Request(
                    ordinal,
                    $"https://legilux.public.lu/source/{ordinal / 16}",
                    physicalAttempt: ordinal % 16 + 1));
                bundle.RecordNoResponse(attempt);
            }

            Assert.Throws<InvalidOperationException>(() =>
                bundle.BeginAttempt(Request(
                    PrivateEvidenceBundle.MaximumAttemptsPerBundle)));
            Assert.Equal(
                PrivateEvidenceBundle.MaximumAttemptsPerBundle * 2,
                Directory.EnumerateFiles(Path.Combine(
                    staging, PrivateEvidenceBundle.AttemptsDirectoryName))
                    .Count());
            await bundle.SealAsync();
        }

        using var reopened = PrivateEvidenceBundle.Open(staging, plan);
        Assert.True(reopened.IsSealed);
        Assert.Equal(PrivateEvidenceBundle.MaximumAttemptsPerBundle,
            reopened.Attempts.Count);
    }

    [Fact]
    public async Task Live_attempts_block_seal_and_tokens_are_bundle_bound()
    {
        var plan = DynamicPlan();
        var firstRoot = EmptyDirectory("attempt-owner-a");
        var secondRoot = EmptyDirectory("attempt-owner-b");
        using var first = PrivateEvidenceBundle.Create(firstRoot, plan);
        using var second = PrivateEvidenceBundle.Create(secondRoot, plan);
        var attempt = first.BeginAttempt(Request(0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.SealAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => second.CaptureAsync(
            attempt,
            Response(),
            new MemoryStream([1], writable: false)));

        first.RecordNoResponse(attempt);
        await first.SealAsync();
    }

    [Fact]
    public async Task Strict_reopen_rejects_a_response_terminal_without_its_receipt()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("terminal-without-receipt");
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            var attempt = bundle.BeginAttempt(Request(0));
            await bundle.CaptureAsync(
                attempt,
                Response(),
                new MemoryStream([8], writable: false));
        }
        File.Delete(Assert.Single(Directory.EnumerateFiles(Path.Combine(
            staging, PrivateEvidenceBundle.ReceiptsDirectoryName))));

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));
    }

    [Fact]
    public async Task Attempt_chain_and_inventory_bind_order_requests_and_outcomes()
    {
        async Task<PrivateEvidenceStageReceipt> Build(
            string name,
            bool reverse,
            bool secondHasResponse)
        {
            var root = EmptyDirectory(name);
            using var bundle = PrivateEvidenceBundle.Create(root, DynamicPlan());
            var first = bundle.BeginAttempt(Request(
                0,
                reverse
                    ? "https://legilux.public.lu/second"
                    : "https://legilux.public.lu/first"));
            bundle.RecordNoResponse(first);
            var second = bundle.BeginAttempt(Request(
                1,
                reverse
                    ? "https://legilux.public.lu/first"
                    : "https://legilux.public.lu/second"));
            if (secondHasResponse)
                await bundle.CaptureAsync(
                    second,
                    Response(),
                    new MemoryStream([9], writable: false));
            else
                bundle.RecordNoResponse(second);
            return await bundle.SealAsync();
        }

        var baseline = await Build("attempt-digest-a", reverse: false,
            secondHasResponse: false);
        var reordered = await Build("attempt-digest-b", reverse: true,
            secondHasResponse: false);
        var changedOutcome = await Build("attempt-digest-c", reverse: false,
            secondHasResponse: true);

        Assert.NotEqual(baseline.AttemptChainSha256,
            reordered.AttemptChainSha256);
        Assert.Equal(baseline.AttemptChainSha256,
            changedOutcome.AttemptChainSha256);
        Assert.NotEqual(baseline.AttemptInventorySha256,
            changedOutcome.AttemptInventorySha256);
    }

    [Fact]
    public void Exclusive_owner_lock_blocks_a_second_live_owner()
    {
        var plan = Plan(Request(0));
        var staging = EmptyDirectory("exclusive");

        using var first = PrivateEvidenceBundle.Create(staging, plan);
        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(staging, plan));
    }

    private static bool ReturnsType(Type candidate, Type forbidden) =>
        candidate == forbidden
        || candidate.IsGenericType
        && candidate.GetGenericArguments().Any(argument =>
            ReturnsType(argument, forbidden));

    private PrivateEvidenceAcquisitionPlan Plan(
        params SourceRequestIdentity[] requests) => Plan(
        requests, BaselineCorpus, EnumerationScope, EndpointPolicy);

    private static PrivateEvidenceAcquisitionPlan Plan(
        IReadOnlyCollection<SourceRequestIdentity> requests,
        string baselineCorpus = BaselineCorpus,
        string enumerationScope = EnumerationScope,
        string endpointPolicy = EndpointPolicy,
        string acquisitionPolicy = AcquisitionPolicy) => new(
        "gha:2026-08-30T101112Z",
        CodeCommit,
        "legilux",
        baselineCorpus,
        enumerationScope,
        endpointPolicy,
        acquisitionPolicy);

    private static PrivateEvidenceAcquisitionPlan DynamicPlan() => new(
        "gha:2026-08-30T101112Z",
        CodeCommit,
        "legilux",
        BaselineCorpus,
        EnumerationScope,
        EndpointPolicy,
        AcquisitionPolicy);

    private static SourceRequestIdentity Request(
        int ordinal,
        string uri = "https://legilux.public.lu/source",
        long maximumResponseBytes = 1024,
        int physicalAttempt = 1,
        int redirectHop = 0) => SourceRequestIdentity.Create(
        "legilux",
        "filestore",
        SourceRequestMethod.Get,
        uri,
        requestBodySha256: null,
        ordinal,
        maximumResponseBytes,
        physicalAttempt,
        redirectHop);

    private static BoundedResponseMetadata Response(
        string uri = "https://legilux.public.lu/final",
        bool bodyComplete = true,
        int statusCode = 200) => BoundedResponseMetadata.Create(
        statusCode,
        "application/xml",
        "utf-8",
        "\"publisher-etag\"",
        DateTimeOffset.Parse("2026-08-29T09:00:00Z"),
        DateTimeOffset.Parse("2026-08-30T10:11:12Z"),
        uri,
        bodyComplete);

    private string EmptyDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task<(string Root, PrivateEvidenceAcquisitionPlan Plan)>
        SealedTwoAttemptBundle(string name, bool responses)
    {
        var root = EmptyDirectory(name);
        var plan = DynamicPlan();
        using var bundle = PrivateEvidenceBundle.Create(root, plan);
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            var attempt = bundle.BeginAttempt(Request(
                ordinal, physicalAttempt: ordinal + 1));
            if (responses)
            {
                await bundle.CaptureAsync(
                    attempt,
                    Response(),
                    new MemoryStream([(byte)(ordinal + 1)], writable: false));
            }
            else
            {
                bundle.RecordNoResponse(attempt);
            }
        }
        await bundle.SealAsync();
        return (root, plan);
    }

    private static string ReadEmittedText(string root)
    {
        var builder = new StringBuilder();
        foreach (var path in new[]
                 {
                     Path.Combine(root, PrivateEvidenceBundle.PlanFileName),
                     Path.Combine(root, PrivateEvidenceBundle.AttemptHeadFileName),
                     Path.Combine(root, PrivateEvidenceBundle.AttemptsDirectoryName),
                     Path.Combine(root, PrivateEvidenceBundle.PendingDirectoryName),
                     Path.Combine(root, PrivateEvidenceBundle.ReceiptsDirectoryName),
                     Path.Combine(root, PrivateEvidenceBundle.ManifestFileName),
                     Path.Combine(root, PrivateEvidenceBundle.CommitMarkerFileName),
                 })
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(
                             path, "*", SearchOption.TopDirectoryOnly))
                    builder.Append(File.ReadAllText(file));
            }
            else if (File.Exists(path))
            {
                builder.Append(File.ReadAllText(path));
            }
        }
        return builder.ToString();
    }

    private static string Sha256(string value) =>
        Sha256(Encoding.UTF8.GetBytes(value));

    private static string Sha256(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    private static void WriteCaptureIntent(
        string root,
        string attemptSha256,
        SourceRequestIdentity request,
        BoundedResponseMetadata response)
    {
        WriteTestJson(
            Path.Combine(
                root,
                PrivateEvidenceBundle.PendingDirectoryName,
                request.RequestId + ".intent.json"),
            new
            {
                Schema = PrivateEvidenceBundle.CaptureIntentSchema,
                AttemptSha256 = attemptSha256,
                BodyFileName = request.RequestId + ".body",
                Request = new
                {
                    request.RequestId,
                    request.Publisher,
                    request.Channel,
                    Method = request.Method.ToString().ToLowerInvariant(),
                    request.RequestUri,
                    request.RequestUriSha256,
                    request.RequestBodySha256,
                    request.Ordinal,
                    request.MaximumResponseBytes,
                    request.PhysicalAttempt,
                    request.RedirectHop,
                },
                Response = new
                {
                    response.StatusCode,
                    response.ContentType,
                    response.Charset,
                    response.EntityTag,
                    response.LastModified,
                    response.FetchedAt,
                    response.EffectiveSourceUri,
                    response.EffectiveSourceUriSha256,
                    response.BodyComplete,
                },
            });
    }

    private static void WriteAttemptHead(
        string root,
        PrivateEvidenceAcquisitionPlan plan,
        int attemptCount,
        string? headSha256)
    {
        WriteTestJson(
            Path.Combine(root, PrivateEvidenceBundle.AttemptHeadFileName),
            new
            {
                Schema = PrivateEvidenceBundle.AttemptHeadSchema,
                BundleId = plan.BundleId,
                AttemptCount = attemptCount,
                HeadAttemptSha256 = headSha256,
            });
    }

    private static string WriteAttemptStart(
        string root,
        PrivateEvidenceAcquisitionPlan plan,
        SourceRequestIdentity request,
        string predecessor)
    {
        var requestDocument = new
        {
            request.RequestId,
            request.Publisher,
            request.Channel,
            Method = request.Method.ToString().ToLowerInvariant(),
            request.RequestUri,
            request.RequestUriSha256,
            request.RequestBodySha256,
            request.Ordinal,
            request.MaximumResponseBytes,
            request.PhysicalAttempt,
            request.RedirectHop,
        };
        var attemptSha256 = Convert.ToHexStringLower(SHA256.HashData(
            TestJsonBytes(new
            {
                Schema = PrivateEvidenceBundle.AttemptChainSchema,
                BundleId = plan.BundleId,
                PredecessorAttemptSha256 = predecessor,
                Request = requestDocument,
            })));
        File.WriteAllBytes(
            Path.Combine(
                root,
                PrivateEvidenceBundle.AttemptsDirectoryName,
                $"{request.Ordinal:D6}-{request.RequestId}.start.json"),
            TestJsonBytes(new
            {
                Schema = PrivateEvidenceBundle.AttemptStartSchema,
                BundleId = plan.BundleId,
                PredecessorAttemptSha256 = predecessor,
                AttemptSha256 = attemptSha256,
                Request = requestDocument,
            }));
        return attemptSha256;
    }

    private static string PendingBodyPath(
        string root, SourceRequestIdentity request) => Path.Combine(
        root,
        PrivateEvidenceBundle.PendingDirectoryName,
        request.RequestId + ".body");

    private static void WriteTestJson(string path, object document)
    {
        File.WriteAllBytes(path, TestJsonBytes(document));
    }

    private static byte[] TestJsonBytes(object document)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, options);
        return [.. bytes, (byte)'\n'];
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class ThrowAfterPrefixStream(
        byte[] prefix,
        Exception? failure = null) : Stream
    {
        private bool _returnedPrefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_returnedPrefix)
                throw failure ?? new IOException("injected interruption");
            _returnedPrefix = true;
            prefix.CopyTo(buffer);
            return ValueTask.FromResult(prefix.Length);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("body must not be read");
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("body must not be read");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class CancelAfterPrefixStream(byte[] prefix) : Stream
    {
        private bool _returnedPrefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_returnedPrefix)
                throw new OperationCanceledException("injected cancellation");
            _returnedPrefix = true;
            prefix.CopyTo(buffer);
            return ValueTask.FromResult(prefix.Length);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
