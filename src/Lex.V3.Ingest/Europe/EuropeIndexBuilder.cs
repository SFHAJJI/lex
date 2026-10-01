using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Platform;
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

/// <summary>
/// One article of one held EU expression that a search matched: the work, its CELEX and the expression,
/// the publisher's article id and heading, the Formex act date of the wording (<c>wording_date</c>, the
/// date of the original act the package carries, never an applicability or consolidation date), the
/// language, the article identity, and the qualified provision coordinate EU <c>resolve</c> accepts.
/// </summary>
public sealed record EuropeIndexSearchHit(
    string PublisherWorkId,
    string PublisherWorkCelex,
    string PublisherExpressionId,
    string PublisherIdentifier,
    string Heading,
    string WordingDate,
    string Language,
    string ArticleIdentitySha256,
    string ProvisionCoordinate);

/// <summary>
/// One held expression of an EU work as the index holds it: the work and its CELEX, the expression and its
/// language, the Formex act date of its wording (normally one), its article count, and the corpus members
/// its articles were read from with their outcome and content class.
/// </summary>
public sealed record EuropeIndexWorkExpression(
    string PublisherWorkId,
    string PublisherWorkCelex,
    string PublisherExpressionId,
    string Language,
    IReadOnlyList<string> WordingDates,
    long ArticleCount,
    IReadOnlyList<EuropeIndexExpressionMember> Members);

/// <summary>A corpus member an expression's articles were read from, with its outcome and content class.</summary>
public sealed record EuropeIndexExpressionMember(string ObjectRefSha256, string Outcome, string? ContentClass);

public sealed record EuropeIndexResolvedExpression(
    string PublisherWorkId,
    string PublisherExpressionId,
    string Language,
    IReadOnlyList<string> PublisherProvisionIdentifiers,
    IReadOnlyList<string> ArticleIdentities);

/// <summary>Builds the immutable EU index from one proof-complete Stage 3 envelope.</summary>
public static partial class EuropeIndexBuilder
{
    public const string Schema = "lex-v3-europe-index/5";
    internal const string DigestSchema = "lex-v3-europe-index/4";
    internal const string SourceSchema = "lex-v3-europe-index/3";
    internal const string LegacySchema = "lex-v3-europe-index/2";
    private const int ApplicationId = 0x4c563307;
    private const string LegacyDdl = """
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
          publisher_work_celex TEXT COLLATE BINARY NOT NULL,
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


    private const string SourceDdl = LegacyDdl + """

        CREATE TABLE article_sources (
          article_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY REFERENCES articles(article_identity_sha256),
          package_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(package_sha256) = 64),
          official_source_uri TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        """;

    private const string DigestDdl = SourceDdl + """

        CREATE TABLE article_digests (
          article_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY REFERENCES articles(article_identity_sha256),
          source_entry_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(source_entry_sha256) = 64),
          text_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(text_sha256) = 64)
        ) STRICT;
        """;

    private static readonly string Ddl = DigestDdl.Replace(
        "publisher_work_celex TEXT COLLATE BINARY NOT NULL,",
        "publisher_work_celex TEXT COLLATE BINARY,", StringComparison.Ordinal) + StatesDdl;

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
                    out var articles, out var sources, out var digests, out refusal, out detail))
                return null;
            var states = ProjectStates(envelope.BodyComposition.Envelope.Europe.ObservedWorkFacts);
            var logicalRowsSha256 = HashLogicalRows(members, lines, gaps, articles, sources, digests, states);
            var path = Path.Combine(Path.GetTempPath(), $"lex-v3-eu-index-{Guid.NewGuid():N}.sqlite");
            try
            {
                BuildDatabase(path, corpus.ArtifactRef.Sha256, logicalRowsSha256,
                    members, lines, gaps, articles, sources, digests, states);
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
        out ArticleSourceRow[] sources,
        out ArticleDigestRow[] digests,
        out EuropeIndexBuildRefusal refusal,
        out string? detail)
    {
        sources = [];
        digests = [];
        var projectedDigests = new List<ArticleDigestRow>();
        var projectedSources = new List<ArticleSourceRow>();
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
                member.EuropeContentClass?.ContentClass is not (EuContentClass.OriginalLegalText or EuContentClass.Consolidation))
            {
                articles = [];
                refusal = EuropeIndexBuildRefusal.RightsIneligible;
                detail = objectRef;
                return false;
            }
            var workCelex = EuObservedWorkIdentity.Resolve(envelope.BodyComposition.Envelope.Europe,
                outcome.Source.ExpressionIdentity.PublisherWorkId);
            if (workCelex is null && !EuObservedWorkIdentity.IsProven(envelope.BodyComposition.Envelope.Europe,
                    outcome.Source.ExpressionIdentity.PublisherWorkId))
            {
                articles = [];
                refusal = EuropeIndexBuildRefusal.DerivationMismatch;
                detail = "An admitted Formex work has no original-root or proven census identity.";
                return false;
            }

            var inventory = outcome.Source.AcquiredInventory
                ?? throw new InvalidDataException("An admitted Formex outcome has no retained inventory.");
            foreach (var article in outcome.Articles)
            {
                projectedDigests.Add(new ArticleDigestRow(article.IdentitySha256, article.SourceEntrySha256, article.TextSha256));
                projectedSources.Add(new ArticleSourceRow(article.IdentitySha256,
                    inventory.SourceReceipt.Reference.ContentSha256,
                    inventory.TransportBinding.RequestEvidence.Uri));
                projected.Add(new ArticleRow(
                    article.IdentitySha256,
                    objectRef,
                    outcome.Source.ExpressionIdentity.PublisherWorkId,
                    workCelex,
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
        digests = projectedDigests.OrderBy(static row => row.ArticleIdentitySha256, StringComparer.Ordinal).ToArray();
        sources = projectedSources.OrderBy(static row => row.ArticleIdentitySha256, StringComparer.Ordinal).ToArray();
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
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<ArticleSourceRow> sources,
        IReadOnlyList<ArticleDigestRow> digests,
        IReadOnlyList<EuropeIndexState> states)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWriteCreate);
        Execute(connection, "PRAGMA page_size=4096");
        Execute(connection, "PRAGMA encoding='UTF-8'");
        Execute(connection, "PRAGMA auto_vacuum=NONE");
        Execute(connection, "PRAGMA journal_mode=DELETE");
        Execute(connection, "PRAGMA synchronous=FULL");
        Execute(connection, "PRAGMA foreign_keys=ON");
        Execute(connection, $"PRAGMA application_id={ApplicationId}");
        Execute(connection, "PRAGMA user_version=5");
        using var transaction = connection.BeginTransaction();
        Execute(connection, Ddl.Replace("\r\n", "\n", StringComparison.Ordinal), transaction);
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
            Insert(connection, transaction, "INSERT INTO articles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11)",
                row.ArticleIdentitySha256, row.ObjectRefSha256, row.PublisherWorkId,
                row.PublisherWorkCelex, row.PublisherExpressionId, row.PackageEntry,
                row.PublisherIdentifier, row.Heading, row.WordingDate, row.Language,
                row.SearchableText, row.TokensJson);
        foreach (var row in sources)
            Insert(connection, transaction, "INSERT INTO article_sources VALUES($p0,$p1,$p2)",
                row.ArticleIdentitySha256, row.PackageSha256, row.OfficialSourceUri);
        foreach (var row in digests)
            Insert(connection, transaction, "INSERT INTO article_digests VALUES($p0,$p1,$p2)",
                row.ArticleIdentitySha256, row.SourceEntrySha256, row.TextSha256);
        foreach (var row in states)
            Insert(connection, transaction, "INSERT INTO states VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7)",
                row.StateIdentitySha256, row.SeedCelex, row.RootWorkIri, row.PublisherWorkIri,
                row.PublisherWorkCelex, (int)row.DateStatus, row.PublisherConsolidationDate, row.FactsJson);
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
            "https://example.invalid/work", "32024R0001", "https://example.invalid/expression",
            "body.xml", "1", "Article 1", "2024-01-01", "eng", "fixed wording", "[]");
        var members = new[] { member };
        var lines = new[] { line };
        var gaps = new[] { gap };
        var articles = new[] { article };
        var sources = new[] { new ArticleSourceRow(article.ArticleIdentitySha256,
            new string('6', 64), "https://publications.europa.eu/resource/cellar/fixed") };
        var digests = new[] { new ArticleDigestRow(article.ArticleIdentitySha256, new string('7', 64),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(article.SearchableText)))) };
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-eu-index-pin-{Guid.NewGuid():N}.sqlite");
        try
        {
            BuildDatabase(path, new string('a', 64),
                HashLogicalRows(members, lines, gaps, articles, sources, digests, []), members, lines, gaps, articles, sources, digests, []);
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
                PublisherId.EuEurLex, digest, cells, V3UnservedOperations.Rows, out var manifest, out var refusal))
            throw new InvalidDataException($"Measured EU capabilities are invalid: {refusal}.");
        return manifest!;
    }

    internal static string HashLogicalRows(
        IReadOnlyList<MemberRow> members,
        IReadOnlyList<CorrigendumLineRow> lines,
        IReadOnlyList<CorrigendumGapRow> gaps,
        IReadOnlyList<ArticleRow> articles,
        IReadOnlyList<ArticleSourceRow>? sources = null,
        IReadOnlyList<ArticleDigestRow>? digests = null,
        IReadOnlyList<EuropeIndexState>? states = null) =>
        Convert.ToHexStringLower(SHA256.HashData(states is not null
            ? JsonSerializer.SerializeToUtf8Bytes(new LogicalRowsWithStates(members, lines, gaps, articles,
                sources ?? throw new ArgumentNullException(nameof(sources)),
                digests ?? throw new ArgumentNullException(nameof(digests)), states))
            : digests is not null
            ? JsonSerializer.SerializeToUtf8Bytes(new LogicalRowsWithDigests(members, lines, gaps, articles,
                sources ?? throw new ArgumentNullException(nameof(sources)), digests))
            : sources is null
                ? JsonSerializer.SerializeToUtf8Bytes(new LogicalRows(members, lines, gaps, articles))
                : JsonSerializer.SerializeToUtf8Bytes(new LogicalRowsWithSources(members, lines, gaps, articles, sources))));

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

    internal static void EnsureExactSchema(SqliteConnection actual, int version = 5)
    {
        using var expected = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:", Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Private, Pooling = false,
        }.ToString());
        expected.Open();
        Execute(expected, (version switch { 2 => LegacyDdl, 3 => SourceDdl, 4 => DigestDdl, 5 => Ddl, _ => throw new InvalidDataException("Unsupported EU schema.") }).Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!ReadSchema(expected).SequenceEqual(ReadSchema(actual), StringComparer.Ordinal))
            throw new InvalidDataException("The EU index schema differs from the exact terminal schema.");
    }

    // C# raw SQL literals inherited checkout line endings in older mounts. Compare only that
    // known spelling difference; all other schema text and the original file digest stay exact.
    private static string[] ReadSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type,name,tbl_name,coalesce(sql,'') FROM sqlite_schema " +
            "WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name,tbl_name";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join('\n', reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3).Replace("\r\n", "\n", StringComparison.Ordinal)));
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
        string? PublisherWorkCelex, string PublisherExpressionId, string PackageEntry,
        string PublisherIdentifier,
        string Heading, string WordingDate, string Language, string SearchableText, string TokensJson);

    internal sealed record ArticleSourceRow(string ArticleIdentitySha256, string PackageSha256, string OfficialSourceUri);

    internal sealed record ArticleDigestRow(string ArticleIdentitySha256, string SourceEntrySha256, string TextSha256);

    private sealed record LogicalRowsWithDigests(IReadOnlyList<MemberRow> Members,
        IReadOnlyList<CorrigendumLineRow> CorrigendumLines,
        IReadOnlyList<CorrigendumGapRow> CorrigendumGaps,
        IReadOnlyList<ArticleRow> Articles,
        IReadOnlyList<ArticleSourceRow> ArticleSources,
        IReadOnlyList<ArticleDigestRow> ArticleDigests);

    private sealed record LogicalRowsWithSources(IReadOnlyList<MemberRow> Members,
        IReadOnlyList<CorrigendumLineRow> CorrigendumLines,
        IReadOnlyList<CorrigendumGapRow> CorrigendumGaps,
        IReadOnlyList<ArticleRow> Articles,
        IReadOnlyList<ArticleSourceRow> ArticleSources);

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

/// <summary>
/// Coordinates retained from the admitted Formex transport and article projection.
/// PackageSha256 identifies the complete ZIP, not article text or the separate XHTML body.
/// This index projection does not replace reopening the original custody evidence.
/// </summary>
public sealed record EuropeIndexArticleSourceEvidence(
    string ArticleIdentitySha256, string PublisherWorkId, string PublisherExpressionId,
    string PublisherIdentifier, string WordingDate, string Language, string PackageEntry,
    string PackageSha256, string OfficialSourceUri);

/// <summary>Hashes of the exact XML entry bytes and emitted SearchableText UTF-8 bytes.
/// Package SHA-256 and official URI remain in the article source coordinates.</summary>
public sealed record EuropeIndexArticleByteDigests(string ArticleIdentitySha256, string SourceEntrySha256, string TextSha256);

/// <summary>A verified read-only mount of one exact EU index.</summary>
public sealed partial class EuropeIndexReader : IDisposable
{
    private const string ProvisionCoordinateMarker = "#lex-provision=";
    private readonly string _path;
    private readonly SqliteConnection _connection;
    private readonly V3IndexCapabilityManifest _capabilityManifest;
    private readonly SourceArtifactRef _indexRef;
    private readonly SourceArtifactRef _corpusRef;
    private readonly object _gate = new();
    public bool HasArticleSourceEvidence { get; }
    public bool HasArticleByteDigests { get; }

    private EuropeIndexReader(string path, SqliteConnection connection,
        V3IndexCapabilityManifest capabilityManifest,
        SourceArtifactRef indexRef,
        SourceArtifactRef corpusRef, bool hasArticleSourceEvidence, bool hasArticleByteDigests, bool hasStates) =>
        (_path, _connection, _capabilityManifest, _indexRef, _corpusRef, HasArticleSourceEvidence, HasArticleByteDigests, HasStates) =
        (path, connection, capabilityManifest, indexRef, corpusRef, hasArticleSourceEvidence, hasArticleByteDigests, hasStates);

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
            var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture);
            if (version is not (2 or 3 or 4 or 5)) throw new InvalidDataException("Unsupported EU index version.");
            var legacy = version == 2;
            EuropeIndexBuilder.EnsureExactSchema(connection, version);
            if (!string.Equals(Scalar(connection, "PRAGMA integrity_check"), "ok", StringComparison.Ordinal) ||
                Convert.ToInt32(Scalar(connection, "PRAGMA application_id"), CultureInfo.InvariantCulture) != 0x4c563307)
                throw new InvalidDataException("The EU index failed SQLite integrity or schema identity checks.");

            using var stamp = connection.CreateCommand();
            stamp.CommandText = "SELECT schema_identity,corpus_sha256,logical_rows_sha256,sqlite_version,sqlite_source_id,compile_options_sha256 FROM stamp WHERE stamp_id=1";
            using var stampReader = stamp.ExecuteReader();
            if (!stampReader.Read() ||
                !string.Equals(stampReader.GetString(0), (version switch { 2 => EuropeIndexBuilder.LegacySchema, 3 => EuropeIndexBuilder.SourceSchema, 4 => EuropeIndexBuilder.DigestSchema, _ => EuropeIndexBuilder.Schema }), StringComparison.Ordinal) ||
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
            var sources = legacy ? null : ReadArticleSources(connection);
            if (sources is not null &&
                (!sources.Select(static row => row.ArticleIdentitySha256).SequenceEqual(
                    articles.Select(static row => row.ArticleIdentitySha256), StringComparer.Ordinal) ||
                 sources.Any(static row => row.PackageSha256.Length != 64 ||
                    row.PackageSha256.Any(static c => !char.IsAsciiHexDigitLower(c)) ||
                    !Uri.TryCreate(row.OfficialSourceUri, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))))
                throw new InvalidDataException("EU article source coordinates are invalid or incomplete.");
            var digests = version >= 4 ? ReadArticleDigests(connection) : null;
            if (digests is not null)
            {
                if (!digests.Select(static row => row.ArticleIdentitySha256).SequenceEqual(
                        articles.Select(static row => row.ArticleIdentitySha256), StringComparer.Ordinal) ||
                    digests.Any(static row => !IsSha256(row.SourceEntrySha256) || !IsSha256(row.TextSha256)))
                    throw new InvalidDataException("EU article byte digests are invalid or incomplete.");
                for (var i = 0; i < articles.Length; i++)
                    if (digests[i].TextSha256 != Convert.ToHexStringLower(SHA256.HashData(
                            Encoding.UTF8.GetBytes(articles[i].SearchableText))))
                        throw new InvalidDataException("EU article text does not match its byte digest.");
                if (articles.Select((article, i) => (sources![i].PackageSha256, article.PackageEntry,
                        digests[i].SourceEntrySha256)).GroupBy(value => (value.PackageSha256, value.PackageEntry))
                    .Any(group => group.Select(value => value.SourceEntrySha256).Distinct(StringComparer.Ordinal).Count() != 1))
                    throw new InvalidDataException("EU articles disagree about the same source entry bytes.");
            }
            var states = version >= 5 ? ReadStates(connection) : null;
            if (states is not null) EuropeIndexBuilder.ValidateStateEvidence(states);
            if (!string.Equals(EuropeIndexBuilder.HashLogicalRows(members, lines, gaps, articles, sources, digests, states),
                    expectedLogical, StringComparison.Ordinal))
                throw new InvalidDataException("The EU index logical rows do not match their stamp.");
            var measured = EuropeIndexBuilder.MeasureCapabilities(digest, articles);
            if (!measured.Cells.SequenceEqual(capabilityManifest.Cells))
                throw new InvalidDataException("The EU capability manifest was not measured from the index.");
            return new EuropeIndexReader(
                path, connection, capabilityManifest, indexRef, expectedCorpusRef, !legacy, version >= 4, version >= 5);
        }
        catch
        {
            connection?.Dispose();
            EuropeIndexBuilder.DeleteDatabase(path);
            throw;
        }
    }

    public static async Task<EuropeIndexReader> OpenAndVerifyFileAsync(
        string indexPath,
        ReadOnlyMemory<byte> capabilityManifestBytes,
        SourceArtifactRef expectedCorpusRef,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        ArgumentNullException.ThrowIfNull(expectedCorpusRef);
        if (!File.Exists(indexPath))
            throw new FileNotFoundException("The EU index artifact is missing.", indexPath);
        var indexBytes = await File.ReadAllBytesAsync(indexPath, cancellationToken)
            .ConfigureAwait(false);
        var digest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
        var indexRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
        var capabilityDigest = V3IndexCapabilityManifestArtifact.ComputeSha256(
            capabilityManifestBytes.Span);
        var capabilityRef = new SourceArtifactRef(
            LexCorpus6Builder.ResourceIdOf(capabilityDigest), capabilityDigest);
        var capability = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            capabilityRef,
            capabilityManifestBytes.Span,
            PublisherId.EuEurLex,
            digest);
        return OpenAndVerify(indexRef, indexBytes, expectedCorpusRef, capability);
    }

    /// <summary>Returns a pinned article's coordinates; null for a missing article or a legacy index.
    /// Check HasArticleSourceEvidence to distinguish legacy storage from an absent identity.</summary>
    public EuropeIndexArticleSourceEvidence? ReadArticleSourceEvidence(string articleIdentitySha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(articleIdentitySha256);
        if (!HasArticleSourceEvidence) return null;
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT a.article_identity_sha256,a.publisher_work_id,a.publisher_expression_id,
                       a.publisher_identifier,a.wording_date,a.language,a.package_entry,
                       s.package_sha256,s.official_source_uri
                FROM articles a JOIN article_sources s USING(article_identity_sha256)
                WHERE a.article_identity_sha256=$identity
                """;
            command.Parameters.AddWithValue("$identity", articleIdentitySha256);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7), reader.GetString(8)) : null;
        }
    }

    /// <summary>Null for an absent article or schema 2/3. No package/network fallback is used.</summary>
    public EuropeIndexArticleByteDigests? ReadArticleByteDigests(string articleIdentitySha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(articleIdentitySha256);
        if (!HasArticleByteDigests) return null;
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT article_identity_sha256,source_entry_sha256,text_sha256 " +
                "FROM article_digests WHERE article_identity_sha256=$identity";
            command.Parameters.AddWithValue("$identity", articleIdentitySha256);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2)) : null;
        }
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);

    public IReadOnlyList<EuropeIndexResolvedExpression> ResolveExact(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var hasQualifiedProvision = TryParseQualifiedProvisionIdentifier(
            identifier, out var provisionExpression, out var provisionIdentifier);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT publisher_work_id,publisher_expression_id,language,
                       publisher_identifier,article_identity_sha256
                FROM articles
                WHERE publisher_work_id=$identifier OR publisher_work_celex=$identifier
                   OR publisher_expression_id=$identifier
                   OR article_identity_sha256=$identifier
                   OR ($has_qualified_provision=1
                       AND publisher_expression_id=$provision_expression
                       AND publisher_identifier=$provision_identifier)
                ORDER BY publisher_work_id,publisher_expression_id,language,
                         publisher_identifier,article_identity_sha256
                """;
            command.Parameters.AddWithValue("$identifier", identifier);
            command.Parameters.AddWithValue(
                "$has_qualified_provision", hasQualifiedProvision ? 1 : 0);
            command.Parameters.AddWithValue("$provision_expression", provisionExpression);
            command.Parameters.AddWithValue("$provision_identifier", provisionIdentifier);
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
    }

    internal static string QualifiedProvisionIdentifierOf(
        string publisherExpressionId,
        string publisherIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherExpressionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherIdentifier);
        return publisherExpressionId + ProvisionCoordinateMarker
            + Uri.EscapeDataString(publisherIdentifier);
    }

    private static bool TryParseQualifiedProvisionIdentifier(
        string identifier,
        out string publisherExpressionId,
        out string publisherIdentifier)
    {
        var marker = identifier.LastIndexOf(ProvisionCoordinateMarker, StringComparison.Ordinal);
        if (marker <= 0 || marker + ProvisionCoordinateMarker.Length >= identifier.Length)
        {
            publisherExpressionId = string.Empty;
            publisherIdentifier = string.Empty;
            return false;
        }

        publisherExpressionId = identifier[..marker];
        var encoded = identifier[(marker + ProvisionCoordinateMarker.Length)..];
        publisherIdentifier = Uri.UnescapeDataString(encoded);
        return publisherIdentifier.Length != 0 && string.Equals(
            identifier,
            QualifiedProvisionIdentifierOf(publisherExpressionId, publisherIdentifier),
            StringComparison.Ordinal);
    }

    public EuropeIndexSearchResult Search(string language, DateOnly from, DateOnly to, string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var outcome = _capabilityManifest.Lookup(
            "search", "articles", "searchable_text", language, from, to, out _);
        if (outcome != V3IndexCapabilityLookupOutcome.Supported)
            return new EuropeIndexSearchResult(outcome, Array.Empty<string>());
        // The shared connection is used under the gate, as ResolveExact uses it: the mount serves
        // requests concurrently and a SQLite connection is not safe to share across threads.
        lock (_gate)
        {
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
    }

    /// <summary>
    /// Every held expression of one EU work, in language and expression order, with the Formex act dates of
    /// its wording, its article count and the members its articles were read from (joined to the members
    /// table for their outcome and content class). This legacy CELEX-addressed surface omits works whose
    /// own CELEX is absent; ReadStateExpressions exposes those through their census/Cellar identities. An unknown work has no rows.
    /// </summary>
    public IReadOnlyList<EuropeIndexWorkExpression> ResolveWorkExpressions(string publisherWorkId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherWorkId);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT a.publisher_work_id,a.publisher_work_celex,a.publisher_expression_id,a.language,
                       a.wording_date,a.object_ref_sha256,m.outcome,m.content_class,count(*)
                FROM articles a JOIN members m ON m.object_ref_sha256=a.object_ref_sha256
                WHERE a.publisher_work_id=$work AND a.publisher_work_celex IS NOT NULL
                GROUP BY a.publisher_work_id,a.publisher_work_celex,a.publisher_expression_id,a.language,
                         a.wording_date,a.object_ref_sha256,m.outcome,m.content_class
                ORDER BY a.language,a.publisher_expression_id,a.wording_date,a.object_ref_sha256
                """;
            command.Parameters.AddWithValue("$work", publisherWorkId);
            using var reader = command.ExecuteReader();
            var rows = new List<(string Work, string Celex, string Expression, string Language, string Date,
                string ObjectRef, string Outcome, string? ContentClass, long Articles)>();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt64(8)));
            }

            return Array.AsReadOnly(rows
                .GroupBy(static row => (row.Work, row.Celex, row.Expression, row.Language))
                .Select(static group => new EuropeIndexWorkExpression(
                    group.Key.Work,
                    group.Key.Celex,
                    group.Key.Expression,
                    group.Key.Language,
                    Array.AsReadOnly(group.Select(static row => row.Date).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                    group.Sum(static row => row.Articles),
                    Array.AsReadOnly(group
                        .Select(static row => new EuropeIndexExpressionMember(row.ObjectRef, row.Outcome, row.ContentClass))
                        .Distinct()
                        .OrderBy(static member => member.ObjectRefSha256, StringComparer.Ordinal)
                        .ToArray())))
                .ToArray());
        }
    }

    /// <summary>The languages the capability manifest measured searchable text in.</summary>
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
    /// The articles of one held expression, in one language, whose searchable text contains every one of
    /// <paramref name="needles"/> as a byte-exact substring, in the publisher's article id and article
    /// identity order. Nothing is ranked, folded or dated here. The result is null when the capability
    /// manifest measured no searchable text in that language, which is the index saying it cannot answer,
    /// as distinct from an empty list, which is no hit.
    /// </summary>
    public IReadOnlyList<EuropeIndexSearchHit>? SearchExpressionArticles(
        string language, IReadOnlyList<string> needles, string publisherExpressionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherExpressionId);
        ArgumentNullException.ThrowIfNull(needles);
        if (needles.Count == 0 || needles.Any(static needle => string.IsNullOrEmpty(needle)))
        {
            throw new ArgumentException("A search needs at least one non-empty needle.", nameof(needles));
        }

        if (!SearchableLanguages().Contains(language, StringComparer.Ordinal))
        {
            return null;
        }

        // Its own read-only connection on the reader's private, verified copy, as the Luxembourg reader's
        // search does: a scan must not hold the shared connection every other operation waits on.
        using var connection = EuropeIndexBuilder.Open(_path, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT publisher_work_id,publisher_work_celex,publisher_expression_id,publisher_identifier,heading," +
            "wording_date,language,article_identity_sha256 FROM articles " +
            "WHERE language=$language AND publisher_expression_id=$expression AND publisher_work_celex IS NOT NULL" +
            string.Concat(needles.Select(static (_, index) => $" AND instr(searchable_text,$needle{index})>0")) +
            " ORDER BY publisher_identifier,article_identity_sha256";
        command.Parameters.AddWithValue("$language", language);
        command.Parameters.AddWithValue("$expression", publisherExpressionId);
        for (var index = 0; index < needles.Count; index++)
        {
            command.Parameters.AddWithValue($"$needle{index}", needles[index]);
        }

        using var reader = command.ExecuteReader();
        var values = new List<EuropeIndexSearchHit>();
        while (reader.Read())
        {
            values.Add(new EuropeIndexSearchHit(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                QualifiedProvisionIdentifierOf(reader.GetString(2), reader.GetString(3))));
        }

        return Array.AsReadOnly(values.ToArray());
    }

    public void Dispose()
    {
        _connection.Dispose();
        EuropeIndexBuilder.DeleteDatabase(_path);
    }

    private long Count(string table)
    {
        lock (_gate)
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

    private static EuropeIndexBuilder.ArticleDigestRow[] ReadArticleDigests(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256,source_entry_sha256,text_sha256 FROM article_digests ORDER BY article_identity_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<EuropeIndexBuilder.ArticleDigestRow>();
        while (reader.Read()) values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return values.ToArray();
    }

    private static EuropeIndexBuilder.ArticleSourceRow[] ReadArticleSources(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256,package_sha256,official_source_uri FROM article_sources ORDER BY article_identity_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<EuropeIndexBuilder.ArticleSourceRow>();
        while (reader.Read()) values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return values.ToArray();
    }

    private static EuropeIndexBuilder.ArticleRow[] ReadArticles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,publisher_work_id,publisher_work_celex,publisher_expression_id,package_entry,publisher_identifier,heading,wording_date,language,searchable_text,tokens_json FROM articles ORDER BY article_identity_sha256";
        using var reader = command.ExecuteReader();
        var values = new List<EuropeIndexBuilder.ArticleRow>();
        while (reader.Read()) values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
            reader.GetString(11)));
        return values.ToArray();
    }
}
