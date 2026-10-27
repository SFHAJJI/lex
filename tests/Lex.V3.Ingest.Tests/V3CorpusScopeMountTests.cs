using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The two refusals every operation can give before it reads the mount, each inside a normal envelope and the same over REST
/// and MCP: an operation served from no data answers <c>retrieval_mode_unavailable</c> (the plan's item 8.6; the specification of
/// record, §3: "All 27 registered operations are served, or refuse with the typed reason their capability manifest states"),
/// and an instrument the out-of-scope list names answers <c>out_of_corpus_scope</c> with its official place and who to ask (the
/// plan's item 6.10; J5, "Circulaire CSSF 20/747").
/// </summary>
[TestClass]
public sealed class V3CorpusScopeMountTests
{
    [TestMethod]
    public async Task EveryOperationServedFromNoDataRefusesItsTypedReasonOverRestAndMcpAlike()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var work = $"/lu-legilux/{fixture.WorkKey}";

        CollectionAssert.AreEqual(
            V3UnservedOperations.Rows.Select(static row => row.Operation).ToArray(),
            V3RestRouteBinding.Refusing.Select(static binding => binding.OperationId).ToArray());
        foreach (var row in V3UnservedOperations.Rows)
        {
            Assert.AreEqual(V3UnservedOperation.RetrievalModeUnavailable, row.Reason, row.Operation);
            foreach (var (parameters, publisher) in new (object, PublisherId)[]
                     {
                         (new { }, PublisherId.LuLegilux),
                         (new { identifier = work }, PublisherId.LuLegilux),
                         (new { identifier = work, date = fixture.ApplicabilityDate }, PublisherId.LuLegilux),
                         (new { identifier = "32016R0679" }, PublisherId.EuEurLex),
                     })
            {
                var (rest, structured) = await BothAsync(mount, row.Operation, parameters);
                var what = $"{row.Operation} {JsonSerializer.Serialize(parameters)}";
                Assert.AreEqual(V3Verdicts.Refuse, rest.GetProperty("verdict").GetString(), what);
                var refusal = rest.GetProperty("refusal");
                Assert.AreEqual("retrieval_mode_unavailable", refusal.GetProperty("code").GetString(), what);
                var payload = refusal.GetProperty("helpful_payload");
                Assert.AreEqual(V3UnservedOperations.RequestedMode(row.Operation), payload.GetProperty("requested_mode").GetString(), what);
                CollectionAssert.AreEqual(
                    V3UnservedOperations.AvailableModes(row.Operation).ToArray(),
                    payload.GetProperty("available_modes").EnumerateArray().Select(static mode => mode.GetString()).ToArray(),
                    what);
                Assert.AreEqual(row.DataNeeded, payload.GetProperty("data_needed").GetString(), what);
                Assert.AreEqual(publisher == PublisherId.LuLegilux ? "lu-legilux" : "eu-eurlex", rest.GetProperty("context").GetProperty("publisher").GetString(), what);
                Assert.AreEqual(JsonSerializer.Serialize(rest), JsonSerializer.Serialize(structured), $"{what}: REST and MCP answered different envelopes");
            }

            // Each available mode is one this server serves: the tag of a served operation.
            foreach (var mode in V3UnservedOperations.AvailableModes(row.Operation))
            {
                CollectionAssert.Contains(
                    V3RestRouteBinding.Served.Select(static binding => binding.OperationId).ToArray(),
                    mode[(mode.IndexOf('_') + 1)..],
                    $"{row.Operation}: {mode} names a served operation");
            }
        }

        // Every registered name is a route; none is a transport failure of its own (operation_not_served is retired).
        foreach (var operation in V3OperationRegistry.Reviewed.Operations)
        {
            Assert.IsTrue(V3RestRouteBinding.Routed.Any(binding => binding.OperationId == operation.OperationId), operation.OperationId);
        }
    }

    [TestMethod]
    public async Task OnAMountWithOnlyTheEuIndexAnOperationServedFromNoDataIsTheEusWithNoIdentifier()
    {
        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = europe;
        using var mount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var (rest, structured) = await BothAsync(mount, "concepts", new { });
        Assert.AreEqual("retrieval_mode_unavailable", rest.GetProperty("refusal").GetProperty("code").GetString());
        Assert.AreEqual("eu-eurlex", rest.GetProperty("context").GetProperty("publisher").GetString());
        Assert.AreEqual(JsonSerializer.Serialize(rest), JsonSerializer.Serialize(structured));
    }

    [TestMethod]
    public async Task AnInstrumentTheOutOfScopeListNamesIsRefusedWithItsOfficialPlaceAndWhoToAskByEveryOperationThatTakesAnIdentifier()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // J5 (05-user-journeys.md): a law the corpus does not hold, asked for by its name, which the list names.
        const string circular = "Circulaire CSSF 20/747";
        var cssf = V3OutOfScope.Classes.Single(static entry => entry.Class == "cssf_circular");
        var asked = 0;
        foreach (var binding in V3RestRouteBinding.Routed)
        {
            if (ParametersWith(binding.OperationId, circular, fixture.ApplicabilityDate) is not { } parameters)
            {
                continue;
            }

            asked++;
            var (rest, structured) = await BothAsync(mount, binding.OperationId, parameters);
            var what = binding.OperationId;
            Assert.AreEqual(V3Verdicts.Refuse, rest.GetProperty("verdict").GetString(), what);
            var refusal = rest.GetProperty("refusal");
            Assert.AreEqual("out_of_corpus_scope", refusal.GetProperty("code").GetString(), what);
            var payload = refusal.GetProperty("helpful_payload");
            Assert.AreEqual(circular, payload.GetProperty("requested_identifier").GetString(), what);
            Assert.AreEqual("cssf_circular", payload.GetProperty("out_of_scope_class").GetString(), what);
            Assert.AreEqual(cssf.Instrument, payload.GetProperty("instrument").GetString(), what);
            Assert.AreEqual(cssf.Reason, payload.GetProperty("reason").GetString(), what);
            Assert.AreEqual("https://www.cssf.lu/", payload.GetProperty("official_source").GetString(), what);
            var who = payload.GetProperty("who_to_ask");
            Assert.AreEqual("Commission de surveillance du secteur financier (CSSF)", who.GetProperty("name").GetString(), what);
            Assert.AreEqual("https://www.cssf.lu/", who.GetProperty("link").GetString(), what);
            Assert.AreEqual("information", who.GetProperty("role").GetString(), what);
            Assert.IsFalse(payload.GetProperty("asserts_absence_of_law").GetBoolean(), what);
            CollectionAssert.AreEqual(new[] { "expanded_official_scope" }, payload.GetProperty("what_would_answer").EnumerateArray().Select(static route => route.GetString()).ToArray(), what);
            Assert.AreEqual(V3OutOfScope.Schema, payload.GetProperty("scope_list").GetString(), what);
            Assert.AreEqual(V3OutOfScope.Sha256, payload.GetProperty("scope_list_sha256").GetString(), what);
            Assert.AreEqual("lu-legilux", rest.GetProperty("context").GetProperty("publisher").GetString(), what);
            Assert.AreEqual(JsonSerializer.Serialize(rest), JsonSerializer.Serialize(structured), $"{what}: REST and MCP answered different envelopes");
        }

        Assert.AreEqual(23, asked, "every routed operation that takes an identifier: the 27 but ask, browse, coverage and events");

        // An EU court's decision is the EU's, and a held work is never out of scope.
        var (ecli, _) = await BothAsync(mount, "dossier", new { identifier = "ECLI:EU:C:2014:317" });
        Assert.AreEqual("out_of_corpus_scope", ecli.GetProperty("refusal").GetProperty("code").GetString());
        Assert.AreEqual("eu_case_law", ecli.GetProperty("refusal").GetProperty("helpful_payload").GetProperty("out_of_scope_class").GetString());
        Assert.AreEqual("eu-eurlex", ecli.GetProperty("context").GetProperty("publisher").GetString());
        var (held, _) = await BothAsync(mount, "dossier", new { identifier = $"/lu-legilux/{fixture.WorkKey}" });
        Assert.AreEqual(V3Verdicts.Answer, held.GetProperty("verdict").GetString());
    }

    /// <summary>
    /// R80: an EU act the mounted EU population does not hold answers <c>out_of_corpus_scope</c>, naming the rule (with the count of
    /// seed acts the index holds) and the act's page on EUR-Lex, over REST and MCP alike, in every spelling of its CELEX; a version or
    /// corrigendum of a held act, a document of a sector that holds no acts and a case of the EU courts are not refused by the rule.
    /// </summary>
    [TestMethod]
    public async Task AnEuActOutsideTheEuPopulationIsRefusedOutOfScopeNamingTheRuleAndEurLex()
    {
        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = europe;
        using var mount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var identifier in new[]
                 {
                     "32019R1150", "CELEX:32019R1150", "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32019R1150",
                     "http://publications.europa.eu/resource/celex/32019R1150", "02019R1150-20200712", "32019R1150R(01)",
                 })
        {
            foreach (var operation in new[] { "dossier", "resolve", "timeline", "as_of" })
            {
                var parameters = operation == "as_of"
                    ? (object)new { identifier, date = "2024-01-01" }
                    : new { identifier };
                var (rest, structured) = await BothAsync(mount, operation, parameters);
                var what = $"{operation} {identifier}";
                Assert.AreEqual(V3Verdicts.Refuse, rest.GetProperty("verdict").GetString(), what);
                var refusal = rest.GetProperty("refusal");
                Assert.AreEqual("out_of_corpus_scope", refusal.GetProperty("code").GetString(), what);
                var payload = refusal.GetProperty("helpful_payload");
                Assert.AreEqual(identifier, payload.GetProperty("requested_identifier").GetString(), what);
                Assert.AreEqual("eu_act_outside_population", payload.GetProperty("out_of_scope_class").GetString(), what);
                Assert.AreEqual(1, payload.GetProperty("population_seed_acts").GetInt32(), what);
                Assert.AreEqual(
                    "The EU population Lex's rule selects is the 1 act of its seed list (Decision 84), and 32019R1150 is not one of them.",
                    payload.GetProperty("reason").GetString(), what);
                Assert.AreEqual("https://eur-lex.europa.eu/legal-content/AUTO/?uri=CELEX:32019R1150", payload.GetProperty("official_source").GetString(), what);
                var who = payload.GetProperty("who_to_ask");
                Assert.AreEqual("Publications Office of the European Union (EUR-Lex)", who.GetProperty("name").GetString(), what);
                Assert.AreEqual("https://eur-lex.europa.eu/", who.GetProperty("link").GetString(), what);
                Assert.AreEqual("information", who.GetProperty("role").GetString(), what);
                Assert.IsFalse(payload.GetProperty("asserts_absence_of_law").GetBoolean(), what);
                Assert.IsFalse(payload.TryGetProperty("scope_list", out _), $"{what}: the rule is no class of the list");
                Assert.AreEqual("eu-eurlex", rest.GetProperty("context").GetProperty("publisher").GetString(), what);
                Assert.AreEqual(JsonSerializer.Serialize(rest), JsonSerializer.Serialize(structured), $"{what}: REST and MCP answered different envelopes");
            }
        }

        // The held act, a consolidated version of it that is not held, and a corrigendum of it: never outside the population.
        foreach (var held in new[] { "32016R0679", "02016R0679-20990101", "32016R0679R(09)" })
        {
            var (rest, _) = await BothAsync(mount, "dossier", new { identifier = held });
            var code = rest.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.Object
                ? refusal.GetProperty("code").GetString()
                : null;
            Assert.AreNotEqual("out_of_corpus_scope", code, held);
        }

        // A document of a sector that holds no acts (a Commission proposal) stays unknown; a case is the list's, not the rule's.
        var (proposal, _) = await BothAsync(mount, "dossier", new { identifier = "52020PC0825" });
        Assert.AreEqual("identifier_unknown", proposal.GetProperty("refusal").GetProperty("code").GetString());
        // A treaty (sector 1), its CELEX alone, after "CELEX:" or with an article number, is never read as an act outside the
        // population: the index holds a treaty by its "/TXT" part, so a held treaty would be refused (the review of #1004, F1).
        foreach (var treaty in new[] { "12016E", "CELEX:12016P", "12016E101", "12016P047", "12012E" })
        {
            foreach (var operation in new[] { "dossier", "resolve", "timeline" })
            {
                var (rest, _) = await BothAsync(mount, operation, new { identifier = treaty });
                Assert.AreEqual("identifier_unknown", rest.GetProperty("refusal").GetProperty("code").GetString(), $"{operation} {treaty}");
            }

            Assert.IsNull(V3CorpusMount.EuropeActCelexOf(treaty), treaty);
        }
        var (judgment, _) = await BothAsync(mount, "dossier", new { identifier = "62012CJ0131" });
        Assert.AreEqual("eu_case_law", judgment.GetProperty("refusal").GetProperty("helpful_payload").GetProperty("out_of_scope_class").GetString());

        // Only an identifier is read: words that cite an act name no CELEX.
        Assert.IsNull(V3CorpusMount.EuropeActCelexOf("Regulation (EU) 2019/1150"));
        Assert.AreEqual(("32019R1150", "32019R1150"), V3CorpusMount.EuropeActCelexOf("CELEX:32019R1150"));
        Assert.AreEqual(("02019R1150-20200712", "32019R1150"), V3CorpusMount.EuropeActCelexOf("02019R1150-20200712"));
    }

    /// <summary>
    /// The parameters of a request for <paramref name="operation"/> naming <paramref name="identifier"/>, with every other parameter
    /// its request schema requires, or null when the operation takes no identifier.
    /// </summary>
    private static Dictionary<string, object>? ParametersWith(string operation, string identifier, string date)
    {
        using var schema = JsonDocument.Parse(V3PlatformSchemaExporter.ExportRequestUtf8(operation));
        var parameters = schema.RootElement.GetProperty("properties").GetProperty("parameters");
        var open = parameters.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.True;
        var declared = parameters.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(static property => property.Name).ToArray()
            : [];
        if (!open && !declared.Contains("identifier", StringComparer.Ordinal))
        {
            return null;
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal) { ["identifier"] = identifier };
        var required = parameters.TryGetProperty("required", out var names) ? names.EnumerateArray().Select(static name => name.GetString()!) : [];
        foreach (var name in required.Where(static name => name != "identifier"))
        {
            values[name] = name switch
            {
                "date" or "date_from" or "date_to" => date,
                "anchor" => "art_1er",
                "query" => "loyer",
                "language" => "fra",
                _ => throw new InvalidOperationException($"{operation} requires {name}, which this test does not fill"),
            };
        }

        return values;
    }

    /// <summary>The REST envelope and the MCP tool call's structured content for one request, each from the real handler.</summary>
    private static async Task<(JsonElement RestEnvelope, JsonElement McpEnvelope)> BothAsync(V3CorpusMount mount, string operation, object parameters)
    {
        const string trace = "scope-mount";
        var rest = await SendAsync(mount, "/api/v3/" + operation, JsonSerializer.Serialize(new { operation_id = operation, parameters }), trace);
        Assert.AreEqual(StatusCodes.Status200OK, rest.Response.StatusCode, operation);
        var restBytes = ((MemoryStream)rest.Response.Body).ToArray();
        _ = V3EnvelopeJson.ParseAndVerify(restBytes, V3OperationRegistry.Reviewed);
        using var restDocument = JsonDocument.Parse(restBytes);

        var mcp = await SendAsync(mount, V3ApiHandler.McpRawTarget, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = operation, arguments = parameters },
        }), trace);
        Assert.AreEqual(StatusCodes.Status200OK, mcp.Response.StatusCode, operation);
        using var rpc = JsonDocument.Parse(((MemoryStream)mcp.Response.Body).ToArray());
        Assert.IsFalse(rpc.RootElement.TryGetProperty("error", out var error), $"{operation}: {error}");
        var result = rpc.RootElement.GetProperty("result");
        Assert.IsFalse(result.GetProperty("isError").GetBoolean(), $"{operation}: a refusal is an answer, never a tool error");
        return (restDocument.RootElement.Clone(), result.GetProperty("structuredContent").Clone());
    }

    private static async Task<DefaultHttpContext> SendAsync(V3CorpusMount mount, string rawTarget, string body, string trace)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext { TraceIdentifier = trace };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }
}
