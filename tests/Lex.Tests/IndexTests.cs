using Lex.Index;

namespace Lex.Tests;

public class IndexTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"lex-test-{Guid.NewGuid():N}.db");

    private static DocRow Row(string key, string group, string from, string? to, string kind = "REG", string? title = null) =>
        new(key, "t-pub", group, $"urn:{group}", kind, "en", from, to, "publisher",
            "2026-08-01T00:00:00Z", Withdrawn: false, TextAvailable: false, TextPublic: false,
            RecordSha: "abc", BodySha: null, SourceUri: "https://example.org", Title: title ?? group,
            TitleShort: title ?? group, Body: null, PublicationDate: from, StatusNote: null);

    private LexIndexReader Build()
    {
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
        };
        var docs = new[]
        {
            Row("t-pub:w1:2020-01-01", "w1", "2020-01-01", "2021-12-31", title: "first thing"),
            Row("t-pub:w1:2022-01-01", "w1", "2022-01-01", null, title: "first thing revised"),
            Row("t-pub:w2:2019-06-01", "w2", "2019-06-01", null, kind: "DIR", title: "second thing"),
        };
        IndexBuilder.Build(_db, stamp, docs, [], [], StampSigner.CreateKeyPem());
        return LexIndexReader.Open(_db);
    }

    [Fact]
    public void Signature_round_trip_is_valid()
    {
        using var r = Build();
        Assert.True(r.SignatureValid);
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
    public void Search_filters_before_ranking()
    {
        using var r = Build();
        var all = r.Search("thing", FilterSet.All, 10);
        Assert.True(all.Count >= 3);
        var dirHits = r.Search("thing", new FilterSet(null, null, "DIR", null), 10);
        Assert.All(dirHits, h => Assert.Equal("DIR", h.Doc.Kind));
    }

    [Fact]
    public void Unknown_schema_is_refused_explicitly()
    {
        var stamp = new Dictionary<string, string> { ["collection"] = "t-pub" };
        IndexBuilder.Build(_db, stamp, [], [], [], null);
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
