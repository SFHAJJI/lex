using System.Text;
using Lex.V3.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class ApiResponseBufferTests
{
    [TestMethod]
    public void ProblemDocumentIsSerializedBeforeCommit()
    {
        var bytes = BoundedJsonBuffer.Serialize(
            new PreviewProblem(
                "urn:lex:v3:preview:invalid-request",
                "Invalid preview request",
                StatusCodes.Status400BadRequest),
            maximumBytes: 4 * 1024);

        Assert.AreEqual(
            "{\"type\":\"urn:lex:v3:preview:invalid-request\",\"title\":\"Invalid preview request\",\"status\":400}",
            Encoding.UTF8.GetString(bytes));
    }

    [TestMethod]
    public async Task OversizedDocumentLeavesResponseUncommittedAndUntouched()
    {
        var (context, feature) = CreateRecordingContext();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            BufferedHttpResponse.WriteJsonAsync(
                context.Response,
                StatusCodes.Status400BadRequest,
                "application/problem+json",
                new { value = new string('x', 128) },
                maximumBytes: 16,
                CancellationToken.None));

        AssertResponseUntouched(feature);
    }

    [TestMethod]
    public async Task CancellationBeforeCommitLeavesResponseUncommittedAndUntouched()
    {
        var (context, feature) = CreateRecordingContext();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            BufferedHttpResponse.WriteJsonAsync(
                context.Response,
                StatusCodes.Status400BadRequest,
                "application/problem+json",
                new PreviewProblem(
                    "urn:lex:v3:preview:invalid-request",
                    "Invalid preview request",
                    StatusCodes.Status400BadRequest),
                4 * 1024,
                cancelled.Token));

        AssertResponseUntouched(feature);
    }

    private static (DefaultHttpContext Context, RecordingResponseFeature Feature) CreateRecordingContext()
    {
        var feature = new RecordingResponseFeature
        {
            StatusCode = StatusCodes.Status202Accepted,
        };
        feature.Headers.ContentType = "application/original+json";
        feature.Headers.ContentLength = 3;
        feature.Headers.CacheControl = "private";
        feature.Headers.XContentTypeOptions = "original";
        feature.Body.Seed([1, 2, 3]);
        feature.ResetCommitState();

        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(feature);
        features.Set<IHttpResponseBodyFeature>(feature);
        return (new DefaultHttpContext(features), feature);
    }

    private static void AssertResponseUntouched(RecordingResponseFeature feature)
    {
        Assert.AreEqual(StatusCodes.Status202Accepted, feature.StatusCode);
        Assert.AreEqual("application/original+json", feature.Headers.ContentType.ToString());
        Assert.AreEqual(3L, feature.Headers.ContentLength);
        Assert.AreEqual("private", feature.Headers.CacheControl.ToString());
        Assert.AreEqual("original", feature.Headers.XContentTypeOptions.ToString());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, feature.Body.ToArray());
        Assert.AreEqual(3L, feature.Body.Position);
        Assert.IsFalse(feature.HasStarted);
        Assert.AreEqual(0, feature.CommitAttempts);
    }

    private sealed class RecordingResponseFeature : IHttpResponseFeature, IHttpResponseBodyFeature
    {
        public RecordingResponseFeature()
        {
            Body = new RecordingBodyStream(() =>
            {
                CommitAttempts++;
                HasStarted = true;
            });
        }

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public RecordingBodyStream Body { get; }

        Stream IHttpResponseFeature.Body
        {
            get => Body;
            set => throw new NotSupportedException();
        }

        Stream IHttpResponseBodyFeature.Stream => Body;

        public System.IO.Pipelines.PipeWriter Writer => throw new NotSupportedException();

        public bool HasStarted { get; private set; }

        public int CommitAttempts { get; private set; }

        public void ResetCommitState()
        {
            CommitAttempts = 0;
            HasStarted = false;
        }

        public void DisableBuffering()
        {
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitAttempts++;
            HasStarted = true;
            return Task.CompletedTask;
        }

        public Task SendFileAsync(
            string path,
            long offset,
            long? count,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CompleteAsync() => Task.CompletedTask;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    private sealed class RecordingBodyStream(Action onWrite) : MemoryStream
    {
        public void Seed(ReadOnlySpan<byte> bytes)
        {
            base.Write(bytes);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            onWrite();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            onWrite();
            base.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onWrite();
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onWrite();
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}
