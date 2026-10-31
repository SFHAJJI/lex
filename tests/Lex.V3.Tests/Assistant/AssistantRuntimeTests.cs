using Lex.V3.Api;
using Lex.V3.Contracts.Platform.Assistant;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The switch (R30): off by default, on only when every condition holds, the operator off-switch winning; ask's own
/// per-client limit below 60 a minute; the per-answer ceiling and the monthly cap (R40, R114); telemetry in buckets (R32).
/// </summary>
[TestClass]
public sealed class AssistantRuntimeTests
{
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private long _ticks;

        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by)
        {
            _ticks += by.Ticks;
            Now += by;
        }
    }

    private sealed class CostlyPlanner(long cost) : IAssistantPlanner
    {
        public string PlannerId => "costly-test-planner/1";

        public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken) => new("{}", cost);
    }

    [TestMethod]
    public void TheSwitchIsOffByDefaultAndOnOnlyWhenEveryConditionHolds()
    {
        var planner = new AssistantRuleTestPlanner();
        Assert.IsFalse(V3AssistantRuntime.Off.IsOn);
        Assert.AreEqual("switch off (the default)", V3AssistantRuntime.Off.OffBecause);
        Assert.AreEqual("switch off (the default)", V3AssistantRuntime.FromConfiguration(null, null, planner).OffBecause);
        Assert.AreEqual("switch off (the default)", V3AssistantRuntime.FromConfiguration("ON", null, planner).OffBecause, "exactly on");
        Assert.AreEqual("switch off (the default)", V3AssistantRuntime.FromConfiguration("true", null, planner).OffBecause, "exactly on");
        Assert.AreEqual("operator off-switch set", V3AssistantRuntime.FromConfiguration("on", "x", planner).OffBecause, "any value of the off-switch wins");
        Assert.AreEqual("no planner wired", V3AssistantRuntime.FromConfiguration("on", null, null).OffBecause, "every deployment of this code");
        Assert.AreEqual("no planner wired", V3AssistantRuntime.FromConfiguration("on", "", null).OffBecause, "an empty off-switch is unset");
        Assert.AreEqual("templates or handoff entries not yet reviewed by a person", V3AssistantRuntime.FromConfiguration("on", null, planner).OffBecause,
            "R43 and R42: the people's reviews are a condition of the switch, not a note beside it");

        var on = new V3AssistantRuntime(true, false, planner, requireReviewedWording: false);
        Assert.IsTrue(on.IsOn);
        on.Trip();
        Assert.IsFalse(on.IsOn, "the breaker turns it off until the process starts again");
    }

    [TestMethod]
    public void AsksOwnLimitAllowsTwentyAMinutePerClientAndRefills()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var runtime = new V3AssistantRuntime(true, false, new AssistantRuleTestPlanner(), requireReviewedWording: false, time);
        Assert.AreEqual(0, runtime.AdmitAsk(), "a request that names no client is not limited");
        using (V3AssistantRuntime.EnterClient((UInt128)7))
        {
            for (var at = 0; at < V3AssistantRuntime.AskPerMinute; at++)
            {
                Assert.AreEqual(0, runtime.AdmitAsk(), $"ask {at + 1}");
            }

            Assert.IsTrue(runtime.AdmitAsk() > 0, "the twenty-first in the minute waits");
            time.Advance(TimeSpan.FromSeconds(3));
            Assert.AreEqual(0, runtime.AdmitAsk(), "one token comes back every three seconds");
        }

        // The review of #1013, LOW 4: a bucket filled again is forgotten, so nothing is kept of a client who stopped asking.
        Assert.AreEqual(1, runtime.BucketsHeld);
        time.Advance(TimeSpan.FromMinutes(2));
        using (V3AssistantRuntime.EnterClient((UInt128)8))
        {
            Assert.AreEqual(0, runtime.AdmitAsk());
        }

        Assert.AreEqual(1, runtime.BucketsHeld, "the first client's bucket, filled again, was swept when the next ask came");
    }

    [TestMethod]
    public void TheMonthlyCapHoldsAndANewMonthStartsAgain()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var runtime = new V3AssistantRuntime(true, false, new AssistantRuleTestPlanner(), requireReviewedWording: false, time);
        var answers = 0;
        while (runtime.TryReserve())
        {
            runtime.Settle(V3AssistantRuntime.PerAnswerCeilingMicroEuros * 10);
            answers++;
        }

        Assert.AreEqual(V3AssistantRuntime.MonthlyCapMicroEuros / V3AssistantRuntime.PerAnswerCeilingMicroEuros, answers, "a reported cost is never counted above the ceiling");
        Assert.IsTrue(runtime.CapReached);
        Assert.AreEqual(V3AssistantRuntime.MonthlyCapMicroEuros, runtime.SpentMicroEuros);
        time.Now = new DateTimeOffset(2026, 11, 1, 0, 0, 1, TimeSpan.Zero);
        Assert.IsFalse(runtime.CapReached, "the cap is per month");
        Assert.AreEqual(0, runtime.SpentMicroEuros);
    }

    [TestMethod]
    public void APlannerOverTheCeilingIsNeverUsed()
    {
        var runtime = new V3AssistantRuntime(true, false, new CostlyPlanner(V3AssistantRuntime.PerAnswerCeilingMicroEuros + 1), requireReviewedWording: false);
        var budgeted = runtime.Budgeted();
        Assert.ThrowsExactly<InvalidOperationException>(() => budgeted.Plan(new V3PlannerInput("question", V3AnswerLocale.Fr, [], ["fra"]), CancellationToken.None));
        Assert.AreEqual(V3AssistantRuntime.PerAnswerCeilingMicroEuros, runtime.SpentMicroEuros, "what it cost is still counted, at the ceiling");
    }

    [TestMethod]
    public void TelemetryKeepsBucketsOfClosedVocabulariesOnly()
    {
        var runtime = new V3AssistantRuntime(true, false, new AssistantRuleTestPlanner(), requireReviewedWording: false);
        runtime.Count(new V3AskPresented(V3AskPresentationResult.LocalizationUnavailable, V3AskOutcomeReason.LocaleUnavailable), TimeSpan.FromMilliseconds(40));
        runtime.CountError("a message carrying a question's words");
        CollectionAssert.AreEquivalent(
            new[] { "outcome:locale_unavailable", "latency:under_1s", "presentation:localization_unavailable", "error:other" },
            runtime.Telemetry.Keys.ToArray(),
            "an error bucket is a closed name; nothing a caller passes becomes a key");
        Assert.AreEqual("over_10s", V3AssistantRuntime.LatencyBucket(TimeSpan.FromSeconds(11)));
    }
}
