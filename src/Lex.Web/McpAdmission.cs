namespace Lex.Web;

public enum McpAdmissionFailure { None, Busy, RateLimited }

public sealed record McpAdmission(McpAdmissionFailure Failure, IDisposable? Lease)
{
    public bool Accepted => Failure == McpAdmissionFailure.None && Lease is not null;
}

public sealed class McpAdmissionController
{
    private sealed class Lease(
        SemaphoreSlim executing,
        SemaphoreSlim? hybrid) : IDisposable
    {
        private SemaphoreSlim? _executing = executing;
        private SemaphoreSlim? _hybrid = hybrid;

        public void Dispose()
        {
            Interlocked.Exchange(ref _hybrid, null)?.Release();
            Interlocked.Exchange(ref _executing, null)?.Release();
        }
    }

    private readonly object _rateSync = new();
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _executing;
    private readonly SemaphoreSlim _hybrid;
    private readonly int _maximumQueued;
    private readonly TimeSpan _queueDeadline;
    private readonly int _perClientPerMinute;
    private readonly int _globalPerMinute;
    private readonly Queue<DateTimeOffset> _global = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _clients = new(StringComparer.Ordinal);
    private int _queued;

    public int TrackedClients
    {
        get { lock (_rateSync) return _clients.Count; }
    }

    public McpAdmissionController(
        TimeProvider clock,
        int executing = 8,
        int queued = 16,
        TimeSpan? queueDeadline = null,
        int perClientPerMinute = 120,
        int globalPerMinute = 600,
        int hybridExecuting = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(executing);
        ArgumentOutOfRangeException.ThrowIfNegative(queued);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perClientPerMinute);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(globalPerMinute);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hybridExecuting);
        _clock = clock;
        _executing = new SemaphoreSlim(executing, executing);
        _hybrid = new SemaphoreSlim(hybridExecuting, hybridExecuting);
        _maximumQueued = queued;
        _queueDeadline = queueDeadline ?? TimeSpan.FromSeconds(2);
        _perClientPerMinute = perClientPerMinute;
        _globalPerMinute = globalPerMinute;
    }

    public async ValueTask<McpAdmission> EnterAsync(
        string client,
        bool hybrid,
        CancellationToken cancellationToken)
    {
        if (!AdmitRate(client))
            return new McpAdmission(McpAdmissionFailure.RateLimited, null);

        var hasExecution = _executing.Wait(0);
        if (!hasExecution)
        {
            if (Interlocked.Increment(ref _queued) > _maximumQueued)
            {
                Interlocked.Decrement(ref _queued);
                return new McpAdmission(McpAdmissionFailure.Busy, null);
            }
            try
            {
                hasExecution = await _executing.WaitAsync(_queueDeadline, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _queued);
            }
            if (!hasExecution)
                return new McpAdmission(McpAdmissionFailure.Busy, null);
        }

        SemaphoreSlim? heldHybrid = null;
        if (hybrid)
        {
            try
            {
                if (!await _hybrid.WaitAsync(_queueDeadline, cancellationToken))
                {
                    _executing.Release();
                    return new McpAdmission(McpAdmissionFailure.Busy, null);
                }
                heldHybrid = _hybrid;
            }
            catch (OperationCanceledException)
            {
                _executing.Release();
                throw;
            }
        }
        return new McpAdmission(
            McpAdmissionFailure.None, new Lease(_executing, heldHybrid));
    }

    private bool AdmitRate(string client)
    {
        lock (_rateSync)
        {
            var now = _clock.GetUtcNow();
            var cutoff = now - TimeSpan.FromMinutes(1);
            Trim(_global, cutoff);
            if (_clients.Count >= _globalPerMinute * 2)
                foreach (var key in _clients.Keys.ToArray())
                {
                    Trim(_clients[key], cutoff);
                    if (_clients[key].Count == 0) _clients.Remove(key);
                }
            if (_global.Count >= _globalPerMinute)
                return false;
            if (!_clients.TryGetValue(client, out var clientWindow))
            {
                clientWindow = new Queue<DateTimeOffset>();
                _clients.Add(client, clientWindow);
            }
            Trim(clientWindow, cutoff);
            if (clientWindow.Count >= _perClientPerMinute)
                return false;
            clientWindow.Enqueue(now);
            _global.Enqueue(now);
            return true;
        }
    }

    private static void Trim(Queue<DateTimeOffset> window, DateTimeOffset cutoff)
    {
        while (window.TryPeek(out var timestamp) && timestamp <= cutoff)
            window.Dequeue();
    }
}
