using System.Security.Cryptography;
using System.Text;
using Lex.Evaluation;

namespace Lex.Web;

public enum EvaluationAdmissionRegistrationKind { Registered, Replayed, Capacity }

public sealed record EvaluationAdmissionRegistration(
    EvaluationAdmissionRegistrationKind Kind,
    string? Token = null,
    DateTimeOffset? ExpiresAt = null);

public enum EvaluationAdmissionAuthorizationKind
{
    Allowed,
    AlreadyUsed,
    InvalidToken,
    Expired,
    RequestNotAllowed,
}

public sealed class EvaluationAdmissionUse : IDisposable
{
    private readonly Action<bool, string?> _release;
    private int _disposed;

    internal EvaluationAdmissionUse(Action<bool, string?> release) => _release = release;

    public void Commit(string? threadToken = null)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _release(true, threadToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _release(false, null);
    }
}

public sealed class EvaluationAdmissionRegistry
{
    private sealed class Entry(
        EvaluationAdmissionCapability capability,
        long retainedBytes)
    {
        public sealed class InvocationState
        {
            public int CompletedTurns { get; set; }
            public string? ThreadTokenDigest { get; set; }
        }

        public EvaluationAdmissionCapability Capability { get; } = capability;
        public long RetainedBytes { get; } = retainedBytes;
        public HashSet<string> Reserved { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Consumed { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, InvocationState> Invocations { get; } =
            new(StringComparer.Ordinal);
    }

    private readonly object _sync = new();
    private readonly TimeProvider _clock;
    private readonly int _maximumEntries;
    private readonly long _maximumRetainedBytes;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nonceTokens = new(StringComparer.Ordinal);
    private long _retainedBytes;

    public EvaluationAdmissionRegistry(
        TimeProvider clock,
        int maximumEntries = 16,
        long maximumRetainedBytes = 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRetainedBytes);
        _clock = clock;
        _maximumEntries = maximumEntries;
        _maximumRetainedBytes = maximumRetainedBytes;
    }

    public EvaluationAdmissionRegistration Register(
        EvaluationAdmissionCapability capability,
        int capabilityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capabilityBytes);
        lock (_sync)
        {
            RemoveExpired();
            if (_nonceTokens.ContainsKey(capability.Nonce))
                return new(EvaluationAdmissionRegistrationKind.Replayed);
            if (_entries.Count >= _maximumEntries
                || _retainedBytes + capabilityBytes > _maximumRetainedBytes)
                return new(EvaluationAdmissionRegistrationKind.Capacity);
            var token = NewToken();
            var digest = TokenDigest(token);
            _entries.Add(digest, new Entry(capability, capabilityBytes));
            _nonceTokens.Add(capability.Nonce, digest);
            _retainedBytes += capabilityBytes;
            return new(EvaluationAdmissionRegistrationKind.Registered,
                token, capability.ExpiresAt);
        }
    }

    public EvaluationAdmissionAuthorizationKind Inspect(
        string token,
        string idempotencyKey,
        string requestBodySha256,
        string? threadToken = null)
    {
        if (!IsValidToken(token))
            return EvaluationAdmissionAuthorizationKind.InvalidToken;
        lock (_sync)
        {
            var digest = TokenDigest(token);
            if (!_entries.TryGetValue(digest, out var entry))
                return EvaluationAdmissionAuthorizationKind.InvalidToken;
            if (entry.Capability.ExpiresAt <= _clock.GetUtcNow())
            {
                Remove(digest, entry);
                return EvaluationAdmissionAuthorizationKind.Expired;
            }
            var request = Find(entry, idempotencyKey, requestBodySha256);
            if (request is null)
                return EvaluationAdmissionAuthorizationKind.RequestNotAllowed;
            return entry.Reserved.Contains(idempotencyKey)
                   || entry.Consumed.Contains(idempotencyKey)
                ? EvaluationAdmissionAuthorizationKind.AlreadyUsed
                : AcceptsThread(entry, request, threadToken)
                    ? EvaluationAdmissionAuthorizationKind.Allowed
                    : EvaluationAdmissionAuthorizationKind.RequestNotAllowed;
        }
    }

    public EvaluationAdmissionUse? Reserve(
        string token,
        string idempotencyKey,
        string requestBodySha256,
        string? threadToken = null)
    {
        if (!IsValidToken(token)) return null;
        lock (_sync)
        {
            var digest = TokenDigest(token);
            if (!_entries.TryGetValue(digest, out var entry)
                || entry.Capability.ExpiresAt <= _clock.GetUtcNow()
                || Find(entry, idempotencyKey, requestBodySha256) is not { } request
                || entry.Reserved.Contains(idempotencyKey)
                || entry.Consumed.Contains(idempotencyKey)
                || !AcceptsThread(entry, request, threadToken))
                return null;
            entry.Reserved.Add(idempotencyKey);
            return new EvaluationAdmissionUse((committed, resultingThreadToken) =>
                Release(entry, request, committed, resultingThreadToken));
        }
    }

    private void Release(
        Entry entry,
        EvaluationAdmissionRequest request,
        bool committed,
        string? resultingThreadToken)
    {
        lock (_sync)
        {
            if (!entry.Reserved.Remove(request.IdempotencyKey)) return;
            if (!committed) return;
            entry.Consumed.Add(request.IdempotencyKey);
            var hasNext = entry.Capability.AllowedRequests.Any(candidate =>
                string.Equals(candidate.InvocationId, request.InvocationId,
                    StringComparison.Ordinal)
                && candidate.Turn == request.Turn + 1);
            if (!hasNext)
            {
                entry.Invocations.Remove(request.InvocationId);
                return;
            }
            if (!IsValidToken(resultingThreadToken)) return;
            if (!entry.Invocations.TryGetValue(request.InvocationId, out var state))
            {
                state = new Entry.InvocationState();
                entry.Invocations.Add(request.InvocationId, state);
            }
            if (state.CompletedTurns != request.Turn - 1) return;
            var threadDigest = TokenDigest(resultingThreadToken!);
            if (state.ThreadTokenDigest is not null
                && !FixedDigest(state.ThreadTokenDigest, threadDigest))
                return;
            state.CompletedTurns = request.Turn;
            state.ThreadTokenDigest ??= threadDigest;
        }
    }

    private static bool AcceptsThread(
        Entry entry,
        EvaluationAdmissionRequest request,
        string? threadToken)
    {
        if (request.Turn == 1)
            return threadToken is null
                && !entry.Invocations.ContainsKey(request.InvocationId);
        return IsValidToken(threadToken)
            && entry.Invocations.TryGetValue(request.InvocationId, out var state)
            && state.CompletedTurns == request.Turn - 1
            && state.ThreadTokenDigest is not null
            && FixedDigest(state.ThreadTokenDigest, TokenDigest(threadToken!));
    }

    private static EvaluationAdmissionRequest? Find(
        Entry entry,
        string idempotencyKey,
        string requestBodySha256) => entry.Capability.AllowedRequests
        .SingleOrDefault(request =>
            string.Equals(request.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)
            && FixedDigest(request.RequestBodySha256, requestBodySha256));

    private void RemoveExpired()
    {
        foreach (var pair in _entries
                     .Where(pair => pair.Value.Capability.ExpiresAt <= _clock.GetUtcNow())
                     .ToArray())
            Remove(pair.Key, pair.Value);
    }

    private void Remove(string digest, Entry entry)
    {
        if (!_entries.Remove(digest)) return;
        _nonceTokens.Remove(entry.Capability.Nonce);
        _retainedBytes -= entry.RetainedBytes;
    }

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
    }

    private static string TokenDigest(string token) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private static bool FixedDigest(string left, string right) =>
        left.Length == 64 && right.Length == 64
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    public static bool IsValidToken(string? token) => token is { Length: 43 }
        && token.All(character => char.IsAsciiLetterOrDigit(character)
                                  || character is '-' or '_');

    internal int Count
    {
        get { lock (_sync) return _entries.Count; }
    }

    internal long RetainedBytes
    {
        get { lock (_sync) return _retainedBytes; }
    }

    internal int ConsumedCallsFor(string token)
    {
        lock (_sync)
            return _entries.TryGetValue(TokenDigest(token), out var entry)
                ? entry.Consumed.Count
                : 0;
    }
}
