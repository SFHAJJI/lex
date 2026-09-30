using System.Text.Json;
using Lex.V3.Api;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Writes a mount a browser journey can put beside a local <c>Lex.V3.Api</c>: the test fixture's
/// Luxembourg index, capability manifest and corpus, opened once through the API's own
/// <see cref="V3CorpusMount"/> to prove it mounts, and a <c>journey-mount.json</c> naming the digests
/// a page answered from it must show. It writes only when <c>V3_WRITE_JOURNEY_MOUNT</c> names a
/// directory (the render pattern of the censuses), because the first real mount is still blocked and
/// the journey needs something to answer from; otherwise it is inconclusive and writes nothing.
/// </summary>
[TestClass]
public sealed class V3JourneyMountTests
{
    private const string WriteVariable = "V3_WRITE_JOURNEY_MOUNT";

    [TestMethod]
    public async Task TheJourneyMountIsTheFixtureMountWrittenWhereTheJourneyAsks()
    {
        var target = Environment.GetEnvironmentVariable(WriteVariable);
        if (string.IsNullOrWhiteSpace(target))
        {
            Assert.Inconclusive($"{WriteVariable} names no directory, so no journey mount is written.");
        }

        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        Directory.CreateDirectory(target);
        foreach (var name in new[] { V3CorpusMount.IndexFileName, V3CorpusMount.CapabilityManifestFileName, V3CorpusMount.CorpusFileName })
        {
            File.Copy(Path.Combine(fixture.Directory, name), Path.Combine(target, name), overwrite: true);
        }

        using (var mount = await V3CorpusMount.OpenAsync(target, CancellationToken.None))
        {
            Assert.IsNotNull(mount, "the written directory mounts through the API's own verifier.");
        }

        var manifest = JsonSerializer.Serialize(new
        {
            schema = "lex-v3-journey-mount/1",
            note = "the test fixture's Luxembourg mount (THE MOUNT IS A FIXTURE), written for a local browser journey",
            corpus_sha256 = fixture.CorpusSha256,
            index_sha256 = fixture.IndexSha256,
            work_key = fixture.WorkKey,
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(target, "journey-mount.json"), manifest + "\n");
    }
}
