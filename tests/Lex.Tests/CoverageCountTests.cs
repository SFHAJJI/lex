using Lex.Index;

namespace Lex.Tests;

/// <summary>
/// The docs table holds one row per language expression, so a bilingual version is two rows
/// with one version-level key. Coverage must count versions by distinct key, not by row: the
/// golden fixture is single-language and structurally cannot catch the difference, which is
/// how the site shipped an expression count labelled "dated versions".
/// </summary>
public sealed class CoverageCountTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-coverage-{Guid.NewGuid():N}");
    private readonly LexIndexReader _reader;

    public CoverageCountTests()
    {
        Directory.CreateDirectory(_dir);
        var db = Path.Combine(_dir, "index-t-pub.db");
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["publisher_name"] = "Test Publisher", ["tier"] = "A",
            ["history_begins"] = "publisher", ["built_at"] = "2026-01-01T00:00:00Z",
            ["corpus_commit"] = "covtest", ["attribution"] = "Test attribution.",
        };
        DocRow Row(string key, string group, string language, string from, string kind, bool text) =>
            new(key, "t-pub", group, $"urn:{group}", kind, language, from, null,
                "publisher", "2026-01-01T00:00:00Z", Withdrawn: false, TextAvailable: text,
                TextPublic: text, RecordSha: Sha(key + language), BodySha: text ? Sha("body" + key + language) : null,
                SourceUri: $"https://example.org/{group}/{from}", Title: group, TitleShort: group,
                Body: null, PublicationDate: from, StatusNote: null);

        // One bilingual version (two rows, one key), a later single-language version without
        // text, and a same-date pair on w2 that only the version key can tell apart (the D41
        // collision suffix). Five expression rows over four dated versions.
        var docs = new[]
        {
            Row("t-pub:w1:2020-01-01", "w1", "fr", "2020-01-01", "LOI", true),
            Row("t-pub:w1:2020-01-01", "w1", "de", "2020-01-01", "LOI", true),
            Row("t-pub:w1:2022-01-01", "w1", "fr", "2022-01-01", "LOI", false),
            Row("t-pub:w2:2020-01-01", "w2", "fr", "2020-01-01", "LOI", true),
            Row("t-pub:w2:2020-01-01--02", "w2", "fr", "2020-01-01", "LOI", false),
        };
        IndexBuilder.Build(db, stamp, docs, [], [], [], StampSigner.CreateKeyPem());
        _reader = LexIndexReader.Open(db);
    }

    [Fact]
    public void Coverage_counts_versions_by_key_and_expressions_by_row()
    {
        var c = _reader.Coverage();
        Assert.Equal(2, c.Groups);
        Assert.Equal(5, c.Rows);              // language expressions
        Assert.Equal(4, c.Versions);          // dated versions: the bilingual one counts once
        Assert.Equal(3, c.TextServed);        // expression rows with text_public
        Assert.Equal(2, c.VersionsWithText);  // versions with text: w1 2020 and w2 2020

        var kind = Assert.Single(c.Kinds);
        Assert.Equal(4, kind.Versions);
        Assert.Equal(2, kind.WithText);
    }

    [Fact]
    public void Change_totals_distinguish_same_date_versions_of_one_work()
    {
        var (works, versions) = _reader.ChangeTotals("2020-01-01", "2020-01-01", null);
        Assert.Equal(2, works);
        // w1 moved once (its bilingual version is one version) and w2 twice: the suffix pair
        // shares work and date, so any group-plus-date key would collapse it to one.
        Assert.Equal(3, versions);
    }

    private static string Sha(string s) => Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));

    public void Dispose()
    {
        _reader.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
