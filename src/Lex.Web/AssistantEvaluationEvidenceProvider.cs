using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lex.Evaluation;
using Lex.Index;

namespace Lex.Web;

internal static class AssistantEvaluationEvidenceServices
{
    private const string ClientName = "assistant-evaluation-evidence";

    internal static IServiceCollection AddAssistantEvaluationEvidence(
        this IServiceCollection services)
    {
        services.AddHttpClient(ClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            AutomaticDecompression = DecompressionMethods.All,
        });
        services.AddSingleton<IAssistantEvaluationEvidenceProvider>(services =>
            ActivatorUtilities.CreateInstance<GitHubAssistantEvaluationEvidenceProvider>(
                services,
                services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName)));
        return services;
    }
}

internal sealed record AssistantEvaluationEvidenceSnapshot(
    VerifiedAssistantEvaluationEvidence? Evidence)
{
    internal static AssistantEvaluationEvidenceSnapshot Unavailable { get; } =
        new((VerifiedAssistantEvaluationEvidence?)null);
    internal bool Verified => Evidence is not null;
}

internal interface IAssistantEvaluationEvidenceProvider
{
    Task<AssistantEvaluationEvidenceSnapshot> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Discovers signed evidence for this exact immutable revision. GitHub is only a bounded delivery
/// channel: the release signature and exact runtime binding are authoritative here, while the
/// canonical publisher owns report semantics. Failures never affect legal readiness.
/// </summary>
internal sealed class GitHubAssistantEvaluationEvidenceProvider
    : IAssistantEvaluationEvidenceProvider, IDisposable
{
    private const string Repository = "SFHAJJI/lex-ops";
    private const int MaximumMatchingTags = 4;
    private static readonly Regex Commit = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex Digest = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Revision = new(
        "^ca-lex-web--[a-z0-9-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex Image = new(
        "^[a-z0-9.-]+(?:/[a-z0-9._-]+)+@sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly HttpClient _http;
    private readonly Func<AssistantEvaluationRuntimeIdentity?> _runtimeIdentity;
    private readonly TimeProvider _clock;
    private readonly ILogger<GitHubAssistantEvaluationEvidenceProvider> _logger;
    private readonly IReadOnlyList<ArtifactTrustRoot> _artifactRoots;
    private readonly EvaluationAdmissionAuthority _admissionAuthority;
    private CacheEntry? _cache;

    public GitHubAssistantEvaluationEvidenceProvider(
        HttpClient http,
        Microsoft.Extensions.Options.IOptions<LexOptions> options,
        IndexRegistry registry,
        TimeProvider clock,
        ILogger<GitHubAssistantEvaluationEvidenceProvider> logger)
        : this(http,
            () => TryRuntimeIdentity(options.Value, registry, out var identity) ? identity : null,
            clock, logger, ArtifactTrustStore.Roots,
            EvaluationAdmissionTrustStore.Load())
    {
    }

    internal GitHubAssistantEvaluationEvidenceProvider(
        HttpClient http,
        Func<AssistantEvaluationRuntimeIdentity?> runtimeIdentity,
        TimeProvider clock,
        ILogger<GitHubAssistantEvaluationEvidenceProvider> logger,
        IReadOnlyList<ArtifactTrustRoot> artifactRoots,
        EvaluationAdmissionAuthority admissionAuthority)
    {
        _http = http;
        _runtimeIdentity = runtimeIdentity;
        _clock = clock;
        _logger = logger;
        _artifactRoots = artifactRoots;
        _admissionAuthority = admissionAuthority;
    }

    public async Task<AssistantEvaluationEvidenceSnapshot> GetAsync(
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        if (Volatile.Read(ref _cache) is { } cached && now < cached.ExpiresAt)
            return cached.Value;
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            now = _clock.GetUtcNow();
            if (Volatile.Read(ref _cache) is { } refreshed && now < refreshed.ExpiresAt)
                return refreshed.Value;
            if (_runtimeIdentity() is not { } runtime)
                return Cache(AssistantEvaluationEvidenceSnapshot.Unavailable,
                    now.AddMinutes(5));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                var candidates = await MatchingTagsAsync(runtime.CodeCommit[..12], timeout.Token);
                var exact = new List<VerifiedAssistantEvaluationEvidence>();
                foreach (var tag in candidates)
                {
                    try
                    {
                        var release = await ReleaseAsync(tag, timeout.Token);
                        if (release is null) continue;
                        var files = await DownloadStandardAssetsAsync(release, timeout.Token);
                        var evidence = AssistantEvaluationEvidenceVerifier.Verify(
                            release, files, _artifactRoots, now, _admissionAuthority);
                        if (evidence.Matches(runtime)) exact.Add(evidence);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _logger.LogWarning("Assistant evaluation evidence candidate rejected: {Reason}",
                            Reason(exception));
                    }
                }

                if (exact.Count != 1)
                {
                    if (exact.Count > 1)
                        _logger.LogWarning("Assistant evaluation evidence is ambiguous for this revision");
                    return Cache(AssistantEvaluationEvidenceSnapshot.Unavailable,
                        now.AddMinutes(5));
                }
                return Cache(new AssistantEvaluationEvidenceSnapshot(exact[0]),
                    now.AddMinutes(30));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Assistant evaluation evidence refresh timed out");
                return Cache(AssistantEvaluationEvidenceSnapshot.Unavailable,
                    now.AddMinutes(1));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning("Assistant evaluation evidence refresh failed: {Reason}",
                    Reason(exception));
                return Cache(AssistantEvaluationEvidenceSnapshot.Unavailable,
                    now.AddMinutes(1));
            }
        }
        finally { _refresh.Release(); }
    }

    private AssistantEvaluationEvidenceSnapshot Cache(
        AssistantEvaluationEvidenceSnapshot value, DateTimeOffset expiresAt)
    {
        Volatile.Write(ref _cache, new CacheEntry(value, expiresAt));
        return value;
    }

    private sealed record CacheEntry(
        AssistantEvaluationEvidenceSnapshot Value,
        DateTimeOffset ExpiresAt);

    private async Task<IReadOnlyList<string>> MatchingTagsAsync(
        string codePrefix, CancellationToken cancellationToken)
    {
        var prefix = $"assistant-eval-{codePrefix}-";
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get,
            $"repos/{Repository}/git/matching-refs/tags/{prefix}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        await RequireSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 12 }, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub matching refs response is malformed.");
        var tags = document.RootElement.EnumerateArray().Select(item =>
        {
            var reference = item.GetProperty("ref").GetString()
                ?? throw new InvalidDataException("GitHub matching ref is missing.");
            var expected = "refs/tags/";
            if (!reference.StartsWith(expected + prefix, StringComparison.Ordinal))
                throw new InvalidDataException("GitHub returned an out-of-scope evaluation ref.");
            return reference[expected.Length..];
        }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (tags.Length > MaximumMatchingTags)
            throw new InvalidDataException("Evaluation release discovery exceeded its bound.");
        return tags;
    }

    private async Task<AssistantEvaluationRelease?> ReleaseAsync(
        string tag, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get,
            $"repos/{Repository}/releases/tags/{Uri.EscapeDataString(tag)}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await RequireSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
        var root = document.RootElement;
        if (root.GetProperty("tag_name").GetString() != tag
            || root.GetProperty("assets").ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub evaluation release is malformed.");
        var assets = root.GetProperty("assets").EnumerateArray().Select(item =>
        {
            var name = item.GetProperty("name").GetString()
                ?? throw new InvalidDataException("GitHub release asset name is missing.");
            return new AssistantEvaluationReleaseAsset(
                item.GetProperty("id").GetInt64(), name, item.GetProperty("size").GetInt64(),
                item.GetProperty("digest").GetString()
                    ?? throw new InvalidDataException("GitHub release asset digest is missing."),
                item.GetProperty("state").GetString()
                    ?? throw new InvalidDataException("GitHub release asset state is missing."),
                item.GetProperty("browser_download_url").GetString()
                    ?? throw new InvalidDataException("GitHub release asset URL is missing."));
        }).ToArray();
        if (assets.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != assets.Length)
            throw new InvalidDataException("GitHub evaluation release has duplicate assets.");
        return new(Repository, tag,
            root.GetProperty("html_url").GetString()
                ?? throw new InvalidDataException("GitHub release URL is missing."),
            root.GetProperty("immutable").GetBoolean(), root.GetProperty("draft").GetBoolean(),
            root.GetProperty("prerelease").GetBoolean(),
            assets.ToDictionary(item => item.Name, StringComparer.Ordinal));
    }

    private async Task<IReadOnlyDictionary<string, byte[]>> DownloadStandardAssetsAsync(
        AssistantEvaluationRelease release, CancellationToken cancellationToken)
    {
        var tasks = AssistantEvaluationEvidenceVerifier.SignedPayloadFiles
            .Append(AssistantEvaluationEvidenceVerifier.ManifestFile)
            .Append(AssistantEvaluationEvidenceVerifier.ManifestSignatureFile)
            .Select(async name => (Name: name,
                Bytes: await DownloadAssetAsync(release.Assets[name], cancellationToken)))
            .ToArray();
        var files = await Task.WhenAll(tasks);
        return files.ToDictionary(item => item.Name, item => item.Bytes, StringComparer.Ordinal);
    }

    private async Task<byte[]> DownloadAssetAsync(
        AssistantEvaluationReleaseAsset asset, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.IdnHost != "github.com"
            || !uri.AbsolutePath.StartsWith(
                "/SFHAJJI/lex-ops/releases/download/assistant-eval-", StringComparison.Ordinal))
            throw new InvalidDataException("GitHub evaluation asset URL is outside the fixed repository.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await SendAsync(request, cancellationToken);
        await RequireSuccessAsync(response, cancellationToken);
        if (response.RequestMessage?.RequestUri is not { } final
            || final.Scheme != Uri.UriSchemeHttps
            || final.IdnHost != "github.com"
                && !final.IdnHost.EndsWith(".githubusercontent.com", StringComparison.Ordinal))
            throw new InvalidDataException("GitHub evaluation asset redirect left the trusted hosts.");
        if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
            throw new InvalidDataException("GitHub evaluation asset length changed.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(checked((int)Math.Min(asset.Size, 4L * 1024 * 1024)));
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > 4L * 1024 * 1024)
                throw new InvalidDataException("GitHub evaluation asset exceeded its download limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2026-03-10");
        request.Headers.UserAgent.ParseAdd("Lex-evaluation-evidence/1");
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static async Task RequireSuccessAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        _ = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        throw new HttpRequestException("GitHub evaluation evidence request failed.", null,
            response.StatusCode);
    }

    private static bool TryRuntimeIdentity(
        LexOptions options, IndexRegistry registry,
        out AssistantEvaluationRuntimeIdentity identity)
    {
        identity = null!;
        if (options.CodeCommit is not { } code || !Commit.IsMatch(code)
            || options.Revision is not { } revision || !Revision.IsMatch(revision)
            || options.RevisionHostname is not { } revisionHost
            || !Uri.CheckHostName(revisionHost).Equals(UriHostNameType.Dns)
            || options.DeployImage is not { } image || !Image.IsMatch(image)
            || options.ArtifactManifestId is not { } artifactSet || !Digest.IsMatch(artifactSet)
            || options.AssistantEvalCatalogSha256 is not { } catalog || !Digest.IsMatch(catalog)
            || registry.VerifiedManifestSetId is not { } mounted || !Fixed(artifactSet, mounted)
            || registry.VerifiedArtifactManifests.Count == 0
            || options.AzureOpenAiEndpoint is not { } endpoint
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || endpointUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(options.AzureOpenAiDeployment))
            return false;
        var indexIds = registry.VerifiedArtifactManifests.Select(item => item.Sha256)
            .Order(StringComparer.Ordinal).ToArray();
        if (indexIds.Any(item => !Digest.IsMatch(item))) return false;
        identity = new(code, revision, revisionHost, image, artifactSet, catalog,
            endpointUri.IdnHost, options.AzureOpenAiDeployment, indexIds);
        return true;
    }

    private static bool Fixed(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(right.ToLowerInvariant()));

    private static string Reason(Exception exception) => exception switch
    {
        CryptographicException => "signature_or_digest_invalid",
        InvalidDataException => "evidence_contract_invalid",
        HttpRequestException => "github_request_failed",
        _ => "unexpected_verification_failure",
    };

    public void Dispose()
    {
        _refresh.Dispose();
        GC.SuppressFinalize(this);
    }
}
