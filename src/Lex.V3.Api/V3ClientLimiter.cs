using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace Lex.V3.Api;

/// <summary>
/// The per-client limit (the specification of record's R109 and R46; the plan's item 10.1): an in-memory token bucket per
/// client, keyed by a keyed hash of the client's address, never written anywhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>The budgets</b> (a driver decision, reversible, STATUS-WEB.md; R109's numbers, "re-sized by the load test"):
/// <list type="bullet">
/// <item><description>The API (<c>/api/v3/</c> and <c>/mcp</c>): <see cref="ApiPerMinute"/> requests a minute, a bucket of that
/// many tokens refilled at one a second, and at most <see cref="ApiInFlight"/> of a client's requests answered at once (R109's
/// "concurrency cap on costly operations": on one vCPU every operation is a costly one, and a page asks one at a time). Over
/// either, the answer is the typed refusal <c>rate_limited</c> with <c>Retry-After</c>.</description></item>
/// <item><description>The pages the server renders from its own answer (R67): <see cref="RendersPerMinute"/> a minute, well above
/// the API's (R46), each render holding one of the client's <see cref="ApiInFlight"/> places while it runs. Over the budget, or
/// with every place taken, the page is served as built and asks for itself, as when the renderer is busy; no page is refused.
/// Every other file of the site is not counted at all.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>What is held, and for how long.</b> The client is the address the connection came from (<see cref="ClientOf"/>; on the
/// host, the address its ingress forwards). It is hashed at once with HMAC-SHA-256 under a 32-byte key drawn at random when the
/// process starts and replaced every day (UTC), which is never written, logged or sent anywhere; only the first 16 bytes of the
/// hash are kept, beside the bucket's count, and the address itself is not kept at all. An IPv6 address counts by its /64, as a
/// household or a host is given one. Every minute the entries whose buckets have filled again (a minute after the client's last
/// request) are forgotten, so none outlives its client's last request by more than about two minutes; the day's key and every
/// entry are dropped when the key is replaced, and all of it when the process ends. At most <see cref="MaximumClients"/> entries
/// are held: past that, a new client is answered without one (the limit's purpose is fairness between clients, not a defence
/// against a flood from many addresses, which one replica per revision bounds instead).
/// </para>
/// <para>
/// <b>Who is not limited.</b> A request with no connection address (an in-process test), and one that reaches the server from
/// inside its host's own network with no address forwarded by the platform's ingress (the platform's own health probes, a release
/// probe of the image on the machine that built it, the journeys on their own machine), names no client and is not counted.
/// </para>
/// </remarks>
internal sealed class V3ClientLimiter
{
    /// <summary>The API's budget: requests a minute, and the bucket's capacity (R109).</summary>
    internal const int ApiPerMinute = 60;

    /// <summary>A client's API requests answered at once, at most (R109's concurrency cap).</summary>
    internal const int ApiInFlight = 4;

    /// <summary>The server-rendered pages' budget: renders a minute, and the bucket's capacity (R46: well above the API's).</summary>
    internal const int RendersPerMinute = 300;

    /// <summary>The entries held at most; past it a new client is answered without one.</summary>
    internal const int MaximumClients = 100_000;

    /// <summary>How often the entries whose buckets have filled again are forgotten.</summary>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>The header the platform's ingress forwards the client's address in, read only for a connection from inside the host.</summary>
    internal const string ForwardedForHeader = "X-Forwarded-For";

    private const int KeyBytes = 32;

    private readonly TimeProvider _time;
    private readonly Func<byte[]> _newKey;
    private readonly Lock _gate = new();
    private readonly Dictionary<UInt128, Entry> _entries = [];
    private readonly ITimer _sweeper;
    private byte[] _key;
    private DateOnly _keyDay;

    /// <param name="time">The clock and the sweep's timer (the system's when not given; a test hands its own).</param>
    /// <param name="newKey">Draws a new hashing key (32 random bytes when not given).</param>
    internal V3ClientLimiter(TimeProvider? time = null, Func<byte[]>? newKey = null)
    {
        _time = time ?? TimeProvider.System;
        _newKey = newKey ?? (static () => RandomNumberGenerator.GetBytes(KeyBytes));
        _key = _newKey();
        _keyDay = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        // The timer holds the limiter weakly, so a limiter no one holds (a test's handler) is collected and its timer stops.
        var owner = new WeakReference<V3ClientLimiter>(this);
        ITimer? timer = null;
        timer = _time.CreateTimer(
            _ =>
            {
                if (owner.TryGetTarget(out var limiter))
                {
                    limiter.Sweep();
                }
                else
                {
                    timer?.Dispose();
                }
            },
            null,
            SweepInterval,
            SweepInterval);
        _sweeper = timer;
    }

    /// <summary>A client, as the limit knows it: 16 bytes of a keyed hash of its address, and nothing else.</summary>
    internal readonly record struct Client(UInt128 Key);

    /// <summary>
    /// An API request's admission: <see cref="Admitted"/>, or the seconds to wait (<see cref="RetryAfterSeconds"/>). An admitted
    /// request holds one of its client's in-flight places until it is disposed.
    /// </summary>
    internal sealed class Admission : IDisposable
    {
        private readonly V3ClientLimiter? _limiter;
        private readonly Client? _client;
        private int _released;

        internal Admission(V3ClientLimiter? limiter, Client? client, int retryAfterSeconds)
        {
            _limiter = limiter;
            _client = client;
            RetryAfterSeconds = retryAfterSeconds;
        }

        /// <summary>Whether the request is answered as asked.</summary>
        internal bool Admitted => RetryAfterSeconds == 0;

        /// <summary>The seconds a refused client is asked to wait (Retry-After); 0 when admitted.</summary>
        internal int RetryAfterSeconds { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0 && _limiter is not null && _client is { } client)
            {
                _limiter.Release(client);
            }
        }
    }

    /// <summary>The entries held now (for the tests: the table is never shown anywhere else).</summary>
    internal int ClientsHeld
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// The client a request comes from, or null when it names none: <paramref name="peer"/> is the connection's address and
    /// <paramref name="forwardedFor"/> the value of <see cref="ForwardedForHeader"/>. A peer outside the host's network is the
    /// client, and what it says it forwards is ignored (anyone can write that header). A peer inside it (loopback, a private,
    /// shared or link-local range) is the platform's ingress, or a caller on the host itself: the client is then the last
    /// address the header lists, the one the ingress itself appended, and with none there is no client. The address is hashed
    /// here and not kept (<see cref="Client"/>).
    /// </summary>
    internal Client? ClientOf(IPAddress? peer, string? forwardedFor)
    {
        if (peer is null)
        {
            return null;
        }

        var address = Unmapped(peer);
        if (InsideHost(address))
        {
            if (LastForwarded(forwardedFor) is not { } forwarded)
            {
                return null;
            }

            address = Unmapped(forwarded);
        }

        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out var written))
        {
            return null;
        }

        // An IPv6 address counts by its /64; a prefix byte keeps the two families apart.
        var counted = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 8 : written;
        Span<byte> message = stackalloc byte[17];
        message[0] = (byte)counted;
        bytes[..counted].CopyTo(message[1..]);
        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        lock (_gate)
        {
            RotateIfDue(_time.GetUtcNow());
            HMACSHA256.HashData(_key, message[..(counted + 1)], hash);
        }

        bytes.Clear();
        message.Clear();
        return new Client(BinaryPrimitives.ReadUInt128LittleEndian(hash));
    }

    /// <summary>
    /// Admits one API request of <paramref name="client"/>, or says how long to wait: refused while the client has
    /// <see cref="ApiInFlight"/> requests being answered, or no token left. A request with no client is always admitted.
    /// </summary>
    internal Admission AdmitApi(Client? client)
    {
        if (client is not { } known)
        {
            return new Admission(null, null, 0);
        }

        lock (_gate)
        {
            var now = _time.GetUtcNow();
            RotateIfDue(now);
            if (EntryFor(known, now) is not { } entry)
            {
                return new Admission(null, null, 0);
            }

            if (entry.InFlight >= ApiInFlight)
            {
                return new Admission(null, null, 1);
            }

            if (entry.ApiTokens < 1)
            {
                // One token comes back each second: the wait is until the next one, never less than a second.
                var seconds = (int)Math.Ceiling((1 - entry.ApiTokens) * 60 / ApiPerMinute);
                return new Admission(null, null, Math.Max(1, seconds));
            }

            entry.ApiTokens -= 1;
            entry.InFlight += 1;
            return new Admission(this, known, 0);
        }
    }

    /// <summary>
    /// Admits one page render for <paramref name="client"/> from the server's own answer, or not: refused while the client has
    /// <see cref="ApiInFlight"/> requests being answered (a render is one of them: it asks the mount and runs the renderer, so
    /// it holds one of the client's in-flight places until it is disposed; the review of #995, F2), or with no render token
    /// left. A request with no client is always admitted. A page whose render is refused is served as built.
    /// </summary>
    internal Admission AdmitRender(Client? client)
    {
        if (client is not { } known)
        {
            return new Admission(null, null, 0);
        }

        lock (_gate)
        {
            var now = _time.GetUtcNow();
            RotateIfDue(now);
            if (EntryFor(known, now) is not { } entry)
            {
                return new Admission(null, null, 0);
            }

            if (entry.InFlight >= ApiInFlight || entry.RenderTokens < 1)
            {
                return new Admission(null, null, 1);
            }

            entry.RenderTokens -= 1;
            entry.InFlight += 1;
            return new Admission(this, known, 0);
        }
    }

    /// <summary>
    /// Forgets every entry whose client has nothing being answered and whose buckets have filled again, and replaces the key
    /// (dropping every entry) once the UTC day has changed. The timer runs it every <see cref="SweepInterval"/>.
    /// </summary>
    internal void Sweep()
    {
        lock (_gate)
        {
            SweepHeld(_time.GetUtcNow());
        }
    }

    /// <summary><see cref="Sweep"/>, with the gate already held.</summary>
    private void SweepHeld(DateTimeOffset now)
    {
        RotateIfDue(now);
        List<UInt128>? idle = null;
        foreach (var (key, entry) in _entries)
        {
            Refill(entry, now);
            if (entry.InFlight == 0 && entry.ApiTokens >= ApiPerMinute && entry.RenderTokens >= RendersPerMinute)
            {
                (idle ??= []).Add(key);
            }
        }

        foreach (var key in idle ?? [])
        {
            _entries.Remove(key);
        }
    }

    /// <summary>Stops the sweep's timer (the process's limiter lives as long as the process).</summary>
    internal void StopSweeping() => _sweeper.Dispose();

    private void Release(Client client)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(client.Key, out var entry) && entry.InFlight > 0)
            {
                entry.InFlight -= 1;
            }
        }
    }

    /// <summary>The client's entry, refilled to <paramref name="now"/>, made when it has none; null when the table is full.</summary>
    private Entry? EntryFor(Client client, DateTimeOffset now)
    {
        if (_entries.TryGetValue(client.Key, out var entry))
        {
            Refill(entry, now);
            return entry;
        }

        // Full: answered without an entry until the next sweep frees some (a sweep here would cost every new client a pass
        // over the whole table).
        if (_entries.Count >= MaximumClients)
        {
            return null;
        }

        entry = new Entry { ApiTokens = ApiPerMinute, RenderTokens = RendersPerMinute, Updated = now };
        _entries[client.Key] = entry;
        return entry;
    }

    private static void Refill(Entry entry, DateTimeOffset now)
    {
        var seconds = (now - entry.Updated).TotalSeconds;
        if (seconds <= 0)
        {
            return;
        }

        entry.ApiTokens = Math.Min(ApiPerMinute, entry.ApiTokens + (seconds * ApiPerMinute / 60));
        entry.RenderTokens = Math.Min(RendersPerMinute, entry.RenderTokens + (seconds * RendersPerMinute / 60));
        entry.Updated = now;
    }

    private void RotateIfDue(DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        if (day == _keyDay)
        {
            return;
        }

        // A new day's key: every entry was keyed under the old one, so all of them go with it.
        CryptographicOperations.ZeroMemory(_key);
        _key = _newKey();
        _keyDay = day;
        _entries.Clear();
    }

    private static IPAddress Unmapped(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>
    /// Whether an address is inside the host's own network rather than the public internet: loopback, the private ranges
    /// (10/8, 172.16/12, 192.168/16), the shared address space (100.64/10, which the platform's ingress uses), link-local
    /// (169.254/16, fe80::/10) and unique local IPv6 (fc00::/7).
    /// </summary>
    internal static bool InsideHost(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var unmapped = Unmapped(address);
        if (IPAddress.IsLoopback(unmapped))
        {
            return true;
        }

        Span<byte> bytes = stackalloc byte[16];
        if (!unmapped.TryWriteBytes(bytes, out var written))
        {
            return false;
        }

        if (written == 4)
        {
            return bytes[0] == 10 ||
                (bytes[0] == 172 && (bytes[1] & 0xF0) == 16) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                (bytes[0] == 100 && (bytes[1] & 0xC0) == 64) ||
                (bytes[0] == 169 && bytes[1] == 254);
        }

        return (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
    }

    /// <summary>The last address <paramref name="forwardedFor"/> lists (an address, or one with a port), or null.</summary>
    internal static IPAddress? LastForwarded(string? forwardedFor)
    {
        if (string.IsNullOrWhiteSpace(forwardedFor))
        {
            return null;
        }

        var last = forwardedFor.Split(',')[^1].Trim();
        if (IPAddress.TryParse(last, out var address))
        {
            return address;
        }

        return IPEndPoint.TryParse(last, out var endpoint) ? endpoint.Address : null;
    }

    private sealed class Entry
    {
        public double ApiTokens;
        public double RenderTokens;
        public DateTimeOffset Updated;
        public int InFlight;
    }
}
