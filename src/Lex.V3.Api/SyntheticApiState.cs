using Lex.V3.Artifacts;
using Lex.V3.Contracts;

namespace Lex.V3.Api;

internal sealed class SyntheticApiState : IDisposable
{
    private readonly IRequestEntropySource? entropy;
    private readonly SyntheticSliceVerification? verification;
    private readonly SyntheticIndexResolver? resolver;
    private readonly ComponentIdentity? runtime;
    private readonly Func<SyntheticResolveEnvelope, SyntheticPreparedResponse>? prepareResponse;
    private int fatalUnavailable;

    private SyntheticApiState(
        SyntheticSliceVerification? verification,
        SyntheticIndexResolver? resolver,
        ComponentIdentity? runtime,
        IRequestEntropySource? entropy,
        Func<SyntheticResolveEnvelope, SyntheticPreparedResponse>? prepareResponse)
    {
        this.verification = verification;
        this.resolver = resolver;
        this.runtime = runtime;
        this.entropy = entropy;
        this.prepareResponse = prepareResponse;
    }

    public bool Ready =>
        Volatile.Read(ref fatalUnavailable) == 0 &&
        verification?.Verified == true &&
        resolver is not null;

    public static SyntheticApiState Available(
        SyntheticSliceVerification verification,
        SyntheticIndexResolver resolver,
        ComponentIdentity runtime,
        IRequestEntropySource entropy,
        Func<SyntheticResolveEnvelope, SyntheticPreparedResponse>? prepareResponse = null)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (!verification.Verified)
        {
            throw new ArgumentException("The API state requires an admitted graph.", nameof(verification));
        }

        return new(
            verification,
            resolver ?? throw new ArgumentNullException(nameof(resolver)),
            runtime ?? throw new ArgumentNullException(nameof(runtime)),
            entropy ?? throw new ArgumentNullException(nameof(entropy)),
            prepareResponse ?? PrepareResponse);
    }

    public static SyntheticApiState Unavailable { get; } = new(null, null, null, null, null);

    public async ValueTask<SyntheticPreparedResponse> ResolveAsync(
        SyntheticResolveRequest request,
        CancellationToken cancellationToken)
    {
        if (!Ready ||
            resolver is null ||
            verification is null ||
            runtime is null ||
            entropy is null ||
            prepareResponse is null)
        {
            throw new InvalidOperationException("The synthetic graph is unavailable.");
        }

        try
        {
            var row = await resolver
                .ResolveAsync(request.Family!, request.Coordinate!, cancellationToken)
                .ConfigureAwait(false);
            var requestReference = RequestReferenceFactory.Create(entropy);
            var response = SyntheticResponseMapper.Map(
                verification,
                row,
                request.Family!,
                request.Coordinate!,
                requestReference,
                runtime);
            var prepared = prepareResponse(response);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Ready)
            {
                throw new InvalidOperationException("The synthetic graph became unavailable.");
            }

            return prepared;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Interlocked.Exchange(ref fatalUnavailable, 1);
            throw;
        }
    }

    public void Dispose() => resolver?.Dispose();

    private static SyntheticPreparedResponse PrepareResponse(SyntheticResolveEnvelope response) =>
        new(BoundedJsonBuffer.Serialize(response, 64 * 1024));
}

internal sealed class SyntheticPreparedResponse
{
    private readonly byte[] bytes;

    public SyntheticPreparedResponse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is <= 0 or > 64 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        this.bytes = bytes;
    }

    public ReadOnlyMemory<byte> Utf8Json => bytes;
}
