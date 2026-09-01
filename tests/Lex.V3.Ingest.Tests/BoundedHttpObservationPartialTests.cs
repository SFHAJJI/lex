using System.Net;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class BoundedHttpObservationPartialTests
{
    [TestMethod]
    public async Task DeclaredShortReadRetainsTheExactPositivePrefix()
    {
        var stream = new ScriptedReadStream(Step.Bytes("AB"), Step.End());
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(Response(stream, declaredLength: 5), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertPartial(result, HttpPartialBodyReason.DeclaredLengthShortRead, "AB", custody);
    }

    [TestMethod]
    public async Task DeclaredLengthNeverReadsBeyondTheFramedBody()
    {
        var stream = new ScriptedReadStream(Step.Bytes("ABCD"), Step.End());
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(Response(stream, declaredLength: 2), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var complete = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(complete);
        Assert.AreEqual(2, complete.ReceivedEncodedEntityByteCount);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("AB"), custody.CreatedBytes);
        Assert.AreEqual(2, stream.BytesReturned);
    }

    [TestMethod]
    public async Task FramedCompletionOutranksCallerCancellationFromTheSameRead()
    {
        using var caller = new CancellationTokenSource();
        var stream = new ScriptedReadStream(Step.BytesThenCancel("ABC", caller));
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(Response(stream, declaredLength: 2), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), caller.Token);

        Assert.IsTrue(caller.IsCancellationRequested);
        var complete = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(complete);
        Assert.AreEqual(2, complete.ReceivedEncodedEntityByteCount);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("AB"), custody.CreatedBytes);
        Assert.AreEqual(2, stream.BytesReturned);
    }

    [TestMethod]
    public async Task MissingCompletionProofRetainsTheBoundedEntityWithoutClaimingItIsPartial()
    {
        var stream = new ScriptedReadStream(Step.Bytes("AB"), Step.End());
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(Response(stream), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertCompletionUnproven(
            result,
            HttpCompletionUnprovenReason.MissingCompletionProof,
            "AB",
            custody);
    }

    [TestMethod]
    public async Task TransferCodingConflictNeverMintsDeclaredLengthCompletion()
    {
        var stream = new ScriptedReadStream(Step.Bytes("AB"), Step.End());
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(stream, declaredLength: 2, transferEncoding: "chunked"),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertCompletionUnproven(
            result,
            HttpCompletionUnprovenReason.TransferCodingConflict,
            "AB",
            custody);
    }

    [TestMethod]
    public async Task OrdinaryChunkedTransferIsUnprovenRatherThanAConflict()
    {
        var stream = new ScriptedReadStream(Step.Bytes("AB"), Step.End());
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(stream, transferEncoding: "chunked"),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertCompletionUnproven(
            result,
            HttpCompletionUnprovenReason.MissingCompletionProof,
            "AB",
            custody);
    }

    [TestMethod]
    public async Task DeclaredLengthAboveTheBoundRetainsOnlyTheAdmittedPrefix()
    {
        var stream = new ScriptedReadStream(Step.Bytes("ABCDE"), Step.End());
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(stream, declaredLength: 5),
            custody,
            maximumResponseBytes: 3);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertPartial(result, HttpPartialBodyReason.ByteBoundPreventedCompletion, "ABC", custody);
        Assert.AreEqual(3, stream.BytesReturned);
    }

    [TestMethod]
    public async Task DeclaredLengthAtTheBoundCompletesAtTheExactDeclaredCount()
    {
        var stream = new ScriptedReadStream(
            Step.Bytes("ABC"),
            Step.WaitForCancellation());
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(stream, declaredLength: 3),
            custody,
            maximumResponseBytes: 3);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var complete = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(complete);
        Assert.AreEqual(3, complete.ReceivedEncodedEntityByteCount);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("ABC"), custody.CreatedBytes);
    }

    [TestMethod]
    public async Task DeclaredLengthCompletionDoesNotWaitForEndOfStream()
    {
        var stream = new ScriptedReadStream(
            Step.Bytes("AB"),
            Step.WaitForCancellation());
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(stream, declaredLength: 2),
            custody,
            bodyTimeout: TimeSpan.FromMilliseconds(250));

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var complete = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(complete);
        Assert.AreEqual(2, complete.ReceivedEncodedEntityByteCount);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("AB"), custody.CreatedBytes);
    }

    [TestMethod]
    public async Task BodyReadFailureAfterAChunkRetainsTheChunk()
    {
        var stream = new ScriptedReadStream(
            Step.Bytes("AB"),
            Step.Throw(new IOException("publisher reset")));
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(Response(stream, declaredLength: 5), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertPartial(result, HttpPartialBodyReason.BodyReadFailure, "AB", custody);
    }

    [TestMethod]
    public async Task ProviderCancellationWithoutEitherLexTokenIsABodyReadFailure()
    {
        var stream = new ScriptedReadStream(
            Step.Bytes("AB"),
            Step.Throw(new OperationCanceledException("provider aborted its read")));
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(Response(stream, declaredLength: 5), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertPartial(result, HttpPartialBodyReason.BodyReadFailure, "AB", custody);
    }

    [TestMethod]
    public async Task ProviderBodyCancellationIsNotRelabeledByLaterCallerCancellation()
    {
        using var caller = new CancellationTokenSource();
        using var provider = new CancellationTokenSource();
        provider.Cancel();
        var stream = new ScriptedReadStream(
            Step.CancelThenThrow(
                caller,
                new OperationCanceledException(provider.Token)));
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(Response(stream, declaredLength: 5), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), caller.Token);

        Assert.IsTrue(caller.IsCancellationRequested);
        var partial = result as ResponsePartialBodyObservation;
        Assert.IsNotNull(partial);
        Assert.AreEqual(
            HttpPartialBodyReason.BodyReadFailure,
            HttpAcquisitionReasonRegistry.RequirePartial(partial.TerminalFailureReason));
        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task BodyDeadlineAfterAChunkUsesAnIndependentEvidenceCommitToken()
    {
        var stream = new ScriptedReadStream(Step.Bytes("AB"), Step.WaitForCancellation());
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(stream, declaredLength: 5),
            custody,
            bodyTimeout: TimeSpan.FromMilliseconds(50));

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertPartial(result, HttpPartialBodyReason.BodyDeadline, "AB", custody);
        Assert.IsFalse(custody.CreateTokenWasCanceled);
    }

    [TestMethod]
    public async Task CallerCancellationAfterAChunkRetainsEvidenceWithoutRelabelingItAsDeadline()
    {
        using var caller = new CancellationTokenSource();
        var stream = new ScriptedReadStream(
            Step.Bytes("AB"),
            Step.Cancel(caller));
        var custody = new RecordingCustodyStore
        {
            ForbiddenCreateToken = caller.Token,
        };
        using var acquirer = Acquirer(Response(stream, declaredLength: 5), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), caller.Token);

        AssertPartial(result, HttpPartialBodyReason.CallerCancelledAfterHeaders, "AB", custody);
        Assert.IsFalse(custody.CreateTokenWasCanceled);
        Assert.IsFalse(custody.UsedForbiddenCreateToken);
    }

    [TestMethod]
    public async Task CallerCancellationAfterHeadersBeforeFirstOctetRetainsTheObservedResponse()
    {
        using var caller = new CancellationTokenSource();
        var stream = new ScriptedReadStream(Step.Cancel(caller));
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(Response(stream, declaredLength: 5), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), caller.Token);

        var partial = result as ResponsePartialBodyObservation;
        Assert.IsNotNull(partial);
        Assert.AreEqual(
            HttpPartialBodyReason.CallerCancelledAfterHeaders,
            HttpAcquisitionReasonRegistry.RequirePartial(partial.TerminalFailureReason));
        Assert.AreEqual(0, partial.ReceivedEncodedEntityByteCount);
        Assert.IsNull(partial.DurableWriteReceipt);
        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task CallerCancellationBeforeAnyResponseEvidencePropagates()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(new ScriptedReadStream(Step.Bytes("AB")), declaredLength: 2),
            custody);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            acquirer.AcquireAsync(RequestEvidence(), caller.Token));

        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task ZeroByteBodyFailureIssuesPartialWithoutAnEmptyCustodyBlob()
    {
        var stream = new ScriptedReadStream(Step.Throw(new IOException("no entity octet")));
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(Response(stream, declaredLength: 5), custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var partial = result as ResponsePartialBodyObservation;
        Assert.IsNotNull(partial);
        Assert.AreEqual(
            HttpPartialBodyReason.BodyReadFailure,
            HttpAcquisitionReasonRegistry.RequirePartial(partial.TerminalFailureReason));
        Assert.AreEqual(0, partial.ReceivedEncodedEntityByteCount);
        Assert.IsNull(partial.DurableWriteReceipt);
        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task PartialReceiptSubstitutionCannotIssueAnObservation()
    {
        var stream = new ScriptedReadStream(Step.Bytes("AB"), Step.End());
        var custody = new RecordingCustodyStore
        {
            ReceiptBytesOverride = Encoding.UTF8.GetBytes("substituted"),
        };
        using var acquirer = Acquirer(Response(stream, declaredLength: 5), custody);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None));
    }

    [TestMethod]
    public async Task HeaderDeadlineProducesTheTypedPreHeaderFailure()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            new DelegateHandler(async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new AssertFailedException("The header wait completed without cancellation.");
            }),
            custody,
            headersTimeout: TimeSpan.FromMilliseconds(50));

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var failure = result as TransportFailureBeforeBodyObservation;
        Assert.IsNotNull(failure);
        Assert.AreEqual(
            HttpPreHeaderFailureClass.HeaderDeadline,
            HttpAcquisitionReasonRegistry.RequireBeforeHeaders(failure.FailureClass));
        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task HeaderDeadlineIsEnforcedWhenTheHandlerIgnoresCancellation()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            new DelegateHandler(async _ =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                return Response(new ScriptedReadStream(Step.End()), declaredLength: 0);
            }),
            custody,
            headersTimeout: TimeSpan.FromMilliseconds(20));

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var failure = result as TransportFailureBeforeBodyObservation;
        Assert.IsNotNull(failure);
        Assert.AreEqual(
            HttpPreHeaderFailureClass.HeaderDeadline,
            HttpAcquisitionReasonRegistry.RequireBeforeHeaders(failure.FailureClass));
        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task BodyDeadlineIsEnforcedWhenTheStreamIgnoresCancellation()
    {
        var stream = new IgnoringCancellationStream();
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(stream, declaredLength: 5),
            custody,
            bodyTimeout: TimeSpan.FromMilliseconds(20));

        var acquisition = acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);
        await stream.ReadStarted.WaitAsync(TimeSpan.FromSeconds(1));
        var winner = await Task.WhenAny(acquisition, Task.Delay(TimeSpan.FromSeconds(1)));
        stream.Complete(0);
        stream.ReleaseCleanup();

        Assert.AreSame(acquisition, winner);
        var partial = await acquisition as ResponsePartialBodyObservation;
        Assert.IsNotNull(partial);
        Assert.AreEqual(
            HttpPartialBodyReason.BodyDeadline,
            HttpAcquisitionReasonRegistry.RequirePartial(partial.TerminalFailureReason));
        Assert.AreEqual(0, partial.ReceivedEncodedEntityByteCount);
        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task ProviderCancellationBeforeHeadersIsATypedTransportFailure()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            new DelegateHandler(_ => Task.FromException<HttpResponseMessage>(
                new OperationCanceledException("provider aborted before headers"))),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var failure = result as TransportFailureBeforeBodyObservation;
        Assert.IsNotNull(failure);
        Assert.AreEqual(
            HttpPreHeaderFailureClass.TransportBeforeHeaders,
            HttpAcquisitionReasonRegistry.RequireBeforeHeaders(failure.FailureClass));
    }

    [TestMethod]
    public async Task ProviderHeaderCancellationIsNotRelabeledByLaterCallerCancellation()
    {
        using var caller = new CancellationTokenSource();
        using var provider = new CancellationTokenSource();
        provider.Cancel();
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            new DelegateHandler(_ =>
            {
                caller.Cancel();
                return Task.FromException<HttpResponseMessage>(
                    new OperationCanceledException(provider.Token));
            }),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), caller.Token);

        Assert.IsTrue(caller.IsCancellationRequested);
        var failure = result as TransportFailureBeforeBodyObservation;
        Assert.IsNotNull(failure);
        Assert.AreEqual(
            HttpPreHeaderFailureClass.TransportBeforeHeaders,
            HttpAcquisitionReasonRegistry.RequireBeforeHeaders(failure.FailureClass));
    }

    [TestMethod]
    public async Task HttpFailureBeforeHeadersProducesTheTypedTransportFailure()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            new DelegateHandler(_ => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("connection refused"))),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var failure = result as TransportFailureBeforeBodyObservation;
        Assert.IsNotNull(failure);
        Assert.AreEqual(
            HttpPreHeaderFailureClass.TransportBeforeHeaders,
            HttpAcquisitionReasonRegistry.RequireBeforeHeaders(failure.FailureClass));
        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task UnknownPreHeaderFailureIsNotCollapsedIntoAKnownReason()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            new DelegateHandler(_ => Task.FromException<HttpResponseMessage>(
                new InvalidOperationException("unexpected adapter defect"))),
            custody);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None));

        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task PartialContentCanBeTransferCompleteButRemainsRangeNotApproved()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(
                new ScriptedReadStream(Step.Bytes("AB"), Step.End()),
                declaredLength: 2,
                statusCode: HttpStatusCode.PartialContent,
                contentRange: "bytes 0-1/4"),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var complete = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(complete);
        Assert.AreEqual(HttpStatusDisposition.RangeNotApproved, complete.StatusDisposition);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("AB"), custody.CreatedBytes);
    }

    [TestMethod]
    public async Task Status204RetainsForbiddenPositiveLengthWithoutReadingABody()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(
                new ScriptedReadStream(
                    Step.Throw(new InvalidOperationException("A 204 body must not be read."))),
                declaredLength: 2,
                statusCode: HttpStatusCode.NoContent),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var withoutBody = result as ResponseWithoutBodyObservation;
        Assert.IsNotNull(withoutBody);
        Assert.AreEqual(
            "2",
            Assert.IsInstanceOfType<SingleHttpHeader>(
                withoutBody.ResponseMetadata.ContentLength).Value);
        Assert.AreEqual(0, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    public async Task Status204RetainsAmbiguousLengthWithoutReadingABody()
    {
        var content = new StreamContent(new ScriptedReadStream(
            Step.Throw(new InvalidOperationException("A 204 body must not be read."))));
        Assert.IsTrue(content.Headers.TryAddWithoutValidation(
            "Content-Length",
            new[] { "2", "3" }));
        var response = new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = content,
        };
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(response, custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var withoutBody = result as ResponseWithoutBodyObservation;
        Assert.IsNotNull(withoutBody);
        CollectionAssert.AreEqual(
            new[] { "2", "3" },
            Assert.IsInstanceOfType<MultipleHttpHeader>(
                withoutBody.ResponseMetadata.ContentLength).Values.ToArray());
        Assert.AreEqual(0, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    public async Task FinalInformationalStatusRetainsHeadersWithoutReadingABody()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(
                new ScriptedReadStream(
                    Step.Throw(new InvalidOperationException("A final 1xx body must not be read."))),
                declaredLength: 2,
                statusCode: HttpStatusCode.SwitchingProtocols),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var withoutBody = result as ResponseWithoutBodyObservation;
        Assert.IsNotNull(withoutBody);
        Assert.AreEqual(HttpNoBodyReason.FramingForbidsBody, withoutBody.Reason);
        Assert.AreEqual(
            "2",
            Assert.IsInstanceOfType<SingleHttpHeader>(
                withoutBody.ResponseMetadata.ContentLength).Value);
        Assert.AreEqual(0, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    public async Task Status205RetainsActuallyDeliveredCompleteBytesAsEvidence()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(
                new ScriptedReadStream(Step.Bytes("AB"), Step.WaitForCancellation()),
                declaredLength: 2,
                statusCode: HttpStatusCode.ResetContent),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var complete = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(complete);
        Assert.AreEqual(HttpStatusDisposition.SemanticNoEntityStatus, complete.StatusDisposition);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("AB"), custody.CreatedBytes);
    }

    [TestMethod]
    public async Task Status205RetainsAnIncompletePublisherBodyAsPartialEvidence()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(
                new ScriptedReadStream(Step.Bytes("A"), Step.End()),
                declaredLength: 2,
                statusCode: HttpStatusCode.ResetContent),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertPartial(result, HttpPartialBodyReason.DeclaredLengthShortRead, "A", custody);
        Assert.AreEqual(HttpStatusDisposition.SemanticNoEntityStatus,
            Assert.IsInstanceOfType<ResponsePartialBodyObservation>(result).StatusDisposition);
    }

    [TestMethod]
    public async Task Status205WithoutCompletionFramingRetainsUnprovenEvidence()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(
                new ScriptedReadStream(Step.Bytes("AB"), Step.End()),
                transferEncoding: "chunked",
                statusCode: HttpStatusCode.ResetContent),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        AssertCompletionUnproven(
            result,
            HttpCompletionUnprovenReason.MissingCompletionProof,
            "AB",
            custody);
        Assert.AreEqual(HttpStatusDisposition.SemanticNoEntityStatus,
            Assert.IsInstanceOfType<ResponseCompletionUnprovenObservation>(result).StatusDisposition);
    }

    [TestMethod]
    public async Task ContentRangeOnStatus200RemainsCompleteEvidenceButCannotBeApprovedAsAFullRange()
    {
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(
            Response(
                new ScriptedReadStream(Step.Bytes("AB"), Step.End()),
                declaredLength: 2,
                contentRange: "bytes 0-1/4"),
            custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var complete = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(complete);
        Assert.AreEqual(HttpStatusDisposition.RangeNotApproved, complete.StatusDisposition);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("AB"), custody.CreatedBytes);
    }

    private static void AssertPartial(
        HttpObservation result,
        HttpPartialBodyReason expectedReason,
        string expectedBytes,
        RecordingCustodyStore custody)
    {
        var partial = result as ResponsePartialBodyObservation;
        Assert.IsNotNull(partial);
        Assert.AreEqual(
            expectedReason,
            HttpAcquisitionReasonRegistry.RequirePartial(partial.TerminalFailureReason));
        var bytes = Encoding.ASCII.GetBytes(expectedBytes);
        Assert.AreEqual(bytes.Length, partial.ReceivedEncodedEntityByteCount);
        CollectionAssert.AreEqual(bytes, custody.CreatedBytes);
        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(1, custody.ReadCount);
    }

    private static void AssertCompletionUnproven(
        HttpObservation result,
        HttpCompletionUnprovenReason expectedReason,
        string expectedBytes,
        RecordingCustodyStore custody)
    {
        var unproven = result as ResponseCompletionUnprovenObservation;
        Assert.IsNotNull(unproven);
        Assert.AreEqual(
            expectedReason,
            HttpAcquisitionReasonRegistry.RequireCompletionUnproven(
                unproven.CompletionUnprovenReason));
        var bytes = Encoding.ASCII.GetBytes(expectedBytes);
        Assert.AreEqual(bytes.Length, unproven.ReceivedEncodedEntityByteCount);
        CollectionAssert.AreEqual(bytes, custody.CreatedBytes);
        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(1, custody.ReadCount);
    }

    private static BoundedHttpObservationAcquirer Acquirer(
        HttpResponseMessage response,
        ICustodyStore custody,
        long maximumResponseBytes = 1024,
        TimeSpan? bodyTimeout = null) => Acquirer(
            new RecordingHandler(response),
            custody,
            maximumResponseBytes,
            bodyTimeout: bodyTimeout);

    private static BoundedHttpObservationAcquirer Acquirer(
        HttpMessageHandler handler,
        ICustodyStore custody,
        long maximumResponseBytes = 1024,
        TimeSpan? headersTimeout = null,
        TimeSpan? bodyTimeout = null) => new(
            handler,
            custody,
            maximumResponseBytes,
            headersTimeout ?? TimeSpan.FromSeconds(5),
            bodyTimeout ?? TimeSpan.FromSeconds(5));

    private static HttpResponseMessage Response(
        Stream stream,
        long? declaredLength = null,
        string? transferEncoding = null,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? contentRange = null)
    {
        var content = new StreamContent(stream);
        if (declaredLength.HasValue)
        {
            content.Headers.ContentLength = declaredLength.Value;
        }

        if (contentRange is not null)
        {
            Assert.IsTrue(content.Headers.TryAddWithoutValidation("Content-Range", contentRange));
        }

        var response = new HttpResponseMessage(statusCode)
        {
            Content = content,
        };
        if (transferEncoding is not null)
        {
            Assert.IsTrue(response.Headers.TryAddWithoutValidation(
                "Transfer-Encoding",
                transferEncoding));
        }

        return response;
    }

    private static HttpRequestEvidence RequestEvidence() => new(
        requestedUri: "https://data.legilux.public.lu/example.xml",
        HttpRequestMethod.Get,
        observedAtUtc: "2026-09-01T10:00:00.000Z",
        timestampPrecision: HttpObservationTimestampPrecision.Millisecond,
        clockSource: HttpObservationClockSource.SystemUtc,
        runIdentity: Artifact(1),
        adapterIdentity: Artifact(2),
        requestPolicyIdentity: Artifact(3),
        representationRequestKeyIdentity: Artifact(4),
        outboundCrawlerIdentity: new OutboundCrawlerIdentityEvidence(
            OutboundCrawlerIdentity.Schema,
            OutboundCrawlerIdentity.Token),
        origin: new HttpOrigin("https", "data.legilux.public.lu", 443),
        queryPlanIdentity: Artifact(5));

    private static SourceArtifactRef Artifact(int suffix) => new(
        $"urn:uuid:00000000-0000-0000-0000-{suffix:D12}",
        new string('a', 64));

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

    private sealed class DelegateHandler(
        Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(cancellationToken);
    }

    private sealed class ScriptedReadStream(params Step[] steps) : Stream
    {
        private readonly Queue<Step> _steps = new(steps);
        private byte[]? _current;
        private int _currentOffset;

        public int BytesReturned { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_current is not null && _currentOffset < _current.Length)
            {
                return CopyCurrent(buffer);
            }

            if (_steps.Count == 0)
            {
                return 0;
            }

            var step = _steps.Dequeue();
            if (step.BytesValue is not null)
            {
                _current = step.BytesValue;
                _currentOffset = 0;
                step.CallerToCancel?.Cancel();
                return CopyCurrent(buffer);
            }

            if (step.CallerToCancel is not null)
            {
                step.CallerToCancel.Cancel();
                if (step.Exception is not null)
                {
                    ExceptionDispatchInfo.Capture(step.Exception).Throw();
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (step.Exception is not null)
            {
                ExceptionDispatchInfo.Capture(step.Exception).Throw();
            }

            if (step.Wait)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return 0;
        }

        private int CopyCurrent(Memory<byte> buffer)
        {
            var count = Math.Min(buffer.Length, _current!.Length - _currentOffset);
            _current.AsMemory(_currentOffset, count).CopyTo(buffer);
            _currentOffset += count;
            BytesReturned += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class IgnoringCancellationStream : Stream
    {
        private readonly TaskCompletionSource<int> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cleanupRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public void Complete(int byteCount) => _completion.TrySetResult(byteCount);

        public void ReleaseCleanup() => _cleanupRelease.TrySetResult();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            return new ValueTask<int>(_completion.Task);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cleanupRelease.Task.GetAwaiter().GetResult();
            }

            base.Dispose(disposing);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed record Step(
        byte[]? BytesValue,
        Exception? Exception,
        bool Wait,
        CancellationTokenSource? CallerToCancel)
    {
        public static Step Bytes(string value) => new(
            Encoding.ASCII.GetBytes(value),
            null,
            false,
            null);

        public static Step BytesThenCancel(
            string value,
            CancellationTokenSource caller) => new(
                Encoding.ASCII.GetBytes(value),
                null,
                false,
                caller);

        public static Step End() => new(null, null, false, null);

        public static Step Throw(Exception exception) => new(null, exception, false, null);

        public static Step WaitForCancellation() => new(null, null, true, null);

        public static Step Cancel(CancellationTokenSource caller) => new(null, null, false, caller);

        public static Step CancelThenThrow(
            CancellationTokenSource caller,
            Exception exception) => new(null, exception, false, caller);
    }

    private sealed class RecordingCustodyStore : ICustodyStore
    {
        private DurableBlobWriteReceipt? _receipt;

        public byte[]? ReceiptBytesOverride { get; init; }

        public CancellationToken ForbiddenCreateToken { get; init; }

        public bool UsedForbiddenCreateToken { get; private set; }

        public bool CreateTokenWasCanceled { get; private set; }

        public byte[] CreatedBytes { get; private set; } = [];

        public int CreateCount { get; private set; }

        public int ReadCount { get; private set; }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            CreateTokenWasCanceled = cancellationToken.IsCancellationRequested;
            UsedForbiddenCreateToken = ForbiddenCreateToken.CanBeCanceled &&
                cancellationToken == ForbiddenCreateToken;
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            CreatedBytes = bytes.ToArray();
            var receiptBytes = ReceiptBytesOverride ?? CreatedBytes;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                CustodyDigest.Of(receiptBytes),
                receiptBytes.Length,
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
            _receipt = new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                policy);
            return Task.FromResult(_receipt);
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            Assert.AreEqual(_receipt?.Reference, reference);
            return Task.FromResult<ReadOnlyMemory<byte>>(CreatedBytes.ToArray());
        }
    }
}
