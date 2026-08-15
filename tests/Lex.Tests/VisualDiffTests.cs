using Lex.Index;
using Lex.Web;

namespace Lex.Tests;

public class VisualDiffTests
{
    [Fact]
    public void Timeline_semantics_are_explicit_and_keep_legacy_eurlex_artifacts_honest()
    {
        Assert.True(Fragments.UsesPublisherVersionDates("new-publisher",
            new Dictionary<string, string> { ["timeline_semantics"] = "official_consolidation_state" }));
        Assert.True(Fragments.UsesPublisherVersionDates("eu-eurlex", new Dictionary<string, string>()));
        Assert.False(Fragments.UsesPublisherVersionDates("lu-legilux", new Dictionary<string, string>()));
    }

    [Fact]
    public void Legal_markdown_is_formatted_but_never_executes_source_html()
    {
        var html = Fragments.RenderLegalMarkdown("**Article 1.** Safe text\n\n<script>alert('unsafe')</script>");

        Assert.Contains("<strong>Article 1.</strong>", html);
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Legal_markdown_tables_are_keyboard_scrollable()
    {
        var html = Fragments.RenderLegalMarkdown("| article | wording |\n| --- | --- |\n| 1 | text |");

        Assert.Contains("<table tabindex=\"0\" aria-label=\"Scrollable legal table\">", html);
    }

    [Fact]
    public void Legal_markdown_hides_only_profile_emphasis_that_commonmark_cannot_parse()
    {
        var html = Fragments.RenderLegalMarkdown(
            "amount follows:*TREA = max{U-TREA; x Â· S-TREA}*where:TREA is defined; A * B remains");

        Assert.Contains("follows:TREA = max", html);
        Assert.Contains("where:TREA", html);
        Assert.Contains("A * B", html);
        Assert.DoesNotContain("*TREA", html);
    }

    [Fact]
    public void Structural_legal_labels_do_not_expose_markdown_delimiters()
    {
        Assert.Equal("Chapitre 2bis — Mesures", Fragments.PlainLegalLabel("**Chapitre 2*bis*** — **Mesures**"));
    }

    [Fact]
    public void Inline_legal_labels_render_emphasis_without_paragraph_or_executable_html()
    {
        var html = Fragments.RenderLegalInline("Article 22 *bis* <script>alert(1)</script>");

        Assert.Contains("Article 22 <em>bis</em>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<p>", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Static_version_history_has_touch_friendly_named_date_links()
    {
        static DocRow Version(string date) => new(
            $"lu-legilux:work:{date}", "lu-legilux", "work", "urn:work", "LOI", "fr",
            date, null, "publisher", "2026-08-08T00:00:00Z", false, true, true,
            "record", "body", "https://example.test", "Law", "Law", null, date, null);

        var html = Fragments.VersionRail("lu-legilux", "work",
            [Version("2020-01-01"), Version("2021-01-01"), Version("2022-01-01")], "2021-01-01");

        Assert.Contains("<details class=\"railversions\">", html);
        Assert.Contains("aria-label=\"Read version 2020-01-01\"", html);
        Assert.Contains("aria-current=\"date\"", html);
    }

    [Fact]
    public void Missing_collection_text_explains_that_the_record_is_a_folder_not_a_failed_law()
    {
        var collection = new DocRow(
            "lu-legilux:recueil-tax:2026-01-01", "lu-legilux", "recueil-tax",
            "urn:recueil-tax", "RECUEIL", "fr", "2026-01-01", null, "publisher",
            "2026-01-02T00:00:00Z", false, false, false, "record", null,
            "https://example.test/recueil", "Tax collection", "Tax collection", null,
            "applicable", null, null, null, null, null, null);

        var html = Fragments.MissingTextBox(collection, publisherTextGateOpen: true);

        Assert.Contains("Thematic collection", html);
        Assert.Contains("not one legal instrument", html);
        Assert.DoesNotContain("Provision text not available", html);
    }

    [Fact]
    public void Comparison_text_gap_status_is_independent_of_version_order()
    {
        static DocRow Document(string key, bool available) => new(
            key, "test", "work", "urn:work", "REG", "en", "2026-01-01", null,
            "publisher", "2026-01-02T00:00:00Z", false, available, false,
            "record", null, "https://example.test/source", "Law", "Law", null,
            "2026-01-01", null);
        var available = Document("test:work:available", available: true);
        var unavailable = Document("test:work:unavailable", available: false);
        var db = Path.Combine(Path.GetTempPath(), $"lex-gap-{Guid.NewGuid():N}.db");
        try
        {
            IndexBuilder.Build(db, new Dictionary<string, string>
            {
                ["collection"] = "test", ["text_public"] = "false",
            }, [available, unavailable], [], [], [], null);
            using (var reader = LexIndexReader.Open(db))
            {
                Assert.Equal("text_not_available",
                    Fragments.ComparisonTextStatus(reader, available, unavailable));
                Assert.Equal("text_not_available",
                    Fragments.ComparisonTextStatus(reader, unavailable, available));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(db)) File.Delete(db);
        }
    }

    [Fact]
    public void Presentation_diff_keeps_new_wording_before_replaced_wording()
    {
        var html = Fragments.RenderDiff("Article 1\nold wording\nEnd", "Article 1\nnew wording\nEnd");

        var added = html.IndexOf("+ new wording", StringComparison.Ordinal);
        var removed = html.IndexOf("− old wording", StringComparison.Ordinal);
        Assert.True(added >= 0 && removed > added, html);
    }

    [Fact]
    public void Presentation_diff_preserves_inserted_empty_lines()
    {
        var html = Fragments.RenderDiff("Article 1\nEnd", "Article 1\n\nEnd");

        Assert.Contains("color:var(--ok)\">+ </span>", html);
    }

    [Fact]
    public void Presentation_diff_escapes_extracted_text()
    {
        var html = Fragments.RenderDiff("safe", "<script>alert(1)</script>");

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Presentation_diff_retains_the_large_change_guard()
    {
        var oldText = string.Join('\n', Enumerable.Range(0, 1_501).Select(i => $"old {i}"));
        var newText = string.Join('\n', Enumerable.Range(0, 1_501).Select(i => $"new {i}"));

        var html = Fragments.RenderDiff(oldText, newText);

        Assert.Contains("Change too large for a useful line-by-line page", html);
    }
}
