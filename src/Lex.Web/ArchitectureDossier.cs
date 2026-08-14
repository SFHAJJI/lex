using Markdig;

namespace Lex.Web;

/// <summary>
/// Renders the reviewed architecture dossier embedded in the same immutable image as the code it
/// describes. HTML in the Markdown stays disabled: the dossier is documentation, not another
/// executable presentation surface.
/// </summary>
internal static class ArchitectureDossier
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .DisableHtml()
        .Build();

    private static readonly Lazy<string> Rendered = new(() =>
    {
        using var stream = typeof(ArchitectureDossier).Assembly
            .GetManifestResourceStream("Lex.Web.architecture-dossier.md")
            ?? throw new InvalidOperationException("Embedded architecture-dossier.md is missing.");
        using var reader = new StreamReader(stream);
        var markdown = reader.ReadToEnd();

        // PageShell owns the canonical page heading. Keep the document's first heading useful in
        // GitHub without rendering two h1 elements on the public page.
        var firstBreak = markdown.IndexOf('\n');
        if (firstBreak >= 0 && markdown.AsSpan(0, firstBreak).StartsWith("# "))
            markdown = markdown[(firstBreak + 1)..];

        return Markdown.ToHtml(markdown, Pipeline)
            .Replace("<table>",
                "<table tabindex=\"0\" aria-label=\"Scrollable architecture table\">",
                StringComparison.Ordinal)
            .Replace("<pre>",
                "<pre tabindex=\"0\" aria-label=\"Scrollable architecture diagram or code\">",
                StringComparison.Ordinal);
    });

    public static string Html => Rendered.Value;
}
