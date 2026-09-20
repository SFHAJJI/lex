using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The answer census: what a served operation really sends when it answers, observed by driving the
/// real handler on a fixture mount, written to <c>schemas/v3-platform/answer-samples.json</c>.
///
/// The refusal census (<see cref="V3RefusalPayloadSamplesTests"/>) did this for every refusal payload, and
/// a reader held against it is held against what the producer sends rather than against a second
/// declaration of what it should send. There was no equivalent for ANSWERS, and the same defect kept
/// arriving from different sides: a reader contract demanding a field no producer sends, a catalogue
/// page teaching a URL grammar nothing emits, a dossier refusing a platform that declares a field
/// absent. Each was found by hand, one surface at a time. This is the artifact that makes the next one
/// fail instead.
///
/// SOME OF WHAT AN ANSWER CARRIES IS NOT STABLE FROM RUN TO RUN, and a census that is compared byte for
/// byte cannot hold it. Measured over four runs of one test at one head: the source object reference and
/// both mount digests differ EVERY run, while the state digest is identical. The cause is the fixture,
/// not the platform -- <c>LuxembourgAcquisitionTestFixture</c> mints a fresh <c>urn:uuid</c> per
/// <c>SourceArtifactRef</c> while hashing deterministic bytes beside it, so the object reference, and the
/// corpus and index digests above it, move. The platform's digests are reproducible; the fixture's
/// identifiers are deliberately not. So the unstable fields are normalised to a placeholder, their SHAPE
/// is held, and the RELATIONS between them are held, which is where their content actually is.
///
/// The normalisation is the dangerous part, because a list of fields to ignore is a list of fields
/// nothing checks. Two things keep it honest: every normalised field's shape is asserted before it is
/// replaced, and <see cref="TheNormalisationNamesEveryFieldThatMoves"/> drives the same request twice in
/// one run and fails if any field outside the list differs. A field that starts moving is a failure, not
/// a silently wider list.
/// </summary>
[TestClass]
public sealed class V3AnswerSamplesTests
{
    private const string RenderVariable = "V3_RENDER_ANSWER_SAMPLES";
    private const string SamplesSchema = "lex-v3-answer-samples/1";
    private const string Placeholder = "<varies-per-run>";

    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-17T00:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>
    /// The operation paths whose values the fixture re-mints every run, as paths into the answer with
    /// <c>[]</c> for a list. Each is asserted to be a 64-character lower-case hex digest before it is
    /// replaced, so "it varies" never becomes "it can be anything".
    /// </summary>
    private static readonly string[] VariesPerRun =
    [
        // The same two facts at two paths, because the two operations shape them differently: `as_of`
        // serves the mount digests flat and `provenance` groups them under `verified_by` with the
        // registry digest beside them. The first run of the double-run test below found this by failing;
        // a list written from reading one answer had covered only the nested pair.
        "corpus_sha256",
        "index_sha256",
        "verified_by.corpus_sha256",
        "verified_by.index_sha256",
        // The object reference moves because the fixture mints the URN it is computed over. The body
        // digests beside it DO NOT: they hash deterministic bytes. They were on this list anyway, put
        // there by me alongside the three I had actually measured, and the writer seat proved what that
        // cost -- a wrong `body_sha256` or `body_receipt_sha256` reached a reader through an answer this
        // census passed. Measuring three fields and listing five is the error this artifact exists to
        // catch, committed in the artifact itself.
        "states[].sources[].object_ref_sha256",
    ];

    /// <summary>
    /// Operations served by a route but not yet sampled here, each with the reason. It is a declared
    /// partial and it can only shrink: a served operation that is neither sampled nor on this list fails.
    /// </summary>
    private static readonly Dictionary<string, string> NotSampled = new(StringComparer.Ordinal)
    {
        ["resolve"] = "sampled when a reader is built against it",
        ["timeline"] = "sampled when a reader is built against it",
        ["article_history"] = "sampled when a reader is built against it",
        ["diff"] = "sampled when a reader is built against it",
        ["changes_in_period"] = "sampled when a reader is built against it",
        ["in_force_on"] = "sampled when a reader is built against it",
        ["search"] = "sampled when a reader is built against it",
        ["coverage"] = "sampled when a reader is built against it",
        ["dossier"] = "sampled when a reader is built against it",
    };

    private sealed record Answer(string Operation, string Scenario, string ObjectType, JsonNode Body);

    [TestMethod]
    public async Task EverySampledAnswerIsWhatTheProducerSendsAndTheServedOperationsArePartitioned()
    {
        var answers = await ObserveAsync();

        // The second witness: the served routes, read from the binding the handler dispatches on rather
        // than from a list kept beside it. An operation that becomes served with no sample and no reason
        // fails here, which is the ratchet.
        var served = V3RestRouteBinding.Served.Select(static binding => binding.OperationId)
            .Order(StringComparer.Ordinal).ToArray();
        var sampled = answers.Select(static a => a.Operation).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var declared = NotSampled.Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.IsEmpty(
            sampled.Except(served, StringComparer.Ordinal).ToArray(),
            "An operation is sampled that no route serves.");
        Assert.IsEmpty(
            declared.Except(served, StringComparer.Ordinal).ToArray(),
            "An operation is declared unsampled that no route serves; the list has outlived its reason.");
        Assert.IsEmpty(
            sampled.Intersect(declared, StringComparer.Ordinal).ToArray(),
            "An operation is both sampled and declared unsampled.");
        CollectionAssert.AreEqual(
            served,
            sampled.Concat(declared).Order(StringComparer.Ordinal).ToArray(),
            "The served operations are not exactly the sampled ones plus the declared ones: sample the new operation or say why it has none.");

        var document = BuildDocument(answers).Replace("\r\n", "\n", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(document + "\n");
        var path = Path.Combine(RepositoryRoot(), "schemas", "v3-platform", "answer-samples.json");
        if (Environment.GetEnvironmentVariable(RenderVariable) == "1")
        {
            await File.WriteAllBytesAsync(path, bytes);
            Assert.Fail($"{RenderVariable} rendered {path} and did not verify it: run again without the variable, which is the only run that checks anything.");
        }

        Assert.IsTrue(File.Exists(path), "The answer samples file is missing: render it with " + RenderVariable + "=1 and commit it.");
        var held = Encoding.UTF8.GetBytes((await File.ReadAllTextAsync(path)).Replace("\r\n", "\n", StringComparison.Ordinal));
        CollectionAssert.AreEqual(bytes, held, "The answer samples file is not what the producers send now.");
    }

    [TestMethod]
    public async Task TheNormalisationNamesEveryFieldThatMoves()
    {
        // The list of fields to ignore is a list of fields nothing checks, so it is measured rather than
        // maintained: the same request twice, in one run, on two fixtures. Everything outside the list
        // must be identical. A field that starts moving fails here and is not quietly absorbed.
        var firstRaw = await ObserveAsync();
        var secondRaw = await ObserveAsync();
        Assert.AreEqual(
            BuildDocument(firstRaw),
            BuildDocument(secondRaw),
            "Two observations of the same request differ outside the normalised fields: add the field to VariesPerRun with its reason, or find why it moved.");

        // AND THE CONVERSE, which is the direction this list was weak in. Nothing made a listed field
        // EARN its place, so two entries that never move sat here hiding values the file could pin, and
        // a wrong digest in either reached a reader through an answer this census passed. Every listed
        // path that the answers actually carry must differ between two raw observations.
        foreach (var path in VariesPerRun)
        {
            var before = ValuesAt(firstRaw, path);
            var after = ValuesAt(secondRaw, path);

            // A path no answer carries was skipped here, which is the same hole one level along: a
            // list that must earn every entry, with an exemption for the entry that earns nothing.
            // A dead path could sit on the list and in the rendered file and pass, which the writer
            // seat proved with a mutant. Every listed path must be one the answers actually have.
            Assert.IsGreaterThan(
                0,
                before.Count,
                $"{path} is normalised and no sampled answer carries it: take it off VariesPerRun, or sample an operation that has it.");

            CollectionAssert.AreNotEqual(
                before,
                after,
                $"{path} is normalised and did not move between two observations: take it off VariesPerRun so the file pins it, or show the run where it moves.");
        }
    }

    [TestMethod]
    public async Task ANormalisedFieldIsCheckedForItsShapeBeforeItIsReplaced()
    {
        // The placeholder must not be a hiding place: a field on the list that stopped being a digest
        // would otherwise be replaced without complaint and the census would say nothing about it.
        // Both rules, separately. Written first with one value that broke BOTH ("not-a-digest": wrong
        // length AND not hex), this passed with the length rule deleted, because the hex rule still
        // caught it -- a mutant proved it. One fixture that violates two rules tests neither of them.
        var answers = await ObserveAsync();
        foreach (var (value, why) in new[]
                 {
                     (new string('z', 64), "64 characters and not hex"),
                     ("abcdef", "hex and not 64 characters"),
                 })
        {
            var body = answers.Single(static a => a.Operation == "provenance").Body.DeepClone();
            body["verified_by"]!["corpus_sha256"] = value;
            var failure = Assert.ThrowsExactly<AssertFailedException>(
                () => Normalise(body, string.Empty),
                $"a value that is {why} was normalised without complaint");
            StringAssert.Contains(failure.Message, "verified_by.corpus_sha256");
        }
    }

    [TestMethod]
    public async Task TheTwoAnswersToOneRequestAgreeOnTheStateTheyDescribe()
    {
        // The relation the normalisation would otherwise destroy. `provenance`'s derivation sends a caller
        // to `as_of`'s article_identities for the SAME request; if the two answers named different states
        // that instruction would be wrong, and after normalising the digests nothing else would say so.
        var answers = await ObserveAsync();
        var provenance = answers.Single(static a => a.Operation == "provenance").Body;
        var asOf = answers.Single(static a => a.Operation == "as_of").Body;

        var provenanceState = provenance["states"]![0]!["state_sha256"]!.GetValue<string>();
        var asOfState = asOf["states"]![0]!["state_sha256"]!.GetValue<string>();
        Assert.AreEqual(asOfState, provenanceState, "provenance and as_of name different states for one request.");
        Assert.AreEqual(
            asOf["states"]![0]!["permalink"]!.GetValue<string>(),
            provenance["states"]![0]!["permalink"]!.GetValue<string>());

        // And the relation inside a source, which is the overclaim the sources_note exists to avoid: the
        // digest of the publisher's bytes is not the corpus's reference to the object holding them.
        var source = provenance["states"]![0]!["sources"]![0]!;
        Assert.AreNotEqual(
            source["object_ref_sha256"]!.GetValue<string>(),
            source["body_sha256"]!.GetValue<string>(),
            "The body digest is the object reference: the answer would be naming one as the other.");
    }

    /// <summary>
    /// One request, both answers, from the real handler on a real mount. The two are observed together and
    /// from one fixture because the derivation's instruction is about one request answered twice.
    /// </summary>
    private static async Task<IReadOnlyList<Answer>> ObserveAsync()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var parameters = new
        {
            identifier = $"/lu-legilux/{fixture.WorkKey}",
            date = fixture.ApplicabilityDate,
            language = "fra",
        };
        return
        [
            await DriveAsync(mount, "provenance", "one work, one state, asked in the language it is held in", parameters),
            await DriveAsync(mount, "as_of", "the same request, so a caller can move from one to the other", parameters),
        ];
    }

    private static async Task<Answer> DriveAsync(
        V3CorpusMount mount, string operation, string scenario, object parameters)
    {
        var body = JsonSerializer.Serialize(new { operation_id = operation, parameters });
        var context = await PostAsync(mount, "/api/v3/" + operation, body);
        Assert.AreEqual(
            StatusCodes.Status200OK,
            context.Response.StatusCode,
            $"{operation} / {scenario}: {Encoding.UTF8.GetString(ResponseBytes(context))}");

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.IsNull(envelope.Refusal, $"{operation} / {scenario} refused with {envelope.Refusal?.Code}.");
        Assert.IsNotNull(envelope.Result, $"{operation} / {scenario} answered with no result.");
        var value = JsonNode.Parse(envelope.Result.Value.GetRawText())!;
        return new Answer(operation, scenario, envelope.Result.ObjectType, value);
    }

    /// <summary>Every value an answer carries at one normalise path, in a stable order, for comparing two runs.</summary>
    private static List<string> ValuesAt(IReadOnlyList<Answer> answers, string path)
    {
        var found = new List<string>();
        foreach (var answer in answers.OrderBy(static a => a.Operation, StringComparer.Ordinal))
        {
            Collect(answer.Body, string.Empty, path, found);
        }

        return found;
    }

    private static void Collect(JsonNode node, string path, string wanted, List<string> found)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null) Collect(item, path + "[]", wanted, found);
                }

                break;
            case JsonObject observed:
                foreach (var property in observed)
                {
                    var childPath = path.Length == 0 ? property.Key : path + "." + property.Key;
                    if (string.Equals(childPath, wanted, StringComparison.Ordinal))
                    {
                        found.Add(property.Value?.GetValue<string>() ?? "<null>");
                        continue;
                    }

                    if (property.Value is not null) Collect(property.Value, childPath, wanted, found);
                }

                break;
        }
    }

    /// <summary>
    /// Replaces every value the fixture re-mints per run, after asserting it is a digest. Paths use
    /// <c>[]</c> for a list, so one entry covers every element of it.
    /// </summary>
    private static void Normalise(JsonNode node, string path)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null) Normalise(item, path + "[]");
                }

                break;
            case JsonObject observed:
                foreach (var property in observed.ToArray())
                {
                    var childPath = path.Length == 0 ? property.Key : path + "." + property.Key;
                    if (VariesPerRun.Contains(childPath, StringComparer.Ordinal))
                    {
                        var value = property.Value?.GetValue<string>();
                        Assert.IsNotNull(value, $"{childPath} is normalised and is not a string.");
                        Assert.AreEqual(64, value.Length, $"{childPath} is normalised and is not a 64-character digest: {value}");
                        Assert.IsTrue(
                            value.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')),
                            $"{childPath} is normalised and is not lower-case hex: {value}");
                        observed[property.Key] = Placeholder;
                        continue;
                    }

                    if (property.Value is not null) Normalise(property.Value, childPath);
                }

                break;
        }
    }

    private static string BuildDocument(IReadOnlyList<Answer> answers)
    {
        var operations = new JsonArray();
        foreach (var answer in answers.OrderBy(static a => a.Operation, StringComparer.Ordinal))
        {
            var body = answer.Body.DeepClone();
            Normalise(body, string.Empty);
            operations.Add(new JsonObject
            {
                ["operation"] = answer.Operation,
                ["object_type"] = answer.ObjectType,
                ["scenario"] = answer.Scenario,
                ["answer"] = body,
            });
        }

        var notSampled = new JsonArray();
        foreach (var (operation, reason) in NotSampled.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            notSampled.Add(new JsonObject { ["operation"] = operation, ["reason"] = reason });
        }

        var document = new JsonObject
        {
            ["schema"] = SamplesSchema,
            ["note"] = "What each served operation really sends when it answers, observed by driving the real handler. "
                + "Values the fixture re-mints per run are replaced by " + Placeholder + "; their shape is asserted before "
                + "replacement and the relations between them are held by tests, not by this file.",
            ["varies_per_run"] = new JsonArray(VariesPerRun.Select(static p => JsonValue.Create(p)).ToArray<JsonNode?>()),
            ["sampled"] = operations,
            ["not_sampled"] = notSampled,
        };
        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] ResponseBytes(DefaultHttpContext context)
    {
        var stream = (MemoryStream)context.Response.Body;
        return stream.ToArray();
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

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "answer-samples";
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
