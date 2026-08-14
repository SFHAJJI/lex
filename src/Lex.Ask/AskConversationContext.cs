namespace Lex.Ask;

/// <summary>
/// Server-owned subject authority carried between assistant turns. It contains only canonical
/// identifiers and held anchors; prior user or assistant prose is never reinterpreted as authority.
/// </summary>
public sealed record AskResolvedSubjectContext(
    string Work,
    string? ArticleAnchor = null,
    string? ExactLexId = null);

public sealed record AskConversationContext(
    IReadOnlyList<AskResolvedSubjectContext> Subjects,
    string? ArticleNumber = null);

public enum AskConversationContextDisposition
{
    Preserve,
    Replace,
    Clear,
}
