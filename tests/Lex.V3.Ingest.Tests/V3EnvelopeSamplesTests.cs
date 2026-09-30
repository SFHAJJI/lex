using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The envelope census: what a REST client really receives, whole, beside the contract a reader needs to
/// verify it without holding its own copy of the registry. The web's envelope reader is written against
/// this file, as the coverage reader is written against the answer census.
/// </summary>
/// <remarks>
/// <para>
/// Each envelope is driven through the real handler and passes <see cref="V3EnvelopeJson.ParseAndVerify"/>
/// before it is recorded. The fixture mints the corpus and index digests per run, so they are replaced by
/// fixed digests of the same shape (every occurrence, text-wise), which keeps every envelope readable by a
/// strict reader: a placeholder that is not a digest would be one a reader has to be taught to accept.
/// </para>
/// <para>
/// The contract block is read from the reviewed registry, not written here: the envelope schema and
/// version, the registry schema and digest, the refusal schema, the verdicts, every operation's result
/// schema and object types, and every refusal code with its mandatory payload fields.
/// </para>
/// </remarks>
[TestClass]
public sealed class V3EnvelopeSamplesTests
{
    private const string RenderVariable = "V3_RENDER_ENVELOPE_SAMPLES";
    private const string SamplesSchema = "lex-v3-envelope-samples/1";
    private static readonly string FixedCorpusDigest = new('a', 64);
    private static readonly string FixedIndexDigest = new('b', 64);

    [TestMethod]
    public async Task TheEnvelopeSamplesAreWhatTheHandlerSendsBesideTheReviewedContract()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelopes = new JsonArray
        {
            await CaptureAsync(fixture, mount, "coverage", "the whole mount: an answer", new { }),
            await CaptureAsync(fixture, mount, "ask", "any question: the contained assistant's card under the point verdict", new { question = "Can I be fired while on sick leave?" }),
            await CaptureAsync(fixture, null, "coverage", "no corpus mounted: a refusal", new { }),
            await CaptureAsync(fixture, mount, "search", "a language the mount holds no text in: a refusal with a payload", new { query = "loyer", language = "deu" }),
            await CaptureAsync(fixture, mount, "search", "a phrase the held text carries: an answer with hits in both lanes", new { query = "assemblée générale", language = "fra" }),
            await CaptureAsync(fixture, null, "search", "no corpus mounted: a refusal", new { query = "assemblée générale", language = "fra" }),
        };

        var document = new JsonObject
        {
            ["schema"] = SamplesSchema,
            ["note"] = "Whole REST envelopes the real handler sent, each verified by V3EnvelopeJson.ParseAndVerify before it was recorded, " +
                "and the reviewed registry's contract a reader needs to verify one. The fixture's per-run corpus and index digests are replaced " +
                $"by {FixedCorpusDigest[..8]}... and {FixedIndexDigest[..8]}... (same shape, every occurrence); nothing else is edited. " +
                "THE PRODUCER IS REAL AND THE MOUNT IS A FIXTURE.",
            ["contract"] = Contract(),
            ["envelopes"] = envelopes,
        };
        var text = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        var bytes = Encoding.UTF8.GetBytes(text);
        var path = Path.Combine(RepositoryRoot(), "schemas", "v3-platform", "envelope-samples.json");
        if (Environment.GetEnvironmentVariable(RenderVariable) == "1")
        {
            await File.WriteAllBytesAsync(path, bytes);
            Assert.Fail($"{RenderVariable} rendered {path} and did not verify it: run again without the variable, which is the only run that checks anything.");
        }

        Assert.IsTrue(File.Exists(path), "The envelope samples file is missing: render it with " + RenderVariable + "=1 and commit it.");
        var held = Encoding.UTF8.GetBytes((await File.ReadAllTextAsync(path)).Replace("\r\n", "\n", StringComparison.Ordinal));
        CollectionAssert.AreEqual(bytes, held, "The envelope samples file is not what the handler sends now.");
    }

    private static JsonObject Contract()
    {
        var registry = V3OperationRegistry.Reviewed;
        return new JsonObject
        {
            ["envelope_schema"] = V3EnvelopeBuilder.Schema,
            ["version"] = V3OperationRegistry.Version,
            ["registry_schema"] = V3OperationRegistry.Schema,
            ["registry_sha256"] = registry.Sha256,
            ["refusal_schema"] = V3OperationRegistry.RefusalSchema,
            ["verdicts"] = new JsonArray(V3Verdicts.All.Select(static verdict => (JsonNode)verdict).ToArray()),
            ["operations"] = new JsonArray(registry.Operations.Select(static operation => (JsonNode)new JsonObject
            {
                ["operation_id"] = operation.OperationId,
                ["result_schema"] = operation.ResultSchema,
                ["result_object_types"] = new JsonArray(operation.ResultObjectTypes.Select(static type => (JsonNode)type).ToArray()),
            }).ToArray()),
            ["refusals"] = new JsonArray(registry.RefusalCodes.Select(code => (JsonNode)new JsonObject
            {
                ["code"] = code,
                ["mandatory_payload_fields"] = new JsonArray(registry.MandatoryPayloadFields(code).Select(static field => (JsonNode)field).ToArray()),
            }).ToArray()),
        };
    }

    private static async Task<JsonObject> CaptureAsync(
        MountedFixture fixture, V3CorpusMount? mount, string operation, string scenario, object parameters)
    {
        var body = JsonSerializer.Serialize(new { operation_id = operation, parameters });
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "envelope-samples";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, $"{operation} / {scenario}");
        var sent = ((MemoryStream)context.Response.Body).ToArray();
        _ = V3EnvelopeJson.ParseAndVerify(sent, V3OperationRegistry.Reviewed);

        var text = Encoding.UTF8.GetString(sent)
            .Replace(fixture.CorpusSha256, FixedCorpusDigest, StringComparison.Ordinal)
            .Replace(fixture.IndexSha256, FixedIndexDigest, StringComparison.Ordinal)
            .Replace("corpus-" + fixture.CorpusSha256[..16], "corpus-" + FixedCorpusDigest[..16], StringComparison.Ordinal);
        return new JsonObject
        {
            ["operation"] = operation,
            ["scenario"] = scenario,
            ["http_status"] = context.Response.StatusCode,
            ["content_type"] = context.Response.ContentType,
            ["envelope"] = JsonNode.Parse(text),
        };
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
