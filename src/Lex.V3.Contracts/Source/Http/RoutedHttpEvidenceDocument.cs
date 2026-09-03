using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public sealed class RoutedHttpNetworkOrigin
{
    internal RoutedHttpNetworkOrigin(string host, ushort effectivePort)
    {
        Host = host;
        EffectivePort = effectivePort;
    }

    public string Scheme => "https";

    public string Host { get; }

    public ushort EffectivePort { get; }

    internal static RoutedHttpNetworkOrigin FromUri(string requestUri)
    {
        requestUri = RoutedHttpValidation.RequireAbsoluteHttpsUri(requestUri, nameof(requestUri));
        var parsed = new Uri(requestUri, UriKind.Absolute);
        return new RoutedHttpNetworkOrigin(parsed.Host, checked((ushort)parsed.Port));
    }
}

public abstract class RoutedHttpCompletion
{
    private protected RoutedHttpCompletion()
    {
    }
}

public sealed class DeclaredContentLengthHttpCompletion : RoutedHttpCompletion
{
    public DeclaredContentLengthHttpCompletion(ulong declaredLength)
    {
        if (declaredLength > RoutedHttpValidation.MaximumCompleteEntityLength)
        {
            throw new ArgumentOutOfRangeException(nameof(declaredLength));
        }

        DeclaredLength = declaredLength;
    }

    public ulong DeclaredLength { get; }
}

public sealed class PinnedHandlerChunkedEofHttpCompletion : RoutedHttpCompletion
{
    public PinnedHandlerChunkedEofHttpCompletion(string adapterExecutionSha256)
    {
        AdapterExecutionSha256 = RoutedHttpValidation.RequireSha256(
            adapterExecutionSha256,
            nameof(adapterExecutionSha256));
    }

    public string AdapterExecutionSha256 { get; }
}

public sealed class Revalidation304HttpCompletion : RoutedHttpCompletion
{
}

public sealed class ResponseWithoutBodyHttpCompletion : RoutedHttpCompletion
{
}

public sealed class IncompleteHttpCompletion : RoutedHttpCompletion
{
    public IncompleteHttpCompletion(SourceRegistryMemberRef reason)
    {
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        if (!RoutedHttpValidation.IsResponseBearingReason(reason))
        {
            throw new ArgumentException(
                "An HTTP /4 incomplete reason must be a response-bearing member.",
                nameof(reason));
        }
    }

    public SourceRegistryMemberRef Reason { get; }
}

public enum HttpRouteIncompleteReason
{
    HopIncomplete = 1,
    SourceProfileStale = 2,
    RedirectRefused = 3,
    RedirectLoop = 4,
    RedirectLimitExceeded = 5,
    RedirectTargetUnobserved = 6,
    RobotsPolicyUnavailable = 7,
    PublisherServerFailure = 8,
}

public abstract class RoutedHttpRouteOutcome
{
    private protected RoutedHttpRouteOutcome()
    {
    }
}

public sealed class CompleteHttpRouteOutcome : RoutedHttpRouteOutcome
{
}

public sealed class IncompleteHttpRouteOutcome : RoutedHttpRouteOutcome
{
    public IncompleteHttpRouteOutcome(HttpRouteIncompleteReason reason)
    {
        if (!Enum.IsDefined(reason) || reason == HttpRouteIncompleteReason.RedirectTargetUnobserved)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        Reason = reason;
    }

    public HttpRouteIncompleteReason Reason { get; }
}

public sealed class RedirectTargetUnobservedHttpRouteOutcome : RoutedHttpRouteOutcome
{
    public RedirectTargetUnobservedHttpRouteOutcome(
        string logicalRequestSha256,
        string requestStartedAt)
    {
        LogicalRequestSha256 = RoutedHttpValidation.RequireSha256(
            logicalRequestSha256,
            nameof(logicalRequestSha256));
        RequestStartedAt = RoutedHttpValidation.RequireTimestamp(
            requestStartedAt,
            nameof(requestStartedAt));
    }

    public HttpRouteIncompleteReason Reason => HttpRouteIncompleteReason.RedirectTargetUnobserved;

    public string LogicalRequestSha256 { get; }

    public string RequestStartedAt { get; }
}

/// <summary>
/// Closed response-hop evidence data. Construction does not prove that a network request occurred.
/// </summary>
public sealed class RoutedHttpHop
{
    private RoutedHttpHop(
        ulong ordinal,
        string observationId,
        string? antecedentHopObservationId,
        string logicalRequestSha256,
        string requestUri,
        RoutedHttpNetworkOrigin networkOrigin,
        int status,
        HttpStatusDisposition statusDisposition,
        RoutedHttpResponseHeaders headers,
        string requestStartedAt,
        string terminalObservedAt,
        RoutedHttpCompletion completion,
        ulong length,
        string sha256,
        string durableWriteReceiptSha256,
        ulong readbackByteLength,
        string readbackSha256)
    {
        Ordinal = ordinal;
        ObservationId = observationId;
        AntecedentHopObservationId = antecedentHopObservationId;
        LogicalRequestSha256 = logicalRequestSha256;
        RequestUri = requestUri;
        NetworkOrigin = networkOrigin;
        Status = status;
        StatusDisposition = statusDisposition;
        Headers = headers;
        RequestStartedAt = requestStartedAt;
        TerminalObservedAt = terminalObservedAt;
        Completion = completion;
        Length = length;
        Sha256 = sha256;
        DurableWriteReceiptSha256 = durableWriteReceiptSha256;
        ReadbackByteLength = readbackByteLength;
        ReadbackSha256 = readbackSha256;
    }

    public ulong Ordinal { get; }

    public string ObservationId { get; }

    public string? AntecedentHopObservationId { get; }

    public string LogicalRequestSha256 { get; }

    public string RequestUri { get; }

    public RoutedHttpNetworkOrigin NetworkOrigin { get; }

    public string NegotiatedHttpVersion => "http/1.1";

    public int Status { get; }

    public HttpStatusDisposition StatusDisposition { get; }

    public RoutedHttpResponseHeaders Headers { get; }

    public string RequestStartedAt { get; }

    public string TerminalObservedAt { get; }

    public RoutedHttpCompletion Completion { get; }

    public ulong Length { get; }

    public string Sha256 { get; }

    public string DurableWriteReceiptSha256 { get; }

    public ulong ReadbackByteLength { get; }

    public string ReadbackSha256 { get; }

    public static RoutedHttpHop Create(
        ulong ordinal,
        string observationId,
        string? antecedentHopObservationId,
        string logicalRequestSha256,
        string requestUri,
        int status,
        RoutedHttpResponseHeaders headers,
        string requestStartedAt,
        string terminalObservedAt,
        RoutedHttpCompletion completion,
        ulong length,
        string sha256,
        string durableWriteReceiptSha256,
        ulong readbackByteLength,
        string readbackSha256)
    {
        observationId = SourceCoreValidation.RequireUuidUrn(observationId, nameof(observationId));
        if (antecedentHopObservationId is not null)
        {
            antecedentHopObservationId = SourceCoreValidation.RequireUuidUrn(
                antecedentHopObservationId,
                nameof(antecedentHopObservationId));
        }

        logicalRequestSha256 = RoutedHttpValidation.RequireSha256(
            logicalRequestSha256,
            nameof(logicalRequestSha256));
        requestUri = RoutedHttpValidation.RequireAbsoluteHttpsUri(requestUri, nameof(requestUri));
        if (status is < 200 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentNullException.ThrowIfNull(headers);
        requestStartedAt = RoutedHttpValidation.RequireTimestamp(
            requestStartedAt,
            nameof(requestStartedAt));
        terminalObservedAt = RoutedHttpValidation.RequireTimestamp(
            terminalObservedAt,
            nameof(terminalObservedAt));

        ArgumentNullException.ThrowIfNull(completion);
        if (length > RoutedHttpValidation.MaximumRetainedEntityLength ||
            readbackByteLength != length)
        {
            throw new ArgumentException(
                "A routed HTTP hop must retain one bounded payload with an equal readback length.",
                nameof(length));
        }

        sha256 = RoutedHttpValidation.RequireSha256(sha256, nameof(sha256));
        durableWriteReceiptSha256 = RoutedHttpValidation.RequireSha256(
            durableWriteReceiptSha256,
            nameof(durableWriteReceiptSha256));
        readbackSha256 = RoutedHttpValidation.RequireSha256(
            readbackSha256,
            nameof(readbackSha256));
        if (!string.Equals(sha256, readbackSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A routed HTTP hop's retained and readback digests must agree.",
                nameof(readbackSha256));
        }

        if (length == 0 && !string.Equals(sha256, RoutedHttpValidation.EmptyEntitySha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A zero-length retained entity must carry the SHA-256 of the empty byte string.",
                nameof(sha256));
        }

        var statusDisposition = RoutedHttpValidation.ClassifyStatus(status, headers);
        RoutedHttpValidation.RequireCompletionFacts(
            status,
            statusDisposition,
            headers,
            completion,
            length,
            sha256);
        return new RoutedHttpHop(
            ordinal,
            observationId,
            antecedentHopObservationId,
            logicalRequestSha256,
            requestUri,
            RoutedHttpNetworkOrigin.FromUri(requestUri),
            status,
            statusDisposition,
            headers,
            requestStartedAt,
            terminalObservedAt,
            completion,
            length,
            sha256,
            durableWriteReceiptSha256,
            readbackByteLength,
            readbackSha256);
    }
}

/// <summary>
/// Canonical /4 route evidence data. Only the private runtime adapter may mint transport authority.
/// </summary>
public sealed class RoutedHttpEvidence
{
    public const string SchemaId = "lex-license-http-evidence/4";
    private readonly RoutedHttpHop[] _hops;
    private readonly byte[] _canonicalBytes;

    private RoutedHttpEvidence(
        SourceArtifactRef runIdentity,
        ulong requestOrdinal,
        ulong attemptOrdinal,
        RoutedHttpHop[] hops,
        RoutedHttpRouteOutcome outcome)
    {
        RunIdentity = runIdentity;
        RequestOrdinal = requestOrdinal;
        AttemptOrdinal = attemptOrdinal;
        _hops = hops;
        Outcome = outcome;
        _canonicalBytes = RoutedHttpCanonicalJson.WriteEvidence(this);
    }

    public string Schema => SchemaId;

    public SourceArtifactRef RunIdentity { get; }

    public ulong RequestOrdinal { get; }

    public ulong AttemptOrdinal { get; }

    public IReadOnlyList<RoutedHttpHop> Hops => Array.AsReadOnly(_hops);

    public RoutedHttpRouteOutcome Outcome { get; }

    public static RoutedHttpEvidence Create(
        SourceArtifactRef runIdentity,
        ulong requestOrdinal,
        ulong attemptOrdinal,
        IReadOnlyList<RoutedHttpHop> hops,
        RoutedHttpRouteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(runIdentity);
        ArgumentNullException.ThrowIfNull(hops);
        var hopSnapshot = hops.ToArray();
        if (hopSnapshot.Length is < 1 or > 6 || hopSnapshot.Any(static hop => hop is null))
        {
            throw new ArgumentException("HTTP /4 evidence must retain one to six route hops.", nameof(hops));
        }

        for (var index = 0; index < hopSnapshot.Length; index++)
        {
            var hop = hopSnapshot[index];
            var expectedAntecedent = index == 0 ? null : hopSnapshot[index - 1].ObservationId;
            if (hop.Ordinal != (ulong)index ||
                !string.Equals(hop.AntecedentHopObservationId, expectedAntecedent, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Route hops must be ordered and bind exactly their immediate antecedent.",
                    nameof(hops));
            }

            if (index == 0)
            {
                continue;
            }

            var predecessor = hopSnapshot[index - 1];
            if (predecessor.Completion is IncompleteHttpCompletion ||
                predecessor.StatusDisposition != HttpStatusDisposition.RedirectObserved ||
                predecessor.Headers.Location is not RoutedHttpSingleHeader location ||
                !string.Equals(location.Value, hop.RequestUri, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every noninitial hop must be caused by its complete redirect predecessor's exact Location.",
                    nameof(hops));
            }

            if (hopSnapshot.Take(index).Any(previous =>
                    string.Equals(previous.RequestUri, hop.RequestUri, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "A redirect loop must stop before sending a repeated route URI.",
                    nameof(hops));
            }
        }

        if (hopSnapshot.Select(static hop => hop.ObservationId)
            .Distinct(StringComparer.Ordinal).Count() != hopSnapshot.Length)
        {
            throw new ArgumentException("Route observation identities must be unique.", nameof(hops));
        }

        if (requestOrdinal == 0 && hopSnapshot.Any(static hop =>
                !IsExactRobotsRequest(hop.RequestUri)))
        {
            throw new ArgumentException(
                "Request ordinal zero is reserved for the run's robots-policy route.",
                nameof(requestOrdinal));
        }

        ArgumentNullException.ThrowIfNull(outcome);
        var incompleteHopCount = hopSnapshot.Count(static hop => hop.Completion is IncompleteHttpCompletion);
        var hasIncompleteHop = incompleteHopCount != 0;
        if (incompleteHopCount > 1 ||
            hasIncompleteHop && hopSnapshot[^1].Completion is not IncompleteHttpCompletion)
        {
            throw new ArgumentException(
                "At most the terminal route hop may be incomplete.",
                nameof(hops));
        }

        var finalIsRedirect = hopSnapshot[^1].StatusDisposition == HttpStatusDisposition.RedirectObserved;
        switch (outcome)
        {
            case CompleteHttpRouteOutcome:
                if (hasIncompleteHop || finalIsRedirect ||
                    IsRobotsStatusFailure(requestOrdinal, hopSnapshot[^1]) ||
                    hopSnapshot.Take(hopSnapshot.Length - 1).Any(static hop =>
                        hop.StatusDisposition != HttpStatusDisposition.RedirectObserved))
                {
                    throw new ArgumentException(
                        "A complete route must contain only complete hops and exhaust its redirects.",
                        nameof(outcome));
                }

                break;
            case IncompleteHttpRouteOutcome incomplete:
                if (hasIncompleteHop != (incomplete.Reason == HttpRouteIncompleteReason.HopIncomplete))
                {
                    throw new ArgumentException(
                        "Hop incompletion must be represented by the exact route outcome.",
                        nameof(outcome));
                }

                RequireVisibleIncompleteRouteFacts(
                    requestOrdinal,
                    hopSnapshot,
                    incomplete,
                    finalIsRedirect);

                break;
            case RedirectTargetUnobservedHttpRouteOutcome:
                if (hasIncompleteHop || !finalIsRedirect || hopSnapshot.Length >= 6 ||
                    !TryGetAdmittedRedirectTarget(hopSnapshot[^1], out var unobservedTarget) ||
                    hopSnapshot.Any(hop =>
                        string.Equals(hop.RequestUri, unobservedTarget, StringComparison.Ordinal)))
                {
                    throw new ArgumentException(
                        "An unobserved redirect target requires one admissible unsent transition below the redirect ceiling.",
                        nameof(outcome));
                }

                break;
            default:
                throw new ArgumentException("The HTTP route-outcome union is not closed.", nameof(outcome));
        }

        return new RoutedHttpEvidence(
            runIdentity,
            requestOrdinal,
            attemptOrdinal,
            hopSnapshot,
            outcome);
    }

    private static void RequireVisibleIncompleteRouteFacts(
        ulong requestOrdinal,
        IReadOnlyList<RoutedHttpHop> hops,
        IncompleteHttpRouteOutcome outcome,
        bool finalIsRedirect)
    {
        var terminal = hops[^1];
        switch (outcome.Reason)
        {
            case HttpRouteIncompleteReason.HopIncomplete:
                return;
            case HttpRouteIncompleteReason.RobotsPolicyUnavailable:
                if (requestOrdinal != 0 ||
                    !IsExactRobotsRequest(terminal.RequestUri) ||
                    terminal.Status is < 400 or > 499)
                {
                    throw new ArgumentException(
                        "A robots-policy-unavailable outcome requires a complete 4xx response for the exact robots target.",
                        nameof(outcome));
                }

                return;
            case HttpRouteIncompleteReason.PublisherServerFailure:
                if (requestOrdinal != 0 ||
                    !IsExactRobotsRequest(terminal.RequestUri) ||
                    terminal.Status is < 500 or > 599)
                {
                    throw new ArgumentException(
                        "A publisher-server-failure outcome requires a complete 5xx response for the exact robots target.",
                        nameof(outcome));
                }

                return;
            case HttpRouteIncompleteReason.SourceProfileStale:
                if (IsRobotsStatusFailure(requestOrdinal, terminal) ||
                    finalIsRedirect &&
                    (!TryGetAdmittedRedirectTarget(terminal, out var staleTarget) ||
                     hops.Any(hop => string.Equals(hop.RequestUri, staleTarget, StringComparison.Ordinal)) ||
                     hops.Count == 6))
                {
                    throw new ArgumentException(
                        "A document-visible redirect refusal, loop, or limit cannot be relabelled as source-profile staleness.",
                        nameof(outcome));
                }

                return;
            case HttpRouteIncompleteReason.RedirectRefused:
                if (!finalIsRedirect || TryGetAdmittedRedirectTarget(terminal, out _))
                {
                    throw new ArgumentException(
                        "A redirect refusal must terminate at a redirect whose target is not an admitted absolute HTTPS URI.",
                        nameof(outcome));
                }

                return;
            case HttpRouteIncompleteReason.RedirectLoop:
                if (!finalIsRedirect || !TryGetAdmittedRedirectTarget(terminal, out var loopTarget) ||
                    !hops.Any(hop => string.Equals(hop.RequestUri, loopTarget, StringComparison.Ordinal)))
                {
                    throw new ArgumentException(
                        "A redirect-loop outcome must retain a terminal redirect back to an observed route URI.",
                        nameof(outcome));
                }

                return;
            case HttpRouteIncompleteReason.RedirectLimitExceeded:
                if (!finalIsRedirect || hops.Count != 6 ||
                    !TryGetAdmittedRedirectTarget(terminal, out var excessTarget) ||
                    hops.Any(hop => string.Equals(hop.RequestUri, excessTarget, StringComparison.Ordinal)))
                {
                    throw new ArgumentException(
                        "A redirect-limit outcome requires six observed hops and one further admissible transition.",
                        nameof(outcome));
                }

                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }

    private static bool TryGetAdmittedRedirectTarget(RoutedHttpHop hop, out string target)
    {
        target = string.Empty;
        if (hop.Headers.Location is not RoutedHttpSingleHeader location)
        {
            return false;
        }

        try
        {
            target = RoutedHttpValidation.RequireAbsoluteHttpsUri(location.Value, nameof(hop));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsRobotsStatusFailure(ulong requestOrdinal, RoutedHttpHop hop) =>
        requestOrdinal == 0 &&
        IsExactRobotsRequest(hop.RequestUri) &&
        hop.Status is >= 400 and <= 599;

    private static bool IsExactRobotsRequest(string requestUri)
    {
        var uri = new Uri(requestUri, UriKind.Absolute);
        return string.Equals(uri.PathAndQuery, "/robots.txt", StringComparison.Ordinal);
    }

    public static RoutedHttpEvidence ParseAndVerify(ReadOnlySpan<byte> canonicalBytes) =>
        RoutedHttpCanonicalJson.ParseEvidence(canonicalBytes);

    public byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();
}

public enum HeldAcquisitionPublisher
{
    LuLegilux = 1,
    EuEurLex = 2,
}

public sealed record HeldAcquisitionCoordinate
{
    public HeldAcquisitionCoordinate(
        string work,
        string version,
        string language,
        string manifestation)
    {
        Work = RoutedHttpValidation.RequireCoordinateValue(work, nameof(work));
        Version = RoutedHttpValidation.RequireCoordinateValue(version, nameof(version));
        Language = RoutedHttpValidation.RequireLanguage(language, nameof(language));
        Manifestation = RoutedHttpValidation.RequireCoordinateValue(
            manifestation,
            nameof(manifestation));
    }

    public string Work { get; }

    public string Version { get; }

    public string Language { get; }

    public string Manifestation { get; }
}

public sealed record HeldAcquisitionRequestBinding
{
    public HeldAcquisitionRequestBinding(
        string enumerationCompletionSha256,
        string acquisitionPlanSha256,
        string logicalRequestSha256)
    {
        EnumerationCompletionSha256 = RoutedHttpValidation.RequireSha256(
            enumerationCompletionSha256,
            nameof(enumerationCompletionSha256));
        AcquisitionPlanSha256 = RoutedHttpValidation.RequireSha256(
            acquisitionPlanSha256,
            nameof(acquisitionPlanSha256));
        LogicalRequestSha256 = RoutedHttpValidation.RequireSha256(
            logicalRequestSha256,
            nameof(logicalRequestSha256));
    }

    public string EnumerationCompletionSha256 { get; }

    public string AcquisitionPlanSha256 { get; }

    public string LogicalRequestSha256 { get; }
}

public sealed record HeldAcquisitionTransportBinding
{
    public HeldAcquisitionTransportBinding(string httpEvidenceSha256, ulong terminalHopOrdinal)
    {
        HttpEvidenceSha256 = RoutedHttpValidation.RequireSha256(
            httpEvidenceSha256,
            nameof(httpEvidenceSha256));
        if (terminalHopOrdinal > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(terminalHopOrdinal));
        }

        TerminalHopOrdinal = terminalHopOrdinal;
    }

    public string HttpEvidenceSha256 { get; }

    public ulong TerminalHopOrdinal { get; }
}

public sealed record HeldAcquisitionPayload
{
    public HeldAcquisitionPayload(
        ulong length,
        string sha256,
        string durableWriteReceiptSha256,
        ulong readbackByteLength,
        string readbackSha256)
    {
        if (length > RoutedHttpValidation.MaximumCompleteEntityLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Length = length;
        Sha256 = RoutedHttpValidation.RequireSha256(sha256, nameof(sha256));
        DurableWriteReceiptSha256 = RoutedHttpValidation.RequireSha256(
            durableWriteReceiptSha256,
            nameof(durableWriteReceiptSha256));
        ReadbackByteLength = readbackByteLength;
        ReadbackSha256 = RoutedHttpValidation.RequireSha256(
            readbackSha256,
            nameof(readbackSha256));
        if (ReadbackByteLength != Length || !string.Equals(Sha256, ReadbackSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A held payload's retained and readback length and digest must agree.",
                nameof(readbackByteLength));
        }

        if (Length == 0 && !string.Equals(Sha256, RoutedHttpValidation.EmptyEntitySha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A zero-length held payload must carry the SHA-256 of the empty byte string.",
                nameof(sha256));
        }
    }

    public ulong Length { get; }

    public string Sha256 { get; }

    public string DurableWriteReceiptSha256 { get; }

    public ulong ReadbackByteLength { get; }

    public string ReadbackSha256 { get; }
}

/// <summary>
/// Canonical held-acquisition receipt data. No public construction or parsing boundary exists
/// until a runtime producer establishes its transitive plan, transport, coordinate and custody
/// predicates.
/// </summary>
public sealed class HeldAcquisitionReceipt
{
    public const string SchemaId = "lex-held-acquisition-receipt/4";
    private readonly byte[] _canonicalBytes;

    private HeldAcquisitionReceipt(
        HeldAcquisitionPublisher publisher,
        SourceArtifactRef runIdentity,
        ulong requestOrdinal,
        ulong attemptOrdinal,
        HeldAcquisitionCoordinate coordinate,
        HeldAcquisitionRequestBinding request,
        HeldAcquisitionTransportBinding transport,
        HeldAcquisitionPayload payload,
        string createdAt)
    {
        Publisher = publisher;
        RunIdentity = runIdentity;
        RequestOrdinal = requestOrdinal;
        AttemptOrdinal = attemptOrdinal;
        Coordinate = coordinate;
        Request = request;
        Transport = transport;
        Payload = payload;
        CreatedAt = createdAt;
        _canonicalBytes = RoutedHttpCanonicalJson.WriteHeldReceipt(this);
    }

    public string Schema => SchemaId;

    public HeldAcquisitionPublisher Publisher { get; }

    public SourceArtifactRef RunIdentity { get; }

    public ulong RequestOrdinal { get; }

    public ulong AttemptOrdinal { get; }

    public HeldAcquisitionCoordinate Coordinate { get; }

    public HeldAcquisitionRequestBinding Request { get; }

    public HeldAcquisitionTransportBinding Transport { get; }

    public HeldAcquisitionPayload Payload { get; }

    public string CreatedAt { get; }

    internal static HeldAcquisitionReceipt Create(
        HeldAcquisitionPublisher publisher,
        SourceArtifactRef runIdentity,
        ulong requestOrdinal,
        ulong attemptOrdinal,
        HeldAcquisitionCoordinate coordinate,
        HeldAcquisitionRequestBinding request,
        HeldAcquisitionTransportBinding transport,
        HeldAcquisitionPayload payload,
        string createdAt)
    {
        if (!Enum.IsDefined(publisher))
        {
            throw new ArgumentOutOfRangeException(nameof(publisher));
        }

        ArgumentNullException.ThrowIfNull(runIdentity);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(payload);
        if (requestOrdinal == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestOrdinal),
                "Request ordinal zero is reserved for robots policy retrieval and can never identify a held acquisition.");
        }

        createdAt = RoutedHttpValidation.RequireTimestamp(createdAt, nameof(createdAt));
        return new HeldAcquisitionReceipt(
            publisher,
            runIdentity,
            requestOrdinal,
            attemptOrdinal,
            coordinate,
            request,
            transport,
            payload,
            createdAt);
    }

    internal static HeldAcquisitionReceipt ParseAndVerify(ReadOnlySpan<byte> canonicalBytes) =>
        RoutedHttpCanonicalJson.ParseHeldReceipt(canonicalBytes);

    public byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();
}

internal static partial class RoutedHttpCanonicalJson
{
    public static byte[] WriteEvidence(RoutedHttpEvidence value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new RoutedHttpTextWriter();
        writer.Raw("{\"schema\":\"lex-license-http-evidence/4\",\"run_identity\":");
        WriteArtifactRef(writer, value.RunIdentity);
        writer.Raw(",\"request_ordinal\":");
        writer.UInt64(value.RequestOrdinal);
        writer.Raw(",\"attempt_ordinal\":");
        writer.UInt64(value.AttemptOrdinal);
        writer.Raw(",\"hops\":[");
        for (var index = 0; index < value.Hops.Count; index++)
        {
            if (index > 0)
            {
                writer.Raw(",");
            }

            WriteHop(writer, value.Hops[index]);
        }

        writer.Raw("],\"outcome\":");
        WriteOutcome(writer, value.Outcome);
        writer.Raw("}\n");
        return writer.ToUtf8();
    }

    public static RoutedHttpEvidence ParseEvidence(ReadOnlySpan<byte> canonicalBytes)
    {
        try
        {
            var json = RoutedHttpValidation.DecodeStrictUtf8(canonicalBytes, nameof(canonicalBytes));
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            var root = document.RootElement;
            RoutedHttpValidation.RequireExactPropertyNames(
                root,
                ["schema", "run_identity", "request_ordinal", "attempt_ordinal", "hops", "outcome"],
                nameof(canonicalBytes));
            if (!string.Equals(root.GetProperty("schema").GetString(), RoutedHttpEvidence.SchemaId, StringComparison.Ordinal))
            {
                throw new ArgumentException("HTTP route evidence has the wrong schema.", nameof(canonicalBytes));
            }

            var hopsElement = root.GetProperty("hops");
            if (hopsElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("HTTP route hops must be an array.", nameof(canonicalBytes));
            }

            var rebuilt = RoutedHttpEvidence.Create(
                ParseArtifactRef(root.GetProperty("run_identity"), nameof(canonicalBytes)),
                root.GetProperty("request_ordinal").GetUInt64(),
                root.GetProperty("attempt_ordinal").GetUInt64(),
                hopsElement.EnumerateArray().Select(element => ParseHop(element, nameof(canonicalBytes))).ToArray(),
                ParseOutcome(root.GetProperty("outcome"), nameof(canonicalBytes)));
            if (!canonicalBytes.SequenceEqual(rebuilt.CopyCanonicalBytes()))
            {
                throw new ArgumentException(
                    "HTTP route evidence is not its exact canonical typed representation.",
                    nameof(canonicalBytes));
            }

            return rebuilt;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException)
        {
            throw new ArgumentException(
                "HTTP route evidence is not one valid closed canonical object.",
                nameof(canonicalBytes),
                exception);
        }
    }

    public static byte[] WriteHeldReceipt(HeldAcquisitionReceipt value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new RoutedHttpTextWriter();
        writer.Raw("{\"schema\":\"lex-held-acquisition-receipt/4\",\"publisher\":");
        writer.String(PublisherWire(value.Publisher));
        writer.Raw(",\"run_identity\":");
        WriteArtifactRef(writer, value.RunIdentity);
        writer.Raw(",\"request_ordinal\":");
        writer.UInt64(value.RequestOrdinal);
        writer.Raw(",\"attempt_ordinal\":");
        writer.UInt64(value.AttemptOrdinal);
        writer.Raw(",\"coordinate\":{\"work\":");
        writer.String(value.Coordinate.Work);
        writer.Raw(",\"version\":");
        writer.String(value.Coordinate.Version);
        writer.Raw(",\"language\":");
        writer.String(value.Coordinate.Language);
        writer.Raw(",\"manifestation\":");
        writer.String(value.Coordinate.Manifestation);
        writer.Raw("},\"request\":{\"enumeration_completion_sha256\":");
        writer.String(value.Request.EnumerationCompletionSha256);
        writer.Raw(",\"acquisition_plan_sha256\":");
        writer.String(value.Request.AcquisitionPlanSha256);
        writer.Raw(",\"logical_request_sha256\":");
        writer.String(value.Request.LogicalRequestSha256);
        writer.Raw("},\"transport\":{\"http_evidence_sha256\":");
        writer.String(value.Transport.HttpEvidenceSha256);
        writer.Raw(",\"terminal_hop_ordinal\":");
        writer.UInt64(value.Transport.TerminalHopOrdinal);
        writer.Raw("},\"payload\":{\"length\":");
        writer.UInt64(value.Payload.Length);
        writer.Raw(",\"sha256\":");
        writer.String(value.Payload.Sha256);
        writer.Raw(",\"durable_write_receipt_sha256\":");
        writer.String(value.Payload.DurableWriteReceiptSha256);
        writer.Raw(",\"readback_byte_length\":");
        writer.UInt64(value.Payload.ReadbackByteLength);
        writer.Raw(",\"readback_sha256\":");
        writer.String(value.Payload.ReadbackSha256);
        writer.Raw("},\"created_at\":");
        writer.String(value.CreatedAt);
        writer.Raw("}\n");
        return writer.ToUtf8();
    }

    private static string PublisherWire(HeldAcquisitionPublisher publisher) => publisher switch
    {
        HeldAcquisitionPublisher.LuLegilux => "lu-legilux",
        HeldAcquisitionPublisher.EuEurLex => "eu-eurlex",
        _ => throw new ArgumentOutOfRangeException(nameof(publisher)),
    };

    public static HeldAcquisitionReceipt ParseHeldReceipt(ReadOnlySpan<byte> canonicalBytes)
    {
        try
        {
            var json = RoutedHttpValidation.DecodeStrictUtf8(canonicalBytes, nameof(canonicalBytes));
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            RoutedHttpValidation.RequireExactPropertyNames(
                root,
                [
                    "schema", "publisher", "run_identity", "request_ordinal", "attempt_ordinal",
                    "coordinate", "request", "transport", "payload", "created_at",
                ],
                nameof(canonicalBytes));
            if (!string.Equals(root.GetProperty("schema").GetString(), HeldAcquisitionReceipt.SchemaId, StringComparison.Ordinal))
            {
                throw new ArgumentException("A held-acquisition receipt has the wrong schema.", nameof(canonicalBytes));
            }

            var coordinate = root.GetProperty("coordinate");
            RoutedHttpValidation.RequireExactPropertyNames(
                coordinate,
                ["work", "version", "language", "manifestation"],
                nameof(canonicalBytes));
            var request = root.GetProperty("request");
            RoutedHttpValidation.RequireExactPropertyNames(
                request,
                ["enumeration_completion_sha256", "acquisition_plan_sha256", "logical_request_sha256"],
                nameof(canonicalBytes));
            var transport = root.GetProperty("transport");
            RoutedHttpValidation.RequireExactPropertyNames(
                transport,
                ["http_evidence_sha256", "terminal_hop_ordinal"],
                nameof(canonicalBytes));
            var payload = root.GetProperty("payload");
            RoutedHttpValidation.RequireExactPropertyNames(
                payload,
                ["length", "sha256", "durable_write_receipt_sha256", "readback_byte_length", "readback_sha256"],
                nameof(canonicalBytes));

            var publisher = root.GetProperty("publisher").GetString() switch
            {
                "lu-legilux" => HeldAcquisitionPublisher.LuLegilux,
                "eu-eurlex" => HeldAcquisitionPublisher.EuEurLex,
                _ => throw new ArgumentException("The held-acquisition publisher is not closed.", nameof(canonicalBytes)),
            };
            var rebuilt = HeldAcquisitionReceipt.Create(
                publisher,
                ParseArtifactRef(root.GetProperty("run_identity"), nameof(canonicalBytes)),
                root.GetProperty("request_ordinal").GetUInt64(),
                root.GetProperty("attempt_ordinal").GetUInt64(),
                new HeldAcquisitionCoordinate(
                    coordinate.GetProperty("work").GetString()!,
                    coordinate.GetProperty("version").GetString()!,
                    coordinate.GetProperty("language").GetString()!,
                    coordinate.GetProperty("manifestation").GetString()!),
                new HeldAcquisitionRequestBinding(
                    request.GetProperty("enumeration_completion_sha256").GetString()!,
                    request.GetProperty("acquisition_plan_sha256").GetString()!,
                    request.GetProperty("logical_request_sha256").GetString()!),
                new HeldAcquisitionTransportBinding(
                    transport.GetProperty("http_evidence_sha256").GetString()!,
                    transport.GetProperty("terminal_hop_ordinal").GetUInt64()),
                new HeldAcquisitionPayload(
                    payload.GetProperty("length").GetUInt64(),
                    payload.GetProperty("sha256").GetString()!,
                    payload.GetProperty("durable_write_receipt_sha256").GetString()!,
                    payload.GetProperty("readback_byte_length").GetUInt64(),
                    payload.GetProperty("readback_sha256").GetString()!),
                root.GetProperty("created_at").GetString()!);
            if (!canonicalBytes.SequenceEqual(rebuilt.CopyCanonicalBytes()))
            {
                throw new ArgumentException(
                    "The held-acquisition receipt is not its exact canonical typed representation.",
                    nameof(canonicalBytes));
            }

            return rebuilt;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException)
        {
            throw new ArgumentException(
                "The held-acquisition receipt is not one valid closed canonical object.",
                nameof(canonicalBytes),
                exception);
        }
    }

    private static void WriteArtifactRef(RoutedHttpTextWriter writer, SourceArtifactRef value)
    {
        writer.Raw("{\"resource_id\":");
        writer.String(value.ResourceId);
        writer.Raw(",\"sha256\":");
        writer.String(value.Sha256);
        writer.Raw("}");
    }

    private static void WriteRegistryMemberRef(
        RoutedHttpTextWriter writer,
        SourceRegistryMemberRef value)
    {
        writer.Raw("{\"registry_ref\":");
        WriteArtifactRef(writer, value.RegistryRef);
        writer.Raw(",\"member_key\":");
        writer.String(value.MemberKey);
        writer.Raw("}");
    }

    private static void WriteHop(RoutedHttpTextWriter writer, RoutedHttpHop value)
    {
        writer.Raw("{\"ordinal\":");
        writer.UInt64(value.Ordinal);
        writer.Raw(",\"observation_id\":");
        writer.String(value.ObservationId);
        writer.Raw(",\"antecedent_hop_observation_id\":");
        if (value.AntecedentHopObservationId is null)
        {
            writer.Raw("null");
        }
        else
        {
            writer.String(value.AntecedentHopObservationId);
        }

        writer.Raw(",\"logical_request_sha256\":");
        writer.String(value.LogicalRequestSha256);
        writer.Raw(",\"request_uri\":");
        writer.String(value.RequestUri);
        writer.Raw(",\"network_origin\":{\"scheme\":\"https\",\"host\":");
        writer.String(value.NetworkOrigin.Host);
        writer.Raw(",\"effective_port\":");
        writer.UInt64(value.NetworkOrigin.EffectivePort);
        writer.Raw("},\"negotiated_http_version\":\"http/1.1\",\"status\":");
        writer.UInt64((ulong)value.Status);
        writer.Raw(",\"status_disposition\":");
        writer.String(RoutedHttpValidation.StatusDispositionWire(value.StatusDisposition));
        writer.Raw(",\"headers\":");
        WriteResponseHeaders(writer, value.Headers);
        writer.Raw(",\"request_started_at\":");
        writer.String(value.RequestStartedAt);
        writer.Raw(",\"terminal_observed_at\":");
        writer.String(value.TerminalObservedAt);
        writer.Raw(",\"completion\":");
        WriteCompletion(writer, value.Completion);
        writer.Raw(",\"length\":");
        writer.UInt64(value.Length);
        writer.Raw(",\"sha256\":");
        writer.String(value.Sha256);
        writer.Raw(",\"durable_write_receipt_sha256\":");
        writer.String(value.DurableWriteReceiptSha256);
        writer.Raw(",\"readback_byte_length\":");
        writer.UInt64(value.ReadbackByteLength);
        writer.Raw(",\"readback_sha256\":");
        writer.String(value.ReadbackSha256);
        writer.Raw("}");
    }

    private static void WriteCompletion(RoutedHttpTextWriter writer, RoutedHttpCompletion value)
    {
        switch (value)
        {
            case DeclaredContentLengthHttpCompletion declared:
                writer.Raw("{\"kind\":\"declared_content_length_complete\",\"declared_length\":");
                writer.UInt64(declared.DeclaredLength);
                writer.Raw("}");
                return;
            case PinnedHandlerChunkedEofHttpCompletion chunked:
                writer.Raw("{\"kind\":\"pinned_handler_chunked_eof\",\"adapter_execution_sha256\":");
                writer.String(chunked.AdapterExecutionSha256);
                writer.Raw("}");
                return;
            case Revalidation304HttpCompletion:
                writer.Raw("{\"kind\":\"revalidation_304\"}");
                return;
            case ResponseWithoutBodyHttpCompletion:
                writer.Raw("{\"kind\":\"response_without_body\"}");
                return;
            case IncompleteHttpCompletion incomplete:
                writer.Raw("{\"kind\":\"incomplete\",\"reason\":");
                WriteRegistryMemberRef(writer, incomplete.Reason);
                writer.Raw("}");
                return;
            default:
                throw new ArgumentException("The HTTP completion union is not closed.", nameof(value));
        }
    }

    private static void WriteOutcome(RoutedHttpTextWriter writer, RoutedHttpRouteOutcome value)
    {
        switch (value)
        {
            case CompleteHttpRouteOutcome:
                writer.Raw("{\"kind\":\"complete\"}");
                return;
            case IncompleteHttpRouteOutcome incomplete:
                writer.Raw("{\"kind\":\"incomplete\",\"reason\":");
                writer.String(RoutedHttpValidation.RouteReasonWire(incomplete.Reason));
                writer.Raw("}");
                return;
            case RedirectTargetUnobservedHttpRouteOutcome target:
                writer.Raw("{\"kind\":\"incomplete\",\"reason\":\"redirect_target_unobserved\",\"logical_request_sha256\":");
                writer.String(target.LogicalRequestSha256);
                writer.Raw(",\"request_started_at\":");
                writer.String(target.RequestStartedAt);
                writer.Raw("}");
                return;
            default:
                throw new ArgumentException("The HTTP route-outcome union is not closed.", nameof(value));
        }
    }

    private static RoutedHttpHop ParseHop(JsonElement element, string parameterName)
    {
        RoutedHttpValidation.RequireExactPropertyNames(
            element,
            [
                "ordinal", "observation_id", "antecedent_hop_observation_id",
                "logical_request_sha256", "request_uri", "network_origin",
                "negotiated_http_version", "status", "status_disposition", "headers",
                "request_started_at", "terminal_observed_at", "completion", "length", "sha256",
                "durable_write_receipt_sha256", "readback_byte_length", "readback_sha256",
            ],
            parameterName);
        var origin = element.GetProperty("network_origin");
        RoutedHttpValidation.RequireExactPropertyNames(
            origin,
            ["scheme", "host", "effective_port"],
            parameterName);
        if (!string.Equals(
                element.GetProperty("negotiated_http_version").GetString(),
                "http/1.1",
                StringComparison.Ordinal))
        {
            throw new ArgumentException("HTTP /4 admits only negotiated HTTP/1.1.", parameterName);
        }

        var antecedentElement = element.GetProperty("antecedent_hop_observation_id");
        var antecedent = antecedentElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => antecedentElement.GetString(),
            _ => throw new ArgumentException("The antecedent identity must be null or a UUID URN.", parameterName),
        };
        var hop = RoutedHttpHop.Create(
            element.GetProperty("ordinal").GetUInt64(),
            element.GetProperty("observation_id").GetString()!,
            antecedent,
            element.GetProperty("logical_request_sha256").GetString()!,
            element.GetProperty("request_uri").GetString()!,
            element.GetProperty("status").GetInt32(),
            ParseHeaders(element.GetProperty("headers"), parameterName),
            element.GetProperty("request_started_at").GetString()!,
            element.GetProperty("terminal_observed_at").GetString()!,
            ParseCompletion(element.GetProperty("completion"), parameterName),
            element.GetProperty("length").GetUInt64(),
            element.GetProperty("sha256").GetString()!,
            element.GetProperty("durable_write_receipt_sha256").GetString()!,
            element.GetProperty("readback_byte_length").GetUInt64(),
            element.GetProperty("readback_sha256").GetString()!);
        if (!string.Equals(origin.GetProperty("scheme").GetString(), hop.NetworkOrigin.Scheme, StringComparison.Ordinal) ||
            !string.Equals(origin.GetProperty("host").GetString(), hop.NetworkOrigin.Host, StringComparison.Ordinal) ||
            origin.GetProperty("effective_port").GetUInt16() != hop.NetworkOrigin.EffectivePort ||
            !string.Equals(
                element.GetProperty("status_disposition").GetString(),
                RoutedHttpValidation.StatusDispositionWire(hop.StatusDisposition),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Derived HTTP hop fields do not match their retained inputs.", parameterName);
        }

        return hop;
    }

    private static RoutedHttpResponseHeaders ParseHeaders(JsonElement element, string parameterName)
    {
        string[] names =
        [
            "content_type", "content_length", "content_encoding", "transfer_encoding",
            "content_range", "etag", "last_modified", "location", "cache_control", "expires",
            "date", "age", "tcn",
        ];
        RoutedHttpValidation.RequireExactPropertyNames(element, names, parameterName);
        var fields = names.Select(name => ParseHeaderField(element.GetProperty(name), parameterName)).ToArray();
        return new RoutedHttpResponseHeaders(
            fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6],
            fields[7], fields[8], fields[9], fields[10], fields[11], fields[12]);
    }

    private static RoutedHttpHeaderField ParseHeaderField(JsonElement element, string parameterName)
    {
        var properties = element.EnumerateObject().Select(static property => property.Name).ToArray();
        var kind = element.GetProperty("kind").GetString();
        switch (kind)
        {
            case "absent":
                RoutedHttpValidation.RequireExactPropertyNames(element, ["kind"], parameterName);
                return new RoutedHttpAbsentHeader();
            case "single":
                RoutedHttpValidation.RequireExactPropertyNames(element, ["kind", "value"], parameterName);
                return new RoutedHttpSingleHeader(element.GetProperty("value").GetString()!);
            case "multiple":
                RoutedHttpValidation.RequireExactPropertyNames(element, ["kind", "values"], parameterName);
                var values = element.GetProperty("values");
                if (values.ValueKind != JsonValueKind.Array)
                {
                    throw new ArgumentException("Multiple HTTP values must be an array.", parameterName);
                }

                return new RoutedHttpMultipleHeader(
                    values.EnumerateArray().Select(static value => value.GetString()!).ToArray());
            default:
                throw new ArgumentException("The HTTP header-field kind is not closed.", parameterName);
        }
    }

    private static RoutedHttpCompletion ParseCompletion(JsonElement element, string parameterName)
    {
        var kind = element.GetProperty("kind").GetString();
        switch (kind)
        {
            case "declared_content_length_complete":
                RoutedHttpValidation.RequireExactPropertyNames(
                    element,
                    ["kind", "declared_length"],
                    parameterName);
                return new DeclaredContentLengthHttpCompletion(
                    element.GetProperty("declared_length").GetUInt64());
            case "pinned_handler_chunked_eof":
                RoutedHttpValidation.RequireExactPropertyNames(
                    element,
                    ["kind", "adapter_execution_sha256"],
                    parameterName);
                return new PinnedHandlerChunkedEofHttpCompletion(
                    element.GetProperty("adapter_execution_sha256").GetString()!);
            case "revalidation_304":
                RoutedHttpValidation.RequireExactPropertyNames(element, ["kind"], parameterName);
                return new Revalidation304HttpCompletion();
            case "response_without_body":
                RoutedHttpValidation.RequireExactPropertyNames(element, ["kind"], parameterName);
                return new ResponseWithoutBodyHttpCompletion();
            case "incomplete":
                RoutedHttpValidation.RequireExactPropertyNames(element, ["kind", "reason"], parameterName);
                return new IncompleteHttpCompletion(
                    ParseRegistryMemberRef(element.GetProperty("reason"), parameterName));
            default:
                throw new ArgumentException("The HTTP completion kind is not closed.", parameterName);
        }
    }

    private static RoutedHttpRouteOutcome ParseOutcome(JsonElement element, string parameterName)
    {
        var kind = element.GetProperty("kind").GetString();
        if (string.Equals(kind, "complete", StringComparison.Ordinal))
        {
            RoutedHttpValidation.RequireExactPropertyNames(element, ["kind"], parameterName);
            return new CompleteHttpRouteOutcome();
        }

        if (!string.Equals(kind, "incomplete", StringComparison.Ordinal))
        {
            throw new ArgumentException("The HTTP route-outcome kind is not closed.", parameterName);
        }

        var reason = RoutedHttpValidation.ParseRouteReason(
            element.GetProperty("reason").GetString()!,
            parameterName);
        if (reason == HttpRouteIncompleteReason.RedirectTargetUnobserved)
        {
            RoutedHttpValidation.RequireExactPropertyNames(
                element,
                ["kind", "reason", "logical_request_sha256", "request_started_at"],
                parameterName);
            return new RedirectTargetUnobservedHttpRouteOutcome(
                element.GetProperty("logical_request_sha256").GetString()!,
                element.GetProperty("request_started_at").GetString()!);
        }

        RoutedHttpValidation.RequireExactPropertyNames(element, ["kind", "reason"], parameterName);
        return new IncompleteHttpRouteOutcome(reason);
    }

    private static SourceArtifactRef ParseArtifactRef(JsonElement element, string parameterName)
    {
        RoutedHttpValidation.RequireExactPropertyNames(
            element,
            ["resource_id", "sha256"],
            parameterName);
        return new SourceArtifactRef(
            element.GetProperty("resource_id").GetString()!,
            element.GetProperty("sha256").GetString()!);
    }

    private static SourceRegistryMemberRef ParseRegistryMemberRef(
        JsonElement element,
        string parameterName)
    {
        RoutedHttpValidation.RequireExactPropertyNames(
            element,
            ["registry_ref", "member_key"],
            parameterName);
        return new SourceRegistryMemberRef(
            ParseArtifactRef(element.GetProperty("registry_ref"), parameterName),
            element.GetProperty("member_key").GetString()!);
    }
}

internal static partial class RoutedHttpValidation
{
    public const ulong MaximumCompleteEntityLength = 268_435_455;
    public const ulong MaximumRetainedEntityLength = 268_435_456;
    internal static readonly string EmptyEntitySha256 =
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([])).ToLowerInvariant();

    public static string RequireCoordinateValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, parameterName);
        try
        {
            if (StrictUtf8.GetByteCount(value) > 2048)
            {
                throw new ArgumentException(
                    "A held-acquisition coordinate value exceeds 2,048 UTF-8 bytes.",
                    parameterName);
            }
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "A held-acquisition coordinate value must be strict UTF-8.",
                parameterName,
                exception);
        }

        return value;
    }

    public static string RequireLanguage(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, parameterName);
        if (value.Length > 16 || value[0] == '-' || value[^1] == '-' || value.Contains("--", StringComparison.Ordinal) ||
            value.Any(static character =>
                character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-'))
        {
            throw new ArgumentException(
                "A held-acquisition language must be one bounded lowercase ASCII token.",
                parameterName);
        }

        return value;
    }

    public static string RequireTimestamp(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed) ||
            !string.Equals(
                value,
                parsed.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An HTTP /4 timestamp must use exact seven-digit UTC Z form.",
                parameterName);
        }

        return value;
    }

    public static HttpStatusDisposition ClassifyStatus(
        int status,
        RoutedHttpResponseHeaders headers) =>
        HttpStatusClassifier.Classify(
            status,
            headers.ContentRange is not RoutedHttpAbsentHeader);

    public static string StatusDispositionWire(HttpStatusDisposition value) => value switch
    {
        HttpStatusDisposition.DerivableStatus => "derivable_status",
        HttpStatusDisposition.RedirectObserved => "redirect_observed",
        HttpStatusDisposition.RevalidationReferenceOnly => "revalidation_reference_only",
        HttpStatusDisposition.SemanticNoEntityStatus => "semantic_no_entity_status",
        HttpStatusDisposition.RangeNotApproved => "range_not_approved",
        HttpStatusDisposition.NonDerivableStatus => "non_derivable_status",
        HttpStatusDisposition.NegotiationChoiceOffered => "negotiation_choice_offered",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string RouteReasonWire(HttpRouteIncompleteReason value) => value switch
    {
        HttpRouteIncompleteReason.HopIncomplete => "hop_incomplete",
        HttpRouteIncompleteReason.SourceProfileStale => "source_profile_stale",
        HttpRouteIncompleteReason.RedirectRefused => "redirect_refused",
        HttpRouteIncompleteReason.RedirectLoop => "redirect_loop",
        HttpRouteIncompleteReason.RedirectLimitExceeded => "redirect_limit_exceeded",
        HttpRouteIncompleteReason.RedirectTargetUnobserved => "redirect_target_unobserved",
        HttpRouteIncompleteReason.RobotsPolicyUnavailable => "robots_policy_unavailable",
        HttpRouteIncompleteReason.PublisherServerFailure => "publisher_server_failure",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static HttpRouteIncompleteReason ParseRouteReason(string value, string parameterName) =>
        value switch
        {
            "hop_incomplete" => HttpRouteIncompleteReason.HopIncomplete,
            "source_profile_stale" => HttpRouteIncompleteReason.SourceProfileStale,
            "redirect_refused" => HttpRouteIncompleteReason.RedirectRefused,
            "redirect_loop" => HttpRouteIncompleteReason.RedirectLoop,
            "redirect_limit_exceeded" => HttpRouteIncompleteReason.RedirectLimitExceeded,
            "redirect_target_unobserved" => HttpRouteIncompleteReason.RedirectTargetUnobserved,
            "robots_policy_unavailable" => HttpRouteIncompleteReason.RobotsPolicyUnavailable,
            "publisher_server_failure" => HttpRouteIncompleteReason.PublisherServerFailure,
            _ => throw new ArgumentException("The HTTP route reason is not closed.", parameterName),
        };

    public static bool IsResponseBearingReason(SourceRegistryMemberRef reason)
    {
        try
        {
            _ = HttpAcquisitionReasonRegistry.RequirePartial(reason);
            return true;
        }
        catch (ArgumentException)
        {
        }

        try
        {
            _ = HttpAcquisitionReasonRegistry.RequireCompletionUnproven(reason);
            return true;
        }
        catch (ArgumentException)
        {
        }

        try
        {
            _ = HttpAcquisitionReasonRegistry.RequireResponseSemantics(reason);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static void RequireCompletionFacts(
        int status,
        HttpStatusDisposition disposition,
        RoutedHttpResponseHeaders headers,
        RoutedHttpCompletion completion,
        ulong length,
        string sha256)
    {
        if (length == MaximumRetainedEntityLength)
        {
            if (completion is not IncompleteHttpCompletion incomplete ||
                HttpAcquisitionReasonRegistry.RequirePartial(incomplete.Reason) !=
                HttpPartialBodyReason.ByteBoundPreventedCompletion)
            {
                throw new ArgumentException(
                    "The cap sentinel can only be retained as byte-bound incompletion.",
                    nameof(completion));
            }

            return;
        }

        if (completion is IncompleteHttpCompletion incompleteCompletion)
        {
            if (IsReason(incompleteCompletion.Reason, HttpPartialBodyReason.ByteBoundPreventedCompletion))
            {
                throw new ArgumentException(
                    "Byte-bound incompletion requires the exact private sentinel length.",
                    nameof(completion));
            }

            RequireIncompleteFacts(status, headers, incompleteCompletion, length);
            return;
        }

        if (length > MaximumCompleteEntityLength)
        {
            throw new ArgumentException("A complete entity exceeds the admitted ceiling.", nameof(length));
        }

        switch (completion)
        {
            case DeclaredContentLengthHttpCompletion declared:
                if (status is 204 or 304 || headers.TransferEncoding is not RoutedHttpAbsentHeader ||
                    headers.ContentLength is not RoutedHttpSingleHeader contentLength ||
                    !TryParseCanonicalUnsigned(contentLength.Value, out var parsedLength) ||
                    parsedLength != declared.DeclaredLength || parsedLength != length ||
                    status == 205 && length != 0)
                {
                    throw new ArgumentException(
                        "Declared-length completion does not match the retained response facts.",
                        nameof(completion));
                }

                return;
            case PinnedHandlerChunkedEofHttpCompletion:
                if (status is 204 or 304 || headers.ContentLength is not RoutedHttpAbsentHeader ||
                    headers.TransferEncoding is not RoutedHttpSingleHeader transferEncoding ||
                    !string.Equals(
                        transferEncoding.Value.Trim(' ', '\t'),
                        "chunked",
                        StringComparison.OrdinalIgnoreCase) ||
                    status == 205 && length != 0)
                {
                    throw new ArgumentException(
                        "Chunked-EOF completion does not match the retained response facts.",
                        nameof(completion));
                }

                return;
            case Revalidation304HttpCompletion:
                if (status != 304 || disposition != HttpStatusDisposition.RevalidationReferenceOnly ||
                    length != 0 || !string.Equals(sha256, EmptyEntitySha256, StringComparison.Ordinal))
                {
                    throw new ArgumentException("A revalidation completion must be an empty status 304.", nameof(completion));
                }

                return;
            case ResponseWithoutBodyHttpCompletion:
                if (status != 204 || disposition != HttpStatusDisposition.SemanticNoEntityStatus ||
                    headers.ContentLength is not RoutedHttpAbsentHeader ||
                    headers.TransferEncoding is not RoutedHttpAbsentHeader || length != 0 ||
                    !string.Equals(sha256, EmptyEntitySha256, StringComparison.Ordinal))
                {
                    throw new ArgumentException("A response-without-body completion must be an unframed empty 204.", nameof(completion));
                }

                return;
            default:
                throw new ArgumentException("The HTTP completion union is not closed.", nameof(completion));
        }
    }

    private static void RequireIncompleteFacts(
        int status,
        RoutedHttpResponseHeaders headers,
        IncompleteHttpCompletion completion,
        ulong length)
    {
        var reason = completion.Reason.MemberKey;
        var hasContentLength = headers.ContentLength is not RoutedHttpAbsentHeader;
        var hasTransferEncoding = headers.TransferEncoding is not RoutedHttpAbsentHeader;
        ulong declaredLength = 0;
        var hasCanonicalContentLength = headers.ContentLength is RoutedHttpSingleHeader contentLength &&
                                        TryParseCanonicalUnsigned(contentLength.Value, out declaredLength);
        var hasChunkedTransfer = headers.TransferEncoding is RoutedHttpSingleHeader transferEncoding &&
                                 string.Equals(
                                     transferEncoding.Value.Trim(' ', '\t'),
                                     "chunked",
                                     StringComparison.OrdinalIgnoreCase);

        var valid = reason switch
        {
            // These three facts depend on the owned stream operation and can precede any later
            // document-visible framing predicate. PR B binds them to the private runtime event.
            "body_deadline" or "body_read_failure" or "caller_cancelled_after_headers" => true,

            "declared_length_short_read" =>
                status is not 204 and not 304 &&
                hasCanonicalContentLength &&
                !hasTransferEncoding &&
                declaredLength > length,

            // The exact sentinel case returned above. No shorter body may borrow its reason.
            "byte_bound_prevented_completion" => false,

            // A 304 bypasses entity framing completely; a 204 with framing has its own earlier
            // semantic reason. A single canonical value is not invalid merely because transfer
            // later failed before reaching it.
            "invalid_content_length" =>
                status is not 204 and not 304 &&
                hasContentLength &&
                (!hasCanonicalContentLength ||
                 !hasTransferEncoding && declaredLength < length),

            "transfer_coding_conflict" =>
                status is not 204 and not 304 &&
                hasCanonicalContentLength &&
                hasTransferEncoding,

            "unsupported_transfer_coding" =>
                status is not 204 and not 304 &&
                !hasContentLength &&
                hasTransferEncoding &&
                !hasChunkedTransfer,

            "missing_completion_proof" =>
                status is not 204 and not 304 &&
                !hasContentLength &&
                !hasTransferEncoding,

            // Whether the request carried one admitted conditional header is opened and checked
            // by PR B. The retained response can still prove that this reason is 304-only.
            "revalidation_request_not_admitted" => status == 304,

            "status_content_forbidden" =>
                status == 304 && length > 0 ||
                status == 204 && length > 0 && !hasContentLength && !hasTransferEncoding ||
                status == 205 && length > 0 &&
                (hasCanonicalContentLength && declaredLength == length && !hasTransferEncoding ||
                 !hasContentLength && hasChunkedTransfer),

            "status_framing_conflict" =>
                status == 204 && (hasContentLength || hasTransferEncoding),

            _ => false,
        };

        if (!valid)
        {
            throw new ArgumentException(
                "The incomplete reason contradicts the response facts retained in the document.",
                nameof(completion));
        }
    }

    private static bool TryParseCanonicalUnsigned(string value, out ulong parsed)
    {
        parsed = 0;
        return !string.IsNullOrEmpty(value) &&
               ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool IsReason(SourceRegistryMemberRef value, HttpPartialBodyReason expected)
    {
        try
        {
            return HttpAcquisitionReasonRegistry.RequirePartial(value) == expected;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
