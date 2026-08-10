using System.Buffers;
using System.Threading.Channels;

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

public enum AskRequestClaimKind { Owner, Duplicate, Conflict, Busy, ReplayUnavailable }

public sealed record AskRequestResponse(int Status, string Body);

public sealed class AskRequestClaim
{
    private readonly Action<int, string, bool>? _complete;
    private readonly Action<string>? _reportOperation;
    private readonly Action? _unsubscribe;

    internal AskRequestClaim(
        AskRequestClaimKind kind,
        string requestId,
        Task<AskRequestResponse> completion,
        Action<int, string, bool>? complete = null,
        Action<string>? reportOperation = null,
        ChannelReader<string>? operationResults = null,
        Action? unsubscribe = null)
    {
        Kind = kind;
        RequestId = requestId;
        Completion = completion;
        _complete = complete;
        _reportOperation = reportOperation;
        OperationResults = operationResults ?? CompletedOperations();
        _unsubscribe = unsubscribe;
    }

    public AskRequestClaimKind Kind { get; }
    public string RequestId { get; }
    public Task<AskRequestResponse> Completion { get; }
    public ChannelReader<string> OperationResults { get; }

    public void Complete(int status, string body, bool retainForReplay)
    {
        if (Kind != AskRequestClaimKind.Owner || _complete is null)
            throw new InvalidOperationException("Only the owner can complete an idempotent request.");
        _complete(status, body, retainForReplay);
    }

    public void ReportOperation(string operation)
    {
        if (Kind != AskRequestClaimKind.Owner || _reportOperation is null)
            throw new InvalidOperationException("Only the owner can report operation results.");
        _reportOperation(operation);
    }

    public void Unsubscribe() => _unsubscribe?.Invoke();

    private static ChannelReader<string> CompletedOperations()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryComplete();
        return channel.Reader;
    }
}

public sealed class AskRequestRegistry
{
    private sealed record Tombstone(
        string Fingerprint,
        string RequestId,
        DateTimeOffset CompletedAt);

    private sealed class Entry(string fingerprint)
    {
        public string Fingerprint { get; } = fingerprint;
        public string RequestId { get; } = Guid.NewGuid().ToString("N");
        public TaskCompletionSource<AskRequestResponse> Source { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset? CompletedAt { get; set; }
        public long RetainedBytes { get; set; }
        public List<string> Operations { get; } = [];
        public List<Channel<string>> Subscribers { get; } = [];
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tombstone> _tombstones = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly int _maximumEntries;
    private readonly long _maximumRetainedBytes;
    private readonly int _maximumSubscribersPerEntry;
    private readonly int _maximumSubscribers;
    private readonly TimeSpan _retention;
    private long _retainedBytes;
    private int _subscriberCount;

    public AskRequestRegistry(
        TimeProvider clock,
        int maximumEntries = 1024,
        long maximumRetainedBytes = 64 * 1024 * 1024,
        int maximumSubscribersPerEntry = 8,
        int maximumSubscribers = 64,
        TimeSpan? retention = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRetainedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSubscribersPerEntry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSubscribers);
        _clock = clock;
        _maximumEntries = maximumEntries;
        _maximumRetainedBytes = maximumRetainedBytes;
        _maximumSubscribersPerEntry = maximumSubscribersPerEntry;
        _maximumSubscribers = maximumSubscribers;
        _retention = retention ?? TimeSpan.FromMinutes(10);
    }

    public AskRequestClaim Claim(string client, string key, string fingerprint)
    {
        _ = client; // Admission is client-scoped; the retry token is globally stable across networks.
        var identity = key;
        lock (_sync)
        {
            RemoveExpired();
            if (_tombstones.TryGetValue(identity, out var tombstone))
                return tombstone.Fingerprint == fingerprint
                    ? Immediate(AskRequestClaimKind.ReplayUnavailable, 409,
                        "{\"error\":\"This request completed, but its cached response is no longer available. Use a new key only for a new request.\"}",
                        tombstone.RequestId)
                    : Immediate(AskRequestClaimKind.Conflict, 409,
                        "{\"error\":\"Idempotency key was already used for another request.\"}");
            if (_entries.TryGetValue(identity, out var existing))
                return existing.Fingerprint == fingerprint
                    ? Duplicate(existing)
                    : Immediate(AskRequestClaimKind.Conflict, 409,
                        "{\"error\":\"Idempotency key was already used for another request.\"}");
            if (_entries.Count + _tombstones.Count >= _maximumEntries)
                return Immediate(AskRequestClaimKind.Busy, 429,
                    "{\"error\":\"The idempotency registry is busy. Try again shortly.\"}");

            var entry = new Entry(fingerprint);
            _entries.Add(identity, entry);
            return new AskRequestClaim(AskRequestClaimKind.Owner,
                entry.RequestId, entry.Source.Task,
                (status, body, retain) => Complete(identity, entry, status, body, retain),
                operation => ReportOperation(entry, operation));
        }
    }

    private AskRequestClaim Duplicate(Entry entry)
    {
        var completed = entry.CompletedAt is not null || entry.Source.Task.IsCompleted;
        if (!completed && (entry.Subscribers.Count >= _maximumSubscribersPerEntry
                           || _subscriberCount >= _maximumSubscribers))
            return Immediate(AskRequestClaimKind.Busy, 429,
                "{\"error\":\"Too many clients are following this assistant request.\"}");
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        foreach (var operation in entry.Operations)
            channel.Writer.TryWrite(operation);
        if (completed)
            channel.Writer.TryComplete();
        else
        {
            entry.Subscribers.Add(channel);
            _subscriberCount++;
        }
        return new AskRequestClaim(AskRequestClaimKind.Duplicate,
            entry.RequestId, entry.Source.Task, operationResults: channel.Reader,
            unsubscribe: () => Unsubscribe(entry, channel));
    }

    private void Unsubscribe(Entry entry, Channel<string> channel)
    {
        lock (_sync)
        {
            if (!entry.Subscribers.Remove(channel)) return;
            _subscriberCount--;
            channel.Writer.TryComplete();
        }
    }

    private void ReportOperation(Entry entry, string operation)
    {
        lock (_sync)
        {
            if (entry.Source.Task.IsCompleted) return;
            entry.Operations.Add(operation);
            foreach (var subscriber in entry.Subscribers)
                subscriber.Writer.TryWrite(operation);
        }
    }

    private void Complete(
        string identity,
        Entry entry,
        int status,
        string body,
        bool retainForReplay)
    {
        lock (_sync)
        {
            if (!entry.Source.TrySetResult(new AskRequestResponse(status, body))) return;
            foreach (var subscriber in entry.Subscribers)
                subscriber.Writer.TryComplete();
            _subscriberCount -= entry.Subscribers.Count;
            entry.Subscribers.Clear();
            entry.Operations.Clear();
            if (retainForReplay)
            {
                entry.CompletedAt = _clock.GetUtcNow();
                entry.RetainedBytes = checked((long)body.Length * sizeof(char));
                _retainedBytes += entry.RetainedBytes;
                RemoveOldestCompletedUntilWithinBudget(entry);
            }
            else if (_entries.TryGetValue(identity, out var current)
                     && ReferenceEquals(current, entry))
                _entries.Remove(identity);
        }
    }

    private void RemoveExpired()
    {
        var cutoff = _clock.GetUtcNow() - _retention;
        foreach (var key in _entries
                     .Where(pair => pair.Value.CompletedAt is { } at && at < cutoff)
                     .Select(pair => pair.Key).ToArray())
            RemoveEntry(key, preserveIdentity: false);
        foreach (var key in _tombstones
                     .Where(pair => pair.Value.CompletedAt < cutoff)
                     .Select(pair => pair.Key).ToArray())
            _tombstones.Remove(key);
    }

    private void RemoveOldestCompletedUntilWithinBudget(Entry justCompleted)
    {
        while (_retainedBytes > _maximumRetainedBytes)
        {
            var oldest = _entries
                .Where(pair => pair.Value.CompletedAt is not null)
                .OrderBy(pair => pair.Value.CompletedAt)
                .ThenBy(pair => pair.Value.RequestId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (oldest.Key is null) break;
            RemoveEntry(oldest.Key, preserveIdentity: true);
        }

        // A single response larger than the whole cache is delivered to all current waiters,
        // but is not kept alive by the registry after they finish.
        if (justCompleted.RetainedBytes > _maximumRetainedBytes)
        {
            var identity = _entries.FirstOrDefault(pair => ReferenceEquals(pair.Value, justCompleted)).Key;
            if (identity is not null) RemoveEntry(identity, preserveIdentity: true);
        }
    }

    private void RemoveEntry(string identity, bool preserveIdentity = false)
    {
        if (!_entries.Remove(identity, out var removed)) return;
        _retainedBytes -= removed.RetainedBytes;
        if (_retainedBytes < 0) _retainedBytes = 0;
        if (preserveIdentity && removed.CompletedAt is { } completedAt)
            _tombstones[identity] = new Tombstone(
                removed.Fingerprint, removed.RequestId, completedAt);
    }

    internal long RetainedBytes
    {
        get { lock (_sync) return _retainedBytes; }
    }

    private static AskRequestClaim Immediate(
        AskRequestClaimKind kind,
        int status,
        string body,
        string? requestId = null) => new(kind, requestId ?? Guid.NewGuid().ToString("N"),
            Task.FromResult(new AskRequestResponse(status, body)));
}
