namespace Lex.Ask;

/// <summary>
/// Exact publisher-owned source that established a deterministic subject identity. This is
/// authority provenance, not legal-text evidence and never enters the planner prompt.
/// </summary>
public sealed record AskSubjectAuthoritySource(
    string Kind,
    string Identifier,
    string Segment,
    string Language,
    string SourceUri);

/// <summary>
/// Server-owned subject authority carried between assistant turns. It contains only canonical
/// identifiers and held anchors; prior user or assistant prose is never reinterpreted as authority.
/// </summary>
public sealed record AskResolvedSubjectContext(
    string Work,
    string? ArticleAnchor = null,
    string? ExactLexId = null,
    AskSubjectAuthoritySource? AuthoritySource = null);

public sealed record AskConversationContext(
    IReadOnlyList<AskResolvedSubjectContext> Subjects,
    string? ArticleNumber = null);

public enum AskConversationContextDisposition
{
    Preserve,
    Replace,
    Clear,
}
