using System.Buffers;

namespace Lex.Web;

public static class BoundedRequestBody
{
    public static async Task<byte[]?> ReadAsync(
        Stream source,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(8192, maximumBytes));
        try
        {
            using var body = new MemoryStream(Math.Min(maximumBytes, 8192));
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) return body.ToArray();
                if (body.Length + read > maximumBytes) return null;
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

public enum AskRequestClaimKind { Owner, Duplicate, Conflict, Busy }

public sealed record AskRequestResponse(int Status, string Body);

public sealed class AskRequestClaim
{
    private readonly Action<int, string>? _complete;

    internal AskRequestClaim(
        AskRequestClaimKind kind,
        Task<AskRequestResponse> completion,
        Action<int, string>? complete = null)
    {
        Kind = kind;
        Completion = completion;
        _complete = complete;
    }

    public AskRequestClaimKind Kind { get; }
    public Task<AskRequestResponse> Completion { get; }

    public void Complete(int status, string body)
    {
        if (Kind != AskRequestClaimKind.Owner || _complete is null)
            throw new InvalidOperationException("Only the owner can complete an idempotent request.");
        _complete(status, body);
    }
}

public sealed class AskRequestRegistry
{
    private sealed class Entry(string fingerprint)
    {
        public string Fingerprint { get; } = fingerprint;
        public TaskCompletionSource<AskRequestResponse> Source { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset? CompletedAt { get; set; }
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly int _maximumEntries;
    private readonly TimeSpan _retention;

    public AskRequestRegistry(
        TimeProvider clock,
        int maximumEntries = 1024,
        TimeSpan? retention = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        _clock = clock;
        _maximumEntries = maximumEntries;
        _retention = retention ?? TimeSpan.FromMinutes(10);
    }

    public AskRequestClaim Claim(string client, string key, string fingerprint)
    {
        var identity = $"{client}\n{key}";
        lock (_sync)
        {
            RemoveExpired();
            if (_entries.TryGetValue(identity, out var existing))
                return existing.Fingerprint == fingerprint
                    ? new AskRequestClaim(AskRequestClaimKind.Duplicate, existing.Source.Task)
                    : Immediate(AskRequestClaimKind.Conflict, 409,
                        "{\"error\":\"Idempotency key was already used for another request.\"}");
            if (_entries.Count >= _maximumEntries)
                return Immediate(AskRequestClaimKind.Busy, 429,
                    "{\"error\":\"The idempotency registry is busy. Try again shortly.\"}");

            var entry = new Entry(fingerprint);
            _entries.Add(identity, entry);
            return new AskRequestClaim(AskRequestClaimKind.Owner, entry.Source.Task,
                (status, body) => Complete(entry, status, body));
        }
    }

    private void Complete(Entry entry, int status, string body)
    {
        lock (_sync)
        {
            if (!entry.Source.TrySetResult(new AskRequestResponse(status, body))) return;
            entry.CompletedAt = _clock.GetUtcNow();
        }
    }

    private void RemoveExpired()
    {
        var cutoff = _clock.GetUtcNow() - _retention;
        foreach (var key in _entries
                     .Where(pair => pair.Value.CompletedAt is { } at && at < cutoff)
                     .Select(pair => pair.Key).ToArray())
            _entries.Remove(key);
    }

    private static AskRequestClaim Immediate(
        AskRequestClaimKind kind,
        int status,
        string body) => new(kind,
            Task.FromResult(new AskRequestResponse(status, body)));
}
