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
    string? Body,
    string? PublicationDate,
    string? StatusNote);

public sealed record EventRow(string Key, string Scope, string Event, string ObservedFrom, string? Detail);

public sealed record ObservationRow(
    string Key, string Language, string ExprValidFrom,
    string? Sha256, string? SourceUri, string ObservedFrom, string? ObservedTo);

/// <summary>
/// F5 — the one rule that cannot be relaxed, as a construct: every query entry point
/// takes a non-optional FilterSet whose fields are each explicitly All or a constraint.
/// Filters are applied as SQL predicates before any ranking.
/// </summary>
public sealed record FilterSet(
    DateOnly? AsOf,
    string? Collection,
    string? Kind,
    string? Language)
{
    public static readonly FilterSet All = new(null, null, null, null);
}
