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
/// Opt-in behind <c>LEX_EU_AXIOM_PROBE=1</c>, paced through the same
/// <see cref="EuPredicateExistenceCensus.OriginPacer"/> and reading the publisher's policy through
/// <see cref="EuPredicateExistenceCensus.ReadDeclaredRobotsPolicyAsync"/> rather than a second copy
/// of it.
/// </para>
/// <para>
/// WHY A PROPERTY INVENTORY WAS NOT AN ANSWER, and this is the correction that produced the row
/// reading below. The first version of this probe ran <c>SELECT DISTINCT ?p</c>, bound
/// <c>?value</c> and threw it away. That establishes which predicate IRIs appear SOMEWHERE across
/// the matching axiom nodes and nothing else: not that any single axiom carries the required
/// fields together, not that <c>owl:annotatedTarget</c> is a literal, not its datatype or lexical
/// value, not what term <c>type_of_date</c> returns, not how a qualifier LABEL is reachable from
/// it, and not whether the quality annotations belong to the same date assertion. A mapping table
/// was then written from that inventory, which is inference from an aggregate presented as
/// measurement -- the precise failure this instrument exists to prevent, committed in the act of
/// introducing it. The row reading below is the repair: it keeps each returned term's KIND,
/// datatype and lexical value, and the artifact separates what is absent from a given axiom from
/// what was merely seen elsewhere in the aggregate.
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

    /// <summary>
    /// The fd_335 carrier. Whether this returns an IRI into a concept scheme or a bare literal is
    /// exactly what decides how <c>rawQualifierCode</c> and the separately required
    /// <c>qualifierLabel</c> are obtained, so the probe reads the term rather than assuming either.
    /// </summary>
    private const string AnnotationTypeOfDate =
        "http://publications.europa.eu/ontology/annotation#type_of_date";

    /// <summary>How many axiom nodes are read whole. Bounded because this is a shape question.</summary>
    private const int SampledAxiomCeiling = 3;

    /// <summary>
    /// The publisher queries one run sends: the ASK, the aggregate property SELECT and the row
    /// SELECT.
    /// </summary>
    /// <remarks>
    /// Three, not four, because the fd_335 carrier is a literal at this endpoint and there is no
    /// node to follow for a label. A URI carrier would add the fourth, which is why the live probe
    /// counts what it actually sent rather than asserting this number.
    /// </remarks>
    internal const int PublisherQueriesPerRun = 3;

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
        using var inner = new HttpClientHandler { AllowAutoRedirect = false };

        // Every request this probe sends is tallied here, per origin, by the transport itself. See
        // CountingHandler's remarks: counting beside the call sites is what produced the wrong
        // number, because the robots route sends one GET per step and the call site saw one helper.
        using var handler = new CountingHandler(inner);
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

        // ---- The aggregate: which predicate IRIs appear ANYWHERE across the matching axioms. ----
        var properties = reified
            ? await SelectColumnAsync(
                http,
                profile,
                pacer,
                $"SELECT DISTINCT ?p WHERE {{ ?axiom a <{OwlAxiom}> ; "
                    + $"<{OwlAnnotatedProperty}> <{EntryIntoForce}> ; ?p ?value }} LIMIT 40",
                "p")
            : [];

        // ---- The rows: a bounded set of axioms read WHOLE, each term with its kind and datatype.
        //
        // This is what the aggregate above cannot supply, and the reason this probe was returned. A
        // predicate list says a property was seen somewhere. Only a row says which properties sit on
        // ONE assertion together, whether the annotated target is a literal rather than an IRI, what
        // datatype it carries, what lexical value it actually has, and what term the fd_335 carrier
        // returns.
        var rows = reified
            ? await SelectRowsAsync(
                http,
                profile,
                pacer,
                $"SELECT ?axiom ?p ?o WHERE {{ {{ SELECT DISTINCT ?axiom WHERE {{ "
                    + $"?axiom a <{OwlAxiom}> ; <{OwlAnnotatedProperty}> <{EntryIntoForce}> }} "
                    + $"LIMIT {SampledAxiomCeiling} }} ?axiom ?p ?o }}")
            : [];

        var byAxiom = new SortedDictionary<string, SortedDictionary<string, Term>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!row.TryGetValue("axiom", out var axiom)
                || !row.TryGetValue("p", out var predicate)
                || !row.TryGetValue("o", out var term))
            {
                continue;
            }

            if (!byAxiom.TryGetValue(axiom.Value, out var carried))
            {
                carried = new SortedDictionary<string, Term>(StringComparer.Ordinal);
                byAxiom[axiom.Value] = carried;
            }

            carried[predicate.Value] = term;
        }

        // ---- The qualifier-label resolution path, followed rather than assumed. ----
        //
        // Create requires rawQualifierCode AND a separately supplied qualifierLabel. If the fd_335
        // carrier returns an IRI then the label is not in the axiom at all and has to be reached
        // from that node; if it returns a plain literal there is no second hop and the label must
        // come from somewhere this probe has not looked. Which of those holds decides the shape of
        // the acquisition query, so it is read rather than guessed.
        var qualifierTerm = byAxiom.Values
            .Select(static carried => carried.TryGetValue(AnnotationTypeOfDate, out var term) ? term : null)
            .FirstOrDefault(static term => term is not null);
        IReadOnlyList<IReadOnlyDictionary<string, Term>> qualifierNode = [];
        if (qualifierTerm is { Kind: "uri" })
        {
            qualifierNode = await SelectRowsAsync(
                http,
                profile,
                pacer,
                $"SELECT ?p ?o WHERE {{ <{qualifierTerm.Value}> ?p ?o }} LIMIT 40");
        }

        Console.WriteLine(
            $"AXIOM|summary|reified={reified}|distinctProperties={properties.Count}"
                + $"|sampledAxioms={byAxiom.Count}|qualifierKind={qualifierTerm?.Kind ?? "absent"}");

        // WRITTEN, NOT PRINTED. A passing test's console output is not surfaced by the platform,
        // which is how a population run's own summary went unread this morning. The finding is the
        // artifact; the console line is a convenience for a failing run.
        var root = Environment.GetEnvironmentVariable("LEX_EU_AXIOM_PROBE_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            Directory.CreateDirectory(root);
            var report = new JsonObject
            {
                ["schema"] = "lex-v3-eu-date-axiom-shape-probe/2",
                ["endpoint"] = profile.RequestTarget,
                ["annotatedProperty"] = EntryIntoForce,
                ["reified"] = reified,
                ["aggregate"] = new JsonObject
                {
                    ["distinctProperties"] = properties.Count,
                    ["properties"] = new JsonArray(
                        [.. properties.Select(static value => JsonValue.Create(value))]),
                },
                ["sampledAxiomCeiling"] = SampledAxiomCeiling,
                ["sampledAxiomCount"] = byAxiom.Count,
                ["rows"] = new JsonArray([.. byAxiom.Select(entry => (JsonNode)new JsonObject
                {
                    ["axiom"] = entry.Key,
                    ["carries"] = new JsonArray(
                        [.. entry.Value.Select(static carried => (JsonNode)Describe(carried.Key, carried.Value))]),

                    // ABSENT FROM THIS AXIOM, which is not the same statement as absent from the
                    // endpoint. A property in the aggregate but missing here was observed on some
                    // OTHER node, and collapsing those two readings is how an inventory gets
                    // mistaken for a row.
                    ["absentFromThisAxiom"] = new JsonArray(
                        [.. properties
                            .Where(property => !entry.Value.ContainsKey(property))
                            .Select(static property => JsonValue.Create(property))]),
                })]),
                ["qualifierLabelResolution"] = qualifierTerm is null
                    ? null
                    : new JsonObject
                    {
                        ["carrier"] = AnnotationTypeOfDate,
                        ["term"] = Describe(AnnotationTypeOfDate, qualifierTerm),
                        ["followedToItsOwnNode"] = qualifierTerm.Kind == "uri",
                        ["carrierNodeCarries"] = new JsonArray([.. qualifierNode
                            .Where(static row => row.ContainsKey("p") && row.ContainsKey("o"))
                            .Select(static row => (JsonNode)Describe(row["p"].Value, row["o"]))]),
                    },
                // COUNTED AT THE TRANSPORT, and reported beside the waits rather than as one
                // number, because they are not the same quantity: the robots route's two steps sit
                // on two different origins, so the second takes no wait.
                ["requestCount"] = handler.Total,
                ["sameOriginWaits"] = SameOriginWaits(handler.ByOrigin),
                ["requestsByOrigin"] = new JsonObject(
                    [.. handler.ByOrigin
                        .OrderBy(static origin => origin.Key, StringComparer.Ordinal)
                        .Select(static origin =>
                            new KeyValuePair<string, JsonNode?>(origin.Key, JsonValue.Create(origin.Value)))]),
            };
            await File.WriteAllTextAsync(
                Path.Combine(root, "axiom-shape.json"),
                report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        started.Stop();

        // Said from the wall clock, because the pacer's own guard proves it asks for the right
        // waits and only this proves the run actually took them.
        //
        // THE FLOOR IS THE WAITS, NOT THE REQUESTS. Deriving it as requestCount - 1 asserted a wait
        // between the robots redirect and its target that the pacer never takes, and only came out
        // right because the miscount and the cross-origin hop cancelled. Both quantities are now
        // computed from what the transport saw, and they are allowed to differ.
        var sameOriginWaits = SameOriginWaits(handler.ByOrigin);
        var floor = profile.MinimumRequestInterval * sameOriginWaits;
        Assert.IsTrue(
            started.Elapsed >= floor,
            $"the probe sent {handler.Total} requests across {handler.ByOrigin.Count} origins in "
                + $"{started.Elapsed}, which is under the {floor} this publisher's declared pacing "
                + $"requires for the {sameOriginWaits} same-origin waits among them.");
    }

    /// <summary>
    /// The pinned robots route plus this probe's own queries are five requests and three
    /// same-origin waits, derived offline from the profile rather than from a live run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS IS NOT LEFT TO THE LIVE PROBE. The live probe reported four requests for a run that
    /// sent five, and it could not catch that itself: it is opt-in, so an ordinary suite never runs
    /// it and the wrong number shipped green through a review. A counting defect needs a guard that
    /// runs when nobody asked for the network.
    /// </para>
    /// <para>
    /// The quantities are derived from <c>profile.RobotsRoute.Steps</c> and
    /// <see cref="PublisherQueriesPerRun"/> rather than written down, so a route that gains a step
    /// or an endpoint that moves host fails here instead of silently changing what the pacing floor
    /// means.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ThePinnedRouteAndQueriesAreFiveRequestsAndThreeSameOriginWaits()
    {
        var profile = OfficialMachineQuerySourceProfiles.Resolve(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql);

        var tally = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in profile.RobotsRoute.Steps)
        {
            var origin = OriginKeyOf(new Uri(step.RequestedUri, UriKind.Absolute));
            tally[origin] = tally.GetValueOrDefault(origin) + 1;
        }

        var endpointOrigin = OriginKeyOf(new Uri(profile.RequestTarget, UriKind.Absolute));
        tally[endpointOrigin] = tally.GetValueOrDefault(endpointOrigin) + PublisherQueriesPerRun;

        Assert.AreEqual(
            2,
            profile.RobotsRoute.Steps.Count,
            "the EU robots route is two steps -- publications.europa.eu 301 to op.europa.eu 200 -- "
                + "and the reader issues one GET per step. Treating that read as a single request "
                + "is the defect this pins.");
        Assert.AreEqual(
            5,
            tally.Values.Sum(),
            "two robots GETs plus the ASK, the aggregate property SELECT and the row SELECT.");
        Assert.AreEqual(
            3,
            SameOriginWaits(tally),
            "the SPARQL endpoint shares publications.europa.eu with the first robots step, so that "
                + "origin carries four requests and three waits, while op.europa.eu carries one "
                + "request and waits for nothing. Five requests are not five waits.");
    }

    /// <summary>One RDF term exactly as the endpoint returned it, kind included.</summary>
    /// <remarks>
    /// The kind is the whole point. <c>owl:annotatedTarget</c> being a literal carrying a datatype
    /// rather than an IRI is what makes <c>rawLexicalValue</c> and <c>datatypeUri</c> obtainable at
    /// all, and a reading that flattens every term to its string value cannot tell those apart.
    /// </remarks>
    private sealed record Term(string Kind, string Value, string? Datatype, string? Language);

    /// <summary>
    /// Counts every HTTP request this probe actually sends, per origin, at the transport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY AT THE TRANSPORT RATHER THAN AT THE CALL SITES. The previous version incremented a
    /// counter beside each helper call, and it was wrong: the robots route has TWO steps and
    /// <see cref="EuPredicateExistenceCensus.ReadDeclaredRobotsPolicyAsync"/> issues one GET per
    /// step, so a run that sent five requests reported four. The floor happened to stay correct
    /// because the five span two origins and three of the waits are same-origin, which is exactly
    /// the kind of coincidence that keeps a false number alive. A handler cannot drift from the
    /// code paths, because it only sees what actually went out.
    /// </para>
    /// <para>
    /// Per origin rather than in total, because <see cref="EuPredicateExistenceCensus.OriginPacer"/>
    /// paces per origin: a request to a different host waits for nothing. Deriving the floor from a
    /// bare count would assert a wait between the robots redirect and its target that the pacer
    /// never takes.
    /// </para>
    /// </remarks>
    private sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly Dictionary<string, int> byOrigin = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, int> ByOrigin => byOrigin;

        public int Total => byOrigin.Values.Sum();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri)
            {
                var origin = OriginKeyOf(uri);
                byOrigin[origin] = byOrigin.GetValueOrDefault(origin) + 1;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>The pacer's own origin key: scheme, host and port.</summary>
    internal static string OriginKeyOf(Uri uri) => $"{uri.Scheme}://{uri.Host}:{uri.Port}";

    /// <summary>
    /// How many pacing waits a tally of requests implies: one before every request after the first
    /// <b>to the same origin</b>, and none at all across origins.
    /// </summary>
    internal static int SameOriginWaits(IReadOnlyDictionary<string, int> requestsByOrigin) =>
        requestsByOrigin.Values.Sum(static count => Math.Max(0, count - 1));

    private static JsonObject Describe(string predicate, Term term) => new()
    {
        ["predicate"] = predicate,
        ["kind"] = term.Kind,
        ["value"] = term.Value,
        ["datatype"] = term.Datatype,
        ["language"] = term.Language,
    };

    private static async Task<bool> AskAsync(
        HttpClient http,
        OfficialMachineQuerySourceProfile profile,
        EuPredicateExistenceCensus.OriginPacer pacer,
        string query)
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

    /// <summary>
    /// Every binding of every row, each term keeping the kind, datatype and language the endpoint
    /// gave it.
    /// </summary>
    /// <remarks>
    /// The SPARQL JSON results form already carries <c>type</c>, <c>datatype</c> and <c>xml:lang</c>
    /// beside each value. The first version of this probe discarded all three by reading only
    /// <c>value</c>, which is why it could report that a property existed but not what it returned.
    /// Nothing here asks the publisher for anything extra; it stops throwing away what was already
    /// in the answer.
    /// </remarks>
    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, Term>>> SelectRowsAsync(
        HttpClient http,
        OfficialMachineQuerySourceProfile profile,
        EuPredicateExistenceCensus.OriginPacer pacer,
        string query)
    {
        using var document = await ExecuteAsync(http, profile, pacer, query);
        var rows = new List<IReadOnlyDictionary<string, Term>>();
        foreach (var row in document.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            var bindings = new Dictionary<string, Term>(StringComparer.Ordinal);
            foreach (var cell in row.EnumerateObject())
            {
                bindings[cell.Name] = new Term(
                    cell.Value.TryGetProperty("type", out var kind) ? kind.GetString() ?? "" : "",
                    cell.Value.TryGetProperty("value", out var value) ? value.GetString() ?? "" : "",
                    cell.Value.TryGetProperty("datatype", out var datatype) ? datatype.GetString() : null,
                    cell.Value.TryGetProperty("xml:lang", out var language) ? language.GetString() : null);
            }

            rows.Add(bindings);
        }

        return rows;
    }

    private static async Task<JsonDocument> ExecuteAsync(
        HttpClient http,
        OfficialMachineQuerySourceProfile profile,
        EuPredicateExistenceCensus.OriginPacer pacer,
        string query)
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
