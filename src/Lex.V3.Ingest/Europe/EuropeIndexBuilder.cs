using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Europe;

public enum EuropeIndexBuildRefusal
{
    [JsonStringEnumMemberName("none")] None = 0,
    [JsonStringEnumMemberName("corpus_refused")] CorpusRefused = 1,
    [JsonStringEnumMemberName("population_mismatch")] PopulationMismatch = 2,
    [JsonStringEnumMemberName("derivation_mismatch")] DerivationMismatch = 3,
    [JsonStringEnumMemberName("rights_ineligible")] RightsIneligible = 4,
    [JsonStringEnumMemberName("index_invalid")] IndexInvalid = 5,
}

public sealed record EuropeIndexBuildResult(
    SourceArtifactRef IndexRef,
    ReadOnlyMemory<byte> IndexBytes,
    SourceArtifactRef CapabilityManifestRef,
    ReadOnlyMemory<byte> CapabilityManifestBytes,
    V3IndexCapabilityManifest CapabilityManifest);

public sealed record EuropeIndexSearchResult(
    V3IndexCapabilityLookupOutcome Outcome,
    IReadOnlyList<string> ArticleIdentities);

public sealed record EuropeIndexResolvedExpression(
    string PublisherWorkId,
    string PublisherExpressionId,
    string Language,
    IReadOnlyList<string> PublisherProvisionIdentifiers,
    IReadOnlyList<string> ArticleIdentities);

/// <summary>Builds the immutable EU index from one proof-complete Stage 3 envelope.</summary>
public static class EuropeIndexBuilder
{
    public const string Schema = "lex-v3-europe-index/1";
    private const int ApplicationId = 0x4c563307;
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
          content_class TEXT COLLATE BINARY,
          stage3_outcomes_json TEXT COLLATE BINARY NOT NULL,
          gaps_json TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE TABLE corrigendum_lines (
          line_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(line_identity_sha256) = 64),
          family_key TEXT COLLATE BINARY NOT NULL,
          corrected_work_root TEXT COLLATE BINARY NOT NULL,
          corrigendum_work_root TEXT COLLATE BINARY NOT NULL,
          publisher_expression_id TEXT COLLATE BINARY NOT NULL,
          language_iri TEXT COLLATE BINARY NOT NULL,
          reach TEXT COLLATE BINARY NOT NULL,
          date_state TEXT COLLATE BINARY NOT NULL,
          publisher_date_raw_lexical TEXT COLLATE BINARY,
          publisher_date_datatype_iri TEXT COLLATE BINARY,
          expression_content_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(expression_content_sha256) = 64)
        ) STRICT;
        CREATE TABLE corrigendum_gaps (
          gap_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(gap_identity_sha256) = 64),
          family_key TEXT COLLATE BINARY NOT NULL,
          work_root TEXT COLLATE BINARY NOT NULL,
          reason TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE TABLE articles (
          article_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(article_identity_sha256) = 64),
          object_ref_sha256 TEXT COLLATE BINARY NOT NULL REFERENCES members(object_ref_sha256),
          publisher_work_id TEXT COLLATE BINARY NOT NULL,
          publisher_expression_id TEXT COLLATE BINARY NOT NULL,
          package_entry TEXT COLLATE BINARY NOT NULL,
          publisher_identifier TEXT COLLATE BINARY NOT NULL,
          heading TEXT COLLATE BINARY NOT NULL,
          wording_date TEXT COLLATE BINARY NOT NULL,
          language TEXT COLLATE BINARY NOT NULL,
          searchable_text TEXT COLLATE BINARY NOT NULL,
          tokens_json TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE INDEX articles_object_ref ON articles(object_ref_sha256);
        CREATE INDEX articles_language_date ON articles(language, wording_date);
        """;

    public static EuropeIndexBuildResult? TryBuild(
        Stage3DerivationProfileEnvelope envelope,
        out EuropeIndexBuildRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        refusal = EuropeIndexBuildRefusal.None;
        detail = null;
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        if (corpus is null)
        {
            refusal = EuropeIndexBuildRefusal.CorpusRefused;
            detail = $"{corpusRefusal}: {corpusDetail}";
            return null;
        }

        try
        {
            if (!TryProjectRows(envelope, corpus, out var members, out var lines, out var gaps,
                    out var articles, out refusal, out detail))
                return null;
            var logicalRowsSha256 = HashLogicalRows(members, lines, gaps, articles);
            var path = Path.Combine(Path.GetTempPath(), $"lex-v3-eu-index-{Guid.NewGuid():N}.sqlite");
            try
            {
                BuildDatabase(path, corpus.ArtifactRef.Sha256, logicalRowsSha256,
                    members, lines, gaps, articles);
                var bytes = File.ReadAllBytes(path);
                var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
                var indexRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
                var manifest = MeasureCapabilities(digest, articles);
                using var manifestStream = new MemoryStream();
                var manifestDigest = V3IndexCapabilityManifestArtifact.Write(manifestStream, manifest);
                var manifestBytes = manifestStream.ToArray();
                var manifestRef = new SourceArtifactRef(
                    LexCorpus6Builder.ResourceIdOf(manifestDigest), manifestDigest);
                manifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
                    manifestRef, manifestBytes, PublisherId.EuEurLex, digest);
                using var verified = EuropeIndexReader.OpenAndVerify(
                    indexRef, bytes, corpus.ArtifactRef, manifest);
                return new EuropeIndexBuildResult(indexRef, bytes, manifestRef, manifestBytes, manifest);
            }
            finally
            {
                DeleteDatabase(path);
            }
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException)
        {
            refusal = EuropeIndexBuildRefusal.IndexInvalid;
            detail = exception.Message;
            return null;
        }
    }

    private static bool TryProjectRows(
        Stage3DerivationProfileEnvelope envelope,
        LexCorpus6BuildResult corpus,
        out MemberRow[] members,
        out CorrigendumLineRow[] lines,
        out CorrigendumGapRow[] gaps,
        out ArticleRow[] articles,
        out EuropeIndexBuildRefusal refusal,
        out string? detail)
    {
        var set = corpus.VerifiedSet.Set;
        var euMembers = set.Members.Where(static member => member.Publisher == PublisherId.EuEurLex).ToArray();
        members = euMembers.Select(static member => new MemberRow(
                member.ObjectRefSha256,
                member.SourceOrdinal,
                ContractWire.NameOf(member.Outcome),
                member.EuropeContentClass is null ? null : ContractWire.NameOf(member.EuropeContentClass.ContentClass),
                JsonSerializer.Serialize(member.Stage3Outcomes.Select(static value => new
                {
                    domain = ContractWire.NameOf(value.Domain),
                    semantic_identity_sha256 = value.SemanticIdentitySha256,
                    disposition = ContractWire.NameOf(value.Disposition),
                })),
                JsonSerializer.Serialize(member.Gaps)))
            .OrderBy(static row => row.ObjectRefSha256, StringComparer.Ordinal).ToArray();
        var sourceRecords = envelope.BodyComposition.Envelope.Europe.CorpusRecordSet!.Set.Records;
        var memberByObject = euMembers.ToDictionary(static value => value.ObjectRefSha256, StringComparer.Ordinal);
        if (members.Length != sourceRecords.Count || memberByObject.Count != sourceRecords.Count)
        {
            lines = [];
            gaps = [];
            articles = [];
            refusal = EuropeIndexBuildRefusal.PopulationMismatch;
            detail = "The EU corpus population is missing, duplicated or extra.";
            return false;
        }

        lines = set.CorrigendumProductions.SelectMany(static production =>
                production.Tripwires.SelectMany(tripwire => tripwire.Lines.Select(line =>
                    CorrigendumLineRow.From(production.FamilyKey, tripwire.CorrectedWorkRoot, line))))
            .OrderBy(static row => row.LineIdentitySha256, StringComparer.Ordinal).ToArray();
        gaps = set.CorrigendumProductions.SelectMany(static production =>
                production.UnresolvedGaps.Select(gap => CorrigendumGapRow.From(production.FamilyKey, gap)))
            .OrderBy(static row => row.GapIdentitySha256, StringComparer.Ordinal).ToArray();
        if (lines.Select(static row => row.LineIdentitySha256).Distinct(StringComparer.Ordinal).Count() != lines.Length ||
            gaps.Select(static row => row.GapIdentitySha256).Distinct(StringComparer.Ordinal).Count() != gaps.Length)
        {
            articles = [];
            refusal = EuropeIndexBuildRefusal.DerivationMismatch;
            detail = "The accepted corrigendum projection contains a duplicate logical row.";
            return false;
        }

        var projected = new List<ArticleRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outcome in envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes)
        {
            if (!seen.Add(outcome.SemanticIdentitySha256))
            {
                articles = [];
                refusal = EuropeIndexBuildRefusal.PopulationMismatch;
                detail = "The Formex main-body population contains a duplicate outcome.";
                return false;
            }
            var matching = euMembers.Where(member => member.Stage3Outcomes.Any(value =>
                    value.Domain == LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody &&
                    string.Equals(value.SemanticIdentitySha256, outcome.SemanticIdentitySha256,
                        StringComparison.Ordinal)))
                .Take(2).ToArray();
            if (matching.Length == 0 && outcome.Source.Kind != EuFormexPackageOutcomeKind.Acquired)
            {
                continue;
            }
            if (matching.Length == 0 && LexCorpus6Builder.IsUnboundAlternateLanguage(
                    outcome.Source.Expression,
                    sourceRecords
                        .Where(static record => record.Body.Kind == CorpusBodyRecordKind.Held)
                        .Select(static record => record.ObjectRef)))
            {
                continue;
            }
            if (matching.Length != 1)
            {
                articles = [];
                refusal = EuropeIndexBuildRefusal.DerivationMismatch;
                detail = "A Formex main-body outcome does not bind to exactly one corpus member.";
                return false;
            }
            var member = matching[0];
            var objectRef = member.ObjectRefSha256;

            if (outcome.Source.Kind != EuFormexPackageOutcomeKind.Acquired)
            {
                if (outcome.Disposition == EuFormexMainBodyLegalContentDisposition.Admitted)
                    throw new InvalidDataException("A non-acquired Formex outcome claims admitted legal content.");
                continue;
            }
            if (outcome.Disposition != EuFormexMainBodyLegalContentDisposition.Admitted) continue;
            if (member.Outcome != LexCorpus6OutcomeKind.Acquired ||
                member.EuropeContentClass?.ContentClass != EuContentClass.OriginalLegalText)
            {
                articles = [];
                refusal = EuropeIndexBuildRefusal.RightsIneligible;
                detail = objectRef;
                return false;
            }

            foreach (var article in outcome.Articles)
            {
                projected.Add(new ArticleRow(
                    article.IdentitySha256,
                    objectRef,
                    outcome.Source.ExpressionIdentity.PublisherWorkId,
                    article.PublisherExpressionId,
                    article.PackageEntry,
                    article.PublisherIdentifier,
                    article.Heading,
                    WordingDate(article.PublisherDate),
                    LanguageToken(article.Language),
                    article.SearchableText,
                    JsonSerializer.Serialize(article.Tokens.Select(static token => new
                    {
                        kind = ContractWire.NameOf(token.Kind),
                        text = token.Text,
                        target = token.Target,
                        note_body = token.NoteBody?.Select(static nested => new
                        {
                            kind = ContractWire.NameOf(nested.Kind),
                            text = nested.Text,
                            target = nested.Target,
                        }),
                    }))));
            }
        }
        articles = projected.OrderBy(static row => row.ArticleIdentitySha256, StringComparer.Ordinal).ToArray();
        if (articles.Select(static row => row.ArticleIdentitySha256).Distinct(StringComparer.Ordinal).Count() != articles.Length)
        {
            refusal = EuropeIndexBuildRefusal.DerivationMismatch;
            detail = "The admitted Formex projection contains a duplicate article identity.";
            return false;
        }
        refusal = EuropeIndexBuildRefusal.None;
        detail = null;
        return true;
    }

    private static string LanguageToken(string value) => value switch
    {
        "EN" or "ENG" => ContractWire.NameOf(EuOfficialLanguage.English).ToLowerInvariant(),
        "FR" or "FRA" => ContractWire.NameOf(EuOfficialLanguage.French).ToLowerInvariant(),
        _ => throw new InvalidDataException("A retained Formex main body is outside the accepted English/French body scope."),
    };

    private static string WordingDate(string value)
    {
        var formats = new[] { "yyyyMMdd", "yyyy-MM-dd" };
        if (!DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw new InvalidDataException("A Formex publisher date is not one exact civil date.");
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static void BuildDatabase(
        string path,
        string corpusSha256,
        string logicalRowsSha256,
        IReadOnlyList<MemberRow> members,
        IReadOnlyList<CorrigendumLineRow> lines,
        IReadOnlyList<CorrigendumGapRow> gaps,
        IReadOnlyList<ArticleRow> articles)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWriteCreate);
        Execute(connection, "PRAGMA page_size=4096");
        Execute(connection, "PRAGMA encoding='UTF-8'");
        Execute(connection, "PRAGMA auto_vacuum=NONE");
        Execute(connection, "PRAGMA journal_mode=DELETE");
        Execute(connection, "PRAGMA synchronous=FULL");
        Execute(connection, "PRAGMA foreign_keys=ON");
        Execute(connection, $"PRAGMA application_id={ApplicationId}");
        Execute(connection, "PRAGMA user_version=1");
        using var transaction = connection.BeginTransaction();
        Execute(connection, Ddl, transaction);
        foreach (var row in members)
            Insert(connection, transaction, "INSERT INTO members VALUES($p0,$p1,$p2,$p3,$p4,$p5)",
                row.ObjectRefSha256, row.SourceOrdinal, row.Outcome, row.ContentClass,
                row.Stage3OutcomesJson, row.GapsJson);
        foreach (var row in lines)
            Insert(connection, transaction, "INSERT INTO corrigendum_lines VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10)",
                row.LineIdentitySha256, row.FamilyKey, row.CorrectedWorkRoot, row.CorrigendumWorkRoot,
                row.PublisherExpressionId, row.LanguageIri, row.Reach, row.DateState,
                row.PublisherDateRawLexical, row.PublisherDateDatatypeIri, row.ExpressionContentSha256);
        foreach (var row in gaps)
            Insert(connection, transaction, "INSERT INTO corrigendum_gaps VALUES($p0,$p1,$p2,$p3)",
                row.GapIdentitySha256, row.FamilyKey, row.WorkRoot, row.Reason);
        foreach (var row in articles)
            Insert(connection, transaction, "INSERT INTO articles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10)",
                row.ArticleIdentitySha256, row.ObjectRefSha256, row.PublisherWorkId,
                row.PublisherExpressionId, row.PackageEntry, row.PublisherIdentifier, row.Heading,
                row.WordingDate, row.Language, row.SearchableText, row.TokensJson);
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
            new string('1', 64), 0, "acquired", "original_legal_text", "[]", "[]");
        var line = new CorrigendumLineRow(
            new string('2', 64), "eu-object-facts-batch-000000000000000000000000",
            "https://example.invalid/work", "https://example.invalid/corrigendum",
            "https://example.invalid/expression", "https://example.invalid/language/ENG",
            "english_only", "publisher_dated", "2024-01-01",
            "http://www.w3.org/2001/XMLSchema#date", new string('3', 64));
        var gap = new CorrigendumGapRow(
            new string('4', 64), line.FamilyKey, "https://example.invalid/gap",
            "corrects_not_stated_by_consulted_delivery");
        var article = new ArticleRow(
            new string('5', 64), member.ObjectRefSha256,
            "https://example.invalid/work", "https://example.invalid/expression", "body.xml",
            "1", "Article 1", "2024-01-01", "eng", "fixed wording", "[]");
        var members = new[] { member };
        var lines = new[] { line };
        var gaps = new[] { gap };
        var articles = new[] { article };
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-eu-index-pin-{Guid.NewGuid():N}.sqlite");
        try
        {
            BuildDatabase(path, new string('a', 64),
                HashLogicalRows(members, lines, gaps, articles), members, lines, gaps, articles);
            return File.ReadAllBytes(path);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    internal static V3IndexCapabilityManifest MeasureCapabilities(
        string digest,
        IReadOnlyList<ArticleRow> articles)
    {
        var cells = articles.Where(static row => row.SearchableText.Length != 0)
            .GroupBy(static row => (row.Language, row.WordingDate))
            .Select(group => new V3IndexCapabilityCell(
                PublisherId.EuEurLex,
                digest,
                "search",
                "articles",
                "searchable_text",
                group.Key.Language,
                DateOnly.ParseExact(group.Key.WordingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(group.Key.WordingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                group.LongCount()))
            .ToArray();
        if (!V3IndexCapabilityManifest.TryCreate(
                PublisherId.EuEurLex, digest, cells, out var manifest, out var refusal))
            throw new InvalidDataException($"Measured EU capabilities are invalid: {refusal}.");
        return manifest!;
    }

    internal static string HashLogicalRows(
        IReadOnlyList<MemberRow> members,
        IReadOnlyList<CorrigendumLineRow> lines,
        IReadOnlyList<CorrigendumGapRow> gaps,
        IReadOnlyList<ArticleRow> articles) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new LogicalRows(members, lines, gaps, articles))));

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
            DataSource = ":memory:", Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Private, Pooling = false,
        }.ToString());
        expected.Open();
        Execute(expected, Ddl);
        if (!ReadSchema(expected).SequenceEqual(ReadSchema(actual), StringComparer.Ordinal))
            throw new InvalidDataException("The EU index schema differs from the exact terminal schema.");
    }

    private static string[] ReadSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type,name,tbl_name,coalesce(sql,'') FROM sqlite_schema " +
            "WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name,tbl_name";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join('\n', reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows.ToArray();
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction,
        string sql, params object?[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        for (var index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue($"$p{index}", values[index] ?? DBNull.Value);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidDataException("An EU index insert did not affect exactly one row.");
    }

    internal static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-journal", path + "-wal", path + "-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
    }

    internal sealed record MemberRow(string ObjectRefSha256, int SourceOrdinal, string Outcome,
        string? ContentClass, string Stage3OutcomesJson, string GapsJson);

    internal sealed record CorrigendumLineRow(
        string LineIdentitySha256, string FamilyKey, string CorrectedWorkRoot,
        string CorrigendumWorkRoot, string PublisherExpressionId, string LanguageIri,
        string Reach, string DateState, string? PublisherDateRawLexical,
        string? PublisherDateDatatypeIri, string ExpressionContentSha256)
    {
        internal static CorrigendumLineRow From(
            string familyKey, string correctedWorkRoot, LexCorpus6CorrigendumLine line)
        {
            var values = new[] { familyKey, correctedWorkRoot, line.CorrigendumWorkRoot,
                line.PublisherExpressionId, line.LanguageIri, ContractWire.NameOf(line.Reach),
                ContractWire.NameOf(line.DateState), line.PublisherDateRawLexical ?? "",
                line.PublisherDateDatatypeIri ?? "", line.ExpressionContentSha256 };
            return new CorrigendumLineRow(Identity("lex-v3-eu-index-corrigendum-line/1", values),
                familyKey, correctedWorkRoot, line.CorrigendumWorkRoot, line.PublisherExpressionId,
                line.LanguageIri, values[5], values[6], line.PublisherDateRawLexical,
                line.PublisherDateDatatypeIri, line.ExpressionContentSha256);
        }
    }

    internal sealed record CorrigendumGapRow(
        string GapIdentitySha256, string FamilyKey, string WorkRoot, string Reason)
    {
        internal static CorrigendumGapRow From(string familyKey, LexCorpus6CorrigendumGap gap)
        {
            var reason = ContractWire.NameOf(gap.Reason);
            return new CorrigendumGapRow(
                Identity("lex-v3-eu-index-corrigendum-gap/1", [familyKey, gap.WorkRoot, reason]),
                familyKey, gap.WorkRoot, reason);
        }
    }

    internal sealed record ArticleRow(
        string ArticleIdentitySha256, string ObjectRefSha256, string PublisherWorkId,
        string PublisherExpressionId, string PackageEntry, string PublisherIdentifier,
        string Heading, string WordingDate, string Language, string SearchableText, string TokensJson);

    private sealed record LogicalRows(IReadOnlyList<MemberRow> Members,
        IReadOnlyList<CorrigendumLineRow> CorrigendumLines,
        IReadOnlyList<CorrigendumGapRow> CorrigendumGaps,
        IReadOnlyList<ArticleRow> Articles);

    private static string Identity(string schema, IEnumerable<string> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        EuFormexMainBodyArticle.Append(hash, schema);
        foreach (var value in values) EuFormexMainBodyArticle.Append(hash, value);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
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

/// <summary>A verified read-only mount of one exact EU index.</summary>
public sealed class EuropeIndexReader : IDisposable
{
    private readonly string _path;
    private readonly SqliteConnection _connection;
    private readonly V3IndexCapabilityManifest _capabilityManifest;
    private readonly SourceArtifactRef _indexRef;
    private readonly SourceArtifactRef _corpusRef;

    private EuropeIndexReader(string path, SqliteConnection connection,
        V3IndexCapabilityManifest capabilityManifest,
        SourceArtifactRef indexRef,
        SourceArtifactRef corpusRef) =>
        (_path, _connection, _capabilityManifest, _indexRef, _corpusRef) =
        (path, connection, capabilityManifest, indexRef, corpusRef);

    public long MemberCount => Count("members");
    public long ArticleCount => Count("articles");
    public long CorrigendumLineCount => Count("corrigendum_lines");
    public long CorrigendumGapCount => Count("corrigendum_gaps");
    public SourceArtifactRef IndexRef => _indexRef;
    public SourceArtifactRef CorpusRef => _corpusRef;

    public static EuropeIndexReader OpenAndVerify(
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
            capabilityManifest.Publisher != PublisherId.EuEurLex ||
            !string.Equals(capabilityManifest.IndexSha256, digest, StringComparison.Ordinal))
            throw new ArgumentException("The EU index identity or capability binding is invalid.");
        if (!string.Equals(expectedCorpusRef.ResourceId,
                LexCorpus6Builder.ResourceIdOf(expectedCorpusRef.Sha256), StringComparison.Ordinal))
            throw new InvalidDataException("The expected corpus resource identity is not bound to its digest.");

        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-eu-mount-{Guid.NewGuid():N}.sqlite");
        File.WriteAllBytes(path, indexBytes.ToArray());
        SqliteConnection? connection = null;
        try
        {
            connection = EuropeIndexBuilder.Open(path, SqliteOpenMode.ReadOnly);
            EuropeIndexBuilder.EnsureExactSchema(connection);
            if (!string.Equals(Scalar(connection, "PRAGMA integrity_check"), "ok", StringComparison.Ordinal) ||
                Convert.ToInt32(Scalar(connection, "PRAGMA application_id"), CultureInfo.InvariantCulture) != 0x4c563307 ||
                Convert.ToInt32(Scalar(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("The EU index failed SQLite integrity or schema identity checks.");

            using var stamp = connection.CreateCommand();
            stamp.CommandText = "SELECT schema_identity,corpus_sha256,logical_rows_sha256,sqlite_version,sqlite_source_id,compile_options_sha256 FROM stamp WHERE stamp_id=1";
            using var stampReader = stamp.ExecuteReader();
            if (!stampReader.Read() ||
                !string.Equals(stampReader.GetString(0), EuropeIndexBuilder.Schema, StringComparison.Ordinal) ||
                !string.Equals(stampReader.GetString(1), expectedCorpusRef.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("The EU index stamp does not bind the expected corpus.");
            var expectedLogical = stampReader.GetString(2);
            var expectedProvenance = new EuropeIndexBuilder.SqliteProvenance(
                stampReader.GetString(3), stampReader.GetString(4), stampReader.GetString(5));
            if (stampReader.Read()) throw new InvalidDataException("The EU index has multiple stamp rows.");
            stampReader.Close();
            if (expectedProvenance != EuropeIndexBuilder.SqliteProvenance.Read(connection))
                throw new InvalidDataException("The EU index SQLite provenance changed.");

            var members = ReadMembers(connection);
            var lines = ReadLines(connection);
            var gaps = ReadGaps(connection);
            var articles = ReadArticles(connection);
            if (!string.Equals(EuropeIndexBuilder.HashLogicalRows(members, lines, gaps, articles),
                    expectedLogical, StringComparison.Ordinal))
                throw new InvalidDataException("The EU index logical rows do not match their stamp.");
            var measured = EuropeIndexBuilder.MeasureCapabilities(digest, articles);
            if (!measured.Cells.SequenceEqual(capabilityManifest.Cells))
                throw new InvalidDataException("The EU capability manifest was not measured from the index.");
            return new EuropeIndexReader(
                path, connection, capabilityManifest, indexRef, expectedCorpusRef);
        }
        catch
        {
            connection?.Dispose();
            EuropeIndexBuilder.DeleteDatabase(path);
            throw;
        }
    }

    public IReadOnlyList<EuropeIndexResolvedExpression> ResolveExact(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT publisher_work_id,publisher_expression_id,language,
                   publisher_identifier,article_identity_sha256
            FROM articles
            WHERE publisher_work_id=$identifier OR publisher_expression_id=$identifier
               OR publisher_identifier=$identifier OR article_identity_sha256=$identifier
            ORDER BY publisher_work_id,publisher_expression_id,language,
                     publisher_identifier,article_identity_sha256
            """;
        command.Parameters.AddWithValue("$identifier", identifier);
        using var reader = command.ExecuteReader();
        var rows = new List<(string Work, string Expression, string Language,
            string Provision, string Article)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4)));
        }

        return rows
            .GroupBy(static row => (row.Work, row.Expression, row.Language))
            .Select(static group => new EuropeIndexResolvedExpression(
                group.Key.Work,
                group.Key.Expression,
                group.Key.Language,
                Array.AsReadOnly(group.Select(static row => row.Provision)
                    .Distinct(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(group.Select(static row => row.Article).ToArray())))
            .ToArray();
    }

    public EuropeIndexSearchResult Search(string language, DateOnly from, DateOnly to, string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var outcome = _capabilityManifest.Lookup(
            "search", "articles", "searchable_text", language, from, to, out _);
        if (outcome != V3IndexCapabilityLookupOutcome.Supported)
            return new EuropeIndexSearchResult(outcome, Array.Empty<string>());
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT article_identity_sha256 FROM articles
            WHERE language=$language AND wording_date BETWEEN $from AND $to
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
        return new EuropeIndexSearchResult(outcome, values.AsReadOnly());
    }

    public void Dispose()
    {
        _connection.Dispose();
        EuropeIndexBuilder.DeleteDatabase(_path);
    }

    private long Count(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = table switch
        {
            "members" => "SELECT count(*) FROM members",
            "articles" => "SELECT count(*) FROM articles",
            "corrigendum_lines" => "SELECT count(*) FROM corrigendum_lines",
            "corrigendum_gaps" => "SELECT count(*) FROM corrigendum_gaps",
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

    private static EuropeIndexBuilder.MemberRow[] ReadMembers(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT object_ref_sha256,source_ordinal,outcome,content_class,stage3_outcomes_json,gaps_json FROM members ORDER BY object_ref_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<EuropeIndexBuilder.MemberRow>();
        while (reader.Read()) values.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return values.ToArray();
    }

    private static EuropeIndexBuilder.CorrigendumLineRow[] ReadLines(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT line_identity_sha256,family_key,corrected_work_root,corrigendum_work_root,publisher_expression_id,language_iri,reach,date_state,publisher_date_raw_lexical,publisher_date_datatype_iri,expression_content_sha256 FROM corrigendum_lines ORDER BY line_identity_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<EuropeIndexBuilder.CorrigendumLineRow>();
        while (reader.Read()) values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10)));
        return values.ToArray();
    }

    private static EuropeIndexBuilder.CorrigendumGapRow[] ReadGaps(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT gap_identity_sha256,family_key,work_root,reason FROM corrigendum_gaps ORDER BY gap_identity_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<EuropeIndexBuilder.CorrigendumGapRow>();
        while (reader.Read()) values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return values.ToArray();
    }

    private static EuropeIndexBuilder.ArticleRow[] ReadArticles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,publisher_work_id,publisher_expression_id,package_entry,publisher_identifier,heading,wording_date,language,searchable_text,tokens_json FROM articles ORDER BY article_identity_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<EuropeIndexBuilder.ArticleRow>();
        while (reader.Read()) values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10)));
        return values.ToArray();
    }
}
