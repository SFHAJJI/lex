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
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("user-agent", profile.CrawlerUserAgent);

        var policy = await ReadDeclaredRobotsPolicyAsync(http, profile);
        var endpoint = new Uri(profile.RequestTarget, UriKind.Absolute);
        var verdict = RobotsExclusionPolicy.Evaluate(
            policy, profile.RobotsProductToken, endpoint.PathAndQuery);
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Allowed,
            verdict,
            "the publisher's own policy governs this census exactly as it governs a run; a refusal "
                + "here is an answer, not a reason to proceed anyway.");

        var missing = new List<string>();
        var present = 0;
        foreach (var predicateIri in EuObjectFactsDiscoveryPlan.ObjectFactAuthorityPredicateIris
            .OrderBy(static iri => iri, StringComparer.Ordinal))
        {
            if (await ExistsAsync(http, profile, predicateIri))
            {
                present++;
            }
            else
            {
                missing.Add(predicateIri);
            }
        }

        Assert.AreEqual(
            0,
            missing.Count,
            "these predicates are asked of the publisher on every run and name nothing upstream, so "
                + "every object answers them as an explicit unbound row and the corpus records an "
                + "absence that was never really measured: "
                + string.Join(", ", missing));
        Assert.AreEqual(
            EuObjectFactsDiscoveryPlan.ObjectFactAuthorityPredicateIris.Count,
            present,
            "every declared predicate must have been asked about exactly once.");
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
    private static async Task<byte[]> ReadDeclaredRobotsPolicyAsync(
        HttpClient http,
        OfficialMachineQuerySourceProfile profile)
    {
        byte[]? terminal = null;
        foreach (var step in profile.RobotsRoute.Steps)
        {
            using var response = await http.GetAsync(step.RequestedUri);
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
        string predicateIri)
    {
        using var content = new StringContent(
            $"ASK {{ ?s <{predicateIri}> ?o }}", Encoding.UTF8, "application/sparql-query");
        using var request = new HttpRequestMessage(HttpMethod.Post, profile.RequestTarget)
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("accept", profile.Accept);

        using var response = await http.SendAsync(request);
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            $"the census could not ask about {predicateIri}; an unanswered question is not a "
                + "missing predicate, and must never be recorded as one.");

        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return document.RootElement.GetProperty("boolean").GetBoolean();
    }
}
