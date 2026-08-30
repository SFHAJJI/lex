using System.Text.Json;
using Lex.Law;

namespace Lex.Sources.Legilux;

internal sealed record SparqlTerm(string Type, string Value);

/// <summary>
/// Minimal SPARQL-protocol client. Sequential, paced, identifying UA (D14).
/// The endpoint is the publisher's officially published open-data access channel.
/// </summary>
public sealed class SparqlClient(string endpoint, TimeSpan? pause = null)
{
    internal const int SortedTopMaximum = 10_000;
    internal const int ResponseMaximumBytes = 128 * 1024 * 1024;
    private static readonly HttpClient Http = CreateClient();
    private static readonly SourceRetryPolicy RetryPolicy = new(MaximumAttempts: 4);
    private readonly HttpClient _http = Http;
    private readonly TimeSpan _pause = pause ?? TimeSpan.FromMilliseconds(1500);
    private readonly Func<string, CancellationToken, Task<List<Dictionary<string, string>>>>? _selectOverride;
    private readonly Func<string, CancellationToken, Task<List<Dictionary<string, SparqlTerm>>>>? _selectTermsOverride;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    internal SparqlClient(
        Func<string, CancellationToken, Task<List<Dictionary<string, string>>>> select)
        : this("https://test.invalid", TimeSpan.Zero) => _selectOverride = select;

    internal SparqlClient(
        Func<string, CancellationToken, Task<List<Dictionary<string, SparqlTerm>>>> select)
        : this("https://test.invalid", TimeSpan.Zero) => _selectTermsOverride = select;

    internal SparqlClient(HttpClient http)
        : this("https://test.invalid", TimeSpan.Zero)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(120) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lex/0.1");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/SFHAJJI/lex)");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/sparql-results+json");
        return c;
    }

    public async Task<List<Dictionary<string, string>>> SelectAsync(string query, CancellationToken ct)
    {
        if (_selectOverride is not null) return await _selectOverride(query, ct);
        var terms = await SelectTermsAsync(query, ct);
        return terms.Select(row => row.ToDictionary(
            entry => entry.Key, entry => entry.Value.Value, StringComparer.Ordinal)).ToList();
    }

    internal async Task<List<Dictionary<string, SparqlTerm>>> SelectTermsAsync(
        string query, CancellationToken ct)
    {
        if (_selectTermsOverride is not null) return await _selectTermsOverride(query, ct);
        if (_selectOverride is not null)
        {
            var rows = await _selectOverride(query, ct);
            return rows.Select(row => row.ToDictionary(
                entry => entry.Key,
                entry => new SparqlTerm("untyped", entry.Value),
                StringComparer.Ordinal)).ToList();
        }
        // Politeness: sequential with a pause between requests.
        var sinceLast = DateTimeOffset.UtcNow - _lastRequest;
        if (sinceLast < _pause) await Task.Delay(_pause - sinceLast, ct);

        var sent = await SourceHttp.SendAsync(_http, () => new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(
                new[] { new KeyValuePair<string, string>("query", query) }),
        }, RetryPolicy, ct, completion: HttpCompletionOption.ResponseHeadersRead);
        _lastRequest = DateTimeOffset.UtcNow;
        using var resp = sent.Response;
        if (resp is null || sent.RetryExhausted || !resp.IsSuccessStatusCode)
            throw new SourceAcquisitionException(new SourceBuildIssue(
                sent.RetryExhausted ? "enumeration_retry_exhausted" : "enumeration_http_failure",
                "lu-legilux",
                sent.FailureDetail ?? $"The official SPARQL endpoint returned HTTP {(int?)resp?.StatusCode}."),
                sent.Attempts);

        if (resp.Content.Headers.ContentLength is > ResponseMaximumBytes)
            throw ResponseTooLarge(sent.Attempts);

        try
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var bounded = await ReadBoundedResponseAsync(
                stream, ResponseMaximumBytes, ct);
            if (bounded.LimitExceeded)
                throw ResponseTooLarge(sent.Attempts);
            return ParseSelectResponse(bounded.Bytes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new SourceAcquisitionException(new SourceBuildIssue(
                "enumeration_parser_failure", "lu-legilux", ex.Message), sent.Attempts);
        }
    }

    private static SourceAcquisitionException ResponseTooLarge(int attempts) =>
        new(new SourceBuildIssue(
            "enumeration_response_too_large",
            "lu-legilux",
            $"The official SPARQL response exceeds {ResponseMaximumBytes} bytes."),
            attempts);

    internal static async Task<(byte[] Bytes, bool LimitExceeded)> ReadBoundedResponseAsync(
        Stream stream, int maximumBytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var output = new MemoryStream(Math.Min(maximumBytes, 1024 * 1024));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var remaining = maximumBytes - checked((int)output.Length);
            var requested = remaining < buffer.Length ? remaining + 1 : buffer.Length;
            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), ct);
            if (read == 0) return (output.ToArray(), false);
            if (read > remaining)
            {
                if (remaining > 0) output.Write(buffer, 0, remaining);
                return (output.ToArray(), true);
            }
            output.Write(buffer, 0, read);
        }
    }

    internal static List<Dictionary<string, SparqlTerm>> ParseSelectResponse(
        byte[] utf8Json)
    {
        using var doc = JsonDocument.Parse(utf8Json);
        var rows = new List<Dictionary<string, SparqlTerm>>();
        foreach (var binding in doc.RootElement.GetProperty("results")
                     .GetProperty("bindings").EnumerateArray())
        {
            var row = new Dictionary<string, SparqlTerm>(StringComparer.Ordinal);
            foreach (var property in binding.EnumerateObject())
            {
                var type = property.Value.GetProperty("type").GetString()
                    ?? throw new JsonException("A SPARQL result term is missing its type.");
                var value = property.Value.GetProperty("value").GetString()
                    ?? throw new JsonException("A SPARQL result term is missing its value.");
                if (!row.TryAdd(property.Name, new SparqlTerm(type, value)))
                    throw new JsonException(
                        $"A SPARQL result binding contains duplicate name '{property.Name}'.");
            }
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// Runs a paged query; <paramref name="pagedQuery"/> receives (limit, offset), and the
    /// mandatory total maximum is checked before any page is appended.
    /// </summary>
    public async Task<List<Dictionary<string, string>>> SelectPagedAsync(
        Func<int, int, string> pagedQuery, int pageSize, int maximumRows,
        CancellationToken ct, Action<int>? onPage = null) =>
        await SelectPagedCoreAsync(
            pagedQuery, SelectAsync, pageSize, maximumRows, ct, onPage);

    internal async Task<List<Dictionary<string, SparqlTerm>>> SelectTermsPagedAsync(
        Func<int, int, string> pagedQuery, int pageSize, int maximumRows,
        CancellationToken ct, Action<int>? onPage = null) =>
        await SelectPagedCoreAsync(
            pagedQuery, SelectTermsAsync, pageSize, maximumRows, ct, onPage);

    private static async Task<List<Dictionary<string, T>>> SelectPagedCoreAsync<T>(
        Func<int, int, string> pagedQuery,
        Func<string, CancellationToken, Task<List<Dictionary<string, T>>>> select,
        int pageSize, int maximumRows, CancellationToken ct, Action<int>? onPage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        var all = new List<Dictionary<string, T>>(Math.Min(pageSize, maximumRows));
        while (true)
        {
            var remaining = maximumRows - all.Count;
            var requestSize = (int)Math.Min(pageSize, (long)remaining + 1);
            if ((long)all.Count + requestSize > SortedTopMaximum)
                throw new InvalidDataException(
                    $"The Legilux Virtuoso endpoint cannot verify a sorted result beyond {SortedTopMaximum} rows; use a bounded VALUES batch instead.");
            var page = await select(pagedQuery(requestSize, all.Count), ct);
            if (page.Count > requestSize)
                throw new InvalidDataException(
                    $"The SPARQL endpoint returned {page.Count} rows for a {requestSize}-row page.");
            if (page.Count > remaining)
                throw new InvalidDataException(
                    $"The paged SPARQL result exceeds the configured maximum of {maximumRows} rows.");
            all.AddRange(page);
            onPage?.Invoke(all.Count);
            if (page.Count < requestSize) break;
        }
        return all;
    }
}
