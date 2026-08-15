using System.Security.Cryptography;
using System.Text;
using Lex.Index;
using Xunit;

namespace Lex.Tests;

/// <summary>
/// Search results carried an empty snippet on every hit, for both publishers, since the schema was
/// written. The provision index is <c>fts5(..., content='')</c>, and SQLite's <c>snippet()</c>
/// returns NULL on a contentless table because there is no stored text to cut from. Nothing was
/// broken at ingest and nothing was missing from the corpus: the window simply could never be
/// produced by the query that asked for it.
///
/// <para>These pin the replacement, which cuts the window from the content-addressed text instead.</para>
/// </summary>
public sealed class SnippetTests : IDisposable
{
    private const string Body =
        "Les établissements de crédit sont soumis à la surveillance prudentielle de la Commission. "
        + "Cette surveillance porte sur les fonds propres, la liquidité et la gouvernance interne. "
        + "Un établissement qui ne respecte pas ces exigences encourt les sanctions prévues.";

    private readonly List<string> _files = [];

    private LexIndexReader Build(string body)
    {
        var db = Path.Combine(Path.GetTempPath(), $"lex-snip-{Guid.NewGuid():N}.db");
        _files.Add(db);
        var key = "lu-legilux:loi-test:2024-01-01";
        var doc = new DocRow(key, "lu-legilux", "loi-test", "http://example.invalid/loi-test",
            "LOI", "fr", "2024-01-01", null, "publisher", "2026-01-01T00:00:00Z", false, true, true,
            "record-sha", "body-sha", "https://example.invalid", "Loi de test", "Loi de test",
            null, "2024-01-01", null);
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var provision = new ProvisionRow($"{key}|fr|2024-01-01", 0, "art_1", $"{key}#art_1",
            "article", "Art. 1er.", null, null, null, "Loi de test", body, sha);
        IndexBuilder.Build(db, new Dictionary<string, string>
        {
            ["collection"] = "lu-legilux",
            ["jurisdiction"] = "LU",
            ["built_at"] = "2026-08-13T00:00:00Z",
            ["corpus_commit"] = "test",
        }, [doc], [provision], [], [], null);
        return LexIndexReader.Open(db);
    }

    private static string ShaOf(string body) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    [Fact]
    public void A_snippet_is_cut_around_the_matched_term()
    {
        using var reader = Build(Body);

        var snippet = reader.SnippetFor(ShaOf(Body), "gouvernance");

        Assert.NotNull(snippet);
        Assert.Contains("gouvernance", snippet);
        // Not simply the head of the provision: the term sits in the second sentence, so a window
        // centred on it must have dropped the opening words.
        Assert.DoesNotContain("Les établissements de crédit sont soumis", snippet);
        Assert.StartsWith("...", snippet);
    }

    // The index folds diacritics (unicode61 remove_diacritics), so a reader who types "credit"
    // matches text spelling it "crédit". The snippet has to fold the same way or the window would
    // be cut from the head while the term sits elsewhere, silently disagreeing with the ranking.
    [Fact]
    public void A_query_without_diacritics_still_locates_accented_wording()
    {
        using var reader = Build(Body);

        // Deliberately a term in the LAST sentence. An earlier draft asked for accented wording
        // that also opens the provision, so the test passed with folding removed: no term matched,
        // the fallback returned the opening words, and those happened to contain the phrase.
        var snippet = reader.SnippetFor(ShaOf(Body), "prevues");

        Assert.NotNull(snippet);
        // Cut from the publisher's bytes, not from the folded copy used to locate the position.
        Assert.Contains("prévues", snippet);
        // Proves a term was actually located rather than the opening words being returned.
        Assert.DoesNotContain("Les établissements de crédit sont soumis", snippet);
    }

    // A provision whose extraction produced nothing is not a runtime provision. Letting it reach
    // the index would make every downstream consumer decide whether a blank row is legal text.
    [Fact]
    public void A_provision_with_no_text_is_refused_before_snippet_storage()
    {
        var error = Assert.Throws<InvalidDataException>(() => Build("   "));

        Assert.Contains("no non-whitespace body text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_text_reference_yields_no_snippet()
    {
        using var reader = Build(Body);

        Assert.Null(reader.SnippetFor(new string('a', 64), "gouvernance"));
    }

    // A public reader entry point rejects bad arguments instead of returning null for them, so a
    // caller can never confuse "this provision holds no text" with "you passed nothing".
    [Fact]
    public void Bad_arguments_are_rejected_rather_than_reported_as_an_absent_snippet()
    {
        using var reader = Build(Body);

        Assert.Throws<ArgumentNullException>(() => reader.SnippetFor(null!, "gouvernance"));
        Assert.Throws<ArgumentNullException>(() => reader.SnippetFor(ShaOf(Body), null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.SnippetFor(ShaOf(Body), "gouvernance", 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.SnippetFor(ShaOf(Body), "gouvernance", -1));
    }

    // A term the body does not contain still returns the provision's opening words rather than
    // nothing: the hit is real, it matched on the title, number or heading, and a reader scanning
    // results is better served by the first line of the article than by a blank cell.
    [Fact]
    public void A_term_matched_outside_the_body_falls_back_to_the_opening_words()
    {
        using var reader = Build(Body);

        var snippet = reader.SnippetFor(ShaOf(Body), "zzzzzz");

        Assert.NotNull(snippet);
        Assert.StartsWith("Les établissements", snippet);
        Assert.False(snippet.StartsWith("...", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (var file in _files)
            try { File.Delete(file); } catch (IOException) { }
    }
}
