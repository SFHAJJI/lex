using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Api;

internal sealed class V3ApiHandler
{
    private const string EmptySha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly SyntheticApiState _syntheticState;
    private readonly V3PlatformHost _host;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly V3CorpusMount? _corpusMount;

    public V3ApiHandler(SyntheticApiState syntheticState, Func<DateTimeOffset> utcNow)
        : this(syntheticState, new V3PlatformHost(), utcNow, null)
    {
    }

    internal V3ApiHandler(
        SyntheticApiState syntheticState,
        V3PlatformHost host,
        Func<DateTimeOffset> utcNow)
        : this(syntheticState, host, utcNow, null)
    {
    }

    internal V3ApiHandler(
        SyntheticApiState syntheticState,
        V3PlatformHost host,
        Func<DateTimeOffset> utcNow,
        V3CorpusMount? corpusMount)
    {
        _syntheticState = syntheticState ?? throw new ArgumentNullException(nameof(syntheticState));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _corpusMount = corpusMount;
    }

    internal static RequestDelegate CreateRequestDelegate(
        SyntheticApiState syntheticState,
        Func<DateTimeOffset> utcNow,
        V3CorpusMount? corpusMount = null)
    {
        var api = new V3ApiHandler(syntheticState, new V3PlatformHost(), utcNow, corpusMount);
        return context => api.HandleAsync(context, context.RequestAborted);
    }

    private V3EnvelopeContext UnmountedLuxembourgContext() =>
        new(
            PublisherId.LuLegilux,
            "refusal",
            TimelineSemantics.PublisherApplicability,
            new V3SnapshotReference("no-corpus-mounted", EmptySha256),
            "lu",
            false,
            new V3Freshness(_utcNow(), "unreachable"));

    public async Task HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        var binding = V3RestRouteBinding.Served.FirstOrDefault(served => served.Claims(rawTarget));
        if (binding is not null)
        {
            if (_corpusMount is not null)
            {
                // The host has already validated the body against the bound operation's request
                // document, so the request that reaches the mount is that operation's.
                Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> execute = binding.OperationId switch
                {
                    "as_of" => AsOfOutcome,
                    "timeline" => TimelineOutcome,
                    "article_history" => ArticleHistoryOutcome,
                    "diff" => DiffOutcome,
                    "changes_in_period" => ChangesInPeriodOutcome,
                    "in_force_on" => InForceOnOutcome,
                    _ => ResolveOutcome,
                };
                await V3ResolveRestRoute.HandleOutcomeAsync(
                        binding,
                        context,
                        _host,
                        RequestReference(context.TraceIdentifier),
                        execute,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await V3ResolveRestRoute.HandleRefusalAsync(
                    binding,
                    context,
                    _host,
                    RequestReference(context.TraceIdentifier),
                    UnmountedLuxembourgContext(),
                    static request => NoCorpusMounted(request),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (rawTarget.StartsWith("/api/v3/", StringComparison.Ordinal))
        {
            await V3TransportResponse.WriteAsync(
                    context.Response,
                    V3TransportFailureKind.UnknownRoute,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await SyntheticApiHandler.HandleAsync(context, _syntheticState, cancellationToken).ConfigureAwait(false);
    }

    private V3PlatformOperationOutcome ResolveOutcome(V3PlatformOperationRequest request) =>
        _corpusMount!.Resolve(request, _utcNow());

    private V3PlatformOperationOutcome AsOfOutcome(V3PlatformOperationRequest request) =>
        _corpusMount!.AsOf(request, _utcNow());

    private V3PlatformOperationOutcome TimelineOutcome(V3PlatformOperationRequest request) =>
        _corpusMount!.Timeline(request, _utcNow());

    private V3PlatformOperationOutcome ArticleHistoryOutcome(V3PlatformOperationRequest request) =>
        _corpusMount!.ArticleHistory(request, _utcNow());

    private V3PlatformOperationOutcome DiffOutcome(V3PlatformOperationRequest request) =>
        _corpusMount!.Diff(request, _utcNow());

    private V3PlatformOperationOutcome ChangesInPeriodOutcome(V3PlatformOperationRequest request) =>
        _corpusMount!.ChangesInPeriod(request, _utcNow());

    private V3PlatformOperationOutcome InForceOnOutcome(V3PlatformOperationRequest request) =>
        _corpusMount!.InForceOn(request, _utcNow());

    private static V3PlatformOperationRefusal NoCorpusMounted(V3PlatformOperationRequest request)
    {
        using var helpful = JsonDocument.Parse("{\"required_corpus\":\"lu\"}");
        return new V3PlatformOperationRefusal(
            request,
            "no_corpus_mounted",
            helpful.RootElement);
    }

    private static string RequestReference(string traceIdentifier)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(traceIdentifier ?? string.Empty));
        return "http_" + Convert.ToHexStringLower(bytes);
    }
}
