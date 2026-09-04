using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Shared plumbing for driving a real <see cref="RoutedHttpAcquisitionSession"/> through a
/// Luxembourg count-then-pages pass, used by <c>LuxembourgDeliveryEvidenceSetTests</c> (step 5, no
/// executor yet) and by the executor's own tests (steps 6-8). A hand-assembled "Observe" routine
/// mirrors what the executor's driving algorithm does, deliberately duplicated here rather than
/// shared with production code, because step 5 is proved before the executor exists to share it
/// with.
/// </summary>
internal static class LuxembourgAcquisitionTestFixture
{
    internal const string SubjectsSetId = "S";

    /// <summary>
    /// Constructs and bootstraps a session against a test handler and time provider, through <see
    /// cref="RoutedHttpAcquisitionSession.StartWithTestTransportAsync"/>, the one internal door a
    /// same-assembly driver uses to start a session on a caller-supplied transport. This used to
    /// reach the session's private constructor and private <c>BootstrapRobotsAsync</c> by
    /// reflection from this fixture, which meant a rename of either one compiled cleanly here and
    /// failed only at run time. The production executor hit the identical problem and got the
    /// identical fix first; this fixture now uses the same door rather than keeping a second,
    /// weaker path to the same session.
    /// </summary>
    internal static async Task<RoutedHttpAcquisitionSession> StartedSessionAsync(
        BoundMachineRequest sourceWitness,
        System.Net.Http.HttpMessageHandler handler,
        ICustodyStore custodyStore,
        TimeProvider timeProvider)
    {
        var started = await RoutedHttpAcquisitionSession.StartWithTestTransportAsync(
                sourceWitness, custodyStore, handler, timeProvider, CancellationToken.None)
            .ConfigureAwait(false);
        if (started.Kind != OfficialHttpAcquisitionOutcomeKind.ExecutedObservation || started.Session is null)
        {
            throw new AssertFailedException($"The fixture's own robots bootstrap did not start: {started.Kind}.");
        }

        return started.Session;
    }

    internal static (LuxembourgQueryPlan Plan, string PlanResourceId, SourceArtifactRef PlanRef) BuildInvariantPlan(
        int scopeSeed = 1)
    {
        var profile = OfficialMachineQuerySourceProfile.LuxembourgSparql();
        var scopeRef = Artifact(NewUrn(), Encoding.UTF8.GetBytes($"lu-fixture-scope-{scopeSeed}"));
        var plan = LuxembourgQueryPlan.CreateDefaultGraph(profile.ArtifactRef, scopeRef);
        var planResourceId = NewUrn();
        var planRef = LuxembourgQueryPlanIdentity.Create(planResourceId, plan);
        return (plan, planResourceId, planRef);
    }

    internal static MachineQueryRendererSource BuildRendererSource(int seed = 1)
    {
        var bytes = Encoding.UTF8.GetBytes($"lu-fixture-renderer-source-{seed}");
        var reference = Artifact(NewUrn(), bytes);
        return MachineQueryRendererSource.Open(reference, bytes);
    }

    internal static string CountJson(long count) =>
        "{\"head\":{\"link\":[],\"vars\":[\"count\"]},\"results\":{\"distinct\":false,\"ordered\":true," +
        "\"bindings\":[{\"count\":{\"type\":\"typed-literal\"," +
        "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\"," +
        $"\"value\":\"{count.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"}}}}]}}}}";

    /// <summary>One row per supplied six-part key tuple.</summary>
    internal static string RowsJson(params (string K1, string K2, string K3, string K4, string K5, string K6)[] rows)
    {
        var bindings = string.Join(',', rows.Select(static row =>
            "{" + string.Join(',', new[] { row.K1, row.K2, row.K3, row.K4, row.K5, row.K6 }
                .Select(static (value, index) =>
                    $"\"key_{index + 1}\":{{\"type\":\"literal\",\"value\":\"{Escape(value)}\"}}"))
            + "}"));
        return "{\"head\":{\"link\":[],\"vars\":[\"key_1\",\"key_2\",\"key_3\",\"key_4\",\"key_5\",\"key_6\"]}," +
               $"\"results\":{{\"distinct\":false,\"ordered\":true,\"bindings\":[{bindings}]}}}}";
    }

    /// <summary>One row per key_1 value; keys 2-6 are empty.</summary>
    internal static string RowsJson(params string[] key1Values) =>
        RowsJson(key1Values.Select(static value => (value, "", "", "", "", "")).ToArray());

    internal static string EmptyRowsJson() => RowsJson(Array.Empty<string>());

    /// <summary>
    /// A wide range that contains every single-lowercase-letter key_1 value the fixture's row
    /// builders use ("a", "b", "c", ...), so fixture rows never trip the partition-bound check.
    /// </summary>
    internal static LuxembourgQueryPartitionRange FullRange(string partitionId = "subjects-fixture") => new(
        partitionId,
        new LuxembourgQueryCursor("", "", "", "", "", ""),
        new LuxembourgQueryCursor("￿", "", "", "", "", ""));

    /// <summary>
    /// Runs a full count-and-pages pass for one <see cref="LuxembourgQueryPass"/>, using
    /// <paramref name="pageBodies"/> in order (the last must be an empty page under
    /// EmptySuccessorAfterShortPage), and folds the result into a <see cref="LuxembourgDeliveryPass"/>
    /// exactly as the executor's Observe routine would. Assumes robots has already been bootstrapped
    /// on <paramref name="session"/>.
    /// </summary>
    internal static async Task<LuxembourgDeliveryPass> RunPassAsync(
        RoutedHttpAcquisitionSession session,
        ICustodyStore custodyStore,
        LuxembourgQueryPlan invariantPlan,
        string invariantPlanResourceId,
        string setId,
        LuxembourgQueryPass pass,
        LuxembourgQueryPartitionRange partition,
        MachineQueryRendererSource rendererSource,
        RepeatedEnumerationInterpretationProfile profile,
        long selectedRowCount,
        IReadOnlyList<string> pageBodies)
    {
        var countBound = invariantPlan.BindCount(
            invariantPlanResourceId, NewUrn(), NewUrn(), setId, pass, partition, rendererSource);
        var countObservation = await ObserveAsync(session, custodyStore, countBound.Request)
            .ConfigureAwait(false);
        var identity = RepeatedEnumerationObservationIdentity.NewObservation();
        var countTransport = await BuildTransportAsync(session, custodyStore, countObservation)
            .ConfigureAwait(false);
        var countDelivery = LuxembourgDeliveryObservation.ForCount(countBound, identity, countTransport, profile);
        var deliveryPass = LuxembourgDeliveryPass.BeginWithCount(countDelivery, selectedRowCount);

        LuxembourgQueryCursor? cursor = null;
        foreach (var body in pageBodies)
        {
            var pageBound = invariantPlan.BindPage(
                invariantPlanResourceId, NewUrn(), NewUrn(), setId, pass, partition, cursor,
                selectedRowCount, countDelivery.HttpEvidenceRef, rendererSource);
            var pageAttempt = await ObserveAsync(session, custodyStore, pageBound.Request)
                .ConfigureAwait(false);
            var pageTransport = await BuildTransportAsync(session, custodyStore, pageAttempt)
                .ConfigureAwait(false);
            var pageDelivery = LuxembourgDeliveryObservation.ForPage(
                pageBound, RepeatedEnumerationObservationIdentity.NewObservation(), pageTransport, profile);
            deliveryPass = deliveryPass.WithPage(pageDelivery);
            // Parsed from the actually-retained response bytes, not the caller's declared body
            // string, so a handler/fixture mismatch surfaces as a wrong cursor rather than hiding.
            cursor = LastRowKey(Encoding.UTF8.GetString(pageAttempt.Body)) ?? cursor;
            _ = body;
        }

        return deliveryPass;
    }

    /// <summary>
    /// One genuine count observation: bound, sent, and folded into the transport an observation is
    /// built from. Used by the tests that then substitute one member of that transport to drive a
    /// binding guard, which is the only way those guards are reachable.
    /// </summary>
    internal static async Task<(LuxembourgBoundQueryCount Bound, RepeatedEnumerationObservedTransport Transport)>
        ObserveOneCountAsync(
            RoutedHttpAcquisitionSession session,
            ICustodyStore custodyStore,
            LuxembourgQueryPlan invariantPlan,
            string invariantPlanResourceId,
            string setId,
            LuxembourgQueryPartitionRange partition,
            MachineQueryRendererSource rendererSource)
    {
        var bound = invariantPlan.BindCount(
            invariantPlanResourceId, NewUrn(), NewUrn(), setId, LuxembourgQueryPass.Pass1, partition,
            rendererSource);
        var observed = await ObserveAsync(session, custodyStore, bound.Request).ConfigureAwait(false);
        var transport = await BuildTransportAsync(session, custodyStore, observed).ConfigureAwait(false);
        return (bound, transport);
    }

    private static async Task<(RoutedHttpEvidence Attempt, byte[] Body)> ObserveAsync(
        RoutedHttpAcquisitionSession session,
        ICustodyStore custodyStore,
        BoundMachineRequest request)
    {
        var item = session.OpenPlanItem(request);
        var attempt = await item.ExecuteNextAttemptAsync(CancellationToken.None).ConfigureAwait(false);
        if (attempt.Kind != OfficialHttpAcquisitionOutcomeKind.ExecutedObservation || attempt.Evidence is null)
        {
            throw new AssertFailedException(
                $"The fixture's own request was not executed: {attempt.Kind} " +
                $"reason={attempt.OperationalReason} preHeader={attempt.PreHeaderFailureClass} " +
                $"postHeader={attempt.PostHeaderRejection?.FailureClass}.");
        }

        if (attempt.Evidence.Hops.Count != 1 || attempt.Evidence.Hops[0].Status != 200)
        {
            throw new AssertFailedException("The fixture's own request was not a clean 200.");
        }

        var payload = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, attempt.Evidence.Hops[0].Sha256, CancellationToken.None)
            .ConfigureAwait(false);

        // Step 9.5 of the design's driving algorithm: the evidence document itself must be written
        // to custody. A later observation's expectedPartitionRowCountEvidenceRef names THIS
        // observation's HttpEvidenceRef, and the binder reopens that reference by digest when
        // binding the next request, so it must already be reopenable, not merely held in memory.
        await custodyStore.CreateAsync(
                attempt.Evidence.CopyCanonicalBytes(), CustodyClass.NightlyFloor90d, CancellationToken.None)
            .ConfigureAwait(false);

        return (attempt.Evidence, payload.ToArray());
    }

    private static async Task<RepeatedEnumerationObservedTransport> BuildTransportAsync(
        RoutedHttpAcquisitionSession session,
        ICustodyStore custodyStore,
        (RoutedHttpEvidence Attempt, byte[] Body) observed)
    {
        var terminal = observed.Attempt.Hops[0];
        var logicalRequestBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, terminal.LogicalRequestSha256, CancellationToken.None)
            .ConfigureAwait(false);
        var logicalRequest = HttpLogicalRequest.ParseAndVerify(logicalRequestBytes.Span);

        var writeReceiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                custodyStore, terminal.DurableWriteReceiptSha256, CancellationToken.None)
            .ConfigureAwait(false);
        var writeReceipt = ContractJson.Deserialize<DurableBlobWriteReceipt>(
            Encoding.UTF8.GetString(writeReceiptBytes.Span))
            ?? throw new AssertFailedException("The fixture's own write receipt failed to decode.");

        return new RepeatedEnumerationObservedTransport(logicalRequest, observed.Attempt, writeReceipt, observed.Body);
    }

    private static LuxembourgQueryCursor? LastRowKey(string body)
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var bindings = document.RootElement.GetProperty("results").GetProperty("bindings");
        var count = bindings.GetArrayLength();
        if (count == 0)
        {
            return null;
        }

        var last = bindings[count - 1];
        var parts = Enumerable.Range(1, 6)
            .Select(index => last.GetProperty($"key_{index}").GetProperty("value").GetString()!)
            .ToArray();
        return new LuxembourgQueryCursor(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";

    private static SourceArtifactRef Artifact(string resourceId, ReadOnlySpan<byte> bytes) =>
        new(resourceId, Convert.ToHexStringLower(SHA256.HashData(bytes)));

    /// <summary>Ordinal 0 answers robots Allow: /; every later ordinal is handed to <paramref name="respond"/>.</summary>
    internal static SequencedHandler AllowRobotsThenHandler(
        Func<int, System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> respond) =>
        new((ordinal, request) => ordinal == 0
            ? PlainTextResponse(request, System.Net.HttpStatusCode.OK, "User-agent: *\nAllow: /\n")
            : respond(ordinal, request));

    internal static System.Net.Http.HttpResponseMessage JsonResponse(
        System.Net.Http.HttpRequestMessage request, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new System.Net.Http.ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/sparql-results+json");
        content.Headers.TryAddWithoutValidation(
            "Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Version = System.Net.HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
    }

    private static System.Net.Http.HttpResponseMessage PlainTextResponse(
        System.Net.Http.HttpRequestMessage request, System.Net.HttpStatusCode status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new System.Net.Http.ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
        content.Headers.TryAddWithoutValidation(
            "Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new System.Net.Http.HttpResponseMessage(status)
        {
            Version = System.Net.HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
    }

    internal sealed class SequencedHandler(
        Func<int, System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> respond)
        : System.Net.Http.HttpMessageHandler
    {
        private int _sendCount;

        internal int SendCount => Volatile.Read(ref _sendCount);

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = Interlocked.Increment(ref _sendCount) - 1;
            return Task.FromResult(respond(ordinal, request));
        }
    }

    internal sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            Epoch.AddTicks(Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks));

        public override long GetTimestamp() =>
            Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks);
    }
}
