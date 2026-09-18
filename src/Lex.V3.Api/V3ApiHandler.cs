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
    private readonly V3EnvelopeContext _unmountedLuxembourgContext;

    public V3ApiHandler(SyntheticApiState syntheticState, DateTimeOffset observedAt)
        : this(syntheticState, observedAt, new V3PlatformHost())
    {
    }

    internal V3ApiHandler(
        SyntheticApiState syntheticState,
        DateTimeOffset observedAt,
        V3PlatformHost host)
    {
        _syntheticState = syntheticState ?? throw new ArgumentNullException(nameof(syntheticState));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _unmountedLuxembourgContext = new V3EnvelopeContext(
            PublisherId.LuLegilux,
            "refusal",
            TimelineSemantics.PublisherApplicability,
            new V3SnapshotReference("no-corpus-mounted", EmptySha256),
            "lu",
            false,
            new V3Freshness(observedAt, "unreachable"));
    }

    internal static RequestDelegate CreateRequestDelegate(
        SyntheticApiState syntheticState,
        DateTimeOffset observedAt)
    {
        var api = new V3ApiHandler(syntheticState, observedAt);
        return context => api.HandleAsync(context, context.RequestAborted);
    }

    public async Task HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        if (string.Equals(rawTarget, V3ResolveRestRoute.RawTarget, StringComparison.Ordinal) ||
            rawTarget.StartsWith(V3ResolveRestRoute.RawTarget + "?", StringComparison.Ordinal))
        {
            await V3ResolveRestRoute.HandleRefusalAsync(
                    context,
                    _host,
                    RequestReference(context.TraceIdentifier),
                    _unmountedLuxembourgContext,
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
