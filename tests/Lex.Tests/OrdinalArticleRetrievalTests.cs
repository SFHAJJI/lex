using Lex.Index;

namespace Lex.Tests;

/// <summary>
/// Article-intent retrieval across the two spellings of one ordinal.
///
/// <para>French drafting writes the first article "Art. 1er." and every later one plainly, so a
/// work whose French expression stores <c>art_1er</c> stores <c>art_1</c> in its German one. The
/// lookup compares an anchor built by substitution against the stored anchor, so "Article 1" of a
/// French text was unreachable and a point-in-time question about it could not be answered at
/// all. Widening retrieval is the whole fix: where an expression genuinely holds both spellings
/// they stay two candidates, and the caller's uniqueness rule refuses.</para>
/// </summary>
public class OrdinalArticleRetrievalTests : IDisposable
{
    private readonly string _db =
        Path.Combine(Path.GetTempPath(), $"lex-ordinal-{Guid.NewGuid():N}.db");

    private static DocRow Doc(string group, string language) =>
        new($"t-pub:{group}:2023-07-01:{language}", "t-pub", group, $"urn:{group}", "LOI",
            language, "2023-07-01", null, "publisher", "2026-01-01T00:00:00Z",
            Withdrawn: false, TextAvailable: true, TextPublic: true,
            RecordSha: Sha($"{group}{language}"), BodySha: Sha($"body{group}{language}"),
            SourceUri: $"https://example.org/{group}", Title: $"texte {group}",
            TitleShort: group, Body: null, PublicationDate: "2023-07-01", StatusNote: null);

    private static ProvisionRow Prov(DocRow doc, int seq, string anchor, string num, string text) =>
        new($"{doc.Key}|{doc.Language}|{doc.ValidFrom}", seq, anchor, $"{doc.Key}#{anchor}",
            "article", num, null, null, null, doc.Title, text, Sha(text));

    private LexIndexReader Build()
    {
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "ordinal",
        };
        // "constitution" is the shipped shape: the French expression numbers its first article
        // "1er" and the German one numbers the same provision "1".
        var frenchConstitution = Doc("constitution", "fr");
        var germanConstitution = Doc("constitution", "de");
        // "dual" is the shape that forbids treating the spellings as equal: seven held works
        // carry art_1 AND art_1er in one French expression, as two different provisions.
        var dual = Doc("dual", "fr");
        // "inserted" carries the suffixes that end in the same two letters and are not ordinals.
        var inserted = Doc("inserted", "fr");
        var docs = new[] { frenchConstitution, germanConstitution, dual, inserted };
        var provisions = new[]
        {
            Prov(frenchConstitution, 0, "art_1er", "Art. 1er.", "le grand duche est un etat"),
            Prov(germanConstitution, 0, "art_1", "Art. 1.", "das grossherzogtum ist ein staat"),
            Prov(dual, 0, "art_1er", "Art. 1er.", "objet et champ d application"),
            Prov(dual, 1, "art_1", "Art. 1.", "disposition transitoire distincte"),
            Prov(inserted, 0, "art_42", "Art. 42.", "la regle originale"),
            Prov(inserted, 1, "art_42ter", "Art. 42ter.", "la regle inseree"),
            Prov(inserted, 2, "art_108quater", "Art. 108quater.", "une autre regle inseree"),
        };
        IndexBuilder.Build(_db, stamp, docs, provisions, [], [], StampSigner.CreateKeyPem());
        return LexIndexReader.Open(_db);
    }

    private static string[] Anchors(SearchExecution execution) => execution.Hits
        .Select(hit => hit.Provision.Anchor).Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal).ToArray();

    private static FilterSet Only(string work, string? language) =>
        new(null, null, null, language, Works: [work]);

    [Fact]
    public void Article_1_reaches_the_french_ordinal_expression()
    {
        using var reader = Build();

        var hits = reader.SearchKeyword(
            "Article 1", Only("constitution", "fr"), 8, fuzzyAuto: true);

        Assert.Equal(["art_1er"], Anchors(hits));
    }

    [Fact]
    public void Article_1er_reaches_the_plainly_numbered_expression()
    {
        using var reader = Build();

        var hits = reader.SearchKeyword(
            "Article 1er", Only("constitution", "de"), 8, fuzzyAuto: true);

        Assert.Equal(["art_1"], Anchors(hits));
    }

    [Fact]
    public void An_expression_holding_both_spellings_returns_both_candidates()
    {
        using var reader = Build();

        var fromPlain = reader.SearchKeyword("Article 1", Only("dual", "fr"), 8, fuzzyAuto: true);
        var fromOrdinal = reader.SearchKeyword("Article 1er", Only("dual", "fr"), 8, fuzzyAuto: true);

        Assert.Equal(["art_1", "art_1er"], Anchors(fromPlain));
        Assert.Equal(["art_1", "art_1er"], Anchors(fromOrdinal));
    }

    /// <summary>
    /// art_42ter and art_108quater also end in "er", and they are articles inserted between
    /// existing ones rather than spellings of 42 and 108. Widening the ordinal must not reach
    /// them: serving art_42ter to a question about Article 42 would be a legal error, not a
    /// ranking one.
    /// </summary>
    [Fact]
    public void Inserted_articles_that_end_in_the_same_letters_are_never_merged()
    {
        using var reader = Build();

        Assert.Equal(["art_42"],
            Anchors(reader.SearchKeyword("Article 42", Only("inserted", "fr"), 8, fuzzyAuto: true)));
        Assert.Empty(
            Anchors(reader.SearchKeyword("Article 108", Only("inserted", "fr"), 8, fuzzyAuto: true)));
    }

    private static string Sha(string value) => Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch { /* temp */ }
    }
}
