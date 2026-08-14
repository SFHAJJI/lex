namespace Lex.Ask;

public enum AskAdmissionFailure { None, PerClientQuota, Busy, GlobalQuota }

public enum AskAdmissionLane { Public, Evaluation }

public sealed record AskAdmission(AskAdmissionFailure Failure, IDisposable? Lease)
{
    public bool Accepted => Failure == AskAdmissionFailure.None && Lease is not null;
}

public sealed class AskAdmissionController
{
    private sealed class Lease(AskAdmissionController owner) : IDisposable
    {
        private AskAdmissionController? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }

    private readonly object _sync = new();
    private readonly TimeProvider _clock;
    private readonly int _perClientDaily;
    private readonly int _globalDaily;
    private readonly int _concurrent;
    private readonly Dictionary<string, int> _clients = new(StringComparer.Ordinal);
    private DateOnly _day;
    private int _global;
    private int _executing;

    public AskAdmissionController(
        TimeProvider clock,
        int perClientDaily,
        int globalDaily,
        int concurrent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perClientDaily);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(globalDaily);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrent);
        _clock = clock;
        _perClientDaily = perClientDaily;
        _globalDaily = globalDaily;
        _concurrent = concurrent;
        _day = Today();
    }

    public int AcceptedToday
    {
        get
        {
            lock (_sync)
            {
                ResetDayIfNeeded();
                return _global;
            }
        }
    }

    public AskAdmission TryAdmit(
        string client,
        AskAdmissionLane lane = AskAdmissionLane.Public)
    {
        lock (_sync)
        {
            ResetDayIfNeeded();
            var clientCount = _clients.GetValueOrDefault(client);
            if (lane == AskAdmissionLane.Public && clientCount >= _perClientDaily)
                return new AskAdmission(AskAdmissionFailure.PerClientQuota, null);
            if (_executing >= _concurrent)
                return new AskAdmission(AskAdmissionFailure.Busy, null);
            if (lane == AskAdmissionLane.Public && _global >= _globalDaily)
                return new AskAdmission(AskAdmissionFailure.GlobalQuota, null);

            if (lane == AskAdmissionLane.Public)
            {
                _clients[client] = clientCount + 1;
                _global++;
            }
            _executing++;
            return new AskAdmission(AskAdmissionFailure.None, new Lease(this));
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            if (_executing > 0) _executing--;
        }
    }

    private void ResetDayIfNeeded()
    {
        var today = Today();
        if (today == _day) return;
        _day = today;
        _global = 0;
        _clients.Clear();
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
}
