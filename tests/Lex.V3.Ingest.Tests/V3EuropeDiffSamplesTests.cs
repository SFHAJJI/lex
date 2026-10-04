using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lex.V3.Api;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU comparison census: what EU <c>diff</c> really sends, observed by driving the real handler on mounts derived from the
/// consolidated fixtures, written to <c>schemas/v3-platform/europe-diff-samples.json</c>. The compare screen's EU reader
/// (<c>web/scripts/compare-answer.mjs</c>) is held to these answers, as its Luxembourg reader is held to the answer census, so
/// a reader demanding a field the producer does not send fails instead of shipping.
///
/// Only the mount's two digests move from run to run: the fixture mints a fresh resource id per held object, so the corpus and
/// the index above them differ every build. Measured on 2026-10-04 by deriving the same fixture twice and comparing the two
/// answers field by field: every wording digest, permalink and article digest was identical, and only these two differed.
/// They are asserted to be digests and replaced by a placeholder; a field that starts moving fails this test.
/// </summary>
public sealed partial class V3FirstMountBuildTests
{
    private const string EuropeDiffSamplesVariable = "V3_RENDER_EUROPE_DIFF_SAMPLES";
    private const string EuropeDiffPlaceholder = "<varies-per-run>";
    private static readonly string[] EuropeDiffVariesPerRun = ["corpus_sha256", "index_sha256"];

    [TestMethod]
    public async Task TheEuComparisonSamplesAreWhatTheHandlerSends()
    {
        var samples = new JsonArray();
        async Task AddAsync(V3CorpusMount mount, string name, string mountNote, object request)
        {
            var envelope = await EnvelopeAsync(mount, "/api/v3/diff", "diff", request);
            Assert.IsNull(envelope.Refusal, $"{name}: {envelope.Refusal?.Code}");
            Assert.AreEqual("diff", envelope.Result!.ObjectType, name);
            var answer = JsonNode.Parse(envelope.Result.Value.GetRawText())!.AsObject();
            foreach (var field in EuropeDiffVariesPerRun)
            {
                var value = answer[field]!.GetValue<string>();
                Assert.IsTrue(Regex.IsMatch(value, "^[0-9a-f]{64}$"), $"{name}: {field} is a digest before it is replaced");
                answer[field] = EuropeDiffPlaceholder;
            }

            samples.Add(new JsonObject
            {
                ["name"] = name,
                ["mount"] = mountNote,
                ["request"] = JsonSerializer.SerializeToNode(request),
                ["object_type"] = "diff",
                ["answer"] = answer,
            });
        }

        var (twoRoot, twoDirectory) = await ConsolidatedWorksMountAsync(
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(WorkB, ConsolidationDate, Designated, OtherEnglishPackage,
                EuFirstMountAcquisitionTests.FrenchOf(OtherEnglishPackage)));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(twoDirectory, CancellationToken.None);
            Assert.IsNotNull(mount);
            const string note = "the GDPR's original wording and one consolidated version dated 2024-01-01, whose English text " +
                "rewrites 'natural persons' as 'natural humans' (the French is the fixture's synthetic package of that English)";
            await AddAsync(mount, "two_wordings", note,
                new { identifier = GdprSeed, date_from = "2020-06-01", date_to = "2025-03-01" });
            await AddAsync(mount, "same_wording", note,
                new { identifier = GdprSeed, date_from = "2018-05-25", date_to = "2020-06-01", language = "eng" });
        }
        finally
        {
            Directory.Delete(twoRoot, recursive: true);
        }

        var (oneRoot, oneDirectory) = await ConsolidatedWorksMountAsync(
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(WorkB, ConsolidationDate, Designated, null, FrenchPackage));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(oneDirectory, CancellationToken.None);
            Assert.IsNotNull(mount);
            await AddAsync(mount, "language_not_compared",
                "the GDPR's original wording and one consolidated version dated 2024-01-01 whose text is held in French only",
                new { identifier = GdprSeed, date_from = "2020-06-01", date_to = "2025-03-01" });
        }
        finally
        {
            Directory.Delete(oneRoot, recursive: true);
        }

        var document = new JsonObject
        {
            ["schema"] = "lex-v3-europe-diff-samples/1",
            ["note"] = "EU diff answers observed by driving the real handler on mounts derived from the consolidated fixtures "
                + "(V3EuropeDiffSamplesTests). THE MOUNTS ARE FIXTURES: these are the shapes the producer sends, not a real "
                + "corpus's scale or completeness.",
            ["varies_per_run"] = new JsonArray(EuropeDiffVariesPerRun.Select(static field => JsonValue.Create(field)).ToArray<JsonNode?>()),
            ["samples"] = samples,
        };
        var bytes = Encoding.UTF8.GetBytes(
            document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        var path = Path.Combine(CheckoutRoot(), "schemas", "v3-platform", "europe-diff-samples.json");
        if (Environment.GetEnvironmentVariable(EuropeDiffSamplesVariable) == "1")
        {
            await File.WriteAllBytesAsync(path, bytes);
            Assert.Fail($"{EuropeDiffSamplesVariable} rendered {path} and did not verify it: run again without the variable, which is the only run that checks anything.");
        }

        Assert.IsTrue(File.Exists(path), "The EU comparison samples are missing: render them with " + EuropeDiffSamplesVariable + "=1 and commit them.");
        var held = Encoding.UTF8.GetBytes((await File.ReadAllTextAsync(path)).Replace("\r\n", "\n", StringComparison.Ordinal));
        CollectionAssert.AreEqual(bytes, held, "The EU comparison samples are not what the handler sends now.");
    }
}
