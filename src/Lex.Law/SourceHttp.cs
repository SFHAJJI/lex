namespace Lex.Law;

public sealed record SourceRetryPolicy(
    int MaximumAttempts = 4,
    TimeSpan? BaseDelay = null,
    TimeSpan? MaximumDelay = null)
{
    public TimeSpan EffectiveBaseDelay => BaseDelay ?? TimeSpan.FromSeconds(1);
    public TimeSpan EffectiveMaximumDelay => MaximumDelay ?? TimeSpan.FromSeconds(30);
}

public sealed record SourceHttpResult(
    HttpResponseMessage? Response,
    int Attempts,
    bool RetryExhausted,
    string? FailureDetail = null);

/// <summary>One bounded, deterministic HTTP retry contract shared by official-source adapters.</summary>
public static class SourceHttp
{
    private static readonly HashSet<System.Net.HttpStatusCode> RetryableStatuses =
    [
        System.Net.HttpStatusCode.RequestTimeout,
        System.Net.HttpStatusCode.TooManyRequests,
        System.Net.HttpStatusCode.InternalServerError,
        System.Net.HttpStatusCode.BadGateway,
        System.Net.HttpStatusCode.ServiceUnavailable,
        System.Net.HttpStatusCode.GatewayTimeout,
    ];

    public static async Task<SourceHttpResult> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        SourceRetryPolicy policy,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        HttpCompletionOption completion = HttpCompletionOption.ResponseHeadersRead)
    {
        if (policy.MaximumAttempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(policy), "Maximum attempts must be between 1 and 10.");
        if (policy.EffectiveBaseDelay < TimeSpan.Zero || policy.EffectiveMaximumDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(policy), "Retry delays cannot be negative.");
        delay ??= Task.Delay;
        string? lastFailure = null;

        for (var attempt = 1; attempt <= policy.MaximumAttempts; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var request = requestFactory();
                response = await client.SendAsync(request, completion, ct);
                if (!RetryableStatuses.Contains(response.StatusCode))
                    return new(response, attempt, RetryExhausted: false);
                lastFailure = $"HTTP {(int)response.StatusCode}";
                if (attempt == policy.MaximumAttempts)
                    return new(response, attempt, RetryExhausted: true, lastFailure);
                var wait = RetryDelay(response, attempt, policy);
                response.Dispose();
                await delay(wait, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                response?.Dispose();
                lastFailure = "bounded network timeout";
                if (attempt == policy.MaximumAttempts)
                    return new(null, attempt, RetryExhausted: true, lastFailure);
                await delay(ExponentialDelay(attempt, policy), ct);
            }
            catch (HttpRequestException ex)
            {
                response?.Dispose();
                lastFailure = $"network failure ({ex.HttpRequestError})";
                if (attempt == policy.MaximumAttempts)
                    return new(null, attempt, RetryExhausted: true, lastFailure);
                await delay(ExponentialDelay(attempt, policy), ct);
            }
        }
        throw new InvalidOperationException("The bounded source retry loop ended without an outcome.");
    }

    private static TimeSpan RetryDelay(
        HttpResponseMessage response, int attempt, SourceRetryPolicy policy)
    {
        var retryAfter = response.Headers.RetryAfter;
        var requested = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        if (requested is null || requested < TimeSpan.Zero)
            return ExponentialDelay(attempt, policy);
        return requested > policy.EffectiveMaximumDelay ? policy.EffectiveMaximumDelay : requested.Value;
    }

    private static TimeSpan ExponentialDelay(int attempt, SourceRetryPolicy policy)
    {
        var ticks = policy.EffectiveBaseDelay.Ticks * (1L << Math.Min(attempt - 1, 20));
        return TimeSpan.FromTicks(Math.Min(ticks, policy.EffectiveMaximumDelay.Ticks));
    }
}
