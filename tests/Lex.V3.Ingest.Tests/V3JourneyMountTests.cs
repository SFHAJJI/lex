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
    private const string LicenceBlockedVariable = "V3_WRITE_LICENCE_BLOCKED_MOUNT";
    private const string NonAdmittingLicence = "non_admitting_licence_scl";

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

    /// <summary>
    /// Writes the licence-blocked mount the launch contract's "one licence-blocked journey" runs on,
    /// when <c>V3_WRITE_LICENCE_BLOCKED_MOUNT</c> names a directory: the fixture mount with its member's
    /// rights recorded as a licence that does not admit redistribution. Its <c>journey-mount.json</c>
    /// names that disposition and the opening words of every article of the state, which no page may
    /// show.
    /// </summary>
    [TestMethod]
    public async Task TheLicenceBlockedMountIsTheFixtureMountWithANonAdmittingLicence()
    {
        var target = Environment.GetEnvironmentVariable(LicenceBlockedVariable);
        if (string.IsNullOrWhiteSpace(target))
        {
            Assert.Inconclusive($"{LicenceBlockedVariable} names no directory, so no licence-blocked mount is written.");
        }

        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.SetMemberRightsDispositionAsync(NonAdmittingLicence);
        var articles = fixture.ArticlesOfOwnState();
        Assert.IsNotEmpty(articles);
        Directory.CreateDirectory(target);
        foreach (var name in new[] { V3CorpusMount.IndexFileName, V3CorpusMount.CapabilityManifestFileName, V3CorpusMount.CorpusFileName })
        {
            File.Copy(Path.Combine(fixture.Directory, name), Path.Combine(target, name), overwrite: true);
        }

        using (var mount = await V3CorpusMount.OpenAsync(target, CancellationToken.None))
        {
            Assert.IsNotNull(mount, "the licence-blocked directory mounts through the API's own verifier: the licence withholds text, not the mount.");
        }

        // The opening words of an article's body: its label ("Art. 15.") and paragraph marker ("(1)") are
        // left off, since a page may show the label and renders it apart from the text.
        static string Opening(string text)
        {
            var words = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            words = System.Text.RegularExpressions.Regex.Replace(words, @"^Art\.\s*[0-9A-Za-z-]+\.\s*", string.Empty);
            words = System.Text.RegularExpressions.Regex.Replace(words, @"^\(\d+\)\s*", string.Empty);
            return words.Length <= 40 ? words : words[..40];
        }

        var manifest = JsonSerializer.Serialize(new
        {
            schema = "lex-v3-journey-mount/1",
            note = "the test fixture's Luxembourg mount (THE MOUNT IS A FIXTURE) with its member's rights recorded as a non-admitting licence, written for the licence-blocked journey",
            corpus_sha256 = fixture.CorpusSha256,
            index_sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(target, V3CorpusMount.IndexFileName)))),
            work_key = fixture.WorkKey,
            rights_disposition = NonAdmittingLicence,
            withheld_openings = articles.Select(static article => Opening(article.Text)).Where(static opening => opening.Length >= 12).Distinct(StringComparer.Ordinal).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(target, "journey-mount.json"), manifest + "\n");
    }
}
