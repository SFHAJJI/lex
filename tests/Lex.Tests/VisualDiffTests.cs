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
