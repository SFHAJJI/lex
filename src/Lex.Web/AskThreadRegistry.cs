using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lex.Ask;

namespace Lex.Web;

public enum AskThreadAcquireKind
{
    Acquired,
    NotFound,
    Busy,
    Capacity,
}

public sealed record AskThreadAcquire(AskThreadAcquireKind Kind, AskThreadLease? Lease = null);

public sealed class AskThreadLease : IAsyncDisposable
{
    private readonly Func<string, string, AskConversationContext?, AskConversationContextDisposition, bool> _commit;
    private readonly Action _release;
    private bool _disposed;
    private bool _commitAttempted;

    internal AskThreadLease(
        string token,
        JsonArray history,
        AskConversationContext? context,
        Func<string, string, AskConversationContext?, AskConversationContextDisposition, bool> commit,
        Action release)
    {
        Token = token;
        History = history;
        Context = context;
        _commit = commit;
        _release = release;
    }

    public string Token { get; }
    public JsonArray History { get; }
    public AskConversationContext? Context { get; }

    public bool Commit(
        string userMessage,
        string assistantMessage,
        AskConversationContext? context,
        AskConversationContextDisposition contextDisposition =
            AskConversationContextDisposition.Preserve)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_commitAttempted)
            throw new InvalidOperationException("A thread lease can commit only one turn.");
        _commitAttempted = true;
        return _commit(userMessage, assistantMessage, context, contextDisposition);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _release();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Bounded in-process conversation memory for the public assistant. Tokens are random opaque
/// capabilities and only their SHA-256 digests are retained. Entries are ephemeral and local to
/// one server process; an unknown or expired token never falls through to another conversation.
/// </summary>
public sealed class AskThreadRegistry
{
    public const int TokenLength = 43;
    public const int DefaultMaximumThreads = 1_024;
    public const int DefaultMaximumRetainedTurns = 6;
    public const int DefaultMaximumThreadBytes = 32 * 1_024;
    public const long DefaultMaximumRetainedBytes = 16 * 1_024 * 1_024;
    public static readonly TimeSpan DefaultIdleTtl = TimeSpan.FromMinutes(30);

    private sealed record Turn(string User, string Assistant);

    private sealed class Entry(DateTimeOffset createdAt, string? ownerScope)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public List<Turn> Turns { get; set; } = [];
        public AskConversationContext? Context { get; set; }
        public DateTimeOffset LastCommittedAt { get; set; } = createdAt;
        public long RetainedBytes { get; set; }
        public int Pins { get; set; }
        public int Waiters { get; set; }
        public string? OwnerScope { get; } = ownerScope;
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly int _maximumThreads;
    private readonly int _maximumRetainedTurns;
    private readonly int _maximumThreadBytes;
    private readonly long _maximumRetainedBytes;
    private readonly int _maximumWaitersPerThread;
    private readonly TimeSpan _idleTtl;
    private long _retainedBytes;

    public AskThreadRegistry(
        TimeProvider clock,
        int maximumThreads = DefaultMaximumThreads,
        int maximumRetainedTurns = DefaultMaximumRetainedTurns,
        int maximumThreadBytes = DefaultMaximumThreadBytes,
        long maximumRetainedBytes = DefaultMaximumRetainedBytes,
        int maximumWaitersPerThread = 2,
        TimeSpan? idleTtl = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumThreads);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRetainedTurns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumThreadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRetainedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWaitersPerThread);
        if (maximumRetainedBytes < maximumThreadBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedBytes),
                "The global thread budget must hold at least one maximum-sized thread.");
        _clock = clock;
        _maximumThreads = maximumThreads;
        _maximumRetainedTurns = maximumRetainedTurns;
        _maximumThreadBytes = maximumThreadBytes;
        _maximumRetainedBytes = maximumRetainedBytes;
        _maximumWaitersPerThread = maximumWaitersPerThread;
        _idleTtl = idleTtl ?? DefaultIdleTtl;
        if (_idleTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleTtl));
    }

    public async ValueTask<AskThreadAcquire> AcquireAsync(
        string? token,
        CancellationToken cancellationToken = default,
        string? ownerScope = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidOwnerScope(ownerScope))
            throw new ArgumentException(
                "A thread owner scope must be a SHA-256 digest.", nameof(ownerScope));
        Entry entry;
        string identity;
        string effectiveToken;
        var queued = false;
        lock (_sync)
        {
            RemoveExpired();
            if (token is null)
            {
                if (!MakeRoomForThread())
                    return new AskThreadAcquire(AskThreadAcquireKind.Capacity);
                do
                {
                    effectiveToken = NewToken();
                    identity = Identity(effectiveToken);
                }
                while (_entries.ContainsKey(identity));
                entry = new Entry(_clock.GetUtcNow(), ownerScope);
                _entries.Add(identity, entry);
            }
            else
            {
                if (!IsValidToken(token))
                    return new AskThreadAcquire(AskThreadAcquireKind.NotFound);
                effectiveToken = token;
                identity = Identity(token);
                if (!_entries.TryGetValue(identity, out entry!))
                    return new AskThreadAcquire(AskThreadAcquireKind.NotFound);
                if (!ScopeMatches(entry.OwnerScope, ownerScope))
                    return new AskThreadAcquire(AskThreadAcquireKind.NotFound);
            }

            if (!entry.Gate.Wait(0))
            {
                if (entry.Waiters >= _maximumWaitersPerThread)
                    return new AskThreadAcquire(AskThreadAcquireKind.Busy);
                entry.Waiters++;
                queued = true;
            }
            entry.Pins++;
        }

        if (queued)
        {
            try
            {
                await entry.Gate.WaitAsync(cancellationToken);
            }
            catch
            {
                lock (_sync)
                {
                    entry.Waiters--;
                    entry.Pins--;
                    RemoveEmptyEntry(identity, entry);
                }
                throw;
            }
            lock (_sync)
            {
                entry.Waiters--;
                if (!_entries.TryGetValue(identity, out var current)
                    || !ReferenceEquals(current, entry))
                {
                    entry.Pins--;
                    entry.Gate.Release();
                    return new AskThreadAcquire(AskThreadAcquireKind.NotFound);
                }
            }
        }

        JsonArray history;
        AskConversationContext? context;
        lock (_sync)
        {
            history = History(entry);
            context = Clone(entry.Context);
        }
        var committed = false;
        return new AskThreadAcquire(AskThreadAcquireKind.Acquired,
            new AskThreadLease(
                effectiveToken,
                history,
                context,
                (user, assistant, nextContext, disposition) =>
                {
                    var stored = Commit(
                        identity, entry, user, assistant, nextContext, disposition);
                    committed |= stored;
                    return stored;
                },
                () => Release(identity, entry, committed)));
    }

    public bool Reset(string token, string? ownerScope = null)
    {
        if (!IsValidToken(token) || !IsValidOwnerScope(ownerScope)) return false;
        lock (_sync)
        {
            RemoveExpired();
            var identity = Identity(token);
            if (!_entries.TryGetValue(identity, out var entry)
                || !ScopeMatches(entry.OwnerScope, ownerScope)
                || entry.Pins != 0)
                return false;
            RemoveEntry(identity, entry);
            return true;
        }
    }

    public static bool IsValidToken(string? token) => token is { Length: TokenLength }
        && token.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_');

    private bool Commit(
        string identity,
        Entry entry,
        string userMessage,
        string assistantMessage,
        AskConversationContext? context,
        AskConversationContextDisposition contextDisposition)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || userMessage.Length > 1_000)
            throw new ArgumentOutOfRangeException(nameof(userMessage));
        if (string.IsNullOrWhiteSpace(assistantMessage))
            throw new ArgumentOutOfRangeException(nameof(assistantMessage));
        const string shortened = "\n\n[Earlier answer shortened in server conversation memory.]";
        if (assistantMessage.Length > 4_000)
            assistantMessage = assistantMessage[..(4_000 - shortened.Length)] + shortened;

        lock (_sync)
        {
            if (!_entries.TryGetValue(identity, out var current)
                || !ReferenceEquals(current, entry))
                return false;
            var turns = entry.Turns.Append(new Turn(userMessage, assistantMessage))
                .TakeLast(_maximumRetainedTurns).ToList();
            var nextContext = contextDisposition switch
            {
                AskConversationContextDisposition.Preserve => Clone(entry.Context),
                AskConversationContextDisposition.Replace when context is not null => Clone(context),
                AskConversationContextDisposition.Replace => throw new InvalidDataException(
                    "Replacing assistant context requires one structured context."),
                AskConversationContextDisposition.Clear => null,
                _ => throw new ArgumentOutOfRangeException(nameof(contextDisposition)),
            };
            var bytes = Bytes(turns, nextContext);
            while (bytes > _maximumThreadBytes && turns.Count > 1)
            {
                turns.RemoveAt(0);
                bytes = Bytes(turns, nextContext);
            }
            if (bytes > _maximumThreadBytes) return false;

            var projected = _retainedBytes - entry.RetainedBytes + bytes;
            while (projected > _maximumRetainedBytes)
            {
                var oldest = OldestInactive(except: entry);
                if (oldest is null) return false;
                RemoveEntry(oldest.Value.Identity, oldest.Value.Entry);
                projected = _retainedBytes - entry.RetainedBytes + bytes;
            }

            _retainedBytes -= entry.RetainedBytes;
            entry.Turns = turns;
            entry.Context = nextContext;
            entry.RetainedBytes = bytes;
            entry.LastCommittedAt = _clock.GetUtcNow();
            _retainedBytes += bytes;
            return true;
        }
    }

    private void Release(string identity, Entry entry, bool committed)
    {
        lock (_sync)
        {
            entry.Pins--;
            entry.Gate.Release();
            if (!committed) RemoveEmptyEntry(identity, entry);
        }
    }

    private bool MakeRoomForThread()
    {
        while (_entries.Count >= _maximumThreads)
        {
            var oldest = OldestInactive(except: null);
            if (oldest is null) return false;
            RemoveEntry(oldest.Value.Identity, oldest.Value.Entry);
        }
        return true;
    }

    private (string Identity, Entry Entry)? OldestInactive(Entry? except)
    {
        var oldest = _entries
            .Where(pair => pair.Value.Pins == 0 && !ReferenceEquals(pair.Value, except))
            .OrderBy(pair => pair.Value.LastCommittedAt)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        return oldest.Key is null ? null : (oldest.Key, oldest.Value);
    }

    private void RemoveExpired()
    {
        var cutoff = _clock.GetUtcNow() - _idleTtl;
        foreach (var pair in _entries
                     .Where(pair => pair.Value.Pins == 0
                         && pair.Value.LastCommittedAt < cutoff)
                     .ToArray())
            RemoveEntry(pair.Key, pair.Value);
    }

    private void RemoveEmptyEntry(string identity, Entry entry)
    {
        if (entry.Pins == 0 && entry.Turns.Count == 0
            && _entries.TryGetValue(identity, out var current)
            && ReferenceEquals(current, entry))
            RemoveEntry(identity, entry);
    }

    private void RemoveEntry(string identity, Entry entry)
    {
        if (!_entries.TryGetValue(identity, out var current)
            || !ReferenceEquals(current, entry)) return;
        _entries.Remove(identity);
        _retainedBytes -= entry.RetainedBytes;
        if (_retainedBytes < 0) _retainedBytes = 0;
    }

    private static JsonArray History(Entry entry)
    {
        var history = new JsonArray();
        foreach (var turn in entry.Turns)
        {
            history.Add(new JsonObject { ["role"] = "user", ["content"] = turn.User });
            history.Add(new JsonObject { ["role"] = "assistant", ["content"] = turn.Assistant });
        }
        return history;
    }

    private static AskConversationContext? Clone(AskConversationContext? context) => context is null
        ? null
        : new AskConversationContext(
            context.Subjects.Select(subject => new AskResolvedSubjectContext(
                subject.Work, subject.ArticleAnchor, subject.ExactLexId,
                subject.AuthoritySource is null ? null : new AskSubjectAuthoritySource(
                    subject.AuthoritySource.Kind,
                    subject.AuthoritySource.Identifier,
                    subject.AuthoritySource.Segment,
                    subject.AuthoritySource.Language,
                    subject.AuthoritySource.SourceUri))).ToArray(),
            context.ArticleNumber);

    private static long Bytes(
        IReadOnlyList<Turn> turns,
        AskConversationContext? context)
    {
        long bytes = 0;
        foreach (var turn in turns)
            bytes += Encoding.UTF8.GetByteCount(turn.User)
                + Encoding.UTF8.GetByteCount(turn.Assistant) + 32;
        if (context is null) return bytes;
        bytes += context.ArticleNumber is null ? 0 : Encoding.UTF8.GetByteCount(context.ArticleNumber);
        foreach (var subject in context.Subjects)
        {
            bytes += Encoding.UTF8.GetByteCount(subject.Work)
                + (subject.ArticleAnchor is null ? 0 : Encoding.UTF8.GetByteCount(subject.ArticleAnchor))
                + (subject.ExactLexId is null ? 0 : Encoding.UTF8.GetByteCount(subject.ExactLexId))
                + 24;
            if (subject.AuthoritySource is { } source)
                bytes += Encoding.UTF8.GetByteCount(source.Kind)
                    + Encoding.UTF8.GetByteCount(source.Identifier)
                    + Encoding.UTF8.GetByteCount(source.Segment)
                    + Encoding.UTF8.GetByteCount(source.Language)
                    + Encoding.UTF8.GetByteCount(source.SourceUri)
                    + 24;
        }
        return bytes;
    }

    private static string NewToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return token;
    }

    private static string Identity(string token) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private static bool ScopeMatches(string? expected, string? supplied) =>
        expected is null || supplied is null
            ? expected is null && supplied is null
            : expected.Length == supplied.Length
              && CryptographicOperations.FixedTimeEquals(
                  Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(supplied));

    private static bool IsValidOwnerScope(string? scope) => scope is null
        || scope is { Length: 64 }
        && scope.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    internal long RetainedBytes
    {
        get { lock (_sync) return _retainedBytes; }
    }

    internal int Count
    {
        get { lock (_sync) return _entries.Count; }
    }

    internal long RetainedBytesFor(string token)
    {
        lock (_sync)
            return IsValidToken(token)
                && _entries.TryGetValue(Identity(token), out var entry)
                    ? entry.RetainedBytes : 0;
    }

    internal int RetainedTurnsFor(string token)
    {
        lock (_sync)
            return IsValidToken(token)
                && _entries.TryGetValue(Identity(token), out var entry)
                    ? entry.Turns.Count : 0;
    }

    internal int WaitersFor(string token)
    {
        lock (_sync)
            return IsValidToken(token)
                && _entries.TryGetValue(Identity(token), out var entry)
                    ? entry.Waiters : 0;
    }
}
