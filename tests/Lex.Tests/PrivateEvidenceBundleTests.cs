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
    public async Task Restart_recovers_created_captured_and_sealed_states()
    {
        var request = Request(0);
        var plan = Plan(request);
        var staging = EmptyDirectory("restart");

        using (PrivateEvidenceBundle.Create(staging, plan))
        {
        }
        using (var created = PrivateEvidenceBundle.Open(staging, plan))
        {
            Assert.False(created.IsSealed);
            Assert.Empty(created.Records);
            var attempt = created.BeginAttempt(request);
            await created.CaptureAsync(
                attempt,
                Response(),
                new MemoryStream([9, 8, 7], writable: false));
        }
        using (var captured = PrivateEvidenceBundle.Open(staging, plan))
        {
            Assert.False(captured.IsSealed);
            Assert.Single(captured.Records);
            Assert.Single(captured.Attempts);
            await captured.SealAsync();
        }
        using (var sealedBundle = PrivateEvidenceBundle.Open(staging, plan))
        {
            Assert.True(sealedBundle.IsSealed);
            Assert.Single(sealedBundle.Records);
            Assert.Throws<InvalidOperationException>(() =>
                sealedBundle.BeginAttempt(Request(1)));
        }
    }

    [Fact]
    public void Restart_recovers_each_atomic_create_boundary()
    {
        var plan = Plan(Request(0));
        var beforePlan = EmptyDirectory("create-before-plan");
        using (PrivateEvidenceBundle.Create(beforePlan, plan))
        {
        }
        File.Delete(Path.Combine(beforePlan, PrivateEvidenceBundle.PlanFileName));
        Directory.Delete(Path.Combine(
            beforePlan, PrivateEvidenceBundle.ObjectsDirectoryName));
        Directory.Delete(Path.Combine(
            beforePlan, PrivateEvidenceBundle.ReceiptsDirectoryName));
        using (var recovered = PrivateEvidenceBundle.Open(beforePlan, plan))
            Assert.False(recovered.IsSealed);

        var beforeDirectories = EmptyDirectory("create-before-directories");
        using (PrivateEvidenceBundle.Create(beforeDirectories, plan))
        {
        }
        Directory.Delete(Path.Combine(
            beforeDirectories, PrivateEvidenceBundle.ObjectsDirectoryName));
        Directory.Delete(Path.Combine(
            beforeDirectories, PrivateEvidenceBundle.ReceiptsDirectoryName));
        using var reopened = PrivateEvidenceBundle.Open(beforeDirectories, plan);
        Assert.False(reopened.IsSealed);
    }

    [Fact]
    public async Task Reopen_recovers_orphans_and_finishes_a_valid_interrupted_seal()
    {
        var request = Request(0);
        var plan = Plan(request);
        var staging = EmptyDirectory("recovery");
        string manifestPath;
        string commitPath;

        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            var attempt = bundle.BeginAttempt(request);
            await bundle.CaptureAsync(
                attempt,
                Response(),
                new MemoryStream([1, 2, 3], writable: false));
            await File.WriteAllBytesAsync(Path.Combine(
                staging,
                PrivateEvidenceBundle.ObjectsDirectoryName,
                new string('a', 64) + ".bin"), [4, 5]);
            await File.WriteAllBytesAsync(Path.Combine(
                staging,
                PrivateEvidenceBundle.ObjectsDirectoryName,
                ".capture-interrupted.tmp"), [6]);
        }

        using (var recovered = PrivateEvidenceBundle.Open(staging, plan))
        {
            Assert.Single(Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.ObjectsDirectoryName)));
            await recovered.SealAsync();
            manifestPath = Path.Combine(
                staging, PrivateEvidenceBundle.ManifestFileName);
            commitPath = Path.Combine(
                staging, PrivateEvidenceBundle.CommitMarkerFileName);
            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(commitPath));
        }

        File.Delete(commitPath);
        using var resealed = PrivateEvidenceBundle.Open(staging, plan);
        Assert.True(resealed.IsSealed);
        Assert.True(File.Exists(commitPath));
    }

    [Fact]
    public void Restart_finalizes_pending_prefix_and_cap_plus_one_without_refetch()
    {
        var prefixRequest = Request(0, maximumResponseBytes: 8);
        var oversizedRequest = Request(1, maximumResponseBytes: 3);
        var plan = Plan(prefixRequest, oversizedRequest);
        var staging = EmptyDirectory("pending-prefix");
        string prefixAttemptSha256;
        string oversizedAttemptSha256;
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            prefixAttemptSha256 = bundle.BeginAttempt(prefixRequest)
                .AttemptSha256;
            oversizedAttemptSha256 = bundle.BeginAttempt(oversizedRequest)
                .AttemptSha256;
        }

        WriteCaptureIntent(
            staging, prefixAttemptSha256, prefixRequest, Response());
        File.WriteAllBytes(PendingBodyPath(staging, prefixRequest), [1, 2]);
        WriteCaptureIntent(
            staging, oversizedAttemptSha256, oversizedRequest, Response());
        File.WriteAllBytes(PendingBodyPath(staging, oversizedRequest), [3, 4, 5, 6]);

        using var recovered = PrivateEvidenceBundle.Open(staging, plan);

        Assert.Collection(
            recovered.Records,
            first =>
            {
                var evidence = Assert.IsType<RejectedStagedResponseEvidence>(
                    first.Evidence);
                Assert.Equal(StagedResponseRejectionReason.TransportInterrupted,
                    evidence.Reason);
                Assert.Equal(2, evidence.ByteLength);
            },
            second =>
            {
                var evidence = Assert.IsType<RejectedStagedResponseEvidence>(
                    second.Evidence);
                Assert.Equal(StagedResponseRejectionReason.BodyTooLarge,
                    evidence.Reason);
                Assert.Equal(4, evidence.ByteLength);
            });
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(
            staging, PrivateEvidenceBundle.PendingDirectoryName)));
    }

    [Fact]
    public void Restart_finishes_a_durable_outcome_after_object_move()
    {
        var request = Request(0);
        var response = Response();
        var plan = Plan(request);
        var staging = EmptyDirectory("pending-object");
        string attemptSha256;
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            attemptSha256 = bundle.BeginAttempt(request).AttemptSha256;
        }

        byte[] body = [7, 8, 9];
        var objectSha256 = Convert.ToHexStringLower(SHA256.HashData(body));
        WriteCaptureIntent(staging, attemptSha256, request, response);
        WriteCaptureOutcome(
            staging,
            request,
            objectSha256,
            body.Length,
            StagedResponseDisposition.Complete,
            rejectionReason: null);
        File.WriteAllBytes(Path.Combine(
            staging,
            PrivateEvidenceBundle.ObjectsDirectoryName,
            objectSha256 + ".bin"), body);

        using var recovered = PrivateEvidenceBundle.Open(staging, plan);

        var record = Assert.Single(recovered.Records);
        Assert.IsType<CompleteStagedResponseEvidence>(record.Evidence);
        Assert.Equal(objectSha256, record.Evidence.ObjectSha256);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(
            staging, PrivateEvidenceBundle.PendingDirectoryName)));
    }

    [Fact]
    public async Task A_cancelled_capture_is_recovered_before_a_same_input_live_retry()
    {
        var request = Request(0);
        var plan = Plan(request);
        var staging = EmptyDirectory("cancelled-capture");
        using var bundle = PrivateEvidenceBundle.Create(staging, plan);
        var attempt = bundle.BeginAttempt(request);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            bundle.CaptureAsync(
                attempt,
                Response(),
                new CancelAfterPrefixStream([1, 2, 3])));

        var recovered = Assert.IsType<RejectedStagedResponseEvidence>(
            await bundle.CaptureAsync(
                attempt,
                Response(),
                new ThrowOnReadStream()));
        Assert.Equal(StagedResponseRejectionReason.TransportInterrupted,
            recovered.Reason);
        Assert.Equal(3, recovered.ByteLength);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(
            staging, PrivateEvidenceBundle.PendingDirectoryName)));
    }

    [Fact]
    public async Task Recovering_multiple_live_attempts_retires_every_terminal_token()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("multiple-live-recovery");
        using var bundle = PrivateEvidenceBundle.Create(staging, plan);
        var first = bundle.BeginAttempt(Request(0));
        var second = bundle.BeginAttempt(Request(1, physicalAttempt: 2));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            bundle.CaptureAsync(
                first,
                Response(),
                new CancelAfterPrefixStream([1])));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            bundle.CaptureAsync(
                second,
                Response(),
                new CancelAfterPrefixStream([2])));
        _ = await bundle.CaptureAsync(
            second,
            Response(bodyComplete: false),
            new ThrowOnReadStream());

        var receipt = await bundle.SealAsync();
        Assert.Equal(2, receipt.Attempts.Count);
        Assert.All(receipt.Attempts, attempt => Assert.Equal(
            PrivateEvidenceAttemptDisposition.Response,
            attempt.Disposition));
    }

    [Fact]
    public async Task Recovered_capture_rejects_different_response_metadata()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("recovered-metadata-mismatch");
        using var bundle = PrivateEvidenceBundle.Create(staging, plan);
        var attempt = bundle.BeginAttempt(Request(0));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            bundle.CaptureAsync(
                attempt,
                Response(),
                new CancelAfterPrefixStream([1, 2])));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            bundle.CaptureAsync(
                attempt,
                Response(statusCode: 201),
                new ThrowOnReadStream()));
        var receipt = await bundle.SealAsync();
        Assert.Equal(200, Assert.Single(receipt.Records).Response.StatusCode);
    }

    [Fact]
    public async Task Recovery_is_reentrant_and_a_committed_tree_is_never_normalized()
    {
        var request = Request(0);
        var plan = Plan(request);
        var interrupted = EmptyDirectory("recovery-reentrant");
        using (PrivateEvidenceBundle.Create(interrupted, plan))
        {
        }
        File.Delete(Path.Combine(interrupted, PrivateEvidenceBundle.PlanFileName));
        Directory.Delete(Path.Combine(
            interrupted, PrivateEvidenceBundle.ObjectsDirectoryName));
        Directory.Delete(Path.Combine(
            interrupted, PrivateEvidenceBundle.ReceiptsDirectoryName));
        Directory.Delete(Path.Combine(
            interrupted, PrivateEvidenceBundle.PendingDirectoryName));
        Directory.CreateDirectory(Path.Combine(
            interrupted, PrivateEvidenceBundle.ObjectsDirectoryName));

        using (var recovered = PrivateEvidenceBundle.Open(interrupted, plan))
            Assert.False(recovered.IsSealed);

        var committed = EmptyDirectory("committed-exact");
        using (var bundle = PrivateEvidenceBundle.Create(committed, plan))
        {
            var attempt = bundle.BeginAttempt(request);
            await bundle.CaptureAsync(
                attempt,
                Response(),
                new MemoryStream([1, 2, 3], writable: false));
            await bundle.SealAsync();
        }
        var foreignObject = Path.Combine(
            committed,
            PrivateEvidenceBundle.ObjectsDirectoryName,
            new string('a', 64) + ".bin");
        await File.WriteAllBytesAsync(foreignObject, [4]);

        PrivateEvidenceBundle? unexpectedlyOpened = null;
        var error = Record.Exception(() =>
            unexpectedlyOpened = PrivateEvidenceBundle.Open(committed, plan));
        unexpectedlyOpened?.Dispose();
        Assert.IsType<InvalidDataException>(error);
        Assert.True(File.Exists(foreignObject));
    }

    [Fact]
    public async Task Final_rehash_and_strict_reopen_fail_closed_on_mutation()
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

        var strict = EmptyDirectory("strict-json");
        using (PrivateEvidenceBundle.Create(strict, plan))
        {
        }
        var planPath = Path.Combine(strict, PrivateEvidenceBundle.PlanFileName);
        var planJson = await File.ReadAllTextAsync(planPath);
        await File.WriteAllTextAsync(planPath,
            planJson.TrimEnd()[..^1] + ",\"unexpected\":true}\n");
        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.Open(strict, plan));
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
        Assert.IsType<InvalidDataException>(error);
        Assert.True(File.Exists(tamper));
        Assert.False(File.Exists(commit));
    }

    [Fact]
    public void Strict_json_rejects_duplicate_members()
    {
        var plan = Plan(Request(0));
        var staging = EmptyDirectory("duplicate-json");
        using (PrivateEvidenceBundle.Create(staging, plan))
        {
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
    public async Task Seal_requires_terminal_runtime_inventory_and_commit_is_last()
    {
        var first = Request(0);
        var second = Request(1);
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
                    physicalAttempt: ordinal %
                        SourceRequestIdentity.MaximumPhysicalAttempt + 1));
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
    public void Reopen_rejects_a_directly_crafted_attempt_inventory_above_cap()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("crafted-attempt-cap");
        string predecessor;
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            for (var ordinal = 0;
                 ordinal < PrivateEvidenceBundle.MaximumAttemptsPerBundle;
                 ordinal++)
            {
                var attempt = bundle.BeginAttempt(Request(
                    ordinal,
                    physicalAttempt: ordinal %
                        SourceRequestIdentity.MaximumPhysicalAttempt + 1));
                bundle.RecordNoResponse(attempt);
            }
            predecessor = bundle.Attempts[^1].AttemptSha256;
        }

        var request = Request(PrivateEvidenceBundle.MaximumAttemptsPerBundle);
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
                staging,
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

        PrivateEvidenceBundle? unexpectedlyOpened = null;
        var error = Record.Exception(() =>
            unexpectedlyOpened = PrivateEvidenceBundle.Open(staging, plan));
        unexpectedlyOpened?.Dispose();
        Assert.Contains(
            "exceeds its bundle cap",
            Assert.IsType<InvalidDataException>(error).Message,
            StringComparison.Ordinal);
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
    public async Task Restart_terminalizes_an_unfinished_attempt_without_overclaiming_send()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("unknown-attempt");
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
            _ = bundle.BeginAttempt(Request(0));

        using var recovered = PrivateEvidenceBundle.Open(staging, plan);

        var attempt = Assert.Single(recovered.Attempts);
        Assert.Equal(
            PrivateEvidenceAttemptDisposition.NotAttemptedOrSendStateUnknown,
            attempt.Disposition);
        var receipt = await recovered.SealAsync();
        Assert.Equal(attempt.TerminalSha256,
            Assert.Single(receipt.Attempts).TerminalSha256);
    }

    [Fact]
    public async Task Restart_binds_a_durable_receipt_to_its_missing_terminal()
    {
        var plan = DynamicPlan();
        var staging = EmptyDirectory("receipt-before-terminal");
        var request = Request(0);
        using (var bundle = PrivateEvidenceBundle.Create(staging, plan))
        {
            var attempt = bundle.BeginAttempt(request);
            await bundle.CaptureAsync(
                attempt,
                Response(),
                new MemoryStream([5, 6, 7], writable: false));
        }
        File.Delete(Assert.Single(Directory.EnumerateFiles(
            Path.Combine(staging, PrivateEvidenceBundle.AttemptsDirectoryName),
            "*.terminal.json")));

        using var recovered = PrivateEvidenceBundle.Open(staging, plan);

        var terminal = Assert.Single(recovered.Attempts);
        Assert.Equal(PrivateEvidenceAttemptDisposition.Response,
            terminal.Disposition);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(
                Assert.Single(Directory.EnumerateFiles(Path.Combine(
                    staging,
                    PrivateEvidenceBundle.ReceiptsDirectoryName)))))),
            terminal.ResponseReceiptSha256);
        await recovered.SealAsync();
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
        int physicalAttempt = 1) => SourceRequestIdentity.Create(
        "legilux",
        "filestore",
        SourceRequestMethod.Get,
        uri,
        requestBodySha256: null,
        ordinal,
        maximumResponseBytes,
        physicalAttempt,
        redirectHop: 0);

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

    private static string ReadEmittedText(string root)
    {
        var builder = new StringBuilder();
        foreach (var path in new[]
                 {
                     Path.Combine(root, PrivateEvidenceBundle.PlanFileName),
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
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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

    private static void WriteCaptureOutcome(
        string root,
        SourceRequestIdentity request,
        string objectSha256,
        long byteLength,
        StagedResponseDisposition disposition,
        StagedResponseRejectionReason? rejectionReason)
    {
        WriteTestJson(
            Path.Combine(
                root,
                PrivateEvidenceBundle.PendingDirectoryName,
                request.RequestId + ".outcome.json"),
            new
            {
                Schema = PrivateEvidenceBundle.CaptureOutcomeSchema,
                Evidence = new
                {
                    Disposition = disposition.ToString().ToLowerInvariant(),
                    request.RequestId,
                    ObjectSha256 = objectSha256,
                    ByteLength = byteLength,
                    RejectionReason = rejectionReason?.ToString()
                        .ToLowerInvariant(),
                },
            });
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

    private sealed class ThrowAfterPrefixStream(byte[] prefix) : Stream
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
            if (_returnedPrefix) throw new IOException("injected interruption");
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
