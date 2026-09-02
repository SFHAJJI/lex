using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

[assembly: InternalsVisibleTo("Lex.V3.Ingest.Tests")]

namespace Lex.V3.Ingest;

// This transport stays assembly-internal until a source profile supplies an executable request
// policy. HttpRequestEvidence retains a policy identity but does not itself prove that the target
// scheme, origin, path or robots decision was admitted.
internal sealed class BoundedHttpObservationAcquirer : IDisposable
{
    private readonly HttpClient _client;
    private readonly ICustodyStore _custodyStore;
    private readonly long _maximumResponseBytes;
    private readonly TimeSpan _headersTimeout;
    private readonly TimeSpan _bodyTimeout;
    private readonly TimeProvider _timeProvider;

    internal BoundedHttpObservationAcquirer(
        ICustodyStore custodyStore,
        TimeSpan requestTimeout,
        long maximumResponseBytes = CustodyBounds.MaxObjectBytes)
        : this(
            CreateHandler(),
            custodyStore,
            maximumResponseBytes,
            requestTimeout,
            requestTimeout,
            TimeProvider.System)
    {
    }

    internal BoundedHttpObservationAcquirer(
        HttpMessageHandler handler,
        ICustodyStore custodyStore,
        long maximumResponseBytes,
        TimeSpan headersTimeout,
        TimeSpan bodyTimeout,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (maximumResponseBytes is <= 0 or > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        }

        RequireTimeout(headersTimeout, nameof(headersTimeout));
        RequireTimeout(bodyTimeout, nameof(bodyTimeout));
        _custodyStore = custodyStore;
        _maximumResponseBytes = maximumResponseBytes;
        _headersTimeout = headersTimeout;
        _bodyTimeout = bodyTimeout;
        _timeProvider = timeProvider;
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        ActivityHeadersPropagator = null,
        MaxResponseDrainSize = 0,
        UseCookies = false,
        UseProxy = false,
    };

    public async Task<HttpObservation> AcquireAsync(
        BoundMachineRequest boundRequest,
        HttpRequestTemplate requestTemplate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundRequest);
        ArgumentNullException.ThrowIfNull(requestTemplate);
        cancellationToken.ThrowIfCancellationRequested();
        if (requestTemplate.RenderReceipt != boundRequest.RenderReceipt ||
            !string.Equals(
                requestTemplate.RequestedUri,
                boundRequest.RequestedUri,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The HTTP request template must describe the exact bound machine request.",
                nameof(requestTemplate));
        }

        var requestBody = boundRequest.CopyVerifiedRequestBody();

        var method = requestTemplate.Method == HttpRequestMethod.Get
            ? HttpMethod.Get
            : HttpMethod.Post;
        using var outbound = new HttpRequestMessage(method, requestTemplate.RequestedUri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        if (requestTemplate.Method == HttpRequestMethod.Post)
        {
            var contentType = requestTemplate.RenderReceipt.ContentType
                ?? throw new ArgumentException(
                    "A POST render receipt requires a content type.",
                    nameof(requestTemplate));
            outbound.Content = new ByteArrayContent(requestBody);
            outbound.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType.MemberKey);
            if (requestTemplate.RenderReceipt.Charset is not null)
            {
                outbound.Content.Headers.ContentType.CharSet = "utf-8";
            }
        }

        outbound.Headers.UserAgent.ParseAdd(OutboundCrawlerIdentity.Token);

        var observationId = $"urn:uuid:{Guid.NewGuid():D}";
        var sendElapsed = Stopwatch.StartNew();
        var sendCancellation = new CancellationTokenSource();
        var sendCancellationOwnershipTransferred = false;
        HttpResponseMessage response;
        var request = HttpRequestEvidence.CreateAtSend(
            requestTemplate,
            _timeProvider);
        var sendTask = _client.SendAsync(
            outbound,
            HttpCompletionOption.ResponseHeadersRead,
            sendCancellation.Token);
        try
        {
            response = await sendTask.WaitAsync(
                _headersTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            sendCancellationOwnershipTransferred = true;
            CancelAndDisposeOffPath(sendCancellation);
            DisposeLateResponse(sendTask);
            return BeforeHeadersFailure(
                observationId,
                request,
                HttpPreHeaderFailureClass.HeaderDeadline,
                sendElapsed.ElapsedMilliseconds);
        }
        catch (OperationCanceledException exception) when (
            cancellationToken.CanBeCanceled &&
            cancellationToken.IsCancellationRequested &&
            exception.CancellationToken == cancellationToken)
        {
            sendCancellationOwnershipTransferred = true;
            CancelAndDisposeOffPath(sendCancellation);
            DisposeLateResponse(sendTask);
            throw;
        }
        catch (OperationCanceledException)
        {
            return BeforeHeadersFailure(
                observationId,
                request,
                HttpPreHeaderFailureClass.TransportBeforeHeaders,
                sendElapsed.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            return BeforeHeadersFailure(
                observationId,
                request,
                HttpPreHeaderFailureClass.TransportBeforeHeaders,
                sendElapsed.ElapsedMilliseconds);
        }
        finally
        {
            if (!sendCancellationOwnershipTransferred)
            {
                sendCancellation.Dispose();
            }
        }

        HttpResponseMetadata metadata;
        string effectiveUri;
        int statusCode;
        HttpStatusDisposition statusDisposition;
        BodyCapture capture;
        try
        {
            metadata = RetainMetadata(response);
            effectiveUri = response.RequestMessage?.RequestUri?.AbsoluteUri
                ?? request.RequestedUri;
            statusCode = (int)response.StatusCode;
            statusDisposition = HttpStatusClassifier.Classify(statusCode, metadata);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                throw new NotSupportedException(
                    "A 304 requires exact predecessor, validator and request-key evidence.");
            }

            if (HttpResponseFraming.IsHeaderTerminatedStatus(statusCode))
            {
                return new ResponseWithoutBodyObservation(
                    HttpObservationSchemaIds.HttpObservation,
                    observationId,
                    request,
                    effectiveUri,
                    statusCode,
                    statusDisposition,
                    metadata,
                    receivedEncodedEntityByteCount: 0,
                    HttpNoBodyReason.FramingForbidsBody,
                    zeroOctetCompletionEvidence: null);
            }

            var hasTransferCoding = metadata.TransferEncoding is not AbsentHttpHeader;
            var transferCodingConflict = hasTransferCoding &&
                metadata.ContentLength is not AbsentHttpHeader;
            long? declaredLength = TryGetDeclaredContentLength(
                metadata.ContentLength,
                out var retainedLength)
                ? retainedLength
                : null;
            if (!hasTransferCoding && declaredLength == 0)
            {
                var semanticNoEntity = statusCode == (int)HttpStatusCode.ResetContent;
                return new ResponseWithoutBodyObservation(
                    HttpObservationSchemaIds.HttpObservation,
                    observationId,
                    request,
                    effectiveUri,
                    statusCode,
                    statusDisposition,
                    metadata,
                    receivedEncodedEntityByteCount: 0,
                    semanticNoEntity
                        ? HttpNoBodyReason.SemanticNoEntity
                        : HttpNoBodyReason.CompleteZeroOctetEntity,
                    semanticNoEntity
                        ? null
                        : new DeclaredZeroOctetContentLengthCompleteEvidence(
                            TransferCompletionSchemaIds.ZeroOctetTransferCompletionEvidence,
                            request.AdapterIdentity,
                            observationId));
            }

            capture = await CaptureBodyAsync(
                response.Content,
                declaredLength,
                hasTransferCoding,
                transferCodingConflict,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DisposeResponseOffPath(response);
        }

        if (capture.Outcome != BodyCaptureOutcome.Complete)
        {
            var capturedReceipt = capture.Bytes.Length == 0
                ? null
                : await HoldCheckedAsync(capture.Bytes).ConfigureAwait(false);
            if (capture.Outcome is BodyCaptureOutcome.MissingCompletionProof or
                BodyCaptureOutcome.TransferCodingConflict)
            {
                return new ResponseCompletionUnprovenObservation(
                    HttpObservationSchemaIds.HttpObservation,
                    observationId,
                    request,
                    effectiveUri,
                    statusCode,
                    statusDisposition,
                    metadata,
                    capture.Bytes.LongLength,
                    _maximumResponseBytes,
                    HttpAcquisitionReasonRegistry.Member(
                        ToCompletionUnprovenReason(capture.Outcome)),
                    capturedReceipt);
            }

            return new ResponsePartialBodyObservation(
                HttpObservationSchemaIds.HttpObservation,
                observationId,
                request,
                effectiveUri,
                statusCode,
                statusDisposition,
                metadata,
                capture.Bytes.LongLength,
                _maximumResponseBytes,
                HttpAcquisitionReasonRegistry.Member(ToPartialReason(capture.Outcome)),
                capturedReceipt);
        }

        var receipt = await HoldCheckedAsync(capture.Bytes).ConfigureAwait(false);

        var bodyCompletion = new DeclaredContentLengthCompleteEvidence(
            TransferCompletionSchemaIds.TransferCompletionEvidence,
            request.AdapterIdentity,
            observationId,
            receipt.Reference.ContentSha256,
            receipt.Reference.ByteLength);
        return new ResponseCompleteBodyObservation(
            HttpObservationSchemaIds.HttpObservation,
            observationId,
            request,
            effectiveUri,
            statusCode,
            statusDisposition,
            metadata,
            bodyCompletion,
            receipt);
    }

    public void Dispose() => _client.Dispose();

    private static void CancelAndDisposeOffPath(CancellationTokenSource source) =>
        _ = CancelAndDisposeAsync(source);

    private static async Task CancelAndDisposeAsync(CancellationTokenSource source)
    {
        await Task.Yield();
        try
        {
            await source.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // Cancellation callback faults cannot hold or replace the typed transport outcome.
        }
        finally
        {
            source.Dispose();
        }
    }

    private static void DisposeLateResponse(Task<HttpResponseMessage> task) =>
        _ = DisposeLateResponseAsync(task);

    private static async Task DisposeLateResponseAsync(Task<HttpResponseMessage> task)
    {
        await Task.Yield();
        try
        {
            using var response = await task.ConfigureAwait(false);
        }
        catch
        {
            // The transport outcome already captured the failure; this only owns late cleanup.
        }
    }

    private static void DisposeResponseOffPath(HttpResponseMessage response) =>
        _ = DisposeResponseAsync(response);

    private static async Task DisposeResponseAsync(HttpResponseMessage response)
    {
        await Task.Yield();
        try
        {
            response.Dispose();
        }
        catch
        {
            // Cleanup faults occur after the typed result and cannot rewrite it.
        }
    }

    private static void DisposeLateStream(Task<Stream> task) =>
        _ = DisposeLateStreamAsync(task);

    private static async Task DisposeLateStreamAsync(Task<Stream> task)
    {
        await Task.Yield();
        try
        {
            using var stream = await task.ConfigureAwait(false);
        }
        catch
        {
            // A bounded result already owns classification; this only closes a late stream.
        }
    }

    private static void ObserveLateRead(Task<int> task) => _ = ObserveLateReadAsync(task);

    private static async Task ObserveLateReadAsync(Task<int> task)
    {
        await Task.Yield();
        try
        {
            _ = await task.ConfigureAwait(false);
        }
        catch
        {
            // The bounded result owns the failure classification; observe only the late task.
        }
    }

    private async Task<BodyCapture> CaptureBodyAsync(
        HttpContent content,
        long? declaredLength,
        bool hasTransferCoding,
        bool transferCodingConflict,
        CancellationToken callerCancellationToken)
    {
        var bodyElapsed = Stopwatch.StartNew();
        var bodyCancellation = new CancellationTokenSource();
        var bodyCancellationOwnershipTransferred = false;
        try
        {
            if (callerCancellationToken.IsCancellationRequested)
            {
                return new BodyCapture([], BodyCaptureOutcome.CallerCancelledAfterHeaders);
            }

            Task<Stream>? openTask = null;
            Stream stream;
            try
            {
                openTask = content.ReadAsStreamAsync(bodyCancellation.Token);
                stream = await openTask.WaitAsync(
                    _bodyTimeout,
                    callerCancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                bodyCancellationOwnershipTransferred = true;
                CancelAndDisposeOffPath(bodyCancellation);
                if (openTask is not null)
                {
                    DisposeLateStream(openTask);
                }
                return new BodyCapture(
                    [],
                    BodyCaptureOutcome.BodyDeadline);
            }
            catch (OperationCanceledException exception) when (
                callerCancellationToken.CanBeCanceled &&
                callerCancellationToken.IsCancellationRequested &&
                exception.CancellationToken == callerCancellationToken)
            {
                bodyCancellationOwnershipTransferred = true;
                CancelAndDisposeOffPath(bodyCancellation);
                if (openTask is not null)
                {
                    DisposeLateStream(openTask);
                }
                return new BodyCapture(
                    [],
                    BodyCaptureOutcome.CallerCancelledAfterHeaders);
            }
            catch (OperationCanceledException)
            {
                return new BodyCapture([], BodyCaptureOutcome.BodyReadFailure);
            }
            catch (IOException)
            {
                return new BodyCapture([], BodyCaptureOutcome.BodyReadFailure);
            }
            catch (HttpRequestException)
            {
                return new BodyCapture([], BodyCaptureOutcome.BodyReadFailure);
            }

            var capture = await ReadBoundedAsync(
                stream,
                declaredLength,
                hasTransferCoding,
                transferCodingConflict,
                bodyCancellation,
                callerCancellationToken,
                bodyElapsed).ConfigureAwait(false);
            if (capture.CancellationCleanupRequired)
            {
                bodyCancellationOwnershipTransferred = true;
                CancelAndDisposeOffPath(bodyCancellation);
            }

            return capture;
        }
        finally
        {
            if (!bodyCancellationOwnershipTransferred)
            {
                bodyCancellation.Dispose();
            }
        }
    }

    private async Task<BodyCapture> ReadBoundedAsync(
        Stream stream,
        long? declaredLength,
        bool hasTransferCoding,
        bool transferCodingConflict,
        CancellationTokenSource bodyCancellation,
        CancellationToken callerCancellationToken,
        Stopwatch bodyElapsed)
    {
        using var destination = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var unprovenFraming = hasTransferCoding || !declaredLength.HasValue;
        var targetLength = unprovenFraming
            ? _maximumResponseBytes
            : Math.Min(declaredLength!.Value, _maximumResponseBytes);
        while (true)
        {
            if (!unprovenFraming && destination.Length == declaredLength)
            {
                return new BodyCapture(destination.ToArray(), BodyCaptureOutcome.Complete);
            }

            if (destination.Length == _maximumResponseBytes)
            {
                return new BodyCapture(
                    destination.ToArray(),
                    BodyCaptureOutcome.ByteBoundPreventedCompletion);
            }

            if (callerCancellationToken.IsCancellationRequested)
            {
                return new BodyCapture(
                    destination.ToArray(),
                    BodyCaptureOutcome.CallerCancelledAfterHeaders);
            }

            var requested = (int)Math.Min(buffer.Length, targetLength - destination.Length);
            int read;
            var remaining = _bodyTimeout - bodyElapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return new BodyCapture(
                    destination.ToArray(),
                    BodyCaptureOutcome.BodyDeadline,
                    CancellationCleanupRequired: true);
            }

            Task<int>? readTask = null;
            try
            {
                readTask = stream.ReadAsync(
                    buffer.AsMemory(0, requested),
                    bodyCancellation.Token).AsTask();
                read = await readTask.WaitAsync(
                    remaining,
                    callerCancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (readTask is not null)
                {
                    ObserveLateRead(readTask);
                }
                return new BodyCapture(
                    destination.ToArray(),
                    BodyCaptureOutcome.BodyDeadline,
                    CancellationCleanupRequired: true);
            }
            catch (OperationCanceledException exception) when (
                callerCancellationToken.CanBeCanceled &&
                callerCancellationToken.IsCancellationRequested &&
                exception.CancellationToken == callerCancellationToken)
            {
                if (readTask is not null)
                {
                    ObserveLateRead(readTask);
                }
                return new BodyCapture(
                    destination.ToArray(),
                    BodyCaptureOutcome.CallerCancelledAfterHeaders,
                    CancellationCleanupRequired: true);
            }
            catch (OperationCanceledException)
            {
                return new BodyCapture(
                    destination.ToArray(),
                    BodyCaptureOutcome.BodyReadFailure);
            }
            catch (IOException)
            {
                return new BodyCapture(
                    destination.ToArray(),
                    BodyCaptureOutcome.BodyReadFailure);
            }
            catch (HttpRequestException)
            {
                return new BodyCapture(
                    destination.ToArray(),
                    BodyCaptureOutcome.BodyReadFailure);
            }

            if (read == 0)
            {
                if (unprovenFraming)
                {
                    return new BodyCapture(
                        destination.ToArray(),
                        transferCodingConflict
                            ? BodyCaptureOutcome.TransferCodingConflict
                            : BodyCaptureOutcome.MissingCompletionProof);
                }

                return new BodyCapture(
                    destination.ToArray(),
                    BodyCaptureOutcome.DeclaredLengthShortRead);
            }

            destination.Write(buffer, 0, read);
        }
    }

    private async Task<DurableBlobWriteReceipt> HoldCheckedAsync(byte[] bytes)
    {
        using var evidenceDeadline = new CancellationTokenSource(_bodyTimeout);
        try
        {
            var frozen = new ReadOnlyMemory<byte>(bytes);
            var capturedDigest = CustodyDigest.Of(frozen.Span, evidenceDeadline.Token);
            var createTask = Task.Run(
                () => _custodyStore.CreateAsync(
                    frozen,
                    CustodyClass.NightlyFloor90d,
                    evidenceDeadline.Token),
                CancellationToken.None);
            var receipt = await WaitForCustodyAsync(
                createTask,
                evidenceDeadline.Token).ConfigureAwait(false);
            if (receipt is null ||
                receipt.Reference.CustodyClass != CustodyClass.NightlyFloor90d ||
                receipt.Reference.ByteLength != frozen.Length ||
                !string.Equals(
                    receipt.Reference.ContentSha256,
                    capturedDigest,
                    StringComparison.Ordinal))
            {
                throw new CustodyIntegrityException(
                    "The durable receipt does not bind the exact acquired entity bytes.");
            }

            var restoreTask = Task.Run(
                () => CustodyRestore.ReadCheckedAsync(
                    _custodyStore,
                    receipt.Reference,
                    evidenceDeadline.Token),
                CancellationToken.None);
            var restored = await WaitForCustodyAsync(
                restoreTask,
                evidenceDeadline.Token).ConfigureAwait(false);
            if (!restored.Span.SequenceEqual(frozen.Span))
            {
                throw new CustodyIntegrityException(
                    "The restored entity bytes differ from the bytes accepted from the publisher.");
            }

            return receipt;
        }
        catch (OperationCanceledException exception) when (evidenceDeadline.IsCancellationRequested)
        {
            throw new CustodyRequiredException(
                "Durable custody did not complete within the evidence deadline.",
                exception);
        }
        catch (Exception exception)
            when (exception is not (CustodyRequiredException
                or CustodyIntegrityException
                or CustodyPolicyException))
        {
            throw new CustodyRequiredException(
                "The acquired bytes did not complete checked durable custody.",
                exception);
        }
    }

    private static async Task<T> WaitForCustodyAsync<T>(
        Task<T> task,
        CancellationToken deadlineToken)
    {
        try
        {
            deadlineToken.ThrowIfCancellationRequested();
            var result = await task.WaitAsync(deadlineToken).ConfigureAwait(false);
            deadlineToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException exception) when (deadlineToken.IsCancellationRequested)
        {
            ObserveLateCustody(task);
            throw new CustodyRequiredException(
                "Durable custody did not complete within the evidence deadline.",
                exception);
        }
    }

    private static void ObserveLateCustody<T>(Task<T> task) =>
        _ = ObserveLateCustodyAsync(task);

    private static async Task ObserveLateCustodyAsync<T>(Task<T> task)
    {
        await Task.Yield();
        try
        {
            _ = await task.ConfigureAwait(false);
        }
        catch
        {
            // The caller received the bounded custody failure; observe only the late task.
        }
    }

    private static TransportFailureBeforeBodyObservation BeforeHeadersFailure(
        string observationId,
        HttpRequestEvidence request,
        HttpPreHeaderFailureClass failureClass,
        long elapsedMilliseconds) => new(
            HttpObservationSchemaIds.HttpObservation,
            observationId,
            request,
            HttpAcquisitionReasonRegistry.Member(failureClass),
            (int)Math.Min(elapsedMilliseconds, int.MaxValue));

    private static HttpPartialBodyReason ToPartialReason(BodyCaptureOutcome outcome) => outcome switch
    {
        BodyCaptureOutcome.DeclaredLengthShortRead => HttpPartialBodyReason.DeclaredLengthShortRead,
        BodyCaptureOutcome.ByteBoundPreventedCompletion =>
            HttpPartialBodyReason.ByteBoundPreventedCompletion,
        BodyCaptureOutcome.BodyDeadline => HttpPartialBodyReason.BodyDeadline,
        BodyCaptureOutcome.BodyReadFailure => HttpPartialBodyReason.BodyReadFailure,
        BodyCaptureOutcome.CallerCancelledAfterHeaders =>
            HttpPartialBodyReason.CallerCancelledAfterHeaders,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static HttpCompletionUnprovenReason ToCompletionUnprovenReason(
        BodyCaptureOutcome outcome) => outcome switch
        {
            BodyCaptureOutcome.MissingCompletionProof =>
                HttpCompletionUnprovenReason.MissingCompletionProof,
            BodyCaptureOutcome.TransferCodingConflict =>
                HttpCompletionUnprovenReason.TransferCodingConflict,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    private static HttpResponseMetadata RetainMetadata(HttpResponseMessage response)
    {
        var contentTypes = HeaderValues(response, "Content-Type");
        return new HttpResponseMetadata(
            ToField(contentTypes),
            ToField(DeclaredCharsets(contentTypes)),
            ToField(HeaderValues(response, "Content-Length")),
            ToField(HeaderValues(response, "Content-Encoding")),
            ToField(HeaderValues(response, "Transfer-Encoding")),
            ToField(HeaderValues(response, "Content-Range")),
            ToField(HeaderValues(response, "ETag")),
            ToField(HeaderValues(response, "Last-Modified")));
    }

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

    private static IReadOnlyList<string> DeclaredCharsets(IReadOnlyList<string> contentTypes)
    {
        var charsets = new List<string>();
        foreach (var value in contentTypes)
        {
            if (!MediaTypeHeaderValue.TryParse(value, out var parsed))
            {
                continue;
            }

            foreach (var parameter in parsed.Parameters)
            {
                if (string.Equals(parameter.Name, "charset", StringComparison.OrdinalIgnoreCase) &&
                    parameter.Value is { Length: > 0 } charset)
                {
                    charsets.Add(charset);
                }
            }
        }

        return charsets;
    }

    private static HttpHeaderField ToField(IReadOnlyList<string> values) => values.Count switch
    {
        0 => new AbsentHttpHeader(),
        1 => new SingleHttpHeader(values[0]),
        _ => new MultipleHttpHeader(values),
    };

    private static bool TryGetDeclaredContentLength(HttpHeaderField field, out long length)
    {
        length = 0;
        return field is SingleHttpHeader single &&
            single.Value.Length > 0 &&
            single.Value.All(static character => character is >= '0' and <= '9') &&
            long.TryParse(
                single.Value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out length);
    }

    private static void RequireTimeout(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private sealed record BodyCapture(
        byte[] Bytes,
        BodyCaptureOutcome Outcome,
        bool CancellationCleanupRequired = false);

    private enum BodyCaptureOutcome
    {
        Complete = 1,
        DeclaredLengthShortRead = 2,
        ByteBoundPreventedCompletion = 3,
        TransferCodingConflict = 4,
        MissingCompletionProof = 5,
        BodyDeadline = 6,
        BodyReadFailure = 7,
        CallerCancelledAfterHeaders = 8,
    }
}
