namespace Lex.Law;

/// <summary>Source tier per spec §1.6. Declared per adapter, surfaced in every response.</summary>
public enum Tier { A, B, C }

/// <summary>
/// An opaque publisher identifier. Stored, compared, returned — never parsed outside an adapter (F4).
/// </summary>
public readonly record struct Identifier(string Value)
{
    public override string ToString() => Value;
}

/// <summary>DocumentType is data, not code (§3.5). Never an enum.</summary>
public sealed record DocumentType(
    string PublisherId,
    string Code,
    IReadOnlyDictionary<string, string> Labels);

/// <summary>Authority is cited publisher data, never our opinion (§3.6). "unknown" is a valid level.</summary>
public sealed record Authority(string Level, string? Statement, string? Source, string AssertedBy);

public sealed record Publisher(
    string Id,
    string Name,
    string Jurisdiction,
    string Homepage,
    Tier Tier,
    string Attribution,
    string? SourceTermsUrl);

/// <summary>A reference to a Work as enumerated by an adapter. Slug is adapter-supplied (adapters may read their own identifiers).</summary>
public sealed record WorkRef(Identifier Id, string Slug, string? TypeCode, string? TitleHint);

/// <summary>One sighting of an expression body: the unit of transaction time (§7.4). Null body hash in metadata-only mode.</summary>
public sealed record ObservationRecord(string? Sha256, string SourceUri, DateTimeOffset RetrievedAt);

/// <summary>A Version in one language, with its own validity interval (§3.3).</summary>
public sealed record ExpressionRecord(
    string Language,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string ValidTimeSource,           // "publisher" | "observation"
    string? Title,
    string? TitleShort,
    string? SourceUri);

public sealed record RelationRecord(string Type, Identifier Target);

/// <summary>One state of a Work, valid between two dates.</summary>
public sealed record VersionRecord(
    Identifier Id,
    Identifier WorkId,
    string? TypeCode,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    string ValidTimeSource,
    string? InForceStatus,
    DateOnly? PublicationDate,
    IReadOnlyList<ExpressionRecord> Expressions,
    IReadOnlyList<RelationRecord> Relations,
    IReadOnlyDictionary<string, string> Raw);

/// <summary>What an adapter declares about its publisher (C4).</summary>
public sealed record PublisherDescriptor(
    Publisher Publisher,
    IReadOnlyList<DocumentType> DocumentTypes,
    IReadOnlyList<string> Languages,
    bool TextIncluded,
    string HistoryBegins);            // "publisher" for Tier A, ISO date for Tier B

/// <summary>
/// C4 — the adapter seam. An adapter never writes files, never touches git, never knows the corpus layout (F8).
/// A Tier A/B adapter whose body channel is gated runs in declared metadata-only mode: FetchBody is not called.
/// </summary>
public interface ISourceAdapter
{
    PublisherDescriptor Describe();
    IAsyncEnumerable<WorkRef> EnumerateWorks(CancellationToken ct);
    Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct);
    Task<string?> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct);
}
