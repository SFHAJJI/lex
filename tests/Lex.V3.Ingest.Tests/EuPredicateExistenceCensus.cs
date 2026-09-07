using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// E0(c), #409, clause S2-A05: every CDM predicate family P asks the publisher for is proved to
/// exist upstream, so a predicate that names nothing is surfaced instead of quietly answering
/// nothing.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT THIS ANSWERS, AND IT IS NOT HYPOTHETICAL. The V3 spec's E0(c) exists because
/// <c>case-law_annuls_resource_legal</c> was carried in scope while having zero live triples: a
/// dead predicate does not fail, it simply returns nothing, and a closed <c>VALUES ?predicate</c>
/// block makes that indistinguishable from a corpus that genuinely has no such edge. I measured
/// both halves of that on 2026-09-07 against this endpoint: the dead predicate answers
/// <c>ASK</c> false, while <c>case-law_requests_annulment_of_resource_legal</c> (1,902) and
/// <c>case-law_declares_void_resource_legal</c> (1,098) are real and absent from this codebase
/// entirely. Nothing in the tree would have reported either fact.
/// </para>
/// <para>
/// Why this is a census rather than a runtime check. The question "does this predicate exist" is
/// answered once per scope, not once per run: asking it on every enumeration would add a request
/// per predicate to every nightly for an answer that changes on the publisher's schedule, not
/// ours. So it is opt-in and bounded, one <c>ASK</c> per declared predicate, in the same shape as
/// the other live probes in this project, and it adds nothing to the nightly path.
/// </para>
/// <para>
/// It asks about exactly the predicates the product asks about, taken from
/// <see cref="EuObjectFactsDiscoveryPlan.ObjectFactAuthorityPredicateIris"/> -- the same set family
/// P's own <c>VALUES ?predicate</c> block is built from. A census with its own hand-written list
/// would drift away from the query and start proving the wrong thing.
/// </para>
/// <para>
/// THE SOURCE POLICY IS NOT ONLY ROBOTS, WHICH THE FIRST VERSION OF THIS PROBE GOT HALF RIGHT.
/// It evaluated robots literally and then sent thirteen same-origin requests as fast as the socket
/// allowed -- 2.699 seconds for all thirteen, against a declared floor of 1,500 ms between
/// requests to one actual network origin. The reviewer measured that. Robots and pacing are the
/// same publisher policy expressed in two places, and an acceptance probe that keeps one and
/// discards the other is still sending unpoliced traffic under Lex's own crawler identity. Every
/// outbound request below now goes through <see cref="OriginPacer"/> at the profile's own
/// <see cref="OfficialMachineQuerySourceProfile.MinimumRequestInterval"/>, scoped per origin as
/// <see cref="OfficialHttpPacingScope.ProcessActualNetworkOrigin"/> requires -- including the
/// robots request to <c>publications.europa.eu</c>, which shares its origin with every ASK that
/// follows it.
/// </para>
/// <para>
/// ROBOTS FIRST, LITERALLY, AND THROUGH THE PRODUCT'S OWN EVALUATOR (Decision 83). The profile
/// declares its robots route -- the 301 to <c>op.europa.eu</c> and the terminal 200 -- so this
/// walks that declared route, checks each observed status against the declared one, and evaluates
/// the endpoint's own path against the retained policy using
/// <see cref="RobotsExclusionPolicy.Evaluate"/> and the profile's own product token. A census that
/// reimplemented that judgement, or skipped it because "it is only a test", would be asserting a
/// permission rather than reading one.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuPredicateExistenceCensus
{
    [TestMethod]
    public async Task EveryPredicateFamilyPAsksForExistsAtThePublisher()
    {
        if (Environment.GetEnvironmentVariable("LEX_EU_PREDICATE_CENSUS") != "1")
        {
            Assert.Inconclusive(
                "Set LEX_EU_PREDICATE_CENSUS=1 for the live bounded EU predicate-existence census.");
        }

        var profile = OfficialMachineQuerySourceProfiles.Resolve(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql);
        var pacer = new OriginPacer(
            profile.MinimumRequestInterval, TimeProvider.System, Task.Delay);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("user-agent", profile.CrawlerUserAgent);

        var started = Stopwatch.StartNew();
        var policy = await ReadDeclaredRobotsPolicyAsync(http, profile, pacer);
        var endpoint = new Uri(profile.RequestTarget, UriKind.Absolute);
        var verdict = RobotsExclusionPolicy.Evaluate(
            policy, profile.RobotsProductToken, endpoint.PathAndQuery);
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Allowed,
            verdict,
            "the publisher's own policy governs this census exactly as it governs a run; a refusal "
                + "here is an answer, not a reason to proceed anyway.");

        var predicates = EuObjectFactsDiscoveryPlan.ObjectFactAuthorityPredicateIris
            .OrderBy(static iri => iri, StringComparer.Ordinal)
            .ToArray();
        var missing = new List<string>();
        foreach (var predicateIri in predicates)
        {
            if (!await ExistsAsync(http, profile, pacer, predicateIri))
            {
                missing.Add(predicateIri);
            }
        }

        started.Stop();
        Assert.AreEqual(
            0,
            missing.Count,
            "these predicates are asked of the publisher on every run and name nothing upstream, so "
                + "every object answers them as an explicit unbound row and the corpus records an "
                + "absence that was never really measured: "
                + string.Join(", ", missing));

        // Said again from the wall clock, because the pacer's own unit guard proves it asks for the
        // right waits and this proves the run actually took them. Every ASK shares one origin with
        // the first robots request, so the floor covers that boundary too.
        var floor = profile.MinimumRequestInterval * predicates.Length;
        Assert.IsTrue(
            started.Elapsed >= floor,
            $"the census sent {predicates.Length + 2} requests in {started.Elapsed}, which is under "
                + $"the {floor} this publisher's declared pacing requires for the shared origin.");
    }

    /// <summary>
    /// The pacer asks for the profile's own interval between requests to one origin, and asks for
    /// nothing between requests to different origins.
    /// </summary>
    /// <remarks>
    /// This runs everywhere, not only under the live gate, so the policy the census claims to obey
    /// is checked on every build rather than only when someone opts into publisher traffic. The
    /// clock is frozen, so the recorded waits are what the pacer asked for rather than what a
    /// loaded machine happened to take.
    /// </remarks>
    [TestMethod]
    public async Task ThePacerHoldsTheProfileIntervalPerOrigin()
    {
        var profile = OfficialMachineQuerySourceProfiles.Resolve(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql);
        var waits = new List<TimeSpan>();
        var frozen = new FrozenClock(DateTimeOffset.UnixEpoch);
        var pacer = new OriginPacer(
            profile.MinimumRequestInterval,
            frozen,
            wait =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            });

        var sparql = new Uri("https://publications.europa.eu/webapi/rdf/sparql");
        var robotsSameOrigin = new Uri("https://publications.europa.eu/robots.txt");
        var robotsOtherOrigin = new Uri("https://op.europa.eu/robots.txt");

        await pacer.BeforeSendAsync(robotsSameOrigin);
        await pacer.BeforeSendAsync(robotsOtherOrigin);
        await pacer.BeforeSendAsync(sparql);
        await pacer.BeforeSendAsync(sparql);

        Assert.AreEqual(
            2,
            waits.Count,
            "the first request to each origin waits for nothing; every later one to a seen origin "
                + "waits.");
        Assert.IsTrue(
            waits.TrueForAll(wait => wait >= profile.MinimumRequestInterval),
            $"every wait must be at least the profile's own {profile.MinimumRequestInterval}, and "
                + $"these were [{string.Join(", ", waits)}].");
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(1_500),
            profile.MinimumRequestInterval,
            "the interval is read from the profile; if the publisher's declared floor moves, this "
                + "census moves with it rather than keeping a stale copy.");
    }

    /// <summary>
    /// Walks the profile's own declared robots route and returns the terminal policy bytes,
    /// checking each hop against the status and Location the profile declares.
    /// </summary>
    /// <remarks>
    /// The route is walked rather than assumed because a census that fetched
    /// <c>op.europa.eu/robots.txt</c> directly would be reading a policy the product never reads,
    /// and would stop noticing the day the publisher changes the redirect.
    /// </remarks>
    /// <remarks>
    /// Internal so a sibling live probe reads the publisher's policy through this exact path
    /// rather than a second copy of it. Two robots readers drift, and the one that drifts is the
    /// one nobody runs.
    /// </remarks>
    internal static async Task<byte[]> ReadDeclaredRobotsPolicyAsync(
        HttpClient http,
        OfficialMachineQuerySourceProfile profile,
        OriginPacer pacer)
    {
        byte[]? terminal = null;
        foreach (var step in profile.RobotsRoute.Steps)
        {
            var target = new Uri(step.RequestedUri, UriKind.Absolute);
            await pacer.BeforeSendAsync(target);
            using var response = await http.GetAsync(target);
            Assert.AreEqual(
                step.ExpectedStatusCode,
                (int)response.StatusCode,
                $"the declared robots route step {step.RequestedUri} no longer answers as declared.");
            if (step.ExpectedLocation is { } location)
            {
                Assert.AreEqual(
                    location,
                    response.Headers.Location?.ToString(),
                    "the declared robots redirect no longer points where the profile says.");
                continue;
            }

            terminal = await response.Content.ReadAsByteArrayAsync();
        }

        Assert.IsNotNull(terminal, "the declared robots route must terminate in a policy body.");
        return terminal;
    }

    /// <summary>One bounded <c>ASK</c>: does the publisher hold any triple on this predicate?</summary>
    private static async Task<bool> ExistsAsync(
        HttpClient http,
        OfficialMachineQuerySourceProfile profile,
        OriginPacer pacer,
        string predicateIri)
    {
        using var content = new StringContent(
            $"ASK {{ ?s <{predicateIri}> ?o }}", Encoding.UTF8, "application/sparql-query");
        using var request = new HttpRequestMessage(HttpMethod.Post, profile.RequestTarget)
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("accept", profile.Accept);

        await pacer.BeforeSendAsync(new Uri(profile.RequestTarget, UriKind.Absolute));
        using var response = await http.SendAsync(request);
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            $"the census could not ask about {predicateIri}; an unanswered question is not a "
                + "missing predicate, and must never be recorded as one.");

        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return document.RootElement.GetProperty("boolean").GetBoolean();
    }

    /// <summary>
    /// Holds the profile's minimum interval between outbound requests to one actual network
    /// origin, which is what <see cref="OfficialHttpPacingScope.ProcessActualNetworkOrigin"/> means.
    /// </summary>
    /// <remarks>
    /// The origin key is scheme, host and port, so <c>publications.europa.eu</c> and
    /// <c>op.europa.eu</c> pace independently while the robots request and every ASK to the SPARQL
    /// endpoint -- the same origin -- pace together. The clock and the wait are both injected so
    /// the guard above can read what the pacer asked for without spending the interval.
    /// </remarks>
    internal sealed class OriginPacer(
        TimeSpan interval, TimeProvider time, Func<TimeSpan, Task> delay)
    {
        private readonly Dictionary<string, DateTimeOffset> _nextAllowed =
            new(StringComparer.Ordinal);

        internal async Task BeforeSendAsync(Uri target)
        {
            var origin = $"{target.Scheme}://{target.Host}:{target.Port}";
            var now = time.GetUtcNow();
            if (_nextAllowed.TryGetValue(origin, out var allowed) && allowed > now)
            {
                await delay(allowed - now);
                now = allowed;
            }

            _nextAllowed[origin] = now + interval;
        }
    }

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
