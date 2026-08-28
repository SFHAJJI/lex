namespace Lex.Index;

public sealed record PublisherMetadataRow(
    string Kind,
    string Identifier,
    string? Language,
    string? Value,
    string SourceUri,
    bool CitationIdentity = false);

public sealed record MatchedPublisherMetadata(
    string Kind,
    string Identifier,
    string? Label,
    string? Language,
    string SourceUri,
    string? MatchedSegment = null);

public sealed record PublisherShortTitleMatch(
    string Work,
    string Segment,
    string Identifier,
    string Label,
    string Language,
    string SourceUri);

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
    string? Profile = null,
    string? Hierarchy = null,
    string? Domains = null,
    string? ActForm = null,
    string? BindingStatus = null,
    string? ConsolidationStatus = null,
    IReadOnlyList<PublisherMetadataRow>? PublisherMetadata = null,
    IReadOnlyList<string>? DocumentRoles = null);

/// <summary>One publisher version and all language expressions available for that version.</summary>
public sealed record TimelineVersionRow(
    DocRow Version,
    IReadOnlyList<DocRow> Expressions);

public sealed record InForceAmbiguity(
    string GroupKey,
    string ValidFrom,
    IReadOnlyList<DocRow> Choices);

public sealed record InForcePage(
    IReadOnlyList<DocRow> Rows,
    int TotalGroups,
    IReadOnlyList<InForceAmbiguity> Ambiguities);

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
    string? CitationsJson = null,
    int? StoredTextBytes = null,
    int? StoredTextCharacters = null,
    bool TextLoaded = true);

public sealed record RetrievalHit(
    DocRow Doc,
    ProvisionRow Provision,
    string Snippet,
    double Score,
    IReadOnlyList<string> MatchReasons,
    MatchedPublisherMetadata? MatchedPublisherMetadata = null);

public sealed record SearchExecution(
    string RetrievalMode,
    IReadOnlyList<RetrievalHit> Hits,
    IReadOnlyList<string> QueryExpansions,
    SearchQueryPlan? QueryPlan = null);

/// <param name="Kind">Which stored name form the mention matched: <c>title</c>,
/// <c>identifier</c> or <c>alias</c>, or null when the mention resolved to nothing. The resolver
/// has always known this and used to drop it on the floor, and it is the difference between the
/// two instruments a full official citation names: a title quoted in full matches as a title,
/// while the amending tail at the end of it names only a number and matches as an identifier.
/// </param>
public sealed record WorkResolution(
    string Mention,
    string Status,
    IReadOnlyList<string> Candidates,
    string? Kind = null,
    IReadOnlyList<PublisherShortTitleMatch>? PublisherShortTitleMatches = null);

public sealed record SearchQueryPlan(
    string RawQuery,
    string ProvisionQuery,
    IReadOnlyList<string> WorkConstraints,
    string? ArticleNumber,
    string? RoleIntent,
    bool HasStrongWorkMatch,
    string WorkResolutionStatus = "not_requested",
    IReadOnlyList<WorkResolution>? WorkResolutions = null,
    bool WorkCatalogAvailable = true);

/// <summary>One distinct text state of one provision across versions (the per-anchor time axis).</summary>
public sealed record ProvisionStateRow(
    string GroupKey, string Language, bool IsPrimaryLanguage, string Anchor, string ValidFrom, string? ValidTo,
    string TextSha, string? InVersion, string? ArticleValidFrom, bool ValidityConflict);

/// <summary>Anchor lifecycle event at a version transition (inserted | removed | renumbered).</summary>
public sealed record AnchorEventRow(
    string GroupKey, string Language, bool IsPrimaryLanguage, string EType, string? FromAnchor, string? ToAnchor,
    string? Anchor, string? TextSha, string? AtVersion);

public sealed record EventRow(
    string Key,
    string Scope,
    string Event,
    string ObservedFrom,
    string? Detail,
    string? FirstMissedAt = null,
    int? RunsMissed = null,
    string? RunIdentity = null);

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
    int VersionsTotal,
    // The version in force immediately BEFORE the window's first change, which is the only
    // sensible left-hand side of "what changed here". Without it a caller compares FirstChange
    // with LastChange, and those are the same date whenever a work changed exactly once, which
    // is the common case: 92% of regulation rows in a recent window. The comparison then runs a
    // version against itself and truthfully reports no differences. Null when the window's first
    // change is also the work's first version, so there is nothing to compare against.
    string? Baseline,
    // Distinct wordings across the comparison span, baseline included. 1 means the publisher
    // reissued the act without altering a word, which is a real and common answer: "2 new
    // versions, wording unchanged". 0 means no version in the span carries text at all.
    int DistinctTexts = 0,
    // True only when both comparison endpoints carry provision text. DistinctTexts alone cannot
    // express the asymmetric case where one publisher state is text-bearing and the other is not.
    bool TextComparable = false,
    string? SourceClass = null,
    string? Hierarchy = null,
    string? Domains = null,
    string? ActForm = null,
    string? BindingStatus = null,
    string? Language = null);

/// <summary>
/// F5 — the one rule that cannot be relaxed, as a construct: every query entry point
/// takes a non-optional FilterSet whose fields are each explicitly All or a constraint.
/// Filters are applied as SQL predicates before any ranking.
/// </summary>
/// <summary>A work as the catalogue lists it: summarised across all of its versions.</summary>
public sealed record CatalogueRow(
    string Collection, string GroupKey, string? Title, string? TitleShort, string? Kind,
    int Versions, int TextVersions, string FirstFrom, string LastFrom, bool HasText,
    // ISO instant, when a record for this work was last observed. Optional so that an
    // index built before this column was selected still opens.
    string? LastObserved = null);

/// <summary>
/// How the catalogue is sorted. An enum rather than a string because the value reaches an
/// ORDER BY clause, and a whitelist that the compiler enforces cannot be talked past.
/// </summary>
public enum CatalogueOrder { Name, MostVersions, MostRecent, Oldest }

public sealed record FilterSet(
    DateOnly? AsOf,
    string? Collection,
    string? Kind,
    string? Language,
    // Restrict retrieval to a named set of works. Full-text ranking over a whole national corpus
    // is precise only when the question happens to use rare words: search the entire body of
    // Luxembourg law for "prix" and seed-certification and care-home tariffs outrank the
    // electricity act. A consumer that knows its subject can say so, and get its subject back.
    IReadOnlyList<string>? Works = null,
    string? Hierarchy = null,
    string? ActForm = null,
    string? BindingStatus = null,
    string? Domain = null,
    string? DocumentRole = null,
    string? PublisherMetadataIdentifier = null)
{
    public static readonly FilterSet All = new(null, null, null, null);
}
