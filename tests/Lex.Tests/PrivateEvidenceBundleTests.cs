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
            await bundle.CaptureAsync(
                sourceRequest,
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
        var outcome = await bundle.CaptureAsync(
            request,
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
        var incomplete = Assert.IsType<RejectedStagedResponseEvidence>(
            await bundle.CaptureAsync(
                incompleteRequest,
                Response(bodyComplete: false),
                new MemoryStream([1, 2], writable: false)));
        var interrupted = Assert.IsType<RejectedStagedResponseEvidence>(
            await bundle.CaptureAsync(
                interruptedRequest,
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
            await created.CaptureAsync(
                request,
                Response(),
                new MemoryStream([9, 8, 7], writable: false));
        }
        using (var captured = PrivateEvidenceBundle.Open(staging, plan))
        {
            Assert.False(captured.IsSealed);
            Assert.Single(captured.Records);
            var replay = await captured.CaptureAsync(
                request, Response(), new ThrowOnReadStream());
            Assert.Equal(captured.Records[0].Evidence.ObjectSha256,
                replay.ObjectSha256);
            await captured.SealAsync();
        }
        using (var sealedBundle = PrivateEvidenceBundle.Open(staging, plan))
        {
            Assert.True(sealedBundle.IsSealed);
            Assert.Single(sealedBundle.Records);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sealedBundle.CaptureAsync(
                    request,
                    Response(),
                    new MemoryStream([1], writable: false)));
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
            await bundle.CaptureAsync(
                request,
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
        using (PrivateEvidenceBundle.Create(staging, plan))
        {
        }

        WriteCaptureIntent(staging, prefixRequest, Response());
        File.WriteAllBytes(PendingBodyPath(staging, prefixRequest), [1, 2]);
        WriteCaptureIntent(staging, oversizedRequest, Response());
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
        using (PrivateEvidenceBundle.Create(staging, plan))
        {
        }

        byte[] body = [7, 8, 9];
        var objectSha256 = Convert.ToHexStringLower(SHA256.HashData(body));
        WriteCaptureIntent(staging, request, response);
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
    public async Task A_cancelled_capture_is_recovered_before_a_live_retry()
    {
        var request = Request(0);
        var plan = Plan(request);
        var staging = EmptyDirectory("cancelled-capture");
        using var bundle = PrivateEvidenceBundle.Create(staging, plan);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            bundle.CaptureAsync(
                request,
                Response(),
                new CancelAfterPrefixStream([1, 2, 3])));

        var recovered = Assert.IsType<RejectedStagedResponseEvidence>(
            await bundle.CaptureAsync(request, Response(), new ThrowOnReadStream()));
        Assert.Equal(StagedResponseRejectionReason.TransportInterrupted,
            recovered.Reason);
        Assert.Equal(3, recovered.ByteLength);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(
            staging, PrivateEvidenceBundle.PendingDirectoryName)));
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
            await bundle.CaptureAsync(
                request,
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
            await bundle.CaptureAsync(
                request,
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
            await bundle.CaptureAsync(
                request,
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
    public async Task Seal_requires_exact_planned_inventory_and_commit_is_last()
    {
        var first = Request(0);
        var second = Request(1);
        var staging = EmptyDirectory("exact-plan");

        using var bundle = PrivateEvidenceBundle.Create(
            staging, Plan(first, second));
        await bundle.CaptureAsync(
            first,
            Response(),
            new MemoryStream([1], writable: false));
        await Assert.ThrowsAsync<InvalidDataException>(() => bundle.SealAsync());
        Assert.False(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.ManifestFileName)));
        Assert.False(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.CommitMarkerFileName)));

        await bundle.CaptureAsync(
            second,
            Response(),
            new MemoryStream([2], writable: false));
        await bundle.SealAsync();
        Assert.True(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.CommitMarkerFileName)));

        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(staging, PrivateEvidenceBundle.ManifestFileName)));
        Assert.Equal(
            [first.RequestId, second.RequestId],
            manifest.RootElement.GetProperty("records").EnumerateArray()
                .Select(record => record.GetProperty("request")
                    .GetProperty("request_id").GetString()!).ToArray());
    }

    [Fact]
    public void Bundle_identity_binds_corpus_scope_policy_and_acquisition_plan()
    {
        var request = Request(0);
        var baseline = Plan(request);
        var changedCorpus = Plan(
            [request], baselineCorpus: new string('4', 64));
        var changedScope = Plan(
            [request], enumerationScope: new string('5', 64));
        var changedPolicy = Plan(
            [request], endpointPolicy: new string('6', 64));
        var changedRequest = Plan(Request(
            0, "https://legilux.public.lu/different"));

        Assert.NotEqual(baseline.BundleId, changedCorpus.BundleId);
        Assert.NotEqual(baseline.BundleId, changedScope.BundleId);
        Assert.NotEqual(baseline.BundleId, changedPolicy.BundleId);
        Assert.NotEqual(baseline.BundleId, changedRequest.BundleId);
        Assert.Equal(baseline.AcquisitionPlanSha256,
            changedCorpus.AcquisitionPlanSha256);
        Assert.NotEqual(baseline.AcquisitionPlanSha256,
            changedRequest.AcquisitionPlanSha256);
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
        string endpointPolicy = EndpointPolicy) => new(
        "gha:2026-08-30T101112Z",
        CodeCommit,
        "legilux",
        baselineCorpus,
        enumerationScope,
        endpointPolicy,
        requests);

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
        bool bodyComplete = true) => BoundedResponseMetadata.Create(
        200,
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
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, options);
        File.WriteAllBytes(path, [.. bytes, (byte)'\n']);
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
