using System.Net.Http.Headers;
using System.Security.Cryptography;
using Lex.Sources.EurLex;

namespace Lex.Tests;

public sealed class EurLexRunnerDiagnosticTests
{
    [Theory]
    [InlineData("32014R0680")]
    [InlineData("32025L0516")]
    public async Task Exact_portal_parser_accepts_live_runner_bytes(string celex)
    {
        if (Environment.GetEnvironmentVariable("EURLEX_RUNNER_DIAGNOSTIC") != "1")
            return;

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(180),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lex/0.1");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/SFHAJJI/lex)");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:{celex}");
        request.Headers.TryAddWithoutValidation("Accept", "application/xhtml+xml, text/html");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        using var response = await client.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var evidence = $"status={(int)response.StatusCode}; length={bytes.Length}; "
            + $"sha256={Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}; "
            + $"type={response.Content.Headers.ContentType}; "
            + $"language={string.Join(',', response.Content.Headers.ContentLanguage)}";

        Assert.True(response.IsSuccessStatusCode, evidence);
        Assert.True(EurLexAdapter.IsExactPortalExpression(text, celex, "en"), evidence);
    }
}
