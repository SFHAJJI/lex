using System.Text.Json;

namespace Lex.Sources.Legilux;

/// <summary>
/// Minimal SPARQL-protocol client. Sequential, paced, identifying UA (D14).
/// The endpoint is the publisher's officially published open-data access channel.
/// </summary>
public sealed class SparqlClient(string endpoint, TimeSpan? pause = null)
{
    private static readonly HttpClient Http = CreateClient();
    private readonly TimeSpan _pause = pause ?? TimeSpan.FromMilliseconds(1500);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lex/0.1");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/SFHAJJI/lex)");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/sparql-results+json");
        return c;
    }

    public async Task<List<Dictionary<string, string>>> SelectAsync(string query, CancellationToken ct)
    {
        // Politeness: sequential with a pause between requests.
        var sinceLast = DateTimeOffset.UtcNow - _lastRequest;
        if (sinceLast < _pause) await Task.Delay(_pause - sinceLast, ct);

        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("query", query) });
        using var resp = await Http.PostAsync(endpoint, content, ct);
        _lastRequest = DateTimeOffset.UtcNow;
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var rows = new List<Dictionary<string, string>>();
        foreach (var binding in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in binding.EnumerateObject())
                row[prop.Name] = prop.Value.GetProperty("value").GetString() ?? "";
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Runs a paged query; <paramref name="pagedQuery"/> receives (limit, offset).</summary>
    public async Task<List<Dictionary<string, string>>> SelectPagedAsync(
        Func<int, int, string> pagedQuery, int pageSize, CancellationToken ct, Action<int>? onPage = null)
    {
        var all = new List<Dictionary<string, string>>();
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await SelectAsync(pagedQuery(pageSize, offset), ct);
            all.AddRange(page);
            onPage?.Invoke(all.Count);
            if (page.Count < pageSize) break;
        }
        return all;
    }
}
