using Lex.Index;

namespace Lex.Tests;

public class IndexTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"lex-test-{Guid.NewGuid():N}.db");

    private static DocRow Row(string key, string group, string from, string? to, string kind = "REG", string? title = null, bool text = false) =>
        new(key, "t-pub", group, $"urn:{group}", kind, "en", from, to, "publisher",
            "2026-08-01T00:00:00Z", Withdrawn: false, TextAvailable: text, TextPublic: text,
            RecordSha: "abc", BodySha: null, SourceUri: "https://example.org", Title: title ?? group,
            TitleShort: title ?? group, Body: null, PublicationDate: from, StatusNote: null);

    private static ProvisionRow Prov(DocRow d, int seq, string anchor, string text, string? num = null) =>
        new(Rid: $"{d.Key}|{d.Language}|{d.ValidFrom}", Seq: seq, Anchor: anchor,
            ProvisionId: $"{d.Key}#{anchor}", PType: "article", Num: num ?? anchor, Heading: null,
            Path: null, ArticleValidFrom: null, WorkTitle: d.Title, TextMd: text,
            TextSha: Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))));

    private LexIndexReader Build()
    {
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
        };
        var docs = new[]
        {
            Row("t-pub:w1:2020-01-01", "w1", "2020-01-01", "2021-12-31", title: "first thing", text: true),
            Row("t-pub:w1:2022-01-01", "w1", "2022-01-01", null, title: "first thing revised", text: true),
            Row("t-pub:w2:2019-06-01", "w2", "2019-06-01", null, kind: "DIR", title: "second thing", text: true),
        };
        var provisions = new[]
        {
            Prov(docs[0], 0, "art_1", "the thing shall apply everywhere"),
            Prov(docs[0], 1, "art_2", "penalties for the thing are mild"),
            Prov(docs[1], 0, "art_1", "the thing shall apply everywhere, revised"),
            Prov(docs[2], 0, "art_1", "a different directive thing entirely"),
        };
        IndexBuilder.Build(_db, stamp, docs, provisions, [], [], StampSigner.CreateKeyPem());
        return LexIndexReader.Open(_db);
    }

    [Fact]
    public void Signature_round_trip_is_valid()
    {
        using var r = Build();
        Assert.True(r.SignatureValid);
    }

    // A signature over the stamp's metadata says nothing about the text the index serves.
    // The stamp therefore commits to a digest of the content, and this is the test that the
    // commitment is real: edit one article's text in the database and the recomputed digest
    // must stop matching the signed one. Without it, "every served hash is attributable"
    // would be a claim with no mechanism behind it.
    [Fact]
    public void Editing_article_text_breaks_the_content_digest()
    {
        string signed;
        using (var r = Build())
        {
            Assert.True(r.SignatureValid);
            signed = r.Stamp["content_digest"];
            Assert.Equal(signed, r.ComputeContentDigest());
        }

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE provisions SET text_sha = 'tampered' WHERE seq = 0";
            Assert.True(cmd.ExecuteNonQuery() > 0);
        }

        using (var r = LexIndexReader.Open(_db))
        {
            Assert.True(r.SignatureValid);                      // the stamp itself is untouched
            Assert.NotEqual(signed, r.ComputeContentDigest());  // but the contents no longer match it
        }
    }

    [Fact]
    public void AsOf_stabs_the_correct_version_and_distinguishes_refusals()
    {
        using var r = Build();
        Assert.Equal("t-pub:w1:2020-01-01", r.AsOf("w1", new DateOnly(2021, 6, 1), FilterSet.All)!.Key);
        Assert.Equal("t-pub:w1:2022-01-01", r.AsOf("w1", new DateOnly(2024, 1, 1), FilterSet.All)!.Key);
        // no_version_for_date vs unknown_work
        Assert.Null(r.AsOf("w1", new DateOnly(1999, 1, 1), FilterSet.All));
        Assert.True(r.WorkExists("w1"));
        Assert.False(r.WorkExists("nope"));
        // work-level and version-level lex_ids both resolve (§9)
        Assert.NotNull(r.AsOf("t-pub:w1", new DateOnly(2021, 6, 1), FilterSet.All));
    }

    [Fact]
    public void InForceOn_is_computed_from_dates_and_deduplicated_by_work()
    {
        using var r = Build();
        var (rows, total) = r.InForceOn(new DateOnly(2023, 1, 1), FilterSet.All, 50, 0);
        Assert.Equal(2, total);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.Key == "t-pub:w1:2022-01-01");     // the version valid on that date
        var (dirOnly, dirTotal) = r.InForceOn(new DateOnly(2023, 1, 1), new FilterSet(null, null, "DIR", null), 50, 0);
        Assert.Equal(1, dirTotal);
        Assert.Equal("w2", dirOnly.Single().GroupKey);
    }

    [Fact]
    public void Search_filters_before_ranking_and_hits_are_provision_level()
    {
        using var r = Build();
        var all = r.Search("thing", FilterSet.All, 10);
        Assert.True(all.Count >= 3);
        Assert.All(all, h => Assert.False(string.IsNullOrEmpty(h.Prov.Anchor)));
        var dirHits = r.Search("thing", new FilterSet(null, null, "DIR", null), 10);
        Assert.All(dirHits, h => Assert.Equal("DIR", h.Doc.Kind));
    }

    [Fact]
    public void Provisions_round_trip_and_body_reconstruction()
    {
        using var r = Build();
        var d = r.AsOf("w1", new DateOnly(2020, 6, 1), FilterSet.All)!;
        var provs = r.Provisions(LexIndexReader.RidOf(d));
        Assert.Equal(2, provs.Count);
        Assert.Equal(["art_1", "art_2"], provs.Select(p => p.Anchor));
        var body = r.BuildBody(d)!;
        Assert.Contains("the thing shall apply everywhere", body);
        Assert.Contains("penalties for the thing are mild", body);
        Assert.Null(d.Body);   // never stored on the row; reconstruction is explicit
    }

    [Fact]
    public void Unknown_schema_is_refused_explicitly()
    {
        var stamp = new Dictionary<string, string> { ["collection"] = "t-pub" };
        IndexBuilder.Build(_db, stamp, [], [], [], [], null);
        // sabotage the schema stamp
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE stamp SET v='lex-index/999' WHERE k='schema'";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => LexIndexReader.Open(_db));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch { /* temp file */ }
    }
}
