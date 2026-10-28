using System.Collections.Concurrent;
using Lex.V3.Contracts.Platform.Assistant;

namespace Lex.V3.Api;

/// <summary>
/// The assistant's switch (R30), its operator off-switch, its own per-client limit, its money caps (R40, R114) and its
/// telemetry buckets (R32). Off by default; and while src wires no planner, the switch cannot take effect at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>On only when every condition holds</b> (<see cref="IsOn"/>): <c>LEX_V3_ASSISTANT</c> is exactly <c>on</c>;
/// <c>LEX_V3_ASSISTANT_OFF</c>, the operator off-switch, is unset or empty (any value turns the assistant off, whatever the
/// switch says); a planner is wired; R43's qualified person reviewed every template and a person reviewed every handoff
/// entry; the monthly cap is not reached and no breaker has tripped. Otherwise <c>ask</c> answers the switched-off card,
/// byte for byte the card it answers today, and every other operation is untouched.
/// </para>
/// <para>
/// <b>Money</b> (R40; O22 recommends a spending cap of about 20 EUR a month, which the owner sets at the provider). Each
/// answer reserves the per-answer ceiling from the month's budget before it plans and settles what the planner reports
/// after; a planner that reports more than the ceiling is not used. At the monthly cap, ask answers the switched-off card
/// and search keeps working. The budget is held in memory and starts again when the process does: the provider's own hard
/// cap is the backstop, and it is the owner's to set before switch-on.
/// </para>
/// <para>
/// <b>Nothing stored</b> (R32). The limit holds 16 bytes of the keyed hash <see cref="V3ClientLimiter"/> already computes
/// and a count, never an address; telemetry holds counters keyed by closed vocabularies only (outcome, verdict, refusal
/// code, locale, latency bucket). No question, plan, trace or text reaches any field of this type.
/// </para>
/// </remarks>
internal sealed class V3AssistantRuntime
{
    internal const string SwitchVariable = "LEX_V3_ASSISTANT";
    internal const string OffSwitchVariable = "LEX_V3_ASSISTANT_OFF";

    /// <summary>ask's own per-client limit, a minute (R40: below 60).</summary>
    internal const int AskPerMinute = 20;

    /// <summary>The per-answer cost ceiling, in micro-euros (0.02 EUR): a driver default, re-sized on the replay set (R40).</summary>
    internal const long PerAnswerCeilingMicroEuros = 20_000;

    /// <summary>The monthly spend cap, in micro-euros (20 EUR, O22's recommended answer; the owner decides it).</summary>
    internal const long MonthlyCapMicroEuros = 20_000_000;

    /// <summary>How long a planner may take before the answer is the switched-off card (R40's 10 s to the full answer).</summary>
    internal static readonly TimeSpan PlannerDeadline = TimeSpan.FromSeconds(8);

    private static readonly AsyncLocal<UInt128?> CurrentClient = new();

    private readonly bool _switchedOn;
    private readonly bool _operatorOff;
    private readonly bool _requireReviewedWording;
    private readonly IAssistantPlanner? _planner;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<UInt128, Bucket> _buckets = new();
    private readonly ConcurrentDictionary<string, long> _telemetry = new(StringComparer.Ordinal);
    private readonly object _budgetLock = new();
    private (int Year, int Month) _budgetMonth;
    private long _spentMicroEuros;
    private long _reservedMicroEuros;
    private int _tripped;

    internal V3AssistantRuntime(
        bool switchedOn,
        bool operatorOff,
        IAssistantPlanner? planner,
        bool requireReviewedWording = true,
        TimeProvider? time = null)
    {
        _switchedOn = switchedOn;
        _operatorOff = operatorOff;
        _planner = planner;
        _requireReviewedWording = requireReviewedWording;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The runtime every mount starts with: off.</summary>
    internal static V3AssistantRuntime Off { get; } = new(false, false, null);

    /// <summary>
    /// The runtime a server's configuration names (Program.cs reads both variables once at start). No planner exists in src,
    /// so the server passes none, and the assistant stays off even with the switch set.
    /// </summary>
    internal static V3AssistantRuntime FromConfiguration(string? switchValue, string? offSwitchValue, IAssistantPlanner? planner) =>
        new(string.Equals(switchValue, "on", StringComparison.Ordinal), !string.IsNullOrEmpty(offSwitchValue), planner);

    /// <summary>Whether the assistant answers. Every condition must hold (see the remarks).</summary>
    internal bool IsOn => OffBecause is null;

    /// <summary>Why the assistant is off, in a few words for the server's start line, or null when it is on.</summary>
    internal string? OffBecause =>
        !_switchedOn ? "switch off (the default)"
        : _operatorOff ? "operator off-switch set"
        : _planner is null ? "no planner wired"
        : _requireReviewedWording && !(V3AnswerTemplates.QualifiedReviewComplete && V3HandoffRegistry.ReviewedByPerson)
            ? "templates or handoff entries not yet reviewed by a person"
        : Volatile.Read(ref _tripped) != 0 ? "a breaker tripped"
        : null;

    internal IAssistantPlanner? Planner => _planner;

    /// <summary>Trips the breaker: the assistant answers the switched-off card until the process starts again.</summary>
    internal void Trip() => Interlocked.Exchange(ref _tripped, 1);

    // ---- The per-client limit (R40) ---------------------------------------------------------------------------------

    private sealed class Bucket
    {
        public double Tokens = AskPerMinute;
        public long LastTicks;
    }

    private sealed class ClientScope(UInt128? previous) : IDisposable
    {
        public void Dispose() => CurrentClient.Value = previous;
    }

    /// <summary>
    /// Names the client of the request being answered, for ask's limit, until the scope is disposed: the same keyed hash the
    /// API's limit holds (<see cref="V3ClientLimiter.Client"/>), or null for a request that names no client.
    /// </summary>
    internal static IDisposable EnterClient(UInt128? client)
    {
        var scope = new ClientScope(CurrentClient.Value);
        CurrentClient.Value = client;
        return scope;
    }

    /// <summary>The seconds to wait before this client may ask again, or 0 when it may ask now. A request naming no client is not limited.</summary>
    internal int AdmitAsk()
    {
        if (CurrentClient.Value is not { } client)
        {
            return 0;
        }

        var now = _time.GetTimestamp();
        var bucket = _buckets.GetOrAdd(client, _ => new Bucket { LastTicks = now });
        lock (bucket)
        {
            var elapsed = _time.GetElapsedTime(bucket.LastTicks, now).TotalSeconds;
            bucket.Tokens = Math.Min(AskPerMinute, bucket.Tokens + (elapsed * AskPerMinute / 60.0));
            bucket.LastTicks = now;
            if (bucket.Tokens >= 1)
            {
                bucket.Tokens -= 1;
                return 0;
            }

            return (int)Math.Ceiling((1 - bucket.Tokens) * 60.0 / AskPerMinute);
        }
    }

    // ---- Money (R40, R114) ------------------------------------------------------------------------------------------

    /// <summary>Whether this month's budget can no longer hold one more answer at the per-answer ceiling.</summary>
    internal bool CapReached
    {
        get
        {
            lock (_budgetLock)
            {
                RollMonth();
                return _spentMicroEuros + _reservedMicroEuros + PerAnswerCeilingMicroEuros > MonthlyCapMicroEuros;
            }
        }
    }

    /// <summary>Reserves the per-answer ceiling from this month's budget, or answers false at the monthly cap.</summary>
    internal bool TryReserve()
    {
        lock (_budgetLock)
        {
            RollMonth();
            if (_spentMicroEuros + _reservedMicroEuros + PerAnswerCeilingMicroEuros > MonthlyCapMicroEuros)
            {
                return false;
            }

            _reservedMicroEuros += PerAnswerCeilingMicroEuros;
            return true;
        }
    }

    /// <summary>Settles a reservation with what the planner reported, never less than zero and never more than the ceiling.</summary>
    internal void Settle(long reportedMicroEuros)
    {
        lock (_budgetLock)
        {
            RollMonth();
            _reservedMicroEuros = Math.Max(0, _reservedMicroEuros - PerAnswerCeilingMicroEuros);
            _spentMicroEuros += Math.Clamp(reportedMicroEuros, 0, PerAnswerCeilingMicroEuros);
        }
    }

    internal long SpentMicroEuros
    {
        get
        {
            lock (_budgetLock)
            {
                RollMonth();
                return _spentMicroEuros;
            }
        }
    }

    private void RollMonth()
    {
        var now = _time.GetUtcNow();
        if (_budgetMonth != (now.Year, now.Month))
        {
            _budgetMonth = (now.Year, now.Month);
            _spentMicroEuros = 0;
            _reservedMicroEuros = 0;
        }
    }

    /// <summary>
    /// The planner as the pipeline calls it: within the deadline, its reported cost settled against the reservation, and an
    /// answer over the ceiling never used (it fails closed, as a planner that fails does).
    /// </summary>
    internal IAssistantPlanner Budgeted() => new BudgetedPlanner(this, _planner ?? throw new InvalidOperationException("No planner is wired."));

    private sealed class BudgetedPlanner(V3AssistantRuntime runtime, IAssistantPlanner inner) : IAssistantPlanner
    {
        public string PlannerId => inner.PlannerId;

        public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken)
        {
            if (!runtime.TryReserve())
            {
                throw new InvalidOperationException("The monthly cap is reached.");
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(PlannerDeadline);
            V3PlannerOutput output;
            try
            {
                output = inner.Plan(input, deadline.Token);
            }
            catch
            {
                runtime.Settle(0);
                throw;
            }

            runtime.Settle(output.CostMicroEuros);
            return output.CostMicroEuros > PerAnswerCeilingMicroEuros
                ? throw new InvalidOperationException("The planner's reported cost is over the per-answer ceiling.")
                : output;
        }
    }

    // ---- Telemetry (R32: buckets only) ------------------------------------------------------------------------------

    /// <summary>The latency buckets telemetry counts (R40's budgets are 3 s to the verdict and 10 s to the full answer).</summary>
    internal static string LatencyBucket(TimeSpan elapsed) =>
        elapsed < TimeSpan.FromSeconds(1) ? "under_1s"
        : elapsed < TimeSpan.FromSeconds(3) ? "under_3s"
        : elapsed < TimeSpan.FromSeconds(10) ? "under_10s"
        : "over_10s";

    /// <summary>
    /// Counts one answer under its buckets. Every key is a token of a closed vocabulary (the outcome reason, the verdict, the
    /// refusal registry's codes, the locale, the latency bucket); nothing a question carries can become a key.
    /// </summary>
    internal void Count(V3AskOutcome outcome, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Increment("outcome:" + Lex.V3.Contracts.ContractWire.NameOf(outcome.Reason));
        Increment("latency:" + LatencyBucket(elapsed));
        switch (outcome)
        {
            case V3AskAnswered answered:
                Increment("verdict:" + Lex.V3.Contracts.ContractWire.NameOf(answered.Dossier.Verdict));
                Increment("locale:" + Lex.V3.Contracts.ContractWire.NameOf(answered.Dossier.Locale));
                foreach (var code in answered.Dossier.Trace.Steps.Select(static step => step.RefusalCode).Where(static code => code is not null))
                {
                    Increment("refusal:" + (Lex.V3.Contracts.Platform.V3OperationRegistry.Reviewed.DeclaresRefusal(code!) ? code : "other"));
                }

                break;
            case V3AskPresented presented:
                Increment("presentation:" + Lex.V3.Contracts.ContractWire.NameOf(presented.Result));
                break;
        }
    }

    /// <summary>Counts an error bucket (the exception's type is not kept, only that one happened where).</summary>
    internal void CountError(string bucket) => Increment("error:" + (bucket is "planner" or "execution" or "composer" ? bucket : "other"));

    private void Increment(string key) => _telemetry.AddOrUpdate(key, 1, static (_, count) => count + 1);

    /// <summary>The counters, for a test to read; they are kept nowhere else and sent nowhere.</summary>
    internal IReadOnlyDictionary<string, long> Telemetry => _telemetry.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
}
