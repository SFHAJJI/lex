using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The refusal census: every refusal code a served operation really produces, observed by driving the
/// real handler on fixture mounts, and the payload it really sends. The samples are written to
/// <c>schemas/v3-platform/refusal-payload-samples.json</c> so a reader of the platform's payloads can
/// be held against what the producer sends and not against a second declaration of what it should.
/// <c>produced</c> and <c>not_produced</c> partition the registry's twenty codes exactly; a code the
/// API constructs that no scenario here produces fails; a scenario that yields a different code from
/// the one it names fails. A regeneration writes the file and then fails, so it can never be mistaken
/// for a check.
/// </summary>
[TestClass]
public sealed class V3RefusalPayloadSamplesTests
{
    private const string RenderVariable = "V3_RENDER_REFUSAL_PAYLOAD_SAMPLES";
    private const string SamplesSchema = "lex-v3-refusal-payload-samples/1";

    private sealed record Observation(string Operation, string Scenario, string Code, JsonElement Payload);

    /// <summary>What the scenarios saw, and every scenario that did not yield the refusal it names: all of them, so one run says everything.</summary>
    private sealed class Census
    {
        public List<Observation> Observed { get; } = [];

        public List<string> Problems { get; } = [];
    }

    [TestMethod]
    public async Task EveryRefusalTheServedOperationsProduceHasARealSampleAndTheTwentyAreOnePartition()
    {
        var census = new Census();
        await ObserveLuxembourgAsync(census);
        await ObserveOneStateAsync(census);
        await ObserveUnmountedAsync(census);
        Assert.IsEmpty(census.Problems, "Scenarios that did not yield the refusal they name:\n" + string.Join("\n", census.Problems));
        var observed = census.Observed;

        var registryCodes = V3OperationRegistry.Reviewed.RefusalCodes.Order(StringComparer.Ordinal).ToArray();
        Assert.AreEqual(20, registryCodes.Length);
        var producedCodes = observed.Select(static o => o.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var notProduced = registryCodes.Except(producedCodes, StringComparer.Ordinal).ToArray();

        // The partition: every code is in exactly one list, and no list holds a code the registry does not.
        Assert.IsEmpty(producedCodes.Except(registryCodes, StringComparer.Ordinal).ToArray(), "A refusal code outside the registry was produced.");
        Assert.AreEqual(20, producedCodes.Length + notProduced.Length);
        Assert.IsEmpty(producedCodes.Intersect(notProduced, StringComparer.Ordinal).ToArray());
        Assert.IsGreaterThan(0, producedCodes.Length, "A census with nothing produced is not a census.");

        // The API's own source is the second witness: a code it constructs that no scenario here
        // produces has no sample, and that fails until a scenario exists.
        var constructed = CodesTheApiConstructs();
        CollectionAssert.AreEqual(
            constructed,
            producedCodes,
            "The codes the API constructs and the codes the scenarios produced are not the same set: add a scenario for the code with no sample, or explain the one no scenario can reach.");

        // Line endings are LF whatever the platform writes and whatever a working copy holds: the repository
        // checks files out with LF, and a Windows runner's newline must not make this comparison say so.
        var document = BuildDocument(observed, notProduced).Replace("\r\n", "\n", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(document + "\n");
        var path = Path.Combine(RepositoryRoot(), "schemas", "v3-platform", "refusal-payload-samples.json");
        if (Environment.GetEnvironmentVariable(RenderVariable) == "1")
        {
            await File.WriteAllBytesAsync(path, bytes);
            Assert.Fail($"{RenderVariable} rendered {path} and did not verify it: run again without the variable, which is the only run that checks anything.");
        }

        Assert.IsTrue(File.Exists(path), "The samples file is missing: render it with " + RenderVariable + "=1 and commit it.");
        var held = Encoding.UTF8.GetBytes((await File.ReadAllTextAsync(path)).Replace("\r\n", "\n", StringComparison.Ordinal));
        CollectionAssert.AreEqual(bytes, held, "The samples file is not what the producers send now.");
    }

    [TestMethod]
    public async Task ARefusalScenarioThatYieldsAnotherCodeFailsNamingTheOperationAndTheScenario()
    {
        // The census is only as good as the scenarios it drives; a scenario that quietly produced a
        // different code would put a sample under the wrong name. This one asks for a refusal that the
        // fixture does not produce and shows the failure names both.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var census = new Census();

        await DriveAsync(census, mount, "as_of", "a date the fixture holds",
            new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" }, "no_version_for_date");

        var problem = census.Problems.Single();
        StringAssert.Contains(problem, "as_of");
        StringAssert.Contains(problem, "a date the fixture holds");
        StringAssert.Contains(problem, "no_version_for_date");
        Assert.IsEmpty(census.Observed);
    }

    private static async Task ObserveOneStateAsync(Census observed)
    {
        // The fixture as it is, one work with one state: a pinned coordinate whose digest is not the held
        // one is a mismatch here, and on a work with several states it is ambiguous, which is another refusal.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var wrongDigest = fixture.StateSha256[..^1] + (fixture.StateSha256[^1] == '0' ? '1' : '0');
        await DriveAsync(observed, mount, "resolve", "a pinned state whose digest is not the held one",
            new { identifier = $"/lu-legilux/{fixture.WorkKey}/{fixture.ApplicabilityDate}--{wrongDigest}" }, "pinned_digest_mismatch");
    }

    private static async Task ObserveLuxembourgAsync(Census observed)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // German is a second language of the work; two works carry a title that prefixes the query (a
        // plural title); one later French state has another rule-profile set; two French states share
        // one later date.
        await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        await fixture.AddTwoWorkTitlesAsync();
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        await fixture.AddRuleProfileToStateAsync(later.ExpressionIri, new string('a', 64));
        var twinDate = Shift(fixture.ApplicabilityDate, 800);
        await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var work = $"/lu-legilux/{fixture.WorkKey}";
        const string unknown = "/lu-legilux/no-such-work";
        const string european = "32016R0679";
        const string beforeHistory = "1900-01-01";
        var date = fixture.ApplicabilityDate;

        // resolve
        await DriveAsync(observed, mount, "resolve", "an identifier no work has", new { identifier = unknown }, "identifier_unknown");
        await DriveAsync(observed, mount, "resolve", "a title two works carry", new { identifier = "Reglement sur l'epreuve" }, "ambiguous_identifier");

        // as_of
        await DriveAsync(observed, mount, "as_of", "two states on the date", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "as_of", "a date before the history", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "as_of", "a language not held", new { identifier = work, date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "as_of", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "as_of", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");

        // timeline
        await DriveAsync(observed, mount, "timeline", "a language not held", new { identifier = work, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "timeline", "an identifier no work has", new { identifier = unknown, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "timeline", "a European identifier on a Luxembourg-only mount", new { identifier = european, language = "fra" }, "retrieval_mode_unavailable");

        // article_history
        await DriveAsync(observed, mount, "article_history", "an anchor the version does not hold", new { identifier = work, anchor = "art_no_such_anchor", language = "fra" }, "anchor_not_in_version");
        await DriveAsync(observed, mount, "article_history", "a language not held", new { identifier = work, anchor = "art_1", language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "article_history", "an identifier no work has", new { identifier = unknown, anchor = "art_1", language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "article_history", "a European identifier on a Luxembourg-only mount", new { identifier = european, anchor = "art_1", language = "fra" }, "retrieval_mode_unavailable");

        // diff
        await DriveAsync(observed, mount, "diff", "two states with different rule profiles", new { identifier = work, date_from = date, date_to = laterDate, language = "fra" }, "profiles_differ");
        await DriveAsync(observed, mount, "diff", "two states on the start date", new { identifier = work, date_from = twinDate, date_to = Shift(twinDate, 10), language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "diff", "a start before the history", new { identifier = work, date_from = beforeHistory, date_to = date, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "diff", "a language not held", new { identifier = work, date_from = date, date_to = laterDate, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "diff", "an identifier no work has", new { identifier = unknown, date_from = date, date_to = laterDate, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "diff", "a European identifier on a Luxembourg-only mount", new { identifier = european, date_from = date, date_to = laterDate, language = "fra" }, "retrieval_mode_unavailable");

        // changes_in_period
        await DriveAsync(observed, mount, "changes_in_period", "a language not held", new { date_from = date, date_to = laterDate, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "changes_in_period", "an identifier no work has", new { identifier = unknown, date_from = date, date_to = laterDate, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "changes_in_period", "a European identifier on a Luxembourg-only mount", new { identifier = european, date_from = date, date_to = laterDate, language = "fra" }, "retrieval_mode_unavailable");

        // in_force_on
        await DriveAsync(observed, mount, "in_force_on", "two states on the date of a named work", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "in_force_on", "a named work with no state that early", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "in_force_on", "a language not held", new { date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "in_force_on", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "in_force_on", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");

        // search
        await DriveAsync(observed, mount, "search", "two states on the date of a named work", new { query = "loyer", language = "fra", identifier = work, date = twinDate }, "ambiguous_version");
        await DriveAsync(observed, mount, "search", "a named work with no state that early", new { query = "loyer", language = "fra", identifier = work, date = beforeHistory }, "no_version_for_date");
        await DriveAsync(observed, mount, "search", "a language not held", new { query = "loyer", language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "search", "an identifier no work has", new { query = "loyer", language = "fra", identifier = unknown }, "identifier_unknown");
        await DriveAsync(observed, mount, "search", "a mode the index cannot serve", new { query = "loyer", language = "fra", mode = "bm25" }, "retrieval_mode_unavailable");
        await DriveAsync(observed, mount, "search", "a European identifier on a Luxembourg-only mount", new { query = "loyer", language = "fra", identifier = european }, "retrieval_mode_unavailable");

        // provenance
        await DriveAsync(observed, mount, "provenance", "two states on the date", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "provenance", "a date before the history", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "provenance", "a language not held", new { identifier = work, date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "provenance", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "provenance", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");

        // dossier
        await DriveAsync(observed, mount, "dossier", "a language not held", new { identifier = work, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "dossier", "an identifier no work has", new { identifier = unknown, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "dossier", "a European identifier on a Luxembourg-only mount", new { identifier = european, language = "fra" }, "retrieval_mode_unavailable");

        // citation
        await DriveAsync(observed, mount, "citation", "two states on the date", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "citation", "a date before the history", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "citation", "a language not held", new { identifier = work, date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "citation", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "citation", "an anchor the version does not hold", new { identifier = work, date, anchor = "art_no_such_anchor", language = "fra" }, "anchor_not_in_version");
        await DriveAsync(observed, mount, "citation", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");

        // coverage
        await DriveAsync(observed, mount, "coverage", "a language not held", new { language = "eng" }, "language_not_available");
    }

    private static async Task ObserveUnmountedAsync(Census observed)
    {
        // A mount that holds the European index and no Luxembourg one: every Luxembourg question is
        // no_corpus_mounted, said as a refusal and not as an error.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName));
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await fixture.AddEuropeCollisionAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        const string work = "/lu-legilux/any-work";

        // resolve never says no_corpus_mounted: a title asked of a mount with no Luxembourg titles is a mode it cannot serve.
        await DriveAsync(observed, mount, "resolve", "a title asked of a mount without a Luxembourg index", new { identifier = "Reglement sur l'epreuve" }, "retrieval_mode_unavailable");
        await DriveAsync(observed, mount, "as_of", "no Luxembourg index", new { identifier = work, date = "2024-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "timeline", "no Luxembourg index", new { identifier = work }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "article_history", "no Luxembourg index", new { identifier = work, anchor = "art_1" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "diff", "no Luxembourg index", new { identifier = work, date_from = "2024-01-01", date_to = "2025-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "changes_in_period", "no Luxembourg index", new { date_from = "2024-01-01", date_to = "2025-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "in_force_on", "no Luxembourg index", new { date = "2024-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "search", "no Luxembourg index", new { query = "loyer", language = "fra" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "coverage", "no Luxembourg index", new { }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "provenance", "no Luxembourg index", new { identifier = work, date = "2024-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "dossier", "no Luxembourg index", new { identifier = work }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "citation", "no Luxembourg index", new { identifier = work, date = "2024-01-01" }, "no_corpus_mounted");
    }

    /// <summary>Drives one served operation through the real handler and records the refusal, which must be the one named.</summary>
    private static async Task DriveAsync(
        Census census, V3CorpusMount mount, string operation, string scenario, object parameters, string expectedCode)
    {
        var body = JsonSerializer.Serialize(new { operation_id = operation, parameters });
        var context = await PostAsync(mount, "/api/v3/" + operation, body);
        var label = $"{operation} / {scenario}";
        if (context.Response.StatusCode != StatusCodes.Status200OK)
        {
            census.Problems.Add($"{label}: expected the refusal {expectedCode} and got HTTP {context.Response.StatusCode}: {Encoding.UTF8.GetString(ResponseBytes(context))}");
            return;
        }

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        if (envelope.Refusal is null || !string.Equals(envelope.Refusal.Code, expectedCode, StringComparison.Ordinal))
        {
            census.Problems.Add($"{label}: expected the refusal {expectedCode} and got {envelope.Refusal?.Code ?? envelope.Verdict}");
            return;
        }

        census.Observed.Add(new Observation(operation, scenario, expectedCode, envelope.Refusal.HelpfulPayload.Clone()));
    }

    /// <summary>The refusal codes the API's source constructs, read from the source: a code built by a variable is not seen here.</summary>
    private static string[] CodesTheApiConstructs()
    {
        var api = Path.Combine(RepositoryRoot(), "src", "Lex.V3.Api");
        var pattern = new Regex(@"V3PlatformOperationRefusal\(\s*[A-Za-z_.]+,\s*""([a-z_]+)""", RegexOptions.CultureInvariant);
        // Recursive, with build output left out: a handler added in a subfolder must not be missed silently.
        var separators = new[] { Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar };
        return Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(file => !separators.Any(separator => file.Contains(separator, StringComparison.Ordinal)))
            .SelectMany(file => pattern.Matches(File.ReadAllText(file)).Select(static m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static string BuildDocument(List<Observation> observed, string[] notProduced)
    {
        var produced = new JsonArray();
        foreach (var group in observed.GroupBy(static o => o.Code, StringComparer.Ordinal).OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            var members = group.OrderBy(static o => o.Operation, StringComparer.Ordinal).ThenBy(static o => o.Scenario, StringComparer.Ordinal).ToArray();
            var keySets = members.Select(static o => o.Payload.EnumerateObject().Select(static p => p.Name).ToArray()).ToArray();
            var union = keySets.SelectMany(static k => k).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var everywhere = union.Where(key => keySets.All(keys => keys.Contains(key, StringComparer.Ordinal))).ToArray();
            // The richest payload seen is the sample: a reader held against it is held against every field any producer sends.
            var richest = members.OrderByDescending(static o => o.Payload.EnumerateObject().Count()).ThenBy(static o => o.Operation, StringComparer.Ordinal).First();
            // "A reader held against the sample is held against every field any producer sends" is true only
            // while one producer's payload holds every key another sends. Two producers with disjoint keys
            // would put a key in optional_payload_keys and in no sample, where nothing could render it.
            var missing = union.Except(richest.Payload.EnumerateObject().Select(static p => p.Name), StringComparer.Ordinal).ToArray();
            Assert.IsEmpty(missing, $"{group.Key}: no single producer sends every key any producer sends ({string.Join(", ", missing)} is in no sample); the census needs a sample per key set for this code.");
            produced.Add(new JsonObject
            {
                ["code"] = group.Key,
                ["optional_payload_keys"] = new JsonArray(union.Except(everywhere, StringComparer.Ordinal).Select(static k => (JsonNode)k).ToArray()),
                ["payload"] = JsonNode.Parse(richest.Payload.GetRawText()),
                ["produced_by"] = new JsonArray(members.Select(static o => o.Operation).Distinct(StringComparer.Ordinal).Select(static o => (JsonNode)o).ToArray()),
            });
        }

        var root = new JsonObject
        {
            ["not_produced"] = new JsonArray(notProduced.Select(static c => (JsonNode)c).ToArray()),
            ["produced"] = produced,
            ["publisher"] = "lu-legilux",
            ["schema"] = SamplesSchema,
        };
        return SortOrdinal(root).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode SortOrdinal(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(static member => member.Key, StringComparer.Ordinal)
            .Select(static member => KeyValuePair.Create(member.Key, member.Value is null ? null : SortOrdinal(member.Value)))),
        JsonArray value => new JsonArray(value.Select(static item => item is null ? null : SortOrdinal(item)).ToArray()),
        _ => node.DeepClone(),
    };

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
        context.TraceIdentifier = "refusal-payload-samples";
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
