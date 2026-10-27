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
