using System.Net;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class BoundedHttpObservationLifecycleTests
{
    private static readonly byte[] EntityBytes = Encoding.ASCII.GetBytes("AB");
    private static readonly TimeSpan OperationDeadline = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan GuardDeadline = TimeSpan.FromSeconds(2);

    [TestMethod]
    [DataRow("io")]
    [DataRow("cancellation")]
    [DataRow("http")]
    public async Task SynchronousBodyOpenFailureRetainsTheObservedResponse(string failureKind)
    {
        using var caller = new CancellationTokenSource();
        using var provider = new CancellationTokenSource();
        provider.Cancel();
        var content = new SynchronousThrowContent(
            Failure(failureKind, provider.Token),
            caller.Cancel);
        content.Headers.ContentLength = 5;
        var custody = new ControlledCustodyStore(CustodyBlock.None);
        using var acquirer = Acquirer(Response(content), custody);

        var result = await acquirer.AcquireAsync(RequestTemplate(), caller.Token);

        AssertBodyReadFailure(result, string.Empty, custody);
    }

    [TestMethod]
    [DataRow("io")]
    [DataRow("cancellation")]
    [DataRow("http")]
    public async Task SynchronousReadFailureRetainsThePositivePrefix(string failureKind)
    {
        using var caller = new CancellationTokenSource();
        using var provider = new CancellationTokenSource();
        provider.Cancel();
        var stream = new SynchronousThrowAfterPrefixStream(
            EntityBytes,
            Failure(failureKind, provider.Token),
            caller.Cancel);
        var custody = new ControlledCustodyStore(CustodyBlock.None);
        using var acquirer = Acquirer(Response(stream, declaredLength: 5), custody);

        var result = await acquirer.AcquireAsync(RequestTemplate(), caller.Token);

        AssertBodyReadFailure(result, "AB", custody);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PendingBodyOpenDoesNotOwnResponseCleanup(bool cancelCaller)
    {
        using var caller = new CancellationTokenSource();
        var content = new PendingOpenContent();
        content.Headers.ContentLength = 5;
        var custody = new ControlledCustodyStore(CustodyBlock.None);
        using var acquirer = Acquirer(
            Response(content),
            custody,
            bodyTimeout: cancelCaller
                ? GuardDeadline + TimeSpan.FromSeconds(1)
                : OperationDeadline);

        var acquisition = acquirer.AcquireAsync(RequestTemplate(), caller.Token);
        await RequireCompletion(content.OpenStarted, "The body opener was not reached.");
        if (cancelCaller)
        {
            caller.Cancel();
        }

        var result = await RequireCompletion(
            acquisition,
            "Body acquisition remained coupled to the pending opener.");
        var partial = result as ResponsePartialBodyObservation;
        Assert.IsNotNull(partial);
        Assert.AreEqual(
            cancelCaller
                ? HttpPartialBodyReason.CallerCancelledAfterHeaders
                : HttpPartialBodyReason.BodyDeadline,
            HttpAcquisitionReasonRegistry.RequirePartial(partial.TerminalFailureReason));

        var lateStream = new DisposalTrackingStream();
        try
        {
            await RequireCompletion(
                content.Disposed,
                "The response was not disposed before the pending opener completed.");
        }
        finally
        {
            content.CompleteOpen(lateStream);
        }

        await RequireCompletion(
            lateStream.Disposed,
            "A stream produced after response disposal was not disposed.");
    }

    [TestMethod]
    public async Task DeclaredZeroCompletesFromHeadersWithoutOpeningTheBody()
    {
        var content = new PendingOpenContent();
        content.Headers.ContentLength = 0;
        var custody = new ControlledCustodyStore(CustodyBlock.None);
        using var acquirer = Acquirer(
            Response(content),
            custody,
            bodyTimeout: OperationDeadline);

        var result = await RequireCompletion(
            acquirer.AcquireAsync(RequestTemplate(), CancellationToken.None),
            "A declared-zero response waited for a body stream it does not have.");

        var withoutBody = result as ResponseWithoutBodyObservation;
        Assert.IsNotNull(withoutBody);
        Assert.AreEqual(HttpNoBodyReason.CompleteZeroOctetEntity, withoutBody.Reason);
        Assert.IsFalse(content.OpenStarted.IsCompleted);
        Assert.AreEqual(0, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    public async Task CancellationIgnoringCustodyCreateCannotHoldAcquisition()
    {
        using var caller = new CancellationTokenSource();
        var custody = new ControlledCustodyStore(CustodyBlock.Create);
        using var acquirer = Acquirer(
            Response(new MemoryStream(EntityBytes), declaredLength: EntityBytes.Length),
            custody,
            bodyTimeout: OperationDeadline);

        var acquisition = acquirer.AcquireAsync(RequestTemplate(), caller.Token);
        await RequireCompletion(custody.CreateStarted, "Custody create was not reached.");
        var winner = await Task.WhenAny(acquisition, Task.Delay(GuardDeadline));
        if (!ReferenceEquals(winner, acquisition))
        {
            custody.ReleaseCreate();
            await ObserveForTestCleanup(acquisition);
            Assert.Fail("A custody create that ignored cancellation held acquisition past its deadline.");
        }

        try
        {
            await Assert.ThrowsExactlyAsync<CustodyRequiredException>(async () =>
                _ = await acquisition);
        }
        finally
        {
            custody.ReleaseCreate();
        }

        Assert.AreNotEqual(caller.Token, custody.CreateToken);
        Assert.IsTrue(custody.CreateToken.IsCancellationRequested);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    public async Task CancellationIgnoringCustodyReadUsesTheSameBoundedIndependentToken()
    {
        using var caller = new CancellationTokenSource();
        var custody = new ControlledCustodyStore(CustodyBlock.Read);
        using var acquirer = Acquirer(
            Response(new MemoryStream(EntityBytes), declaredLength: EntityBytes.Length),
            custody,
            bodyTimeout: OperationDeadline);

        var acquisition = acquirer.AcquireAsync(RequestTemplate(), caller.Token);
        await RequireCompletion(custody.ReadStarted, "Custody readback was not reached.");
        var winner = await Task.WhenAny(acquisition, Task.Delay(GuardDeadline));
        if (!ReferenceEquals(winner, acquisition))
        {
            custody.ReleaseRead();
            await ObserveForTestCleanup(acquisition);
            Assert.Fail("A custody read that ignored cancellation held acquisition past its deadline.");
        }

        try
        {
            await Assert.ThrowsExactlyAsync<CustodyRequiredException>(async () =>
                _ = await acquisition);
        }
        finally
        {
            custody.ReleaseRead();
        }

        Assert.AreNotEqual(caller.Token, custody.CreateToken);
        Assert.AreEqual(custody.CreateToken, custody.ReadToken);
        Assert.IsTrue(custody.ReadToken.IsCancellationRequested);
        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(1, custody.ReadCount);
    }

    [TestMethod]
    public async Task SynchronousCustodyCreateBeforeTaskReturnCannotHoldAcquisition()
    {
        var custody = new ControlledCustodyStore(CustodyBlock.SynchronousCreate);
        using var acquirer = Acquirer(
            Response(new MemoryStream(EntityBytes), declaredLength: EntityBytes.Length),
            custody,
            bodyTimeout: OperationDeadline);

        var acquisition = Task.Run(() =>
            acquirer.AcquireAsync(RequestTemplate(), CancellationToken.None));
        await RequireCompletion(custody.CreateStarted, "Custody create was not reached.");
        var winner = await Task.WhenAny(acquisition, Task.Delay(GuardDeadline));
        if (!ReferenceEquals(winner, acquisition))
        {
            custody.ReleaseCreate();
            await ObserveForTestCleanup(acquisition);
            Assert.Fail("Synchronous custody create held acquisition past its deadline.");
        }

        try
        {
            await Assert.ThrowsExactlyAsync<CustodyRequiredException>(async () =>
                _ = await acquisition);
        }
        finally
        {
            custody.ReleaseCreate();
        }

        Assert.IsTrue(custody.CreateToken.IsCancellationRequested);
        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    public async Task SynchronousCustodyReadBeforeTaskReturnCannotHoldAcquisition()
    {
        var custody = new ControlledCustodyStore(CustodyBlock.SynchronousRead);
        using var acquirer = Acquirer(
            Response(new MemoryStream(EntityBytes), declaredLength: EntityBytes.Length),
            custody,
            bodyTimeout: OperationDeadline);

        var acquisition = Task.Run(() =>
            acquirer.AcquireAsync(RequestTemplate(), CancellationToken.None));
        await RequireCompletion(custody.ReadStarted, "Custody read was not reached.");
        var winner = await Task.WhenAny(acquisition, Task.Delay(GuardDeadline));
        if (!ReferenceEquals(winner, acquisition))
        {
            custody.ReleaseRead();
            await ObserveForTestCleanup(acquisition);
            Assert.Fail("Synchronous custody read held acquisition past its deadline.");
        }

        try
        {
            await Assert.ThrowsExactlyAsync<CustodyRequiredException>(async () =>
                _ = await acquisition);
        }
        finally
        {
            custody.ReleaseRead();
        }

        Assert.IsTrue(custody.ReadToken.IsCancellationRequested);
        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(1, custody.ReadCount);
    }

    [TestMethod]
    [DataRow("create")]
    [DataRow("read")]
    public async Task ProviderCustodyFailureIsNormalizedToTheCustodyRequiredGate(string stage)
    {
        var custody = new ControlledCustodyStore(stage switch
        {
            "create" => CustodyBlock.CreateFailure,
            "read" => CustodyBlock.ReadFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        });
        using var acquirer = Acquirer(
            Response(new MemoryStream(EntityBytes), declaredLength: EntityBytes.Length),
            custody);

        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
            acquirer.AcquireAsync(RequestTemplate(), CancellationToken.None));

        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(stage == "read" ? 1 : 0, custody.ReadCount);
    }

    private static void AssertBodyReadFailure(
        HttpObservation result,
        string expectedBytes,
        ControlledCustodyStore custody)
    {
        var partial = result as ResponsePartialBodyObservation;
        Assert.IsNotNull(partial);
        Assert.AreEqual(
            HttpPartialBodyReason.BodyReadFailure,
            HttpAcquisitionReasonRegistry.RequirePartial(partial.TerminalFailureReason));
        Assert.AreEqual(200, partial.StatusCode);
        Assert.AreEqual(partial.Request.RequestedUri, partial.EffectiveUri);
        Assert.AreEqual(
            "5",
            Assert.IsInstanceOfType<SingleHttpHeader>(
                partial.ResponseMetadata.ContentLength).Value);
        var expected = Encoding.ASCII.GetBytes(expectedBytes);
        Assert.AreEqual(expected.Length, partial.ReceivedEncodedEntityByteCount);
        CollectionAssert.AreEqual(expected, custody.CreatedBytes);
        Assert.AreEqual(expected.Length == 0 ? 0 : 1, custody.CreateCount);
        Assert.AreEqual(expected.Length == 0 ? 0 : 1, custody.ReadCount);
    }

    private static async Task RequireCompletion(Task task, string message)
    {
        var winner = await Task.WhenAny(task, Task.Delay(GuardDeadline));
        Assert.AreSame(task, winner, message);
        await task;
    }

    private static async Task<T> RequireCompletion<T>(Task<T> task, string message)
    {
        var winner = await Task.WhenAny(task, Task.Delay(GuardDeadline));
        Assert.AreSame(task, winner, message);
        return await task;
    }

    private static async Task ObserveForTestCleanup(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // The assertion reports the boundedness defect; cleanup only drains the released task.
        }
    }

    private static Exception Failure(string kind, CancellationToken providerToken) => kind switch
    {
        "io" => new IOException("publisher reset synchronously"),
        "cancellation" => new OperationCanceledException(
            "provider cancelled synchronously",
            providerToken),
        "http" => new HttpRequestException("publisher transport failed synchronously"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static BoundedHttpObservationAcquirer Acquirer(
        HttpResponseMessage response,
        ICustodyStore custody,
        TimeSpan? bodyTimeout = null) => new(
            new RecordingHandler(response),
            custody,
            maximumResponseBytes: 1024,
            headersTimeout: TimeSpan.FromSeconds(5),
            bodyTimeout ?? TimeSpan.FromSeconds(5),
            MachineQueryEvidenceFixture.Clock());

    private static HttpResponseMessage Response(Stream stream, long? declaredLength = null)
    {
        var content = new StreamContent(stream);
        if (declaredLength.HasValue)
        {
            content.Headers.ContentLength = declaredLength.Value;
        }

        return Response(content);
    }

    private static HttpResponseMessage Response(HttpContent content) => new(HttpStatusCode.OK)
    {
        Content = content,
    };

    private static HttpRequestTemplate RequestTemplate() =>
        MachineQueryEvidenceFixture.Template();

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class SynchronousThrowContent(
        Exception failure,
        Action beforeThrow) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync(
            CancellationToken cancellationToken)
        {
            beforeThrow();
            throw failure;
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class SynchronousThrowAfterPrefixStream(
        byte[] prefix,
        Exception failure,
        Action beforeThrow) : Stream
    {
        private bool _returnedPrefix;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_returnedPrefix)
            {
                _returnedPrefix = true;
                prefix.CopyTo(buffer);
                return ValueTask.FromResult(prefix.Length);
            }

            beforeThrow();
            throw failure;
        }

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
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class PendingOpenContent : HttpContent
    {
        private readonly TaskCompletionSource<Stream> _open =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _openStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OpenStarted => _openStarted.Task;
        public Task Disposed => _disposed.Task;

        public void CompleteOpen(Stream stream) => _open.TrySetResult(stream);

        protected override Task<Stream> CreateContentReadStreamAsync(
            CancellationToken cancellationToken)
        {
            _openStarted.TrySetResult();
            return _open.Task;
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed.TrySetResult();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class DisposalTrackingStream : MemoryStream
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => _disposed.Task;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _disposed.TrySetResult();
            }
        }
    }

    private enum CustodyBlock
    {
        None,
        Create,
        Read,
        SynchronousCreate,
        SynchronousRead,
        CreateFailure,
        ReadFailure,
    }

    private sealed class ControlledCustodyStore(CustodyBlock block) : ICustodyStore
    {
        private readonly TaskCompletionSource<DurableBlobWriteReceipt> _create =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ReadOnlyMemory<byte>> _read =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _createStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DurableBlobWriteReceipt? _receipt;

        public Task CreateStarted => _createStarted.Task;
        public Task ReadStarted => _readStarted.Task;
        public CancellationToken CreateToken { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public byte[] CreatedBytes { get; private set; } = [];
        public int CreateCount { get; private set; }
        public int ReadCount { get; private set; }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            CreateToken = cancellationToken;
            CreatedBytes = bytes.ToArray();
            CreateCount++;
            _receipt = Receipt(CreatedBytes, custodyClass);
            _createStarted.TrySetResult();
            if (block == CustodyBlock.SynchronousCreate)
            {
                _create.Task.GetAwaiter().GetResult();
            }

            if (block == CustodyBlock.CreateFailure)
            {
                return Task.FromException<DurableBlobWriteReceipt>(
                    new IOException("custody create unavailable"));
            }

            return block == CustodyBlock.Create
                ? _create.Task
                : Task.FromResult(_receipt);
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            ReadToken = cancellationToken;
            ReadCount++;
            Assert.AreEqual(_receipt?.Reference, reference);
            _readStarted.TrySetResult();
            if (block == CustodyBlock.SynchronousRead)
            {
                _read.Task.GetAwaiter().GetResult();
            }

            if (block == CustodyBlock.ReadFailure)
            {
                return Task.FromException<ReadOnlyMemory<byte>>(
                    new IOException("custody read unavailable"));
            }

            return block == CustodyBlock.Read
                ? _read.Task
                : Task.FromResult<ReadOnlyMemory<byte>>(CreatedBytes.ToArray());
        }

        public void ReleaseCreate() => _create.TrySetResult(
            _receipt ?? throw new InvalidOperationException("Custody create was not started."));

        public void ReleaseRead() => _read.TrySetResult(CreatedBytes.ToArray());

        private static DurableBlobWriteReceipt Receipt(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass)
        {
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                CustodyDigest.Of(bytes.Span),
                bytes.Length,
                custodyClass);
            var observed = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-000000000040"),
                CustodyProtection.LockedTime,
                observed,
                observed.AddDays(91));
            return new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                policy);
        }
    }
}
