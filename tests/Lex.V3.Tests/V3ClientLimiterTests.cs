using System.Net;
using System.Reflection;
using Lex.V3.Api;

namespace Lex.V3.Tests;

/// <summary>
/// The per-client limit (the specification of record's R109 and R46; the plan's item 10.1): who the client is, its two budgets
/// and its in-flight cap, and what it holds and for how long. The handler's use of it is held in <c>V3ClientLimitTests</c>
/// (Lex.V3.Ingest.Tests, over a mount) and <c>V3HostAnswerTests</c>.
/// </summary>
[TestClass]
public sealed class V3ClientLimiterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A clock the test moves, and a timer the test fires.</summary>
    internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public TimerCallback? Callback { get; private set; }

        public TimeSpan? Period { get; private set; }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;

        public void Fire() => Callback?.Invoke(null);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Callback = callback;
            Period = period;
            return new Held();
        }

        private sealed class Held : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static byte[] Key(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    private static (V3ClientLimiter Limiter, ManualTime Time) Limiter(Func<byte[]>? keys = null)
    {
        var time = new ManualTime(Noon);
        return (new V3ClientLimiter(time, keys ?? (() => Key(7))), time);
    }

    private static V3ClientLimiter.Client Public(V3ClientLimiter limiter, string address) =>
        limiter.ClientOf(IPAddress.Parse(address), null) ?? throw new AssertFailedException($"{address} names no client");

    [TestMethod]
    public void APublicPeerIsTheClientAndWhatItSaysItForwardsIsIgnored()
    {
        var (limiter, _) = Limiter();
        var alone = limiter.ClientOf(IPAddress.Parse("203.0.113.77"), null);
        Assert.IsNotNull(alone);
        Assert.AreEqual(alone, limiter.ClientOf(IPAddress.Parse("203.0.113.77"), "198.51.100.23"), "anyone can write the header: a public peer is the client");
        Assert.AreEqual(alone, limiter.ClientOf(IPAddress.Parse("::ffff:203.0.113.77"), null), "an IPv4 address mapped into IPv6 is that IPv4 address");
        Assert.AreNotEqual(alone, limiter.ClientOf(IPAddress.Parse("203.0.113.78"), null), "another address is another client");
    }

    [TestMethod]
    public void APeerInsideTheHostIsTheIngressAndTheClientIsTheLastAddressItForwards()
    {
        var (limiter, _) = Limiter();
        var client = Public(limiter, "203.0.113.77");
        foreach (var ingress in new[] { "10.0.0.4", "172.16.3.1", "172.31.255.254", "192.168.1.9", "100.100.0.7", "100.64.0.1", "169.254.1.1", "127.0.0.1", "::1", "fd00::5", "fe80::1", "::ffff:10.1.2.3" })
        {
            var peer = IPAddress.Parse(ingress);
            Assert.IsTrue(V3ClientLimiter.InsideHost(peer), ingress);
            Assert.AreEqual(client, limiter.ClientOf(peer, "203.0.113.77"), $"{ingress}: the ingress forwards the client");
            Assert.AreEqual(client, limiter.ClientOf(peer, "198.51.100.23, 203.0.113.77"), $"{ingress}: the last address is the one the ingress appended; the first is the caller's own say");
            Assert.AreEqual(client, limiter.ClientOf(peer, "198.51.100.23,203.0.113.77:51234"), $"{ingress}: an address with a port");
            Assert.IsNull(limiter.ClientOf(peer, null), $"{ingress}: nothing forwarded, a caller on the host itself: no client");
            Assert.IsNull(limiter.ClientOf(peer, " "), ingress);
            Assert.IsNull(limiter.ClientOf(peer, "unknown"), $"{ingress}: what is no address names no client");
        }

        foreach (var outside in new[] { "203.0.113.77", "8.8.8.8", "172.32.0.1", "172.15.255.255", "100.128.0.1", "192.169.0.1", "11.0.0.1", "2001:db8::1" })
        {
            Assert.IsFalse(V3ClientLimiter.InsideHost(IPAddress.Parse(outside)), outside);
        }

        Assert.IsNull(limiter.ClientOf(null, "203.0.113.77"), "no connection address (a request made in process): no client");
    }

    [TestMethod]
    public void AnIpv6AddressCountsByItsSlash64AndTheTwoFamiliesNeverMeet()
    {
        var (limiter, _) = Limiter();
        var one = Public(limiter, "2001:db8:1:2::1");
        Assert.AreEqual(one, Public(limiter, "2001:db8:1:2:ffff:ffff:ffff:ffff"), "one /64 is one client");
        Assert.AreNotEqual(one, Public(limiter, "2001:db8:1:3::1"), "another /64 is another");
        Assert.AreNotEqual(Public(limiter, "32.1.13.184"), Public(limiter, "2001:db8::"), "an IPv4 address and an IPv6 prefix of the same bytes are two clients");
    }

    [TestMethod]
    public void TheKeyIsTheProcesssOwnSoTheSameAddressIsAnotherKeyUnderAnotherAndTheSameUnderTheSame()
    {
        var (first, _) = Limiter(() => Key(1));
        var (again, _) = Limiter(() => Key(1));
        var (other, _) = Limiter(() => Key(2));
        Assert.AreEqual(Public(first, "203.0.113.77"), Public(again, "203.0.113.77"), "a keyed hash: one key, one value");
        Assert.AreNotEqual(Public(first, "203.0.113.77"), Public(other, "203.0.113.77"), "under another key the same address is another value");

        // The default key is drawn at random for each process (each limiter).
        Assert.AreNotEqual(Public(new V3ClientLimiter(), "203.0.113.77"), Public(new V3ClientLimiter(), "203.0.113.77"));
    }

    [TestMethod]
    public void SixtyApiRequestsAMinuteThenTheWaitUntilTheNextTokenAndOneBackEachSecond()
    {
        var (limiter, time) = Limiter();
        var client = Public(limiter, "203.0.113.77");
        for (var index = 0; index < V3ClientLimiter.ApiPerMinute; index++)
        {
            using var admitted = limiter.AdmitApi(client);
            Assert.IsTrue(admitted.Admitted, $"request {index + 1}");
            Assert.AreEqual(0, admitted.RetryAfterSeconds);
        }

        using (var refused = limiter.AdmitApi(client))
        {
            Assert.IsFalse(refused.Admitted, "the 61st within the minute");
            Assert.AreEqual(1, refused.RetryAfterSeconds, "one token comes back each second");
        }

        Assert.IsTrue(limiter.AdmitApi(Public(limiter, "203.0.113.78")).Admitted, "another client has its own budget");
        Assert.IsTrue(limiter.AdmitApi(null).Admitted, "no client is never refused");

        time.Advance(TimeSpan.FromMilliseconds(400));
        using (var early = limiter.AdmitApi(client))
        {
            Assert.IsFalse(early.Admitted);
            Assert.AreEqual(1, early.RetryAfterSeconds, "never less than a second");
        }

        time.Advance(TimeSpan.FromMilliseconds(600));
        using (var next = limiter.AdmitApi(client))
        {
            Assert.IsTrue(next.Admitted, "a second later, one token");
        }

        Assert.IsFalse(limiter.AdmitApi(client).Admitted);
        time.Advance(TimeSpan.FromMinutes(5));
        var count = 0;
        while (limiter.AdmitApi(client) is { Admitted: true } admission)
        {
            admission.Dispose();
            count++;
        }

        Assert.AreEqual(V3ClientLimiter.ApiPerMinute, count, "the bucket fills to a minute's worth and no more");
    }

    [TestMethod]
    public void AtMostFourOfAClientsRequestsAreAnsweredAtOnce()
    {
        var (limiter, _) = Limiter();
        var client = Public(limiter, "203.0.113.77");
        var held = Enumerable.Range(0, V3ClientLimiter.ApiInFlight).Select(_ => limiter.AdmitApi(client)).ToList();
        Assert.IsTrue(held.All(static admission => admission.Admitted));
        using (var fifth = limiter.AdmitApi(client))
        {
            Assert.IsFalse(fifth.Admitted, "a fifth at once, with tokens left");
            Assert.AreEqual(1, fifth.RetryAfterSeconds);
        }

        held[0].Dispose();
        held[0].Dispose();
        using (var after = limiter.AdmitApi(client))
        {
            Assert.IsTrue(after.Admitted, "one answered, one place free; a second release frees nothing more");
            using var over = limiter.AdmitApi(client);
            Assert.IsFalse(over.Admitted, "the place the twice-disposed admission freed is taken again");
        }

        foreach (var admission in held)
        {
            admission.Dispose();
        }
    }

    [TestMethod]
    public void ThreeHundredRendersAMinuteWellAboveTheApisThenThePageAsBuilt()
    {
        var (limiter, time) = Limiter();
        var client = Public(limiter, "203.0.113.77");
        Assert.IsGreaterThanOrEqualTo(5 * V3ClientLimiter.ApiPerMinute, V3ClientLimiter.RendersPerMinute, "R46: the site's own pages sit well above the API's limit");
        for (var index = 0; index < V3ClientLimiter.RendersPerMinute; index++)
        {
            using var render = limiter.AdmitRender(client);
            Assert.IsTrue(render.Admitted, $"render {index + 1}");
        }

        Assert.IsFalse(limiter.AdmitRender(client).Admitted);
        using (var api = limiter.AdmitApi(client))
        {
            Assert.IsTrue(api.Admitted, "the two budgets are apart");
        }

        Assert.IsTrue(limiter.AdmitRender(null).Admitted);
        time.Advance(TimeSpan.FromSeconds(1));
        using var again = limiter.AdmitRender(client);
        Assert.IsTrue(again.Admitted, "five come back each second");
    }

    [TestMethod]
    public void ARenderHoldsOneOfTheClientsFourPlacesSoFourRequestsAnsweredAtOnceLeaveNoneForAPageAndTheReverse()
    {
        // The review of #995, F2: a server-rendered page asks the mount and runs the renderer, so it is one of the client's
        // requests answered at once, and the in-flight cap holds renders too.
        var (limiter, _) = Limiter();
        var client = Public(limiter, "203.0.113.77");
        var apis = Enumerable.Range(0, V3ClientLimiter.ApiInFlight).Select(_ => limiter.AdmitApi(client)).ToList();
        Assert.IsTrue(apis.All(static admission => admission.Admitted));
        using (var page = limiter.AdmitRender(client))
        {
            Assert.IsFalse(page.Admitted, "four API requests being answered: the page is served as built, not rendered");
        }

        apis[0].Dispose();
        var render = limiter.AdmitRender(client);
        Assert.IsTrue(render.Admitted, "a place free: the page is rendered");
        using (var fifth = limiter.AdmitApi(client))
        {
            Assert.IsFalse(fifth.Admitted, "the render holds the fourth place while it runs");
            Assert.AreEqual(1, fifth.RetryAfterSeconds);
        }

        render.Dispose();
        using (var after = limiter.AdmitApi(client))
        {
            Assert.IsTrue(after.Admitted, "the render's place is freed once it is done");
        }

        foreach (var admission in apis)
        {
            admission.Dispose();
        }

        var renders = Enumerable.Range(0, V3ClientLimiter.ApiInFlight).Select(_ => limiter.AdmitRender(client)).ToList();
        Assert.IsTrue(renders.All(static admission => admission.Admitted), "four renders at once");
        using (var fifthRender = limiter.AdmitRender(client))
        {
            Assert.IsFalse(fifthRender.Admitted, "and a fifth at once is served as built");
        }

        foreach (var admission in renders)
        {
            admission.Dispose();
        }
    }

    [TestMethod]
    public void AnEntryIsForgottenOnceItsBucketsHaveFilledAgainAndEveryEntryWithTheDaysKey()
    {
        var keys = 0;
        var time = new ManualTime(Noon);
        var limiter = new V3ClientLimiter(time, () => Key((byte)++keys));
        Assert.AreEqual(V3ClientLimiter.SweepInterval, time.Period, "the sweep runs every minute");
        Assert.AreEqual(TimeSpan.FromMinutes(1), V3ClientLimiter.SweepInterval);

        var quiet = Public(limiter, "203.0.113.77");
        var busy = Public(limiter, "203.0.113.78");
        var holding = Public(limiter, "203.0.113.79");
        void Spend(V3ClientLimiter.Client client)
        {
            for (var index = 0; index < V3ClientLimiter.ApiPerMinute; index++)
            {
                limiter.AdmitApi(client).Dispose();
            }
        }

        Spend(quiet);
        var held = limiter.AdmitApi(holding);
        Assert.AreEqual(2, limiter.ClientsHeld, "only a client that asked is held");

        time.Advance(TimeSpan.FromSeconds(59));
        Spend(busy);
        time.Fire();
        Assert.AreEqual(3, limiter.ClientsHeld, "59 seconds after it spent its minute, the first client's bucket is not full yet");

        time.Advance(TimeSpan.FromSeconds(1));
        time.Fire();
        Assert.AreEqual(2, limiter.ClientsHeld, "full again a minute after its last request, the quiet client is forgotten; one being answered is kept");

        time.Advance(TimeSpan.FromMinutes(1));
        time.Fire();
        Assert.AreEqual(1, limiter.ClientsHeld, "the busy one a minute after its last request");
        held.Dispose();
        time.Fire();
        Assert.AreEqual(0, limiter.ClientsHeld, "and the one whose request was answered");

        // A new day: a new key, and every entry made under the old one dropped at once.
        limiter.AdmitApi(quiet).Dispose();
        Assert.AreEqual(1, keys);
        var before = Public(limiter, "203.0.113.77");
        time.Advance(TimeSpan.FromHours(12));
        time.Fire();
        Assert.AreEqual(2, keys, "the key is replaced when the UTC day changes");
        Assert.AreEqual(0, limiter.ClientsHeld);
        Assert.AreNotEqual(before, Public(limiter, "203.0.113.77"), "and the same address is another value under it");
    }

    [TestMethod]
    public void PastTheMostEntriesItHoldsANewClientIsAnsweredWithoutOne()
    {
        var (limiter, _) = Limiter();
        var bytes = new byte[4];
        for (var index = 0; index < V3ClientLimiter.MaximumClients; index++)
        {
            bytes[0] = 11;
            bytes[1] = (byte)(index >> 16);
            bytes[2] = (byte)(index >> 8);
            bytes[3] = (byte)index;
            limiter.AdmitApi(limiter.ClientOf(new IPAddress(bytes), null)).Dispose();
        }

        Assert.AreEqual(V3ClientLimiter.MaximumClients, limiter.ClientsHeld);
        var late = Public(limiter, "203.0.113.77");
        for (var index = 0; index < V3ClientLimiter.ApiPerMinute + 5; index++)
        {
            Assert.IsTrue(limiter.AdmitApi(late).Admitted, "no entry: answered, and not counted");
        }

        Assert.AreEqual(V3ClientLimiter.MaximumClients, limiter.ClientsHeld, "the table grows no further");
    }

    [TestMethod]
    public void TheLimitHoldsNoAddressOnlyAKeyedHashAndCounts()
    {
        // What the limiter and an entry can hold, by their fields: never an address, a string or a header value.
        var nested = typeof(V3ClientLimiter).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var type in nested.Append(typeof(V3ClientLimiter)))
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.AreNotEqual(typeof(IPAddress), field.FieldType, $"{type.Name}.{field.Name}");
                Assert.AreNotEqual(typeof(string), field.FieldType, $"{type.Name}.{field.Name}");
            }
        }

        var client = new V3ClientLimiter(new ManualTime(Noon), () => Key(7)).ClientOf(IPAddress.Parse("203.0.113.77"), null)!.Value;
        CollectionAssert.AreEqual(new[] { "Key" }, typeof(V3ClientLimiter.Client).GetProperties().Select(static property => property.Name).ToArray());
        Assert.AreEqual(typeof(UInt128), typeof(V3ClientLimiter.Client).GetProperty("Key")!.PropertyType, "16 bytes of the hash, never the address");
        Assert.AreNotEqual(UInt128.Zero, client.Key);
    }
}
