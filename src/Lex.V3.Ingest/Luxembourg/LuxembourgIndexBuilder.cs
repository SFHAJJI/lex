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
        IReadOnlyList<LuxembourgIndexBuilder.ObservationRow> observations,
        IReadOnlyList<LuxembourgIndexBuilder.EventRow> events)
    {
        IndexSha256 = indexSha256;
        Observations = observations;
        Events = events;
    }

    /// <summary>The predecessor index's digest, which the successor's new observation names.</summary>
    public string IndexSha256 { get; }

    internal IReadOnlyList<LuxembourgIndexBuilder.ObservationRow> Observations { get; }

    internal IReadOnlyList<LuxembourgIndexBuilder.EventRow> Events { get; }

    internal long LastSeq => Observations[^1].LastSeq;

    /// <summary>
    /// Reads a predecessor index: its bytes must be the digest the caller names (the predecessor's build report), it
    /// must hold a log of this log schema (an index without one, schema 6 and before, refuses
    /// <see cref="LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers"/>), and the log must match its own stamp and be
    /// numbered as observations appending events, or it refuses <see cref="LuxembourgIndexBuildRefusal.PredecessorMismatch"/>.
    /// </summary>
    public static LuxembourgIndexPredecessor? TryRead(
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

            if (!string.Equals(logSchema, LuxembourgIndexBuilder.EventLogSchema, StringComparison.Ordinal))
            {
                refusal = LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers;
                detail = $"The predecessor's event log is {logSchema}; this build carries {LuxembourgIndexBuilder.EventLogSchema} forward.";
                return null;
            }

            var observations = LuxembourgIndexBuilder.ReadLogObservations(connection);
            var events = LuxembourgIndexBuilder.ReadLogEvents(connection);
            if (!string.Equals(LuxembourgIndexBuilder.HashEventLog(observations, events), logDigest, StringComparison.Ordinal))
            {
                refusal = LuxembourgIndexBuildRefusal.PredecessorMismatch;
                detail = "The predecessor's event log does not match its log stamp.";
                return null;
            }

            LuxembourgIndexBuilder.ValidateLogNumbering(observations, events);
            LuxembourgIndexBuilder.ValidateLogHistory(observations, events);
            return new LuxembourgIndexPredecessor(digest, observations, events);
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
/// One observation of the index's event log: the build that observed (its corpus), the index whose log it carried
/// forward (null for the first), the events it appended by sequence number (an empty range: first is last + 1), and
/// when that build ran (<c>yyyy-MM-ddTHH:mm:ssZ</c>), an upper bound on when its corpus was observed and never an
/// observation time.
/// </summary>
public sealed record LuxembourgIndexObservation(
    long Observation,
    string CorpusSha256,
    string? PredecessorIndexSha256,
    long FirstSeq,
    long LastSeq,
    string BuiltAt);

/// <summary>
/// Builds the immutable Luxembourg index from the same proof-complete envelope that builds
/// lex-corpus/6. Callers cannot provide index rows or capability counts.
/// </summary>
public static class LuxembourgIndexBuilder
{
    public const string Schema = "lex-v3-luxembourg-index/8";

    /// <summary>
    /// The event log's own schema, stamped apart from the index's (<c>log_stamp</c>), so a later build can verify a
    /// predecessor's log, which it carries forward unchanged, across a change of the index's other tables.
    /// </summary>
    internal const string EventLogSchema = "lex-v3-event-log/2";

    /// <summary>
    /// The form of an observation's build time: UTC to the second. A build time is captured once per build, after
    /// every fetch, and rounded up to the next second, so it is an upper bound on when the build's corpus was observed.
    /// </summary>
    internal const string BuiltAtFormat = "yyyy-MM-ddTHH:mm:ssZ";
    private const int ApplicationId = 0x4c563306;
    private const string Ddl = """
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

    public static LuxembourgIndexBuildResult? TryBuild(
        Stage3DerivationProfileEnvelope envelope,
        DateTimeOffset builtAt,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail) =>
        TryBuild(envelope, predecessor: null, builtAt, out refusal, out detail);

    /// <summary>
    /// Builds the index, its event log chained to <paramref name="predecessor"/> when one is given: the predecessor's
    /// observations and events carried forward unchanged (an exact prefix), then one observation of this build
    /// appending the comparison events (<c>first_sighting</c>, <c>expression_added</c>, <c>file_replaced</c>, and the
    /// derived <c>interval_closed</c> and <c>validity_revised</c>). A state the log holds and this build does not is
    /// silent (absence is not a withdrawal). With no predecessor the log is a genesis log. The observation records
    /// <paramref name="builtAt"/>, the build's time, which the caller captures once after every fetch and passes to
    /// each build it compares: UTC to the whole second, later than the predecessor's last build and no earlier than the
    /// corpus's EU capture, or the build refuses <see cref="LuxembourgIndexBuildRefusal.BuildTimeInvalid"/>. It is an
    /// upper bound on when the corpus was observed, never an observation time.
    /// </summary>
    public static LuxembourgIndexBuildResult? TryBuild(
        Stage3DerivationProfileEnvelope envelope,
        LuxembourgIndexPredecessor? predecessor,
        DateTimeOffset builtAt,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        refusal = LuxembourgIndexBuildRefusal.None;
        detail = null;

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

        MemberRow[] members;
        ArticleRow[] articles;
        StateRow[] states;
        WorkTitleRow[] workTitles;
        WorkFactRow[] workFacts;
        try
        {
            if (!TryProjectRows(
                    envelope, corpus, out members, out articles, out states, out workTitles, out workFacts,
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

        var relations = ProjectRelations(articles);
        IReadOnlyDictionary<string, string[]> sourceBodies;
        try
        {
            sourceBodies = SourceBodiesOfStates(articles, states, BodiesByObjectRef(corpus.VerifiedSet));
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
            observations = ProjectGenesisObservations(corpus.ArtifactRef.Sha256, events.Length, builtAtText);
        }
        else
        {
            var appended = ProjectChainedEvents(FoldLog(predecessor.Events), states, sourceBodies, predecessor.LastSeq + 1, out var changed);
            if (appended is null)
            {
                refusal = LuxembourgIndexBuildRefusal.PredecessorDerivationDiffers;
                detail = $"The state of {changed} has another digest from the same source bodies than the predecessor's log holds: this build's derivation differs from the predecessor's, so the change is not the publisher's.";
                return null;
            }

            events = [.. predecessor.Events, .. appended];
            observations =
            [
                .. predecessor.Observations,
                new ObservationRow(
                    predecessor.Observations.Count + 1L, corpus.ArtifactRef.Sha256, predecessor.IndexSha256,
                    predecessor.LastSeq + 1, predecessor.LastSeq + appended.Length, builtAtText, null),
            ];
        }

        var logicalRowsSha256 = HashLogicalRows(members, articles, states, workTitles, relations, workFacts, events);
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-{Guid.NewGuid():N}.sqlite");
        try
        {
            BuildDatabase(
                path, corpus.ArtifactRef.Sha256, logicalRowsSha256, members, articles, states, workTitles, relations, workFacts, events, observations);
            var bytes = File.ReadAllBytes(path);
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var indexRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
            var manifest = MeasureCapabilities(digest, articles, workTitles);
            using var manifestStream = new MemoryStream();
            var manifestDigest = V3IndexCapabilityManifestArtifact.Write(manifestStream, manifest);
            var manifestBytes = manifestStream.ToArray();
            var manifestRef = new SourceArtifactRef(
                LexCorpus6Builder.ResourceIdOf(manifestDigest), manifestDigest);
            manifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
                manifestRef, manifestBytes, PublisherId.LuLegilux, digest);
            using var verified = LuxembourgIndexReader.OpenAndVerify(
                indexRef, bytes, corpus.ArtifactRef, manifest);
            verified.VerifyEventLogSources(corpus.VerifiedSet);
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
        out MemberRow[] members,
        out ArticleRow[] articles,
        out StateRow[] states,
        out WorkTitleRow[] workTitles,
        out WorkFactRow[] workFacts,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail)
    {
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
                TokensJson(article.Tokens)));
        }

        articles = projected.OrderBy(static row => row.ArticleIdentitySha256, StringComparer.Ordinal).ToArray();
        var stateSources = envelope.BodyComposition.Envelope.LuxembourgAknArticleInventoryPopulation
            .Outcomes
            .Where(static outcome => outcome.Inventory?.PublisherWorkIri is not null &&
                                     outcome.Inventory.PublisherLegalResourceIri is not null &&
                                     outcome.Inventory.PublisherApplicabilityDate is not null)
            .ToDictionary(
                static outcome => Lex.V3.Contracts.Source.Scope.ScopeManifestCanonicalWriter
                    .ComputeObjectRefSha256(outcome.Input.CorpusRecord.ObjectRef),
                static outcome => new StateSource(
                    outcome.Inventory!.PublisherExpressionIri,
                    outcome.Inventory.PublisherWorkIri!,
                    outcome.Inventory.PublisherLegalResourceIri!,
                    outcome.Inventory.PublisherApplicabilityDate!,
                    outcome.Inventory.RuleProfileSha256),
                StringComparer.Ordinal);
        states = ProjectStates(articles, stateSources);
        var assertions = envelope.BodyComposition.Envelope.Luxembourg.TypedAssertions;
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
        workTitles = assertions
            .Where(static value => value.FactDisposition.Predicate is
                Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate.Title or
                Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate.TitleShort)
            .SelectMany(assertion => articlesBySubject.TryGetValue(
                    assertion.Assertion.SubjectIri, out var matches)
                ? matches.Select(article => new WorkTitleRow(
                    article.PublisherWid ?? article.ExpressionIri,
                    article.ExpressionIri,
                    article.Language,
                    assertion.Assertion.ObjectIriOrLexical,
                    NormalizeTitle(assertion.Assertion.ObjectIriOrLexical),
                    datesBySubject.GetValueOrDefault(assertion.Assertion.SubjectIri) ??
                        datesBySubject.GetValueOrDefault(article.PublisherWid ?? article.ExpressionIri) ??
                        article.ApplicabilityDate,
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
        workFacts = ProjectWorkFacts(assertions);
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
    /// <c>first_sighting</c> per held state, in the states table's order, numbered from 1. The key is the
    /// states table's primary key as a JSON array, which a later build keeps for the same state, so a
    /// comparison can say file_replaced rather than a withdrawal and a new sighting; the detail carries
    /// the state's digest. No observation time is held, so none is written: first_sighting here means
    /// first present in this log, not when the publisher published the text or when it was fetched.
    /// The detail also names the digests of the publisher bodies the state's articles were read from
    /// (<c>source_body_sha256</c>, sorted, each once), so a later build that carries this log forward can tell a
    /// replaced publisher file from an unchanged one by the log alone. A pure function of the states and their
    /// source bodies, so the reader recomputes it and refuses any other rows; the bodies themselves are checked
    /// against the corpus by whoever holds it (<see cref="LuxembourgIndexReader.VerifyEventLogSources"/>).
    /// </summary>
    internal static EventRow[] ProjectGenesisEvents(IReadOnlyList<StateRow> states, IReadOnlyDictionary<string, string[]> sourceBodies) =>
        ProjectChainedEvents(new Dictionary<string, LoggedState>(StringComparer.Ordinal), states, sourceBodies, 1, out _)!;

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
    /// publisher's dates, not asserted by the publisher, and say so.</item>
    /// </list>
    /// A key both hold with the same bodies and another digest is not the publisher's change but this derivation's: null,
    /// naming the state. A genesis log is the events appended to no log.
    /// </summary>
    internal static EventRow[]? ProjectChainedEvents(
        IReadOnlyDictionary<string, LoggedState> fold,
        IReadOnlyList<StateRow> states,
        IReadOnlyDictionary<string, string[]> sourceBodies,
        long firstSeq,
        out string? changedState)
    {
        changedState = null;
        var current = new Dictionary<string, (StateRow Row, string[] Bodies)>(StringComparer.Ordinal);
        foreach (var row in states)
        {
            current[StateKey(row)] = (row, sourceBodies.TryGetValue(row.StateSha256, out var found)
                ? found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                : throw new InvalidDataException($"The state {row.StateSha256} has no source bodies for its event."));
        }

        var endsBefore = IntervalEnds(fold.Values.Select(static held => (held.WorkKey, held.ApplicabilityDate, held.Language)));
        var endsAfter = IntervalEnds(fold.Values.Select(static held => (held.WorkKey, held.ApplicabilityDate, held.Language))
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
                Append(key, newLanguage ? V3EventRegistry.ExpressionAdded : V3EventRegistry.FirstSighting,
                    JsonSerializer.Serialize(new { state_sha256 = stateSha256, source_body_sha256 = bodies }));
                continue;
            }

            if (heldNow && heldBefore)
            {
                if (!held!.SourceBodies.SequenceEqual(bodies, StringComparer.Ordinal))
                {
                    Append(key, V3EventRegistry.FileReplaced, JsonSerializer.Serialize(new
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

            var before = endsBefore[(work, date, language)];
            var after = endsAfter[(work, date, language)];
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

    /// <summary>
    /// Each held date's derived end in its work and language: the next later date held in them, or null for the latest.
    /// The publisher dates states from; it gives no end date, so an end is this derivation's and never the publisher's.
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

    /// <summary>A state's key in the log: the states table's primary key as a JSON array, which a later build keeps for the same state.</summary>
    internal static string StateKey(StateRow row) =>
        JsonSerializer.Serialize(new[] { row.WorkKey, row.ApplicabilityDate, row.ExpressionIri, row.Language });

    /// <summary>What a log says one state key holds after all its events: the key's parts, and the digest and source bodies of its last event.</summary>
    internal sealed record LoggedState(
        string WorkKey,
        string ApplicabilityDate,
        string ExpressionIri,
        string Language,
        string StateSha256,
        IReadOnlyList<string> SourceBodies);

    /// <summary>
    /// What a log says each state key holds, its events read in order (a later event of a key replaces an earlier one).
    /// An event whose key or detail is not the shape this log schema writes refuses.
    /// </summary>
    internal static IReadOnlyDictionary<string, LoggedState> FoldLog(IReadOnlyList<EventRow> events)
    {
        var fold = new Dictionary<string, LoggedState>(StringComparer.Ordinal);
        foreach (var value in events)
        {
            try
            {
                var key = JsonSerializer.Deserialize<string[]>(value.Key);
                if (key is not { Length: 4 } || key.Any(static part => string.IsNullOrEmpty(part)))
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
                fold[value.Key] = new LoggedState(key[0], key[1], key[2], key[3], state, bodies);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new InvalidDataException("An event of the log is not a state event this log schema writes.", exception);
            }
        }

        return fold;
    }

    internal static ObservationRow[] ReadLogObservations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT observation,corpus_sha256,predecessor_index_sha256,first_seq,last_seq,built_at,observed_from FROM observations ORDER BY observation";
        using var reader = command.ExecuteReader();
        var values = new List<ObservationRow>();
        while (reader.Read()) values.Add(new(
            reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
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
        IReadOnlyList<EventRow> events)
    {
        ValidateLogNumbering(observations, events);
        var last = observations[^1];
        if (!string.Equals(last.CorpusSha256, corpusSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Luxembourg event log's last observation is not of this index's corpus.");
        }

        try
        {
            ValidateLogHistory(observations, events);
            var held = FoldLog(events);
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
            var expected = ProjectChainedEvents(FoldLog(before), states, sourceBodies, last.FirstSeq, out _);
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
    internal static void ValidateLogHistory(IReadOnlyList<ObservationRow> observations, IReadOnlyList<EventRow> events)
    {
        foreach (var observation in observations)
        {
            var before = events.Take(checked((int)(observation.FirstSeq - 1))).ToArray();
            var appended = events.Skip(before.Length).Take(checked((int)(observation.LastSeq - observation.FirstSeq + 1))).ToArray();
            var after = FoldLog([.. before, .. appended]);
            var states = after.Values
                .Select(static held => new StateRow(
                    held.WorkKey, held.ApplicabilityDate, held.StateSha256, held.ExpressionIri, string.Empty, string.Empty, held.Language, "[]", "[]"))
                .ToArray();
            var bodies = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var held in after.Values)
            {
                bodies[held.StateSha256] = held.SourceBodies.ToArray();
            }

            var expected = ProjectChainedEvents(FoldLog(before), states, bodies, observation.FirstSeq, out _);
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
    internal static ObservationRow[] ProjectGenesisObservations(string corpusSha256, int eventCount, string builtAt) =>
        [new ObservationRow(1, corpusSha256, null, 1, eventCount, builtAt, null)];

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

        if (DateTimeOffset.TryParse(
                corpus.Set.EuropeRightsMatrix.CapturedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var captured) &&
            builtAt < captured)
        {
            detail = $"The build time {text} is earlier than the corpus's EU capture at {corpus.Set.EuropeRightsMatrix.CapturedAt}.";
            return null;
        }

        detail = null;
        return text;
    }

    /// <summary>The digest of the event log alone (its observations and events), as <c>log_stamp</c> records it.</summary>
    internal static string HashEventLog(IReadOnlyList<ObservationRow> observations, IReadOnlyList<EventRow> events) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new EventLogRows(observations, events))));

    /// <summary>The body digest of every Luxembourg member that holds one, by the member's object reference.</summary>
    internal static IReadOnlyDictionary<string, string> BodiesByObjectRef(VerifiedLexCorpus6ManifestSet corpus) =>
        corpus.Set.Members
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
    internal static string TokensJson(IReadOnlyList<LuxembourgAknLegalContentToken> tokens) =>
        JsonSerializer.Serialize(tokens.Select(static token => new
        {
            kind = ContractWire.NameOf(token.Kind),
            text = token.Text,
            target = token.Target,
            marker = token.Marker,
            note_body = token.NoteBody?.Select(static nested => new
            {
                kind = ContractWire.NameOf(nested.Kind),
                text = nested.Text,
                target = nested.Target,
                marker = nested.Marker,
            }),
        }));

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

    private static StateRow[] ProjectStates(
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyDictionary<string, StateSource> sources)
    {
        var rows = new List<StateRow>();
        foreach (var group in articles.GroupBy(static article => article.ObjectRefSha256,
                     StringComparer.Ordinal))
        {
            if (!sources.TryGetValue(group.Key, out var source))
            {
                continue;
            }
            var expressions = group.Select(static article => article.ExpressionIri)
                .Distinct(StringComparer.Ordinal).ToArray();
            var languages = group.Select(static article => article.Language)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (expressions.Length != 1 || languages.Length != 1 ||
                !string.Equals(expressions[0], source.ExpressionIri, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A Luxembourg expression-state source does not match its admitted articles.");
            }

            var language = languages[0];
            var workKey = WorkKeyOf(source.PublisherWorkIri);
            var profiles = group.Select(static article => article.RuleProfileSha256)
                .Append(source.RuleProfileSha256)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var identities = group.Select(static article => article.ArticleIdentitySha256)
                .Order(StringComparer.Ordinal).ToArray();
            var digest = StateSha256(
                workKey,
                source.PublisherApplicabilityDate,
                source.ExpressionIri,
                source.PublisherWorkIri,
                source.PublisherLegalResourceIri,
                language,
                profiles,
                identities);
            rows.Add(new StateRow(
                workKey,
                source.PublisherApplicabilityDate,
                digest,
                source.ExpressionIri,
                source.PublisherWorkIri,
                source.PublisherLegalResourceIri,
                language,
                JsonSerializer.Serialize(profiles),
                JsonSerializer.Serialize(identities)));
        }

        return rows.OrderBy(static row => row.WorkKey, StringComparer.Ordinal)
            .ThenBy(static row => row.ApplicabilityDate, StringComparer.Ordinal)
            .ThenBy(static row => row.ExpressionIri, StringComparer.Ordinal)
            .ThenBy(static row => row.Language, StringComparer.Ordinal)
            .ToArray();
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
        Execute(connection, "PRAGMA user_version=8");
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
                "INSERT INTO articles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9)",
                article.ArticleIdentitySha256, article.ObjectRefSha256, article.ExpressionIri,
                article.PublisherId, article.PublisherWid, article.ApplicabilityDate,
                article.Language, article.RuleProfileSha256, article.SearchableText, article.TokensJson);
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
                "INSERT INTO states VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8)",
                state.WorkKey, state.ApplicabilityDate, state.StateSha256, state.ExpressionIri,
                state.PublisherWorkIri, state.PublisherLegalResourceIri, state.Language, state.RuleProfilesJson,
                state.ArticleIdentitiesJson);
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
                "INSERT INTO observations VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6)",
                observation.Observation, observation.CorpusSha256, observation.PredecessorIndexSha256,
                observation.FirstSeq, observation.LastSeq, observation.BuiltAt, observation.ObservedFrom);
        }
        Insert(connection, transaction, "INSERT INTO log_stamp VALUES(1,$p0,$p1)", EventLogSchema, HashEventLog(observations, events));
        var provenance = SqliteProvenance.Read(connection);
        Insert(connection, transaction, "INSERT INTO stamp VALUES(1,$p0,$p1,$p2,$p3,$p4,$p5)",
            Schema, corpusSha256, logicalRowsSha256, provenance.Version,
            provenance.SourceId, provenance.CompileOptionsSha256);
        transaction.Commit();
        Execute(connection, "PRAGMA optimize");
    }

    internal static byte[] BuildFixedInputDeterminismEvidence()
    {
        var member = new MemberRow(
            new string('1', 64), 0, "acquired", "agreed_same_run_cc_by", "[]", "[]");
        var article = new ArticleRow(
            new string('2', 64), member.ObjectRefSha256,
            "https://example.invalid/expression", "art_1",
            "http://data.legilux.public.lu/eli/etat/leg/loi/2024/01/01/n1", "2024-01-01",
            "fra", new string('4', 64), "libellé fixe",
            TokensJson(
            [
                new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Text, "libellé fixe", null, null),
                new LuxembourgAknLegalContentToken(
                    LuxembourgAknLegalContentTokenKind.Reference, "loi fixe", "/eli/etat/leg/loi/2023/01/01/n1/jo", null),
                new LuxembourgAknLegalContentToken(
                    LuxembourgAknLegalContentTokenKind.NoteReference, null, null, "1",
                    [new LuxembourgAknLegalContentToken(LuxembourgAknLegalContentTokenKind.Reference, "note fixe", "???", null)]),
            ]));
        var members = new[] { member };
        var articles = new[] { article };
        var states = ProjectStates(articles, new Dictionary<string, StateSource>(StringComparer.Ordinal)
        {
            [member.ObjectRefSha256] = new(
                article.ExpressionIri,
                article.PublisherWid!,
                article.PublisherWid! + "/jo",
                "2024-01-01",
                new string('5', 64)),
        });
        var titles = new[] { new WorkTitleRow(
            article.PublisherWid!, article.ExpressionIri, "fra", "Titre fixe", "titre fixe",
            "2024-01-01", "title", new string('3', 64)) };
        var facts = new[] { new WorkFactRow(
            article.PublisherWid!,
            ContractWire.NameOf(Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate.TypeDocument),
            ContractWire.NameOf(Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionFactKind.ResourceType),
            "iri", "http://data.legilux.public.lu/resource/authority/legal-type/LOI", "", "", new string('6', 64)) };
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-pin-{Guid.NewGuid():N}.sqlite");
        try
        {
            var relations = ProjectRelations(articles);
            var events = ProjectGenesisEvents(states, SourceBodiesOfStates(articles, states, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [member.ObjectRefSha256] = new string('7', 64),
            }));
            BuildDatabase(path, new string('a', 64), HashLogicalRows(members, articles, states, titles, relations, facts, events),
                members, articles, states, titles, relations, facts, events,
                ProjectGenesisObservations(new string('a', 64), events.Length, "2026-01-01T00:00:00Z"));
            return File.ReadAllBytes(path);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    internal static V3IndexCapabilityManifest MeasureCapabilities(
        string digest,
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<WorkTitleRow>? workTitles = null)
    {
        var cells = articles
            .Where(static row => row.ApplicabilityDate is not null && row.SearchableText.Length != 0)
            .GroupBy(static row => (row.Language, row.ApplicabilityDate))
            .Select(group => new V3IndexCapabilityCell(
                PublisherId.LuLegilux,
                digest,
                "search",
                "articles",
                "searchable_text",
                group.Key.Language,
                DateOnly.ParseExact(group.Key.ApplicabilityDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(group.Key.ApplicabilityDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                group.LongCount()))
            .Concat((workTitles ?? Array.Empty<WorkTitleRow>())
                .Where(static row => row.DocumentDate is not null)
                .GroupBy(static row => (row.Language, row.DocumentDate))
                .Select(group => new V3IndexCapabilityCell(
                    PublisherId.LuLegilux,
                    digest,
                    "resolve",
                    "work_titles",
                    "normalized_title",
                    group.Key.Language,
                    DateOnly.ParseExact(group.Key.DocumentDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateOnly.ParseExact(group.Key.DocumentDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    group.LongCount())))
            .ToArray();
        if (!V3IndexCapabilityManifest.TryCreate(
                PublisherId.LuLegilux, digest, cells, out var manifest, out var refusal))
        {
            throw new InvalidDataException($"Measured Luxembourg capabilities are invalid: {refusal}.");
        }
        return manifest!;
    }

    internal static string HashLogicalRows(
        IReadOnlyList<MemberRow> members,
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<StateRow> states,
        IReadOnlyList<WorkTitleRow> workTitles,
        IReadOnlyList<RelationRow> relations,
        IReadOnlyList<WorkFactRow> workFacts,
        IReadOnlyList<EventRow> events)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new LogicalRows(members, articles, states, workTitles, relations, workFacts, events));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
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

    internal static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static void EnsureExactSchema(SqliteConnection actual)
    {
        using var expected = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        expected.Open();
        Execute(expected, Ddl);
        if (!ReadSchema(expected).SequenceEqual(ReadSchema(actual), StringComparer.Ordinal))
        {
            throw new InvalidDataException("The Luxembourg index schema differs from the exact terminal schema.");
        }
    }

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
        string TokensJson);

    internal sealed record StateRow(
        string WorkKey,
        string ApplicabilityDate,
        string StateSha256,
        string ExpressionIri,
        string PublisherWorkIri,
        string PublisherLegalResourceIri,
        string Language,
        string RuleProfilesJson,
        string ArticleIdentitiesJson);

    private sealed record StateSource(
        string ExpressionIri,
        string PublisherWorkIri,
        string PublisherLegalResourceIri,
        string PublisherApplicabilityDate,
        string RuleProfileSha256);

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
        string? ObservedFrom);

    private sealed record EventLogRows(IReadOnlyList<ObservationRow> Observations, IReadOnlyList<EventRow> Events);

    private sealed record LogicalRows(
        IReadOnlyList<MemberRow> Members,
        IReadOnlyList<ArticleRow> Articles,
        IReadOnlyList<StateRow> States,
        IReadOnlyList<WorkTitleRow> WorkTitles,
        IReadOnlyList<RelationRow> Relations,
        IReadOnlyList<WorkFactRow> WorkFacts,
        IReadOnlyList<EventRow> Events);

    internal static string NormalizeTitle(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length != 0)
                {
                    builder.Append(' ');
                }
                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

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

    private LuxembourgIndexReader(
        string path,
        SqliteConnection connection,
        V3IndexCapabilityManifest capabilityManifest,
        SourceArtifactRef indexRef,
        SourceArtifactRef corpusRef,
        bool deleteOnDispose)
    {
        _path = path;
        _connection = connection;
        _capabilityManifest = capabilityManifest;
        IndexRef = indexRef;
        CorpusRef = corpusRef;
        _deleteOnDispose = deleteOnDispose;
    }

    public SourceArtifactRef IndexRef { get; }

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
            LuxembourgIndexBuilder.EnsureExactSchema(connection);
            if (!string.Equals(Scalar(connection, "PRAGMA integrity_check"), "ok", StringComparison.Ordinal) ||
                Convert.ToInt32(Scalar(connection, "PRAGMA application_id"), CultureInfo.InvariantCulture) != 0x4c563306 ||
                Convert.ToInt32(Scalar(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture) != 8)
            {
                throw new InvalidDataException("The Luxembourg index failed SQLite integrity or schema identity checks.");
            }

            using var stamp = connection.CreateCommand();
            stamp.CommandText = "SELECT schema_identity,corpus_sha256,logical_rows_sha256,sqlite_version,sqlite_source_id,compile_options_sha256 FROM stamp WHERE stamp_id=1";
            using var stampReader = stamp.ExecuteReader();
            if (!stampReader.Read() ||
                !string.Equals(stampReader.GetString(0), LuxembourgIndexBuilder.Schema, StringComparison.Ordinal))
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

            var members = ReadMembers(connection);
            var articles = ReadArticles(connection);
            var states = ReadStates(connection);
            var workTitles = ReadWorkTitles(connection);
            var relations = ReadRelations(connection);
            var workFacts = ReadWorkFacts(connection);
            var events = LuxembourgIndexBuilder.ReadLogEvents(connection);
            var observations = LuxembourgIndexBuilder.ReadLogObservations(connection);
            ValidateStates(articles, states);
            if (!LuxembourgIndexBuilder.ProjectRelations(articles).SequenceEqual(relations))
                throw new InvalidDataException(
                    "The Luxembourg index relation rows are not the references its articles carry.");
            LuxembourgIndexBuilder.ValidateLog(corpusSha256, states, observations, events);
            VerifyLogStamp(connection, observations, events);
            if (!string.Equals(
                    LuxembourgIndexBuilder.HashLogicalRows(members, articles, states, workTitles, relations, workFacts, events),
                    expectedLogical,
                    StringComparison.Ordinal))
                throw new InvalidDataException("The Luxembourg index logical rows do not match their stamp.");

            var measured = LuxembourgIndexBuilder.MeasureCapabilities(indexRef.Sha256, articles, workTitles);
            if (!measured.Cells.SequenceEqual(capabilityManifest.Cells))
                throw new InvalidDataException("The Luxembourg capability manifest was not measured from the index.");

            return new LuxembourgIndexReader(
                path,
                connection,
                capabilityManifest,
                indexRef,
                corpusRef,
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
            command.CommandText = """
                SELECT work_key,applicability_date,state_sha256,expression_iri,publisher_work_iri,
                       publisher_legal_resource_iri,language,rule_profiles_json,article_identities_json
                FROM states
                WHERE work_key=$work AND applicability_date=$date
                ORDER BY expression_iri,language,state_sha256
                """;
            command.Parameters.AddWithValue("$work", workKey);
            command.Parameters.AddWithValue("$date", applicabilityDate);
            return ReadResolvedStates(command);
        }
    }

    /// <summary>
    /// Every publisher-dated state of one work, in date order, then language, expression and digest.
    /// The work is named either by its product work key or by the publisher's own work IRI as
    /// stored; nothing is normalised. Temporal operations select over this list; the reader asserts
    /// nothing about an end date, which the publisher does not state.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexResolvedState> ResolveWorkStates(string workIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workIdentifier);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT work_key,applicability_date,state_sha256,expression_iri,publisher_work_iri,
                       publisher_legal_resource_iri,language,rule_profiles_json,article_identities_json
                FROM states
                WHERE work_key=$identifier OR publisher_work_iri=$identifier
                ORDER BY applicability_date,language,expression_iri,state_sha256
                """;
            command.Parameters.AddWithValue("$identifier", workIdentifier);
            return ReadResolvedStates(command);
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
            command.CommandText = """
                SELECT work_key,applicability_date,state_sha256,expression_iri,publisher_work_iri,
                       publisher_legal_resource_iri,language,rule_profiles_json,article_identities_json
                FROM states
                WHERE applicability_date>=$from AND applicability_date<=$to
                ORDER BY applicability_date,work_key,language,expression_iri,state_sha256
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
                "SELECT DISTINCT work_key FROM states WHERE applicability_date<=$date" +
                (language is null ? string.Empty : " AND language=$language") +
                (afterWorkKey is null ? string.Empty : " AND work_key>$after") +
                " ORDER BY work_key LIMIT $take";
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
            using var connection = LuxembourgIndexBuilder.Open(_path, SqliteOpenMode.ReadOnly);
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
                "SELECT COUNT(DISTINCT work_key) FROM states WHERE applicability_date<=$date" +
                (language is null ? string.Empty : " AND language=$language");
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
                    ? "SELECT COUNT(DISTINCT work_key),MIN(applicability_date),MAX(applicability_date) FROM states"
                    : "SELECT COUNT(DISTINCT work_key),MIN(applicability_date),MAX(applicability_date) FROM states WHERE language=$language";
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
                command.CommandText = "SELECT DISTINCT language FROM states ORDER BY language";
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
    /// operation on the mount wait behind the counts.
    /// </summary>
    public LuxembourgIndexCoverage ResolveCoverage()
    {
        {
            using var connection = LuxembourgIndexBuilder.Open(_path, SqliteOpenMode.ReadOnly);
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
            var works = Counted("SELECT COUNT(DISTINCT work_key) FROM states");
            var states = Counted("SELECT COUNT(*) FROM states");
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
                    "SELECT language,COUNT(DISTINCT work_key),COUNT(*)," +
                    "MIN(applicability_date),MAX(applicability_date) FROM states GROUP BY language ORDER BY language";
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
    /// article and are left out before merging.
    /// </summary>
    internal static string WordingSha256(string tokensJson)
    {
        using var stream = JsonDocument.Parse(tokensJson);
        var words = new List<string?[]>();
        var pendingText = new StringBuilder();
        var hasPendingText = false;
        void FlushText()
        {
            if (hasPendingText)
            {
                words.Add(["text", pendingText.ToString(), null]);
                pendingText.Clear();
                hasPendingText = false;
            }
        }

        foreach (var token in stream.RootElement.EnumerateArray())
        {
            var kind = token.GetProperty("kind").GetString();
            var text = token.TryGetProperty("text", out var textValue) && textValue.ValueKind == JsonValueKind.String ? textValue.GetString() : null;
            if (kind == "text")
            {
                pendingText.Append(text);
                hasPendingText = true;
                continue;
            }

            if (kind != "reference")
            {
                continue;
            }

            FlushText();
            words.Add(
            [
                kind,
                text,
                token.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String ? target.GetString() : null,
            ]);
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
            command.CommandText = LuxembourgIndexQueries.HeldWorks;
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
            command.CommandText = LuxembourgIndexQueries.StatesOfExpressions;
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

    /// <summary>The observations of the event log, in order (<see cref="LuxembourgIndexQueries.Observations"/>).</summary>
    public IReadOnlyList<LuxembourgIndexObservation> ResolveObservations()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.Observations;
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexObservation>();
            while (reader.Read())
            {
                values.Add(new LuxembourgIndexObservation(
                    reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5)));
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
            command.CommandText = LuxembourgIndexQueries.WorkRecords;
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

    private static LuxembourgIndexBuilder.MemberRow[] ReadMembers(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT object_ref_sha256,source_ordinal,outcome,rights_disposition,stage3_outcomes_json,gaps_json FROM members ORDER BY object_ref_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.MemberRow>();
        while (reader.Read()) values.Add(new(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return values.ToArray();
    }

    private static LuxembourgIndexBuilder.ArticleRow[] ReadArticles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,expression_iri,publisher_id,publisher_wid,applicability_date,language,rule_profile_sha256,searchable_text,tokens_json FROM articles ORDER BY article_identity_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.ArticleRow>();
        while (reader.Read()) values.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9)));
        return values.ToArray();
    }

    private static LuxembourgIndexBuilder.StateRow[] ReadStates(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_key,applicability_date,state_sha256,expression_iri,publisher_work_iri,publisher_legal_resource_iri,language,rule_profiles_json,article_identities_json FROM states ORDER BY work_key,applicability_date,expression_iri,language";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.StateRow>();
        while (reader.Read()) values.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8)));
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
        IReadOnlyList<LuxembourgIndexBuilder.EventRow> events)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT log_schema,log_rows_sha256 FROM log_stamp WHERE log_stamp_id=1";
        using var reader = command.ExecuteReader();
        if (!reader.Read() ||
            !string.Equals(reader.GetString(0), LuxembourgIndexBuilder.EventLogSchema, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), LuxembourgIndexBuilder.HashEventLog(observations, events), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Luxembourg event log does not match its log stamp.");
        }
    }

    /// <summary>
    /// Checks the source bodies the event log names against the corpus this index binds: every state's events name
    /// exactly the body digests of the members its articles were read from. The index alone holds no body digest, so
    /// whoever mounts it with its corpus calls this (the mount, the mount writer's verification, the build).
    /// </summary>
    public void VerifyEventLogSources(VerifiedLexCorpus6ManifestSet corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (corpus.ArtifactRef != CorpusRef)
        {
            throw new InvalidDataException("The corpus is not the one this Luxembourg index binds.");
        }

        lock (_gate)
        {
            var states = ReadStates(_connection);
            var expected = LuxembourgIndexBuilder.SourceBodiesOfStates(
                ReadArticles(_connection), states, LuxembourgIndexBuilder.BodiesByObjectRef(corpus));
            var held = LuxembourgIndexBuilder.FoldLog(LuxembourgIndexBuilder.ReadLogEvents(_connection));
            foreach (var state in states)
            {
                if (!held.TryGetValue(LuxembourgIndexBuilder.StateKey(state), out var logged) ||
                    !logged.SourceBodies.SequenceEqual(expected[state.StateSha256], StringComparer.Ordinal))
                {
                    throw new InvalidDataException("The Luxembourg event log names source bodies the corpus does not hold for its states.");
                }
            }
        }
    }

    private static LuxembourgIndexBuilder.WorkFactRow[] ReadWorkFacts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT subject_iri,predicate,fact_kind,object_kind,object_value,datatype_iri,language_tag,evidence_sha256 FROM work_facts ORDER BY subject_iri,predicate,object_kind,object_value,datatype_iri,language_tag,evidence_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.WorkFactRow>();
        while (reader.Read()) values.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        return values.ToArray();
    }

}
