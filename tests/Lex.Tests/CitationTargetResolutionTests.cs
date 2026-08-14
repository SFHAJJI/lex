using System.Security.Cryptography;
using System.Text;
using Lex.Index;

namespace Lex.Tests;

public sealed class CitationTargetResolutionTests : IDisposable
{
    private readonly List<string> _files = [];

    [Fact]
    public void Index_canonicalizes_official_same_as_local_citation_identities()
    {
        var db = TempPath();
        var citing = Doc("mixed", "citing-work");
        var aliasUri = "https://data.legilux.public.lu/eli/etat/leg/code/penal";
        var lu = Doc("mixed", "loi-1879-06-18-n1") with
        {
            PublisherMetadata =
            [
                new PublisherMetadataRow(
                    "legilux_same_as", aliasUri, null, "code-penal", aliasUri,
                    CitationIdentity: true),
            ],
        };
        IndexBuilder.Build(db, Stamp("mixed"), [citing, lu],
        [
            Prov(citing, 0, "art_1", "/eli/etat/leg/code/penal/art_454/20210430"),
        ], [], [], StampSigner.CreateKeyPem());
        using var reader = LexIndexReader.Open(db);

        Assert.Equal("mixed:loi-1879-06-18-n1", Assert.Single(reader.CitationsOf(
            LexIndexReader.RidOf(citing), "art_1", 10)).Slug);
        Assert.Equal(["art_1"], reader.CitedBy(
            "mixed:loi-1879-06-18-n1", 10).Select(row => row.Anchor));
    }

    private static Dictionary<string, string> Stamp(string collection) => new()
    {
        ["collection"] = collection, ["tier"] = "A", ["history_begins"] = "publisher",
        ["built_at"] = "2026-08-14T00:00:00Z", ["corpus_commit"] = "test",
    };

    private static DocRow Doc(string collection, string work) => new(
        $"{collection}:{work}:2025-01-01", collection, work, $"urn:{work}", "REG", "en",
        "2025-01-01", null, "publisher", "2026-08-14T00:00:00Z", false, true, true,
        Sha(work), null, "https://example.test/source", work, work, null, "2025-01-01", null);

    private static ProvisionRow Prov(DocRow document, int sequence, string anchor, string href)
    {
        var text = $"Provision {sequence}";
        return new ProvisionRow(LexIndexReader.RidOf(document), sequence, anchor,
            $"{document.Key}#{anchor}", "article", anchor, null, null, null, document.Title,
            text, Sha(text), $"[{{\"href\":\"{href}\",\"text\":\"citation\"}}]");
    }

    private static string Sha(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private string TempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-citations-{Guid.NewGuid():N}.db");
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in _files)
            try { File.Delete(file); } catch { }
    }
}
