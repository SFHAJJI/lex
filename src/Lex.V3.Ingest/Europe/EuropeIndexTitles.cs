using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Europe;

/// <summary>The sources an EU expression's title is held from (<see cref="EuropeIndexExpressionTitle.Source"/>).</summary>
public static class EuropeTitleSources
{
    /// <summary>The publisher's <c>cdm:expression_title</c> of the expression, a literal of family X.</summary>
    public const string CellarExpressionTitle = "cellar_expression_title";

    /// <summary>The publisher's <c>cdm:expression_title_short</c> of the expression, a literal of family X.</summary>
    public const string CellarExpressionTitleShort = "cellar_expression_title_short";

    /// <summary>The <c>TITLE</c> of the expression's admitted Formex main unit.</summary>
    public const string FormexTitle = "formex_title";

    public static IReadOnlyList<string> InOrder { get; } =
        Array.AsReadOnly(new[] { CellarExpressionTitle, CellarExpressionTitleShort, FormexTitle });

    /// <summary>The kind of title a source holds: a short title from the short-title predicate, else the title.</summary>
    public static string KindOf(string source) => source == CellarExpressionTitleShort ? EuropeTitleKinds.ShortTitle : EuropeTitleKinds.Title;
}

/// <summary>The kinds of an EU expression's title.</summary>
public static class EuropeTitleKinds
{
    public const string Title = "title";
    public const string ShortTitle = "short_title";
}

/// <summary>
/// One title of one English or French EU expression as the EU index /7 holds it: the words (<see cref="TitleText"/>: the
/// literal with its white space made single spaces, or a Formex title's words with its markup and notes removed,
/// <see cref="EuFormexPublicationReader.TitleTextRule"/>) and the raw form they were read from (<see cref="RawValue"/>: the
/// literal exactly as served, with its language tag or datatype, or the <c>TITLE</c> element's markup), and its source with
/// the coordinate it came from (a family-X batch's interpretation profile, or the package entry and the package's digest).
/// A publisher's title: never a date, never chosen between.
/// </summary>
public sealed record EuropeIndexExpressionTitle(
    string TitleIdentitySha256,
    string PublisherWorkIri,
    string PublisherExpressionIri,
    string Language,
    string TitleKind,
    string Source,
    string TitleText,
    string RawValue,
    string? RawLanguageTag,
    string? RawDatatypeIri,
    string SourceReference,
    string SourceSha256);

/// <summary>
/// One Official Journal reference of one English or French EU expression's wording as the EU index /7 holds it, read from its
/// admitted Formex package (<see cref="EuFormexOfficialJournalReference"/>): the kind (the act's publication, or the
/// publication of the act a consolidated version consolidates), the number form, the series, issue or act number, year, first
/// page and publication date as the package states them, the raw elements they were read from, and the package entry and
/// digest. Lex composes no citation here.
/// </summary>
public sealed record EuropeIndexOfficialJournalReference(
    string ReferenceIdentitySha256,
    string PublisherWorkIri,
    string PublisherExpressionIri,
    string Language,
    string Kind,
    string NumberForm,
    string Series,
    string? OjNumber,
    string? ActNumber,
    string Year,
    string? FirstPage,
    string? PublicationDate,
    string ReferenceXml,
    string? PublicationXml,
    string PackageEntry,
    string PackageSha256);

public static partial class EuropeIndexBuilder
{
    internal const string ForceDatesSchema = "lex-v3-europe-index/6";

    private const string TitlesDdl = """

        CREATE TABLE expression_titles (
          title_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(title_identity_sha256) = 64),
          publisher_work_iri TEXT COLLATE BINARY NOT NULL,
          publisher_expression_iri TEXT COLLATE BINARY NOT NULL,
          language TEXT COLLATE BINARY NOT NULL CHECK (language IN ('eng','fra')),
          title_kind TEXT COLLATE BINARY NOT NULL CHECK (title_kind IN ('title','short_title')),
          source TEXT COLLATE BINARY NOT NULL CHECK (source IN ('cellar_expression_title','cellar_expression_title_short','formex_title')),
          title_text TEXT COLLATE BINARY NOT NULL CHECK (length(title_text) > 0),
          raw_value TEXT COLLATE BINARY NOT NULL,
          raw_language_tag TEXT COLLATE BINARY,
          raw_datatype_iri TEXT COLLATE BINARY,
          source_reference TEXT COLLATE BINARY NOT NULL,
          source_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(source_sha256) = 64)
        ) STRICT;
        CREATE INDEX expression_titles_expression ON expression_titles(publisher_expression_iri, source, title_kind);
        CREATE TABLE official_journal_references (
          reference_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(reference_identity_sha256) = 64),
          publisher_work_iri TEXT COLLATE BINARY NOT NULL,
          publisher_expression_iri TEXT COLLATE BINARY NOT NULL,
          language TEXT COLLATE BINARY NOT NULL CHECK (language IN ('eng','fra')),
          kind TEXT COLLATE BINARY NOT NULL CHECK (kind IN ('act_publication','consolidated_act_publication')),
          number_form TEXT COLLATE BINARY NOT NULL CHECK (number_form IN ('series_number','act_number')),
          series TEXT COLLATE BINARY NOT NULL,
          oj_number TEXT COLLATE BINARY,
          act_number TEXT COLLATE BINARY,
          year TEXT COLLATE BINARY NOT NULL CHECK (length(year) = 4),
          first_page TEXT COLLATE BINARY,
          publication_date TEXT COLLATE BINARY,
          reference_xml TEXT COLLATE BINARY NOT NULL,
          publication_xml TEXT COLLATE BINARY,
          package_entry TEXT COLLATE BINARY NOT NULL,
          package_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(package_sha256) = 64)
        ) STRICT;
        CREATE INDEX official_journal_references_expression ON official_journal_references(publisher_expression_iri, kind);
        """;

    /// <summary>One Formex main body the index projects articles from: its expression, work, language and the package it was read from.</summary>
    internal sealed record IndexedPublication(
        string PublisherWorkIri, string PublisherExpressionIri, string Language, string PackageSha256, EuFormexPublication Publication);

    /// <summary>The language token of an authority language IRI a title is held for: eng or fra.</summary>
    private static string TitleLanguageOf(string languageIri) => languageIri switch
    {
        "http://publications.europa.eu/resource/authority/language/ENG" => LanguageToken("ENG"),
        "http://publications.europa.eu/resource/authority/language/FRA" => LanguageToken("FRA"),
        _ => throw new InvalidDataException("An EU expression title is outside the accepted English/French scope."),
    };

    /// <summary>A literal's words: the literal with every run of white space one space, and none at either end.</summary>
    internal static string LiteralTitleText(string raw) => string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// A title's identity: its expression, source and raw form, and for a package's title the package entry and digest it was
    /// read from. A literal's family-X batch is not part of it: that coordinate is minted per acquisition, so it stays in the
    /// index (<see cref="EuropeIndexExpressionTitle.SourceReference"/>) and two acquisitions of one title answer alike, as a
    /// force date's identity names its statement and not the batch it came in.
    /// </summary>
    private static string TitleIdentity(string expression, string source, string raw, string? tag, string? datatype, string reference, string sha256) =>
        Identity("lex-v3-eu-index-expression-title/1", source == EuropeTitleSources.FormexTitle
            ? new[] { expression, source, raw, tag ?? "", datatype ?? "", reference, sha256 }
            : new[] { expression, source, raw, tag ?? "", datatype ?? "" });

    private static string ReferenceIdentity(EuropeIndexOfficialJournalReference row) =>
        Identity("lex-v3-eu-index-official-journal-reference/1",
            [row.PublisherExpressionIri, row.Kind, row.ReferenceXml, row.PublicationXml ?? "", row.PackageEntry, row.PackageSha256]);

    /// <summary>
    /// The titles of the census's English and French expressions (family X's title literals, on a census work) and of every
    /// admitted Formex main body the index projects (its <c>TITLE</c>), one row per identity in identity order. A literal whose
    /// words are empty is not a title and is not held; a literal two batches delivered alike is one row, kept with the first
    /// batch's coordinate in ordinal order.
    /// </summary>
    internal static EuropeIndexExpressionTitle[] ProjectTitles(
        IReadOnlyList<EuObservedExpressionTitle> observed,
        IReadOnlyList<EuropeIndexState> states,
        IReadOnlyList<IndexedPublication> publications)
    {
        var works = states.Select(static state => state.PublisherWorkIri).ToHashSet(StringComparer.Ordinal);
        var rows = new Dictionary<string, EuropeIndexExpressionTitle>(StringComparer.Ordinal);
        void Add(EuropeIndexExpressionTitle row)
        {
            if (rows.TryGetValue(row.TitleIdentitySha256, out var seen))
            {
                if (seen with { SourceReference = row.SourceReference, SourceSha256 = row.SourceSha256 } != row)
                    throw new InvalidDataException("Two EU expression titles share an identity and differ.");
                if (string.CompareOrdinal(row.SourceReference + row.SourceSha256, seen.SourceReference + seen.SourceSha256) < 0)
                    rows[row.TitleIdentitySha256] = row;
                return;
            }

            rows.Add(row.TitleIdentitySha256, row);
        }

        var shortTitle = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionTitleShort);
        foreach (var title in observed)
        {
            if (!works.Contains(title.PublisherWorkIri)) continue;
            var text = LiteralTitleText(title.RawLexical);
            if (text.Length == 0) continue;
            var source = title.PredicateIri == shortTitle ? EuropeTitleSources.CellarExpressionTitleShort : EuropeTitleSources.CellarExpressionTitle;
            var reference = title.InterpretationProfileRef.ResourceId;
            var sha256 = title.InterpretationProfileRef.Sha256;
            Add(new EuropeIndexExpressionTitle(
                TitleIdentity(title.PublisherExpressionIri, source, title.RawLexical, title.RawLanguageTag, title.RawDatatypeIri, reference, sha256),
                title.PublisherWorkIri, title.PublisherExpressionIri, TitleLanguageOf(title.LanguageIri), EuropeTitleSources.KindOf(source),
                source, text, title.RawLexical, title.RawLanguageTag, title.RawDatatypeIri, reference, sha256));
        }

        foreach (var indexed in publications)
        {
            if (indexed.Publication is not { TitleXml: { } xml, TitleText: { } text }) continue;
            Add(new EuropeIndexExpressionTitle(
                TitleIdentity(indexed.PublisherExpressionIri, EuropeTitleSources.FormexTitle, xml, null, null, indexed.Publication.PackageEntry, indexed.PackageSha256),
                indexed.PublisherWorkIri, indexed.PublisherExpressionIri, indexed.Language, EuropeTitleKinds.Title, EuropeTitleSources.FormexTitle,
                text, xml, null, null, indexed.Publication.PackageEntry, indexed.PackageSha256));
        }

        return rows.Values.OrderBy(static row => row.TitleIdentitySha256, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The Official Journal reference of every admitted Formex main body the index projects that states one, in identity order.</summary>
    internal static EuropeIndexOfficialJournalReference[] ProjectOfficialJournalReferences(IReadOnlyList<IndexedPublication> publications)
    {
        var rows = new Dictionary<string, EuropeIndexOfficialJournalReference>(StringComparer.Ordinal);
        foreach (var indexed in publications)
        {
            if (indexed.Publication.Reference is not { } reference) continue;
            var row = new EuropeIndexOfficialJournalReference(string.Empty, indexed.PublisherWorkIri, indexed.PublisherExpressionIri,
                indexed.Language, reference.Kind, reference.NumberForm, reference.Series, reference.OjNumber, reference.ActNumber, reference.Year,
                reference.FirstPage, reference.PublicationDate, reference.ReferenceXml, reference.PublicationXml, indexed.Publication.PackageEntry,
                indexed.PackageSha256);
            row = row with { ReferenceIdentitySha256 = ReferenceIdentity(row) };
            if (rows.TryGetValue(row.ReferenceIdentitySha256, out var seen))
            {
                if (seen != row) throw new InvalidDataException("Two EU Official Journal references share an identity and differ.");
                continue;
            }

            rows.Add(row.ReferenceIdentitySha256, row);
        }

        return rows.Values.OrderBy(static row => row.ReferenceIdentitySha256, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Reopens every stored title and reference as the build made it: each identity is its fields', each title's words are read
    /// again from its raw form (a literal's, or a Formex title's markup), each reference's fields are read again from its raw
    /// elements, a literal's work is a census work, a Formex row's expression is one the index holds articles of, and the rows
    /// are in identity order, once each.
    /// </summary>
    internal static void ValidateTitleEvidence(
        IReadOnlyList<EuropeIndexExpressionTitle> titles,
        IReadOnlyList<EuropeIndexOfficialJournalReference> references,
        IReadOnlyList<EuropeIndexState> states,
        Func<string, bool> holdsArticlesOf)
    {
        var works = states.Select(static state => state.PublisherWorkIri).ToHashSet(StringComparer.Ordinal);
        foreach (var row in titles)
        {
            var formex = row.Source == EuropeTitleSources.FormexTitle;
            var text = formex ? EuFormexPublicationReader.TitleTextOfXml(row.RawValue) : LiteralTitleText(row.RawValue);
            if (!EuropeTitleSources.InOrder.Contains(row.Source, StringComparer.Ordinal) ||
                row.TitleKind != EuropeTitleSources.KindOf(row.Source) ||
                text != row.TitleText ||
                row.TitleIdentitySha256 != TitleIdentity(row.PublisherExpressionIri, row.Source, row.RawValue, row.RawLanguageTag,
                    row.RawDatatypeIri, row.SourceReference, row.SourceSha256) ||
                (formex ? row.RawLanguageTag is not null || row.RawDatatypeIri is not null || !holdsArticlesOf(row.PublisherExpressionIri)
                    : !works.Contains(row.PublisherWorkIri)) ||
                row.Language is not ("eng" or "fra") || !IsLowerSha256(row.SourceSha256))
                throw new InvalidDataException("An EU expression title disagrees with the raw form or the source it was read from.");
        }

        foreach (var row in references)
        {
            var read = EuFormexPublicationReader.ReferenceOfXml(row.Kind, row.ReferenceXml, row.PublicationXml);
            if (read is null || read.Kind != row.Kind || read.NumberForm != row.NumberForm || read.Series != row.Series ||
                read.OjNumber != row.OjNumber || read.ActNumber != row.ActNumber || read.Year != row.Year || read.FirstPage != row.FirstPage ||
                read.PublicationDate != row.PublicationDate || read.PublicationXml != row.PublicationXml ||
                row.ReferenceIdentitySha256 != ReferenceIdentity(row) || !holdsArticlesOf(row.PublisherExpressionIri) ||
                row.Language is not ("eng" or "fra") || !IsLowerSha256(row.PackageSha256))
                throw new InvalidDataException("An EU Official Journal reference disagrees with the elements it was read from.");
        }

        static bool InIdentityOrder<T>(IReadOnlyList<T> rows, Func<T, string> identity) =>
            rows.Select(identity).Distinct(StringComparer.Ordinal).Count() == rows.Count &&
            rows.Select(identity).SequenceEqual(rows.Select(identity).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        if (!InIdentityOrder(titles, static row => row.TitleIdentitySha256) ||
            !InIdentityOrder(references, static row => row.ReferenceIdentitySha256))
            throw new InvalidDataException("EU titles or Official Journal references repeat or are out of identity order.");
    }

    private static bool IsLowerSha256(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);

    private static void InsertTitles(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<EuropeIndexExpressionTitle> titles,
        IReadOnlyList<EuropeIndexOfficialJournalReference> references)
    {
        foreach (var row in titles)
            Insert(connection, transaction, "INSERT INTO expression_titles VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11)",
                row.TitleIdentitySha256, row.PublisherWorkIri, row.PublisherExpressionIri, row.Language, row.TitleKind, row.Source,
                row.TitleText, row.RawValue, row.RawLanguageTag, row.RawDatatypeIri, row.SourceReference, row.SourceSha256);
        foreach (var row in references)
            Insert(connection, transaction,
                "INSERT INTO official_journal_references VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13,$p14,$p15)",
                row.ReferenceIdentitySha256, row.PublisherWorkIri, row.PublisherExpressionIri, row.Language, row.Kind, row.NumberForm,
                row.Series, row.OjNumber, row.ActNumber, row.Year, row.FirstPage, row.PublicationDate, row.ReferenceXml, row.PublicationXml,
                row.PackageEntry, row.PackageSha256);
    }

    private sealed record LogicalRowsWithTitles(IReadOnlyList<MemberRow> Members,
        IReadOnlyList<CorrigendumLineRow> Lines, IReadOnlyList<CorrigendumGapRow> Gaps,
        IReadOnlyList<ArticleRow> Articles, IReadOnlyList<ArticleSourceRow> Sources,
        IReadOnlyList<ArticleDigestRow> Digests, IReadOnlyList<EuropeIndexState> States,
        IReadOnlyList<EuropeIndexForceDate> ForceDates, IReadOnlyList<EuropeIndexExpressionTitle> Titles,
        IReadOnlyList<EuropeIndexOfficialJournalReference> OfficialJournalReferences);
}

/// <summary>One EU act a title search found: the title row that matched it.</summary>
public sealed record EuropeIndexTitleMatch(EuropeIndexExpressionTitle Title, bool WholeTitle);

public sealed partial class EuropeIndexReader
{
    /// <summary>Whether this index holds the expressions' titles and Official Journal references (schema 7); an index /6 or older holds none.</summary>
    public bool HasTitles { get; private set; }

    private EuropeIndexExpressionTitle[] _titles = [];
    private EuropeIndexOfficialJournalReference[] _references = [];
    private (EuropeIndexExpressionTitle Title, string Folded)[]? _foldedTitles;

    /// <summary>The verified rows of an index /7, kept from the open (they are small: a title and a reference per expression).</summary>
    private void HoldTitles(EuropeIndexExpressionTitle[] titles, EuropeIndexOfficialJournalReference[] references)
    {
        HasTitles = true;
        _titles = titles;
        _references = references;
    }

    /// <summary>
    /// The titles the index holds of the given expressions, in expression order, then source order
    /// (<see cref="EuropeTitleSources.InOrder"/>), then identity. Throws on an index that holds no title table.
    /// </summary>
    public IReadOnlyList<EuropeIndexExpressionTitle> ReadTitles(IEnumerable<string> publisherExpressionIris)
    {
        ArgumentNullException.ThrowIfNull(publisherExpressionIris);
        if (!HasTitles) throw new InvalidOperationException("This historical EU index has no title table.");
        var wanted = publisherExpressionIris.ToHashSet(StringComparer.Ordinal);
        return Array.AsReadOnly(_titles.Where(row => wanted.Contains(row.PublisherExpressionIri))
            .OrderBy(static row => row.PublisherExpressionIri, StringComparer.Ordinal)
            .ThenBy(static row => EuropeTitleSources.InOrder.ToList().IndexOf(row.Source))
            .ThenBy(static row => row.TitleIdentitySha256, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>
    /// The Official Journal references the index holds of the given expressions, in expression order, then kind, then identity.
    /// Throws on an index that holds no reference table.
    /// </summary>
    public IReadOnlyList<EuropeIndexOfficialJournalReference> ReadOfficialJournalReferences(IEnumerable<string> publisherExpressionIris)
    {
        ArgumentNullException.ThrowIfNull(publisherExpressionIris);
        if (!HasTitles) throw new InvalidOperationException("This historical EU index has no Official Journal reference table.");
        var wanted = publisherExpressionIris.ToHashSet(StringComparer.Ordinal);
        return Array.AsReadOnly(_references.Where(row => wanted.Contains(row.PublisherExpressionIri))
            .OrderBy(static row => row.PublisherExpressionIri, StringComparer.Ordinal)
            .ThenBy(static row => row.Kind, StringComparer.Ordinal)
            .ThenBy(static row => row.ReferenceIdentitySha256, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>
    /// The titles whose words hold <paramref name="query"/> once both are folded alike (<see cref="TextFolding"/>, as full-text
    /// search folds them): every title of every source, in identity order, each with whether the query is its whole title. Empty
    /// for a query that folds to nothing to match. Throws on an index that holds no title table.
    /// </summary>
    public IReadOnlyList<EuropeIndexTitleMatch> SearchTitles(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!HasTitles) throw new InvalidOperationException("This historical EU index has no title table.");
        var needle = TextFolding.Fold(query.Trim());
        if (!TextFolding.CanMatch(needle)) return [];
        lock (_gate)
        {
            _foldedTitles ??= _titles.Select(static row => (Title: row, Folded: TextFolding.Fold(row.TitleText))).ToArray();
        }

        return Array.AsReadOnly(_foldedTitles
            .Where(entry => entry.Folded.Contains(needle, StringComparison.Ordinal))
            .Select(entry => new EuropeIndexTitleMatch(entry.Title, string.Equals(entry.Folded, needle, StringComparison.Ordinal)))
            .ToArray());
    }

    private static EuropeIndexExpressionTitle[] ReadTitleRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT title_identity_sha256,publisher_work_iri,publisher_expression_iri,language,title_kind,source,title_text," +
            "raw_value,raw_language_tag,raw_datatype_iri,source_reference,source_sha256 FROM expression_titles ORDER BY title_identity_sha256 COLLATE BINARY";
        using var reader = command.ExecuteReader();
        var rows = new List<EuropeIndexExpressionTitle>();
        while (reader.Read())
        {
            string? Optional(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
            rows.Add(new EuropeIndexExpressionTitle(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), Optional(8), Optional(9),
                reader.GetString(10), reader.GetString(11)));
        }

        return rows.ToArray();
    }

    private static EuropeIndexOfficialJournalReference[] ReadReferenceRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT reference_identity_sha256,publisher_work_iri,publisher_expression_iri,language,kind,number_form,series," +
            "oj_number,act_number,year,first_page,publication_date,reference_xml,publication_xml,package_entry,package_sha256 " +
            "FROM official_journal_references ORDER BY reference_identity_sha256 COLLATE BINARY";
        using var reader = command.ExecuteReader();
        var rows = new List<EuropeIndexOfficialJournalReference>();
        while (reader.Read())
        {
            string? Optional(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
            rows.Add(new EuropeIndexOfficialJournalReference(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), Optional(7), Optional(8), reader.GetString(9), Optional(10),
                Optional(11), reader.GetString(12), Optional(13), reader.GetString(14), reader.GetString(15)));
        }

        return rows.ToArray();
    }

    internal static EuropeIndexExpressionTitle[] ReadTitleRowsOf(SqliteConnection connection) => ReadTitleRows(connection);

    internal static EuropeIndexOfficialJournalReference[] ReadReferenceRowsOf(SqliteConnection connection) => ReadReferenceRows(connection);
}
