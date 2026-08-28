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

/// <summary>
/// Publisher-supplied work metadata. Taxonomy rows are weak discovery signals; an exact,
/// unique publisher short-title segment may identify a work, and an official same-as relation
/// may canonicalize a publisher citation. None of these rows is legal wording.
/// </summary>
public sealed record PublisherMetadataRecord(
    string Kind,
    string Identifier,
    string? Language,
    string? Label,
    string SourceUri);

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
    IReadOnlyDictionary<string, string> Raw,
    IReadOnlyList<PublisherMetadataRecord>? PublisherMetadata = null,
    IReadOnlyList<string>? DocumentRoles = null);

/// <summary>What an adapter declares about its publisher (C4).</summary>
public sealed record PublisherDescriptor(
    Publisher Publisher,
    IReadOnlyList<DocumentType> DocumentTypes,
    IReadOnlyList<string> Languages,
    bool TextIncluded,
    bool TextPublic,                  // D38: true only when the publisher's text-reuse right is measured
    string HistoryBegins);            // "publisher" for Tier A, ISO date for Tier B

public sealed record SourceBuildIssue(string Code, string Work, string? Detail = null);

public sealed class SourceAcquisitionException(SourceBuildIssue issue, int attempts = 1)
    : Exception($"{issue.Code}: {issue.Work}: {issue.Detail}")
{
    public SourceBuildIssue Issue { get; } = issue;
    public int Attempts { get; } = attempts;
}

public sealed record SourceBuildInventory(
    int ExpectedWorks,
    IReadOnlyList<SourceBuildIssue> Issues,
    bool EnumerationComplete = true,
    int RetryMaximumAttempts = 1,
    string SourceConfigurationKind = "code_only",
    string? SourceConfigurationSha256 = null);

public interface ISourceBuildInventory
{
    SourceBuildInventory GetBuildInventory();
}

/// <summary>
/// The source facts retained by a legacy corpus for one publisher version whose opaque
/// publisher identifier was not persisted yet. Only a publisher adapter may interpret these
/// values and recover that identifier (F4).
/// </summary>
public sealed record LegacyVersionIdentity(
    string WorkIdentifier,
    DateOnly ValidFrom,
    IReadOnlyList<LegacyExpressionIdentity> Expressions);

public sealed record LegacyExpressionIdentity(string Language, string SourceUri);

/// <summary>
/// Optional one-time migration seam for a source adapter that can recover an exact publisher
/// version identifier from its own official legacy URLs. Implementations must fail closed when
/// the retained facts do not identify exactly one publisher version.
/// </summary>
public interface ILegacyVersionIdentityResolver
{
    Identifier ResolveLegacyVersionIdentity(LegacyVersionIdentity legacy);
}

/// <summary>
/// The observable outcome of attempting to acquire one publisher expression. Absence is data,
/// never an overloaded null: the corpus build must be able to distinguish a declared
/// metadata-only record from transport exhaustion, a permanent publisher response, an
/// oversized body, or content that could not be parsed safely.
/// </summary>
public enum SourceBodyStatus
{
    Retrieved,
    PublisherMetadataOnly,
    PermanentNotFound,
    Gone,
    Oversized,
    ParserFailure,
    RetryExhausted,
    // A 2xx response whose body is empty or whitespace. Distinct from PermanentNotFound because
    // the URL resolves; distinct from Retrieved because there is no text to store.
    EmptyBody,
}

/// <summary>
/// The bounded response metadata retained beside a primary publisher body. This deliberately
/// excludes arbitrary headers, cookies, addresses, request data, and redirect history.
/// </summary>
public sealed record SourceHttpEvidence(
    int StatusCode,
    string? ContentType,
    string? Charset,
    string? EntityTag,
    DateTimeOffset? LastModified,
    DateTimeOffset FetchedAt,
    string EffectiveSourceUri,
    bool BodyComplete = true);

public sealed record SourceBodyFetch(
    SourceBodyStatus Status,
    byte[]? Bytes = null,
    SourceHttpEvidence? Http = null,
    string? Detail = null,
    int Attempts = 1)
{
    public static SourceBodyFetch Retrieved(
        byte[] bytes,
        SourceHttpEvidence http,
        int attempts = 1) =>
        new(SourceBodyStatus.Retrieved, bytes, http, Attempts: attempts);

    public string IssueCode => Status switch
    {
        SourceBodyStatus.PublisherMetadataOnly => "publisher_metadata_only",
        SourceBodyStatus.PermanentNotFound => "body_not_found",
        SourceBodyStatus.Gone => "body_gone",
        SourceBodyStatus.Oversized => "body_oversized",
        SourceBodyStatus.ParserFailure => "body_parser_failure",
        SourceBodyStatus.RetryExhausted => "body_retry_exhausted",
        SourceBodyStatus.EmptyBody => "body_empty",
        _ => throw new InvalidOperationException("A retrieved body is not a build issue."),
    };
}

/// <summary>One file inside an alternative manifestation (an archive member). Bytes are publisher-verbatim.</summary>
public sealed record ManifestationMember(string Name, byte[] Bytes);

/// <summary>
/// An alternative manifestation of an expression (D48): a richer structural format some
/// publishers serve alongside the primary body. A container archive is packaging, not
/// evidence — members carry the stable publisher bytes.
/// </summary>
public sealed record ManifestationFetch(string Format, IReadOnlyList<ManifestationMember> Members, string SourceUri);

public sealed record SourceManifestationFetch(
    SourceBodyStatus Status,
    ManifestationFetch? Value = null,
    string? Detail = null,
    int Attempts = 1)
{
    public static SourceManifestationFetch Retrieved(ManifestationFetch value, int attempts = 1) =>
        new(SourceBodyStatus.Retrieved, value, Attempts: attempts);
}

/// <summary>
/// C4 — the adapter seam. An adapter never writes files, never touches git, never knows the corpus layout (F8).
/// A Tier A/B adapter whose body channel is gated runs in declared metadata-only mode: FetchBody is not called.
/// </summary>
public interface ISourceAdapter
{
    PublisherDescriptor Describe();
    IAsyncEnumerable<WorkRef> EnumerateWorks(CancellationToken ct);
    Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct);
    Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct);

    /// <summary>
    /// D48: fetch an alternative structural manifestation for an expression, or null when the
    /// publisher has none (the default for adapters without one). Implementations must verify
    /// the returned content identifies as the REQUESTED version — a manifestation that cannot
    /// be identity-checked is not evidence and must be dropped.
    /// </summary>
    Task<SourceManifestationFetch> FetchAltManifestation(
        VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
        Task.FromResult(new SourceManifestationFetch(SourceBodyStatus.PublisherMetadataOnly));
}
