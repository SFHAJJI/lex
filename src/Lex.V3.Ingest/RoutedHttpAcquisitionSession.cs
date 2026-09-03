using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

[assembly: InternalsVisibleTo("Lex.V3.Ingest.Tests")]

namespace Lex.V3.Ingest;

internal enum OfficialMachineQueryLocalSafetyReason
{
    ApplicableRobotsGroupUninterpretable = 1,
    RobotsPolicyUnavailable = 2,
}

/// <summary>
/// One source-bound acquisition run. The run owns its robots generation, request ordinals,
/// application-attempt ordinals, network sender and custody lookup. Nothing outside this type can
/// provide a handler, clock, ordinal, timestamp, request message, route verdict or HTTP evidence.
/// </summary>
internal sealed class RoutedHttpAcquisitionSession : IDisposable
{
    private static readonly ConcurrentDictionary<string, OriginPacingState> PacingStates =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ActiveGenerationState> ActiveGenerations =
        new(StringComparer.Ordinal);

    private readonly HttpClient _client;
    private readonly ICustodyStore _custodyStore;
    private readonly TimeProvider _timeProvider;
    private readonly OfficialMachineQuerySourceProfile _profile;
    private readonly SourceArtifactRef _runIdentity;
    private readonly byte[] _runIdentityBytes;
    private readonly SourceArtifactRef _adapterExecutionIdentity;
    private readonly byte[] _adapterExecutionBytes;
    private readonly RequestPolicyArtifact _robotsRequestPolicy;
    private readonly RedirectPolicyArtifact _robotsRedirectPolicy;
    private readonly RedirectPolicyArtifact _noRedirectPolicy;
    private readonly ConcurrentDictionary<string, RequestPolicyArtifact> _requestPolicies =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RedirectPolicyArtifact> _redirectPolicies =
        new(StringComparer.Ordinal);
    private readonly DateTimeOffset _runCreatedAt;
    private readonly bool _usesPinnedHandler;
    private readonly BoundMachineRequestIdentity _sourceWitnessIdentity;
    private readonly ActiveGenerationState _activeGeneration;
    private readonly object _generationToken = new();
    private readonly object _generationLock = new();
    private readonly object _requestOrdinalLock = new();
    private readonly HashSet<BoundMachineRequest> _openedPlanItems =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SourceArtifactRef> _openedQueryPlanRefs = [];
    private readonly ConcurrentDictionary<HopCustodyKey, HeldBodyReceipt> _heldBodies = new();
    private readonly ConcurrentDictionary<HopCustodyKey, RoutedHttpHop> _retainedHops = new();
    private readonly ConcurrentDictionary<HopCustodyKey, byte> _openedAntecedents = new();
    private readonly object _durableArtifactLock = new();
    // Decision 71. Membership is what the receipt says, not that a digest was seen. A read proves
    // presence and digest and nothing about protection, so a read never admits anything here.
    private readonly Dictionary<string, CustodyMembership> _artifactMembership =
        new(StringComparer.Ordinal);
    private long? _robotsStartedTimestamp;
    private DateTimeOffset? _robotsStartedAt;
    private ulong _nextRequestOrdinal;
    private bool _requestOrdinalsExhausted;
    private bool _disposed;

    private RoutedHttpAcquisitionSession(
        BoundMachineRequest sourceWitness,
        ICustodyStore custodyStore,
        HttpMessageHandler handler,
        TimeProvider timeProvider,
        bool usesPinnedHandler)
    {
        ArgumentNullException.ThrowIfNull(sourceWitness);
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _sourceWitnessIdentity = MachineQueryBinder.OpenIdentity(sourceWitness);
        _profile = OfficialMachineQuerySourceProfiles.ResolveFor(_sourceWitnessIdentity);
        _nextRequestOrdinal = _profile.FirstProductRequestOrdinal;
        _custodyStore = custodyStore;
        _timeProvider = timeProvider;
        _usesPinnedHandler = usesPinnedHandler;
        _runCreatedAt = timeProvider.GetUtcNow();
        (_adapterExecutionIdentity, _adapterExecutionBytes) = CreateAdapterExecutionArtifact();
        _robotsRequestPolicy = RequestPolicyArtifact.ForRobots(
            _profile,
            _adapterExecutionIdentity,
            _adapterExecutionBytes);
        _robotsRedirectPolicy = RedirectPolicyArtifact.ForRobots(_profile);
        _noRedirectPolicy = RedirectPolicyArtifact.NoRedirect(_profile);
        RegisterRequestPolicy(_robotsRequestPolicy);
        RegisterRedirectPolicy(_robotsRedirectPolicy);
        RegisterRedirectPolicy(_noRedirectPolicy);
        (_runIdentity, _runIdentityBytes) = CreateRunIdentity(
            _profile,
            _runCreatedAt,
            _adapterExecutionIdentity);
        var generationKey = string.Join('\n',
            usesPinnedHandler
                ? "production"
                : $"test-{RuntimeHelpers.GetHashCode(timeProvider).ToString(CultureInfo.InvariantCulture)}",
            _profile.ArtifactRef.ResourceId,
            _profile.ArtifactRef.Sha256,
            _robotsRedirectPolicy.Sha256);
        _activeGeneration = ActiveGenerations.GetOrAdd(
            generationKey,
            static _ => new ActiveGenerationState());
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        ActivateGeneration();
    }

    internal SourceArtifactRef RunIdentity => _runIdentity;

    internal OfficialMachineQuerySourceProfile SourceProfile => _profile;

    internal static Task<StartResult> StartAsync(
        BoundMachineRequest sourceWitness,
        ICustodyStore custodyStore,
        CancellationToken cancellationToken)
    {
        var session = new RoutedHttpAcquisitionSession(
            sourceWitness,
            custodyStore,
            CreatePinnedHandler(),
            TimeProvider.System,
            usesPinnedHandler: true);
        return session.BootstrapRobotsAsync(cancellationToken);
    }

    internal IPlanItem OpenPlanItem(BoundMachineRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        var identity = MachineQueryBinder.OpenIdentity(request);
        var resolved = OfficialMachineQuerySourceProfiles.ResolveFor(identity);
        if (resolved.ArtifactRef != _profile.ArtifactRef)
        {
            throw new ArgumentException(
                "An acquisition run cannot cross its source profile or robots generation.",
                nameof(request));
        }

        ulong ordinal;
        lock (_requestOrdinalLock)
        {
            if (_requestOrdinalsExhausted)
            {
                throw new InvalidOperationException("The acquisition run exhausted request ordinals.");
            }

            if (!_openedPlanItems.Add(request))
            {
                throw new InvalidOperationException(
                    "One bound acquisition-plan item can be opened only once in a run; retries belong to that item.");
            }

            if (!_openedQueryPlanRefs.Add(identity.QueryPlanRef))
            {
                _openedPlanItems.Remove(request);
                throw new InvalidOperationException(
                    "One exact acquisition-plan item can be opened only once in a run; retries belong to that item.");
            }

            ordinal = _nextRequestOrdinal;
            if (_nextRequestOrdinal == ulong.MaxValue)
            {
                _requestOrdinalsExhausted = true;
            }
            else
            {
                _nextRequestOrdinal++;
            }
        }

        return new PlanItem(this, request, identity.QueryPlanRef, ordinal);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_activeGeneration.Gate)
        {
            if (ReferenceEquals(_activeGeneration.Token, _generationToken))
            {
                _activeGeneration.Token = null;
            }
        }

        _client.Dispose();
    }

    private static SocketsHttpHandler CreatePinnedHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        ActivityHeadersPropagator = null,
        MaxResponseDrainSize = 0,
        UseCookies = false,
        UseProxy = false,
    };

    private async Task<StartResult> BootstrapRobotsAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = CreateRobotsRequest(_profile.RobotsRoute.Steps[0].RequestedUri);
            var route = await ExecuteRouteAsync(
                request,
                requestBody: ReadOnlyMemory<byte>.Empty,
                requestOrdinal: _profile.RobotsRequestOrdinal,
                attemptOrdinal: 0,
                _profile.RobotsRoute,
                enforceGenerationAge: false,
                cancellationToken).ConfigureAwait(false);

            if (route.PreHeaderFailure is not null)
            {
                Dispose();
                return StartResult.Operational(
                    OfficialHttpOperationalFailureReason.NetworkFailure,
                    evidence: null);
            }

            if (route.PostHeaderFailure is not null)
            {
                // The publisher answered and its bytes are in custody; only /4 cannot encode the
                // response. Reporting that as a pre-header transport loss would erase the
                // distinction a later auditor needs, so the typed rejection travels on the result.
                Dispose();
                return StartResult.PostHeaderRejected(route.PostHeaderFailure.ToRejection());
            }

            var evidence = route.Evidence
                ?? throw new InvalidOperationException("A response-bearing robots route lost its evidence.");
            var terminalBody = route.TerminalCustodyKey is null
                ? null
                : await ResolveHeldBodyAsync(route.TerminalCustodyKey).ConfigureAwait(false);
            var terminal = evidence.Hops[^1];
            if (evidence.Outcome is IncompleteHttpRouteOutcome
                {
                    Reason: HttpRouteIncompleteReason.PublisherServerFailure,
                })
            {
                // RFC 9309 2.3.1.4 requires complete disallow while robots is unreachable. The
                // run therefore never starts, but a transient server failure does not stale the
                // frozen route or become a publisher-configuration claim.
                Dispose();
                return StartResult.Operational(
                    OfficialHttpOperationalFailureReason.PublisherServerFailure,
                    evidence);
            }

            if (evidence.Outcome is IncompleteHttpRouteOutcome incomplete &&
                incomplete.Reason == HttpRouteIncompleteReason.SourceProfileStale)
            {
                Dispose();
                return StartResult.Operational(
                    OfficialHttpOperationalFailureReason.SourceProfileStale,
                    evidence);
            }

            if (evidence.Outcome is RedirectTargetUnobservedHttpRouteOutcome)
            {
                Dispose();
                return StartResult.Operational(
                    OfficialHttpOperationalFailureReason.NetworkFailure,
                    evidence);
            }

            if (evidence.Outcome is not CompleteHttpRouteOutcome || terminalBody is null)
            {
                Dispose();
                return StartResult.Refused(
                    OfficialMachineQueryLocalSafetyReason.RobotsPolicyUnavailable,
                    evidence);
            }

            if (terminal.Status != 200 ||
                terminal.StatusDisposition != HttpStatusDisposition.DerivableStatus ||
                terminal.Completion is not (DeclaredContentLengthHttpCompletion or
                    PinnedHandlerChunkedEofHttpCompletion) ||
                !IsAdmittedRobotsRepresentation(terminal.Headers))
            {
                Dispose();
                return StartResult.Refused(
                    OfficialMachineQueryLocalSafetyReason.RobotsPolicyUnavailable,
                    evidence);
            }

            RobotsPolicyEvaluationResult verdict;
            try
            {
                verdict = RobotsExclusionPolicy.Evaluate(
                    terminalBody.Bytes.Span,
                    _profile.RobotsProductToken,
                    new Uri(_sourceWitnessIdentity.RequestedUri, UriKind.Absolute).PathAndQuery);
            }
            catch (ArgumentException)
            {
                Dispose();
                return StartResult.Refused(
                    OfficialMachineQueryLocalSafetyReason.ApplicableRobotsGroupUninterpretable,
                    evidence);
            }

            if (verdict == RobotsPolicyEvaluationResult.Denied)
            {
                Dispose();
                return StartResult.PublisherDenied(evidence);
            }

            if (verdict == RobotsPolicyEvaluationResult.UnsafeToInterpret)
            {
                Dispose();
                return StartResult.Refused(
                    OfficialMachineQueryLocalSafetyReason.ApplicableRobotsGroupUninterpretable,
                    evidence);
            }

            EnsureGenerationCurrent();
            return StartResult.Started(this, evidence);
        }
        catch (RobotsPolicyExpiredException)
        {
            Dispose();
            return StartResult.Operational(
                OfficialHttpOperationalFailureReason.RobotsPolicyExpired,
                evidence: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Dispose();
            throw;
        }
        catch (CustodyRequiredException)
        {
            Dispose();
            return StartResult.Operational(
                OfficialHttpOperationalFailureReason.CustodyUnavailable,
                evidence: null);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyPolicyException)
        {
            Dispose();
            return StartResult.Integrity(evidence: null);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private async Task<RouteExecution> ExecuteMachineAttemptAsync(
        BoundMachineRequest boundRequest,
        SourceArtifactRef expectedQueryPlanRef,
        ulong requestOrdinal,
        ulong attemptOrdinal,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureGenerationCurrent();
        var artifactResolver = new SessionMachineArtifactResolver(this);
        var opened = await MachineQueryBinder.OpenForSendAsync(
                boundRequest,
                artifactResolver,
            cancellationToken)
            .ConfigureAwait(false);
        if (opened.QueryPlanRef != expectedQueryPlanRef)
        {
            throw new InvalidOperationException(
                "A plan item no longer reopens as the exact query plan admitted by this run.");
        }

        var resolved = new ResolvedMachineRequest(
            opened,
            artifactResolver.CopyResolvedArtifacts());
        var request = CreateMachineRequest(resolved);
        return await ExecuteRouteAsync(
            request,
            opened.CopyRequestBody(),
            requestOrdinal,
            attemptOrdinal,
            robotsRoute: null,
            enforceGenerationAge: true,
            cancellationToken).ConfigureAwait(false);
    }

    private HttpLogicalRequest CreateRobotsRequest(string uri) => HttpLogicalRequest.Create(
        uri,
        HttpRequestMethod.Get,
        [new HttpLogicalRequestHeader("user-agent", _profile.CrawlerUserAgent)],
        new HttpLogicalRequestBody(0, Hash(Array.Empty<byte>().AsSpan())),
        _robotsRequestPolicy.Sha256,
        _robotsRedirectPolicy.Sha256);

    private HttpLogicalRequest CreateMachineRequest(ResolvedMachineRequest resolvedRequest)
    {
        var openedRequest = resolvedRequest.Request;
        var profile = OfficialMachineQuerySourceProfiles.ResolveFor(openedRequest);
        if (profile.ArtifactRef != _profile.ArtifactRef)
        {
            throw new ArgumentException(
                "The bound request belongs to another official source profile.",
                nameof(openedRequest));
        }

        var body = openedRequest.CopyRequestBody();
        var contentType = profile.RequestCharset == MachineQueryCharset.Utf8
            ? $"{profile.RequestContentType}; charset=utf-8"
            : profile.RequestContentType;
        var headers = new[]
        {
            new HttpLogicalRequestHeader("user-agent", profile.CrawlerUserAgent),
            new HttpLogicalRequestHeader("accept", profile.Accept),
            new HttpLogicalRequestHeader("content-type", contentType),
        };
        var requestPolicy = RequestPolicyArtifact.ForMachineQuery(
            profile,
            _adapterExecutionIdentity,
            _adapterExecutionBytes,
            openedRequest,
            resolvedRequest.Artifacts,
            headers,
            body);
        RegisterRequestPolicy(requestPolicy);
        return HttpLogicalRequest.Create(
            openedRequest.RequestedUri,
            profile.Method,
            headers,
            new HttpLogicalRequestBody(checked((ulong)body.LongLength), Hash(body)),
            requestPolicy.Sha256,
            _noRedirectPolicy.Sha256);
    }

    private void ActivateGeneration()
    {
        lock (_activeGeneration.Gate)
        {
            _activeGeneration.Token = _generationToken;
        }
    }

    private void EnsureGenerationCurrent()
    {
        lock (_activeGeneration.Gate)
        {
            EnsureGenerationCurrentAt(
                _timeProvider.GetUtcNow(),
                _timeProvider.GetTimestamp());
        }
    }

    private void EnsureGenerationCurrentAt(DateTimeOffset now, long nowTimestamp)
    {
        EnsureGenerationActive();

        DateTimeOffset observedAt;
        long observedTimestamp;
        lock (_generationLock)
        {
            observedAt = _robotsStartedAt
                ?? throw new InvalidOperationException("The robots generation has not started.");
            observedTimestamp = _robotsStartedTimestamp
                ?? throw new InvalidOperationException("The robots generation has no monotonic anchor.");
        }

        var utcAge = now - observedAt;
        var monotonicAge = _timeProvider.GetElapsedTime(
            observedTimestamp,
            nowTimestamp);
        if (utcAge < TimeSpan.Zero ||
            utcAge >= _profile.MaximumRobotsPolicyAge ||
            monotonicAge >= _profile.MaximumRobotsPolicyAge)
        {
            throw new RobotsPolicyExpiredException();
        }
    }

    private void EnsureGenerationActive()
    {
        if (!ReferenceEquals(_activeGeneration.Token, _generationToken))
        {
            throw new InvalidOperationException(
                "This acquisition run was superseded before it could send.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static bool IsAdmittedRobotsRepresentation(RoutedHttpResponseHeaders headers)
    {
        if (headers.ContentEncoding is not RoutedHttpAbsentHeader ||
            headers.ContentType is not RoutedHttpSingleHeader contentType ||
            !System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType.Value, out var parsed) ||
            !string.Equals(parsed.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var charset = parsed.CharSet;
        return charset is null || string.Equals(
            charset.Trim('"'),
            "utf-8",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateRequestBody(
        HttpLogicalRequest request,
        ReadOnlyMemory<byte> requestBody)
    {
        if (request.Body.Length != checked((ulong)requestBody.Length) ||
            !string.Equals(request.Body.Sha256, Hash(requestBody.Span), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The send capability's exact request bytes no longer match its logical request.");
        }
    }

    private static RawResponseHeaders SnapshotHeaders(HttpResponseMessage response) => new(
        HeaderValues(response, "Content-Type"),
        HeaderValues(response, "Content-Length"),
        HeaderValues(response, "Content-Encoding"),
        HeaderValues(response, "Transfer-Encoding"),
        HeaderValues(response, "Content-Range"),
        HeaderValues(response, "ETag"),
        HeaderValues(response, "Last-Modified"),
        HeaderValues(response, "Location"),
        HeaderValues(response, "Cache-Control"),
        HeaderValues(response, "Expires"),
        HeaderValues(response, "Date"),
        HeaderValues(response, "Age"),
        HeaderValues(response, "TCN"));

    private static RoutedHttpResponseHeaders ProjectHeaders(RawResponseHeaders raw) => new(
        contentType: ToHeaderField(raw.ContentType),
        contentLength: ToHeaderField(raw.ContentLength),
        contentEncoding: ToHeaderField(raw.ContentEncoding),
        transferEncoding: ToHeaderField(raw.TransferEncoding),
        contentRange: ToHeaderField(raw.ContentRange),
        etag: ToHeaderField(raw.ETag),
        lastModified: ToHeaderField(raw.LastModified),
        location: ToHeaderField(raw.Location),
        cacheControl: ToHeaderField(raw.CacheControl),
        expires: ToHeaderField(raw.Expires),
        date: ToHeaderField(raw.Date),
        age: ToHeaderField(raw.Age),
        tcn: ToHeaderField(raw.Tcn));

    private static IReadOnlyList<string> HeaderValues(
        HttpResponseMessage response,
        string name)
    {
        var values = new List<string>();
        if (response.Headers.NonValidated.TryGetValues(name, out var responseValues))
        {
            values.AddRange(responseValues);
        }

        if (response.Content.Headers.NonValidated.TryGetValues(name, out var contentValues))
        {
            values.AddRange(contentValues);
        }

        return values;
    }

    private static RoutedHttpHeaderField ToHeaderField(IReadOnlyList<string> values) =>
        values.Count switch
        {
            0 => new RoutedHttpAbsentHeader(),
            1 => new RoutedHttpSingleHeader(values[0]),
            _ => new RoutedHttpMultipleHeader(values),
        };

    private static bool TryGetDeclaredContentLength(
        RoutedHttpHeaderField field,
        out ulong length)
    {
        length = 0;
        if (field is not RoutedHttpSingleHeader single)
        {
            return false;
        }

        var value = single.Value.AsSpan();
        return value.Length > 0 &&
               value.IndexOfAnyExceptInRange('0', '9') < 0 &&
               ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out length);
    }

    private static bool TryGetRawDeclaredContentLength(
        IReadOnlyList<string> values,
        out ulong length)
    {
        length = 0;
        if (values.Count != 1)
        {
            return false;
        }

        var value = values[0].AsSpan();
        return value.Length > 0 &&
               value.IndexOfAnyExceptInRange('0', '9') < 0 &&
               ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out length);
    }

    private async Task<BodyCapture> CaptureBodyAsync(
        HttpContent content,
        ulong? declaredLength,
        CancellationToken callerCancellationToken,
        long maximumRetainedBytes)
    {
        const int bufferSize = 64 * 1024;
        if (maximumRetainedBytes is < 2 or > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedBytes));
        }

        var maximumRetained = checked((ulong)maximumRetainedBytes);
        var completeCeiling = maximumRetained - 1;
        var targetLength = declaredLength.HasValue && declaredLength.Value <= completeCeiling
            ? declaredLength
            : null;
        if (callerCancellationToken.IsCancellationRequested)
        {
            return new BodyCapture(
                ReadOnlyMemory<byte>.Empty,
                BodyCaptureEvent.CallerCancelledAfterHeaders);
        }

        if (targetLength == 0)
        {
            return new BodyCapture(ReadOnlyMemory<byte>.Empty, BodyCaptureEvent.DeclaredLengthReached);
        }

        var started = _timeProvider.GetTimestamp();
        using var transportCancellation = new CancellationTokenSource();
        Task<Stream>? openTask = null;
        Stream stream;
        try
        {
            openTask = content.ReadAsStreamAsync(transportCancellation.Token);
            stream = await WaitForBodyAsync(
                openTask,
                started,
                transportCancellation,
                callerCancellationToken).ConfigureAwait(false);
        }
        catch (BodyDeadlineException)
        {
            if (openTask is not null)
            {
                ObserveLate(openTask);
            }

            return new BodyCapture(ReadOnlyMemory<byte>.Empty, BodyCaptureEvent.BodyDeadline);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            if (openTask is not null)
            {
                ObserveLate(openTask);
            }

            return new BodyCapture(
                ReadOnlyMemory<byte>.Empty,
                BodyCaptureEvent.CallerCancelledAfterHeaders);
        }
        catch (HttpRequestException exception)
        {
            return new BodyCapture(
                ReadOnlyMemory<byte>.Empty,
                exception.HttpRequestError == HttpRequestError.ResponseEnded
                    ? BodyCaptureEvent.ResponseEnded
                    : BodyCaptureEvent.BodyReadFailure);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return new BodyCapture(ReadOnlyMemory<byte>.Empty, BodyCaptureEvent.BodyReadFailure);
        }

        using (stream)
        using (var destination = new MemoryStream())
        {
            var buffer = new byte[bufferSize];
            while (true)
            {
                if (destination.Length == maximumRetainedBytes)
                {
                    return new BodyCapture(destination.ToArray(), BodyCaptureEvent.CapSentinel);
                }

                if (callerCancellationToken.IsCancellationRequested)
                {
                    return new BodyCapture(
                        destination.ToArray(),
                        BodyCaptureEvent.CallerCancelledAfterHeaders);
                }

                if (targetLength.HasValue && checked((ulong)destination.Length) == targetLength.Value)
                {
                    return new BodyCapture(
                        destination.ToArray(),
                        BodyCaptureEvent.DeclaredLengthReached);
                }

                var remainingCapacity = maximumRetainedBytes - destination.Length;
                var requested = (int)Math.Min(buffer.Length, remainingCapacity);
                if (targetLength.HasValue)
                {
                    requested = (int)Math.Min(
                        requested,
                        checked((long)(targetLength.Value - checked((ulong)destination.Length))));
                }

                Task<int>? readTask = null;
                int read;
                try
                {
                    readTask = stream.ReadAsync(
                        buffer.AsMemory(0, requested),
                        transportCancellation.Token).AsTask();
                    read = await WaitForBodyAsync(
                        readTask,
                        started,
                        transportCancellation,
                        callerCancellationToken).ConfigureAwait(false);
                }
                catch (BodyDeadlineException)
                {
                    if (readTask is not null)
                    {
                        ObserveLate(readTask);
                    }

                    return new BodyCapture(destination.ToArray(), BodyCaptureEvent.BodyDeadline);
                }
                catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
                {
                    if (readTask is not null)
                    {
                        ObserveLate(readTask);
                    }

                    return new BodyCapture(
                        destination.ToArray(),
                        BodyCaptureEvent.CallerCancelledAfterHeaders);
                }
                catch (HttpRequestException exception)
                {
                    return new BodyCapture(
                        destination.ToArray(),
                        exception.HttpRequestError == HttpRequestError.ResponseEnded
                            ? BodyCaptureEvent.ResponseEnded
                            : BodyCaptureEvent.BodyReadFailure);
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException)
                {
                    return new BodyCapture(destination.ToArray(), BodyCaptureEvent.BodyReadFailure);
                }

                if (read == 0)
                {
                    return new BodyCapture(destination.ToArray(), BodyCaptureEvent.CleanEof);
                }

                destination.Write(buffer, 0, read);
            }
        }
    }

    private async Task<T> WaitForBodyAsync<T>(
        Task<T> task,
        long startedTimestamp,
        CancellationTokenSource transportCancellation,
        CancellationToken callerCancellationToken)
    {
        var elapsed = _timeProvider.GetElapsedTime(startedTimestamp, _timeProvider.GetTimestamp());
        var remaining = _profile.RequestTimeout - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            await transportCancellation.CancelAsync().ConfigureAwait(false);
            throw new BodyDeadlineException();
        }

        try
        {
            return await task.WaitAsync(
                remaining,
                _timeProvider,
                callerCancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            await transportCancellation.CancelAsync().ConfigureAwait(false);
            throw new BodyDeadlineException(exception);
        }
    }

    private RoutedHttpCompletion ClassifyCompletion(
        HttpLogicalRequest request,
        int status,
        RoutedHttpResponseHeaders headers,
        BodyCapture capture)
    {
        if (capture.Event == BodyCaptureEvent.CapSentinel)
        {
            return Incomplete(HttpPartialBodyReason.ByteBoundPreventedCompletion);
        }

        if (capture.Event == BodyCaptureEvent.CallerCancelledAfterHeaders)
        {
            return Incomplete(HttpPartialBodyReason.CallerCancelledAfterHeaders);
        }

        if (capture.Event == BodyCaptureEvent.BodyDeadline)
        {
            return Incomplete(HttpPartialBodyReason.BodyDeadline);
        }

        if (status == 304)
        {
            // A 304's framing fields describe the hypothetical selected response. They can never
            // turn an observed stream failure into a declared-length comparison against the empty
            // revalidation response.
            if (capture.Event is BodyCaptureEvent.ResponseEnded or BodyCaptureEvent.BodyReadFailure)
            {
                return Incomplete(HttpPartialBodyReason.BodyReadFailure);
            }

            if (!IsAdmittedConditionalGet(request))
            {
                return Incomplete(HttpResponseSemanticsReason.RevalidationRequestNotAdmitted);
            }

            return capture.Bytes.Length == 0
                ? new Revalidation304HttpCompletion()
                : Incomplete(HttpResponseSemanticsReason.StatusContentForbidden);
        }

        var hasContentLength = headers.ContentLength is not RoutedHttpAbsentHeader;
        var hasTransferEncoding = headers.TransferEncoding is not RoutedHttpAbsentHeader;
        var hasValidLength = TryGetDeclaredContentLength(headers.ContentLength, out var declaredLength);
        if (capture.Event == BodyCaptureEvent.ResponseEnded)
        {
            // RFC 9112 section 6.1: a Content-Length beside a Transfer-Encoding has no standing,
            // so a stream that ended early cannot be measured against it. The validator refuses
            // declared_length_short_read under a Transfer-Encoding for the same reason; the
            // honest fact is that the read failed.
            return hasValidLength && !hasTransferEncoding &&
                   checked((ulong)capture.Bytes.Length) < declaredLength
                ? Incomplete(HttpPartialBodyReason.DeclaredLengthShortRead)
                : Incomplete(HttpPartialBodyReason.BodyReadFailure);
        }

        if (capture.Event == BodyCaptureEvent.BodyReadFailure)
        {
            return Incomplete(HttpPartialBodyReason.BodyReadFailure);
        }

        if (status == 204)
        {
            if (hasContentLength || hasTransferEncoding)
            {
                return Incomplete(HttpResponseSemanticsReason.StatusFramingConflict);
            }

            return capture.Bytes.Length == 0
                ? new ResponseWithoutBodyHttpCompletion()
                : Incomplete(HttpResponseSemanticsReason.StatusContentForbidden);
        }

        if (hasContentLength && !hasValidLength)
        {
            return Incomplete(HttpCompletionUnprovenReason.InvalidContentLength);
        }

        if (hasContentLength && hasTransferEncoding)
        {
            return Incomplete(HttpCompletionUnprovenReason.TransferCodingConflict);
        }

        var hasAdmittedChunked = IsAdmittedChunked(headers.TransferEncoding);
        if (hasTransferEncoding && !hasAdmittedChunked)
        {
            return Incomplete(HttpCompletionUnprovenReason.UnsupportedTransferCoding);
        }

        var retainedLength = checked((ulong)capture.Bytes.Length);
        if (hasValidLength)
        {
            if (retainedLength < declaredLength)
            {
                return Incomplete(HttpPartialBodyReason.DeclaredLengthShortRead);
            }

            if (retainedLength != declaredLength || declaredLength >= checked((ulong)CustodyBounds.MaxObjectBytes))
            {
                return Incomplete(HttpCompletionUnprovenReason.InvalidContentLength);
            }

            if (status == 205 && retainedLength > 0)
            {
                return Incomplete(HttpResponseSemanticsReason.StatusContentForbidden);
            }

            return new DeclaredContentLengthHttpCompletion(declaredLength);
        }

        if (hasAdmittedChunked)
        {
            if (!_usesPinnedHandler)
            {
                throw new InvalidOperationException(
                    "Only the exact pinned production handler can warrant chunked application-stream EOF.");
            }

            if (capture.Event != BodyCaptureEvent.CleanEof)
            {
                return Incomplete(HttpPartialBodyReason.BodyReadFailure);
            }

            if (status == 205 && retainedLength > 0)
            {
                return Incomplete(HttpResponseSemanticsReason.StatusContentForbidden);
            }

            return new PinnedHandlerChunkedEofHttpCompletion(_adapterExecutionIdentity.Sha256);
        }

        return Incomplete(HttpCompletionUnprovenReason.MissingCompletionProof);
    }

    private static bool IsAdmittedConditionalGet(HttpLogicalRequest request)
    {
        if (request.Method != HttpRequestMethod.Get)
        {
            return false;
        }

        var count = request.Headers.Count(static header =>
            string.Equals(header.Name, "if-none-match", StringComparison.Ordinal) ||
            string.Equals(header.Name, "if-modified-since", StringComparison.Ordinal));
        return count == 1;
    }

    private static bool IsAdmittedChunked(RoutedHttpHeaderField field) =>
        field is RoutedHttpSingleHeader single &&
        string.Equals(single.Value.Trim(' ', '\t'), "chunked", StringComparison.OrdinalIgnoreCase);

    private static IncompleteHttpCompletion Incomplete(HttpPartialBodyReason reason) =>
        new(HttpAcquisitionReasonRegistry.Member(reason));

    private static IncompleteHttpCompletion Incomplete(HttpCompletionUnprovenReason reason) =>
        new(HttpAcquisitionReasonRegistry.Member(reason));

    private static IncompleteHttpCompletion Incomplete(HttpResponseSemanticsReason reason) =>
        new(HttpAcquisitionReasonRegistry.Member(reason));

    private async Task<HeldBodyReceipt> HoldAsync(ReadOnlyMemory<byte> bytes, HeldCausalFacts causal)
    {
        var frozen = new ReadOnlyMemory<byte>(bytes.ToArray());
        using var deadline = new CancellationTokenSource(_profile.RequestTimeout, _timeProvider);
        var expectedSha256 = CustodyDigest.Of(frozen.Span, deadline.Token);
        DurableBlobWriteReceipt receipt;
        try
        {
            var create = _custodyStore.CreateAsync(
                frozen,
                CustodyClass.NightlyFloor90d,
                deadline.Token);
            receipt = await create.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
        {
            throw new CustodyRequiredException(
                "HTTP evidence custody did not complete before its evidence deadline.",
                exception);
        }
        catch (Exception exception) when (exception is not (
            CustodyRequiredException or CustodyIntegrityException or CustodyPolicyException))
        {
            throw new CustodyRequiredException("HTTP evidence bytes were not held.", exception);
        }

        if (receipt is null ||
            receipt.Reference.CustodyClass != CustodyClass.NightlyFloor90d ||
            receipt.Reference.ByteLength != frozen.Length ||
            !string.Equals(receipt.Reference.ContentSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new CustodyIntegrityException(
                "The HTTP evidence receipt does not bind the exact retained bytes.");
        }

        var receiptBytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(receipt));
        var receiptSha256 = Hash(receiptBytes);
        try
        {
            await RetainArtifactAsync(receiptBytes, receiptSha256, deadline.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
        {
            throw new CustodyRequiredException(
                "The durable HTTP write receipt did not become reopenable before its evidence deadline.",
                exception);
        }

        return new HeldBodyReceipt(receipt, receiptSha256, causal);
    }

    private async Task<ReadOnlyMemory<byte>> RetainArtifactAsync(
        ReadOnlyMemory<byte> canonicalBytes,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!CustodyDigest.IsLowercaseSha256(expectedSha256))
        {
            throw new ArgumentException(
                "A durable artifact requires one lowercase SHA-256.",
                nameof(expectedSha256));
        }

        var frozen = new ReadOnlyMemory<byte>(canonicalBytes.ToArray());
        if (!string.Equals(Hash(frozen.Span), expectedSha256, StringComparison.Ordinal))
        {
            throw new CustodyIntegrityException(
                "The artifact bytes do not carry the digest that will name them.");
        }

        bool alreadyRetained;
        lock (_durableArtifactLock)
        {
            alreadyRetained = _artifactMembership.ContainsKey(expectedSha256);
        }

        if (alreadyRetained)
        {
            var reopenedExisting = await CustodyRestore.ReadByDigestCheckedAsync(
                    _custodyStore,
                    expectedSha256,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!reopenedExisting.Span.SequenceEqual(frozen.Span))
            {
                throw new CustodyIntegrityException(
                    "A previously retained artifact no longer contains its exact bytes.");
            }

            return reopenedExisting;
        }

        ReadOnlyMemory<byte> reopened;
        DurableBlobWriteReceipt createdReceipt;
        try
        {
            var receipt = await _custodyStore.CreateAsync(
                    frozen,
                    CustodyClass.NightlyFloor90d,
                    cancellationToken)
                .ConfigureAwait(false);
            if (receipt is null ||
                receipt.Reference.CustodyClass != CustodyClass.NightlyFloor90d ||
                receipt.Reference.ByteLength != frozen.Length ||
                !string.Equals(
                    receipt.Reference.ContentSha256,
                    expectedSha256,
                    StringComparison.Ordinal))
            {
                throw new CustodyIntegrityException(
                    "The artifact receipt does not bind the bytes that its digest names.");
            }

            createdReceipt = receipt;

            reopened = await CustodyRestore.ReadByDigestCheckedAsync(
                    _custodyStore,
                    expectedSha256,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!reopened.Span.SequenceEqual(frozen.Span))
            {
                throw new CustodyIntegrityException(
                    "Content-addressed reopening returned different artifact bytes.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is not (CustodyRequiredException
                or CustodyIntegrityException
                or CustodyPolicyException
                or ArgumentException))
        {
            throw new CustodyRequiredException(
                "The referenced artifact was not durably reopenable before the network send.",
                exception);
        }

        lock (_durableArtifactLock)
        {
            _artifactMembership[expectedSha256] = ClassifyMembership(createdReceipt);
        }

        return reopened;
    }

    private async Task<RetainedSendArtifacts> RetainSendArtifactsAsync(
        SendLease lease,
        CancellationToken cancellationToken)
    {
        if (!lease.BindsSession(this))
        {
            throw new InvalidOperationException(
                "Artifact retention must be requested by the exact live send capability.");
        }

        var request = lease.Request;
        var body = lease.Body;
        OpenAndValidatePolicies(request, body);
        var requestPolicy = _requestPolicies[request.RequestPolicySha256];
        var redirectPolicy = _redirectPolicies[request.RedirectPolicySha256];
        var logicalRequestBytes = request.CopyCanonicalBytes();
        var logicalRequestSha256 = Hash(logicalRequestBytes);
        var sourceProfileBytes = _profile.CopyCanonicalBytes();
        var reasonRegistryBytes = HttpAcquisitionReasonRegistry.CanonicalBytes.ToArray();
        _ = HttpLogicalRequest.ParseAndVerify(logicalRequestBytes);

        await RetainArtifactAsync(
                sourceProfileBytes,
                _profile.ArtifactRef.Sha256,
                cancellationToken)
            .ConfigureAwait(false);
        await RetainArtifactAsync(
                _runIdentityBytes,
                _runIdentity.Sha256,
                cancellationToken)
            .ConfigureAwait(false);
        await RetainArtifactAsync(
                reasonRegistryBytes,
                HttpAcquisitionReasonRegistry.Sha256,
                cancellationToken)
            .ConfigureAwait(false);
        await RetainArtifactAsync(
                _adapterExecutionBytes,
                _adapterExecutionIdentity.Sha256,
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var artifact in requestPolicy.BinderArtifacts)
        {
            await RetainArtifactAsync(
                    artifact.CopyCanonicalBytes(),
                    artifact.Reference.Sha256,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await RetainArtifactAsync(
                requestPolicy.CopyCanonicalBytes(),
                request.RequestPolicySha256,
                cancellationToken)
            .ConfigureAwait(false);
        await RetainArtifactAsync(
                redirectPolicy.CopyCanonicalBytes(),
                request.RedirectPolicySha256,
                cancellationToken)
            .ConfigureAwait(false);
        await RetainArtifactAsync(
                logicalRequestBytes,
                logicalRequestSha256,
                cancellationToken)
            .ConfigureAwait(false);

        // The outbound body. The request policy names it as body={length}	{digest}, so a reader
        // holding the retained policy can see what was sent and could not open it: the bytes
        // themselves were the one send dependency never retained. An empty body has nothing to
        // retain and its absence is not a gap.
        if (!body.IsEmpty)
        {
            await RetainArtifactAsync(body, Hash(body.Span), cancellationToken)
                .ConfigureAwait(false);
        }

        var closureSha256 = ComputeSendClosureSha256(
            request,
            requestPolicy,
            logicalRequestSha256);
        return new RetainedSendArtifacts(lease, logicalRequestSha256, closureSha256);
    }

    /// <summary>
    /// What a receipt establishes about where the bytes are held. Decision 71: the floor is a
    /// property of the store observed after write and readback, so only a receipt can say it.
    /// Keyed on the observed protection rather than the profile that implies it today: the policy
    /// evidence constructor already proved the class floor for every protection except
    /// NotEnforced, so a profile added later cannot misclassify through this switch.
    /// </summary>
    private static CustodyMembership ClassifyMembership(DurableBlobWriteReceipt receipt) =>
        receipt.PolicyEvidence.Protection == CustodyProtection.NotEnforced
            ? CustodyMembership.RetainedUnenforced
            : CustodyMembership.Floored;

    /// <summary>
    /// What this run can say about each closure member. Never derived from the digest alone.
    /// </summary>
    internal IReadOnlyDictionary<string, CustodyMembership> CopyArtifactMembership()
    {
        lock (_durableArtifactLock)
        {
            return new Dictionary<string, CustodyMembership>(_artifactMembership, StringComparer.Ordinal);
        }
    }

    private string ComputeSendClosureSha256(
        HttpLogicalRequest request,
        RequestPolicyArtifact requestPolicy,
        string logicalRequestSha256)
    {
        var digests = new List<string>
        {
            _profile.ArtifactRef.Sha256,
            _runIdentity.Sha256,
            HttpAcquisitionReasonRegistry.Sha256,
            _adapterExecutionIdentity.Sha256,
        };
        digests.AddRange(requestPolicy.BinderArtifacts.Select(static artifact => artifact.Reference.Sha256));
        digests.Add(request.RequestPolicySha256);
        digests.Add(request.RedirectPolicySha256);
        digests.Add(logicalRequestSha256);

        lock (_durableArtifactLock)
        {
            // Retention is required; the floor is reported. A dependency this run never wrote
            // has no membership at all and fails here. One that was written under a store that
            // enforces nothing is retained-unenforced, which the closure records rather than
            // silently counts as durable: on the filesystem adapter every member is that.
            var unretained = digests.Where(digest => !_artifactMembership.ContainsKey(digest)).ToArray();
            if (unretained.Length > 0)
            {
                throw new CustodyIntegrityException(
                    "A send dependency was not retained by this run before capability minting.");
            }
        }

        return Hash(Encoding.ASCII.GetBytes(string.Join('\n', digests) + "\n"));
    }

    private async Task<ResolvedHeldBody> ResolveHeldBodyAsync(HopCustodyKey key)
    {
        if (!_heldBodies.TryGetValue(key, out var held))
        {
            throw new CustodyIntegrityException(
                "The HTTP evidence custody key does not resolve inside its exact run and attempt.");
        }

        using var deadline = new CancellationTokenSource(_profile.RequestTimeout, _timeProvider);
        ReadOnlyMemory<byte> restored;
        try
        {
            restored = await CustodyRestore.ReadCheckedAsync(
                _custodyStore,
                held.Receipt.Reference,
                deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
        {
            throw new CustodyRequiredException(
                "HTTP evidence custody readback did not complete before its evidence deadline.",
                exception);
        }
        var length = checked((ulong)restored.Length);
        var sha256 = Hash(restored.Span);
        if (length != checked((ulong)held.Receipt.Reference.ByteLength) ||
            !string.Equals(sha256, held.Receipt.Reference.ContentSha256, StringComparison.Ordinal))
        {
            throw new CustodyIntegrityException(
                "The privately resolved HTTP evidence differs from its retained custody receipt.");
        }

        return new ResolvedHeldBody(held.Receipt, restored.ToArray(), held.ReceiptSha256);
    }

    private void RegisterRetainedHop(HopCustodyKey key, RoutedHttpHop hop)
    {
        if (_openedAntecedents.ContainsKey(key))
        {
            // The custody record outlives the antecedent capability because the send reopens the
            // body through it, so the key alone cannot say "already used". This does.
            throw new InvalidOperationException(
                "A hop that already minted its redirect antecedent capability cannot be registered again.");
        }

        // The causal facts were retained when the bytes entered custody. A hop that reuses a real
        // custody key with a different target, status or request is refused below, not merely
        // ordered behind the genuine one.
        if (!_heldBodies.TryGetValue(key, out var held) ||
            key.RunIdentity != _runIdentity ||
            key.HopOrdinal != hop.Ordinal ||
            !string.Equals(key.ObservationId, hop.ObservationId, StringComparison.Ordinal) ||
            !string.Equals(
                held.ReceiptSha256,
                hop.DurableWriteReceiptSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                held.Receipt.Reference.ContentSha256,
                hop.Sha256,
                StringComparison.Ordinal) ||
            !string.Equals(held.Causal.RequestUri, hop.RequestUri, StringComparison.Ordinal) ||
            held.Causal.Status != hop.Status ||
            !string.Equals(
                held.Causal.LogicalRequestSha256,
                hop.LogicalRequestSha256,
                StringComparison.Ordinal) ||
            !HeaderFieldMatches(held.Causal.Location, hop.Headers.Location))
        {
            throw new CustodyIntegrityException(
                "A routed hop does not bind the exact body custody record that preceded it.");
        }

        if (!_retainedHops.TryAdd(key, hop))
        {
            throw new InvalidOperationException("One custody tuple cannot register two routed hops.");
        }
    }

    private static bool HeaderFieldMatches(IReadOnlyList<string> observed, RoutedHttpHeaderField projected) =>
        projected switch
        {
            RoutedHttpAbsentHeader => observed.Count == 0,
            RoutedHttpSingleHeader single =>
                observed.Count == 1 && string.Equals(observed[0], single.Value, StringComparison.Ordinal),
            RoutedHttpMultipleHeader multiple => observed.SequenceEqual(multiple.Values, StringComparer.Ordinal),
            _ => false,
        };

    private RedirectAntecedentCapability OpenRedirectAntecedent(HopCustodyKey key)
    {
        if (!_retainedHops.TryRemove(key, out var hop))
        {
            throw new InvalidOperationException(
                "A redirect antecedent must come once from the exact retained hop produced by this session.");
        }

        _openedAntecedents.TryAdd(key, 0);
        return new RedirectAntecedentCapability(this, key, hop);
    }

    private async Task<(HopCustodyKey Key, ResolvedHeldBody Body)> HoldAndResolveAsync(
        ReadOnlyMemory<byte> bytes,
        ulong requestOrdinal,
        ulong attemptOrdinal,
        ulong hopOrdinal,
        string observationId,
        HeldCausalFacts causal)
    {
        var held = await HoldAsync(bytes, causal).ConfigureAwait(false);
        var key = new HopCustodyKey(
            _runIdentity,
            requestOrdinal,
            attemptOrdinal,
            hopOrdinal,
            observationId);
        if (!_heldBodies.TryAdd(key, held))
        {
            throw new CustodyIntegrityException("One HTTP observation acquired custody twice.");
        }

        try
        {
            return (key, await ResolveHeldBodyAsync(key).ConfigureAwait(false));
        }
        catch
        {
            _heldBodies.TryRemove(key, out _);
            throw;
        }
    }

    private void RegisterRequestPolicy(RequestPolicyArtifact policy)
    {
        var retained = _requestPolicies.GetOrAdd(policy.Sha256, policy);
        if (!retained.CopyCanonicalBytes().AsSpan().SequenceEqual(policy.CopyCanonicalBytes()))
        {
            throw new CustodyIntegrityException(
                "One request-policy digest resolved to different canonical policy bytes.");
        }
    }

    private void RegisterRedirectPolicy(RedirectPolicyArtifact policy)
    {
        var retained = _redirectPolicies.GetOrAdd(policy.Sha256, policy);
        if (!retained.CopyCanonicalBytes().AsSpan().SequenceEqual(policy.CopyCanonicalBytes()))
        {
            throw new CustodyIntegrityException(
                "One redirect-policy digest resolved to different canonical policy bytes.");
        }
    }

    private void OpenAndValidatePolicies(
        HttpLogicalRequest request,
        ReadOnlyMemory<byte> body)
    {
        if (!_requestPolicies.TryGetValue(request.RequestPolicySha256, out var requestPolicy) ||
            !_redirectPolicies.TryGetValue(request.RedirectPolicySha256, out var redirectPolicy))
        {
            throw new InvalidOperationException(
                "The logical request does not open both exact retained transport policies.");
        }

        requestPolicy.Validate(
            request,
            body,
            _profile,
            _adapterExecutionIdentity,
            _adapterExecutionBytes);
        redirectPolicy.Validate(request, _profile);
        if ((requestPolicy.Kind == RequestPolicyKind.RobotsGet) !=
            (redirectPolicy.Kind == RedirectPolicyKind.RobotsRoute))
        {
            throw new InvalidOperationException(
                "The request and redirect policy arms describe different acquisition channels.");
        }
    }

    private static bool TryCreateRedirectRequest(
        HttpLogicalRequest current,
        RoutedHttpHeaderField location,
        out HttpLogicalRequest next)
    {
        next = null!;
        if (location is not RoutedHttpSingleHeader single)
        {
            return false;
        }

        try
        {
            next = HttpLogicalRequest.Create(
                single.Value,
                current.Method,
                current.Headers,
                current.Body,
                current.RequestPolicySha256,
                current.RedirectPolicySha256);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void SetRobotsGenerationStart(DateTimeOffset startedAt, long startedTimestamp)
    {
        lock (_generationLock)
        {
            if (_robotsStartedAt is not null || _robotsStartedTimestamp is not null)
            {
                throw new InvalidOperationException("A robots generation cannot start twice.");
            }

            _robotsStartedAt = startedAt;
            _robotsStartedTimestamp = startedTimestamp;
        }
    }

    private static void ObserveLate<T>(Task<T> task) => _ = ObserveLateAsync(task);

    private static async Task ObserveLateAsync<T>(Task<T> task)
    {
        try
        {
            _ = await task.ConfigureAwait(false);
        }
        catch
        {
            // The typed bounded outcome already owns the failure; this only observes late cleanup.
        }
    }

    private static (SourceArtifactRef Identity, byte[] CanonicalBytes) CreateAdapterExecutionArtifact()
    {
        var httpAssembly = typeof(HttpClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("System.Net.Http has no informational version.");
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n',
            "lex-routed-http-adapter-execution/1",
            RuntimeInformation.FrameworkDescription,
            httpAssembly,
            "http/1.1",
            "allow_auto_redirect=false",
            "automatic_decompression=none",
            "activity_headers_propagator=null",
            "max_response_drain_size=0",
            "cookies=false",
            "proxy=false",
            "http_client_timeout=infinite") + "\n");
        return (
            new SourceArtifactRef(
                "urn:uuid:d51a7d7b-57d7-4c98-8b05-75caa173ff17",
                Hash(bytes)),
            bytes);
    }

    private static (SourceArtifactRef Reference, byte[] CanonicalBytes) CreateRunIdentity(
        OfficialMachineQuerySourceProfile profile,
        DateTimeOffset startedAt,
        SourceArtifactRef adapterExecutionIdentity)
    {
        var resourceId = $"urn:uuid:{Guid.NewGuid():D}";
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n',
            "lex-http-acquisition-run/1",
            resourceId,
            profile.ArtifactRef.ResourceId,
            profile.ArtifactRef.Sha256,
            Timestamp(startedAt),
            adapterExecutionIdentity.ResourceId,
            adapterExecutionIdentity.Sha256));
        return (new SourceArtifactRef(resourceId, Hash(bytes)), bytes);
    }

    private static byte[] BuildRoutePolicyBytes(OfficialMachineQuerySourceProfile profile)
    {
        var lines = new List<string>
        {
            "lex-http-redirect-policy/1",
            profile.ArtifactRef.ResourceId,
            profile.ArtifactRef.Sha256,
        };
        lines.AddRange(profile.RobotsRoute.Steps.Select((step, index) => string.Join('\t',
            index.ToString(CultureInfo.InvariantCulture),
            step.RequestedUri,
            step.ExpectedStatusCode.ToString(CultureInfo.InvariantCulture),
            step.ExpectedLocation ?? string.Empty)));
        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }

    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString(
        "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
        CultureInfo.InvariantCulture);

    private static string Hash(params ReadOnlyMemory<byte>[] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in parts)
        {
            hash.AppendData(part.Span);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// An operational failure is discriminated by exactly one of a transport reason or a
    /// post-header rejection. Enforced at construction so the pair cannot collapse into a result
    /// whose kind says failure and whose reason says nothing, or one that claims both.
    /// </summary>
    private static void RequireExactlyOneOperationalReasonShape(
        OfficialHttpAcquisitionOutcomeKind kind,
        OfficialHttpOperationalFailureReason? operationalReason,
        PostHeaderRejection? postHeaderRejection)
    {
        if (kind == OfficialHttpAcquisitionOutcomeKind.OperationalFailure &&
            (operationalReason is null) == (postHeaderRejection is null))
        {
            throw new InvalidOperationException(
                "An operational failure carries exactly one of a transport reason or a post-header rejection.");
        }
    }

    internal sealed class StartResult
    {
        private StartResult(
            OfficialHttpAcquisitionOutcomeKind kind,
            RoutedHttpAcquisitionSession? session,
            RoutedHttpEvidence? evidence,
            OfficialMachineQueryLocalSafetyReason? localSafetyReason,
            OfficialHttpOperationalFailureReason? operationalReason,
            PostHeaderRejection? postHeaderRejection)
        {
            RequireExactlyOneOperationalReasonShape(kind, operationalReason, postHeaderRejection);
            Kind = kind;
            Session = session;
            Evidence = evidence;
            LocalSafetyReason = localSafetyReason;
            OperationalReason = operationalReason;
            PostHeaderRejection = postHeaderRejection;
        }

        internal OfficialHttpAcquisitionOutcomeKind Kind { get; }
        internal RoutedHttpAcquisitionSession? Session { get; }
        internal RoutedHttpEvidence? Evidence { get; }
        internal OfficialMachineQueryLocalSafetyReason? LocalSafetyReason { get; }
        internal OfficialHttpOperationalFailureReason? OperationalReason { get; }

        /// <summary>
        /// Present exactly when the robots response reached header completion and was then
        /// rejected as unrepresentable in /4. Its bytes are in custody and its prior hops are
        /// retained; <see cref="OperationalReason"/> is null because no transport reason applies.
        /// </summary>
        internal PostHeaderRejection? PostHeaderRejection { get; }

        internal static StartResult Started(
            RoutedHttpAcquisitionSession session,
            RoutedHttpEvidence evidence) => new(
                OfficialHttpAcquisitionOutcomeKind.ExecutedObservation,
                session,
                evidence,
                null,
                null,
                null);

        internal static StartResult PublisherDenied(RoutedHttpEvidence evidence) => new(
            OfficialHttpAcquisitionOutcomeKind.PublisherDenial,
            null,
            evidence,
            null,
            null,
            null);

        internal static StartResult Refused(
            OfficialMachineQueryLocalSafetyReason reason,
            RoutedHttpEvidence evidence) => new(
                OfficialHttpAcquisitionOutcomeKind.LocalSafetyRefusal,
                null,
                evidence,
                reason,
                null,
                null);

        internal static StartResult Operational(
            OfficialHttpOperationalFailureReason reason,
            RoutedHttpEvidence? evidence) => new(
                OfficialHttpAcquisitionOutcomeKind.OperationalFailure,
                null,
                evidence,
                null,
                reason,
                null);

        internal static StartResult PostHeaderRejected(PostHeaderRejection rejection) => new(
            rejection.FailureClass == PostHeaderFailureClass.AdapterIdentityRejected
                ? OfficialHttpAcquisitionOutcomeKind.IntegrityFailure
                : OfficialHttpAcquisitionOutcomeKind.OperationalFailure,
            null,
            null,
            null,
            null,
            rejection);

        internal static StartResult Integrity(RoutedHttpEvidence? evidence) => new(
            OfficialHttpAcquisitionOutcomeKind.IntegrityFailure,
            null,
            evidence,
            null,
            null,
            null);
    }

    internal interface IPlanItem
    {
        ulong RequestOrdinal { get; }

        Task<AttemptResult> ExecuteNextAttemptAsync(CancellationToken cancellationToken);
    }

    private sealed class PlanItem : IPlanItem
    {
        private readonly RoutedHttpAcquisitionSession _session;
        private readonly BoundMachineRequest _request;
        private readonly SourceArtifactRef _queryPlanRef;
        private readonly SemaphoreSlim _attemptGate = new(1, 1);
        private ulong _nextAttemptOrdinal;
        private bool _mayAttempt = true;

        internal PlanItem(
            RoutedHttpAcquisitionSession session,
            BoundMachineRequest request,
            SourceArtifactRef queryPlanRef,
            ulong requestOrdinal)
        {
            _session = session;
            _request = request;
            _queryPlanRef = queryPlanRef;
            RequestOrdinal = requestOrdinal;
        }

        public ulong RequestOrdinal { get; }

        public async Task<AttemptResult> ExecuteNextAttemptAsync(
            CancellationToken cancellationToken)
        {
            await _attemptGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_mayAttempt || _nextAttemptOrdinal >= (ulong)_session._profile.MaximumAttempts)
                {
                    throw new InvalidOperationException(
                        "The plan item has no admitted application attempt remaining.");
                }

                if (_nextAttemptOrdinal > 0)
                {
                    var exponent = checked((int)_nextAttemptOrdinal - 1);
                    var multiplier = 1L << Math.Min(exponent, 30);
                    var delayTicks = Math.Min(
                        checked(_session._profile.InitialRetryDelay.Ticks * multiplier),
                        _session._profile.MaximumRetryDelay.Ticks);
                    await Task.Delay(
                        TimeSpan.FromTicks(delayTicks),
                        _session._timeProvider,
                        cancellationToken).ConfigureAwait(false);
                }

                var attempt = _nextAttemptOrdinal;
                _nextAttemptOrdinal++;

                try
                {
                    var route = await _session.ExecuteMachineAttemptAsync(
                        _request,
                        _queryPlanRef,
                        RequestOrdinal,
                        attempt,
                        cancellationToken).ConfigureAwait(false);
                    var result = route.Evidence is not null
                        ? AttemptResult.Executed(route.Evidence)
                        : route.PreHeaderFailure is not null
                            ? AttemptResult.Operational(
                                OfficialHttpOperationalFailureReason.NetworkFailure,
                                route.PreHeaderFailure.FailureClass)
                            : AttemptResult.PostHeaderRejected(
                                (route.PostHeaderFailure ??
                                throw new InvalidOperationException(
                                    "A response-less route lost its typed failure.")).ToRejection());
                    _mayAttempt = IsRetryable(result) &&
                        _nextAttemptOrdinal < (ulong)_session._profile.MaximumAttempts;
                    return result;
                }
                catch (RobotsPolicyExpiredException)
                {
                    _mayAttempt = false;
                    return AttemptResult.Operational(
                        OfficialHttpOperationalFailureReason.RobotsPolicyExpired);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _mayAttempt = false;
                    throw;
                }
                catch (CustodyRequiredException)
                {
                    _mayAttempt = false;
                    return AttemptResult.Operational(
                        OfficialHttpOperationalFailureReason.CustodyUnavailable);
                }
                catch (Exception exception) when (exception is
                    CustodyIntegrityException or CustodyPolicyException)
                {
                    _mayAttempt = false;
                    return AttemptResult.IntegrityFailure();
                }
                catch
                {
                    _mayAttempt = false;
                    throw;
                }
            }
            finally
            {
                _attemptGate.Release();
            }
        }

        private static bool IsRetryable(AttemptResult result)
        {
            if (result.PreHeaderFailureClass is
                HttpPreHeaderFailureClass.HeaderDeadline or
                HttpPreHeaderFailureClass.TransportBeforeHeaders)
            {
                return true;
            }

            if (result.Evidence is null)
            {
                return false;
            }

            var terminal = result.Evidence.Hops[^1];
            if (terminal.Completion is IncompleteHttpCompletion incomplete)
            {
                try
                {
                    return HttpAcquisitionReasonRegistry.RequirePartial(incomplete.Reason) is
                        HttpPartialBodyReason.BodyDeadline or
                        HttpPartialBodyReason.BodyReadFailure or
                        HttpPartialBodyReason.DeclaredLengthShortRead;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            if (result.Evidence.Outcome is RedirectTargetUnobservedHttpRouteOutcome)
            {
                return true;
            }

            return terminal.Status is 408 or 429 or 500 or 502 or 503 or 504;
        }
    }

    internal sealed class AttemptResult
    {
        private AttemptResult(
            OfficialHttpAcquisitionOutcomeKind kind,
            RoutedHttpEvidence? evidence,
            OfficialHttpOperationalFailureReason? operationalReason,
            HttpPreHeaderFailureClass? preHeaderFailureClass,
            PostHeaderRejection? postHeaderRejection)
        {
            RequireExactlyOneOperationalReasonShape(kind, operationalReason, postHeaderRejection);
            Kind = kind;
            Evidence = evidence;
            OperationalReason = operationalReason;
            PreHeaderFailureClass = preHeaderFailureClass;
            PostHeaderRejection = postHeaderRejection;
        }

        internal OfficialHttpAcquisitionOutcomeKind Kind { get; }
        internal RoutedHttpEvidence? Evidence { get; }
        internal OfficialHttpOperationalFailureReason? OperationalReason { get; }
        internal HttpPreHeaderFailureClass? PreHeaderFailureClass { get; }

        /// <summary>
        /// Present exactly when the product response reached header completion and was then
        /// rejected as unrepresentable in /4. The same vocabulary as
        /// <see cref="StartResult.PostHeaderRejection"/>, so the two paths cannot drift.
        /// </summary>
        internal PostHeaderRejection? PostHeaderRejection { get; }

        internal static AttemptResult Executed(RoutedHttpEvidence evidence) => new(
            OfficialHttpAcquisitionOutcomeKind.ExecutedObservation,
            evidence,
            null,
            null,
            null);

        internal static AttemptResult Operational(
            OfficialHttpOperationalFailureReason reason,
            HttpPreHeaderFailureClass? preHeaderFailureClass = null) =>
            new(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, null, reason, preHeaderFailureClass, null);

        internal static AttemptResult PostHeaderRejected(PostHeaderRejection rejection) => new(
            rejection.FailureClass == PostHeaderFailureClass.AdapterIdentityRejected
                ? OfficialHttpAcquisitionOutcomeKind.IntegrityFailure
                : OfficialHttpAcquisitionOutcomeKind.OperationalFailure,
            null,
            null,
            null,
            rejection);

        internal static AttemptResult IntegrityFailure() => new(
            OfficialHttpAcquisitionOutcomeKind.IntegrityFailure,
            null,
            null,
            null,
            null);
    }

    private sealed record RouteExecution(
        RoutedHttpEvidence? Evidence,
        HopCustodyKey? TerminalCustodyKey,
        PreHeaderFailure? PreHeaderFailure,
        PostHeaderFailure? PostHeaderFailure);

    /// <summary>
    /// What the session observed about a response at the moment its bytes entered custody. A hop
    /// registered later is checked against these, not against its own claims, so a caller cannot
    /// attach a target, a status or a request the session never observed to a real custody key.
    /// </summary>
    private sealed record HeldCausalFacts(
        string RequestUri,
        int Status,
        string LogicalRequestSha256,
        IReadOnlyList<string> Location);

    private sealed record HeldBodyReceipt(
        DurableBlobWriteReceipt Receipt,
        string ReceiptSha256,
        HeldCausalFacts Causal);

    private sealed record ResolvedHeldBody(
        DurableBlobWriteReceipt Receipt,
        ReadOnlyMemory<byte> Bytes,
        string ReceiptSha256);

    private sealed record PreHeaderFailure(
        HttpPreHeaderFailureClass FailureClass,
        string LogicalRequestSha256,
        string RequestStartedAt);

    internal enum PostHeaderFailureClass
    {
        UnsupportedNegotiatedProtocol = 1,
        UnsupportedStatus = 2,
        HeaderProjectionRejected = 3,
        AdapterIdentityRejected = 4,

        /// <summary>
        /// The classified response could not be represented as a /4 hop. This is a drift catch:
        /// the classifier and the document validator disagreed, which at this head no input
        /// reaches, so the retained bytes and the route context survive as typed evidence instead
        /// of escaping as an exception.
        /// </summary>
        HopRepresentationRejected = 5,
    }

    /// <summary>
    /// A response that completed its headers and was then refused representation in /4. It is
    /// neither a transport loss nor evidence: the rejected entity is already in custody under
    /// <see cref="ContentSha256"/> with write receipt <see cref="DurableWriteReceiptSha256"/>,
    /// and every hop observed before it is retained verbatim.
    /// </summary>
    // An abstract class with a private protected constructor rather than a record: a record's
    // public constructor would let any friend assembly mint evidence of a rejected response with
    // arbitrary digests. The only concrete implementation is nested inside the private
    // PostHeaderFailure below, so the session's own route execution is the sole minter by
    // construction, and private protected does not extend across InternalsVisibleTo.
    internal abstract class PostHeaderRejection
    {
        private protected PostHeaderRejection(
            PostHeaderFailureClass failureClass,
            IReadOnlyList<RoutedHttpHop> priorHops,
            string contentSha256,
            string durableWriteReceiptSha256)
        {
            ArgumentNullException.ThrowIfNull(priorHops);
            ArgumentException.ThrowIfNullOrEmpty(contentSha256);
            ArgumentException.ThrowIfNullOrEmpty(durableWriteReceiptSha256);
            FailureClass = failureClass;
            PriorHops = priorHops;
            ContentSha256 = contentSha256;
            DurableWriteReceiptSha256 = durableWriteReceiptSha256;
        }

        internal PostHeaderFailureClass FailureClass { get; }

        /// <summary>Every hop observed before the rejected response, retained verbatim.</summary>
        internal IReadOnlyList<RoutedHttpHop> PriorHops { get; }

        /// <summary>The custody content digest of the rejected entity.</summary>
        internal string ContentSha256 { get; }

        /// <summary>The digest of the durable write receipt issued for that entity.</summary>
        internal string DurableWriteReceiptSha256 { get; }
    }

    private sealed record PostHeaderFailure(
        PostHeaderFailureClass FailureClass,
        string LogicalRequestSha256,
        string RequestStartedAt,
        HopCustodyKey CustodyKey,
        IReadOnlyList<RoutedHttpHop> PriorHops,
        string ContentSha256,
        string DurableWriteReceiptSha256)
    {
        public PostHeaderRejection ToRejection() => new MintedPostHeaderRejection(
            FailureClass,
            PriorHops,
            ContentSha256,
            DurableWriteReceiptSha256);

        private sealed class MintedPostHeaderRejection(
            PostHeaderFailureClass failureClass,
            IReadOnlyList<RoutedHttpHop> priorHops,
            string contentSha256,
            string durableWriteReceiptSha256)
            : PostHeaderRejection(failureClass, priorHops, contentSha256, durableWriteReceiptSha256);
    }

    private sealed record HopCustodyKey(
        SourceArtifactRef RunIdentity,
        ulong RequestOrdinal,
        ulong AttemptOrdinal,
        ulong HopOrdinal,
        string ObservationId);

    private sealed class RedirectAntecedentCapability(
        RoutedHttpAcquisitionSession session,
        HopCustodyKey custodyKey,
        RoutedHttpHop hop)
    {
        private int _consumed;

        internal (HopCustodyKey Key, RoutedHttpHop Hop) Consume(
            RoutedHttpAcquisitionSession expectedSession)
        {
            if (Interlocked.Exchange(ref _consumed, 1) != 0 ||
                !ReferenceEquals(session, expectedSession))
            {
                throw new InvalidOperationException(
                    "A redirect antecedent capability is session-bound and one-use.");
            }

            return (custodyKey, hop);
        }
    }

    private sealed class OriginPacingState
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal long? LastSendStartedTimestamp { get; set; }
    }

    private sealed class ActiveGenerationState
    {
        internal object Gate { get; } = new();
        internal object? Token { get; set; }
    }

    private enum RequestPolicyKind
    {
        RobotsGet = 1,
        MachineQueryPost = 2,
    }

    private sealed record ResolvedMachineRequest(
        OpenedMachineRequest Request,
        IReadOnlyList<CanonicalArtifactBytes> Artifacts);

    private sealed class SessionMachineArtifactResolver(
        RoutedHttpAcquisitionSession session) : IMachineQueryArtifactResolver
    {
        private readonly List<CanonicalArtifactBytes> _resolved = new();

        public async Task<ReadOnlyMemory<byte>> RetainAndReopenAsync(
            SourceArtifactRef reference,
            ReadOnlyMemory<byte> producerBytes,
            CancellationToken cancellationToken)
        {
            _ = new CanonicalArtifactBytes(reference, producerBytes.Span);
            var reopened = await session.RetainArtifactAsync(
                    producerBytes,
                    reference.Sha256,
                    cancellationToken)
                .ConfigureAwait(false);
            _resolved.Add(new CanonicalArtifactBytes(reference, reopened.Span));
            return reopened;
        }

        public async Task<ReadOnlyMemory<byte>> ReopenAsync(
            SourceArtifactRef reference,
            CancellationToken cancellationToken)
        {
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(
                    session._custodyStore,
                    reference.Sha256,
                    cancellationToken)
                .ConfigureAwait(false);

            // Through the retaining path, never straight into the set. Reading a digest out of
            // some lane establishes that those bytes are readable now; the gate this set feeds
            // says a send dependency was durably reopenable, which is a different claim. Routing
            // here means this run places what it depends on under its own retention rather than
            // inheriting someone else's. What that retention guarantees is not asserted: the
            // floor's semantics wait on an accepted D1-320 candidate, and none is accepted.
            var retained = await session.RetainArtifactAsync(
                    bytes, reference.Sha256, cancellationToken)
                .ConfigureAwait(false);

            _resolved.Add(new CanonicalArtifactBytes(reference, retained.Span));
            return retained;
        }

        internal IReadOnlyList<CanonicalArtifactBytes> CopyResolvedArtifacts() =>
            _resolved
                .Select(static artifact => new CanonicalArtifactBytes(
                    artifact.Reference,
                    artifact.CopyCanonicalBytes()))
                .ToArray();
    }

    private sealed class CanonicalArtifactBytes
    {
        private readonly byte[] _canonicalBytes;

        internal CanonicalArtifactBytes(
            SourceArtifactRef reference,
            ReadOnlySpan<byte> canonicalBytes)
        {
            Reference = reference ?? throw new ArgumentNullException(nameof(reference));
            _canonicalBytes = canonicalBytes.ToArray();
            if (!string.Equals(Hash(_canonicalBytes), reference.Sha256, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Canonical artifact bytes do not match their retained reference.",
                    nameof(canonicalBytes));
            }
        }

        internal SourceArtifactRef Reference { get; }

        internal byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();
    }

    private sealed class RequestPolicyArtifact
    {
        private readonly byte[] _canonicalBytes;
        private readonly string[] _admittedUris;
        private readonly HttpLogicalRequestHeader[] _headers;
        private readonly CanonicalArtifactBytes[] _binderArtifacts;

        private RequestPolicyArtifact(
            RequestPolicyKind kind,
            SourceArtifactRef sourceProfileRef,
            SourceArtifactRef adapterExecutionRef,
            ReadOnlySpan<byte> adapterExecutionBytes,
            IReadOnlyList<string> admittedUris,
            HttpRequestMethod method,
            IReadOnlyList<HttpLogicalRequestHeader> headers,
            ulong bodyLength,
            string bodySha256,
            SourceArtifactRef? renderReceiptRef,
            SourceArtifactRef? queryPlanRef,
            SourceArtifactRef? orderedParameterSetRef,
            SourceArtifactRef? rendererProfileRef,
            SourceArtifactRef? rendererSourceRef,
            SourceRegistryMemberRef? contentType,
            IReadOnlyList<CanonicalArtifactBytes> binderArtifacts,
            OfficialMachineQuerySourceProfile profile)
        {
            Kind = kind;
            SourceProfileRef = sourceProfileRef;
            AdapterExecutionRef = adapterExecutionRef;
            _admittedUris = admittedUris.ToArray();
            Method = method;
            _headers = headers.ToArray();
            BodyLength = bodyLength;
            BodySha256 = bodySha256;
            RenderReceiptRef = renderReceiptRef;
            QueryPlanRef = queryPlanRef;
            OrderedParameterSetRef = orderedParameterSetRef;
            RendererProfileRef = rendererProfileRef;
            RendererSourceRef = rendererSourceRef;
            ContentType = contentType;
            _binderArtifacts = binderArtifacts.ToArray();

            var lines = new List<string>
            {
                "lex-http-request-policy/1",
                kind == RequestPolicyKind.RobotsGet ? "robots_get" : "machine_query_post",
                $"source_profile={sourceProfileRef.ResourceId}\t{sourceProfileRef.Sha256}",
                $"adapter_execution={adapterExecutionRef.ResourceId}\t{adapterExecutionRef.Sha256}",
                $"adapter_execution_bytes_sha256={Hash(adapterExecutionBytes)}",

                // The reason vocabulary decides how a partial completion is labelled, and it was
                // retained and bound to the hop while being reachable from no published digest: a
                // verifier could learn which vocabulary was in force only by trusting the binary.
                // Named here because these bytes are already retained and already bound.
                // The closure digest is deliberately not added: the closure is computed from this
                // policy's digest, so naming it here would be circular.
                $"reason_registry={HttpAcquisitionReasonRegistry.Sha256}",
                $"method={method}",
                $"requested_http_version={HttpLogicalRequest.RequestedHttpVersion}",
                $"version_policy={HttpLogicalRequest.VersionPolicy}",
                $"request_timeout_ticks={profile.RequestTimeout.Ticks.ToString(CultureInfo.InvariantCulture)}",
                $"minimum_request_interval_ticks={profile.MinimumRequestInterval.Ticks.ToString(CultureInfo.InvariantCulture)}",
                $"maximum_attempts={profile.MaximumAttempts.ToString(CultureInfo.InvariantCulture)}",
                $"initial_retry_delay_ticks={profile.InitialRetryDelay.Ticks.ToString(CultureInfo.InvariantCulture)}",
                $"maximum_retry_delay_ticks={profile.MaximumRetryDelay.Ticks.ToString(CultureInfo.InvariantCulture)}",
                $"maximum_response_bytes={profile.MaximumResponseBytes.ToString(CultureInfo.InvariantCulture)}",
                "allow_auto_redirect=false",
                "automatic_decompression=none",
                "activity_headers_propagator=null",
                "max_response_drain_size=0",
                "cookies=false",
                "proxy=false",
                "http_client_timeout=infinite",
            };
            lines.AddRange(profile.RetryConditions.Select(static condition => $"retry={condition}"));
            lines.AddRange(_admittedUris.Select(static uri => $"uri={uri}"));
            lines.AddRange(_headers.Select(static header => $"header={header.Name}\t{header.Value}"));
            lines.Add($"body={bodyLength.ToString(CultureInfo.InvariantCulture)}\t{bodySha256}");
            if (kind == RequestPolicyKind.MachineQueryPost)
            {
                lines.Add($"render_receipt={renderReceiptRef!.ResourceId}\t{renderReceiptRef.Sha256}");
                lines.Add($"query_plan={queryPlanRef!.ResourceId}\t{queryPlanRef.Sha256}");
                lines.Add($"ordered_parameter_set={orderedParameterSetRef!.ResourceId}\t{orderedParameterSetRef.Sha256}");
                lines.Add($"renderer_profile={rendererProfileRef!.ResourceId}\t{rendererProfileRef.Sha256}");
                lines.Add($"renderer_source={rendererSourceRef!.ResourceId}\t{rendererSourceRef.Sha256}");
                lines.Add($"content_type_registry={contentType!.RegistryRef.ResourceId}\t{contentType.RegistryRef.Sha256}");
                lines.Add($"content_type_member={contentType.MemberKey}");
                lines.AddRange(_binderArtifacts.Select(static artifact =>
                    $"opened_artifact={artifact.Reference.ResourceId}\t{artifact.Reference.Sha256}"));
            }

            _canonicalBytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
            Sha256 = Hash(_canonicalBytes);
        }

        internal RequestPolicyKind Kind { get; }

        internal string Sha256 { get; }

        private SourceArtifactRef SourceProfileRef { get; }

        private SourceArtifactRef AdapterExecutionRef { get; }

        private HttpRequestMethod Method { get; }

        private ulong BodyLength { get; }

        private string BodySha256 { get; }

        private SourceArtifactRef? RenderReceiptRef { get; }

        private SourceArtifactRef? QueryPlanRef { get; }

        private SourceArtifactRef? OrderedParameterSetRef { get; }

        private SourceArtifactRef? RendererProfileRef { get; }

        private SourceArtifactRef? RendererSourceRef { get; }

        private SourceRegistryMemberRef? ContentType { get; }

        internal IReadOnlyList<CanonicalArtifactBytes> BinderArtifacts => _binderArtifacts;

        internal static RequestPolicyArtifact ForRobots(
            OfficialMachineQuerySourceProfile profile,
            SourceArtifactRef adapterExecutionRef,
            ReadOnlySpan<byte> adapterExecutionBytes)
        {
            var empty = Array.Empty<byte>();
            return new RequestPolicyArtifact(
                RequestPolicyKind.RobotsGet,
                profile.ArtifactRef,
                adapterExecutionRef,
                adapterExecutionBytes,
                profile.RobotsRoute.Steps.Select(static step => step.RequestedUri).ToArray(),
                HttpRequestMethod.Get,
                [new HttpLogicalRequestHeader("user-agent", profile.CrawlerUserAgent)],
                0,
                Hash(empty.AsSpan()),
                null,
                null,
                null,
                null,
                null,
                null,
                Array.Empty<CanonicalArtifactBytes>(),
                profile);
        }

        internal static RequestPolicyArtifact ForMachineQuery(
            OfficialMachineQuerySourceProfile profile,
            SourceArtifactRef adapterExecutionRef,
            ReadOnlySpan<byte> adapterExecutionBytes,
            OpenedMachineRequest request,
            IReadOnlyList<CanonicalArtifactBytes> binderArtifacts,
            IReadOnlyList<HttpLogicalRequestHeader> headers,
            ReadOnlySpan<byte> body)
        {
            var receipt = request.RenderReceipt;
            var requiredArtifacts = new[]
            {
                request.RenderReceiptRef,
                receipt.QueryPlanRef,
                receipt.OrderedParameterSetRef,
                receipt.RendererProfileRef,
                receipt.RendererSourceRef,
                receipt.ContentType?.RegistryRef ?? throw new ArgumentException(
                    "A machine POST request policy requires the full content-type registry member.",
                    nameof(request)),
            };
            var reopenedArtifacts = binderArtifacts
                .Select(static artifact => artifact.Reference)
                .ToHashSet();
            if (requiredArtifacts.Any(reference => !reopenedArtifacts.Contains(reference)))
            {
                throw new ArgumentException(
                    "The machine request did not reopen every required artifact role.",
                    nameof(binderArtifacts));
            }

            return new RequestPolicyArtifact(
                RequestPolicyKind.MachineQueryPost,
                profile.ArtifactRef,
                adapterExecutionRef,
                adapterExecutionBytes,
                [request.RequestedUri],
                receipt.Method,
                headers,
                checked((ulong)body.Length),
                Hash(body),
                request.RenderReceiptRef,
                receipt.QueryPlanRef,
                receipt.OrderedParameterSetRef,
                receipt.RendererProfileRef,
                receipt.RendererSourceRef,
                receipt.ContentType,
                binderArtifacts,
                profile);
        }

        internal byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();

        internal void Validate(
            HttpLogicalRequest request,
            ReadOnlyMemory<byte> body,
            OfficialMachineQuerySourceProfile profile,
            SourceArtifactRef adapterExecutionRef,
            ReadOnlySpan<byte> adapterExecutionBytes)
        {
            // Sha256 was assigned as Hash(_canonicalBytes) in the constructor over a private array
            // nothing writes, so comparing the two here could never be false. The binding that can
            // fail is the request's digest against this artifact's.
            if (!string.Equals(Sha256, request.RequestPolicySha256, StringComparison.Ordinal) ||
                SourceProfileRef != profile.ArtifactRef ||
                AdapterExecutionRef != adapterExecutionRef ||
                !string.Equals(AdapterExecutionRef.Sha256, Hash(adapterExecutionBytes), StringComparison.Ordinal) ||
                request.Method != Method ||
                !_admittedUris.Contains(request.Uri, StringComparer.Ordinal) ||
                !request.Headers.SequenceEqual(_headers) ||
                request.Body.Length != BodyLength ||
                !string.Equals(request.Body.Sha256, BodySha256, StringComparison.Ordinal) ||
                checked((ulong)body.Length) != BodyLength ||
                !string.Equals(Hash(body.Span), BodySha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The logical request does not reproduce its exact opened request policy.");
            }

            if (Kind == RequestPolicyKind.RobotsGet &&
                (QueryPlanRef is not null || OrderedParameterSetRef is not null ||
                 RendererProfileRef is not null || RendererSourceRef is not null || ContentType is not null))
            {
                throw new InvalidOperationException("A robots GET policy cannot carry machine-query authority.");
            }

            if (Kind == RequestPolicyKind.MachineQueryPost &&
                (QueryPlanRef is null || OrderedParameterSetRef is null ||
                 RendererProfileRef is null || RendererSourceRef is null || ContentType is null ||
                 request.Headers.Count != 3 ||
                 !string.Equals(request.Headers[2].Name, "content-type", StringComparison.Ordinal) ||
                 !string.Equals(
                     request.Headers[2].Value,
                     $"{ContentType.MemberKey}; charset=utf-8",
                     StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "A machine-query request policy lost its exact plan or representation binding.");
            }
        }
    }

    private enum RedirectPolicyKind
    {
        RobotsRoute = 1,
        NoRedirect = 2,
    }

    private sealed class RedirectPolicyArtifact
    {
        private readonly byte[] _canonicalBytes;
        private readonly string[] _admittedUris;

        private RedirectPolicyArtifact(
            RedirectPolicyKind kind,
            SourceArtifactRef sourceProfileRef,
            byte[] canonicalBytes,
            IReadOnlyList<string> admittedUris)
        {
            Kind = kind;
            SourceProfileRef = sourceProfileRef;
            _canonicalBytes = canonicalBytes.ToArray();
            _admittedUris = admittedUris.ToArray();
            Sha256 = Hash(_canonicalBytes);
        }

        internal RedirectPolicyKind Kind { get; }

        internal string Sha256 { get; }

        private SourceArtifactRef SourceProfileRef { get; }

        internal static RedirectPolicyArtifact ForRobots(OfficialMachineQuerySourceProfile profile) =>
            new(
                RedirectPolicyKind.RobotsRoute,
                profile.ArtifactRef,
                BuildRoutePolicyBytes(profile),
                profile.RobotsRoute.Steps.Select(static step => step.RequestedUri).ToArray());

        internal static RedirectPolicyArtifact NoRedirect(OfficialMachineQuerySourceProfile profile) =>
            new(
                RedirectPolicyKind.NoRedirect,
                profile.ArtifactRef,
                Encoding.UTF8.GetBytes(string.Join('\n',
                    "lex-http-redirect-policy/1",
                    profile.ArtifactRef.ResourceId,
                    profile.ArtifactRef.Sha256,
                    "no_redirect") + "\n"),
                [profile.RequestTarget]);

        internal byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();

        internal void Validate(
            HttpLogicalRequest request,
            OfficialMachineQuerySourceProfile profile)
        {
            // As in RequestPolicyArtifact: the constructor's own hash is not re-compared, because
            // that clause could never be false; the request's digest against this one can.
            if (!string.Equals(Sha256, request.RedirectPolicySha256, StringComparison.Ordinal) ||
                SourceProfileRef != profile.ArtifactRef ||
                !_admittedUris.Contains(request.Uri, StringComparer.Ordinal) ||
                Kind == RedirectPolicyKind.NoRedirect && request.Method != HttpRequestMethod.Post ||
                Kind == RedirectPolicyKind.RobotsRoute && request.Method != HttpRequestMethod.Get)
            {
                throw new InvalidOperationException(
                    "The logical request does not reproduce its exact opened redirect policy.");
            }
        }
    }

    private sealed class SendLease
    {
        private readonly RoutedHttpAcquisitionSession _session;
        private readonly HttpLogicalRequest _request;
        private readonly ReadOnlyMemory<byte> _body;
        private readonly SourceArtifactRef _runIdentity;
        private readonly object _generationToken;
        private readonly ulong _requestOrdinal;
        private readonly ulong _attemptOrdinal;
        private readonly ulong _hopOrdinal;
        private readonly string _logicalRequestSha256;
        private readonly RedirectAntecedent? _antecedent;
        private readonly bool _startsRobotsGeneration;
        private int _started;

        internal HttpLogicalRequest Request => _request;

        internal ReadOnlyMemory<byte> Body => _body;

        internal bool BindsSession(RoutedHttpAcquisitionSession session) =>
            ReferenceEquals(_session, session) &&
            _runIdentity == session._runIdentity &&
            ReferenceEquals(_generationToken, session._generationToken);

        internal bool MatchesRetention(string logicalRequestSha256, string closureSha256) =>
            string.Equals(
                logicalRequestSha256,
                _logicalRequestSha256,
                StringComparison.Ordinal) &&
            string.Equals(
                closureSha256,
                _session.ComputeSendClosureSha256(
                    _request,
                    _session._requestPolicies[_request.RequestPolicySha256],
                    _logicalRequestSha256),
                StringComparison.Ordinal);

        private SendLease(
            RoutedHttpAcquisitionSession session,
            HttpLogicalRequest request,
            ReadOnlyMemory<byte> body,
            ulong requestOrdinal,
            ulong attemptOrdinal,
            ulong hopOrdinal,
            string logicalRequestSha256,
            RedirectAntecedent? antecedent,
            bool startsRobotsGeneration)
        {
            _session = session;
            _request = request;
            _body = body;
            _runIdentity = session._runIdentity;
            _generationToken = session._generationToken;
            _requestOrdinal = requestOrdinal;
            _attemptOrdinal = attemptOrdinal;
            _hopOrdinal = hopOrdinal;
            _logicalRequestSha256 = logicalRequestSha256;
            _antecedent = antecedent;
            _startsRobotsGeneration = startsRobotsGeneration;
        }

        internal static SendLease Initial(
            RoutedHttpAcquisitionSession session,
            HttpLogicalRequest request,
            ReadOnlyMemory<byte> body,
            ulong requestOrdinal,
            ulong attemptOrdinal,
            bool startsRobotsGeneration)
        {
            var logicalRequestSha256 = Hash(request.CopyCanonicalBytes());
            return new SendLease(
                session,
                request,
                body,
                requestOrdinal,
                attemptOrdinal,
                0,
                logicalRequestSha256,
                null,
                startsRobotsGeneration);
        }

        internal static SendLease FromRedirect(
            RoutedHttpAcquisitionSession session,
            HttpLogicalRequest request,
            ReadOnlyMemory<byte> body,
            ulong requestOrdinal,
            ulong attemptOrdinal,
            ulong nextHopOrdinal,
            string antecedentRedirectPolicySha256,
            RedirectAntecedentCapability antecedentCapability)
        {
            var (antecedentCustodyKey, antecedent) = antecedentCapability.Consume(session);
            if (nextHopOrdinal == 0 || antecedent.Ordinal != nextHopOrdinal - 1 ||
                antecedent.Completion is IncompleteHttpCompletion ||
                antecedent.StatusDisposition != HttpStatusDisposition.RedirectObserved ||
                antecedent.Headers.Location is not RoutedHttpSingleHeader location ||
                !string.Equals(location.Value, request.Uri, StringComparison.Ordinal) ||
                antecedentCustodyKey.RunIdentity != session._runIdentity ||
                antecedentCustodyKey.RequestOrdinal != requestOrdinal ||
                antecedentCustodyKey.AttemptOrdinal != attemptOrdinal ||
                antecedentCustodyKey.HopOrdinal != antecedent.Ordinal ||
                !string.Equals(
                    antecedentCustodyKey.ObservationId,
                    antecedent.ObservationId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A redirect send capability must name its exact immediate antecedent hop.");
            }

            // The route policy is bound here, at mint, against the digest the caller took from the
            // request that produced the antecedent hop: two sources, so the comparison can fail.
            // It is not re-checked at send, because RetainSendArtifactsAsync validates the
            // successor's own policy before the send-time tuple check runs, which would make a
            // second comparison there unreachable by construction.
            if (!string.Equals(
                    request.RedirectPolicySha256,
                    antecedentRedirectPolicySha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A redirect send capability must carry its antecedent's exact route policy.");
            }

            var logicalRequestSha256 = Hash(request.CopyCanonicalBytes());
            return new SendLease(
                session,
                request,
                body,
                requestOrdinal,
                attemptOrdinal,
                nextHopOrdinal,
                logicalRequestSha256,
                new RedirectAntecedent(
                    antecedent.ObservationId,
                    antecedentCustodyKey,
                    antecedent.DurableWriteReceiptSha256,
                    antecedentRedirectPolicySha256,
                    location.Value),
                startsRobotsGeneration: false);
        }

        internal async Task<SendInvocation> RetainAndSendAsync(
            CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                throw new InvalidOperationException("An HTTP send capability is one-use.");
            }

            var retained = await _session.RetainSendArtifactsAsync(this, cancellationToken)
                .ConfigureAwait(false);
            return await ConsumeAndSendCoreAsync(retained, cancellationToken).ConfigureAwait(false);
        }

        private async Task<SendInvocation> ConsumeAndSendCoreAsync(
            RetainedSendArtifacts retained,
            CancellationToken cancellationToken)
        {
            retained.Consume(this);

            if (_runIdentity != _session._runIdentity ||
                !ReferenceEquals(_generationToken, _session._generationToken) ||
                !string.Equals(
                    _logicalRequestSha256,
                    Hash(_request.CopyCanonicalBytes()),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The HTTP send capability no longer binds this run, generation, or logical request.");
            }

            if (_antecedent is null)
            {
                if (_hopOrdinal != 0)
                {
                    throw new InvalidOperationException("Only hop zero can use an initial send capability.");
                }
            }
            else
            {
                var key = _antecedent.CustodyKey;
                if (_hopOrdinal == 0 || key.RunIdentity != _runIdentity ||
                    key.RequestOrdinal != _requestOrdinal || key.AttemptOrdinal != _attemptOrdinal ||
                    key.HopOrdinal != _hopOrdinal - 1 ||
                    !string.Equals(key.ObservationId, _antecedent.ObservationId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The redirect send capability does not bind its exact antecedent evidence tuple.");
                }

                var reopenedAntecedent = await _session.ResolveHeldBodyAsync(key).ConfigureAwait(false);
                if (!string.Equals(
                        reopenedAntecedent.ReceiptSha256,
                        _antecedent.ReceiptSha256,
                        StringComparison.Ordinal))
                {
                    throw new CustodyIntegrityException(
                        "The redirect antecedent custody receipt changed before its dependent send.");
                }
            }

            ValidateRequestBody(_request, _body);
            _session.OpenAndValidatePolicies(_request, _body);
            var uri = new Uri(_request.Uri, UriKind.Absolute);
            var origin = string.Create(
                CultureInfo.InvariantCulture,
                $"https://{uri.Host}:{uri.Port}");
            var pacingKey = _session._usesPinnedHandler
                ? origin
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"test-{RuntimeHelpers.GetHashCode(_session._timeProvider)}:{origin}");
            var pacing = PacingStates.GetOrAdd(pacingKey, static _ => new OriginPacingState());
            await pacing.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            HttpRequestMessage? outbound = null;
            Task<HttpResponseMessage>? sendTask = null;
            CancellationTokenSource? sendCancellation = null;
            DateTimeOffset startedAt;
            long startedTimestamp;
            try
            {
                if (pacing.LastSendStartedTimestamp is long previous)
                {
                    var elapsed = _session._timeProvider.GetElapsedTime(
                        previous,
                        _session._timeProvider.GetTimestamp());
                    var delay = _session._profile.MinimumRequestInterval - elapsed;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(
                            delay,
                            _session._timeProvider,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                outbound = BuildOutboundRequest(_request, _body);
                ValidateRequestBody(_request, _body);
                _session.OpenAndValidatePolicies(_request, _body);
                sendCancellation = new CancellationTokenSource();
                lock (_session._activeGeneration.Gate)
                {
                    // These exact two reads define the retained send instant. The active-generation
                    // check and SendAsync invocation share one lock, so a refresh cannot interleave.
                    startedAt = _session._timeProvider.GetUtcNow();
                    startedTimestamp = _session._timeProvider.GetTimestamp();
                    if (_startsRobotsGeneration)
                    {
                        _session.EnsureGenerationActive();
                        _session.SetRobotsGenerationStart(startedAt, startedTimestamp);
                    }
                    else
                    {
                        _session.EnsureGenerationCurrentAt(startedAt, startedTimestamp);
                    }

                    pacing.LastSendStartedTimestamp = startedTimestamp;
                    try
                    {
                        // Validation and request construction are complete. This owned operation
                        // performs the invocation itself; no caller holds a check-to-send interval.
                        sendTask = _session._client.SendAsync(
                            outbound,
                            HttpCompletionOption.ResponseHeadersRead,
                            sendCancellation.Token);
                    }
                    catch (HttpRequestException exception)
                    {
                        outbound.Dispose();
                        sendCancellation.Dispose();
                        return new SendInvocation(
                            null,
                            null,
                            Timestamp(startedAt),
                            exception,
                            HttpPreHeaderFailureClass.TransportBeforeHeaders);
                    }
                }
            }
            finally
            {
                pacing.Gate.Release();
            }

            try
            {
                var response = await sendTask.WaitAsync(
                    _session._profile.RequestTimeout,
                    _session._timeProvider,
                    cancellationToken).ConfigureAwait(false);
                sendCancellation.Dispose();
                return new SendInvocation(
                    response,
                    outbound,
                    Timestamp(startedAt),
                    null,
                    null);
            }
            catch (TimeoutException exception)
            {
                await sendCancellation.CancelAsync().ConfigureAwait(false);
                DisposeLateSend(sendTask, outbound, sendCancellation);
                return new SendInvocation(
                    null,
                    null,
                    Timestamp(startedAt),
                    exception,
                    HttpPreHeaderFailureClass.HeaderDeadline);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await sendCancellation.CancelAsync().ConfigureAwait(false);
                DisposeLateSend(sendTask, outbound, sendCancellation);
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                outbound.Dispose();
                sendCancellation.Dispose();
                return new SendInvocation(
                    null,
                    null,
                    Timestamp(startedAt),
                    exception,
                    HttpPreHeaderFailureClass.TransportBeforeHeaders);
            }
        }

        // OBS-01 5.4 names four things the capability must contain: the observation id, the
        // retained absolute target, the route-policy identity and the next logical request
        // identity. RetainedTarget is containment, not a check: a later reader can see from the
        // capability alone which destination this authorization was bound to, without trusting
        // that the mint-time comparison ran.
        private sealed record RedirectAntecedent(
            string ObservationId,
            HopCustodyKey CustodyKey,
            string ReceiptSha256,
            string RedirectPolicySha256,
            string RetainedTarget);

        private static HttpRequestMessage BuildOutboundRequest(
            HttpLogicalRequest request,
            ReadOnlyMemory<byte> body)
        {
            var outbound = new HttpRequestMessage(
                request.Method == HttpRequestMethod.Get ? HttpMethod.Get : HttpMethod.Post,
                request.Uri)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };

            if (request.Method == HttpRequestMethod.Post)
            {
                outbound.Content = new ByteArrayContent(body.ToArray());
            }

            foreach (var header in request.Headers)
            {
                var added = string.Equals(header.Name, "content-type", StringComparison.Ordinal)
                    ? outbound.Content is not null &&
                      outbound.Content.Headers.TryAddWithoutValidation(header.Name, header.Value)
                    : outbound.Headers.TryAddWithoutValidation(header.Name, header.Value);
                if (!added)
                {
                    outbound.Dispose();
                    throw new InvalidOperationException(
                        $"The admitted logical header '{header.Name}' could not be applied exactly.");
                }
            }

            return outbound;
        }

        private static void DisposeLateSend(
            Task<HttpResponseMessage> sendTask,
            HttpRequestMessage outbound,
            CancellationTokenSource cancellation) =>
            _ = DisposeLateSendAsync(sendTask, outbound, cancellation);

        private static async Task DisposeLateSendAsync(
            Task<HttpResponseMessage> sendTask,
            HttpRequestMessage outbound,
            CancellationTokenSource cancellation)
        {
            try
            {
                using var response = await sendTask.ConfigureAwait(false);
            }
            catch
            {
                // The bounded failure already owns the result; this path only owns late cleanup.
            }
            finally
            {
                outbound.Dispose();
                cancellation.Dispose();
            }
        }
    }

    private sealed class RetainedSendArtifacts(
        SendLease lease,
        string logicalRequestSha256,
        string closureSha256)
    {
        private int _consumed;

        // Tripwires, not reachable guards: there is one mint site and one consume site, both
        // inside RetainAndSendAsync behind the lease's own one-use exchange, so neither the
        // second-consume clause nor the wrong-lease clause can fire today. They stay because the
        // durable-set gate they sit beside is what would catch the retain sequence and the closure
        // list drifting apart, and a caller added tomorrow outside that path hits these first.
        internal void Consume(SendLease expectedLease)
        {
            if (Interlocked.Exchange(ref _consumed, 1) != 0 ||
                !ReferenceEquals(lease, expectedLease) ||
                !expectedLease.MatchesRetention(logicalRequestSha256, closureSha256))
            {
                throw new InvalidOperationException(
                    "The retained-artifact capability does not bind this exact send lease.");
            }
        }
    }

    private sealed record SendInvocation(
        HttpResponseMessage? Response,
        HttpRequestMessage? OutboundRequest,
        string RequestStartedAt,
        Exception? Failure,
        HttpPreHeaderFailureClass? PreHeaderFailureClass);

    private sealed record BodyCapture(ReadOnlyMemory<byte> Bytes, BodyCaptureEvent Event);

    private sealed record RawResponseHeaders(
        IReadOnlyList<string> ContentType,
        IReadOnlyList<string> ContentLength,
        IReadOnlyList<string> ContentEncoding,
        IReadOnlyList<string> TransferEncoding,
        IReadOnlyList<string> ContentRange,
        IReadOnlyList<string> ETag,
        IReadOnlyList<string> LastModified,
        IReadOnlyList<string> Location,
        IReadOnlyList<string> CacheControl,
        IReadOnlyList<string> Expires,
        IReadOnlyList<string> Date,
        IReadOnlyList<string> Age,
        IReadOnlyList<string> Tcn);

    private enum BodyCaptureEvent
    {
        DeclaredLengthReached = 1,
        CleanEof = 2,
        CapSentinel = 3,
        CallerCancelledAfterHeaders = 4,
        BodyDeadline = 5,
        ResponseEnded = 6,
        BodyReadFailure = 7,
    }

    private sealed class BodyDeadlineException : Exception
    {
        internal BodyDeadlineException()
        {
        }

        internal BodyDeadlineException(Exception innerException)
            : base("The response body did not complete before its transport-owned deadline.", innerException)
        {
        }
    }

    private sealed class RobotsPolicyExpiredException : Exception;

    private async Task<RouteExecution> ExecuteRouteAsync(
        HttpLogicalRequest request,
        ReadOnlyMemory<byte> requestBody,
        ulong requestOrdinal,
        ulong attemptOrdinal,
        RobotsPolicyRoute? robotsRoute,
        bool enforceGenerationAge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestBody(request, requestBody);

        var hops = new List<RoutedHttpHop>();
        var currentRequest = request;
        var logicalRequestBytes = currentRequest.CopyCanonicalBytes();
        _ = HttpLogicalRequest.ParseAndVerify(logicalRequestBytes);
        var logicalRequestSha256 = Hash(logicalRequestBytes);
        var lease = SendLease.Initial(
            this,
            currentRequest,
            requestBody,
            requestOrdinal,
            attemptOrdinal,
            startsRobotsGeneration: robotsRoute is not null);
        string? antecedentObservationId = null;
        while (true)
        {
            if (enforceGenerationAge)
            {
                EnsureGenerationCurrent();
            }

            var send = await lease.RetainAndSendAsync(cancellationToken).ConfigureAwait(false);
            if (send.Response is null)
            {
                if (hops.Count == 0)
                {
                    return new RouteExecution(
                        null,
                        null,
                        new PreHeaderFailure(
                            send.PreHeaderFailureClass ?? HttpPreHeaderFailureClass.TransportBeforeHeaders,
                            logicalRequestSha256,
                            send.RequestStartedAt),
                        null);
                }

                return new RouteExecution(
                    RoutedHttpEvidence.Create(
                        _runIdentity,
                        requestOrdinal,
                        attemptOrdinal,
                        hops,
                        new RedirectTargetUnobservedHttpRouteOutcome(
                            logicalRequestSha256,
                            send.RequestStartedAt)),
                    null,
                    null,
                    null);
            }

            using var outbound = send.OutboundRequest
                ?? throw new InvalidOperationException("A response lost its outbound request owner.");
            using var response = send.Response;
            // NonValidated must be the first header access. In particular, reading Location through
            // a typed accessor would replace the API-visible publisher value before we retained it.
            // This raw snapshot deliberately precedes contract validation: a malformed bounded
            // projection cannot make lawfully received entity bytes disappear before custody.
            var rawHeaders = SnapshotHeaders(response);
            var status = (int)response.StatusCode;
            ulong? declaredLength = status is 204 or 304
                ? null
                : rawHeaders.TransferEncoding.Count == 0 &&
                  TryGetRawDeclaredContentLength(rawHeaders.ContentLength, out var parsedLength)
                    ? parsedLength
                    : null;
            var capture = await CaptureBodyAsync(
                response.Content,
                declaredLength,
                cancellationToken,
                _profile.MaximumResponseBytes).ConfigureAwait(false);
            var terminalObservedAt = Timestamp(_timeProvider.GetUtcNow());
            var observationId = $"urn:uuid:{Guid.NewGuid():D}";
            var hopOrdinal = checked((ulong)hops.Count);
            var (custodyKey, held) = await HoldAndResolveAsync(
                capture.Bytes,
                requestOrdinal,
                attemptOrdinal,
                hopOrdinal,
                observationId,
                new HeldCausalFacts(
                    currentRequest.Uri,
                    status,
                    logicalRequestSha256,
                    rawHeaders.Location)).ConfigureAwait(false);

            if (response.Version != HttpVersion.Version11 || status is < 200 or > 599)
            {
                // Custody precedes refusal. These headers did arrive, but /4 cannot truthfully encode
                // this negotiated protocol or status domain, so never relabel it as a pre-header loss.
                var failureClass = response.Version != HttpVersion.Version11
                    ? PostHeaderFailureClass.UnsupportedNegotiatedProtocol
                    : PostHeaderFailureClass.UnsupportedStatus;
                return new RouteExecution(
                    null,
                    custodyKey,
                    null,
                    new PostHeaderFailure(
                        failureClass,
                        logicalRequestSha256,
                        send.RequestStartedAt,
                        custodyKey,
                        hops.ToArray(),
                        held.Receipt.Reference.ContentSha256,
                        held.ReceiptSha256));
            }

            RoutedHttpResponseHeaders headers;
            try
            {
                headers = ProjectHeaders(rawHeaders);
            }
            catch (ArgumentException)
            {
                return new RouteExecution(
                    null,
                    custodyKey,
                    null,
                    new PostHeaderFailure(
                        PostHeaderFailureClass.HeaderProjectionRejected,
                        logicalRequestSha256,
                        send.RequestStartedAt,
                        custodyKey,
                        hops.ToArray(),
                        held.Receipt.Reference.ContentSha256,
                        held.ReceiptSha256));
            }

            RoutedHttpCompletion completion;
            try
            {
                completion = ClassifyCompletion(currentRequest, status, headers, capture);
            }
            catch (InvalidOperationException) when (!_usesPinnedHandler)
            {
                // Custody-before-decode applies to adapter authority too: an injected handler can
                // never warrant chunked EOF, but its already received bytes remain held evidence.
                return new RouteExecution(
                    null,
                    custodyKey,
                    null,
                    new PostHeaderFailure(
                        PostHeaderFailureClass.AdapterIdentityRejected,
                        logicalRequestSha256,
                        send.RequestStartedAt,
                        custodyKey,
                        hops.ToArray(),
                        held.Receipt.Reference.ContentSha256,
                        held.ReceiptSha256));
            }

            RoutedHttpHop hop;
            try
            {
                hop = RoutedHttpHop.Create(
                    hopOrdinal,
                    observationId,
                    antecedentObservationId,
                    logicalRequestSha256,
                    currentRequest.Uri,
                    status,
                    headers,
                    send.RequestStartedAt,
                    terminalObservedAt,
                    completion,
                    checked((ulong)held.Bytes.Length),
                    held.Receipt.Reference.ContentSha256,
                    held.ReceiptSha256,
                    checked((ulong)held.Bytes.Length),
                    Hash(held.Bytes.Span));
            }
            catch (ArgumentException)
            {
                // Custody precedes representation. If the classifier and the validator ever
                // disagree again, the publisher's bytes and the route context stay typed evidence
                // rather than an exception that loses both.
                return new RouteExecution(
                    null,
                    custodyKey,
                    null,
                    new PostHeaderFailure(
                        PostHeaderFailureClass.HopRepresentationRejected,
                        logicalRequestSha256,
                        send.RequestStartedAt,
                        custodyKey,
                        hops.ToArray(),
                        held.Receipt.Reference.ContentSha256,
                        held.ReceiptSha256));
            }

            hops.Add(hop);
            antecedentObservationId = observationId;
            if (completion is IncompleteHttpCompletion)
            {
                return new RouteExecution(
                    RoutedHttpEvidence.Create(
                        _runIdentity,
                        requestOrdinal,
                        attemptOrdinal,
                        hops,
                        new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.HopIncomplete)),
                    custodyKey,
                    null,
                    null);
            }

            if (hop.StatusDisposition != HttpStatusDisposition.RedirectObserved)
            {
                if (robotsRoute is not null && status is >= 400 and <= 599)
                {
                    // RFC 9309 permits access after a 4xx and requires complete disallow after a
                    // 5xx. Lex deliberately declines the 4xx permission and the optional 30-day
                    // escalation. Neither transient class asserts that the reviewed route moved.
                    return new RouteExecution(
                        RoutedHttpEvidence.Create(
                            _runIdentity,
                            requestOrdinal,
                            attemptOrdinal,
                            hops,
                            new IncompleteHttpRouteOutcome(
                                status <= 499
                                    ? HttpRouteIncompleteReason.RobotsPolicyUnavailable
                                    : HttpRouteIncompleteReason.PublisherServerFailure)),
                        custodyKey,
                        null,
                        null);
                }

                if (robotsRoute is not null &&
                    (hops.Count > robotsRoute.Steps.Count ||
                     status != robotsRoute.Steps[hops.Count - 1].ExpectedStatusCode ||
                     hops.Count != robotsRoute.Steps.Count))
                {
                    return new RouteExecution(
                        RoutedHttpEvidence.Create(
                            _runIdentity,
                            requestOrdinal,
                            attemptOrdinal,
                            hops,
                            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale)),
                        custodyKey,
                        null,
                        null);
                }

                return new RouteExecution(
                    RoutedHttpEvidence.Create(
                        _runIdentity,
                        requestOrdinal,
                        attemptOrdinal,
                        hops,
                        new CompleteHttpRouteOutcome()),
                    custodyKey,
                    null,
                    null);
            }

            if (!TryCreateRedirectRequest(currentRequest, headers.Location, out var nextRequest))
            {
                return new RouteExecution(
                    RoutedHttpEvidence.Create(
                        _runIdentity,
                        requestOrdinal,
                        attemptOrdinal,
                        hops,
                        new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectRefused)),
                    custodyKey,
                    null,
                    null);
            }

            var nextUri = nextRequest.Uri;

            if (hops.Any(existing => string.Equals(existing.RequestUri, nextUri, StringComparison.Ordinal)))
            {
                return new RouteExecution(
                    RoutedHttpEvidence.Create(
                        _runIdentity,
                        requestOrdinal,
                        attemptOrdinal,
                        hops,
                        new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectLoop)),
                    custodyKey,
                    null,
                    null);
            }

            if (hops.Count == 6)
            {
                return new RouteExecution(
                    RoutedHttpEvidence.Create(
                        _runIdentity,
                        requestOrdinal,
                        attemptOrdinal,
                        hops,
                        new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectLimitExceeded)),
                    custodyKey,
                    null,
                    null);
            }

            var stepIndex = hops.Count - 1;
            if (robotsRoute is null ||
                stepIndex >= robotsRoute.Steps.Count ||
                status != robotsRoute.Steps[stepIndex].ExpectedStatusCode ||
                !string.Equals(
                    robotsRoute.Steps[stepIndex].ExpectedLocation,
                    nextUri,
                    StringComparison.Ordinal))
            {
                return new RouteExecution(
                    RoutedHttpEvidence.Create(
                        _runIdentity,
                        requestOrdinal,
                        attemptOrdinal,
                        hops,
                        new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale)),
                    custodyKey,
                    null,
                    null);
            }

            var nextLogicalRequestBytes = nextRequest.CopyCanonicalBytes();
            _ = HttpLogicalRequest.ParseAndVerify(nextLogicalRequestBytes);
            var nextLogicalRequestSha256 = Hash(nextLogicalRequestBytes);
            RegisterRetainedHop(custodyKey, hop);
            var antecedentCapability = OpenRedirectAntecedent(custodyKey);
            // The antecedent's own route policy is passed from currentRequest, which is still the
            // request that produced the hop being redirected from, not the successor. In every
            // route today the two digests are equal, because one redirect policy governs a whole
            // route; the parameter exists so a future per-hop or per-target policy cannot pass
            // unnoticed, and it is not redundant even though nothing in the corpus can make it
            // differ yet.
            lease = SendLease.FromRedirect(
                this,
                nextRequest,
                requestBody,
                requestOrdinal,
                attemptOrdinal,
                checked((ulong)hops.Count),
                currentRequest.RedirectPolicySha256,
                antecedentCapability);
            currentRequest = nextRequest;
            logicalRequestSha256 = nextLogicalRequestSha256;
        }
    }
}
