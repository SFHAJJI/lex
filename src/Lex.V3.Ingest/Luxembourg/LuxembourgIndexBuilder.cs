using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
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
}

public sealed record LuxembourgIndexBuildResult(
    SourceArtifactRef IndexRef,
    ReadOnlyMemory<byte> IndexBytes,
    SourceArtifactRef CapabilityManifestRef,
    ReadOnlyMemory<byte> CapabilityManifestBytes,
    V3IndexCapabilityManifest CapabilityManifest);

public sealed record LuxembourgIndexSearchResult(
    V3IndexCapabilityLookupOutcome Outcome,
    IReadOnlyList<string> ArticleIdentities);

public sealed record LuxembourgIndexResolvedExpression(
    string ExpressionIri,
    string? PublisherWid,
    string Language,
    IReadOnlyList<string> ArticleIdentities);

public sealed record LuxembourgIndexResolvedWork(
    string WorkIdentifier,
    IReadOnlyList<string> ExpressionIris,
    IReadOnlyList<string> Languages,
    string MatchedTitle,
    string MatchReason);

public sealed record LuxembourgIndexWorkResolution(
    bool Available,
    IReadOnlyList<LuxembourgIndexResolvedWork> Candidates);

/// <summary>
/// Builds the immutable Luxembourg index from the same proof-complete envelope that builds
/// lex-corpus/6. Callers cannot provide index rows or capability counts.
/// </summary>
public static class LuxembourgIndexBuilder
{
    public const string Schema = "lex-v3-luxembourg-index/2";
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
          searchable_text TEXT COLLATE BINARY NOT NULL,
          tokens_json TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE INDEX articles_object_ref ON articles(object_ref_sha256);
        CREATE INDEX articles_language_date ON articles(language, applicability_date);
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
        """;

    public static LuxembourgIndexBuildResult? TryBuild(
        Stage3DerivationProfileEnvelope envelope,
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

        MemberRow[] members;
        ArticleRow[] articles;
        WorkTitleRow[] workTitles;
        try
        {
            if (!TryProjectRows(
                    envelope, corpus, out members, out articles, out workTitles,
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

        var logicalRowsSha256 = HashLogicalRows(members, articles, workTitles);
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-{Guid.NewGuid():N}.sqlite");
        try
        {
            BuildDatabase(
                path, corpus.ArtifactRef.Sha256, logicalRowsSha256, members, articles, workTitles);
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
        out WorkTitleRow[] workTitles,
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
            workTitles = [];
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
                workTitles = [];
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
                workTitles = [];
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
                workTitles = [];
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
                string.Concat(article.Tokens
                    .Where(static token => token.Kind is
                        LuxembourgAknLegalContentTokenKind.Text or
                        LuxembourgAknLegalContentTokenKind.Reference)
                    .Select(static token => token.Text)),
                JsonSerializer.Serialize(article.Tokens.Select(static token => new
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
                }))));
        }

        articles = projected.OrderBy(static row => row.ArticleIdentitySha256, StringComparer.Ordinal).ToArray();
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
        refusal = LuxembourgIndexBuildRefusal.None;
        detail = null;
        return true;
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
        IReadOnlyList<WorkTitleRow> workTitles)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWriteCreate);
        Execute(connection, "PRAGMA page_size=4096");
        Execute(connection, "PRAGMA encoding='UTF-8'");
        Execute(connection, "PRAGMA auto_vacuum=NONE");
        Execute(connection, "PRAGMA journal_mode=DELETE");
        Execute(connection, "PRAGMA synchronous=FULL");
        Execute(connection, "PRAGMA foreign_keys=ON");
        Execute(connection, $"PRAGMA application_id={ApplicationId}");
        Execute(connection, "PRAGMA user_version=2");
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
                "INSERT INTO articles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8)",
                article.ArticleIdentitySha256, article.ObjectRefSha256, article.ExpressionIri,
                article.PublisherId, article.PublisherWid, article.ApplicabilityDate,
                article.Language, article.SearchableText, article.TokensJson);
        }
        foreach (var title in workTitles)
        {
            Insert(connection, transaction,
                "INSERT INTO work_titles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7)",
                title.WorkIdentifier, title.ExpressionIri, title.Language, title.Title,
                title.NormalizedTitle, title.DocumentDate, title.TitleKind, title.EvidenceSha256);
        }
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
            "https://example.invalid/expression", "art_1", "wid-1", "2024-01-01",
            "fra", "libellé fixe", "[]");
        var members = new[] { member };
        var articles = new[] { article };
        var titles = new[] { new WorkTitleRow(
            "wid-1", article.ExpressionIri, "fra", "Titre fixe", "titre fixe",
            "2024-01-01", "title", new string('3', 64)) };
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-pin-{Guid.NewGuid():N}.sqlite");
        try
        {
            BuildDatabase(path, new string('a', 64), HashLogicalRows(members, articles, titles),
                members, articles, titles);
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
        IReadOnlyList<WorkTitleRow>? workTitles = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new LogicalRows(members, articles, workTitles ?? Array.Empty<WorkTitleRow>()));
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
        string SearchableText,
        string TokensJson);

    internal sealed record WorkTitleRow(
        string WorkIdentifier,
        string ExpressionIri,
        string Language,
        string Title,
        string NormalizedTitle,
        string? DocumentDate,
        string TitleKind,
        string EvidenceSha256);

    private sealed record LogicalRows(
        IReadOnlyList<MemberRow> Members,
        IReadOnlyList<ArticleRow> Articles,
        IReadOnlyList<WorkTitleRow> WorkTitles);

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
                Convert.ToInt32(Scalar(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture) != 2)
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
            var workTitles = ReadWorkTitles(connection);
            if (!string.Equals(
                    LuxembourgIndexBuilder.HashLogicalRows(members, articles, workTitles),
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

    public LuxembourgIndexWorkResolution ResolveWorkTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var normalized = LuxembourgIndexBuilder.NormalizeTitle(title);
        if (normalized.Length == 0)
        {
            return new LuxembourgIndexWorkResolution(false, Array.Empty<LuxembourgIndexResolvedWork>());
        }

        lock (_gate)
        {
            var rows = ReadWorkTitles(_connection);
            if (rows.Length == 0)
            {
                return new LuxembourgIndexWorkResolution(false, Array.Empty<LuxembourgIndexResolvedWork>());
            }

            var exact = rows.Where(row => string.Equals(
                row.NormalizedTitle, normalized, StringComparison.Ordinal)).ToArray();
            var reason = "exact_normalized_title";
            var selected = exact;
            if (selected.Length == 0)
            {
                selected = rows.Where(row => row.NormalizedTitle.StartsWith(
                    normalized, StringComparison.Ordinal)).ToArray();
                reason = "unique_prefix";
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
                .GroupBy(static row => row.WorkIdentifier, StringComparer.Ordinal)
                .OrderByDescending(static group => group.Max(row => row.DocumentDate), StringComparer.Ordinal)
                .ThenBy(static group => group.Key, StringComparer.Ordinal)
                .Select(group => new LuxembourgIndexResolvedWork(
                    group.Key,
                    Array.AsReadOnly(group.Select(static row => row.ExpressionIri)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                    Array.AsReadOnly(group.Select(static row => row.Language)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                    group.OrderBy(static row => row.Title, StringComparer.Ordinal).First().Title,
                    reason))
                .ToArray();
            return new LuxembourgIndexWorkResolution(true, Array.AsReadOnly(candidates));
        }
    }

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
        command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,expression_iri,publisher_id,publisher_wid,applicability_date,language,searchable_text,tokens_json FROM articles ORDER BY article_identity_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.ArticleRow>();
        while (reader.Read()) values.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetString(7), reader.GetString(8)));
        return values.ToArray();
    }

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

}
