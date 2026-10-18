using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Luxembourg;

public enum LuxembourgIndexBuildRefusal
{
    [JsonStringEnumMemberName("none")] None = 0,
    [JsonStringEnumMemberName("corpus_refused")] CorpusRefused = 1,
    [JsonStringEnumMemberName("population_mismatch")] PopulationMismatch = 2,
    [JsonStringEnumMemberName("derivation_mismatch")] DerivationMismatch = 3,
    [JsonStringEnumMemberName("index_invalid")] IndexInvalid = 4,
    [JsonStringEnumMemberName("predecessor_mismatch")] PredecessorMismatch = 5,
    [JsonStringEnumMemberName("predecessor_schema_differs")] PredecessorSchemaDiffers = 6,
    [JsonStringEnumMemberName("predecessor_derivation_differs")] PredecessorDerivationDiffers = 7,
    [JsonStringEnumMemberName("build_time_invalid")] BuildTimeInvalid = 8,
}

public sealed record LuxembourgIndexBuildResult(
    SourceArtifactRef IndexRef,
    ReadOnlyMemory<byte> IndexBytes,
    SourceArtifactRef CapabilityManifestRef,
    ReadOnlyMemory<byte> CapabilityManifestBytes,
    V3IndexCapabilityManifest CapabilityManifest);

/// <summary>
/// The previous build's index as the next build's input (predecessor chaining): its digest and its event log, read
/// and verified by the log's own stamp (<c>log_stamp</c>) and structure, independently of the index's other tables, so
/// a predecessor of an older index schema still chains while its log schema is the one this build writes. Built only
/// by <see cref="TryRead"/>, which verifies the bytes against the digest the caller names.
/// </summary>
public sealed class LuxembourgIndexPredecessor
{
    private LuxembourgIndexPredecessor(
        string indexSha256,
        string logSchema,
        IReadOnlyList<LuxembourgIndexBuilder.ObservationRow> observations,
        IReadOnlyList<LuxembourgIndexBuilder.EventRow> events)
    {
        IndexSha256 = indexSha256;
        LogSchema = logSchema;
        Observations = observations;
        Events = events;
    }

    /// <summary>The predecessor index's digest, which the successor's new observation names.</summary>
    public string IndexSha256 { get; }

    /// <summary>
    /// The log schema the predecessor holds: log /3 (index 9), which a build carries forward, or log /2 (index 8), which is
    /// read and verified here but never carried forward: an index 9 build over it starts a new genesis log.
    /// </summary>
    public string LogSchema { get; }

    internal IReadOnlyList<LuxembourgIndexBuilder.ObservationRow> Observations { get; }

    internal IReadOnlyList<LuxembourgIndexBuilder.EventRow> Events { get; }

    internal long LastSeq => Observations[^1].LastSeq;

    /// <summary>
    /// Reads a predecessor index for a build to carry forward: its bytes must be the digest the caller names (the
    /// predecessor's build report), it must hold a log of this log schema (an index without one, schema 6 and before, or
    /// of another log schema, index 8's log /2 among them, refuses <see cref="LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers"/>:
    /// an index 9 build over an index 8 starts a new genesis log), and the log must match its own stamp and be numbered as
    /// observations appending events, or it refuses <see cref="LuxembourgIndexBuildRefusal.PredecessorMismatch"/>.
    /// </summary>
    public static LuxembourgIndexPredecessor? TryRead(
        SourceArtifactRef indexRef,
        ReadOnlySpan<byte> indexBytes,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail)
    {
        var read = TryReadLog(indexRef, indexBytes, out refusal, out detail);
        if (read is not null && !string.Equals(read.LogSchema, LuxembourgIndexBuilder.EventLogSchema, StringComparison.Ordinal))
        {
            refusal = LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers;
            detail = $"The predecessor's event log is {read.LogSchema}; this build carries {LuxembourgIndexBuilder.EventLogSchema} forward "
                + "and an index 9 build over an index 8 starts a new genesis log.";
            return null;
        }

        return read;
    }

    /// <summary>
    /// Reads an index's event log, verified as <see cref="TryRead"/> verifies it, of log /3 or log /2: the mount writer's
    /// verification reads it so that an index 8 and its generations still verify. Never a predecessor of a build.
    /// </summary>
    internal static LuxembourgIndexPredecessor? TryReadLog(
        SourceArtifactRef indexRef,
        ReadOnlySpan<byte> indexBytes,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(indexRef);
        refusal = LuxembourgIndexBuildRefusal.None;
        detail = null;
        var digest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
        if (!string.Equals(digest, indexRef.Sha256, StringComparison.Ordinal))
        {
            refusal = LuxembourgIndexBuildRefusal.PredecessorMismatch;
            detail = $"The predecessor index's bytes are {digest}, not the {indexRef.Sha256} its build names.";
            return null;
        }

        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-predecessor-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.WriteAllBytes(path, indexBytes.ToArray());
            using var connection = LuxembourgIndexBuilder.Open(path, SqliteOpenMode.ReadOnly);
            using (var tables = connection.CreateCommand())
            {
                tables.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='log_stamp'";
                if (Convert.ToInt64(tables.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                {
                    refusal = LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers;
                    detail = "The predecessor index holds no event log stamp (index schema 6 or before), so its log cannot be carried forward.";
                    return null;
                }
            }

            string logSchema;
            string logDigest;
            using (var stamp = connection.CreateCommand())
            {
                stamp.CommandText = "SELECT log_schema,log_rows_sha256 FROM log_stamp WHERE log_stamp_id=1";
                using var reader = stamp.ExecuteReader();
                if (!reader.Read())
                {
                    refusal = LuxembourgIndexBuildRefusal.PredecessorMismatch;
                    detail = "The predecessor index's event log has no stamp row.";
                    return null;
                }

                logSchema = reader.GetString(0);
                logDigest = reader.GetString(1);
            }

            if (!string.Equals(logSchema, LuxembourgIndexBuilder.EventLogSchema, StringComparison.Ordinal) &&
                !string.Equals(logSchema, LuxembourgIndexBuilder.LegacyEventLogSchema8, StringComparison.Ordinal))
            {
                refusal = LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers;
                detail = $"The predecessor's event log is {logSchema}; this build carries {LuxembourgIndexBuilder.EventLogSchema} forward.";
                return null;
            }

            var observations = LuxembourgIndexBuilder.ReadLogObservations(connection, logSchema);
            var events = LuxembourgIndexBuilder.ReadLogEvents(connection);
            if (!string.Equals(LuxembourgIndexBuilder.HashEventLogOf(logSchema, observations, events), logDigest, StringComparison.Ordinal))
            {
                refusal = LuxembourgIndexBuildRefusal.PredecessorMismatch;
                detail = "The predecessor's event log does not match its log stamp.";
                return null;
            }

            LuxembourgIndexBuilder.ValidateLogNumbering(observations, events);
            LuxembourgIndexBuilder.ValidateLogHistory(observations, events, logSchema);
            return new LuxembourgIndexPredecessor(digest, logSchema, observations, events);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException)
        {
            refusal = LuxembourgIndexBuildRefusal.PredecessorMismatch;
            detail = exception.Message;
            return null;
        }
        finally
        {
            LuxembourgIndexBuilder.DeleteDatabase(path);
        }
    }
}

/// <summary>One article of one held state whose searchable text matched, as the index holds it.</summary>
public sealed record LuxembourgIndexSearchHit(
    string WorkKey,
    string ApplicabilityDate,
    string StateSha256,
    string ArticleIdentitySha256,
    string PublisherId,
    string? PublisherWId);

public sealed record LuxembourgIndexSearchResult(
    V3IndexCapabilityLookupOutcome Outcome,
    IReadOnlyList<string> ArticleIdentities);

public sealed record LuxembourgIndexResolvedExpression(
    string ExpressionIri,
    string? PublisherWid,
    string Language,
    IReadOnlyList<string> ArticleIdentities);

public sealed record LuxembourgIndexResolvedState(
    string WorkKey,
    string ApplicabilityDate,
    string StateSha256,
    string ExpressionIri,
    string PublisherWorkIri,
    string PublisherLegalResourceIri,
    string Language,
    IReadOnlyList<string> RuleProfileSha256s,
    IReadOnlyList<string> ArticleIdentities);

/// <summary>
/// What the index holds of dated states: the works that have one, the first and last publisher date
/// held, and the languages held.
/// </summary>
public sealed record LuxembourgIndexStatePopulation(
    long Works,
    string? FirstDate,
    string? LastDate,
    IReadOnlyList<string> Languages);

/// <summary>
/// What the index holds in one language: works and dated states (a state is one expression's articles
/// on one publisher date, so there is no separate count of expressions) with the first and last
/// publisher date, articles, and how many of those articles carry no publisher date. Counts of rows
/// the index holds, never of what the publisher holds.
/// </summary>
public sealed record LuxembourgIndexLanguageCoverage(
    string Language,
    long Works,
    long States,
    string? FirstStateDate,
    string? LastStateDate,
    long Articles,
    long ArticlesWithoutPublisherDate);

/// <summary>
/// What the index holds and what it recorded as missing: members by outcome, the gap tokens the
/// corpus recorded per member counted by member (verbatim, in ordinal order), the corpus's
/// legal-content outcomes of its acquired members counted by disposition token (verbatim, in ordinal
/// order), and every language's counts. The counts are grouped and bounded: no per-work and no
/// per-article row.
/// </summary>
public sealed record LuxembourgIndexCoverage(
    long Members,
    long Works,
    long States,
    long Articles,
    IReadOnlyList<KeyValuePair<string, long>> MemberOutcomes,
    long MembersWithGaps,
    IReadOnlyList<KeyValuePair<string, long>> Gaps,
    IReadOnlyList<KeyValuePair<string, long>> ArticleOutcomes,
    IReadOnlyList<LuxembourgIndexLanguageCoverage> Languages);

/// <summary>
/// One article's publisher-stated applicability date, or <c>null</c> where the publisher stated none
/// for that article. Read from the article-level <c>scl:dateApplicability</c> the inventory retained;
/// never derived from the state.
/// </summary>
public sealed record LuxembourgIndexArticleDate(
    string ArticleIdentitySha256,
    string? ApplicabilityDate);

/// <summary>
/// One state's articles with searchable text that came from corpus members with one outcome and one rights disposition, as
/// the corpus recorded them: a state whose articles came from one member (the usual case) has one row. What a search may
/// match is the caller's rule; nothing is admitted or refused here.
/// </summary>
public sealed record LuxembourgIndexStateTextSource(
    string StateSha256,
    string Outcome,
    string? RightsDisposition,
    long ArticlesWithSearchableText);

/// <summary>
/// One article of one state that carries a publisher-minted article id: the publisher's ids and
/// article-level date as stored, and the wording digest: the SHA-256 of a canonical JSON array of
/// <c>[kind, text, target]</c> for the Text and Reference tokens of the stored stream, in order.
/// Note references, note bodies and modification markers are the publisher's editorial apparatus
/// and stay out; paragraph structure and whitespace-only nodes are not retained at ingest, so they
/// are not in it either. Nothing is compared here.
/// </summary>
/// <summary>
/// One article of one state: its identity, the publisher-minted article id, the wording digest
/// (<see cref="LuxembourgIndexReader.WordingSha256"/>), the publisher's wId and article-level
/// applicability date where it wrote them, the searchable text (the text and reference tokens'
/// texts concatenated in publisher order, the bytes <c>search</c> matches), the stored token
/// stream, verbatim, and the object reference of the corpus member it was read from. Nothing is
/// compared here.
/// </summary>
public sealed record LuxembourgIndexStateArticle(
    string ArticleIdentitySha256,
    string PublisherId,
    string WordingSha256,
    string? PublisherWid,
    string? ApplicabilityDate,
    string Text,
    string TokensJson,
    string ObjectRefSha256);

public sealed record LuxembourgIndexInboundCitation(
    string ArticleIdentitySha256,
    string PublisherId,
    string ExpressionIri,
    string Language,
    int Ordinal,
    bool InNote,
    string? Label,
    string? Href,
    string ToRef);

/// <summary>
/// One reference the publisher wrote in an article of a state: which article and where in it (<c>Ordinal</c>
/// counts the article's references in the order they occur, a footnote body's at its note reference and
/// <c>InNote</c> set), the publisher's label and value (<c>Href</c>, verbatim, null where the mark carried none),
/// and the target read from the value by a fixed grammar that resolves nothing: <c>legilux_eli</c> and its absolute
/// IRI, <c>other_uri</c> and the value as written, or <c>unparsed</c> and no target. It records that a reference
/// was written and says nothing about its meaning or its legal effect.
/// </summary>
public sealed record LuxembourgIndexCitation(
    string ArticleIdentitySha256,
    string PublisherId,
    int Ordinal,
    bool InNote,
    string? Label,
    string? Href,
    string ToKind,
    string? ToRef);

/// <summary>
/// One source document a state's articles were taken from, as the corpus recorded it: its digest (the
/// corpus member's object reference), the outcome it was admitted with, the rights disposition when the
/// corpus states one, the gap tokens it recorded, verbatim, and the legal-content outcomes the corpus
/// recorded for it counted by disposition token (verbatim, in ordinal order).
/// </summary>
public sealed record LuxembourgIndexStateSource(
    string ObjectRefSha256,
    string Outcome,
    string? RightsDisposition,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<KeyValuePair<string, long>> ArticleOutcomes);

public sealed record LuxembourgIndexAnchorArticle(
    string StateSha256,
    string ArticleIdentitySha256,
    string PublisherId,
    string? PublisherWid,
    string? ApplicabilityDate,
    string WordingSha256);

/// <summary>
/// One work a title resolved to: its expressions and languages, the title that matched and that title's own language
/// (the resolver searches every language's titles, so the language searched in is not the title's), and why it matched.
/// </summary>
public sealed record LuxembourgIndexResolvedWork(
    string WorkIdentifier,
    IReadOnlyList<string> ExpressionIris,
    IReadOnlyList<string> Languages,
    string MatchedTitle,
    string MatchedTitleLanguage,
    string MatchReason);

public sealed record LuxembourgIndexWorkResolution(
    bool Available,
    IReadOnlyList<LuxembourgIndexResolvedWork> Candidates);

/// <summary>
/// One title the publisher stated for one expression of a work, as the index holds it: the language, the
/// expression, whether it is the title or the short title, the words, and the digest of the evidence they
/// were read from. The document date the index stores beside a title is not carried: it falls back to an
/// article's applicability date, so it is not the publisher's document date.
/// </summary>
public sealed record LuxembourgIndexWorkTitle(
    string ExpressionIri,
    string Language,
    string TitleKind,
    string Title,
    string EvidenceSha256);

/// <summary>
/// One of the publisher's typed assertions as the index holds it: the subject, the predicate and its fact
/// kind as the closed vocabulary names them on the wire, the object as an IRI or a literal (with its
/// datatype IRI and language tag, empty when the publisher gave none), and the digest of the observation
/// it was read from. Verbatim; nothing is interpreted here.
/// </summary>
/// <summary>
/// One held work as <c>browse</c> lists it: the work key, the distinct publisher work and legal-resource
/// IRIs and languages its states carry (ordinal order), the first and latest state dates and the state count.
/// </summary>
public sealed record LuxembourgIndexWorkRecord(
    string WorkKey,
    IReadOnlyList<string> PublisherWorkIris,
    IReadOnlyList<string> PublisherLegalResourceIris,
    IReadOnlyList<string> Languages,
    string FirstDate,
    string LastDate,
    long StateCount);

public sealed record LuxembourgIndexWorkFact(
    string SubjectIri,
    string Predicate,
    string FactKind,
    string ObjectKind,
    string ObjectValue,
    string DatatypeIri,
    string LanguageTag,
    string EvidenceSha256);

/// <summary>
/// One event of the index's log as the reader hands it out: its sequence number, scope and name, the
/// observation time (null: no observation time is held, and none is invented), and the state it is
/// about, by the states table's key and digest.
/// </summary>
public sealed record LuxembourgIndexEvent(
    long Seq,
    string Scope,
    string Event,
    string? ObservedFrom,
    string WorkKey,
    string ApplicabilityDate,
    string ExpressionIri,
    string Language,
    string StateSha256,
    string DetailJson);

/// <summary>What the index's event log holds: how many events and the last sequence number (0 when empty).</summary>
public sealed record LuxembourgIndexEventLog(long Events, long LastSeq);

/// <summary>
/// One state of one work as the event log held it at an observation: the states table's key, the digest of its last
/// event up to that observation and the publisher bodies it was read from. The log alone says it; whether the mounted
/// index still holds the state is the caller's to ask.
/// </summary>
public sealed record LuxembourgIndexObservedState(
    string WorkKey,
    string ApplicabilityDate,
    string ExpressionIri,
    string Language,
    string StateSha256,
    IReadOnlyList<string> SourceBodySha256);

/// <summary>
/// One observation of the index's event log: the build that observed (its corpus), the index whose log it carried
/// forward (null for the first), the events it appended by sequence number (an empty range: first is last + 1), and
/// its build time (<c>yyyy-MM-ddTHH:mm:ssZ</c>): the time the acquiring build read once its acquisition had ended, an
/// upper bound on when its corpus was observed and never an observation time, which a derivation from custody keeps.
/// </summary>
public sealed record LuxembourgIndexObservation(
    long Observation,
    string CorpusSha256,
    string? PredecessorIndexSha256,
    long FirstSeq,
    long LastSeq,
    string BuiltAt,
    string? DerivedAt = null);

/// <summary>
/// One state row of a work as index 9 holds it, held or textless: its key, digest and publisher identities, its articles
/// (none for a textless row), its kind and the role of its date (the catalogue's applicability, the file's own
/// applicability, or the catalogue's entry into force), an act's catalogue entry into force kept beside that date, its
/// end and where that end comes from (the publisher's,
/// or derived from the next state), the boundary kind of that end, its publication date, why its text is or is not held,
/// what its own file says, the French expression it translates, and whether its date is after the observation bound.
/// Index 8 and 6 rows carry none of the later fields (null, and text status <c>held</c>): they recorded none.
/// </summary>
public sealed record LuxembourgIndexCatalogueState(
    string WorkKey,
    string ApplicabilityDate,
    string StateSha256,
    string ExpressionIri,
    string PublisherWorkIri,
    string PublisherLegalResourceIri,
    string Language,
    IReadOnlyList<string> ArticleIdentities,
    string? StateKind,
    string? DateRole,
    string? EntryIntoForce,
    string? ApplicableTo,
    string? ApplicableToSource,
    string? Boundary,
    string? PublicationDate,
    string TextStatus,
    string? TextStatusReason,
    string? InFileIdentity,
    string? InFileDate,
    string? TranslationOf,
    bool Provisional);

/// <summary>
/// One interval transition of a work's French chain as index 9 holds it: from a state to the next state of a later date
/// (its kind how the first state's end meets that start: contiguous, derived, a suspected inclusive end, a hole, an overlap
/// or an invalid end) or to the next state of the same date (<c>same_date</c>), with a hole's or an overlap's span.
/// </summary>
public sealed record LuxembourgIndexInterval(
    string WorkKey,
    int Ordinal,
    string Kind,
    string FromStateSha256,
    string ToStateSha256,
    string? SpanFrom,
    string? SpanTo);

/// <summary>
/// Builds the immutable Luxembourg index from the same proof-complete envelope that builds
/// lex-corpus/6. Callers cannot provide index rows or capability counts.
/// </summary>
public static partial class LuxembourgIndexBuilder
{
    /// <summary>
    /// Index 9: the catalogue's states of every served work (held or textless), dated by role, with the publisher's ends,
    /// boundary kinds, publication dates, text status, in-file evidence and translations; the interval transitions
    /// between them (<c>intervals</c>); titles keyed by work key; state digest /2; and the event log /3, whose
    /// observations record a derivation time and whose sightings carry each state's own publisher facts.
    /// </summary>
    public const string Schema = "lex-v3-luxembourg-index/9";

    /// <summary>
    /// The event log's own schema, stamped apart from the index's (<c>log_stamp</c>), so a later build can verify a
    /// predecessor's log, which it carries forward unchanged, across a change of the index's other tables. Log /3 is index
    /// 9's: its observations carry <c>derived_at</c> and its sightings each state's publisher end, text status and
    /// publication date.
    /// </summary>
    internal const string EventLogSchema = "lex-v3-event-log/3";

    /// <summary>The log schema index 8 wrote, read for an index of that schema and never carried forward by index 9.</summary>
    internal const string LegacyEventLogSchema8 = "lex-v3-event-log/2";

    /// <summary>
    /// The form of an observation's build time: UTC to the second. A build time is captured once per build, after
    /// every fetch, and rounded up to the next second, so it is an upper bound on when the build's corpus was observed.
    /// </summary>
    internal const string BuiltAtFormat = "yyyy-MM-ddTHH:mm:ssZ";
    private const int ApplicationId = 0x4c563306;
    private const string Ddl = TablesDdl + "\n" + EventLogDdl;

    /// <summary>
    /// Schema 8, the last before index 9: its states are the held ones only, dated by the inventory, with state digest /1,
    /// and its event log is log /2. The reader still serves an index of it exactly as schema 8 checked it.
    /// </summary>
    internal const string LegacySchema8 = "lex-v3-luxembourg-index/8";

    internal const string LegacyDdl8 = LegacyTablesDdl + "\n" + LegacyEventLogDdl8;

    /// <summary>
    /// Schema 6, the last before the event log recorded builds. Its tables are schema 8's but for the event log: a
    /// genesis log of <c>first_sighting</c> events only, unique per state, with no observations and no log stamp. The
    /// reader still serves an index of it, with that evidence absent (the panel's answer to Q-20261001-0656-codex).
    /// </summary>
    internal const string LegacySchema6 = "lex-v3-luxembourg-index/6";

    private const string LegacyDdl6 = LegacyTablesDdl + "\n" + LegacyEventLogDdl6;

    /// <summary>
    /// Index 9's tables: schema 8's, with the states table's new columns, and the interval transitions between states.
    /// A state row is held (its articles listed) or textless (none listed, and its text status says why); an empty
    /// expression and language say the catalogue names no expression of the state.
    /// </summary>
    private const string TablesDdl = """
        CREATE TABLE stamp (
          stamp_id INTEGER NOT NULL PRIMARY KEY CHECK (stamp_id = 1),
          schema_identity TEXT COLLATE BINARY NOT NULL,
          corpus_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(corpus_sha256) = 64),
          logical_rows_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(logical_rows_sha256) = 64),
          sqlite_version TEXT COLLATE BINARY NOT NULL,
          sqlite_source_id TEXT COLLATE BINARY NOT NULL,
          compile_options_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(compile_options_sha256) = 64)
        ) STRICT;
        CREATE TABLE members (
          object_ref_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(object_ref_sha256) = 64),
          source_ordinal INTEGER NOT NULL CHECK (source_ordinal >= 0),
          outcome TEXT COLLATE BINARY NOT NULL,
          rights_disposition TEXT COLLATE BINARY,
          stage3_outcomes_json TEXT COLLATE BINARY NOT NULL,
          gaps_json TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE TABLE articles (
          article_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(article_identity_sha256) = 64),
          object_ref_sha256 TEXT COLLATE BINARY NOT NULL REFERENCES members(object_ref_sha256),
          expression_iri TEXT COLLATE BINARY NOT NULL,
          publisher_id TEXT COLLATE BINARY NOT NULL,
          publisher_wid TEXT COLLATE BINARY,
          applicability_date TEXT COLLATE BINARY,
          language TEXT COLLATE BINARY NOT NULL,
          rule_profile_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(rule_profile_sha256) = 64),
          searchable_text TEXT COLLATE BINARY NOT NULL,
          tokens_json TEXT COLLATE BINARY NOT NULL,
          document_position INTEGER NOT NULL CHECK (document_position >= 1)
        ) STRICT;
        CREATE INDEX articles_object_ref ON articles(object_ref_sha256);
        CREATE INDEX articles_language_date ON articles(language, applicability_date);
        CREATE TABLE states (
          work_key TEXT COLLATE BINARY NOT NULL,
          applicability_date TEXT COLLATE BINARY NOT NULL,
          state_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(state_sha256) = 64),
          expression_iri TEXT COLLATE BINARY NOT NULL,
          publisher_work_iri TEXT COLLATE BINARY NOT NULL,
          publisher_legal_resource_iri TEXT COLLATE BINARY NOT NULL,
          language TEXT COLLATE BINARY NOT NULL,
          rule_profiles_json TEXT COLLATE BINARY NOT NULL,
          article_identities_json TEXT COLLATE BINARY NOT NULL,
          state_kind TEXT COLLATE BINARY NOT NULL CHECK (state_kind IN ('as_published', 'consolidation', 'code')),
          date_role TEXT COLLATE BINARY NOT NULL CHECK (date_role IN ('applicability', 'applicability_in_file', 'entry_into_force')),
          applicable_to TEXT COLLATE BINARY,
          applicable_to_source TEXT COLLATE BINARY CHECK (applicable_to_source IN ('publisher', 'derived')),
          boundary TEXT COLLATE BINARY NOT NULL CHECK (boundary IN ('half_open', 'inclusive_suspected', 'invalid', 'derived')),
          publication_date TEXT COLLATE BINARY,
          text_status TEXT COLLATE BINARY NOT NULL CHECK (text_status IN ('held', 'withheld_rights', 'not_available_pdf', 'not_available_derivation', 'not_available_misfiled', 'not_acquired')),
          text_status_reason TEXT COLLATE BINARY,
          in_file_identity TEXT COLLATE BINARY,
          in_file_date TEXT COLLATE BINARY,
          translation_of TEXT COLLATE BINARY,
          provisional INTEGER NOT NULL CHECK (provisional IN (0, 1)),
          entry_into_force TEXT COLLATE BINARY,
          PRIMARY KEY (work_key, applicability_date, expression_iri, language),
          CHECK ((applicable_to IS NULL) = (applicable_to_source IS NULL)),
          CHECK ((text_status = 'held') = (article_identities_json <> '[]')),
          CHECK ((text_status = 'held') = (text_status_reason IS NULL)),
          CHECK ((expression_iri = '') = (language = '')),
          CHECK (expression_iri <> '' OR text_status = 'not_acquired')
        ) STRICT;
        CREATE UNIQUE INDEX states_digest ON states(state_sha256);
        CREATE TABLE intervals (
          work_key TEXT COLLATE BINARY NOT NULL,
          ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
          kind TEXT COLLATE BINARY NOT NULL CHECK (kind IN ('same_date', 'contiguous', 'derived', 'inclusive_suspected', 'hole', 'overlap', 'invalid')),
          from_state_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(from_state_sha256) = 64),
          to_state_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(to_state_sha256) = 64),
          span_from TEXT COLLATE BINARY,
          span_to TEXT COLLATE BINARY,
          PRIMARY KEY (work_key, ordinal),
          CHECK ((kind IN ('hole', 'overlap')) = (span_from IS NOT NULL)),
          CHECK ((span_from IS NULL) = (span_to IS NULL))
        ) STRICT;
        CREATE TABLE work_titles (
          work_identifier TEXT COLLATE BINARY NOT NULL,
          expression_iri TEXT COLLATE BINARY NOT NULL,
          language TEXT COLLATE BINARY NOT NULL,
          title TEXT COLLATE BINARY NOT NULL,
          normalized_title TEXT COLLATE BINARY NOT NULL,
          document_date TEXT COLLATE BINARY,
          title_kind TEXT COLLATE BINARY NOT NULL CHECK (title_kind IN ('title','title_short')),
          evidence_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(evidence_sha256) = 64),
          PRIMARY KEY (work_identifier, expression_iri, language, title, title_kind, evidence_sha256)
        ) STRICT;
        CREATE INDEX work_titles_normalized ON work_titles(normalized_title, work_identifier);
        CREATE TABLE relations (
          from_ref TEXT COLLATE BINARY NOT NULL REFERENCES articles(article_identity_sha256),
          ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
          edge_type TEXT COLLATE BINARY NOT NULL CHECK (edge_type IN ('cites')),
          asserted_by TEXT COLLATE BINARY NOT NULL CHECK (asserted_by IN ('publisher_text')),
          source_predicate TEXT COLLATE BINARY NOT NULL CHECK (source_predicate IN ('akn_ref')),
          in_note INTEGER NOT NULL CHECK (in_note IN (0, 1)),
          label TEXT COLLATE BINARY,
          href TEXT COLLATE BINARY,
          to_kind TEXT COLLATE BINARY NOT NULL CHECK (to_kind IN ('legilux_eli', 'other_uri', 'unparsed')),
          to_ref TEXT COLLATE BINARY,
          PRIMARY KEY (from_ref, ordinal),
          CHECK ((to_kind = 'unparsed') = (to_ref IS NULL))
        ) STRICT;
        CREATE INDEX relations_to_ref ON relations(to_ref, edge_type);
        CREATE TABLE work_facts (
          subject_iri TEXT COLLATE BINARY NOT NULL,
          predicate TEXT COLLATE BINARY NOT NULL,
          fact_kind TEXT COLLATE BINARY NOT NULL,
          object_kind TEXT COLLATE BINARY NOT NULL CHECK (object_kind IN ('iri', 'literal')),
          object_value TEXT COLLATE BINARY NOT NULL,
          datatype_iri TEXT COLLATE BINARY NOT NULL,
          language_tag TEXT COLLATE BINARY NOT NULL,
          evidence_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(evidence_sha256) = 64),
          PRIMARY KEY (subject_iri, predicate, object_kind, object_value, datatype_iri, language_tag, evidence_sha256)
        ) STRICT;
        """;

    /// <summary>Schema 8's and schema 6's tables, exactly as schema 8 wrote them.</summary>
    private const string LegacyTablesDdl = """
        CREATE TABLE stamp (
          stamp_id INTEGER NOT NULL PRIMARY KEY CHECK (stamp_id = 1),
          schema_identity TEXT COLLATE BINARY NOT NULL,
          corpus_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(corpus_sha256) = 64),
          logical_rows_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(logical_rows_sha256) = 64),
          sqlite_version TEXT COLLATE BINARY NOT NULL,
          sqlite_source_id TEXT COLLATE BINARY NOT NULL,
          compile_options_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(compile_options_sha256) = 64)
        ) STRICT;
        CREATE TABLE members (
          object_ref_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(object_ref_sha256) = 64),
          source_ordinal INTEGER NOT NULL CHECK (source_ordinal >= 0),
          outcome TEXT COLLATE BINARY NOT NULL,
          rights_disposition TEXT COLLATE BINARY,
          stage3_outcomes_json TEXT COLLATE BINARY NOT NULL,
          gaps_json TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE TABLE articles (
          article_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(article_identity_sha256) = 64),
          object_ref_sha256 TEXT COLLATE BINARY NOT NULL REFERENCES members(object_ref_sha256),
          expression_iri TEXT COLLATE BINARY NOT NULL,
          publisher_id TEXT COLLATE BINARY NOT NULL,
          publisher_wid TEXT COLLATE BINARY,
          applicability_date TEXT COLLATE BINARY,
          language TEXT COLLATE BINARY NOT NULL,
          rule_profile_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(rule_profile_sha256) = 64),
          searchable_text TEXT COLLATE BINARY NOT NULL,
          tokens_json TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE INDEX articles_object_ref ON articles(object_ref_sha256);
        CREATE INDEX articles_language_date ON articles(language, applicability_date);
        CREATE TABLE states (
          work_key TEXT COLLATE BINARY NOT NULL,
          applicability_date TEXT COLLATE BINARY NOT NULL,
          state_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(state_sha256) = 64),
          expression_iri TEXT COLLATE BINARY NOT NULL,
          publisher_work_iri TEXT COLLATE BINARY NOT NULL,
          publisher_legal_resource_iri TEXT COLLATE BINARY NOT NULL,
          language TEXT COLLATE BINARY NOT NULL,
          rule_profiles_json TEXT COLLATE BINARY NOT NULL,
          article_identities_json TEXT COLLATE BINARY NOT NULL,
          PRIMARY KEY (work_key, applicability_date, expression_iri, language)
        ) STRICT;
        CREATE UNIQUE INDEX states_digest ON states(state_sha256);
        CREATE TABLE work_titles (
          work_identifier TEXT COLLATE BINARY NOT NULL,
          expression_iri TEXT COLLATE BINARY NOT NULL,
          language TEXT COLLATE BINARY NOT NULL,
          title TEXT COLLATE BINARY NOT NULL,
          normalized_title TEXT COLLATE BINARY NOT NULL,
          document_date TEXT COLLATE BINARY,
          title_kind TEXT COLLATE BINARY NOT NULL CHECK (title_kind IN ('title','title_short')),
          evidence_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(evidence_sha256) = 64),
          PRIMARY KEY (work_identifier, expression_iri, language, title, title_kind, evidence_sha256)
        ) STRICT;
        CREATE INDEX work_titles_normalized ON work_titles(normalized_title, work_identifier);
        CREATE TABLE relations (
          from_ref TEXT COLLATE BINARY NOT NULL REFERENCES articles(article_identity_sha256),
          ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
          edge_type TEXT COLLATE BINARY NOT NULL CHECK (edge_type IN ('cites')),
          asserted_by TEXT COLLATE BINARY NOT NULL CHECK (asserted_by IN ('publisher_text')),
          source_predicate TEXT COLLATE BINARY NOT NULL CHECK (source_predicate IN ('akn_ref')),
          in_note INTEGER NOT NULL CHECK (in_note IN (0, 1)),
          label TEXT COLLATE BINARY,
          href TEXT COLLATE BINARY,
          to_kind TEXT COLLATE BINARY NOT NULL CHECK (to_kind IN ('legilux_eli', 'other_uri', 'unparsed')),
          to_ref TEXT COLLATE BINARY,
          PRIMARY KEY (from_ref, ordinal),
          CHECK ((to_kind = 'unparsed') = (to_ref IS NULL))
        ) STRICT;
        CREATE INDEX relations_to_ref ON relations(to_ref, edge_type);
        CREATE TABLE work_facts (
          subject_iri TEXT COLLATE BINARY NOT NULL,
          predicate TEXT COLLATE BINARY NOT NULL,
          fact_kind TEXT COLLATE BINARY NOT NULL,
          object_kind TEXT COLLATE BINARY NOT NULL CHECK (object_kind IN ('iri', 'literal')),
          object_value TEXT COLLATE BINARY NOT NULL,
          datatype_iri TEXT COLLATE BINARY NOT NULL,
          language_tag TEXT COLLATE BINARY NOT NULL,
          evidence_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(evidence_sha256) = 64),
          PRIMARY KEY (subject_iri, predicate, object_kind, object_value, datatype_iri, language_tag, evidence_sha256)
        ) STRICT;
        """;

    /// <summary>
    /// Log /3's tables: log /2's, with each observation's derivation time (<c>derived_at</c>), the time a derivation from
    /// custody states it ran (its inputs' <c>derived_at</c>), null when the index's derivations are the acquiring build's own.
    /// </summary>
    private const string EventLogDdl = """
        CREATE TABLE events (
          seq INTEGER NOT NULL PRIMARY KEY CHECK (seq >= 1),
          scope TEXT COLLATE BINARY NOT NULL CHECK (scope IN ('state')),
          key TEXT COLLATE BINARY NOT NULL,
          event TEXT COLLATE BINARY NOT NULL CHECK (event IN ('first_sighting', 'expression_added', 'file_replaced', 'interval_closed', 'validity_revised')),
          observed_from TEXT COLLATE BINARY CHECK (observed_from IS NULL),
          detail_json TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE INDEX events_event_seq ON events(event, seq);
        CREATE INDEX events_work_seq ON events(json_extract(key, '$[0]'), seq);
        CREATE TABLE observations (
          observation INTEGER NOT NULL PRIMARY KEY CHECK (observation >= 1),
          corpus_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(corpus_sha256) = 64),
          predecessor_index_sha256 TEXT COLLATE BINARY CHECK (predecessor_index_sha256 IS NULL OR length(predecessor_index_sha256) = 64),
          first_seq INTEGER NOT NULL CHECK (first_seq >= 1),
          last_seq INTEGER NOT NULL CHECK (last_seq >= first_seq - 1),
          built_at TEXT COLLATE BINARY NOT NULL CHECK (built_at GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
          observed_from TEXT COLLATE BINARY CHECK (observed_from IS NULL),
          derived_at TEXT COLLATE BINARY CHECK (derived_at IS NULL OR derived_at GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
          CHECK ((observation = 1) = (predecessor_index_sha256 IS NULL))
        ) STRICT;
        CREATE TABLE log_stamp (
          log_stamp_id INTEGER NOT NULL PRIMARY KEY CHECK (log_stamp_id = 1),
          log_schema TEXT COLLATE BINARY NOT NULL,
          log_rows_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(log_rows_sha256) = 64)
        ) STRICT;
        """;

    /// <summary>Log /2's tables, exactly as schema 8 wrote them.</summary>
    internal const string LegacyEventLogDdl8 = """
        CREATE TABLE events (
          seq INTEGER NOT NULL PRIMARY KEY CHECK (seq >= 1),
          scope TEXT COLLATE BINARY NOT NULL CHECK (scope IN ('state')),
          key TEXT COLLATE BINARY NOT NULL,
          event TEXT COLLATE BINARY NOT NULL CHECK (event IN ('first_sighting', 'expression_added', 'file_replaced', 'interval_closed', 'validity_revised')),
          observed_from TEXT COLLATE BINARY CHECK (observed_from IS NULL),
          detail_json TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE INDEX events_event_seq ON events(event, seq);
        CREATE INDEX events_work_seq ON events(json_extract(key, '$[0]'), seq);
        CREATE TABLE observations (
          observation INTEGER NOT NULL PRIMARY KEY CHECK (observation >= 1),
          corpus_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(corpus_sha256) = 64),
          predecessor_index_sha256 TEXT COLLATE BINARY CHECK (predecessor_index_sha256 IS NULL OR length(predecessor_index_sha256) = 64),
          first_seq INTEGER NOT NULL CHECK (first_seq >= 1),
          last_seq INTEGER NOT NULL CHECK (last_seq >= first_seq - 1),
          built_at TEXT COLLATE BINARY NOT NULL CHECK (built_at GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]Z'),
          observed_from TEXT COLLATE BINARY CHECK (observed_from IS NULL),
          CHECK ((observation = 1) = (predecessor_index_sha256 IS NULL))
        ) STRICT;
        CREATE TABLE log_stamp (
          log_stamp_id INTEGER NOT NULL PRIMARY KEY CHECK (log_stamp_id = 1),
          log_schema TEXT COLLATE BINARY NOT NULL,
          log_rows_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(log_rows_sha256) = 64)
        ) STRICT;
        """;

    internal const string LegacyEventLogDdl6 = """
        CREATE TABLE events (
          seq INTEGER NOT NULL PRIMARY KEY CHECK (seq >= 1),
          scope TEXT COLLATE BINARY NOT NULL CHECK (scope IN ('state')),
          key TEXT COLLATE BINARY NOT NULL,
          event TEXT COLLATE BINARY NOT NULL CHECK (event IN ('first_sighting')),
          observed_from TEXT COLLATE BINARY CHECK (observed_from IS NULL),
          detail_json TEXT COLLATE BINARY NOT NULL,
          UNIQUE (scope, key, event)
        ) STRICT;
        CREATE INDEX events_event_seq ON events(event, seq);
        """;

    public static LuxembourgIndexBuildResult? TryBuild(
        Stage3DerivationProfileEnvelope envelope,
        DateTimeOffset builtAt,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail) =>
        TryBuild(envelope, predecessor: null, builtAt, derivedAt: null, out refusal, out detail);

    /// <summary>The build with no derivation time (the acquiring build's own derivations).</summary>
    public static LuxembourgIndexBuildResult? TryBuild(
        Stage3DerivationProfileEnvelope envelope,
        LuxembourgIndexPredecessor? predecessor,
        DateTimeOffset builtAt,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail) =>
        TryBuild(envelope, predecessor, builtAt, derivedAt: null, out refusal, out detail);

    /// <summary>
    /// Builds the index, its event log chained to <paramref name="predecessor"/> when one is given: the predecessor's
    /// observations and events carried forward unchanged (an exact prefix), then one observation of this build
    /// appending the comparison events (<c>first_sighting</c>, <c>expression_added</c>, <c>file_replaced</c>, and the
    /// derived <c>interval_closed</c> and <c>validity_revised</c>). A state the log holds and this build does not is
    /// silent (absence is not a withdrawal). With no predecessor the log is a genesis log. The observation records
    /// <paramref name="builtAt"/>, the build's time, which the caller captures once after every fetch and passes to
    /// each build it compares: UTC to the whole second, later than the predecessor's last build and no earlier than the
    /// corpus's EU capture, or the build refuses <see cref="LuxembourgIndexBuildRefusal.BuildTimeInvalid"/>. It is an
    /// upper bound on when the corpus was observed, never an observation time. <paramref name="derivedAt"/> is the time a
    /// derivation from custody states it ran (its inputs' <c>derived_at</c>), recorded on the observation as
    /// <c>derived_at</c>: a UTC whole second no earlier than the build time, or null when the derivations are the acquiring
    /// build's own. A predecessor of index 8 (log /2) is never carried forward: index 9 starts a new genesis log, and its
    /// build over one refuses <see cref="LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers"/>.
    /// </summary>
    public static LuxembourgIndexBuildResult? TryBuild(
        Stage3DerivationProfileEnvelope envelope,
        LuxembourgIndexPredecessor? predecessor,
        DateTimeOffset builtAt,
        DateTimeOffset? derivedAt,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        refusal = LuxembourgIndexBuildRefusal.None;
        detail = null;
        if (predecessor is not null && !string.Equals(predecessor.LogSchema, EventLogSchema, StringComparison.Ordinal))
        {
            refusal = LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers;
            detail = $"The predecessor's event log is {predecessor.LogSchema} (index 8); an index 9 build starts a new genesis log "
                + $"and carries only {EventLogSchema} forward.";
            return null;
        }

        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        if (corpus is null)
        {
            refusal = LuxembourgIndexBuildRefusal.CorpusRefused;
            detail = $"{corpusRefusal}: {corpusDetail}";
            return null;
        }

        var builtAtText = TryBuiltAt(builtAt, predecessor, corpus.VerifiedSet, out detail);
        if (builtAtText is null)
        {
            refusal = LuxembourgIndexBuildRefusal.BuildTimeInvalid;
            return null;
        }

        string? derivedAtText = null;
        if (derivedAt is { } derivation)
        {
            if (derivation.Offset != TimeSpan.Zero || derivation.Ticks % TimeSpan.TicksPerSecond != 0 || derivation < builtAt)
            {
                refusal = LuxembourgIndexBuildRefusal.BuildTimeInvalid;
                detail = $"The derivation time {derivation:O} is not a UTC whole second no earlier than the build time {builtAtText}.";
                return null;
            }

            derivedAtText = derivation.ToString(BuiltAtFormat, CultureInfo.InvariantCulture);
        }

        MemberRow[] members;
        ArticleRow[] articles;
        StateRow[] states;
        IntervalRow[] intervals;
        WorkTitleRow[] workTitles;
        WorkFactRow[] workFacts;
        try
        {
            if (!TryProjectRows(
                    envelope, corpus, builtAtText[..10], out members, out articles, out states, out intervals, out workTitles, out workFacts,
                    out refusal, out detail))
            {
                return null;
            }
        }
        catch (InvalidDataException exception)
        {
            refusal = LuxembourgIndexBuildRefusal.DerivationMismatch;
            detail = exception.Message;
            return null;
        }

        return WriteIndex(
            corpus.ArtifactRef, members, articles, states, intervals, workTitles, workFacts, BodiesByObjectRef(corpus.VerifiedSet),
            predecessor, builtAtText, derivedAtText, reader => reader.VerifyEventLogSources(corpus.VerifiedSet), out refusal, out detail);
    }

    /// <summary>
    /// The build's tail, from the projected rows: the relations and the event log (a genesis log, or the predecessor's
    /// carried forward and this observation's events appended), the stamps, the database, the measured capability manifest,
    /// and the reader's own verification of what was written (with the corpus's check of the log's source bodies when
    /// <paramref name="verifyWithCorpus"/> is given). <see cref="TryBuild(Stage3DerivationProfileEnvelope, LuxembourgIndexPredecessor?, DateTimeOffset, DateTimeOffset?, out LuxembourgIndexBuildRefusal, out string?)"/>
    /// runs it over an envelope's rows; a test harness runs it over rows of its own, so both go through one writer.
    /// </summary>
    internal static LuxembourgIndexBuildResult? WriteIndex(
        SourceArtifactRef corpusRef,
        MemberRow[] members,
        ArticleRow[] articles,
        StateRow[] states,
        IntervalRow[] intervals,
        WorkTitleRow[] workTitles,
        WorkFactRow[] workFacts,
        IReadOnlyDictionary<string, string> bodyByObjectRef,
        LuxembourgIndexPredecessor? predecessor,
        string builtAtText,
        string? derivedAtText,
        Action<LuxembourgIndexReader>? verifyWithCorpus,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail)
    {
        refusal = LuxembourgIndexBuildRefusal.None;
        detail = null;
        var relations = ProjectRelations(articles);
        IReadOnlyDictionary<string, string[]> sourceBodies;
        try
        {
            sourceBodies = SourceBodiesOfStates(articles, states, bodyByObjectRef);
        }
        catch (InvalidDataException exception)
        {
            refusal = LuxembourgIndexBuildRefusal.DerivationMismatch;
            detail = exception.Message;
            return null;
        }

        EventRow[] events;
        ObservationRow[] observations;
        if (predecessor is null)
        {
            events = ProjectGenesisEvents(states, sourceBodies);
            observations = ProjectGenesisObservations(corpusRef.Sha256, events.Length, builtAtText, derivedAtText);
        }
        else
        {
            var appended = ProjectChainedEvents(FoldLog(predecessor.Events), states, sourceBodies, predecessor.LastSeq + 1, out var changed);
            if (appended is null)
            {
                refusal = LuxembourgIndexBuildRefusal.PredecessorDerivationDiffers;
                detail = $"The state of {changed} has another digest from the same source bodies than the predecessor's log holds: this build's derivation differs from the predecessor's, or the catalogue moved the state's own facts, which no event of this log records; the change is not shown to be the publisher's text.";
                return null;
            }

            events = [.. predecessor.Events, .. appended];
            observations =
            [
                .. predecessor.Observations,
                new ObservationRow(
                    predecessor.Observations.Count + 1L, corpusRef.Sha256, predecessor.IndexSha256,
                    predecessor.LastSeq + 1, predecessor.LastSeq + appended.Length, builtAtText, null, derivedAtText),
            ];
        }

        var logicalRowsSha256 = HashLogicalRows(members, articles, states, workTitles, relations, workFacts, events, intervals);
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-{Guid.NewGuid():N}.sqlite");
        try
        {
            BuildDatabase(
                path, corpusRef.Sha256, logicalRowsSha256, members, articles, states, intervals, workTitles, relations, workFacts, events, observations);
            var bytes = File.ReadAllBytes(path);
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var indexRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
            var manifest = MeasureCapabilities(digest, articles, states, workTitles);
            using var manifestStream = new MemoryStream();
            var manifestDigest = V3IndexCapabilityManifestArtifact.Write(manifestStream, manifest);
            var manifestBytes = manifestStream.ToArray();
            var manifestRef = new SourceArtifactRef(
                LexCorpus6Builder.ResourceIdOf(manifestDigest), manifestDigest);
            manifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
                manifestRef, manifestBytes, PublisherId.LuLegilux, digest);
            using var verified = LuxembourgIndexReader.OpenAndVerify(
                indexRef, bytes, corpusRef, manifest);
            verifyWithCorpus?.Invoke(verified);
            return new LuxembourgIndexBuildResult(
                indexRef, bytes, manifestRef, manifestBytes, manifest);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException)
        {
            refusal = LuxembourgIndexBuildRefusal.IndexInvalid;
            detail = exception.Message;
            return null;
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static bool TryProjectRows(
        Stage3DerivationProfileEnvelope envelope,
        LexCorpus6BuildResult corpus,
        string builtAtDate,
        out MemberRow[] members,
        out ArticleRow[] articles,
        out StateRow[] states,
        out IntervalRow[] intervals,
        out WorkTitleRow[] workTitles,
        out WorkFactRow[] workFacts,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail)
    {
        intervals = [];
        members = corpus.VerifiedSet.Set.Members
            .Where(static member => member.Publisher == PublisherId.LuLegilux)
            .Select(static member => new MemberRow(
                member.ObjectRefSha256,
                member.SourceOrdinal,
                ContractWire.NameOf(member.Outcome),
                member.LuxembourgRights is null
                    ? null
                    : ContractWire.NameOf(member.LuxembourgRights.Disposition),
                JsonSerializer.Serialize(member.Stage3Outcomes.Select(static value => new
                {
                    domain = ContractWire.NameOf(value.Domain),
                    semantic_identity_sha256 = value.SemanticIdentitySha256,
                    disposition = ContractWire.NameOf(value.Disposition),
                })),
                JsonSerializer.Serialize(member.Gaps)))
            .OrderBy(static row => row.ObjectRefSha256, StringComparer.Ordinal)
            .ToArray();

        var memberByObject = corpus.VerifiedSet.Set.Members
            .Where(static member => member.Publisher == PublisherId.LuLegilux)
            .ToDictionary(static member => member.ObjectRefSha256, StringComparer.Ordinal);
        var sourceRecords = envelope.BodyComposition.Envelope.Luxembourg.CorpusRecordSet!.Set.Records;
        if (members.Length != sourceRecords.Count || memberByObject.Count != sourceRecords.Count)
        {
            articles = [];
            states = [];
            workTitles = [];
            workFacts = [];
            refusal = LuxembourgIndexBuildRefusal.PopulationMismatch;
            detail = "The Luxembourg corpus population is missing, duplicated or extra.";
            return false;
        }

        var projected = new List<ArticleRow>();
        var seenOutcomes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outcome in envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes)
        {
            if (!seenOutcomes.Add(outcome.SemanticIdentitySha256))
            {
                articles = [];
                states = [];
                workTitles = [];
                workFacts = [];
                refusal = LuxembourgIndexBuildRefusal.PopulationMismatch;
                detail = "The Luxembourg legal-content population contains a duplicate outcome.";
                return false;
            }

            var objectRefSha256 = Lex.V3.Contracts.Source.Scope.ScopeManifestCanonicalWriter
                .ComputeObjectRefSha256(outcome.SourceInventoryOutcome.Input.CorpusRecord.ObjectRef);
            if (!memberByObject.TryGetValue(objectRefSha256, out var member) ||
                !member.Stage3Outcomes.Any(value =>
                    value.Domain == LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent &&
                    string.Equals(value.SemanticIdentitySha256, outcome.SemanticIdentitySha256, StringComparison.Ordinal)))
            {
                articles = [];
                states = [];
                workTitles = [];
                workFacts = [];
                refusal = LuxembourgIndexBuildRefusal.DerivationMismatch;
                detail = objectRefSha256;
                return false;
            }

            if (outcome.Disposition is not (
                    LuxembourgAknLegalContentDisposition.Admitted or
                    LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence) ||
                member.Outcome != LexCorpus6OutcomeKind.Acquired)
            {
                continue;
            }

            if (outcome.Article is null || member.LuxembourgRights is null)
            {
                articles = [];
                states = [];
                workTitles = [];
                workFacts = [];
                refusal = LuxembourgIndexBuildRefusal.DerivationMismatch;
                detail = "An admitted article lacks its article or rights-bound WEMI.";
                return false;
            }

            var article = outcome.Article;
            // The article's place in its publisher document: its position among the inventory's top-level articles, which
            // the inventory lists in the publisher's order (index 9; S7a reads a state's articles in that order).
            var documentPosition = DocumentPositionOf(outcome.SourceInventoryOutcome.Inventory, article.Coordinate.PublisherId);
            if (documentPosition is null)
            {
                articles = [];
                states = [];
                workTitles = [];
                workFacts = [];
                refusal = LuxembourgIndexBuildRefusal.DerivationMismatch;
                detail = "An admitted article is not one of its inventory's articles: " + article.Coordinate.PublisherId;
                return false;
            }

            var language = LanguageToken(member.LuxembourgRights.SelectedWemi.LanguageIri);
            var date = ApplicabilityDate(article.Coordinate.PublisherApplicability);
            projected.Add(new ArticleRow(
                article.IdentitySha256,
                objectRefSha256,
                article.PublisherExpressionIri,
                article.Coordinate.PublisherId,
                article.Coordinate.PublisherWId,
                date,
                language,
                article.RuleProfileSha256,
                string.Concat(article.Tokens
                    .Where(static token => token.Kind is
                        LuxembourgAknLegalContentTokenKind.Text or
                        LuxembourgAknLegalContentTokenKind.Reference)
                    .Select(static token => token.Text)),
                TokensJson(article.Tokens),
                documentPosition));
        }

        articles = projected.OrderBy(static row => row.ArticleIdentitySha256, StringComparer.Ordinal).ToArray();
        var assertions = envelope.BodyComposition.Envelope.Luxembourg.TypedAssertions;
        workFacts = ProjectWorkFacts(assertions);

        // ---- Index 9's states, from the catalogue: the held bodies and what the derivations made of them, the records. ----
        var contentByObject = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes
            .GroupBy(static outcome => Lex.V3.Contracts.Source.Scope.ScopeManifestCanonicalWriter
                .ComputeObjectRefSha256(outcome.SourceInventoryOutcome.Input.CorpusRecord.ObjectRef), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<KeyValuePair<string, long>>)group
                    .GroupBy(static outcome => ContractWire.NameOf(outcome.Disposition), StringComparer.Ordinal)
                    .OrderBy(static counted => counted.Key, StringComparer.Ordinal)
                    .Select(static counted => new KeyValuePair<string, long>(counted.Key, counted.LongCount()))
                    .ToArray(),
                StringComparer.Ordinal);
        var bodies = new List<CatalogueBody>();
        foreach (var outcome in envelope.BodyComposition.Envelope.LuxembourgAknArticleInventoryPopulation.Outcomes)
        {
            var objectRefSha256 = Lex.V3.Contracts.Source.Scope.ScopeManifestCanonicalWriter
                .ComputeObjectRefSha256(outcome.Input.CorpusRecord.ObjectRef);
            if (!memberByObject.TryGetValue(objectRefSha256, out var member) || member.LuxembourgRights is not { } rights)
            {
                states = [];
                workTitles = [];
                refusal = LuxembourgIndexBuildRefusal.DerivationMismatch;
                detail = $"The held body of {outcome.Input.CorpusRecord.ObjectRef.PublisherUri} has no rights-bound corpus member.";
                return false;
            }

            var evidence = outcome.Inventory?.Evidence;
            bodies.Add(new CatalogueBody(
                objectRefSha256,
                rights.SelectedWemi.RootIri,
                rights.SelectedWemi.ExpressionIri,
                LanguageToken(rights.SelectedWemi.LanguageIri),
                ContractWire.NameOf(member.Outcome),
                ContractWire.NameOf(rights.Disposition),
                ContractWire.NameOf(outcome.Disposition),
                outcome.Detail,
                outcome.Inventory?.RuleProfileSha256,
                evidence is null ? null : ContractWire.NameOf(evidence.IdentityClass),
                evidence?.InFileApplicabilityDate ?? outcome.Inventory?.PublisherApplicabilityDate,
                outcome.ConsistencyFindings
                    .Select(static finding => $"{ContractWire.NameOf(finding.Check)}:{finding.NamedState}")
                    .ToArray(),
                contentByObject.GetValueOrDefault(objectRefSha256) ?? []));
        }

        var records = sourceRecords
            .Select(static record => (Record: record, ObjectRef: Lex.V3.Contracts.Source.Scope.ScopeManifestCanonicalWriter.ComputeObjectRefSha256(record.ObjectRef)))
            .Select(pair => new CatalogueRecord(
                pair.Record.ObjectRef.PublisherUri,
                pair.ObjectRef,
                ContractWire.NameOf(memberByObject[pair.ObjectRef].Outcome),
                ContractWire.NameOf(pair.Record.BodyDisposition)))
            .ToArray();
        (states, intervals) = ProjectCatalogueStates(articles, CatalogueFacts.From(workFacts), bodies, records, builtAtDate);
        var articlesBySubject = articles
            .SelectMany(static article => new[] { article.ExpressionIri, article.PublisherWid }
                .Where(static subject => subject is not null)
                .Select(subject => (Subject: subject!, Article: article)))
            .GroupBy(static value => value.Subject, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static value => value.Article).Distinct().ToArray(),
                StringComparer.Ordinal);
        var datesBySubject = assertions
            .Where(static value => value.FactDisposition.Predicate ==
                Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate.DateDocument)
            .GroupBy(static value => value.Assertion.SubjectIri, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => ExactDocumentDate(group.Select(
                    static value => value.Assertion.ObjectIriOrLexical)),
                StringComparer.Ordinal);
        // The legal resource each held expression's body names, for a title's document date (TitleDocumentDate); an
        // expression named with two legal resources has none.
        var legalResourceByExpression = bodies
            .GroupBy(static body => body.ExpressionIri, StringComparer.Ordinal)
            .Where(static group => group.Select(static body => body.RootIri).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.First().RootIri, StringComparer.Ordinal);
        // Index 9 keys a title by its work (the work key of the expression's legal resource), so a code's states are one
        // work; a title of an expression whose legal resource spells no state keeps index 8's key, its wId or expression.
        var workKeyByExpression = bodies
            .Where(static body => StateKindOf(body.RootIri) is not null)
            .GroupBy(static body => body.ExpressionIri, StringComparer.Ordinal)
            .Where(static group => group.Select(static body => body.RootIri).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(static group => group.Key, static group => WorkKeyOf(WorkOfState(group.First().RootIri)), StringComparer.Ordinal);
        workTitles = assertions
            .Where(static value => value.FactDisposition.Predicate is
                Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate.Title or
                Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate.TitleShort)
            .SelectMany(assertion => articlesBySubject.TryGetValue(
                    assertion.Assertion.SubjectIri, out var matches)
                ? matches.Select(article => new WorkTitleRow(
                    workKeyByExpression.GetValueOrDefault(article.ExpressionIri) ?? article.PublisherWid ?? article.ExpressionIri,
                    article.ExpressionIri,
                    article.Language,
                    assertion.Assertion.ObjectIriOrLexical,
                    NormalizeTitle(assertion.Assertion.ObjectIriOrLexical),
                    TitleDocumentDate(assertion.Assertion.SubjectIri, article, datesBySubject, legalResourceByExpression),
                    assertion.FactDisposition.Predicate ==
                        Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate.Title
                        ? "title"
                        : "title_short",
                    assertion.FactDisposition.EvidenceRef.Sha256))
                : [])
            .Where(static row => row.NormalizedTitle.Length != 0)
            .Distinct()
            .OrderBy(static row => row.WorkIdentifier, StringComparer.Ordinal)
            .ThenBy(static row => row.ExpressionIri, StringComparer.Ordinal)
            .ThenBy(static row => row.Language, StringComparer.Ordinal)
            .ThenBy(static row => row.Title, StringComparer.Ordinal)
            .ThenBy(static row => row.TitleKind, StringComparer.Ordinal)
            .ToArray();
        refusal = LuxembourgIndexBuildRefusal.None;
        detail = null;
        return true;
    }

    /// <summary>
    /// The publisher's typed assertions, verbatim, one row each: the subject, the predicate and its pinned
    /// fact kind as the closed vocabulary names them, the object as an IRI or a literal with its datatype and
    /// language tag (empty when the publisher gave none), and the digest of the observation the assertion
    /// was read from. Every admitted predicate is kept, so the table is the envelope's assertion list and
    /// not a selection made here; the operations that read it select by predicate. Nothing is interpreted:
    /// a status token or a date is the publisher's lexical value.
    /// </summary>
    internal static WorkFactRow[] ProjectWorkFacts(IReadOnlyList<LuxembourgTypedAssertion> assertions) =>
        assertions
            .Select(static value => new WorkFactRow(
                value.Assertion.SubjectIri,
                ContractWire.NameOf(value.FactDisposition.Predicate),
                ContractWire.NameOf(value.FactDisposition.FactKind),
                value.Assertion.ObjectKind == Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionObjectKind.Iri
                    ? "iri"
                    : "literal",
                value.Assertion.ObjectIriOrLexical,
                value.Assertion.DatatypeIriOrEmpty,
                value.Assertion.LanguageTagOrEmpty,
                value.FactDisposition.EvidenceRef.Sha256))
            .Distinct()
            .OrderBy(static row => row.SubjectIri, StringComparer.Ordinal)
            .ThenBy(static row => row.Predicate, StringComparer.Ordinal)
            .ThenBy(static row => row.ObjectKind, StringComparer.Ordinal)
            .ThenBy(static row => row.ObjectValue, StringComparer.Ordinal)
            .ThenBy(static row => row.DatatypeIri, StringComparer.Ordinal)
            .ThenBy(static row => row.LanguageTag, StringComparer.Ordinal)
            .ThenBy(static row => row.EvidenceSha256, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The event log of an index built from one observation with no predecessor (a genesis log): one
    /// <c>first_sighting</c> per state row, in the states table's order, numbered from 1. The key is the
    /// states table's primary key as a JSON array, which a later build keeps for the same state, so a
    /// comparison can say file_replaced rather than a withdrawal and a new sighting; the detail carries
    /// the state's digest. No observation time is held, so none is written: first_sighting here means
    /// first present in this log, not when the publisher published the text or when it was fetched.
    /// The detail also names the digests of the publisher bodies the state's articles were read from
    /// (<c>source_body_sha256</c>, sorted, each once; none for a textless state), so a later build that carries this log
    /// forward can tell a replaced publisher file from an unchanged one by the log alone; under log /3 it also carries the
    /// state's own publisher facts: the end the publisher states (<c>applicable_to</c>, <c>applicable_to_source</c>
    /// <c>publisher</c>, or both null: a derived end is not the state's and is reported by <c>interval_closed</c> and
    /// <c>validity_revised</c>), its text status and its publication date. A pure function of the states and their
    /// source bodies, so the reader recomputes it and refuses any other rows; the bodies themselves are checked
    /// against the corpus by whoever holds it (<see cref="LuxembourgIndexReader.VerifyEventLogSources"/>).
    /// </summary>
    internal static EventRow[] ProjectGenesisEvents(
        IReadOnlyList<StateRow> states, IReadOnlyDictionary<string, string[]> sourceBodies, string logSchema = EventLogSchema) =>
        ProjectChainedEvents(new Dictionary<string, LoggedState>(StringComparer.Ordinal), states, sourceBodies, 1, out _, logSchema)!;

    /// <summary>
    /// The events one observation appends to a log whose states are <paramref name="fold"/>, numbered from
    /// <paramref name="firstSeq"/>, key by key in the states table's key order over the keys the log holds and the keys
    /// this build holds (a state the log holds and this build does not stays held: absence is not a withdrawal):
    /// <list type="bullet">
    /// <item>a key this build holds and the log does not: <c>first_sighting</c>, or <c>expression_added</c> when the log
    /// holds the same work and date in another language;</item>
    /// <item>a key both hold whose source bodies differ: <c>file_replaced</c>, naming the state and bodies it replaces
    /// (the state's digest changes with its text, not with the bytes alone);</item>
    /// <item>then, for a key the log held, its applicability interval moved by this build's states: <c>interval_closed</c>
    /// when the log held it as the latest state of its work and language and a later one is now held, or
    /// <c>validity_revised</c> when its end moved because a state was inserted before it; both are derived from the
    /// publisher's dates, not asserted by the publisher, and say so. Under log /3 an end the publisher states is the
    /// state's end, so it never moves with the states around it, and a translation's interval is its French state's,
    /// reported there.</item>
    /// </list>
    /// A key both hold with the same bodies and another digest is not the publisher's change but this derivation's: null,
    /// naming the state. Under log /3 the digest also covers the state's catalogue facts, so a catalogue that moved a
    /// state's end or publication date without changing its bodies also refuses (no event of this log records that). A
    /// genesis log is the events appended to no log.
    /// </summary>
    internal static EventRow[]? ProjectChainedEvents(
        IReadOnlyDictionary<string, LoggedState> fold,
        IReadOnlyList<StateRow> states,
        IReadOnlyDictionary<string, string[]> sourceBodies,
        long firstSeq,
        out string? changedState,
        string logSchema = EventLogSchema)
    {
        var three = IsLogThree(logSchema);
        changedState = null;
        var current = new Dictionary<string, (StateRow Row, string[] Bodies)>(StringComparer.Ordinal);
        foreach (var row in states)
        {
            current[StateKey(row)] = (row, sourceBodies.TryGetValue(row.StateSha256, out var found)
                ? found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                : throw new InvalidDataException($"The state {row.StateSha256} has no source bodies for its event."));
        }

        var endsBefore = three
            ? ChainEnds(fold.Select(static pair => (pair.Key, pair.Value.WorkKey, pair.Value.ApplicabilityDate, pair.Value.Language, pair.Value.PublisherEnd)))
            : null;
        var endsAfter = three
            ? ChainEnds(fold.Where(pair => !current.ContainsKey(pair.Key))
                .Select(static pair => (pair.Key, pair.Value.WorkKey, pair.Value.ApplicabilityDate, pair.Value.Language, pair.Value.PublisherEnd))
                .Concat(current.Select(static pair => (pair.Key, pair.Value.Row.WorkKey, pair.Value.Row.ApplicabilityDate, pair.Value.Row.Language, pair.Value.Row.PublisherEnd))))
            : null;
        var legacyBefore = three ? null : IntervalEnds(fold.Values.Select(static held => (held.WorkKey, held.ApplicabilityDate, held.Language)));
        var legacyAfter = three
            ? null
            : IntervalEnds(fold.Values.Select(static held => (held.WorkKey, held.ApplicabilityDate, held.Language))
                .Concat(states.Select(static row => (row.WorkKey, row.ApplicabilityDate, row.Language))));
        var keys = fold.Values.Select(static held => (held.WorkKey, held.ApplicabilityDate, held.ExpressionIri, held.Language))
            .Concat(states.Select(static row => (row.WorkKey, row.ApplicabilityDate, row.ExpressionIri, row.Language)))
            .Distinct()
            .OrderBy(static key => key.WorkKey, StringComparer.Ordinal)
            .ThenBy(static key => key.ApplicabilityDate, StringComparer.Ordinal)
            .ThenBy(static key => key.ExpressionIri, StringComparer.Ordinal)
            .ThenBy(static key => key.Language, StringComparer.Ordinal);

        var appended = new List<EventRow>();
        void Append(string key, string name, string detail) =>
            appended.Add(new EventRow(firstSeq + appended.Count, "state", key, name, null, detail));

        foreach (var (work, date, expression, language) in keys)
        {
            var key = JsonSerializer.Serialize(new[] { work, date, expression, language });
            var heldBefore = fold.TryGetValue(key, out var held);
            var heldNow = current.TryGetValue(key, out var now);
            var stateSha256 = heldNow ? now.Row.StateSha256 : held!.StateSha256;
            var bodies = heldNow ? now.Bodies : held!.SourceBodies.ToArray();
            if (heldNow && !heldBefore)
            {
                var newLanguage = fold.Values.Any(other =>
                    string.Equals(other.WorkKey, work, StringComparison.Ordinal) &&
                    string.Equals(other.ApplicabilityDate, date, StringComparison.Ordinal) &&
                    !string.Equals(other.Language, language, StringComparison.Ordinal));
                Append(key, newLanguage ? V3EventRegistry.ExpressionAdded : V3EventRegistry.FirstSighting, three
                    ? JsonSerializer.Serialize(new
                    {
                        state_sha256 = stateSha256,
                        source_body_sha256 = bodies,
                        applicable_to = now.Row.PublisherEnd,
                        applicable_to_source = now.Row.PublisherEnd is null ? null : SourcePublisher,
                        text_status = now.Row.TextStatus,
                        publication_date = now.Row.PublicationDate,
                    })
                    : JsonSerializer.Serialize(new { state_sha256 = stateSha256, source_body_sha256 = bodies }));
                continue;
            }

            if (heldNow && heldBefore)
            {
                if (!held!.SourceBodies.SequenceEqual(bodies, StringComparer.Ordinal))
                {
                    Append(key, V3EventRegistry.FileReplaced, three
                        ? JsonSerializer.Serialize(new
                        {
                            state_sha256 = stateSha256,
                            source_body_sha256 = bodies,
                            applicable_to = now.Row.PublisherEnd,
                            applicable_to_source = now.Row.PublisherEnd is null ? null : SourcePublisher,
                            text_status = now.Row.TextStatus,
                            publication_date = now.Row.PublicationDate,
                            replaced_state_sha256 = held.StateSha256,
                            replaced_source_body_sha256 = held.SourceBodies,
                        })
                        : JsonSerializer.Serialize(new
                        {
                            state_sha256 = stateSha256,
                            source_body_sha256 = bodies,
                            replaced_state_sha256 = held.StateSha256,
                            replaced_source_body_sha256 = held.SourceBodies,
                        }));
                }
                else if (!string.Equals(held.StateSha256, stateSha256, StringComparison.Ordinal))
                {
                    changedState = $"/lu-legilux/{work}/{date} ({language})";
                    return null;
                }
            }

            string? before;
            string? after;
            if (three)
            {
                // A translation's interval is its French state's, whose own events report it.
                if (IsTranslationLanguage(language))
                {
                    continue;
                }

                before = endsBefore![key];
                after = endsAfter![key];
            }
            else
            {
                before = legacyBefore![(work, date, language)];
                after = legacyAfter![(work, date, language)];
            }

            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                continue;
            }

            Append(key, before is null ? V3EventRegistry.IntervalClosed : V3EventRegistry.ValidityRevised, JsonSerializer.Serialize(new
            {
                state_sha256 = stateSha256,
                source_body_sha256 = bodies,
                applicable_from = date,
                previous_to = before,
                new_to = after,
                derived = true,
            }));
        }

        return appended.ToArray();
    }

    private static bool IsLogThree(string logSchema) =>
        string.Equals(logSchema, EventLogSchema, StringComparison.Ordinal)
            ? true
            : string.Equals(logSchema, LegacyEventLogSchema8, StringComparison.Ordinal)
                ? false
                : throw new InvalidDataException($"The event log schema {logSchema} is not one this build reads.");

    /// <summary>
    /// Log /2's derived ends (index 8): each held date's end in its work and language, the next later date held in them, or
    /// null for the latest. Index 8 held no publisher end, so every end there is this derivation's.
    /// </summary>
    internal static IReadOnlyDictionary<(string WorkKey, string ApplicabilityDate, string Language), string?> IntervalEnds(
        IEnumerable<(string WorkKey, string ApplicabilityDate, string Language)> held)
    {
        var ends = new Dictionary<(string, string, string), string?>();
        foreach (var group in held.Distinct().GroupBy(static state => (state.WorkKey, state.Language)))
        {
            var dates = group.Select(static state => state.ApplicabilityDate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            for (var index = 0; index < dates.Length; index++)
            {
                ends[(group.Key.WorkKey, dates[index], group.Key.Language)] = index + 1 < dates.Length ? dates[index + 1] : null;
            }
        }

        return ends;
    }

    /// <summary>
    /// Log /3's ends (index 9), by state key, over each work's French chain (translations have none here): the end the
    /// publisher states, else the next later date of the chain (derived), else null. An end the publisher states does not
    /// move with the states around it, so a chained build that adds a state emits nothing for one whose end is stated.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> ChainEnds(
        IEnumerable<(string Key, string WorkKey, string ApplicabilityDate, string Language, string? PublisherEnd)> states)
    {
        var ends = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var work in states.Where(static state => !IsTranslationLanguage(state.Language)).GroupBy(static state => state.WorkKey, StringComparer.Ordinal))
        {
            var members = work.ToArray();
            var dates = members.Select(static state => state.ApplicabilityDate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            foreach (var state in members)
            {
                var index = Array.IndexOf(dates, state.ApplicabilityDate);
                ends[state.Key] = state.PublisherEnd ?? (index + 1 < dates.Length ? dates[index + 1] : null);
            }
        }

        return ends;
    }

    /// <summary>A state's key in the log: the states table's primary key as a JSON array, which a later build keeps for the same state.</summary>
    internal static string StateKey(StateRow row) =>
        JsonSerializer.Serialize(new[] { row.WorkKey, row.ApplicabilityDate, row.ExpressionIri, row.Language });

    /// <summary>
    /// What a log says one state key holds after all its events: the key's parts, and the digest and source bodies of its
    /// last event; under log /3 also the publisher facts of its last sighting (the end the publisher states, the text
    /// status, the publication date), null under log /2.
    /// </summary>
    internal sealed record LoggedState(
        string WorkKey,
        string ApplicabilityDate,
        string ExpressionIri,
        string Language,
        string StateSha256,
        IReadOnlyList<string> SourceBodies,
        string? PublisherEnd = null,
        string? TextStatus = null,
        string? PublicationDate = null);

    /// <summary>
    /// What a log says each state key holds, its events read in order (a later event of a key replaces an earlier one; an
    /// interval event, which carries no publisher facts, keeps the ones its key's last sighting stated). An event whose key
    /// or detail is not the shape its log schema writes refuses. Under log /3 a key may name an empty expression and
    /// language together (a state the catalogue names no expression of).
    /// </summary>
    internal static IReadOnlyDictionary<string, LoggedState> FoldLog(IReadOnlyList<EventRow> events, string logSchema = EventLogSchema)
    {
        var three = IsLogThree(logSchema);
        var fold = new Dictionary<string, LoggedState>(StringComparer.Ordinal);
        foreach (var value in events)
        {
            try
            {
                var key = JsonSerializer.Deserialize<string[]>(value.Key);
                if (key is not { Length: 4 } || key.Any(static part => part is null) ||
                    string.IsNullOrEmpty(key[0]) || string.IsNullOrEmpty(key[1]) ||
                    (three ? (key[2].Length == 0) != (key[3].Length == 0) : key[2].Length == 0 || key[3].Length == 0))
                {
                    throw new InvalidDataException("An event's key is not a state key.");
                }

                using var detail = JsonDocument.Parse(value.DetailJson);
                var state = detail.RootElement.GetProperty("state_sha256").GetString();
                if (state is not { Length: 64 })
                {
                    throw new InvalidDataException("An event names no state digest.");
                }

                var bodies = detail.RootElement.GetProperty("source_body_sha256").EnumerateArray()
                    .Select(static body => body.GetString() is { Length: 64 } digest && digest.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'))
                        ? digest
                        : throw new InvalidDataException("An event names a source body that is not a SHA-256 digest."))
                    .ToArray();
                var logged = new LoggedState(key[0], key[1], key[2], key[3], state, bodies);
                if (three)
                {
                    if (detail.RootElement.TryGetProperty("text_status", out var status))
                    {
                        var end = detail.RootElement.GetProperty("applicable_to");
                        var source = detail.RootElement.GetProperty("applicable_to_source");
                        var publication = detail.RootElement.GetProperty("publication_date");
                        var endText = end.ValueKind == JsonValueKind.Null ? null : end.GetString();
                        if ((endText is null) != (source.ValueKind == JsonValueKind.Null) ||
                            (endText is not null && (!IsExactDate(endText) || !string.Equals(source.GetString(), SourcePublisher, StringComparison.Ordinal))) ||
                            status.GetString() is not { Length: > 0 })
                        {
                            throw new InvalidDataException("An event's publisher facts are not the shape log /3 writes.");
                        }

                        logged = logged with
                        {
                            PublisherEnd = endText,
                            TextStatus = status.GetString(),
                            PublicationDate = publication.ValueKind == JsonValueKind.Null ? null : publication.GetString(),
                        };
                    }
                    else if (fold.TryGetValue(value.Key, out var sighted))
                    {
                        logged = logged with { PublisherEnd = sighted.PublisherEnd, TextStatus = sighted.TextStatus, PublicationDate = sighted.PublicationDate };
                    }
                    else
                    {
                        throw new InvalidDataException("A log /3 key's first event states no publisher facts.");
                    }
                }

                fold[value.Key] = logged;
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new InvalidDataException("An event of the log is not a state event this log schema writes.", exception);
            }
        }

        return fold;
    }

    /// <summary>The log's observations, with their derivation times when the log is /3 (index 9); log /2 records none.</summary>
    internal static ObservationRow[] ReadLogObservations(SqliteConnection connection, string logSchema = EventLogSchema)
    {
        var withDerivedAt = string.Equals(logSchema, EventLogSchema, StringComparison.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = withDerivedAt
            ? "SELECT observation,corpus_sha256,predecessor_index_sha256,first_seq,last_seq,built_at,observed_from,derived_at FROM observations ORDER BY observation"
            : "SELECT observation,corpus_sha256,predecessor_index_sha256,first_seq,last_seq,built_at,observed_from FROM observations ORDER BY observation";
        using var reader = command.ExecuteReader();
        var values = new List<ObservationRow>();
        while (reader.Read()) values.Add(new(
            reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
            withDerivedAt && !reader.IsDBNull(7) ? reader.GetString(7) : null));
        return values.ToArray();
    }

    internal static EventRow[] ReadLogEvents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT seq,scope,key,event,observed_from,detail_json FROM events ORDER BY seq";
        using var reader = command.ExecuteReader();
        var values = new List<EventRow>();
        while (reader.Read()) values.Add(new(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
        return values.ToArray();
    }

    /// <summary>
    /// The reader's check of an index's event log: numbered as observations appending events; the last observation of
    /// this index's corpus; every state of the index held by the log at its own digest; and the last observation's
    /// events exactly those it must append to the log before it (for a genesis log, to no log). The source bodies are
    /// taken from the log, which only the corpus can confirm (<see cref="LuxembourgIndexReader.VerifyEventLogSources"/>);
    /// that the log before the last observation is the predecessor's is proven when the build carries it forward.
    /// </summary>
    internal static void ValidateLog(
        string corpusSha256,
        IReadOnlyList<StateRow> states,
        IReadOnlyList<ObservationRow> observations,
        IReadOnlyList<EventRow> events,
        string logSchema = EventLogSchema)
    {
        ValidateLogNumbering(observations, events);
        var last = observations[^1];
        if (!string.Equals(last.CorpusSha256, corpusSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Luxembourg event log's last observation is not of this index's corpus.");
        }

        try
        {
            ValidateLogHistory(observations, events, logSchema);
            var held = FoldLog(events, logSchema);
            var sourceBodies = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var state in states)
            {
                if (!held.TryGetValue(StateKey(state), out var logged) ||
                    !string.Equals(logged.StateSha256, state.StateSha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A state is not held by the log at its own digest.");
                }

                sourceBodies[state.StateSha256] = logged.SourceBodies.ToArray();
            }

            var before = events.Take(checked((int)(last.FirstSeq - 1))).ToArray();
            var expected = ProjectChainedEvents(FoldLog(before, logSchema), states, sourceBodies, last.FirstSeq, out _, logSchema);
            if (expected is null || !expected.SequenceEqual(events.Skip(before.Length)))
            {
                throw new InvalidDataException("The last observation's events are not the ones it appends.");
            }
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException("The Luxembourg index event rows are not the log of its states.", exception);
        }
    }

    /// <summary>
    /// The log's history, replayed from the log alone (review of #866): every observation's events must be exactly the
    /// ones it appends to the log before it, given the states it leaves the log holding (every state held before, with
    /// those it replaced, and those it sighted), so an event a build could not have written refuses wherever it sits in
    /// the log, not only in its last observation. A genesis observation can hold only <c>first_sighting</c>: before it the
    /// log holds nothing.
    /// </summary>
    internal static void ValidateLogHistory(IReadOnlyList<ObservationRow> observations, IReadOnlyList<EventRow> events, string logSchema = EventLogSchema)
    {
        foreach (var observation in observations)
        {
            var before = events.Take(checked((int)(observation.FirstSeq - 1))).ToArray();
            var appended = events.Skip(before.Length).Take(checked((int)(observation.LastSeq - observation.FirstSeq + 1))).ToArray();
            var after = FoldLog([.. before, .. appended], logSchema);
            var states = after.Values
                .Select(static held => new StateRow(
                    held.WorkKey, held.ApplicabilityDate, held.StateSha256, held.ExpressionIri, string.Empty, string.Empty, held.Language, "[]", "[]")
                {
                    ApplicableTo = held.PublisherEnd,
                    ApplicableToSource = held.PublisherEnd is null ? null : SourcePublisher,
                    TextStatus = held.TextStatus,
                    PublicationDate = held.PublicationDate,
                })
                .ToArray();
            var bodies = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var held in after.Values)
            {
                bodies[held.StateSha256] = held.SourceBodies.ToArray();
            }

            var expected = ProjectChainedEvents(FoldLog(before, logSchema), states, bodies, observation.FirstSeq, out _, logSchema);
            if (expected is null || !expected.SequenceEqual(appended))
            {
                throw new InvalidDataException(
                    $"The Luxembourg event log's observation {observation.Observation} holds events that are not the ones it appends.");
            }
        }
    }

    /// <summary>
    /// The log's numbering: observations 1, 2, ... in order, each appending the events after the previous one's (the
    /// first from 1), the last ending at the log's last event, and the events numbered 1, 2, ... without a gap; each
    /// observation's build time a real UTC second later than the one before.
    /// </summary>
    internal static void ValidateLogNumbering(IReadOnlyList<ObservationRow> observations, IReadOnlyList<EventRow> events)
    {
        if (observations.Count == 0)
        {
            throw new InvalidDataException("The Luxembourg event log's observations do not number its events.");
        }

        for (var index = 0; index < observations.Count; index++)
        {
            var observation = observations[index];
            var first = index == 0 ? 1 : observations[index - 1].LastSeq + 1;
            if (observation.Observation != index + 1 || observation.FirstSeq != first || observation.LastSeq < observation.FirstSeq - 1 ||
                (observation.Observation == 1) != (observation.PredecessorIndexSha256 is null) || observation.ObservedFrom is not null)
            {
                throw new InvalidDataException("The Luxembourg event log's observations do not number its events.");
            }

            if (ParseBuiltAt(observation.BuiltAt) is not { } builtAt ||
                (index > 0 && builtAt <= ParseBuiltAt(observations[index - 1].BuiltAt)))
            {
                throw new InvalidDataException("The Luxembourg event log's build times are not UTC seconds, each later than the one before.");
            }

            if (observation.DerivedAt is { } derivedAt && (ParseBuiltAt(derivedAt) is not { } derived || derived < builtAt))
            {
                throw new InvalidDataException("The Luxembourg event log's derivation times are not UTC seconds no earlier than their build times.");
            }
        }

        if (observations[^1].LastSeq != events.Count || events.Select(static (value, index) => value.Seq == index + 1).Any(static ok => !ok))
        {
            throw new InvalidDataException("The Luxembourg event log's observations do not number its events.");
        }
    }

    /// <summary>
    /// The observations of a genesis log: one, of this build's corpus, with no predecessor, numbering every event the
    /// log holds (an empty log numbers none: first 1, last 0), at the build's time. No observation time is held, so
    /// none is written.
    /// </summary>
    internal static ObservationRow[] ProjectGenesisObservations(string corpusSha256, int eventCount, string builtAt, string? derivedAt = null) =>
        [new ObservationRow(1, corpusSha256, null, 1, eventCount, builtAt, null, derivedAt)];

    /// <summary>A build time in <see cref="BuiltAtFormat"/>, or null when the text is not one.</summary>
    internal static DateTimeOffset? ParseBuiltAt(string text) =>
        DateTimeOffset.TryParseExact(
            text, BuiltAtFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;

    /// <summary>
    /// The build time this build records, or null with the reason it cannot be: it must be UTC to the whole second
    /// (the caller rounds up, so the bound stays an upper one), later than the predecessor's last build (a log's
    /// builds are in time order), and no earlier than the corpus's EU capture, the one observation clock the corpus
    /// holds. Nothing can check it against the Luxembourg fetches, whose times the corpus does not hold.
    /// </summary>
    private static string? TryBuiltAt(
        DateTimeOffset builtAt,
        LuxembourgIndexPredecessor? predecessor,
        VerifiedLexCorpus6ManifestSet corpus,
        out string? detail)
    {
        if (builtAt.Offset != TimeSpan.Zero || builtAt.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            detail = $"The build time {builtAt:O} is not a UTC whole second.";
            return null;
        }

        var text = builtAt.ToString(BuiltAtFormat, CultureInfo.InvariantCulture);
        if (predecessor is not null && ParseBuiltAt(predecessor.Observations[^1].BuiltAt) is { } previous && builtAt <= previous)
        {
            detail = $"The build time {text} is not later than the predecessor's build at {predecessor.Observations[^1].BuiltAt}.";
            return null;
        }

        if (EarlierThanEuCapture(builtAt, corpus) is { } reason)
        {
            detail = $"The build time {text} {reason}.";
            return null;
        }

        detail = null;
        return text;
    }

    /// <summary>
    /// Why a build time cannot be an upper bound on the corpus's observation, by the one observation clock the corpus
    /// holds (its EU legal notice's capture), or null when it can be.
    /// </summary>
    internal static string? EarlierThanEuCapture(DateTimeOffset builtAt, VerifiedLexCorpus6ManifestSet corpus) =>
        EarlierThanEuCapture(builtAt, corpus.Set.EuropeRightsMatrix.CapturedAt);

    internal static string? EarlierThanEuCapture(DateTimeOffset builtAt, string capturedAt)
    {
        if (!DateTimeOffset.TryParse(
                capturedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var captured))
        {
            return $"cannot be held to the corpus's EU capture, whose time {capturedAt} is not a timestamp";
        }

        return builtAt < captured ? $"is earlier than the corpus's EU capture at {capturedAt}" : null;
    }

    /// <summary>The digest of the event log alone (its observations and events), as <c>log_stamp</c> records it: log /3's.</summary>
    internal static string HashEventLog(IReadOnlyList<ObservationRow> observations, IReadOnlyList<EventRow> events) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new EventLogRows(observations, events))));

    /// <summary>The same digest as log /2 (index 8) recorded it, over observations with no derivation time.</summary>
    internal static string HashLegacyEventLog8(IReadOnlyList<ObservationRow> observations, IReadOnlyList<EventRow> events) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new LegacyEventLogRows8(
            observations.Select(static row => row.DerivedAt is null
                ? new LegacyObservationRow8(row.Observation, row.CorpusSha256, row.PredecessorIndexSha256, row.FirstSeq, row.LastSeq, row.BuiltAt, row.ObservedFrom)
                : throw new InvalidDataException("A log /2 observation records no derivation time.")).ToArray(),
            events))));

    /// <summary>The log digest of the given log schema.</summary>
    internal static string HashEventLogOf(string logSchema, IReadOnlyList<ObservationRow> observations, IReadOnlyList<EventRow> events) =>
        string.Equals(logSchema, EventLogSchema, StringComparison.Ordinal)
            ? HashEventLog(observations, events)
            : HashLegacyEventLog8(observations, events);

    /// <summary>The body digest of every Luxembourg member that holds one, by the member's object reference.</summary>
    internal static IReadOnlyDictionary<string, string> BodiesByObjectRef(VerifiedLexCorpus6ManifestSet corpus) =>
        BodiesByObjectRef(corpus.Set.Members);

    /// <summary>The same, from a mount's view of the corpus, which keeps every member that holds a body.</summary>
    internal static IReadOnlyDictionary<string, string> BodiesByObjectRef(LexCorpus6MountView corpus) =>
        BodiesByObjectRef(corpus.HeldMembers);

    private static IReadOnlyDictionary<string, string> BodiesByObjectRef(IEnumerable<LexCorpus6Member> members) =>
        members
            .Where(static member => member.Publisher == PublisherId.LuLegilux && member.BodySha256 is not null)
            .ToDictionary(static member => member.ObjectRefSha256, static member => member.BodySha256!, StringComparer.Ordinal);

    /// <summary>
    /// The publisher bodies each state's articles were read from: each article identity's member, by its object
    /// reference, and that member's body digest, sorted and each once. A member that holds no body (one not acquired)
    /// names none: a build reads articles only from held bodies, so it never meets one, and a body-less member has no
    /// digest a replaced file could change.
    /// </summary>
    internal static IReadOnlyDictionary<string, string[]> SourceBodiesOfStates(
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<StateRow> states,
        IReadOnlyDictionary<string, string> bodyByObjectRef)
    {
        var objectByArticle = articles.ToDictionary(static article => article.ArticleIdentitySha256, static article => article.ObjectRefSha256, StringComparer.Ordinal);
        var sources = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            var identities = JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)
                ?? throw new InvalidDataException($"The state {state.StateSha256} names no article identities.");
            sources[state.StateSha256] = identities
                .Select(identity => objectByArticle.TryGetValue(identity, out var objectRef)
                    ? objectRef
                    : throw new InvalidDataException($"The state {state.StateSha256} names an article the index does not hold."))
                .Where(bodyByObjectRef.ContainsKey)
                .Select(objectRef => bodyByObjectRef[objectRef])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        return sources;
    }

    private static string LanguageToken(string iri)
    {
        var token = iri[(iri.LastIndexOf('/') + 1)..].ToLowerInvariant();
        if (token.Length != 3 || token.Any(static value => value is < 'a' or > 'z'))
        {
            throw new InvalidDataException("A Luxembourg language IRI does not end in a three-letter authority token.");
        }
        return token;
    }

    /// <summary>
    /// The stored token stream of one article, exactly as the <c>articles.tokens_json</c> column
    /// holds it. One function, so a test can digest what the index stores.
    /// </summary>
    /// <remarks>
    /// A token that carries none of content /3's marks is written as it always was (<c>kind</c>, <c>text</c>,
    /// <c>target</c>, <c>marker</c> and, at the top level, <c>note_body</c>), so a content /1 or /2 stream keeps its bytes.
    /// A token that carries one is written with the same members first and then only the marks it carries:
    /// <c>underline</c>, <c>unmarked</c>, <c>quotation</c> and <c>marks_in_wording</c>.
    /// </remarks>
    internal static string TokensJson(IReadOnlyList<LuxembourgAknLegalContentToken> tokens) =>
        JsonSerializer.Serialize(tokens.Select(static token => StoredToken(token, nested: false)));

    private static object StoredToken(LuxembourgAknLegalContentToken token, bool nested)
    {
        var body = token.NoteBody?.Select(static inner => StoredToken(inner, nested: true));
        if (!token.UsesContent3)
        {
            return nested
                ? new
                {
                    kind = ContractWire.NameOf(token.Kind),
                    text = token.Text,
                    target = token.Target,
                    marker = token.Marker,
                }
                : new
                {
                    kind = ContractWire.NameOf(token.Kind),
                    text = token.Text,
                    target = token.Target,
                    marker = token.Marker,
                    note_body = body,
                };
        }

        var stored = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = ContractWire.NameOf(token.Kind),
            ["text"] = token.Text,
            ["target"] = token.Target,
            ["marker"] = token.Marker,
        };
        if (!nested)
        {
            stored["note_body"] = body;
        }
        if (token.Underline)
        {
            stored["underline"] = true;
        }
        if (token.Unmarked)
        {
            stored["unmarked"] = true;
        }
        if (token.Quotation is not null)
        {
            stored["quotation"] = token.Quotation;
            stored["marks_in_wording"] = token.MarksInWording;
        }
        return stored;
    }

    private static string? ApplicabilityDate(string? value)
    {
        if (value is null)
        {
            return null;
        }
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            throw new InvalidDataException("A publisher applicability value is not an exact civil date.");
        }
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The forward edges of lane R4's edge table: every reference the publisher wrote in each article, in the
    /// article's identity order and then the order the references occur in its stored token stream, a footnote
    /// body's references at the note reference and flagged <see cref="RelationRow.InNote"/>. Read from
    /// <see cref="ArticleRow.TokensJson"/> and from nothing else, so the reader can recompute exactly these rows
    /// from the articles it holds and refuse an index whose table is not them.
    /// </summary>
    internal static RelationRow[] ProjectRelations(IReadOnlyList<ArticleRow> articles)
    {
        var rows = new List<RelationRow>();
        foreach (var article in articles.OrderBy(static row => row.ArticleIdentitySha256, StringComparer.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(article.TokensJson);
                var ordinal = 0;
                foreach (var token in document.RootElement.EnumerateArray())
                {
                    var kind = token.GetProperty("kind").GetString();
                    if (string.Equals(kind, "reference", StringComparison.Ordinal))
                    {
                        rows.Add(RelationOf(article.ArticleIdentitySha256, ordinal++, inNote: false, token));
                    }
                    else if (string.Equals(kind, "note_reference", StringComparison.Ordinal) &&
                             token.TryGetProperty("note_body", out var body) &&
                             body.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var nested in body.EnumerateArray())
                        {
                            if (string.Equals(nested.GetProperty("kind").GetString(), "reference", StringComparison.Ordinal))
                            {
                                rows.Add(RelationOf(article.ArticleIdentitySha256, ordinal++, inNote: true, nested));
                            }
                        }
                    }
                }
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                throw new InvalidDataException(
                    $"The stored token stream of article {article.ArticleIdentitySha256} is not a list of tokens the relation rows can be read from.",
                    exception);
            }
        }

        return rows.ToArray();
    }

    private static RelationRow RelationOf(string fromRef, int ordinal, bool inNote, JsonElement token)
    {
        static string? StringOf(JsonElement token, string name) =>
            token.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var href = StringOf(token, "target");
        var (toKind, toRef) = ClassifyTarget(href);
        return new RelationRow(
            fromRef, ordinal, "cites", "publisher_text", "akn_ref", inNote, StringOf(token, "text"), href, toKind, toRef);
    }

    private const string LegiluxEliRoot = "http://data.legilux.public.lu";

    /// <summary>
    /// What a reference's value names, by a fixed grammar that resolves nothing. <c>legilux_eli</c> is a value that
    /// begins <c>/eli/</c> or <c>http://data.legilux.public.lu/eli/</c>, exactly, with something after it, and its
    /// target is the absolute form: the one change made is putting the host in front of a relative value. No trailing
    /// slash is trimmed, no scheme or case is changed and nothing after the path is dropped, so a target can only
    /// match a work by string equality with its publisher work IRI. <c>other_uri</c> is any other absolute http or
    /// https value, and its target is the value as written. <c>unparsed</c> is everything else (an empty value,
    /// <c>???</c>, a relative path that is not an ELI) and **any value with a whitespace or control character in it,
    /// whatever it begins with**: it is not a URI, and calling it <c>legilux_eli</c> would say it names a work. Its
    /// target is null, because nothing is ever named from a value the publisher did not give. None of the 74 values
    /// the two retained acts carry in their admitted articles has such a character (pinned by a test), so the rule
    /// changes no edge they carry.
    /// </summary>
    internal static (string Kind, string? ToRef) ClassifyTarget(string? href)
    {
        if (string.IsNullOrEmpty(href) ||
            href.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return ("unparsed", null);
        }

        if (href.StartsWith("/eli/", StringComparison.Ordinal) && href.Length > "/eli/".Length)
        {
            return ("legilux_eli", LegiluxEliRoot + href);
        }

        if (href.StartsWith(LegiluxEliRoot + "/eli/", StringComparison.Ordinal) &&
            href.Length > LegiluxEliRoot.Length + "/eli/".Length)
        {
            return ("legilux_eli", href);
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            return ("other_uri", href);
        }

        return ("unparsed", null);
    }

    internal static string WorkKeyOf(string publisherWid)
    {
        if (!Uri.TryCreate(publisherWid, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
            !string.Equals(uri.Host, "data.legilux.public.lu", StringComparison.Ordinal) ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidDataException("A Luxembourg publisher work identity cannot mint a product work key.");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var prefix = new[] { "eli", "etat", "leg" };
        if (segments.Length <= prefix.Length ||
            !segments.Take(prefix.Length).SequenceEqual(prefix, StringComparer.Ordinal) ||
            segments.Skip(prefix.Length).Any(static segment =>
                segment.Length == 0 || segment.Any(character =>
                    character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_')))
        {
            throw new InvalidDataException("A Luxembourg publisher work identity has no canonical V3 work-key projection.");
        }

        return string.Join('-', segments.Skip(prefix.Length));
    }

    internal static string StateSha256(
        string workKey,
        string applicabilityDate,
        string expressionIri,
        string publisherWorkIri,
        string publisherLegalResourceIri,
        string language,
        IReadOnlyList<string> profiles,
        IReadOnlyList<string> articleIdentities)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        static void Append(IncrementalHash target, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            target.AppendData(length);
            target.AppendData(bytes);
        }

        Append(hash, "lex-v3-luxembourg-expression-state/1");
        Append(hash, "lu-legilux");
        Append(hash, workKey);
        Append(hash, applicabilityDate);
        Append(hash, expressionIri);
        Append(hash, publisherWorkIri);
        Append(hash, publisherLegalResourceIri);
        Append(hash, language);
        foreach (var profile in profiles) Append(hash, profile);
        foreach (var identity in articleIdentities) Append(hash, identity);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// A title's document date: the publisher's <c>dateDocument</c> on the title's own subject, else on the article's work
    /// identifier (its expression when it has none), else on the expression's legal resource, else the article's own
    /// applicability date. The publisher dates its journal documents on the legal resource (<c>.../jo</c>) and titles them
    /// on the expression (<c>.../jo/fr</c>), so before the legal resource was asked none of the Luxembourg population's
    /// 2,021 titles had a date (the mounted gates of 2026-10-13).
    /// </summary>
    internal static string? TitleDocumentDate(
        string titleSubject,
        ArticleRow article,
        IReadOnlyDictionary<string, string> datesBySubject,
        IReadOnlyDictionary<string, string> legalResourceByExpression) =>
        datesBySubject.GetValueOrDefault(titleSubject) ??
        datesBySubject.GetValueOrDefault(article.PublisherWid ?? article.ExpressionIri) ??
        (legalResourceByExpression.TryGetValue(article.ExpressionIri, out var legalResource)
            ? datesBySubject.GetValueOrDefault(legalResource)
            : null) ??
        article.ApplicabilityDate;

    private static string ExactDocumentDate(IEnumerable<string> values)
    {
        var dates = values.Select(ApplicabilityDate).Distinct(StringComparer.Ordinal).ToArray();
        if (dates.Length != 1 || dates[0] is null)
        {
            throw new InvalidDataException(
                "A title subject has conflicting publisher document dates.");
        }
        return dates[0]!;
    }

    private static void BuildDatabase(
        string path,
        string corpusSha256,
        string logicalRowsSha256,
        IReadOnlyList<MemberRow> members,
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<StateRow> states,
        IReadOnlyList<IntervalRow> intervals,
        IReadOnlyList<WorkTitleRow> workTitles,
        IReadOnlyList<RelationRow> relations,
        IReadOnlyList<WorkFactRow> workFacts,
        IReadOnlyList<EventRow> events,
        IReadOnlyList<ObservationRow> observations)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWriteCreate);
        Execute(connection, "PRAGMA page_size=4096");
        Execute(connection, "PRAGMA encoding='UTF-8'");
        Execute(connection, "PRAGMA auto_vacuum=NONE");
        Execute(connection, "PRAGMA journal_mode=DELETE");
        Execute(connection, "PRAGMA synchronous=FULL");
        Execute(connection, "PRAGMA foreign_keys=ON");
        Execute(connection, $"PRAGMA application_id={ApplicationId}");
        Execute(connection, "PRAGMA user_version=9");
        using var transaction = connection.BeginTransaction();
        Execute(connection, Ddl, transaction);
        foreach (var member in members)
        {
            Insert(connection, transaction,
                "INSERT INTO members VALUES($p0,$p1,$p2,$p3,$p4,$p5)",
                member.ObjectRefSha256, member.SourceOrdinal, member.Outcome,
                member.RightsDisposition, member.Stage3OutcomesJson, member.GapsJson);
        }
        foreach (var article in articles)
        {
            Insert(connection, transaction,
                "INSERT INTO articles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10)",
                article.ArticleIdentitySha256, article.ObjectRefSha256, article.ExpressionIri,
                article.PublisherId, article.PublisherWid, article.ApplicabilityDate,
                article.Language, article.RuleProfileSha256, article.SearchableText, article.TokensJson,
                article.DocumentPosition ?? throw new InvalidDataException("An index 9 article has no document position."));
        }
        foreach (var relation in relations)
        {
            Insert(connection, transaction,
                "INSERT INTO relations VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9)",
                relation.FromRef, relation.Ordinal, relation.EdgeType, relation.AssertedBy,
                relation.SourcePredicate, relation.InNote ? 1 : 0, relation.Label, relation.Href,
                relation.ToKind, relation.ToRef);
        }
        foreach (var state in states)
        {
            Insert(connection, transaction,
                "INSERT INTO states VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13,$p14,$p15,$p16,$p17,$p18,$p19,$p20,$p21)",
                state.WorkKey, state.ApplicabilityDate, state.StateSha256, state.ExpressionIri,
                state.PublisherWorkIri, state.PublisherLegalResourceIri, state.Language, state.RuleProfilesJson,
                state.ArticleIdentitiesJson, state.StateKind, state.DateRole, state.ApplicableTo, state.ApplicableToSource,
                state.Boundary, state.PublicationDate, state.TextStatus, state.TextStatusReason, state.InFileIdentity,
                state.InFileDate, state.TranslationOf, state.Provisional ? 1 : 0, state.EntryIntoForce);
        }
        foreach (var interval in intervals)
        {
            Insert(connection, transaction,
                "INSERT INTO intervals VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6)",
                interval.WorkKey, interval.Ordinal, interval.Kind, interval.FromStateSha256, interval.ToStateSha256,
                interval.SpanFrom, interval.SpanTo);
        }
        foreach (var title in workTitles)
        {
            Insert(connection, transaction,
                "INSERT INTO work_titles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7)",
                title.WorkIdentifier, title.ExpressionIri, title.Language, title.Title,
                title.NormalizedTitle, title.DocumentDate, title.TitleKind, title.EvidenceSha256);
        }
        foreach (var fact in workFacts)
        {
            Insert(connection, transaction,
                "INSERT INTO work_facts VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7)",
                fact.SubjectIri, fact.Predicate, fact.FactKind, fact.ObjectKind, fact.ObjectValue,
                fact.DatatypeIri, fact.LanguageTag, fact.EvidenceSha256);
        }
        foreach (var value in events)
        {
            Insert(connection, transaction,
                "INSERT INTO events VALUES($p0,$p1,$p2,$p3,$p4,$p5)",
                value.Seq, value.Scope, value.Key, value.Event, value.ObservedFrom, value.DetailJson);
        }
        foreach (var observation in observations)
        {
            Insert(connection, transaction,
                "INSERT INTO observations VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7)",
                observation.Observation, observation.CorpusSha256, observation.PredecessorIndexSha256,
                observation.FirstSeq, observation.LastSeq, observation.BuiltAt, observation.ObservedFrom, observation.DerivedAt);
        }
        Insert(connection, transaction, "INSERT INTO log_stamp VALUES(1,$p0,$p1)", EventLogSchema, HashEventLog(observations, events));
        var provenance = SqliteProvenance.Read(connection);
        Insert(connection, transaction, "INSERT INTO stamp VALUES(1,$p0,$p1,$p2,$p3,$p4,$p5)",
            Schema, corpusSha256, logicalRowsSha256, provenance.Version,
            provenance.SourceId, provenance.CompileOptionsSha256);
        transaction.Commit();
        Execute(connection, "PRAGMA optimize");
    }

    /// <summary>
    /// The index of a fixed logical input, whose bytes a test pins: one held act and a textless consolidation of its work
    /// that the publisher ends, so every column of index 9's states, an interval transition and a derivation time are written.
    /// </summary>
    internal static byte[] BuildFixedInputDeterminismEvidence()
    {
        const string Work = "http://data.legilux.public.lu/eli/etat/leg/loi/2024/01/01/n1";
        const string Act = Work + "/jo";
        const string Consolidation = Work + "/consolide/20250101";
        var member = new MemberRow(
            new string('1', 64), 0, "acquired", "agreed_same_run_cc_by", "[]", "[]");
        var article = new ArticleRow(
            new string('2', 64), member.ObjectRefSha256,
            Act + "/fr", "art_1",
            Work, null,
            "fra", new string('4', 64), "libellé fixe",
            TokensJson(
            [
                new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Text, "libellé fixe", null, null),
                new LuxembourgAknLegalContentToken(
                    LuxembourgAknLegalContentTokenKind.Reference, "loi fixe", "/eli/etat/leg/loi/2023/01/01/n1/jo", null),
                new LuxembourgAknLegalContentToken(
                    LuxembourgAknLegalContentTokenKind.NoteReference, null, null, "1",
                    [new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Reference, "note fixe", "???", null)]),
            ]),
            1);
        var members = new[] { member };
        var articles = new[] { article };
        static WorkFactRow Fact(string subject, string predicate, string kind, string objectKind, string value) =>
            new(subject, predicate, kind, objectKind, value, objectKind == "literal" ? "http://www.w3.org/2001/XMLSchema#date" : "", "", new string('6', 64));
        var facts = new[]
        {
            Fact(Act, "dateEntryInForce", "act_force", "literal", "2024-01-05"),
            Fact(Act, "isMemberOf", "wemi_structural", "iri", Work),
            Fact(Act, "publicationDate", "descriptive_date", "literal", "2024-01-03"),
            Fact(Consolidation, "dateApplicability", "consolidation_applicability", "literal", "2025-01-01"),
            Fact(Consolidation, "dateEndApplicability", "consolidation_applicability", "literal", "2025-06-30"),
            Fact(Consolidation, "isRealizedBy", "wemi_structural", "iri", Consolidation + "/fr"),
            Fact(Consolidation + "/fr", "language", "expression_language_or_title", "iri", FrenchLanguageIri),
            Fact(Work, "typeDocument", "resource_type", "iri", "http://data.legilux.public.lu/resource/authority/legal-type/LOI"),
        }.OrderBy(static row => row.SubjectIri, StringComparer.Ordinal).ThenBy(static row => row.Predicate, StringComparer.Ordinal).ToArray();
        var (states, intervals) = ProjectCatalogueStates(
            articles,
            CatalogueFacts.From(facts),
            [new CatalogueBody(member.ObjectRefSha256, Act, Act + "/fr", "fra", "acquired", "agreed_same_run_cc_by", "inventoried", null,
                new string('5', 64), "own", null, [], [new("admitted", 1)])],
            [new CatalogueRecord(Act, member.ObjectRefSha256, "acquired", "accepted_selected")],
            "2026-01-01");
        var titles = new[] { new WorkTitleRow(
            "loi-2024-01-01-n1", article.ExpressionIri, "fra", "Titre fixe", "titre fixe",
            "2024-01-01", "title", new string('3', 64)) };
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-pin-{Guid.NewGuid():N}.sqlite");
        try
        {
            var relations = ProjectRelations(articles);
            var events = ProjectGenesisEvents(states, SourceBodiesOfStates(articles, states, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [member.ObjectRefSha256] = new string('7', 64),
            }));
            BuildDatabase(path, new string('a', 64), HashLogicalRows(members, articles, states, titles, relations, facts, events, intervals),
                members, articles, states, intervals, titles, relations, facts, events,
                ProjectGenesisObservations(new string('a', 64), events.Length, "2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z"));
            return File.ReadAllBytes(path);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    /// <summary>
    /// The capability manifest measured from the index's rows. A search cell counts, per language and day, the articles with
    /// searchable text applicable on that day: by the article's own publisher date where the publisher states one, else by
    /// the date of the one state of its language that holds it (the date <see cref="LuxembourgIndexReader.SearchStateArticles"/>
    /// serves every hit at; a dated article keeps its own date here, as before). Search itself gates only on a language
    /// holding any cell; the period lookup (<see cref="LuxembourgIndexReader.Search"/>, which nothing served calls) reads
    /// articles by their own dates. The publisher's journal documents carry their applicability
    /// on the document and not on each article (none of the Luxembourg population's 8,854 articles carries one), so the
    /// earlier measure, by the article's own date alone (<see cref="MeasureCapabilitiesByArticleDates"/>), stated no
    /// searchable text and search answered nothing on a real mount (the mounted gates of 2026-10-13). An article without a
    /// date of its own and in no state is not counted, as before.
    /// </summary>
    internal static V3IndexCapabilityManifest MeasureCapabilities(
        string digest,
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<StateRow> states,
        IReadOnlyList<WorkTitleRow>? workTitles = null)
    {
        ArgumentNullException.ThrowIfNull(states);
        // An article's state, when exactly one state holds it (the reader refuses an index where two do).
        var stateOfArticle = states
            .SelectMany(static state => (JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson) ?? [])
                .Select(identity => (Identity: identity, State: state)))
            .GroupBy(static entry => entry.Identity, StringComparer.Ordinal)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single().State, StringComparer.Ordinal);
        string? Applicable(ArticleRow row) =>
            row.ApplicabilityDate ??
            (stateOfArticle.TryGetValue(row.ArticleIdentitySha256, out var state) &&
             string.Equals(state.Language, row.Language, StringComparison.Ordinal)
                ? state.ApplicabilityDate
                : null);
        return Manifest(digest, articles
            .Where(static row => row.SearchableText.Length != 0)
            .Select(row => (row.Language, Date: Applicable(row)))
            .Where(static row => row.Date is not null)
            .GroupBy(static row => row)
            .Select(group => Cell(digest, "search", "articles", "searchable_text", group.Key.Language, group.Key.Date!, group.LongCount()))
            .Concat(TitleCells(digest, workTitles)));
    }

    /// <summary>
    /// The earlier measure, by each article's own publisher date: what an index built before 2026-10-13 states. The reader
    /// accepts a manifest measured either way, so a retained generation built then still opens and serves what its manifest
    /// states.
    /// </summary>
    internal static V3IndexCapabilityManifest MeasureCapabilitiesByArticleDates(
        string digest,
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<WorkTitleRow>? workTitles = null) =>
        Manifest(digest, articles
            .Where(static row => row.ApplicabilityDate is not null && row.SearchableText.Length != 0)
            .GroupBy(static row => (row.Language, row.ApplicabilityDate))
            .Select(group => Cell(digest, "search", "articles", "searchable_text", group.Key.Language, group.Key.ApplicabilityDate!, group.LongCount()))
            .Concat(TitleCells(digest, workTitles)));

    private static IEnumerable<V3IndexCapabilityCell> TitleCells(string digest, IReadOnlyList<WorkTitleRow>? workTitles) =>
        (workTitles ?? Array.Empty<WorkTitleRow>())
            .Where(static row => row.DocumentDate is not null)
            .GroupBy(static row => (row.Language, row.DocumentDate))
            .Select(group => Cell(digest, "resolve", "work_titles", "normalized_title", group.Key.Language, group.Key.DocumentDate!, group.LongCount()));

    private static V3IndexCapabilityCell Cell(string digest, string operation, string column, string field, string language, string date, long population)
    {
        var day = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new V3IndexCapabilityCell(PublisherId.LuLegilux, digest, operation, column, field, language, day, day, population);
    }

    private static V3IndexCapabilityManifest Manifest(string digest, IEnumerable<V3IndexCapabilityCell> cells)
    {
        if (!V3IndexCapabilityManifest.TryCreate(
                PublisherId.LuLegilux, digest, cells.ToArray(), V3UnservedOperations.Rows, out var manifest, out var refusal))
        {
            throw new InvalidDataException($"Measured Luxembourg capabilities are invalid: {refusal}.");
        }
        return manifest!;
    }

    /// <summary>The logical rows' digest the stamp records: index 9's rows with the interval transitions.</summary>
    internal static string HashLogicalRows(
        IReadOnlyList<MemberRow> members,
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<StateRow> states,
        IReadOnlyList<WorkTitleRow> workTitles,
        IReadOnlyList<RelationRow> relations,
        IReadOnlyList<WorkFactRow> workFacts,
        IReadOnlyList<EventRow> events,
        IReadOnlyList<IntervalRow> intervals)
    {
        return ContractJson.Sha256OfDefaultJson(
            new LogicalRows(members, articles, states, workTitles, relations, workFacts, events, intervals));
    }

    /// <summary>The logical rows' digest as schema 8 and 6 recorded it: their nine state fields, no interval table.</summary>
    internal static string HashLegacyLogicalRows8(
        IReadOnlyList<MemberRow> members,
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<StateRow> states,
        IReadOnlyList<WorkTitleRow> workTitles,
        IReadOnlyList<RelationRow> relations,
        IReadOnlyList<WorkFactRow> workFacts,
        IReadOnlyList<EventRow> events)
    {
        return ContractJson.Sha256OfDefaultJson(new LegacyLogicalRows8(
            members, articles.Select(LegacyArticleRow8.Of).ToArray(), states.Select(LegacyStateRow8.Of).ToArray(), workTitles,
            relations, workFacts, events));
    }

    internal static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>
    /// A read-only connection that serves requests: its temporary tables, sorts and indexes stay in memory. SQLite
    /// otherwise spills one past its 2 MB cache to a file in the temporary folder, and over the population mount nine
    /// of the release's page probes found two such files written while one request was served (2026-10-13); a server
    /// writes nothing for a request. The verification at open keeps SQLite's default: only a serving connection is set.
    /// </summary>
    internal static SqliteConnection OpenForServing(string path)
    {
        var connection = Open(path, SqliteOpenMode.ReadOnly);
        try
        {
            KeepTemporaryStorageInMemory(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Sets a connection's temporary storage to memory (<c>PRAGMA temp_store = MEMORY</c>), from then on.</summary>
    internal static void KeepTemporaryStorageInMemory(SqliteConnection connection) =>
        Execute(connection, "PRAGMA temp_store = MEMORY");

    internal static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static void EnsureExactSchema(SqliteConnection actual)
    {
        if (!SchemaOf(Ddl).SequenceEqual(ReadSchema(actual), StringComparer.Ordinal))
        {
            throw new InvalidDataException("The Luxembourg index schema differs from the exact terminal schema.");
        }
    }

    /// <summary>
    /// The readable schema an index's tables are exactly, by its <c>user_version</c>: this schema (9), schema 8 (the last
    /// before the catalogue's states), or schema 6 (the last before the event log recorded builds). Any other version, or
    /// tables that are not exactly that version's, refuses: no schema in between is read, and none is read loosely.
    /// </summary>
    internal static int ExactReadableSchemaVersion(SqliteConnection actual, int userVersion)
    {
        var ddl = userVersion switch
        {
            9 => Ddl,
            8 => LegacyDdl8,
            6 => LegacyDdl6,
            _ => throw new InvalidDataException("The Luxembourg index schema differs from the exact terminal schema."),
        };
        if (!SchemaOf(ddl).SequenceEqual(ReadSchema(actual), StringComparer.Ordinal))
        {
            throw new InvalidDataException("The Luxembourg index schema differs from the exact terminal schema.");
        }

        return userVersion;
    }

    private static string[] SchemaOf(string ddl)
    {
        using var expected = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        expected.Open();
        Execute(expected, ddl);
        return ReadSchema(expected);
    }

    /// <summary>
    /// The genesis log a schema-6 index holds, as schema 6 wrote it: one <c>first_sighting</c> per state, ordered by its
    /// key, naming the state digest only (no source bodies, which schema 6 did not record).
    /// </summary>
    internal static EventRow[] LegacyGenesisEvents6(IReadOnlyList<StateRow> states) =>
        states
            .OrderBy(static row => row.WorkKey, StringComparer.Ordinal)
            .ThenBy(static row => row.ApplicabilityDate, StringComparer.Ordinal)
            .ThenBy(static row => row.ExpressionIri, StringComparer.Ordinal)
            .ThenBy(static row => row.Language, StringComparer.Ordinal)
            .Select(static (row, index) => new EventRow(
                index + 1L,
                "state",
                JsonSerializer.Serialize(new[] { row.WorkKey, row.ApplicabilityDate, row.ExpressionIri, row.Language }),
                V3EventRegistry.FirstSighting,
                null,
                JsonSerializer.Serialize(new { state_sha256 = row.StateSha256 })))
            .ToArray();

    private static string[] ReadSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type,name,tbl_name,coalesce(sql,'') FROM sqlite_schema " +
            "WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name,tbl_name";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(string.Join('\n',
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        return rows.ToArray();
    }

    private static void Insert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params object?[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        for (var index = 0; index < values.Length; index++)
        {
            command.Parameters.AddWithValue($"$p{index}", values[index] ?? DBNull.Value);
        }
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidDataException("A Luxembourg index insert did not affect exactly one row.");
        }
    }

    internal static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-journal", path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    internal sealed record MemberRow(
        string ObjectRefSha256,
        int SourceOrdinal,
        string Outcome,
        string? RightsDisposition,
        string Stage3OutcomesJson,
        string GapsJson);

    /// <summary>
    /// One article row. <see cref="DocumentPosition"/> is index 9's: the article's place among its publisher document's
    /// top-level articles, from 1, in the publisher's order (an article not admitted keeps its place, so the positions of
    /// a body's admitted articles can skip); null in a row of index 8 or 6, which recorded none.
    /// </summary>
    internal sealed record ArticleRow(
        string ArticleIdentitySha256,
        string ObjectRefSha256,
        string ExpressionIri,
        string PublisherId,
        string? PublisherWid,
        string? ApplicabilityDate,
        string Language,
        string RuleProfileSha256,
        string SearchableText,
        string TokensJson,
        int? DocumentPosition = null);

    /// <summary>Schema 8's and 6's article row, exactly as their logical rows hashed it.</summary>
    private sealed record LegacyArticleRow8(
        string ArticleIdentitySha256,
        string ObjectRefSha256,
        string ExpressionIri,
        string PublisherId,
        string? PublisherWid,
        string? ApplicabilityDate,
        string Language,
        string RuleProfileSha256,
        string SearchableText,
        string TokensJson)
    {
        internal static LegacyArticleRow8 Of(ArticleRow row) => new(
            row.ArticleIdentitySha256, row.ObjectRefSha256, row.ExpressionIri, row.PublisherId, row.PublisherWid,
            row.ApplicabilityDate, row.Language, row.RuleProfileSha256, row.SearchableText, row.TokensJson);
    }

    /// <summary>The 1-based position of <paramref name="publisherId"/> among an inventory's articles, or null.</summary>
    internal static int? DocumentPositionOf(LuxembourgAknArticleInventory? inventory, string publisherId)
    {
        if (inventory is null)
        {
            return null;
        }

        for (var index = 0; index < inventory.Articles.Count; index++)
        {
            if (string.Equals(inventory.Articles[index].PublisherId, publisherId, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return null;
    }

    /// <summary>
    /// One state row. Its first nine fields are every schema's; the rest are index 9's and null (false) in a row of index 8
    /// or 6, which recorded none of them: there a row is a held state, dated by its file. <see cref="DateRole"/> says what
    /// <see cref="ApplicabilityDate"/> is (the catalogue's applicability, the file's own applicability, or the catalogue's
    /// entry into force); <see cref="EntryIntoForce"/> keeps an act's catalogue entry into force beside it, so the two are
    /// never one value.
    /// </summary>
    internal sealed record StateRow(
        string WorkKey,
        string ApplicabilityDate,
        string StateSha256,
        string ExpressionIri,
        string PublisherWorkIri,
        string PublisherLegalResourceIri,
        string Language,
        string RuleProfilesJson,
        string ArticleIdentitiesJson,
        string? StateKind = null,
        string? DateRole = null,
        string? ApplicableTo = null,
        string? ApplicableToSource = null,
        string? Boundary = null,
        string? PublicationDate = null,
        string? TextStatus = null,
        string? TextStatusReason = null,
        string? InFileIdentity = null,
        string? InFileDate = null,
        string? TranslationOf = null,
        bool Provisional = false,
        string? EntryIntoForce = null)
    {
        /// <summary>Whether the row holds text: every row of index 8 or 6, and a row of index 9 whose text status is <c>held</c>.</summary>
        internal bool Held => TextStatus is null || string.Equals(TextStatus, TextHeld, StringComparison.Ordinal);

        /// <summary>The end the publisher states for the state, when the row's end is the publisher's; null otherwise.</summary>
        internal string? PublisherEnd => string.Equals(ApplicableToSource, SourcePublisher, StringComparison.Ordinal) ? ApplicableTo : null;
    }

    /// <summary>A state row exactly as schema 8 and 6 hashed it into their logical rows.</summary>
    private sealed record LegacyStateRow8(
        string WorkKey,
        string ApplicabilityDate,
        string StateSha256,
        string ExpressionIri,
        string PublisherWorkIri,
        string PublisherLegalResourceIri,
        string Language,
        string RuleProfilesJson,
        string ArticleIdentitiesJson)
    {
        internal static LegacyStateRow8 Of(StateRow row) => new(
            row.WorkKey, row.ApplicabilityDate, row.StateSha256, row.ExpressionIri, row.PublisherWorkIri,
            row.PublisherLegalResourceIri, row.Language, row.RuleProfilesJson, row.ArticleIdentitiesJson);
    }

    internal sealed record WorkTitleRow(
        string WorkIdentifier,
        string ExpressionIri,
        string Language,
        string Title,
        string NormalizedTitle,
        string? DocumentDate,
        string TitleKind,
        string EvidenceSha256);

    /// <summary>
    /// One reference the publisher wrote inside an article of the index, as a forward edge of lane R4's edge
    /// table: the citing article (<see cref="FromRef"/>), the reference's place in that article's token stream
    /// (<see cref="Ordinal"/>, with a footnote body's references at the note reference and <see cref="InNote"/> set),
    /// the publisher's own label and value (<see cref="Href"/>, verbatim), and the target read from that value by
    /// <see cref="ClassifyTarget"/>. The row says the publisher wrote a reference there and asserts nothing about
    /// what it means: no relationship type, no legal effect.
    /// </summary>
    /// <remarks>
    /// <see cref="InNote"/> is a structural fact: the reference sat inside a note element. It is not a proxy for
    /// anything else. In the two retained acts, over the 74 edges of their admitted articles (68 in the 1991 act, of
    /// which 64 name a Legilux work, and 6 in the 1984 act, of which 5 do), every Legilux reference written with the
    /// host (16 and 2) is inside a publisher modification note and every one written without it (48 and 3) is in
    /// running text. That is a measurement of two acts, pinned by a test that names those counts, and not a property
    /// of the format; nothing here relies on it. A reader who takes <c>in_note</c> for "absolute" or for "an
    /// amendment" is relying on a sample of two.
    /// </remarks>
    internal sealed record RelationRow(
        string FromRef,
        int Ordinal,
        string EdgeType,
        string AssertedBy,
        string SourcePredicate,
        bool InNote,
        string? Label,
        string? Href,
        string ToKind,
        string? ToRef);

    /// <summary>
    /// One of the publisher's typed assertions as the fact table holds it (<see cref="ProjectWorkFacts"/>):
    /// the predicate and fact kind are the closed vocabulary's wire names; the datatype and language tag
    /// are empty strings when the publisher gave none, so the whole row is the primary key.
    /// </summary>
    internal sealed record WorkFactRow(
        string SubjectIri,
        string Predicate,
        string FactKind,
        string ObjectKind,
        string ObjectValue,
        string DatatypeIri,
        string LanguageTag,
        string EvidenceSha256);

    /// <summary>
    /// One row of the event log (<see cref="ProjectGenesisEvents"/>): the sequence number, the scope
    /// (<c>state</c>), the key (the state's primary key as a JSON array), the event name, the observation
    /// time (null: none is held) and the detail (the state's digest as JSON).
    /// </summary>
    internal sealed record EventRow(
        long Seq,
        string Scope,
        string Key,
        string Event,
        string? ObservedFrom,
        string DetailJson);

    /// <summary>
    /// One observation of the event log: the build that observed (its corpus), the index it carried forward (null for
    /// the first, a genesis log's only observation), the events it appended by sequence number, the build's time (an
    /// upper bound on when its corpus was observed), and its observation time (null: none is held).
    /// </summary>
    internal sealed record ObservationRow(
        long Observation,
        string CorpusSha256,
        string? PredecessorIndexSha256,
        long FirstSeq,
        long LastSeq,
        string BuiltAt,
        string? ObservedFrom,
        string? DerivedAt = null);

    /// <summary>An observation exactly as log /2 (index 8) hashed it: no derivation time.</summary>
    private sealed record LegacyObservationRow8(
        long Observation,
        string CorpusSha256,
        string? PredecessorIndexSha256,
        long FirstSeq,
        long LastSeq,
        string BuiltAt,
        string? ObservedFrom);

    private sealed record EventLogRows(IReadOnlyList<ObservationRow> Observations, IReadOnlyList<EventRow> Events);

    private sealed record LegacyEventLogRows8(IReadOnlyList<LegacyObservationRow8> Observations, IReadOnlyList<EventRow> Events);

    private sealed record LogicalRows(
        IReadOnlyList<MemberRow> Members,
        IReadOnlyList<ArticleRow> Articles,
        IReadOnlyList<StateRow> States,
        IReadOnlyList<WorkTitleRow> WorkTitles,
        IReadOnlyList<RelationRow> Relations,
        IReadOnlyList<WorkFactRow> WorkFacts,
        IReadOnlyList<EventRow> Events,
        IReadOnlyList<IntervalRow> Intervals);

    /// <summary>Schema 8's and 6's logical rows, exactly as they hashed them.</summary>
    private sealed record LegacyLogicalRows8(
        IReadOnlyList<MemberRow> Members,
        IReadOnlyList<LegacyArticleRow8> Articles,
        IReadOnlyList<LegacyStateRow8> States,
        IReadOnlyList<WorkTitleRow> WorkTitles,
        IReadOnlyList<RelationRow> Relations,
        IReadOnlyList<WorkFactRow> WorkFacts,
        IReadOnlyList<EventRow> Events);

    // The work_titles column and every title query are folded by Lex's own table (TitleFolding), never by the operating
    // system's language library, which the release image does not carry (plan 0.8).
    internal static string NormalizeTitle(string value) => TitleFolding.NormalizeTitle(value);

    internal sealed record SqliteProvenance(string Version, string SourceId, string CompileOptionsSha256)
    {
        internal static SqliteProvenance Read(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT sqlite_version(), sqlite_source_id()";
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidDataException("SQLite returned no provenance row.");
            var version = reader.GetString(0);
            var sourceId = reader.GetString(1);
            reader.Close();
            return new SqliteProvenance(
                version,
                sourceId,
                SqlitePortableProvenance.CompileOptionsSha256(connection));
        }
    }
}

/// <summary>A verified read-only mount of one exact Luxembourg index.</summary>
public sealed class LuxembourgIndexReader : IDisposable
{
    private readonly string _path;
    private readonly SqliteConnection _connection;
    private readonly V3IndexCapabilityManifest _capabilityManifest;
    private readonly bool _deleteOnDispose;
    private readonly object _gate = new();
    private readonly object _articleOutcomesGate = new();
    private IReadOnlyList<KeyValuePair<string, long>>? _articleOutcomes;
    private readonly object _coverageGate = new();
    private LuxembourgIndexCoverage? _coverage;

    private readonly int _version;

    private LuxembourgIndexReader(
        string path,
        SqliteConnection connection,
        V3IndexCapabilityManifest capabilityManifest,
        SourceArtifactRef indexRef,
        SourceArtifactRef corpusRef,
        int version,
        bool deleteOnDispose)
    {
        _path = path;
        _connection = connection;
        _capabilityManifest = capabilityManifest;
        IndexRef = indexRef;
        CorpusRef = corpusRef;
        _version = version;
        SchemaIdentity = SchemaOfVersion(version);
        _deleteOnDispose = deleteOnDispose;
    }

    public SourceArtifactRef IndexRef { get; }

    /// <summary>
    /// The schema the index's stamp names: <see cref="LuxembourgIndexBuilder.Schema"/> (9),
    /// <see cref="LuxembourgIndexBuilder.LegacySchema8"/> or <see cref="LuxembourgIndexBuilder.LegacySchema6"/>.
    /// </summary>
    public string SchemaIdentity { get; }

    /// <summary>
    /// Whether the index's event log records its builds: observations with their build times and corpora, the source
    /// bodies each event names, and the log's own stamp (schema 8 and 9). A schema-6 index records none of them, so
    /// nothing it holds names a snapshot, bounds an observation or can be checked against the corpus's bodies; they are
    /// absent, never invented.
    /// </summary>
    public bool RecordsBuilds => _version != 6;

    /// <summary>
    /// Whether the index holds the catalogue's states (schema 9): every catalogue state of a served work, held or textless,
    /// with its role, ends, boundary and text status, and the interval transitions between them. A schema 8 or 6 index
    /// holds the held states only, dated by their files, and none of these fields; they are absent, never invented.
    /// Until the reader's interval rules land (S7a), every operation that existed before index 9 reads the held states only.
    /// </summary>
    public bool RecordsCatalogueStates => _version == 9;

    /// <summary>The event log schema the index holds: log /3 for schema 9, log /2 for schema 8; schema 6 stamps none.</summary>
    internal string LogSchema => _version == 9 ? LuxembourgIndexBuilder.EventLogSchema : LuxembourgIndexBuilder.LegacyEventLogSchema8;

    private static string SchemaOfVersion(int version) => version switch
    {
        9 => LuxembourgIndexBuilder.Schema,
        8 => LuxembourgIndexBuilder.LegacySchema8,
        6 => LuxembourgIndexBuilder.LegacySchema6,
        _ => throw new InvalidDataException("The Luxembourg index schema differs from the exact terminal schema."),
    };

    /// <summary>
    /// The clause that keeps a query of the states table to held rows: none for schema 8 and 6, whose rows are all held,
    /// and <c>text_status='held'</c> for schema 9, so every operation that existed before index 9 reads what it read.
    /// </summary>
    private string HeldOnly(string alias) => _version == 9 ? $" AND {alias}.text_status='held'" : string.Empty;

    public SourceArtifactRef CorpusRef { get; }

    public long MemberCount => Count("members");

    public long ArticleCount => Count("articles");

    public static LuxembourgIndexReader OpenAndVerify(
        SourceArtifactRef indexRef,
        ReadOnlySpan<byte> indexBytes,
        SourceArtifactRef expectedCorpusRef,
        V3IndexCapabilityManifest capabilityManifest)
    {
        ArgumentNullException.ThrowIfNull(indexRef);
        ArgumentNullException.ThrowIfNull(expectedCorpusRef);
        ArgumentNullException.ThrowIfNull(capabilityManifest);
        var digest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
        if (!string.Equals(digest, indexRef.Sha256, StringComparison.Ordinal) ||
            !string.Equals(indexRef.ResourceId, LexCorpus6Builder.ResourceIdOf(digest), StringComparison.Ordinal) ||
            capabilityManifest.Publisher != PublisherId.LuLegilux ||
            !string.Equals(capabilityManifest.IndexSha256, digest, StringComparison.Ordinal))
        {
            throw new ArgumentException("The Luxembourg index identity or capability binding is invalid.");
        }
        if (!string.Equals(
                expectedCorpusRef.ResourceId,
                LexCorpus6Builder.ResourceIdOf(expectedCorpusRef.Sha256),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The expected corpus resource identity is not bound to its digest.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-mount-{Guid.NewGuid():N}.sqlite");
        File.WriteAllBytes(path, indexBytes.ToArray());
        return OpenVerifiedFile(
            path,
            indexRef,
            expectedCorpusRef,
            capabilityManifest,
            deleteOnDispose: true);
    }

    public static async Task<LuxembourgIndexReader> OpenAndVerifyFileAsync(
        string indexPath,
        ReadOnlyMemory<byte> capabilityManifestBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("The Luxembourg index artifact is missing.", indexPath);
        }

        var privatePath = Path.Combine(
            Path.GetTempPath(), $"lex-v3-lu-mount-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using (var source = new FileStream(
                indexPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                privatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            string digest;
            await using (var copied = new FileStream(
                privatePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                digest = Convert.ToHexStringLower(
                    await SHA256.HashDataAsync(copied, cancellationToken).ConfigureAwait(false));
            }

            var indexRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
            var capabilityDigest = V3IndexCapabilityManifestArtifact.ComputeSha256(
                capabilityManifestBytes.Span);
            var capabilityRef = new SourceArtifactRef(
                LexCorpus6Builder.ResourceIdOf(capabilityDigest), capabilityDigest);
            var capabilityManifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
                capabilityRef,
                capabilityManifestBytes.Span,
                PublisherId.LuLegilux,
                digest);
            return OpenVerifiedFile(
                privatePath,
                indexRef,
                expectedCorpusRef: null,
                capabilityManifest,
                deleteOnDispose: true);
        }
        catch
        {
            LuxembourgIndexBuilder.DeleteDatabase(privatePath);
            throw;
        }
    }

    private static LuxembourgIndexReader OpenVerifiedFile(
        string path,
        SourceArtifactRef indexRef,
        SourceArtifactRef? expectedCorpusRef,
        V3IndexCapabilityManifest capabilityManifest,
        bool deleteOnDispose)
    {
        SqliteConnection? connection = null;
        try
        {
            connection = LuxembourgIndexBuilder.Open(path, SqliteOpenMode.ReadOnly);
            var version = LuxembourgIndexBuilder.ExactReadableSchemaVersion(
                connection, Convert.ToInt32(Scalar(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture));
            var schemaIdentity = SchemaOfVersion(version);
            if (!string.Equals(Scalar(connection, "PRAGMA integrity_check"), "ok", StringComparison.Ordinal) ||
                Convert.ToInt32(Scalar(connection, "PRAGMA application_id"), CultureInfo.InvariantCulture) != 0x4c563306)
            {
                throw new InvalidDataException("The Luxembourg index failed SQLite integrity or schema identity checks.");
            }

            using var stamp = connection.CreateCommand();
            stamp.CommandText = "SELECT schema_identity,corpus_sha256,logical_rows_sha256,sqlite_version,sqlite_source_id,compile_options_sha256 FROM stamp WHERE stamp_id=1";
            using var stampReader = stamp.ExecuteReader();
            if (!stampReader.Read() ||
                !string.Equals(stampReader.GetString(0), schemaIdentity, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The Luxembourg index stamp does not bind the expected corpus.");
            }
            var corpusSha256 = stampReader.GetString(1);
            var corpusRef = new SourceArtifactRef(
                LexCorpus6Builder.ResourceIdOf(corpusSha256), corpusSha256);
            if (expectedCorpusRef is not null && expectedCorpusRef != corpusRef)
            {
                throw new InvalidDataException("The Luxembourg index stamp does not bind the expected corpus.");
            }
            var expectedLogical = stampReader.GetString(2);
            var expectedProvenance = new LuxembourgIndexBuilder.SqliteProvenance(
                stampReader.GetString(3), stampReader.GetString(4), stampReader.GetString(5));
            if (stampReader.Read()) throw new InvalidDataException("The Luxembourg index has multiple stamp rows.");
            stampReader.Close();
            if (expectedProvenance != LuxembourgIndexBuilder.SqliteProvenance.Read(connection))
                throw new InvalidDataException("The Luxembourg index SQLite provenance changed.");

            // The two large tables (260,235 members and 1.5 million facts in the population) are read once, as the hash
            // reads them; nothing else here needs them.
            var members = new StreamedRows<LuxembourgIndexBuilder.MemberRow>(StreamMembers(connection));
            var articles = ReadArticles(connection, version);
            var states = ReadStates(connection, version);
            var workTitles = ReadWorkTitles(connection);
            var relations = ReadRelations(connection);
            var workFacts = new StreamedRows<LuxembourgIndexBuilder.WorkFactRow>(StreamWorkFacts(connection));
            var events = LuxembourgIndexBuilder.ReadLogEvents(connection);
            var logSchema = version == 9 ? LuxembourgIndexBuilder.EventLogSchema : LuxembourgIndexBuilder.LegacyEventLogSchema8;
            var observations = version == 6 ? [] : LuxembourgIndexBuilder.ReadLogObservations(connection, logSchema);
            var intervals = version == 9 ? ReadIntervals(connection) : [];
            if (version == 9)
            {
                if (observations.Length == 0)
                {
                    throw new InvalidDataException("The Luxembourg event log's observations do not number its events.");
                }

                ValidateDocumentPositions(articles);
                ValidateCatalogueStates(articles, states, intervals, observations[^1].BuiltAt[..10]);
            }
            else
            {
                ValidateStates(articles, states);
            }

            if (!LuxembourgIndexBuilder.ProjectRelations(articles).SequenceEqual(relations))
                throw new InvalidDataException(
                    "The Luxembourg index relation rows are not the references its articles carry.");
            if (version != 6)
            {
                LuxembourgIndexBuilder.ValidateLog(corpusSha256, states, observations, events, logSchema);
                VerifyLogStamp(connection, observations, events, logSchema);
            }
            else if (!LuxembourgIndexBuilder.LegacyGenesisEvents6(states).SequenceEqual(events))
            {
                // Schema 6's own check: its log is exactly the genesis log of its states, as schema 6 wrote it.
                throw new InvalidDataException("The Luxembourg index event rows are not the genesis log of its states.");
            }
            if (!string.Equals(
                    version == 9
                        ? LuxembourgIndexBuilder.HashLogicalRows(members, articles, states, workTitles, relations, workFacts, events, intervals)
                        : LuxembourgIndexBuilder.HashLegacyLogicalRows8(members, articles, states, workTitles, relations, workFacts, events),
                    expectedLogical,
                    StringComparison.Ordinal))
                throw new InvalidDataException("The Luxembourg index logical rows do not match their stamp.");

            // Measured by the state dates search serves, or by the earlier article dates an index built before 2026-10-13
            // states: either is a measurement of these rows, and a retained generation keeps opening.
            if (!LuxembourgIndexBuilder.MeasureCapabilities(indexRef.Sha256, articles, states, workTitles).Cells.SequenceEqual(capabilityManifest.Cells) &&
                !LuxembourgIndexBuilder.MeasureCapabilitiesByArticleDates(indexRef.Sha256, articles, workTitles).Cells.SequenceEqual(capabilityManifest.Cells))
                throw new InvalidDataException("The Luxembourg capability manifest was not measured from the index.");

            // Verified: from here the connection serves requests.
            LuxembourgIndexBuilder.KeepTemporaryStorageInMemory(connection);
            return new LuxembourgIndexReader(
                path,
                connection,
                capabilityManifest,
                indexRef,
                corpusRef,
                version,
                deleteOnDispose);
        }
        catch
        {
            connection?.Dispose();
            if (deleteOnDispose)
            {
                LuxembourgIndexBuilder.DeleteDatabase(path);
            }
            throw;
        }
    }

    public IReadOnlyList<LuxembourgIndexResolvedExpression> ResolveExact(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT expression_iri,publisher_wid,language,article_identity_sha256
                FROM articles
                WHERE expression_iri=$identifier OR publisher_wid=$identifier
                  OR article_identity_sha256=$identifier
                ORDER BY expression_iri,publisher_wid,language,article_identity_sha256
                """;
            command.Parameters.AddWithValue("$identifier", identifier);
            using var reader = command.ExecuteReader();
            var rows = new List<(string Expression, string? Wid, string Language, string Article)>();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }

            return rows
                .GroupBy(static row => (row.Expression, row.Wid, row.Language))
                .Select(static group => new LuxembourgIndexResolvedExpression(
                    group.Key.Expression,
                    group.Key.Wid,
                    group.Key.Language,
                    Array.AsReadOnly(group.Select(static row => row.Article).ToArray())))
                .ToArray();
        }
    }

    public IReadOnlyList<LuxembourgIndexResolvedState> ResolveState(string workKey, string applicabilityDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicabilityDate);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $$"""
                SELECT s.work_key,s.applicability_date,s.state_sha256,s.expression_iri,s.publisher_work_iri,
                       s.publisher_legal_resource_iri,s.language,s.rule_profiles_json,s.article_identities_json
                FROM states s
                WHERE s.work_key=$work AND s.applicability_date=$date{{HeldOnly("s")}}
                ORDER BY s.expression_iri,s.language,s.state_sha256
                """;
            command.Parameters.AddWithValue("$work", workKey);
            command.Parameters.AddWithValue("$date", applicabilityDate);
            return ReadResolvedStates(command);
        }
    }

    /// <summary>
    /// Every publisher-dated held state of one work, in date order, then language, expression and digest.
    /// The work is named either by its product work key or by the publisher's own work IRI as
    /// stored; nothing is normalised. Temporal operations select over this list; it carries no end
    /// date: on index 9 the publisher's ends, the textless rows and the intervals are read by
    /// <see cref="ResolveCatalogueStates"/> and <see cref="ResolveIntervals"/>, which the reader rules (S7a) will select over.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexResolvedState> ResolveWorkStates(string workIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workIdentifier);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $$"""
                SELECT s.work_key,s.applicability_date,s.state_sha256,s.expression_iri,s.publisher_work_iri,
                       s.publisher_legal_resource_iri,s.language,s.rule_profiles_json,s.article_identities_json
                FROM states s
                WHERE (s.work_key=$identifier OR s.publisher_work_iri=$identifier){{HeldOnly("s")}}
                ORDER BY s.applicability_date,s.language,s.expression_iri,s.state_sha256
                """;
            command.Parameters.AddWithValue("$identifier", workIdentifier);
            return ReadResolvedStates(command);
        }
    }

    /// <summary>
    /// Every state row of one work, held or textless, in key order (date, expression, language), with every field index 9
    /// records (<see cref="LuxembourgIndexCatalogueState"/>); for an index of schema 8 or 6, its held states with those
    /// fields absent. Read by the work key's primary-key range (<see cref="LuxembourgIndexQueries.CatalogueStatesOfWork"/>).
    /// Nothing is selected here: which state covers a date is the reader rules' (S7a).
    /// </summary>
    public IReadOnlyList<LuxembourgIndexCatalogueState> ResolveCatalogueStates(string workKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workKey);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = _version == 9
                ? LuxembourgIndexQueries.CatalogueStatesOfWork
                : "SELECT work_key,applicability_date,state_sha256,expression_iri,publisher_work_iri,publisher_legal_resource_iri,language," +
                  "rule_profiles_json,article_identities_json FROM states WHERE work_key=$work ORDER BY applicability_date,expression_iri,language";
            command.Parameters.AddWithValue("$work", workKey);
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexCatalogueState>();
            while (reader.Read())
            {
                var row = _version == 9 ? StateRowNine(reader) : new LuxembourgIndexBuilder.StateRow(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8));
                values.Add(new LuxembourgIndexCatalogueState(
                    row.WorkKey, row.ApplicabilityDate, row.StateSha256, row.ExpressionIri, row.PublisherWorkIri,
                    row.PublisherLegalResourceIri, row.Language,
                    Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(row.ArticleIdentitiesJson)
                        ?? throw new InvalidDataException("A state has no article-identity population.")),
                    row.StateKind, row.DateRole, row.EntryIntoForce, row.ApplicableTo, row.ApplicableToSource, row.Boundary, row.PublicationDate,
                    row.TextStatus ?? LuxembourgIndexBuilder.TextHeld, row.TextStatusReason, row.InFileIdentity, row.InFileDate,
                    row.TranslationOf, row.Provisional));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The interval transitions of one work's French chain, in chain order (<see cref="LuxembourgIndexQueries.IntervalsOfWork"/>);
    /// none for an index of schema 8 or 6, which records none.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexInterval> ResolveIntervals(string workKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workKey);
        if (_version != 9)
        {
            return Array.Empty<LuxembourgIndexInterval>();
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.IntervalsOfWork;
            command.Parameters.AddWithValue("$work", workKey);
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexInterval>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexInterval(
                    reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// Every state whose publisher date lies in the closed window, across works, in publisher date, work
    /// key, language, expression and digest order. Dates are the publisher's <c>yyyy-MM-dd</c> strings,
    /// which order as dates do. Nothing is compared here.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexResolvedState> ResolveStatesInPeriod(string windowFrom, string windowTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowFrom);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowTo);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $$"""
                SELECT s.work_key,s.applicability_date,s.state_sha256,s.expression_iri,s.publisher_work_iri,
                       s.publisher_legal_resource_iri,s.language,s.rule_profiles_json,s.article_identities_json
                FROM states s
                WHERE s.applicability_date>=$from AND s.applicability_date<=$to{{HeldOnly("s")}}
                ORDER BY s.applicability_date,s.work_key,s.language,s.expression_iri,s.state_sha256
                """;
            command.Parameters.AddWithValue("$from", windowFrom);
            command.Parameters.AddWithValue("$to", windowTo);
            return ReadResolvedStates(command);
        }
    }

    /// <summary>
    /// The work keys that have a state dated at or before <paramref name="date"/>, in ordinal work-key
    /// order, strictly after <paramref name="afterWorkKey"/> when one is given, at most
    /// <paramref name="take"/> of them. With a language, only that language's states count. Nothing is
    /// selected here: which state applies is the caller's rule.
    /// </summary>
    public IReadOnlyList<string> ResolveWorkKeysWithStateOnOrBefore(
        string date, string? language, string? afterWorkKey, int take)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(date);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT DISTINCT s.work_key FROM states s WHERE s.applicability_date<=$date" + HeldOnly("s") +
                (language is null ? string.Empty : " AND s.language=$language") +
                (afterWorkKey is null ? string.Empty : " AND s.work_key>$after") +
                " ORDER BY s.work_key LIMIT $take";
            command.Parameters.AddWithValue("$date", date);
            command.Parameters.AddWithValue("$take", take);
            if (language is not null)
            {
                command.Parameters.AddWithValue("$language", language);
            }

            if (afterWorkKey is not null)
            {
                command.Parameters.AddWithValue("$after", afterWorkKey);
            }

            var keys = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                keys.Add(reader.GetString(0));
            }

            return keys;
        }
    }

    /// <summary>
    /// The languages the capability manifest measured searchable article text for, in ordinal order:
    /// the languages a search can be asked in, as distinct from the languages the index holds states
    /// for. Empty when none was measured.
    /// </summary>
    public IReadOnlyList<string> SearchableLanguages() =>
        Array.AsReadOnly(_capabilityManifest.Cells
            .Where(static cell =>
                string.Equals(cell.Operation, "search", StringComparison.Ordinal) &&
                string.Equals(cell.Column, "articles", StringComparison.Ordinal) &&
                string.Equals(cell.Field, "searchable_text", StringComparison.Ordinal))
            .Select(static cell => cell.Language)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());

    /// <summary>
    /// The articles of held states, in one language, whose searchable text contains every one of
    /// <paramref name="needles"/> as a byte-exact substring, each paired with every state that holds
    /// it, in work key, publisher date, article identity and state digest order. With a work key, only
    /// that work's states. Nothing is ranked, folded or selected here: which lane a hit belongs to and
    /// which state applies on a date are the caller's rules. The result is null when the capability
    /// manifest measured no searchable text in that language, which is the index saying it cannot
    /// answer, as distinct from an empty list, which is no hit.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexSearchHit>? SearchStateArticles(
        string language, IReadOnlyList<string> needles, string? workKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(needles);
        if (needles.Count == 0 || needles.Any(static needle => string.IsNullOrEmpty(needle)))
        {
            throw new ArgumentException("A search needs at least one non-empty needle.", nameof(needles));
        }

        if (!_capabilityManifest.Cells.Any(cell =>
                string.Equals(cell.Operation, "search", StringComparison.Ordinal) &&
                string.Equals(cell.Column, "articles", StringComparison.Ordinal) &&
                string.Equals(cell.Field, "searchable_text", StringComparison.Ordinal) &&
                string.Equals(cell.Language, language, StringComparison.Ordinal)))
        {
            return null;
        }

        // The scan has its own read-only connection on the same immutable file and does not take the
        // reader's gate. A search reads every article of a language, and holding the one shared
        // connection for that long would stall every other operation on the mount behind it, which a
        // one-character query would be the cheapest way to do. The path is the reader's private copy,
        // the file whose bytes were hashed and verified when the reader was opened (both open routes
        // copy to a private temporary path and hash that copy), so this connection reads what the shared
        // one reads; a local process able to rewrite that file could alter either, which is no wider
        // than it was.
        {
            using var connection = LuxembourgIndexBuilder.OpenForServing(_path);
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT s.work_key, s.applicability_date, s.state_sha256, a.article_identity_sha256, a.publisher_id, a.publisher_wid " +
                // The state of an article is found by exact element of the state's identity list, as the
                // sibling queries do, and not by a substring of the JSON text: the join no longer rests on
                // every value in the column being a 64-character digest (which OpenAndVerify does require).
                // json_each yields one row per element, so this is one row per article only while no state
                // lists an identity twice and no article sits in two states. The index cannot be opened
                // otherwise: ValidateStates refuses a list that is not its own Distinct().Order(), a list
                // that is not exactly the expression's articles (whose identity is the articles table's
                // PRIMARY KEY), and an article claimed by two states. There is deliberately no DISTINCT
                // here: it would turn a broken invariant into a plausible answer, and the refusal at open
                // is the loud form. The refusals are pinned in LuxembourgIndexBuilderTests.
                "FROM states s, json_each(s.article_identities_json) j " +
                "JOIN articles a ON a.article_identity_sha256 = j.value AND a.language = s.language " +
                "WHERE a.language=$language" +
                string.Concat(needles.Select(static (_, index) => $" AND instr(a.searchable_text,$needle{index})>0")) +
                (workKey is null ? string.Empty : " AND s.work_key=$work") +
                " ORDER BY s.work_key, s.applicability_date, a.article_identity_sha256, s.state_sha256";
            command.Parameters.AddWithValue("$language", language);
            for (var index = 0; index < needles.Count; index++)
            {
                command.Parameters.AddWithValue($"$needle{index}", needles[index]);
            }

            if (workKey is not null)
            {
                command.Parameters.AddWithValue("$work", workKey);
            }

            var hits = new List<LuxembourgIndexSearchHit>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                hits.Add(new LuxembourgIndexSearchHit(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
            }

            return hits;
        }
    }

    /// <summary>How many works have a state dated at or before the date, in the language when one is given.</summary>
    public long CountWorksWithStateOnOrBefore(string date, string? language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(date);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(DISTINCT s.work_key) FROM states s WHERE s.applicability_date<=$date" + HeldOnly("s") +
                (language is null ? string.Empty : " AND s.language=$language");
            command.Parameters.AddWithValue("$date", date);
            if (language is not null)
            {
                command.Parameters.AddWithValue("$language", language);
            }

            return (long)(command.ExecuteScalar() ?? 0L);
        }
    }

    /// <summary>
    /// What the index holds, so a window can be read against it: how many works have a dated state, the
    /// first and last publisher date held, and the languages held. With a language, the works and dates
    /// are that language's; the languages listed are always every language held, since they are what
    /// could be asked for. All null or empty where nothing is held.
    /// </summary>
    public LuxembourgIndexStatePopulation ResolveStatePopulation(string? language = null)
    {
        lock (_gate)
        {
            long works;
            string? first;
            string? last;
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = language is null
                    ? "SELECT COUNT(DISTINCT s.work_key),MIN(s.applicability_date),MAX(s.applicability_date) FROM states s WHERE 1=1" + HeldOnly("s")
                    : "SELECT COUNT(DISTINCT s.work_key),MIN(s.applicability_date),MAX(s.applicability_date) FROM states s WHERE s.language=$language" + HeldOnly("s");
                if (language is not null)
                {
                    command.Parameters.AddWithValue("$language", language);
                }

                using var reader = command.ExecuteReader();
                reader.Read();
                works = reader.GetInt64(0);
                first = reader.IsDBNull(1) ? null : reader.GetString(1);
                last = reader.IsDBNull(2) ? null : reader.GetString(2);
            }

            var languages = new List<string>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT DISTINCT s.language FROM states s WHERE 1=1" + HeldOnly("s") + " ORDER BY s.language";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    languages.Add(reader.GetString(0));
                }
            }

            return new LuxembourgIndexStatePopulation(works, first, last, languages);
        }
    }

    /// <summary>
    /// The source documents one state's articles come from: every corpus member that holds at least one
    /// of the state's articles, in digest order, with what the corpus recorded for it. A state is one
    /// expression's articles, so this is normally one document. Empty for a state the index does not hold.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexStateSource> ResolveStateSources(string stateSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSha256);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.StateSources;
            command.Parameters.AddWithValue("$state", stateSha256);
            var rows = new List<(string ObjectRef, string Outcome, string? Rights, string[] Gaps)>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? []));
                }
            }

            var sources = new List<LuxembourgIndexStateSource>();
            foreach (var row in rows)
            {
                using var outcomes = _connection.CreateCommand();
                outcomes.CommandText = LuxembourgIndexQueries.MemberOutcomes;
                outcomes.Parameters.AddWithValue("$member", row.ObjectRef);
                var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
                CountArticleOutcomes((string)outcomes.ExecuteScalar()!, counts);
                sources.Add(new LuxembourgIndexStateSource(
                    row.ObjectRef, row.Outcome, row.Rights, row.Gaps, counts.ToArray()));
            }

            return sources;
        }
    }

    /// <summary>
    /// For each of the given states, how many articles of the state's document the corpus recorded that the state
    /// does not hold: the document's legal-content outcomes under <c>akn_unsupported_content_shape</c>. A state is
    /// one document's held articles, so a document that has a state loaded and matched its reviewed inventory and
    /// has at least one admitted article, and an article it did not admit can only be that one token (an outcome
    /// under any other non-held token exists only for a document that has no state). A state the index resolved
    /// always has its article and member rows, so one that does not come back is a broken index and not a zero.
    /// </summary>
    public IReadOnlyDictionary<string, long> ResolveArticlesNotAdmitted(IReadOnlyList<string> stateSha256s)
    {
        ArgumentNullException.ThrowIfNull(stateSha256s);
        var asked = stateSha256s.Distinct(StringComparer.Ordinal).ToArray();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        if (asked.Length == 0)
        {
            return counts;
        }

        var token = ContractWire.NameOf(LexCorpus6Stage3Disposition.AknUnsupportedContentShape);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.StateDocumentOutcomes;
            command.Parameters.AddWithValue("$states", JsonSerializer.Serialize(asked));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var byToken = new SortedDictionary<string, long>(StringComparer.Ordinal);
                CountArticleOutcomes(reader.GetString(1), byToken);
                counts[reader.GetString(0)] = byToken.GetValueOrDefault(token);
            }
        }

        var missing = asked.Where(sha => !counts.ContainsKey(sha)).ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidDataException(
                "The index resolved a state whose first article or source document it does not hold: " + missing[0]);
        }

        return counts;
    }

    /// <summary>
    /// The capability manifest's cells as the reader was verified against them: what the index
    /// measured it can be asked, per operation, column, field, language and date span. Sorted as the
    /// manifest sorts them.
    /// </summary>
    public IReadOnlyList<V3IndexCapabilityCell> CapabilityCells() =>
        Array.AsReadOnly(_capabilityManifest.Cells.ToArray());

    /// <summary>
    /// What the index holds and what it recorded as missing, as grouped counts read from the small
    /// tables and the covering indexes: members by outcome, the corpus's gap tokens counted by
    /// member, and works, states and articles overall and per language. A missing
    /// publisher date is counted and never dropped. Nothing here says what the publisher holds. It
    /// reads through its own read-only connection on the reader's private verified copy and does not
    /// take the reader's gate, so a caller who asks for it again and again cannot make every other
    /// operation on the mount wait behind the counts. The index is immutable, so the counts are read
    /// once for the reader's life and every later report is the same one: every page asks for
    /// coverage, and the counts' sorts, held in memory while serving (the gaps' distinct count is
    /// about 24 MiB over the population mount, the review of #953), would otherwise be built again for
    /// each request at once.
    /// </summary>
    public LuxembourgIndexCoverage ResolveCoverage()
    {
        lock (_coverageGate)
        {
            return _coverage ??= ReadCoverage();
        }
    }

    private LuxembourgIndexCoverage ReadCoverage()
    {
        {
            using var connection = LuxembourgIndexBuilder.OpenForServing(_path);
            List<KeyValuePair<string, long>> Grouped(string sql)
            {
                var rows = new List<KeyValuePair<string, long>>();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add(new KeyValuePair<string, long>(reader.GetString(0), reader.GetInt64(1)));
                }

                return rows;
            }

            var outcomes = Grouped("SELECT outcome,COUNT(*) FROM members GROUP BY outcome ORDER BY outcome");
            var gaps = Grouped(
                "SELECT j.value,COUNT(DISTINCT m.object_ref_sha256) FROM members m,json_each(m.gaps_json) j GROUP BY j.value ORDER BY j.value");
            long Counted(string sql) => Convert.ToInt64(Scalar(connection, sql), CultureInfo.InvariantCulture);
            var members = Counted("SELECT COUNT(*) FROM members");
            var membersWithGaps = Counted("SELECT COUNT(*) FROM members WHERE gaps_json<>'[]'");
            var works = Counted("SELECT COUNT(DISTINCT s.work_key) FROM states s WHERE 1=1" + HeldOnly("s"));
            var states = Counted("SELECT COUNT(*) FROM states s WHERE 1=1" + HeldOnly("s"));
            var articleCount = Counted("SELECT COUNT(*) FROM articles");

            var articles = new Dictionary<string, (long All, long Undated)>(StringComparer.Ordinal);
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT language,COUNT(*),COUNT(*)-COUNT(applicability_date) FROM articles GROUP BY language";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    articles[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
                }
            }

            var languages = new List<LuxembourgIndexLanguageCoverage>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT s.language,COUNT(DISTINCT s.work_key),COUNT(*)," +
                    "MIN(s.applicability_date),MAX(s.applicability_date) FROM states s WHERE 1=1" + HeldOnly("s") +
                    " GROUP BY s.language ORDER BY s.language";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var language = reader.GetString(0);
                    var (all, undated) = articles.TryGetValue(language, out var counted) ? counted : (0L, 0L);
                    languages.Add(new LuxembourgIndexLanguageCoverage(
                        language,
                        reader.GetInt64(1),
                        reader.GetInt64(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        all,
                        undated));
                }
            }

            return new LuxembourgIndexCoverage(
                members, works, states, articleCount,
                outcomes, membersWithGaps, gaps, ArticleOutcomesOfAcquiredMembers(connection), languages);
        }
    }

    /// <summary>
    /// The corpus's legal-content outcomes for its acquired members, counted by the corpus's own
    /// disposition token in ordinal order. Only acquired members are read, because the index holds an
    /// article for an outcome only when its member is acquired. Unlike every other count in the report
    /// this reads each acquired member's whole outcome list, so its cost grows with the outcomes the
    /// corpus recorded: 0.2 to 0.7 s for 10,000 synthetic members of 54 outcomes each on a warm file
    /// (not a real index, and more on a cold file). The index is immutable, so it is read once for
    /// the reader's life and a later report reuses it.
    /// </summary>
    private IReadOnlyList<KeyValuePair<string, long>> ArticleOutcomesOfAcquiredMembers(SqliteConnection connection)
    {
        lock (_articleOutcomesGate)
        {
            if (_articleOutcomes is not null)
            {
                return _articleOutcomes;
            }

            var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT stage3_outcomes_json FROM members WHERE outcome=$outcome";
            command.Parameters.AddWithValue("$outcome", ContractWire.NameOf(LexCorpus6OutcomeKind.Acquired));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                CountArticleOutcomes(reader.GetString(0), counts);
            }

            return _articleOutcomes = counts.ToArray();
        }
    }

    /// <summary>
    /// Adds one member's legal-content outcomes to <paramref name="counts"/> by the corpus's disposition
    /// token, verbatim. An outcome of another domain (a PDF act scope, Europe's) is not one and is not
    /// counted.
    /// </summary>
    private static void CountArticleOutcomes(string stage3OutcomesJson, SortedDictionary<string, long> counts)
    {
        var domain = ContractWire.NameOf(LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent);
        using var document = JsonDocument.Parse(stage3OutcomesJson);
        foreach (var outcome in document.RootElement.EnumerateArray())
        {
            if (!string.Equals(outcome.GetProperty("domain").GetString(), domain, StringComparison.Ordinal))
            {
                continue;
            }

            var disposition = outcome.GetProperty("disposition").GetString()!;
            counts[disposition] = counts.GetValueOrDefault(disposition) + 1;
        }
    }

    /// <summary>
    /// The searchable text of each of the given article identities, the bytes <see cref="SearchStateArticles"/> matches, as
    /// stored: one lookup by primary key per identity, so the cost is the identities asked and not the table. An identity the
    /// index does not hold is absent from the result; the caller decides whether that is a defect.
    /// </summary>
    public IReadOnlyDictionary<string, string> ResolveArticleSearchableTexts(IReadOnlyList<string> articleIdentities)
    {
        ArgumentNullException.ThrowIfNull(articleIdentities);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (articleIdentities.Count == 0)
        {
            return texts;
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT article_identity_sha256, searchable_text
                FROM articles
                WHERE article_identity_sha256 IN (SELECT value FROM json_each($identities))
                """;
            command.Parameters.AddWithValue(
                "$identities", JsonSerializer.Serialize(articleIdentities.Distinct(StringComparer.Ordinal).ToArray()));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                texts[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return texts;
    }

    /// <summary>
    /// For every state the index holds, its articles with searchable text grouped by the outcome and rights disposition the
    /// corpus recorded for the member each was read from, in state digest, outcome and rights order: what a search's
    /// population is counted from. One pass over the states' articles and their members; a state none of whose articles
    /// has searchable text still has its rows, counting zero.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexStateTextSource> ResolveStateTextSources()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT s.state_sha256, m.outcome, m.rights_disposition,
                       SUM(CASE WHEN length(a.searchable_text) > 0 THEN 1 ELSE 0 END)
                FROM states s, json_each(s.article_identities_json) j
                JOIN articles a ON a.article_identity_sha256 = j.value AND a.language = s.language
                JOIN members m ON m.object_ref_sha256 = a.object_ref_sha256
                GROUP BY s.state_sha256, m.outcome, m.rights_disposition
                ORDER BY s.state_sha256, m.outcome, m.rights_disposition
                """;
            using var reader = command.ExecuteReader();
            var rows = new List<LuxembourgIndexStateTextSource>();
            while (reader.Read())
            {
                rows.Add(new LuxembourgIndexStateTextSource(
                    reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt64(3)));
            }

            return Array.AsReadOnly(rows.ToArray());
        }
    }

    /// <summary>
    /// The publisher's article-level applicability date for each of the given article identities, in
    /// the order given. An identity the index does not hold is reported with a <c>null</c> date; the
    /// caller decides whether that is a defect. Nothing is compared here.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexArticleDate> ResolveArticleDates(IReadOnlyList<string> articleIdentities)
    {
        ArgumentNullException.ThrowIfNull(articleIdentities);
        if (articleIdentities.Count == 0)
        {
            return Array.Empty<LuxembourgIndexArticleDate>();
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT article_identity_sha256, applicability_date
                FROM articles
                WHERE article_identity_sha256 IN (SELECT value FROM json_each($identities))
                """;
            command.Parameters.AddWithValue("$identities", JsonSerializer.Serialize(articleIdentities));
            using var reader = command.ExecuteReader();
            var dates = new Dictionary<string, string?>(StringComparer.Ordinal);
            while (reader.Read())
            {
                dates[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }

            return Array.AsReadOnly(articleIdentities
                .Select(identity => new LuxembourgIndexArticleDate(
                    identity, dates.TryGetValue(identity, out var date) ? date : null))
                .ToArray());
        }
    }

    /// <summary>
    /// The articles carrying the publisher-minted article id <paramref name="anchor"/> in each of the
    /// given states, ordered by state digest then article identity. A state that does not carry the
    /// anchor contributes no row; a state that carries it more than once contributes each article.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexAnchorArticle> ResolveAnchorArticles(
        IReadOnlyList<string> stateDigests, string anchor)
    {
        ArgumentNullException.ThrowIfNull(stateDigests);
        ArgumentException.ThrowIfNullOrWhiteSpace(anchor);
        if (stateDigests.Count == 0)
        {
            return Array.Empty<LuxembourgIndexAnchorArticle>();
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.AnchorArticles;
            command.Parameters.AddWithValue("$states", JsonSerializer.Serialize(stateDigests));
            command.Parameters.AddWithValue("$anchor", anchor);
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexAnchorArticle>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexAnchorArticle(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    WordingSha256(reader.GetString(5))));
            }
            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The wording digest of one stored token stream: SHA-256 over the canonical JSON array of
    /// <c>[kind, text, target]</c> for its Text and Reference tokens, in order, with consecutive
    /// text merged into one entry first. The producer emits one text token per XML text node, so a
    /// paragraph or inline-formatting boundary splits text without changing a word; merging makes the
    /// digest blind to the boundary and to nothing else. Notes and markers are not words of the
    /// article and are left out before merging. Content /3's wording is in it too: deleted wording as
    /// <c>["deleted_text", text, target]</c>, consecutive deleted text without a target merged as text is, and each
    /// quotation boundary as <c>[kind, null, null]</c>, so a stream with neither digests as it always did.
    /// </summary>
    internal static string WordingSha256(string tokensJson)
    {
        using var stream = JsonDocument.Parse(tokensJson);
        var words = new List<string?[]>();
        var pendingText = new StringBuilder();
        string? pendingKind = null;
        void FlushText()
        {
            if (pendingKind is not null)
            {
                words.Add([pendingKind, pendingText.ToString(), null]);
                pendingText.Clear();
                pendingKind = null;
            }
        }

        foreach (var token in stream.RootElement.EnumerateArray())
        {
            var kind = token.GetProperty("kind").GetString();
            var text = token.TryGetProperty("text", out var textValue) && textValue.ValueKind == JsonValueKind.String ? textValue.GetString() : null;
            var target = token.TryGetProperty("target", out var targetValue) && targetValue.ValueKind == JsonValueKind.String ? targetValue.GetString() : null;
            if (kind == "text" || (kind == "deleted_text" && target is null))
            {
                if (pendingKind != kind)
                {
                    FlushText();
                    pendingKind = kind;
                }
                pendingText.Append(text);
                continue;
            }

            if (kind is "quotation_start" or "quotation_end")
            {
                FlushText();
                words.Add([kind, null, null]);
                continue;
            }

            if (kind is not ("reference" or "deleted_text"))
            {
                continue;
            }

            FlushText();
            words.Add([kind, text, target]);
        }

        FlushText();
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(words)));
    }

    /// <summary>
    /// Every article one state binds, with its publisher-minted id and wording digest, ordered by
    /// publisher id then identity, in one query.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexStateArticle> ResolveStateArticles(string stateSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSha256);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.StateArticles;
            command.Parameters.AddWithValue("$digest", stateSha256);
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexStateArticle>();
            while (reader.Read())
            {
                var tokensJson = reader.GetString(2);
                values.Add(new LuxembourgIndexStateArticle(
                    reader.GetString(0), reader.GetString(1), WordingSha256(tokensJson),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5), tokensJson, reader.GetString(6)));
            }
            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>The distinct publisher-minted article ids of one state, in ordinal order.</summary>
    public IReadOnlyList<string> ResolveArticleIds(string stateSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSha256);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.ArticleIds;
            command.Parameters.AddWithValue("$digest", stateSha256);
            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
            {
                values.Add(reader.GetString(0));
            }
            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The references the publisher wrote in the articles one state binds, or in the one carrying a publisher id, as
    /// the forward edges of lane R4's edge table: by the citing article's publisher id and identity, then the
    /// order the references occur in it. Read from <c>relations</c> by the citing article's primary key, so the cost
    /// is the state's articles and their references and never a scan; the rows were verified as the projection of
    /// the articles' tokens when the index was opened.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexCitation> ResolveStateCitations(string stateSha256, string? anchor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSha256);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.StateCitations;
            command.Parameters.AddWithValue("$digest", stateSha256);
            command.Parameters.AddWithValue("$anchor", (object?)anchor ?? DBNull.Value);
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexCitation>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexCitation(
                    reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3) == 1,
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The product work key of each of the given IRIs that some state of the index carries as its publisher legal-resource
    /// IRI or as its publisher work IRI, and only those: an IRI names a held work when it is exactly one of those two
    /// strings of some state, by string equality and by nothing else. One pass over <c>states</c> for each column, for
    /// the whole list.
    /// </summary>
    public IReadOnlyDictionary<string, string> ResolveHeldWorks(IReadOnlyList<string> iris)
    {
        ArgumentNullException.ThrowIfNull(iris);
        var held = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (iris.Count == 0)
        {
            return held;
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = _version == 9 ? LuxembourgIndexQueries.HeldWorksNine : LuxembourgIndexQueries.HeldWorks;
            command.Parameters.AddWithValue("$iris", JsonSerializer.Serialize(iris.Distinct(StringComparer.Ordinal)));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                held[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return held;
    }

    /// <summary>
    /// The forward edges whose target is exactly one of the given IRIs, each with the citing article's publisher id,
    /// expression and language: lane R4's edges read by their target, from the table that
    /// <see cref="ResolveStateCitations"/> reads by the citing article, so the two cannot disagree about a pair of texts.
    /// By the citing expression, publisher id, identity and place in the article.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexInboundCitation> ResolveCitationsTo(IReadOnlyList<string> targetIris)
    {
        ArgumentNullException.ThrowIfNull(targetIris);
        if (targetIris.Count == 0)
        {
            return Array.Empty<LuxembourgIndexInboundCitation>();
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.CitationsTo;
            command.Parameters.AddWithValue("$iris", JsonSerializer.Serialize(targetIris.Distinct(StringComparer.Ordinal)));
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexInboundCitation>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexInboundCitation(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetInt32(4), reader.GetInt32(5) == 1,
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetString(8)));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The held works whose publisher work IRI starts with <paramref name="workIriPrefix"/> (the works of one ELI type and date,
    /// <c>…/eli/etat/leg/loi/1991/08/10/</c>, for an exact reference by type and date): each work key with its work IRI, once,
    /// in work-key order. An empty prefix is refused, since it would read every state.
    /// </summary>
    public IReadOnlyList<(string WorkKey, string PublisherWorkIri)> ResolveWorksUnder(string workIriPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(workIriPrefix);
        var last = workIriPrefix[^1];
        if (last >= '\u007f')
        {
            throw new ArgumentException("A work prefix ends in a character outside ASCII.", nameof(workIriPrefix));
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $$"""
                SELECT DISTINCT s.work_key,s.publisher_work_iri
                FROM states s
                WHERE s.publisher_work_iri>=$low AND s.publisher_work_iri<$high{{HeldOnly("s")}}
                ORDER BY s.work_key,s.publisher_work_iri
                """;
            command.Parameters.AddWithValue("$low", workIriPrefix);
            command.Parameters.AddWithValue("$high", workIriPrefix[..^1] + (char)(last + 1));
            using var reader = command.ExecuteReader();
            var values = new List<(string, string)>();
            while (reader.Read())
            {
                values.Add((reader.GetString(0), reader.GetString(1)));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// Every subject IRI of the fact table that starts with <paramref name="prefix"/>, once, in ordinal order: the works, versions
    /// and expressions the publisher's catalogue names under one ELI (a type and day, <c>…/eli/etat/leg/loi/2018/02/03/</c>, for
    /// an exact reference by type and date), whether or not the index holds a version of them. It reads the fact table's key
    /// range, and an empty prefix is refused, since it would read the whole table.
    /// </summary>
    public IReadOnlyList<string> ResolveSubjectsUnder(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        var last = prefix[^1];
        if (last >= '\u007f')
        {
            throw new ArgumentException("A subject prefix ends in a character outside ASCII.", nameof(prefix));
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT subject_iri
                FROM work_facts
                WHERE subject_iri>=$low AND subject_iri<$high
                ORDER BY subject_iri
                """;
            command.Parameters.AddWithValue("$low", prefix);
            command.Parameters.AddWithValue("$high", prefix[..^1] + (char)(last + 1));
            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
            {
                values.Add(reader.GetString(0));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The publisher's catalogue references an exact reference is read against: each subject the catalogue says is part of an
    /// issue of the Mémorial (<c>isPartOf</c> an IRI under <c>…/eli/etat/leg/memorial/</c>), and each subject's historical legal
    /// identifier (<c>historicalLegalId</c>), verbatim, in subject, predicate and value order. It reads the fact table once (the
    /// caller keeps it), and holds every subject the catalogue names, whether or not the index holds its text.
    /// </summary>
    public IReadOnlyList<(string SubjectIri, string Predicate, string Value)> ResolveCatalogueReferences()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT subject_iri,predicate,object_value
                FROM work_facts
                WHERE (predicate='isPartOf' AND object_value GLOB 'http://data.legilux.public.lu/eli/etat/leg/memorial/*')
                   OR predicate='historicalLegalId'
                ORDER BY subject_iri,predicate,object_value
                """;
            using var reader = command.ExecuteReader();
            var values = new List<(string, string, string)>();
            while (reader.Read())
            {
                values.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>The states of the given expressions, by publisher date, work key, language, expression and digest.</summary>
    public IReadOnlyList<LuxembourgIndexResolvedState> ResolveStatesOfExpressions(IReadOnlyList<string> expressionIris)
    {
        ArgumentNullException.ThrowIfNull(expressionIris);
        if (expressionIris.Count == 0)
        {
            return Array.Empty<LuxembourgIndexResolvedState>();
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = _version == 9 ? LuxembourgIndexQueries.StatesOfExpressionsNine : LuxembourgIndexQueries.StatesOfExpressions;
            command.Parameters.AddWithValue("$expressions", JsonSerializer.Serialize(expressionIris.Distinct(StringComparer.Ordinal)));
            return ReadResolvedStates(command);
        }
    }

    private static IReadOnlyList<LuxembourgIndexResolvedState> ReadResolvedStates(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexResolvedState>();
        while (reader.Read())
        {
            values.Add(new LuxembourgIndexResolvedState(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6),
                JsonSerializer.Deserialize<string[]>(reader.GetString(7))
                    ?? throw new InvalidDataException("A state has no rule-profile population."),
                JsonSerializer.Deserialize<string[]>(reader.GetString(8))
                    ?? throw new InvalidDataException("A state has no article-identity population.")));
        }
        return Array.AsReadOnly(values.ToArray());
    }

    public LuxembourgIndexWorkResolution ResolveWorkTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var normalized = LuxembourgIndexBuilder.NormalizeTitle(title);
        lock (_gate)
        {
            var rows = ReadWorkTitles(_connection);
            if (rows.Length == 0)
            {
                return new LuxembourgIndexWorkResolution(false, Array.Empty<LuxembourgIndexResolvedWork>());
            }
            if (normalized.Length == 0)
            {
                return new LuxembourgIndexWorkResolution(true, Array.Empty<LuxembourgIndexResolvedWork>());
            }

            var exact = rows.Where(row => string.Equals(
                row.NormalizedTitle, normalized, StringComparison.Ordinal)).ToArray();
            var reason = "exact_normalized_title";
            var selected = exact;
            if (selected.Length == 0)
            {
                selected = rows.Where(row => row.NormalizedTitle.StartsWith(
                    normalized, StringComparison.Ordinal)).ToArray();
                reason = "prefix";
            }
            if (selected.Length == 0)
            {
                var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                selected = rows.Where(row => tokens.All(token =>
                    row.NormalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Contains(token, StringComparer.Ordinal))).ToArray();
                reason = "all_tokens_contained";
            }

            var candidates = selected
                .OrderBy(static row => row.NormalizedTitle, StringComparer.Ordinal)
                .ThenBy(static row => row.WorkIdentifier, StringComparer.Ordinal)
                .GroupBy(static row => row.WorkIdentifier, StringComparer.Ordinal)
                .OrderByDescending(static group => group.Max(row => row.DocumentDate), StringComparer.Ordinal)
                .ThenBy(static group => group.Key, StringComparer.Ordinal)
                .Select(group => new LuxembourgIndexResolvedWork(
                    group.Key,
                    Array.AsReadOnly(group.Select(static row => row.ExpressionIri)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                    Array.AsReadOnly(group.Select(static row => row.Language)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                    group.OrderBy(static row => row.Title, StringComparer.Ordinal).ThenBy(static row => row.Language, StringComparer.Ordinal).First().Title,
                    group.OrderBy(static row => row.Title, StringComparer.Ordinal).ThenBy(static row => row.Language, StringComparer.Ordinal).First().Language,
                    reason))
                .ToArray();
            return new LuxembourgIndexWorkResolution(true, Array.AsReadOnly(candidates));
        }
    }

    /// <summary>
    /// The titles held for the given expressions (one work's), each once, in language, expression, kind,
    /// words and evidence order. It scans the title table: see <see cref="LuxembourgIndexQueries.WorkTitles"/>.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexWorkTitle> ResolveWorkTitles(IReadOnlyList<string> expressionIris)
    {
        ArgumentNullException.ThrowIfNull(expressionIris);
        if (expressionIris.Count == 0)
        {
            return Array.Empty<LuxembourgIndexWorkTitle>();
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.WorkTitles;
            command.Parameters.AddWithValue("$expressions", JsonSerializer.Serialize(expressionIris));
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexWorkTitle>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexWorkTitle(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4)));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The publisher's typed assertions about the given subjects (work, expression or manifestation IRIs),
    /// verbatim, in the fact table's order (<see cref="LuxembourgIndexQueries.SubjectFacts"/>). An
    /// unknown subject simply has no rows.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexWorkFact> ResolveFacts(IReadOnlyList<string> subjectIris)
    {
        ArgumentNullException.ThrowIfNull(subjectIris);
        if (subjectIris.Count == 0)
        {
            return Array.Empty<LuxembourgIndexWorkFact>();
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.SubjectFacts;
            command.Parameters.AddWithValue("$subjects", JsonSerializer.Serialize(subjectIris));
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexWorkFact>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexWorkFact(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The publisher's typed assertions about every subject whose IRI starts with <paramref name="prefix"/>, verbatim,
    /// in the fact table's order (<see cref="LuxembourgIndexQueries.SubjectFactsUnder"/>): the consolidated versions the
    /// publisher names under a work (<c>{work}/consolide/</c>), say, which the index holds facts about and no state of.
    /// An empty prefix is refused, since it would read the whole table.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexWorkFact> ResolveFactsUnder(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        var last = prefix[^1];
        if (last >= '\u007f')
        {
            // The range's upper bound raises the last character by one, which is a byte's order only for ASCII.
            throw new ArgumentException("A subject prefix ends in a character outside ASCII.", nameof(prefix));
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.SubjectFactsUnder;
            command.Parameters.AddWithValue("$low", prefix);
            command.Parameters.AddWithValue("$high", prefix[..^1] + (char)(last + 1));
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexWorkFact>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexWorkFact(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// One page of the held works in work-key order (<see cref="LuxembourgIndexQueries.WorkRecords"/>),
    /// optionally those with a state in a language, those whose publisher typeDocument is the given IRI or
    /// ends in the given token, and those after a work key. <paramref name="take"/> rows at most.
    /// </summary>
    /// <summary>How many events the log holds and its last sequence number (0 when it holds none).</summary>
    public LuxembourgIndexEventLog ResolveEventLog()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.EventLog;
            using var reader = command.ExecuteReader();
            reader.Read();
            return new LuxembourgIndexEventLog(reader.GetInt64(0), reader.GetInt64(1));
        }
    }

    /// <summary>
    /// The events after a sequence number, in sequence order, at most <paramref name="take"/>, optionally
    /// of one event name (<see cref="LuxembourgIndexQueries.EventsAfter"/>,
    /// <see cref="LuxembourgIndexQueries.EventsOfNameAfter"/>). The key and detail are read
    /// back into the state's key columns and digest, which the reader verified against the states table
    /// when it opened.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexEvent> ResolveEvents(long afterSeq, string? eventName, int take)
    {
        if (afterSeq < 0) throw new ArgumentOutOfRangeException(nameof(afterSeq));
        if (take < 1) throw new ArgumentOutOfRangeException(nameof(take));
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            if (eventName is null)
            {
                command.CommandText = LuxembourgIndexQueries.EventsAfter;
            }
            else
            {
                command.CommandText = LuxembourgIndexQueries.EventsOfNameAfter;
                command.Parameters.AddWithValue("$event", eventName);
            }

            command.Parameters.AddWithValue("$after", afterSeq);
            command.Parameters.AddWithValue("$take", take);
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexEvent>();
            while (reader.Read())
            {
                var key = JsonSerializer.Deserialize<string[]>(reader.GetString(2))!;
                using var detail = JsonDocument.Parse(reader.GetString(5));
                values.Add(new LuxembourgIndexEvent(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    key[0], key[1], key[2], key[3],
                    detail.RootElement.GetProperty("state_sha256").GetString()!,
                    reader.GetString(5)));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The observations of the event log, in order (<see cref="LuxembourgIndexQueries.Observations"/>); none for an index
    /// that records no builds (<see cref="RecordsBuilds"/>), which has no observation to name.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexObservation> ResolveObservations()
    {
        if (!RecordsBuilds) return Array.Empty<LuxembourgIndexObservation>();
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = _version == 9 ? LuxembourgIndexQueries.ObservationsNine : LuxembourgIndexQueries.Observations;
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexObservation>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexObservation(
                    reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5),
                    _version == 9 && !reader.IsDBNull(6) ? reader.GetString(6) : null));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The revising events (<c>validity_revised</c>, <c>interval_closed</c>) after a sequence number, in sequence order,
    /// at most <paramref name="take"/>, optionally of one work (<see cref="LuxembourgIndexQueries.RevisingEventsAfter"/>).
    /// </summary>
    public IReadOnlyList<LuxembourgIndexEvent> ResolveRevisingEvents(long afterSeq, string? workKey, int take)
    {
        if (afterSeq < 0) throw new ArgumentOutOfRangeException(nameof(afterSeq));
        if (take < 1) throw new ArgumentOutOfRangeException(nameof(take));
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.RevisingEventsAfter;
            command.Parameters.AddWithValue("$events", JsonSerializer.Serialize(V3EventRegistry.Revising));
            command.Parameters.AddWithValue("$after", afterSeq);
            command.Parameters.AddWithValue("$work", (object?)workKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$take", take);
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexEvent>();
            while (reader.Read())
            {
                var key = JsonSerializer.Deserialize<string[]>(reader.GetString(2))!;
                using var detail = JsonDocument.Parse(reader.GetString(5));
                values.Add(new LuxembourgIndexEvent(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    key[0], key[1], key[2], key[3],
                    detail.RootElement.GetProperty("state_sha256").GetString()!,
                    reader.GetString(5)));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The states of one work the event log held after its events up to <paramref name="lastSeq"/> (an observation's last
    /// event), folded as the builder folds a predecessor's log: a later event of a key replaces an earlier one, and a key
    /// once held stays held (absence is not a withdrawal). Ordered by language, date and expression
    /// (<see cref="LuxembourgIndexQueries.WorkEventsUpTo"/>).
    /// </summary>
    public IReadOnlyList<LuxembourgIndexObservedState> ResolveObservedStates(string workKey, long lastSeq)
    {
        ArgumentException.ThrowIfNullOrEmpty(workKey);
        if (lastSeq < 0) throw new ArgumentOutOfRangeException(nameof(lastSeq));
        if (!RecordsBuilds)
        {
            throw new InvalidOperationException("An index that records no builds names no observation to fold its log to.");
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.WorkEventsUpTo;
            command.Parameters.AddWithValue("$work", workKey);
            command.Parameters.AddWithValue("$last", lastSeq);
            using var reader = command.ExecuteReader();
            var events = new List<LuxembourgIndexBuilder.EventRow>();
            while (reader.Read())
            {
                events.Add(new(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
            }

            // Until the interval rules land (S7a), as_observed reads the held states of the log, as before index 9.
            return Array.AsReadOnly(LuxembourgIndexBuilder.FoldLog(events, LogSchema).Values
                .Where(static held => held.TextStatus is null || string.Equals(held.TextStatus, LuxembourgIndexBuilder.TextHeld, StringComparison.Ordinal))
                .Select(static held => new LuxembourgIndexObservedState(
                    held.WorkKey, held.ApplicabilityDate, held.ExpressionIri, held.Language, held.StateSha256,
                    Array.AsReadOnly(held.SourceBodies.ToArray())))
                .OrderBy(static held => held.Language, StringComparer.Ordinal)
                .ThenBy(static held => held.ApplicabilityDate, StringComparer.Ordinal)
                .ThenBy(static held => held.ExpressionIri, StringComparer.Ordinal)
                .ToArray());
        }
    }

    /// <summary>How many events of the given names the log holds.</summary>
    public long CountEvents(IReadOnlyList<string> eventNames)
    {
        ArgumentNullException.ThrowIfNull(eventNames);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.EventCount;
            command.Parameters.AddWithValue("$events", JsonSerializer.Serialize(eventNames));
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public IReadOnlyList<LuxembourgIndexWorkRecord> ResolveWorkRecords(string? language, string? type, string? afterWorkKey, int take)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = _version == 9 ? LuxembourgIndexQueries.WorkRecordsNine : LuxembourgIndexQueries.WorkRecords;
            command.Parameters.AddWithValue("$after", (object?)afterWorkKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$language", (object?)language ?? DBNull.Value);
            command.Parameters.AddWithValue("$type", (object?)type ?? DBNull.Value);
            command.Parameters.AddWithValue("$take", take);
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexWorkRecord>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexWorkRecord(
                    reader.GetString(0),
                    SortedStrings(reader.GetString(1)),
                    SortedStrings(reader.GetString(2)),
                    SortedStrings(reader.GetString(3)),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt64(6)));
            }

            return Array.AsReadOnly(values.ToArray());
        }

        static IReadOnlyList<string> SortedStrings(string json) =>
            Array.AsReadOnly((JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    /// <remarks>
    /// <b>Not what the API serves.</b> This is the period-scoped lookup that gates on the capability
    /// manifest per date range and returns article identities only; nothing in <c>src</c> calls it, and
    /// it is kept for the capability-gate tests that pin <c>filter_not_supported_by_index</c>. The
    /// <c>search</c> operation reads <see cref="SearchStateArticles"/>, which gates on the language
    /// having any measured searchable text and returns each hit with the state that holds it.
    /// </remarks>
    public LuxembourgIndexSearchResult Search(
        string language,
        DateOnly from,
        DateOnly to,
        string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var outcome = _capabilityManifest.Lookup(
            "search", "articles", "searchable_text", language, from, to, out _);
        if (outcome != V3IndexCapabilityLookupOutcome.Supported)
        {
            return new LuxembourgIndexSearchResult(outcome, Array.Empty<string>());
        }

        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT article_identity_sha256 FROM articles
            WHERE language=$language AND applicability_date BETWEEN $from AND $to
              AND instr(searchable_text,$query) > 0
            ORDER BY article_identity_sha256
            """;
        command.Parameters.AddWithValue("$language", language);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$query", query);
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return new LuxembourgIndexSearchResult(outcome, values.AsReadOnly());
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (_deleteOnDispose)
        {
            LuxembourgIndexBuilder.DeleteDatabase(_path);
        }
    }

    private long Count(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = table switch
        {
            "members" => "SELECT count(*) FROM members",
            "articles" => "SELECT count(*) FROM articles",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IEnumerable<LuxembourgIndexBuilder.MemberRow> StreamMembers(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT object_ref_sha256,source_ordinal,outcome,rights_disposition,stage3_outcomes_json,gaps_json FROM members ORDER BY object_ref_sha256";
        using var reader = command.ExecuteReader();
        while (reader.Read()) yield return new(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5));
    }

    /// <summary>Every article row in identity order: with its document position on index 9, without on index 8 and 6.</summary>
    private static LuxembourgIndexBuilder.ArticleRow[] ReadArticles(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,expression_iri,publisher_id,publisher_wid,applicability_date,language,rule_profile_sha256,searchable_text,tokens_json" +
            (version == 9 ? ",document_position" : "") + " FROM articles ORDER BY article_identity_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.ArticleRow>();
        while (reader.Read()) values.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
            version == 9 ? checked((int)reader.GetInt64(10)) : null));
        return values.ToArray();
    }

    /// <summary>
    /// Index 9's document positions: one body's articles hold distinct places in its document (the table's CHECK holds
    /// each to 1 or more); the places themselves are the inventory's, which the corpus binds.
    /// </summary>
    private static void ValidateDocumentPositions(IReadOnlyList<LuxembourgIndexBuilder.ArticleRow> articles)
    {
        var seen = new HashSet<(string, int)>();
        foreach (var article in articles)
        {
            if (article.DocumentPosition is not int position || !seen.Add((article.ObjectRefSha256, position)))
            {
                throw new InvalidDataException(
                    "The Luxembourg index gives two articles of one body the same place in its document: " + article.PublisherId);
            }
        }
    }

    /// <summary>Every state row in key order: schema 9's twenty-one columns, or schema 8's and 6's nine.</summary>
    internal static LuxembourgIndexBuilder.StateRow[] ReadStates(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = version == 9
            ? "SELECT " + LuxembourgIndexQueries.StateColumnsNine + " FROM states ORDER BY work_key,applicability_date,expression_iri,language"
            : "SELECT work_key,applicability_date,state_sha256,expression_iri,publisher_work_iri,publisher_legal_resource_iri,language,rule_profiles_json,article_identities_json FROM states ORDER BY work_key,applicability_date,expression_iri,language";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.StateRow>();
        while (reader.Read()) values.Add(version == 9 ? StateRowNine(reader) : new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8)));
        return values.ToArray();
    }

    /// <summary>One schema-9 state row, read in <see cref="LuxembourgIndexQueries.StateColumnsNine"/>'s order.</summary>
    private static LuxembourgIndexBuilder.StateRow StateRowNine(SqliteDataReader reader)
    {
        string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        return new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.GetString(9), reader.GetString(10), Text(11), Text(12), reader.GetString(13),
            Text(14), reader.GetString(15), Text(16), Text(17), Text(18), Text(19), reader.GetInt64(20) == 1, Text(21));
    }

    internal static LuxembourgIndexBuilder.IntervalRow[] ReadIntervals(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_key,ordinal,kind,from_state_sha256,to_state_sha256,span_from,span_to FROM intervals ORDER BY work_key,ordinal";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.IntervalRow>();
        while (reader.Read()) values.Add(new(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        return values.ToArray();
    }

    private static void ValidateStates(
        IReadOnlyList<LuxembourgIndexBuilder.ArticleRow> articles,
        IReadOnlyList<LuxembourgIndexBuilder.StateRow> states)
    {
        // The article-level publisher date is served and compared with the state date as text; both
        // sides are checked here as exact civil dates so a malformed index is refused, never served.
        // A null date is the publisher stating none, which is allowed.
        foreach (var article in articles)
        {
            if (article.ApplicabilityDate is not null &&
                !DateOnly.TryParseExact(
                    article.ApplicabilityDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
            {
                throw new InvalidDataException("A Luxembourg article applicability date is not an exact civil date.");
            }
        }

        var articleByIdentity = articles.ToDictionary(
            static article => article.ArticleIdentitySha256, StringComparer.Ordinal);
        var seenArticles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            var profiles = JsonSerializer.Deserialize<string[]>(state.RuleProfilesJson)
                ?? throw new InvalidDataException("A state has no rule-profile population.");
            var identities = JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)
                ?? throw new InvalidDataException("A state has no article-identity population.");
            if (profiles.Length == 0 || identities.Length == 0 ||
                !profiles.SequenceEqual(profiles.Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !identities.SequenceEqual(identities.Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                profiles.Any(static value => !IsSha256(value)) ||
                identities.Any(static value => !IsSha256(value)) ||
                !DateOnly.TryParseExact(
                    state.ApplicabilityDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _) ||
                !string.Equals(
                    LuxembourgIndexBuilder.WorkKeyOf(state.PublisherWorkIri), state.WorkKey,
                    StringComparison.Ordinal) ||
                !state.PublisherLegalResourceIri.StartsWith(
                    state.PublisherWorkIri + "/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("A Luxembourg expression state is not canonical.");
            }

            foreach (var identity in identities)
            {
                if (!articleByIdentity.TryGetValue(identity, out var article) ||
                    !seenArticles.Add(identity) ||
                    !string.Equals(article.ExpressionIri, state.ExpressionIri, StringComparison.Ordinal) ||
                    !profiles.Contains(article.RuleProfileSha256, StringComparer.Ordinal))
                {
                    throw new InvalidDataException(
                        "A Luxembourg expression state does not bind its exact article population.");
                }
                if (!string.Equals(article.Language, state.Language, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "A Luxembourg expression state language contradicts its articles.");
            }
            var expectedIdentities = articles
                .Where(article => string.Equals(
                    article.ExpressionIri, state.ExpressionIri, StringComparison.Ordinal))
                .Select(static article => article.ArticleIdentitySha256)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!identities.SequenceEqual(expectedIdentities, StringComparer.Ordinal) ||
                !state.ExpressionIri.StartsWith(
                    state.PublisherLegalResourceIri + "/", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A Luxembourg expression state omits or crosses its expression population.");
            }
            if (!string.Equals(
                    LuxembourgIndexBuilder.StateSha256(
                        state.WorkKey, state.ApplicabilityDate, state.ExpressionIri,
                        state.PublisherWorkIri, state.PublisherLegalResourceIri, state.Language,
                        profiles, identities),
                    state.StateSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("A Luxembourg expression state digest is not derived from its row.");
            }
        }
    }

    /// <summary>
    /// Index 9's checks of its state rows, each refused rather than served: the article dates; every row's own facts
    /// canonical (its work and key from its legal resource, its kind from its IRI, a role its kind allows, an in-file role
    /// dating it by its file's date, a text status and reason that agree, an expression under its legal resource or none
    /// with no language, a translation naming the French row of its legal resource, an end the publisher states only for a
    /// consolidation or a code state, an entry into force only on an act's row and equal to a date of that role,
    /// <c>provisional</c> exactly when its date is after the observation bound); a held row
    /// binds exactly its expression's articles, as schema 8's did, a textless row none; its digest is state digest /2 of
    /// the row; and the chain fields and interval transitions are exactly what the rows' own facts give.
    /// </summary>
    private static void ValidateCatalogueStates(
        IReadOnlyList<LuxembourgIndexBuilder.ArticleRow> articles,
        IReadOnlyList<LuxembourgIndexBuilder.StateRow> states,
        IReadOnlyList<LuxembourgIndexBuilder.IntervalRow> intervals,
        string builtAtDate)
    {
        foreach (var article in articles)
        {
            if (article.ApplicabilityDate is not null && !LuxembourgIndexBuilder.IsExactDate(article.ApplicabilityDate))
            {
                throw new InvalidDataException("A Luxembourg article applicability date is not an exact civil date.");
            }
        }

        var articleByIdentity = articles.ToDictionary(static article => article.ArticleIdentitySha256, StringComparer.Ordinal);
        var identitiesByExpression = articles
            .GroupBy(static article => article.ExpressionIri, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static article => article.ArticleIdentitySha256).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var frenchByRoot = states
            .Where(static state => string.Equals(state.Language, "fra", StringComparison.Ordinal))
            .GroupBy(static state => state.PublisherLegalResourceIri, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Select(static state => state.ExpressionIri).ToArray(), StringComparer.Ordinal);
        var identityClasses = Enum.GetValues<LuxembourgAknInFileIdentityClass>().Select(static value => ContractWire.NameOf(value)).ToHashSet(StringComparer.Ordinal);
        var textStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            LuxembourgIndexBuilder.TextHeld, LuxembourgIndexBuilder.TextWithheldRights, LuxembourgIndexBuilder.TextNotAvailablePdf,
            LuxembourgIndexBuilder.TextNotAvailableDerivation, LuxembourgIndexBuilder.TextNotAvailableMisfiled, LuxembourgIndexBuilder.TextNotAcquired,
        };
        var seenArticles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            var profiles = JsonSerializer.Deserialize<string[]>(state.RuleProfilesJson)
                ?? throw new InvalidDataException("A state has no rule-profile population.");
            var identities = JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)
                ?? throw new InvalidDataException("A state has no article-identity population.");
            var held = string.Equals(state.TextStatus, LuxembourgIndexBuilder.TextHeld, StringComparison.Ordinal);
            var translation = LuxembourgIndexBuilder.IsTranslationLanguage(state.Language);
            var french = frenchByRoot.GetValueOrDefault(state.PublisherLegalResourceIri) ?? [];
            string? work = null;
            string? kind = null;
            try
            {
                work = LuxembourgIndexBuilder.WorkOfState(state.PublisherLegalResourceIri);
                kind = LuxembourgIndexBuilder.StateKindOf(state.PublisherLegalResourceIri);
            }
            catch (InvalidDataException)
            {
            }

            var roleAllowed = string.Equals(state.StateKind, LuxembourgIndexBuilder.KindAsPublished, StringComparison.Ordinal)
                ? state.DateRole is LuxembourgIndexBuilder.RoleApplicabilityInFile or LuxembourgIndexBuilder.RoleEntryIntoForce
                : string.Equals(state.DateRole, LuxembourgIndexBuilder.RoleApplicability, StringComparison.Ordinal);
            if (!LuxembourgIndexBuilder.IsExactDate(state.ApplicabilityDate) ||
                (state.ApplicableTo is not null && !LuxembourgIndexBuilder.IsExactDate(state.ApplicableTo)) ||
                (state.PublicationDate is not null && !LuxembourgIndexBuilder.IsExactDate(state.PublicationDate)) ||
                (state.InFileDate is not null && !LuxembourgIndexBuilder.IsExactDate(state.InFileDate)) ||
                (state.EntryIntoForce is not null && (!LuxembourgIndexBuilder.IsExactDate(state.EntryIntoForce) ||
                    !string.Equals(state.StateKind, LuxembourgIndexBuilder.KindAsPublished, StringComparison.Ordinal))) ||
                (!translation && string.Equals(state.DateRole, LuxembourgIndexBuilder.RoleEntryIntoForce, StringComparison.Ordinal) &&
                 !string.Equals(state.EntryIntoForce, state.ApplicabilityDate, StringComparison.Ordinal)) ||
                work is null || !string.Equals(work, state.PublisherWorkIri, StringComparison.Ordinal) ||
                !string.Equals(LuxembourgIndexBuilder.WorkKeyOf(state.PublisherWorkIri), state.WorkKey, StringComparison.Ordinal) ||
                !string.Equals(kind, state.StateKind, StringComparison.Ordinal) || !roleAllowed ||
                state.TextStatus is null || !textStatuses.Contains(state.TextStatus) || held != (state.TextStatusReason is null) ||
                (state.InFileIdentity is not null && !identityClasses.Contains(state.InFileIdentity)) ||
                (state.ExpressionIri.Length != 0 && !state.ExpressionIri.StartsWith(state.PublisherLegalResourceIri + "/", StringComparison.Ordinal)) ||
                (state.Language.Length != 0 && (state.Language.Length != 3 || state.Language.Any(static c => c is < 'a' or > 'z'))) ||
                (state.TranslationOf is not null && (!translation || !french.Contains(state.TranslationOf, StringComparer.Ordinal))) ||
                (translation && french.Length == 1 && !string.Equals(state.TranslationOf, french[0], StringComparison.Ordinal)) ||
                (!translation && string.Equals(state.DateRole, LuxembourgIndexBuilder.RoleApplicabilityInFile, StringComparison.Ordinal) &&
                 !string.Equals(state.InFileDate, state.ApplicabilityDate, StringComparison.Ordinal)) ||
                (!translation && state.PublisherEnd is not null &&
                 string.Equals(state.StateKind, LuxembourgIndexBuilder.KindAsPublished, StringComparison.Ordinal)) ||
                state.Provisional != (string.CompareOrdinal(state.ApplicabilityDate, builtAtDate) > 0))
            {
                throw new InvalidDataException("A Luxembourg expression state is not canonical.");
            }

            if (!held)
            {
                if (profiles.Length != 0 || identities.Length != 0)
                {
                    throw new InvalidDataException("A textless Luxembourg state binds articles or profiles.");
                }
            }
            else
            {
                if (profiles.Length == 0 || identities.Length == 0 ||
                    !profiles.SequenceEqual(profiles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                    !identities.SequenceEqual(identities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                    profiles.Any(static value => !IsSha256(value)) || identities.Any(static value => !IsSha256(value)))
                {
                    throw new InvalidDataException("A Luxembourg expression state is not canonical.");
                }

                foreach (var identity in identities)
                {
                    if (!articleByIdentity.TryGetValue(identity, out var article) ||
                        !seenArticles.Add(identity) ||
                        !string.Equals(article.ExpressionIri, state.ExpressionIri, StringComparison.Ordinal) ||
                        !profiles.Contains(article.RuleProfileSha256, StringComparer.Ordinal))
                    {
                        throw new InvalidDataException("A Luxembourg expression state does not bind its exact article population.");
                    }

                    if (!string.Equals(article.Language, state.Language, StringComparison.Ordinal))
                        throw new InvalidDataException("A Luxembourg expression state language contradicts its articles.");
                }

                if (!identities.SequenceEqual(identitiesByExpression.GetValueOrDefault(state.ExpressionIri) ?? [], StringComparer.Ordinal))
                {
                    throw new InvalidDataException("A Luxembourg expression state omits or crosses its expression population.");
                }
            }

            if (!string.Equals(LuxembourgIndexBuilder.StateSha256Two(state), state.StateSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A Luxembourg expression state digest is not derived from its row.");
            }
        }

        var (completed, expected) = LuxembourgIndexBuilder.CompleteIntervals(states);
        if (!completed.SequenceEqual(states))
        {
            throw new InvalidDataException("A Luxembourg expression state's end or boundary is not the one its work's states give.");
        }

        if (!expected.SequenceEqual(intervals))
        {
            throw new InvalidDataException("The Luxembourg index interval rows are not the transitions of its states.");
        }
    }

    private static bool IsSha256(string value) => value.Length == 64 &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static LuxembourgIndexBuilder.WorkTitleRow[] ReadWorkTitles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_identifier,expression_iri,language,title,normalized_title,document_date,title_kind,evidence_sha256 FROM work_titles ORDER BY work_identifier,expression_iri,language,title,title_kind,evidence_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.WorkTitleRow>();
        while (reader.Read()) values.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
            reader.GetString(7)));
        return values.ToArray();
    }

    private static LuxembourgIndexBuilder.RelationRow[] ReadRelations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT from_ref,ordinal,edge_type,asserted_by,source_predicate,in_note,label,href,to_kind,to_ref FROM relations ORDER BY from_ref,ordinal";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.RelationRow>();
        while (reader.Read()) values.Add(new(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetInt32(5) == 1,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        return values.ToArray();
    }

    /// <summary>The log's own stamp: its schema, and the digest of exactly its observations and events.</summary>
    private static void VerifyLogStamp(
        SqliteConnection connection,
        IReadOnlyList<LuxembourgIndexBuilder.ObservationRow> observations,
        IReadOnlyList<LuxembourgIndexBuilder.EventRow> events,
        string logSchema)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT log_schema,log_rows_sha256 FROM log_stamp WHERE log_stamp_id=1";
        using var reader = command.ExecuteReader();
        if (!reader.Read() ||
            !string.Equals(reader.GetString(0), logSchema, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), LuxembourgIndexBuilder.HashEventLogOf(logSchema, observations, events), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Luxembourg event log does not match its log stamp.");
        }
    }

    /// <summary>
    /// Checks the event log against the corpus this index binds: every state's events name exactly the body digests of
    /// the members its articles were read from, and the last build's time is no earlier than the corpus's EU capture.
    /// The index alone holds neither a body digest nor a capture time, so whoever mounts it with its corpus calls this
    /// (the mount, the mount writer's verification, the build).
    /// </summary>
    public void VerifyEventLogSources(VerifiedLexCorpus6ManifestSet corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        VerifyEventLogSources(corpus.ArtifactRef, () => LuxembourgIndexBuilder.BodiesByObjectRef(corpus), corpus.Set.EuropeRightsMatrix.CapturedAt);
    }

    /// <summary>The same check, from a mount's view of the corpus (<see cref="LexCorpus6MountView"/>).</summary>
    public void VerifyEventLogSources(LexCorpus6MountView corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        VerifyEventLogSources(corpus.ArtifactRef, () => LuxembourgIndexBuilder.BodiesByObjectRef(corpus), corpus.EuropeRightsMatrix.CapturedAt);
    }

    private void VerifyEventLogSources(SourceArtifactRef corpusRef, Func<IReadOnlyDictionary<string, string>> bodies, string euCapturedAt)
    {
        if (corpusRef != CorpusRef)
        {
            throw new InvalidDataException("The corpus is not the one this Luxembourg index binds.");
        }

        if (!RecordsBuilds)
        {
            // Schema 6 recorded no source bodies and no build time: there is nothing of the log to hold to the corpus
            // beyond the binding above, and nothing is invented to hold.
            return;
        }

        lock (_gate)
        {
            var states = ReadStates(_connection, _version);
            var expected = LuxembourgIndexBuilder.SourceBodiesOfStates(
                ReadArticles(_connection, _version), states, bodies());
            var held = LuxembourgIndexBuilder.FoldLog(LuxembourgIndexBuilder.ReadLogEvents(_connection), LogSchema);
            foreach (var state in states)
            {
                if (!held.TryGetValue(LuxembourgIndexBuilder.StateKey(state), out var logged) ||
                    !logged.SourceBodies.SequenceEqual(expected[state.StateSha256], StringComparer.Ordinal))
                {
                    throw new InvalidDataException("The Luxembourg event log names source bodies the corpus does not hold for its states.");
                }
            }

            // This build's time is an upper bound on its corpus's observation only if it is no earlier than the corpus's
            // EU capture; the index alone holds no capture time, so the check is made here, with the corpus.
            var builtAt = LuxembourgIndexBuilder.ReadLogObservations(_connection, LogSchema)[^1].BuiltAt;
            if (LuxembourgIndexBuilder.EarlierThanEuCapture(LuxembourgIndexBuilder.ParseBuiltAt(builtAt)!.Value, euCapturedAt) is { } reason)
            {
                throw new InvalidDataException($"The Luxembourg event log's last build time {builtAt} {reason}.");
            }
        }
    }

    private static IEnumerable<LuxembourgIndexBuilder.WorkFactRow> StreamWorkFacts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT subject_iri,predicate,fact_kind,object_kind,object_value,datatype_iri,language_tag,evidence_sha256 FROM work_facts ORDER BY subject_iri,predicate,object_kind,object_value,datatype_iri,language_tag,evidence_sha256";
        using var reader = command.ExecuteReader();
        while (reader.Read()) yield return new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7));
    }

}
