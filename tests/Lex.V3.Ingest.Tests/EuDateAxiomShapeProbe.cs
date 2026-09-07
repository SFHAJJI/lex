using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// E1, #410: what shape does the publisher actually reify an E1 date assertion in?
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS BEFORE ANY QUERY PLAN. `EuDateAxiomBinding.Create` refuses a null axiom, and the
/// fixture for the case the contract's own remarks describe as having "no qualifier example
/// evidenced anywhere in review/23" still supplies a real remote axiom identity with a null
/// qualifier list. So every E1 row needs the reified node and only the fd_335 qualifier is
/// optional, which means the acquisition slice has to ask the publisher for reifications. Writing
/// that query against an assumed graph shape is the exact error the contract's own history records
/// -- the first E1 head was rebuilt after being designed beside the evidence rather than from it.
/// </para>
/// <para>
/// WHAT IT IS NOT. Not a gate and not a census: it asserts only what must hold for its own answer
/// to be worth anything -- that the publisher's policy allows the request, that the endpoint
/// answered, and that the declared pacing was actually taken. The shape it finds is reported by a
/// person on the issue, because "what does this graph look like" is a question whose answer changes
/// on the publisher's schedule rather than ours, and pinning it as an assertion would make an
/// upstream edit look like our regression.
/// </para>
/// <para>
/// Two requests plus the robots read, opt-in behind <c>LEX_EU_AXIOM_PROBE=1</c>, paced through the
/// same <see cref="EuPredicateExistenceCensus.OriginPacer"/> and reading the publisher's policy through
/// <see cref="EuPredicateExistenceCensus.ReadDeclaredRobotsPolicyAsync"/> rather than a second copy
/// of it.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuDateAxiomShapeProbe
{
    private const string OwlAxiom = "http://www.w3.org/2002/07/owl#Axiom";
    private const string OwlAnnotatedProperty = "http://www.w3.org/2002/07/owl#annotatedProperty";

    /// <summary>The E1 predicate whose role the pinned fd_335 table is meant to qualify.</summary>
    private const string EntryIntoForce =
        "http://publications.europa.eu/ontology/cdm#resource_legal_date_entry-into-force";

    [TestMethod]
    public async Task TheReifiedShapeBehindAnE1DateIsReadFromThePublisherRatherThanAssumed()
    {
        if (Environment.GetEnvironmentVariable("LEX_EU_AXIOM_PROBE") != "1")
        {
            Assert.Inconclusive(
                "Set LEX_EU_AXIOM_PROBE=1 for the live bounded EU date-axiom shape probe.");
        }

        var profile = OfficialMachineQuerySourceProfiles.Resolve(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql);
        var pacer = new EuPredicateExistenceCensus.OriginPacer(profile.MinimumRequestInterval, TimeProvider.System, Task.Delay);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("user-agent", profile.CrawlerUserAgent);

        var started = Stopwatch.StartNew();
        var policy = await EuPredicateExistenceCensus.ReadDeclaredRobotsPolicyAsync(http, profile, pacer);
        var endpoint = new Uri(profile.RequestTarget, UriKind.Absolute);
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Allowed,
            RobotsExclusionPolicy.Evaluate(policy, profile.RobotsProductToken, endpoint.PathAndQuery),
            "the publisher's own policy governs this probe exactly as it governs a run; a refusal "
                + "here is an answer, not a reason to proceed anyway.");

        var reified = await AskAsync(
            http,
            profile,
            pacer,
            $"ASK {{ ?axiom a <{OwlAxiom}> ; <{OwlAnnotatedProperty}> <{EntryIntoForce}> }}");
        Console.WriteLine($"AXIOM|reified|{EntryIntoForce}|{reified}");

        var properties = reified
            ? await SelectColumnAsync(
                http,
                profile,
                pacer,
                $"SELECT DISTINCT ?p WHERE {{ ?axiom a <{OwlAxiom}> ; "
                    + $"<{OwlAnnotatedProperty}> <{EntryIntoForce}> ; ?p ?value }} LIMIT 40",
                "p")
            : [];
        foreach (var property in properties)
        {
            Console.WriteLine($"AXIOM|property|{property}");
        }

        Console.WriteLine($"AXIOM|summary|reified={reified}|distinctProperties={properties.Count}");

        // WRITTEN, NOT PRINTED. A passing test's console output is not surfaced by the platform,
        // which is how a population run's own summary went unread this morning. The finding is the
        // artifact; the console line is a convenience for a failing run.
        var root = Environment.GetEnvironmentVariable("LEX_EU_AXIOM_PROBE_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            Directory.CreateDirectory(root);
            var report = new JsonObject
            {
                ["schema"] = "lex-v3-eu-date-axiom-shape-probe/1",
                ["endpoint"] = profile.RequestTarget,
                ["annotatedProperty"] = EntryIntoForce,
                ["reified"] = reified,
                ["distinctProperties"] = properties.Count,
                ["properties"] = new JsonArray([.. properties.Select(static value => JsonValue.Create(value))]),
            };
            await File.WriteAllTextAsync(
                Path.Combine(root, "axiom-shape.json"),
                report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        started.Stop();

        // Said from the wall clock, because the pacer's own guard proves it asks for the right
        // waits and only this proves the run actually took them. The robots read shares the origin
        // with both queries, so the floor covers that boundary too.
        var requests = reified ? 3 : 2;
        var floor = profile.MinimumRequestInterval * (requests - 1);
        Assert.IsTrue(
            started.Elapsed >= floor,
            $"the probe sent {requests} requests in {started.Elapsed}, which is under the {floor} "
                + "this publisher's declared pacing requires for the shared origin.");
    }

    private static async Task<bool> AskAsync(
        HttpClient http, OfficialMachineQuerySourceProfile profile, EuPredicateExistenceCensus.OriginPacer pacer, string query)
    {
        using var document = await ExecuteAsync(http, profile, pacer, query);
        return document.RootElement.GetProperty("boolean").GetBoolean();
    }

    private static async Task<IReadOnlyList<string>> SelectColumnAsync(
        HttpClient http,
        OfficialMachineQuerySourceProfile profile,
        EuPredicateExistenceCensus.OriginPacer pacer,
        string query,
        string column)
    {
        using var document = await ExecuteAsync(http, profile, pacer, query);
        var values = new List<string>();
        foreach (var row in document.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            if (row.TryGetProperty(column, out var cell) && cell.TryGetProperty("value", out var value))
            {
                values.Add(value.GetString() ?? string.Empty);
            }
        }

        values.Sort(StringComparer.Ordinal);
        return values;
    }

    private static async Task<JsonDocument> ExecuteAsync(
        HttpClient http, OfficialMachineQuerySourceProfile profile, EuPredicateExistenceCensus.OriginPacer pacer, string query)
    {
        using var content = new StringContent(query, Encoding.UTF8, "application/sparql-query");
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
            "the probe could not ask the publisher; an unanswered question is not a shape, and must "
                + "never be recorded as one.");

        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
    }
}
