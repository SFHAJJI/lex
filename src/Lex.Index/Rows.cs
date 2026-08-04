namespace Lex.Index;

/// <summary>
/// One row per expression current-state (generic "versioned document" vocabulary —
/// this layer knows nothing about law or any publisher, per fitness rule F1).
/// </summary>
public sealed record DocRow(
    string Key,                 // lex_id (opaque here)
    string Collection,          // e.g. a publisher id (opaque here)
    string GroupKey,            // work slug (opaque grouping key)
    string GroupIdentifier,     // work identifier (opaque)
    string? Kind,               // document type code (opaque)
    string Language,
    string ValidFrom,           // ISO date
    string? ValidTo,            // ISO date or null = open
    string ValidTimeSource,
    string ObservedFrom,        // ISO instant
    bool Withdrawn,
    bool TextAvailable,
    bool TextPublic,
    string? RecordSha,
    string? BodySha,
    string? SourceUri,
    string? Title,
    string? TitleShort,
    string? Body,               // reconstructed from provisions on demand (never stored in lex-index/2)
    string? PublicationDate,
    string? StatusNote,
    // Which extraction profile produced this version's text. It is a confidence marker, not
    // trivia: text from publisher markup and text inferred from a page-description format are
    // not the same claim, and one law is routinely the first on some dates and the second on
    // others. Null when the version carries no text at all.
    string? Profile = null);

/// <summary>One provision (article/annex) of one document version — the retrieval unit.</summary>
public sealed record ProvisionRow(
    string Rid,                 // parent doc rid (key|language|valid_from)
    int Seq,                    // document order
    string Anchor,              // publisher-minted fragment id (art_1er, anx_i, ...)
    string ProvisionId,         // lex_id#anchor
    string PType,               // article | annex (opaque here)
    string? Num,
    string? Heading,
    string? Path,               // container ancestry, " / "-joined
    string? ArticleValidFrom,   // publisher-asserted per-provision date, when present
    string? WorkTitle,          // denormalized for ranking
    string TextMd,
    string TextSha,
    // Cross-references the publisher wrote into the text ("modifie par la loi du 4 juin 2020"),
    // captured at derive time with their ELI target. Serialised as JSON because a provision has
    // few of them and they are always read whole, never queried field by field.
    string? CitationsJson = null);

/// <summary>One distinct text state of one provision across versions (the per-anchor time axis).</summary>
public sealed record ProvisionStateRow(
    string GroupKey, string Anchor, string ValidFrom, string? ValidTo,
    string TextSha, string? InVersion, string? ArticleValidFrom, bool ValidityConflict);

/// <summary>Anchor lifecycle event at a version transition (inserted | removed | renumbered).</summary>
public sealed record AnchorEventRow(
    string GroupKey, string EType, string? FromAnchor, string? ToAnchor,
    string? Anchor, string? TextSha, string? AtVersion);

public sealed record EventRow(string Key, string Scope, string Event, string ObservedFrom, string? Detail);

public sealed record ObservationRow(
    string Key, string Language, string ExprValidFrom,
    string? Sha256, string? SourceUri, string ObservedFrom, string? ObservedTo);

/// <summary>
/// One work's movement inside a period: how many distinct validity dates fell in the window,
/// when the first and last of them were, and how many versions the work has in total.
/// </summary>
public sealed record ChangeRow(
    string GroupKey,
    int VersionsInPeriod,
    string FirstChange,
    string LastChange,
    string? Title,
    int VersionsTotal);

/// <summary>
/// F5 — the one rule that cannot be relaxed, as a construct: every query entry point
/// takes a non-optional FilterSet whose fields are each explicitly All or a constraint.
/// Filters are applied as SQL predicates before any ranking.
/// </summary>
public sealed record FilterSet(
    DateOnly? AsOf,
    string? Collection,
    string? Kind,
    string? Language,
    // Restrict retrieval to a named set of works. Full-text ranking over a whole national corpus
    // is precise only when the question happens to use rare words: search the entire body of
    // Luxembourg law for "prix" and seed-certification and care-home tariffs outrank the
    // electricity act. A consumer that knows its subject can say so, and get its subject back.
    IReadOnlyList<string>? Works = null)
{
    public static readonly FilterSet All = new(null, null, null, null);
}
