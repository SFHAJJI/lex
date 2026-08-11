using System.Net;
using System.Net.Http.Headers;
using Lex.Law;

namespace Lex.Tests;

public sealed class SourceHttpTests
{
    [Fact]
    public async Task Retryable_response_honours_bounded_retry_after_then_succeeds()
    {
        var handler = new SequenceHandler(
            Response(HttpStatusCode.ServiceUnavailable, TimeSpan.FromSeconds(12)),
            Response(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        var delays = new List<TimeSpan>();

        var result = await SourceHttp.SendAsync(client,
            () => new HttpRequestMessage(HttpMethod.Get, "https://publisher.test/body"),
            new SourceRetryPolicy(4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)), default,
            (value, _) => { delays.Add(value); return Task.CompletedTask; });

        using var response = result.Response;
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(2, result.Attempts);
        Assert.False(result.RetryExhausted);
        Assert.Equal([TimeSpan.FromSeconds(5)], delays);
    }

    [Fact]
    public async Task Permanent_not_found_is_never_retried()
    {
        var handler = new SequenceHandler(Response(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        var result = await SourceHttp.SendAsync(client,
            () => new HttpRequestMessage(HttpMethod.Get, "https://publisher.test/body"),
            new SourceRetryPolicy(), default, (_, _) => Task.CompletedTask);

        using var response = result.Response;
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
        Assert.Equal(1, result.Attempts);
        Assert.False(result.RetryExhausted);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Network_failures_stop_at_the_declared_maximum()
    {
        var handler = new SequenceHandler(
            new HttpRequestException(HttpRequestError.ConnectionError),
            new HttpRequestException(HttpRequestError.ConnectionError),
            new HttpRequestException(HttpRequestError.ConnectionError));
        using var client = new HttpClient(handler);

        var result = await SourceHttp.SendAsync(client,
            () => new HttpRequestMessage(HttpMethod.Get, "https://publisher.test/body"),
            new SourceRetryPolicy(3), default, (_, _) => Task.CompletedTask);

        Assert.Null(result.Response);
        Assert.True(result.RetryExhausted);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(3, handler.Calls);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(status);
        if (retryAfter is { } value) response.Headers.RetryAfter = new RetryConditionHeaderValue(value);
        return response;
    }

    private sealed class SequenceHandler(params object[] outcomes) : HttpMessageHandler
    {
        private readonly Queue<object> _outcomes = new(outcomes);
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var outcome = _outcomes.Dequeue();
            return outcome is Exception error
                ? Task.FromException<HttpResponseMessage>(error)
                : Task.FromResult((HttpResponseMessage)outcome);
        }
    }
}
