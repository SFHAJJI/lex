using System.Text.Json.Nodes;
using Lex.Derive;

namespace Lex.Tests;

public sealed class DerivationGenerationTests : IDisposable
{
    private const string CorpusCommit = "cccccccccccccccccccccccccccccccccccccccc";
    private const string IngesterCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DeriverCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DeriverTree = "dddddddddddddddddddddddddddddddddddddddd";
    private const string ManifestDigest =
        "1111111111111111111111111111111111111111111111111111111111111111";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-generation-{Guid.NewGuid():N}");

    public DerivationGenerationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void One_top_level_manifest_preserves_other_publishers_and_has_no_self_commit()
    {
        Write("lu-legilux", ["akn-lu/1"]);
        var path = Path.Combine(_root, DerivationGeneration.FileName);
        var first = JsonNode.Parse(File.ReadAllText(path))!;
        var preserved = first["publishers"]!["lu-legilux"]!.ToJsonString();

        Write("eu-eurlex", ["xhtml-eu/1", "fmx4-eu/1"]);

        var root = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(DerivationGeneration.PreviousSchemaId,
            root["schema"]!.GetValue<string>());
        Assert.Equal(["eu-eurlex", "lu-legilux"],
            root["publishers"]!.AsObject().Select(item => item.Key));
        Assert.Equal(preserved,
            root["publishers"]!["lu-legilux"]!.ToJsonString());
        Assert.DoesNotContain("articles_commit", File.ReadAllText(path),
            StringComparison.Ordinal);
        var eu = DerivationGeneration.ReadPublisher(_root, "eu-eurlex");
        Assert.Equal(["fmx4-eu/1", "xhtml-eu/1"], eu.Profiles);
        Assert.DoesNotContain("reviewed_configuration", File.ReadAllText(path),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tampered_profile_identity_is_rejected()
    {
        Write("eu-eurlex", ["xhtml-eu/1"]);
        var path = Path.Combine(_root, DerivationGeneration.FileName);
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        root["publishers"]!["eu-eurlex"]!["profiles"] =
            new JsonArray("tampered/1");
        File.WriteAllText(path, root.ToJsonString());

        Assert.Throws<InvalidDataException>(() =>
            DerivationGeneration.ReadPublisher(_root, "eu-eurlex"));
    }

    [Fact]
    public void Wrong_publisher_identity_is_rejected()
    {
        Write("eu-eurlex", ["xhtml-eu/1"]);
        var path = Path.Combine(_root, DerivationGeneration.FileName);
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        root["publishers"]!["eu-eurlex"]!["collection"] = "lu-legilux";
        File.WriteAllText(path, root.ToJsonString());

        Assert.Throws<InvalidDataException>(() =>
            DerivationGeneration.ReadPublisher(_root, "eu-eurlex"));
    }

    [Fact]
    public void Provenance_identities_require_exact_full_digests()
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdatePublisher(
                _root, "eu-eurlex", "short", ManifestDigest,
                IngesterCommit, DeriverCommit, DeriverTree,
                ["xhtml-eu/1"]));
        Assert.Throws<InvalidDataException>(() =>
            UpdatePublisher(
                _root, "eu-eurlex", CorpusCommit, ManifestDigest,
                IngesterCommit, DeriverCommit,
                "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
                ["xhtml-eu/1"]));
    }

    [Fact]
    public void Canon_two_publisher_cannot_be_silently_downgraded_to_canon_one()
    {
        UpdatePublisher(
            _root, "lu-legilux", CorpusCommit, ManifestDigest,
            IngesterCommit, DeriverCommit, DeriverTree,
            [AknLuProfileV3.ProfileId], DerivationGeneration.Canon2);
        var path = Path.Combine(_root, DerivationGeneration.FileName);
        var before = File.ReadAllBytes(path);

        var error = Assert.Throws<InvalidDataException>(() =>
            UpdatePublisher(
                _root, "lu-legilux", CorpusCommit, ManifestDigest,
                IngesterCommit, DeriverCommit, DeriverTree,
                [AknLuProfileV2.ProfileId]));

        Assert.Contains("canon/2", error.Message, StringComparison.Ordinal);
        Assert.Contains("canon/1", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(DerivationGeneration.Canon2,
            DerivationGeneration.ReadPublisher(_root, "lu-legilux").ArticlesCanon);
    }

    private void Write(string publisher, IEnumerable<string> profiles) =>
        UpdatePublisher(
            _root, publisher, CorpusCommit, ManifestDigest,
            IngesterCommit, DeriverCommit, DeriverTree,
            profiles);

    private static void UpdatePublisher(
        string root,
        string publisher,
        string corpusCommit,
        string corpusManifestSha256,
        string ingesterCodeCommit,
        string deriverCodeCommit,
        string deriverTreeId,
        IEnumerable<string> profiles,
        string articlesCanon = DerivationGeneration.Canon1)
    {
        Directory.CreateDirectory(Path.Combine(root, publisher));
        DerivationGeneration.UpdatePublisherWithLocksHeld(
            root, publisher, corpusCommit, corpusManifestSha256,
            ingesterCodeCommit, deriverCodeCommit, deriverTreeId,
            profiles, articlesCanon);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
