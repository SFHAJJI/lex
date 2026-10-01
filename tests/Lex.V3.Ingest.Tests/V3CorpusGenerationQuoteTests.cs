using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A state only a retained generation holds is answered from it (predecessor chaining, the sixth slice's third part, G3):
/// two real builds of the state fixture, the second with one article reworded, so the mounted index holds a new state at
/// the act's coordinate and only the first build, kept beside it as a generation, holds the original. <c>as_observed</c>
/// at the first build's snapshot serves the original state in full from the generation, and <c>verify</c> resolves the
/// permalink the product emitted before the rewording, naming the generation that holds it and the state that replaced
/// it. Without the generation, the same requests answer as before: without text, and <c>pinned_digest_mismatch</c>.
/// </summary>
[TestClass]
public sealed class V3CorpusGenerationQuoteTests
{
    private sealed record TwoBuilds(string Root, string Pruned, string FirstIndexSha256, string FirstCorpusSha256, string WorkKey) : IDisposable
    {
        public void Dispose()
        {
            foreach (var directory in new[] { Root, Pruned })
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }

    internal static string WriteMount(Stage3DerivationProfileEnvelope envelope, LuxembourgIndexBuildResult luxembourg)
    {
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var europe = EuropeIndexBuilder.TryBuild(envelope, out var europeRefusal, out var europeDetail);
        Assert.IsNotNull(europe, $"{europeRefusal}: {europeDetail}");
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-generation-quote-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, V3FirstMountBuildResult.CorpusFileName), corpus.CanonicalBytes.ToArray());
        File.WriteAllBytes(Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), luxembourg.IndexBytes.ToArray());
        File.WriteAllBytes(Path.Combine(directory, V3FirstMountBuildResult.LuxembourgCapabilityManifestFileName), luxembourg.CapabilityManifestBytes.ToArray());
        File.WriteAllBytes(Path.Combine(directory, V3FirstMountBuildResult.EuropeIndexFileName), europe.IndexBytes.ToArray());
        File.WriteAllBytes(Path.Combine(directory, V3FirstMountBuildResult.EuropeCapabilityManifestFileName), europe.CapabilityManifestBytes.ToArray());
        File.WriteAllText(Path.Combine(directory, "build-report.json"), "{}");
        return directory;
    }

    private static async Task<TwoBuilds> TwoBuildsAsync()
    {
        var (first, firstBuilt, firstCorpus) = await LuxembourgIndexBuilderTests.BuildStateEnvelopeAsync();
        var (second, _, _) = await LuxembourgIndexBuilderTests.BuildStateEnvelopeAsync(
            static xml => LuxembourgIndexBuilderTests.ReplaceFirst(xml, "assemblée générale", "assemblée plénière"));
        var predecessor = LuxembourgIndexPredecessor.TryRead(firstBuilt.IndexRef, firstBuilt.IndexBytes.Span, out var readRefusal, out var readDetail);
        Assert.IsNotNull(predecessor, $"{readRefusal}: {readDetail}");
        var secondBuilt = LuxembourgIndexBuilder.TryBuild(second, predecessor, LuxembourgIndexBuilderTests.Later, out var refusal, out var detail);
        Assert.IsNotNull(secondBuilt, $"{refusal}: {detail}");

        // The second build's mount, keeping the first beside it; and the same mount with the first pruned.
        var generation = WriteMount(first, firstBuilt);
        var root = WriteMount(second, secondBuilt);
        var kept = Path.Combine(root, V3CorpusMountWriter.GenerationsDirectoryName, firstBuilt.IndexRef.Sha256);
        Directory.CreateDirectory(kept);
        foreach (var file in Directory.GetFiles(generation)) File.Copy(file, Path.Combine(kept, Path.GetFileName(file)));
        Directory.Delete(generation, recursive: true);
        await V3CorpusMountWriter.WriteRetentionRecordAsync(root, secondBuilt.IndexRef, secondBuilt.IndexBytes, CancellationToken.None,
            new HashSet<string>(StringComparer.Ordinal) { firstBuilt.IndexRef.Sha256 });
        var verified = await V3CorpusMountWriter.VerifyAsync(root, CancellationToken.None);
        Assert.IsTrue(verified.Verified, verified.Detail);

        var pruned = WriteMount(second, secondBuilt);
        await V3CorpusMountWriter.WriteRetentionRecordAsync(pruned, secondBuilt.IndexRef, secondBuilt.IndexBytes, CancellationToken.None);
        var states = (await V3CorpusMount.OpenAsync(root, CancellationToken.None))!;
        string workKey;
        using (states)
        {
            workKey = (await EnvelopeAsync(states, "/api/v3/events", "events", new { })).Result!.Value.GetProperty("events")[0].GetProperty("work_key").GetString()!;
        }

        return new TwoBuilds(root, pruned, firstBuilt.IndexRef.Sha256, firstCorpus.ArtifactRef.Sha256, workKey);
    }

    [TestMethod]
    public async Task AStateOnlyARetainedGenerationHoldsIsServedInFullFromItAndItsPermalinkStillVerifies()
    {
        using var fixture = await TwoBuildsAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Root, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        // The mounted state, today, and the state the first build held at the same coordinate, by the log.
        var current = (await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier, date = "2030-01-01" })).Result!.Value.GetProperty("states")[0];
        var date = current.GetProperty("applicability_date").GetString()!;
        var observed = (await EnvelopeAsync(mount, "/api/v3/as_observed", "as_observed", new { identifier, date, snapshot = fixture.FirstIndexSha256 })).Result!.Value;
        var original = observed.GetProperty("states").EnumerateArray().Single();
        Assert.AreNotEqual(current.GetProperty("state_sha256").GetString(), original.GetProperty("state_sha256").GetString(), "the rewording minted a new version");
        Assert.IsTrue(original.GetProperty("text_held").GetBoolean(), "the generation holds the original state");
        Assert.AreEqual(fixture.FirstIndexSha256, original.GetProperty("text_from").GetProperty("snapshot_id").GetString());
        Assert.AreEqual(1, original.GetProperty("text_from").GetProperty("observation").GetInt64());
        Assert.IsTrue(original.GetProperty("articles").GetArrayLength() > 0, "served in full, as as_of serves a state");

        // The mounted snapshot serves the mounted state with no text_from.
        var mounted = (await EnvelopeAsync(mount, "/api/v3/as_observed", "as_observed", new { identifier, date, snapshot = observed.GetProperty("index_sha256").GetString() })).Result!.Value
            .GetProperty("states")[0];
        Assert.AreEqual(JsonValueKind.Null, mounted.GetProperty("text_from").ValueKind);

        // The permalink the product emitted before the rewording verifies in the generation, and says what replaced it.
        var permalink = original.GetProperty("permalink").GetString()!;
        var verified = (await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = permalink })).Result!.Value;
        Assert.AreEqual("digest_matches", verified.GetProperty("verdict").GetString());
        Assert.AreEqual(fixture.FirstIndexSha256, verified.GetProperty("held_in").GetProperty("snapshot_id").GetString());
        Assert.AreEqual(current.GetProperty("permalink").GetString(), verified.GetProperty("superseded_by").GetProperty("permalink").GetString());
        Assert.AreEqual(fixture.FirstIndexSha256, verified.GetProperty("verified_by").GetProperty("index_sha256").GetString());
        Assert.AreEqual(fixture.FirstCorpusSha256, verified.GetProperty("verified_by").GetProperty("corpus_sha256").GetString());
        Assert.IsTrue(verified.GetProperty("sources").GetArrayLength() > 0, "the generation's own sources");

        // The current permalink verifies in the mounted index, naming no generation.
        var today = (await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = current.GetProperty("permalink").GetString() })).Result!.Value;
        Assert.AreEqual(JsonValueKind.Null, today.GetProperty("held_in").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, today.GetProperty("superseded_by").ValueKind);
    }

    /// <summary>
    /// The review of #889: a later build that holds the work no longer at all (the complete envelope's Luxembourg part
    /// holds no state), chained to the first. The coordinate holds no current state, so <c>verify</c> must ask the
    /// generation before refusing, and <c>as_observed</c> must find the work through the log by its stable coordinate.
    /// </summary>
    [TestMethod]
    public async Task AWorkTheMountedBuildNoLongerHoldsIsStillAnsweredFromItsGeneration()
    {
        var (first, firstBuilt, _) = await LuxembourgIndexBuilderTests.BuildStateEnvelopeAsync();
        var second = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var predecessor = LuxembourgIndexPredecessor.TryRead(firstBuilt.IndexRef, firstBuilt.IndexBytes.Span, out _, out _);
        var secondBuilt = LuxembourgIndexBuilder.TryBuild(second, predecessor, LuxembourgIndexBuilderTests.Later, out var refusal, out var detail);
        Assert.IsNotNull(secondBuilt, $"{refusal}: {detail}");
        var generation = WriteMount(first, firstBuilt);
        var root = WriteMount(second, secondBuilt);
        try
        {
            var kept = Path.Combine(root, V3CorpusMountWriter.GenerationsDirectoryName, firstBuilt.IndexRef.Sha256);
            Directory.CreateDirectory(kept);
            foreach (var file in Directory.GetFiles(generation)) File.Copy(file, Path.Combine(kept, Path.GetFileName(file)));
            await V3CorpusMountWriter.WriteRetentionRecordAsync(root, secondBuilt.IndexRef, secondBuilt.IndexBytes, CancellationToken.None,
                new HashSet<string>(StringComparer.Ordinal) { firstBuilt.IndexRef.Sha256 });
            using var mount = await V3CorpusMount.OpenAsync(root, CancellationToken.None);
            Assert.IsNotNull(mount);

            var sighted = (await EnvelopeAsync(mount, "/api/v3/events", "events", new { })).Result!.Value.GetProperty("events")[0];
            var workKey = sighted.GetProperty("work_key").GetString()!;
            var date = sighted.GetProperty("applicability_date").GetString()!;
            var identifier = $"/lu-legilux/{workKey}";
            var current = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier, date });
            Assert.AreEqual("identifier_unknown", current.Refusal?.Code, "the mounted build holds the work no longer");

            var observed = (await EnvelopeAsync(mount, "/api/v3/as_observed", "as_observed", new { identifier, date, snapshot = firstBuilt.IndexRef.Sha256 })).Result!.Value;
            var state = observed.GetProperty("states").EnumerateArray().Single();
            Assert.IsTrue(state.GetProperty("text_held").GetBoolean());
            Assert.AreEqual(firstBuilt.IndexRef.Sha256, state.GetProperty("text_from").GetProperty("snapshot_id").GetString());

            var verified = (await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = state.GetProperty("permalink").GetString() })).Result!.Value;
            Assert.AreEqual("digest_matches", verified.GetProperty("verdict").GetString());
            Assert.AreEqual(firstBuilt.IndexRef.Sha256, verified.GetProperty("held_in").GetProperty("snapshot_id").GetString());
            Assert.AreEqual(JsonValueKind.Null, verified.GetProperty("superseded_by").ValueKind, "no current state replaced it at that coordinate");

            var never = await EnvelopeAsync(mount, "/api/v3/as_observed", "as_observed", new { identifier = "/lu-legilux/not-a-held-work", date, snapshot = firstBuilt.IndexRef.Sha256 });
            Assert.AreEqual("identifier_unknown", never.Refusal?.Code, "a work the log never held keeps the refusal");
        }
        finally
        {
            foreach (var directory in new[] { generation, root })
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task WithoutTheGenerationTheSameRequestsAnswerWithoutTextAndAsAMismatch()
    {
        using var fixture = await TwoBuildsAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Pruned, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var current = (await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier, date = "2030-01-01" })).Result!.Value.GetProperty("states")[0];
        var date = current.GetProperty("applicability_date").GetString()!;
        var original = (await EnvelopeAsync(mount, "/api/v3/as_observed", "as_observed", new { identifier, date, snapshot = fixture.FirstIndexSha256 })).Result!.Value
            .GetProperty("states").EnumerateArray().Single();
        Assert.IsFalse(original.GetProperty("text_held").GetBoolean(), "no held build holds the original state");
        var verified = await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = original.GetProperty("permalink").GetString() });
        Assert.AreEqual("pinned_digest_mismatch", verified.Refusal?.Code, "with the generation pruned, the old permalink is a mismatch naming the current state");
    }
}
