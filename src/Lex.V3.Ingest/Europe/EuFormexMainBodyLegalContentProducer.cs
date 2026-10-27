using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest.Europe;

public enum EuFormexMainBodyLegalContentDisposition
{
    [JsonStringEnumMemberName("admitted")] Admitted = 1,
    [JsonStringEnumMemberName("not_eligible")] NotEligible = 2,
    [JsonStringEnumMemberName("package_unavailable")] PackageUnavailable = 3,
    [JsonStringEnumMemberName("package_refused")] PackageRefused = 4,
    [JsonStringEnumMemberName("retained_bytes_unavailable")] RetainedBytesUnavailable = 5,
    [JsonStringEnumMemberName("package_unreadable")] PackageUnreadable = 6,
    [JsonStringEnumMemberName("xml_rejected")] XmlRejected = 7,
    [JsonStringEnumMemberName("main_body_missing")] MainBodyMissing = 8,
    [JsonStringEnumMemberName("unsupported_content_shape")] UnsupportedContentShape = 9,
    [JsonStringEnumMemberName("package_not_acquired")] PackageNotAcquired = 10,
    [JsonStringEnumMemberName("route_refused")] RouteRefused = 11,
    [JsonStringEnumMemberName("package_rejected")] PackageRejected = 12,

    /// <summary>
    /// The main body of an admitted package whose annex chain did not close: its articles are read as an acquired package's
    /// are, and its annexes stay unclassified for the package outcome's stated reason (never merged into the main body).
    /// </summary>
    [JsonStringEnumMemberName("admitted_annexes_unclassified")] AdmittedAnnexesUnclassified = 13,
}

public enum EuFormexMainBodyTokenKind
{
    [JsonStringEnumMemberName("text")] Text = 1,
    [JsonStringEnumMemberName("reference")] Reference = 2,
    [JsonStringEnumMemberName("footnote")] Footnote = 3,
}

public sealed record EuFormexMainBodyToken(
    EuFormexMainBodyTokenKind Kind,
    string Text,
    string? Target,
    IReadOnlyList<EuFormexMainBodyToken>? NoteBody = null);

public sealed class EuFormexMainBodyArticle
{
    internal EuFormexMainBodyArticle(
        string publisherExpressionId,
        string packageEntry,
        string publisherIdentifier,
        string heading,
        string language,
        string publisherDate,
        string searchableText,
        IReadOnlyList<EuFormexMainBodyToken> tokens,
        string sourceEntrySha256)
    {
        PublisherExpressionId = publisherExpressionId;
        PackageEntry = packageEntry;
        PublisherIdentifier = publisherIdentifier;
        Heading = heading;
        Language = language;
        PublisherDate = publisherDate;
        SearchableText = searchableText;
        SourceEntrySha256 = sourceEntrySha256;
        TextSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(searchableText)));
        Tokens = Array.AsReadOnly(tokens.ToArray());
        IdentitySha256 = IdentityOf(this);
    }

    public string PublisherExpressionId { get; }
    public string PackageEntry { get; }
    public string PublisherIdentifier { get; }
    public string Heading { get; }
    public string Language { get; }
    public string PublisherDate { get; }
    public string SearchableText { get; }
    public IReadOnlyList<EuFormexMainBodyToken> Tokens { get; }
    /// <summary>SHA-256 of the exact decompressed publisher XML entry, including its encoding,
    /// BOM and comments. This identifies an entry inside the separately pinned ZIP package.</summary>
    public string SourceEntrySha256 { get; }
    /// <summary>SHA-256 of SearchableText encoded as UTF-8 without a BOM or added newline.
    /// This is the emitted article text representation; it is not a hash of token JSON or XML.</summary>
    public string TextSha256 { get; }
    /// <summary>Existing semantic article identity. Source byte digests are separate coordinates.</summary>
    public string IdentitySha256 { get; }

    private static string IdentityOf(EuFormexMainBodyArticle article)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-eu-formex-main-body-article/2");
        Append(hash, article.PublisherExpressionId);
        Append(hash, article.PackageEntry);
        Append(hash, article.PublisherIdentifier);
        Append(hash, article.Heading);
        Append(hash, article.Language);
        Append(hash, article.PublisherDate);
        Append(hash, article.SearchableText);
        foreach (var token in article.Tokens) AppendToken(hash, token);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendToken(IncrementalHash hash, EuFormexMainBodyToken token)
    {
        Append(hash, ((int)token.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, token.Text);
        Append(hash, token.Target ?? "");
        Append(hash, (token.NoteBody?.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (token.NoteBody is null) return;
        foreach (var nested in token.NoteBody) AppendToken(hash, nested);
    }

    internal static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

/// <summary>What a part of an admitted main body's document that is not served as text is (review of #996, F2).</summary>
public enum EuFormexMainBodyPartKind
{
    /// <summary>An annex unit of an admitted package whose annex chain did not close: not classified against the PDF.</summary>
    [JsonStringEnumMemberName("annex_unclassified")] AnnexUnclassified = 1,

    /// <summary>A Protocol, Annex, Declaration or table of equivalences published with a consolidated Treaty, which is not one of the Treaty's articles.</summary>
    [JsonStringEnumMemberName("treaty_part_not_served")] TreatyPartNotServed = 2,
}

/// <summary>
/// One part of an admitted main body's package that is not served as text, typed and counted so that no reader takes the
/// act for one without it: its identity (the kind, the expression, the package entry, the part's place and its own text's
/// digest), its kind and the publisher's title for it.
/// </summary>
public sealed record EuFormexMainBodyPartNotServed(string IdentitySha256, EuFormexMainBodyPartKind Kind, string Title);

public sealed class EuFormexMainBodyLegalContentOutcome
{
    internal EuFormexMainBodyLegalContentOutcome(
        EuFormexPackageOutcome source,
        EuFormexMainBodyLegalContentDisposition disposition,
        IReadOnlyList<EuFormexMainBodyArticle> articles,
        string? detail,
        IReadOnlyList<EuFormexMainBodyPartNotServed>? partsNotServed = null)
    {
        Source = source;
        Disposition = disposition;
        Articles = Array.AsReadOnly(articles.ToArray());
        Detail = detail;
        PartsNotServed = Array.AsReadOnly((partsNotServed ?? []).ToArray());
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        EuFormexMainBodyArticle.Append(hash, "lex-v3-eu-formex-main-body-outcome/1");
        EuFormexMainBodyArticle.Append(hash, EuFormexMainBodyLegalContentProducer.Profile);
        EuFormexMainBodyArticle.Append(hash, source.ExpressionIdentity.PublisherWorkId);
        EuFormexMainBodyArticle.Append(hash, source.ExpressionIdentity.PublisherExpressionId);
        EuFormexMainBodyArticle.Append(hash, ((int)disposition).ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var article in Articles) EuFormexMainBodyArticle.Append(hash, article.IdentitySha256);
        EuFormexMainBodyArticle.Append(hash, detail ?? "");
        foreach (var part in PartsNotServed)
        {
            EuFormexMainBodyArticle.Append(hash, ((int)part.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
            EuFormexMainBodyArticle.Append(hash, part.IdentitySha256);
        }
        SemanticIdentitySha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public EuFormexPackageOutcome Source { get; }

    /// <summary>The parts of the admitted package that are not served as text, each typed; empty for every other outcome.</summary>
    public IReadOnlyList<EuFormexMainBodyPartNotServed> PartsNotServed { get; }
    public EuFormexMainBodyLegalContentDisposition Disposition { get; }
    public IReadOnlyList<EuFormexMainBodyArticle> Articles { get; }
    public string? Detail { get; }
    public string SemanticIdentitySha256 { get; }
}

public sealed class EuFormexMainBodyLegalContentPopulation
{
    internal EuFormexMainBodyLegalContentPopulation(
        EuFormexRunOutcomeReconciliation formex,
        IReadOnlyList<EuFormexMainBodyLegalContentOutcome> outcomes)
    {
        Formex = formex;
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
    }

    public EuFormexRunOutcomeReconciliation Formex { get; }
    public IReadOnlyList<EuFormexMainBodyLegalContentOutcome> Outcomes { get; }
}

/// <summary>Reopens acquired Formex packages and derives ordered main-body ARTICLE content.</summary>
public sealed class EuFormexMainBodyLegalContentProducer
{
    /// <summary>
    /// Profile /5 reads two main-body roots /4 did not, both measured over EU run 16's retained packages (2026-10-27):
    /// <c>CONS.OTH</c>, the consolidated Treaties (TEU and TFEU, 48 retained packages that /4 typed
    /// <c>main_body_missing</c>), whose operative text is the one <c>ENACTING.TERMS</c> reached through
    /// <c>CONS.DOC.GEN</c> parts only (the Protocols, Annexes and Declarations sit under <c>CONTENTS</c> and stay out, as
    /// annexes do); and <c>GENERAL</c>, the Charter of Fundamental Rights (4 packages), read only when the package holds no
    /// act root and exactly one <c>GENERAL</c> unit carries an <c>ENACTING.TERMS</c> (a cover or contents page carries none).
    /// </summary>
    public const string Profile =
        "lex-v3-eu-formex-main-body-profile/5;root=ACT,CONS.ACT,CONS.OTH,GENERAL-without-act;units=ARTICLE;" +
        "tokens=text,reference,footnote-with-body;oj-reference-target=publisher-attributes;" +
        "exclude=recitals,final,annex,protocols,declarations;consolidated=one-cons-doc,one-bib-instance,one-enacting-terms;" +
        "consolidated-other=one-cons-oth-doc,one-bib-instance,one-enacting-terms-through-cons-doc-gen;" +
        "general=only-without-act-root,one-unit-with-enacting-terms,one-enacting-terms;" +
        "language=matches-expression-with-en-eng-fr-fra-authority-aliases";
    public static string ProfileSha256 { get; } = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(Profile)));

    private const int MaxEntries = 4_096;
    private const long MaxXmlEntryBytes = 16 * 1024 * 1024;
    private const long MaxXmlPackageBytes = 64 * 1024 * 1024;
    private readonly ICustodyStore _custodyStore;

    public EuFormexMainBodyLegalContentProducer(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    public async Task<EuFormexMainBodyLegalContentPopulation> RunAsync(
        EuFormexRunOutcomeReconciliation formex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formex);
        var outcomes = new List<EuFormexMainBodyLegalContentOutcome>(formex.Outcomes.Count);
        foreach (var source in formex.Outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // An admitted package whose annex chain did not close is read for its main body; every other outcome that is
            // not an acquisition keeps its own typed disposition.
            var annexChainOpen = source.Kind == EuFormexPackageOutcomeKind.NotAcquired && source.RetainedInventory is not null;
            if (source.Kind != EuFormexPackageOutcomeKind.Acquired && !annexChainOpen)
            {
                outcomes.Add(new(source, Map(source.Kind), [], source.Detail));
                continue;
            }

            var inventory = source.AcquiredInventory ?? source.RetainedInventory!;
            ReadOnlyMemory<byte> bytes;
            try
            {
                bytes = await CustodyRestore.ReadCheckedAsync(
                    _custodyStore, inventory.SourceReceipt.Reference, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is CustodyRequiredException
                or CustodyIntegrityException or CustodyPolicyException)
            {
                outcomes.Add(new(source,
                    EuFormexMainBodyLegalContentDisposition.RetainedBytesUnavailable, [], exception.Message));
                continue;
            }

            var parsed = Parse(source, bytes, cancellationToken);
            if (annexChainOpen && parsed.Disposition == EuFormexMainBodyLegalContentDisposition.Admitted)
            {
                // Each annex unit the admitted inventory names stays a typed part: not classified, never text.
                var reason = ContractWireName(source.NotAcquiredReason);
                var annexes = inventory.Members.Select(member => new EuFormexMainBodyPartNotServed(
                        PartIdentity(EuFormexMainBodyPartKind.AnnexUnclassified, source.ExpressionIdentity.PublisherExpressionId,
                            member.PackageEntry, inventory.IdentitySha256, member.Sequence, reason),
                        EuFormexMainBodyPartKind.AnnexUnclassified, member.Title))
                    .ToArray();
                outcomes.Add(new(source, EuFormexMainBodyLegalContentDisposition.AdmittedAnnexesUnclassified, parsed.Articles,
                    $"the package's {inventory.Members.Count} annex member(s) are not classified: {reason}", annexes));
                continue;
            }

            outcomes.Add(new(source, parsed.Disposition, parsed.Articles, parsed.Detail, parsed.Parts));
        }
        return new EuFormexMainBodyLegalContentPopulation(formex, outcomes);
    }

    /// <summary>Whether a disposition carries the articles of a main body: an acquired package's, or one whose annexes are unclassified.</summary>
    public static bool CarriesArticles(EuFormexMainBodyLegalContentDisposition disposition) =>
        disposition is EuFormexMainBodyLegalContentDisposition.Admitted or EuFormexMainBodyLegalContentDisposition.AdmittedAnnexesUnclassified;

    private static string ContractWireName(EuFormexPackageNotAcquiredReason reason) =>
        Lex.V3.Contracts.ContractWire.NameOf(reason);

    private static EuFormexMainBodyLegalContentDisposition Map(EuFormexPackageOutcomeKind kind) => kind switch
    {
        EuFormexPackageOutcomeKind.NotEligible => EuFormexMainBodyLegalContentDisposition.NotEligible,
        EuFormexPackageOutcomeKind.Unavailable => EuFormexMainBodyLegalContentDisposition.PackageUnavailable,
        EuFormexPackageOutcomeKind.Refused => EuFormexMainBodyLegalContentDisposition.PackageRefused,
        EuFormexPackageOutcomeKind.NotEnumeratedLanguageOutOfScope => EuFormexMainBodyLegalContentDisposition.PackageNotAcquired,
        EuFormexPackageOutcomeKind.NotAcquired => EuFormexMainBodyLegalContentDisposition.PackageNotAcquired,
        EuFormexPackageOutcomeKind.RouteRefused => EuFormexMainBodyLegalContentDisposition.RouteRefused,
        EuFormexPackageOutcomeKind.PackageRejected => EuFormexMainBodyLegalContentDisposition.PackageRejected,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string NormalizeLanguage(string value) => value switch
    {
        "EN" or "ENG" or "http://publications.europa.eu/resource/authority/language/ENG" => "EN",
        "FR" or "FRA" or "http://publications.europa.eu/resource/authority/language/FRA" => "FR",
        _ => value,
    };

    private static (EuFormexMainBodyLegalContentDisposition Disposition,
        IReadOnlyList<EuFormexMainBodyArticle> Articles, string? Detail, IReadOnlyList<EuFormexMainBodyPartNotServed> Parts) Parse(
        EuFormexPackageOutcome source,
        ReadOnlyMemory<byte> packageBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(packageBytes.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxEntries)
                return Refused(EuFormexMainBodyLegalContentDisposition.PackageUnreadable,
                    "the Formex package exceeds the entry bound");

            // Pass one reads every XML entry (an invalid one refuses the package, as it always did) and notes its root; pass
            // two reads the one main-body unit again. No more than one entry's tree is held at a time.
            var names = new HashSet<string>(StringComparer.Ordinal);
            var units = new List<(ZipArchiveEntry Entry, string Root, int EnactingTerms)>();
            long totalXmlBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SafeName(entry.FullName) || !names.Add(entry.FullName))
                    return Refused(EuFormexMainBodyLegalContentDisposition.PackageUnreadable,
                        "the Formex package contains an unsafe or duplicate entry name");
                if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Length < 0 || entry.Length > MaxXmlEntryBytes ||
                    totalXmlBytes > MaxXmlPackageBytes - entry.Length)
                    return Refused(EuFormexMainBodyLegalContentDisposition.PackageUnreadable,
                        "the Formex XML payload exceeds the admitted bound");
                totalXmlBytes += entry.Length;

                var loaded = Load(entry);
                if (loaded is null)
                    return Refused(EuFormexMainBodyLegalContentDisposition.XmlRejected,
                        $"Formex XML entry {entry.FullName} is invalid");
                var root = loaded.Root?.Name.LocalName;
                if (root is "ACT" or "CONS.ACT" or "CONS.OTH" or "GENERAL")
                    units.Add((entry, root, loaded.Root!.Descendants().Count(static value => value.Name.LocalName == "ENACTING.TERMS")));
            }

            // An act root is the main body. A GENERAL unit is one only in a package that holds no act root, and only when it
            // carries an ENACTING.TERMS: the Charter's package holds its contents page as a second GENERAL unit, which
            // carries none.
            var main = units.Where(static unit => unit.Root is not "GENERAL").ToArray();
            if (main.Length == 0)
                main = units.Where(static unit => unit.Root == "GENERAL" && unit.EnactingTerms > 0).ToArray();
            if (main.Length == 0)
                return Refused(EuFormexMainBodyLegalContentDisposition.MainBodyMissing,
                    "the retained Formex package contains no ACT, CONS.ACT or CONS.OTH unit, nor a GENERAL unit with an ENACTING.TERMS");
            if (main.Length != 1)
                return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                    "the retained Formex package contains more than one ACT/CONS.ACT/CONS.OTH main-body unit");

            var (mainEntry, rootName, _) = main[0];
            var document = Load(mainEntry)
                ?? throw new InvalidDataException($"Formex XML entry {mainEntry.FullName} did not read the same twice");
            var sourceEntrySha256 = HashEntry(mainEntry, cancellationToken);
            var articleRoot = document.Root!;
            string? language;
            string? publisherDate;
            if (rootName is "CONS.ACT" or "CONS.OTH")
            {
                // Consolidated Formex wraps the operative text in CONS.DOC (an act) or CONS.OTH.DOC (another document: the
                // consolidated Treaties). Its bibliography has other dates/languages in amendment history; only the
                // document's own BIB.INSTANCE supplies these coordinates.
                var documentName = rootName == "CONS.ACT" ? "CONS.DOC" : "CONS.OTH.DOC";
                var documents = document.Root!.Elements()
                    .Where(value => value.Name.LocalName == documentName).ToArray();
                if (documents.Length != 1)
                    return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                        $"a {rootName} must contain exactly one {documentName}");
                var bibliographies = documents[0].Elements()
                    .Where(static value => value.Name.LocalName == "BIB.INSTANCE").ToArray();
                // A consolidated act's operative text is its document's own ENACTING.TERMS. A consolidated Treaty's is the
                // one ENACTING.TERMS reached through CONS.DOC.GEN parts only: the Protocols, Annexes and Declarations
                // published with it sit under CONTENTS, each with ENACTING.TERMS of its own, and are not the Treaty's articles.
                var operative = rootName == "CONS.ACT"
                    ? documents[0].Elements().Where(static value => value.Name.LocalName == "ENACTING.TERMS").ToArray()
                    : documents[0].Descendants().Where(value => value.Name.LocalName == "ENACTING.TERMS" &&
                        value.Ancestors().TakeWhile(parent => parent != documents[0])
                            .All(static parent => parent.Name.LocalName == "CONS.DOC.GEN")).ToArray();
                if (bibliographies.Length != 1 || operative.Length != 1)
                    return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                        $"a {documentName} must contain one BIB.INSTANCE and one ENACTING.TERMS");
                var languages = bibliographies[0].Elements()
                    .Where(static value => value.Name.LocalName == "LG.DOC").ToArray();
                var dates = bibliographies[0].Elements()
                    .Where(static value => value.Name.LocalName == "DATE").ToArray();
                language = languages.Length == 1 ? languages[0].Value.Trim() : null;
                // Keep the existing article bibliographic date meaning. CONSLEG.DATE
                // is a separate publisher state coordinate, never an applicability date.
                publisherDate = dates.Length == 1 ? dates[0].Attribute("ISO")?.Value : null;
                if (!DateOnly.TryParseExact(publisherDate, new[] { "yyyyMMdd", "yyyy-MM-dd" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                        $"a {documentName} bibliographic date must be one exact civil date");
                articleRoot = operative[0];
            }
            else
            {
                language = RequiredSingleValue(document.Root!, "LG.DOC");
                publisherDate = document.Root!.Descendants()
                    .FirstOrDefault(static value => value.Name.LocalName == "BIB.INSTANCE")?
                    .Elements().FirstOrDefault(static value => value.Name.LocalName == "DATE")?
                    .Attribute("ISO")?.Value;
                if (rootName == "GENERAL")
                {
                    // A GENERAL unit's operative text is its one ENACTING.TERMS; its title and preamble are not articles.
                    var operative = document.Root!.Descendants()
                        .Where(static value => value.Name.LocalName == "ENACTING.TERMS").ToArray();
                    if (operative.Length != 1)
                        return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                            "a GENERAL main-body unit must contain exactly one ENACTING.TERMS");
                    articleRoot = operative[0];
                }
            }
            if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(publisherDate))
                return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                    $"Formex main-body entry {mainEntry.FullName} lacks one language or publisher date");

            if (NormalizeLanguage(language) != NormalizeLanguage(source.Expression.OfficialLanguage))
                return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                    $"Formex main-body entry {mainEntry.FullName} language does not match its proven expression language");

            var articles = new List<EuFormexMainBodyArticle>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in articleRoot.Descendants()
                .Where(value => value.Name.LocalName == "ARTICLE" &&
                    (rootName == "ACT" || !value.Ancestors().TakeWhile(parent => parent != articleRoot)
                        .Any(static parent => parent.Name.LocalName is "ANNEX" or "NOTE" or "FT"))))
            {
                var identifier = element.Attribute("IDENTIFIER")?.Value;
                var heading = element.Elements().SingleOrDefault(static value =>
                    value.Name.LocalName == "TI.ART")?.Value;
                if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(heading))
                    return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                        $"Formex main-body entry {mainEntry.FullName} contains an article without one identifier and heading");
                var tokens = TokensOf(element);
                if (tokens.Count == 0)
                    return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                        $"Formex article {identifier} contains no legal text");
                var article = new EuFormexMainBodyArticle(
                    source.ExpressionIdentity.PublisherExpressionId,
                    mainEntry.FullName,
                    identifier,
                    heading,
                    language,
                    publisherDate,
                    SearchableTextOf(element),
                    tokens,
                    sourceEntrySha256);
                if (!identities.Add(article.IdentitySha256))
                    return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                        $"Formex package contains duplicate article identity {identifier}");
                articles.Add(article);
            }

            if (articles.Count == 0)
                return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                    "the retained Formex main body contains no ARTICLE unit");
            // A consolidated Treaty's Protocols, Annexes, Declarations and tables are left out of its articles, and are
            // typed and counted as parts not served (Art. 51 TEU makes the Protocols and Annexes integral to the Treaties).
            var parts = rootName == "CONS.OTH" ? TreatyPartsNotServed(source, mainEntry.FullName, document.Root!, articleRoot) : [];
            return (EuFormexMainBodyLegalContentDisposition.Admitted,
                Array.AsReadOnly(articles.ToArray()), null, parts);
        }
        catch (InvalidDataException exception)
        {
            return Refused(EuFormexMainBodyLegalContentDisposition.PackageUnreadable, exception.Message);
        }
    }

    /// <summary>One XML entry read under the producer's bounds, or null when it is not well-formed XML within them.</summary>
    private static XDocument? Load(ZipArchiveEntry entry)
    {
        try
        {
            using var entryStream = entry.Open();
            using var reader = XmlReader.Create(entryStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlEntryBytes,
                IgnoreComments = true,
            });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
        {
            return null;
        }
    }

    private static IReadOnlyList<EuFormexMainBodyToken> TokensOf(XElement article)
    {
        var tokens = new List<EuFormexMainBodyToken>();
        AppendTokens(article, tokens);
        return Array.AsReadOnly(tokens.ToArray());
    }

    private static string HashEntry(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        using var stream = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[8192];
        long length = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = stream.Read(buffer);
            if (count == 0) break;
            length += count;
            if (length > MaxXmlEntryBytes || length > entry.Length)
                throw new InvalidDataException("The Formex XML entry exceeds its declared byte length.");
            hash.AppendData(buffer[..count]);
        }
        if (length != entry.Length)
            throw new InvalidDataException("The Formex XML entry is shorter than its declared byte length.");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string SearchableTextOf(XElement article)
    {
        var builder = new StringBuilder();
        AppendSearchableText(article, builder);
        return string.Join(' ', builder.ToString()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void AppendSearchableText(XElement element, StringBuilder builder)
    {
        if (element.Name.LocalName is "NOTE" or "FT") return;
        var block = element.Name.LocalName is "TI.ART" or "STI.ART" or "PARAG" or "NO.PARAG"
            or "ALINEA" or "LIST" or "ITEM" or "NP" or "NO.P" or "P" or "TXT";
        if (block) AppendSearchBoundary(builder);
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                builder.Append(text.Value);
            }
            else if (node is XElement child && child.Name.LocalName is "QUOT.START" or "QUOT.END")
            {
                builder.Append(QuoteCharacter(child));
            }
            else if (node is XElement childElement)
            {
                AppendSearchableText(childElement, builder);
            }
        }
        if (block) AppendSearchBoundary(builder);
    }

    private static void AppendSearchBoundary(StringBuilder builder)
    {
        if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1])) builder.Append(' ');
    }

    private static void AppendTokens(
        XElement element,
        List<EuFormexMainBodyToken> tokens,
        bool allowFootnotes = true)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                if (!string.IsNullOrWhiteSpace(text.Value))
                    tokens.Add(new(EuFormexMainBodyTokenKind.Text, text.Value, null));
                continue;
            }
            if (node is not XElement child) continue;

            if (child.Name.LocalName is "QUOT.START" or "QUOT.END")
            {
                tokens.Add(new(EuFormexMainBodyTokenKind.Text, QuoteCharacter(child), null));
                continue;
            }
            if (child.Name.LocalName is "NOTE" or "FT")
            {
                if (!allowFootnotes)
                    throw new InvalidDataException("A Formex footnote cannot recursively contain another footnote.");
                var note = DisplayText(child);
                var noteBody = new List<EuFormexMainBodyToken>();
                AppendTokens(child, noteBody, allowFootnotes: false);
                if (!string.IsNullOrWhiteSpace(note) && noteBody.Count != 0)
                    tokens.Add(new(EuFormexMainBodyTokenKind.Footnote, note, null,
                        Array.AsReadOnly(noteBody.ToArray())));
                continue;
            }
            if (child.Name.LocalName is "REF.DOC.OJ" or "REF.DOC" or "LINK")
            {
                var reference = DisplayText(child);
                var target = ReferenceTarget(child);
                if (!string.IsNullOrWhiteSpace(reference))
                    tokens.Add(new(EuFormexMainBodyTokenKind.Reference, reference, target));
                continue;
            }
            AppendTokens(child, tokens);
        }
    }

    private static string? ReferenceTarget(XElement element)
    {
        var direct = element.Attributes().FirstOrDefault(static value =>
            value.Name.LocalName is "REF" or "HREF" or "FILE")?.Value;
        if (direct is not null || element.Name.LocalName != "REF.DOC.OJ") return direct;
        var attributes = element.Attributes()
            .OrderBy(static value => value.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(static value => value.Name.LocalName, static value => value.Value,
                StringComparer.Ordinal);
        return attributes.Count == 0 ? null : JsonSerializer.Serialize(attributes);
    }

    private static string DisplayText(XElement element)
    {
        var builder = new StringBuilder();
        foreach (var node in element.DescendantNodesAndSelf())
        {
            if (node is XText text) builder.Append(text.Value);
            else if (node is XElement marker && marker.Name.LocalName is "QUOT.START" or "QUOT.END")
                builder.Append(QuoteCharacter(marker));
        }
        return builder.ToString();
    }

    private static string QuoteCharacter(XElement element)
    {
        var code = element.Attribute("CODE")?.Value;
        if (code is null || !int.TryParse(code, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var scalar) || !Rune.IsValid(scalar))
            throw new InvalidDataException("A Formex quotation marker has no valid Unicode CODE.");
        return new Rune(scalar).ToString();
    }

    private static string? RequiredSingleValue(XElement root, string localName)
    {
        var values = root.Descendants().Where(value => value.Name.LocalName == localName)
            .Select(static value => value.Value.Trim()).Where(static value => value.Length != 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static bool SafeName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !Path.IsPathRooted(name) &&
        !name.Contains('\\', StringComparison.Ordinal) &&
        name.Split('/').All(static part => part.Length != 0 && part != "." && part != "..");

    private static (EuFormexMainBodyLegalContentDisposition Disposition,
        IReadOnlyList<EuFormexMainBodyArticle> Articles, string Detail, IReadOnlyList<EuFormexMainBodyPartNotServed> Parts) Refused(
        EuFormexMainBodyLegalContentDisposition disposition,
        string detail) => (disposition, Array.Empty<EuFormexMainBodyArticle>(), detail, Array.Empty<EuFormexMainBodyPartNotServed>());

    /// <summary>
    /// The parts of a consolidated Treaty's document that are not its own text: each <c>CONS.DOC.GEN</c> part that neither is
    /// nor holds the Treaty's own <c>ENACTING.TERMS</c> (the Protocols, the Annexes, the Declarations, the tables of
    /// equivalences), counted unit by unit (each child of its <c>CONTENTS</c>, or the part when it has none), in the
    /// document's order. Each is titled by its own <c>TITLE</c>, else its group's.
    /// </summary>
    private static IReadOnlyList<EuFormexMainBodyPartNotServed> TreatyPartsNotServed(
        EuFormexPackageOutcome source, string entry, XElement root, XElement operative)
    {
        var held = operative.AncestorsAndSelf().ToHashSet();
        var parts = new List<EuFormexMainBodyPartNotServed>();
        var groups = root.Descendants().Where(value => value.Name.LocalName == "CONS.DOC.GEN" && !held.Contains(value) &&
                value.Parent is { } parent && parent.Name.LocalName == "CONS.DOC.GEN" && held.Contains(parent))
            .ToArray();
        for (var group = 0; group < groups.Length; group++)
        {
            var groupTitle = TitleOf(groups[group]);
            var contents = groups[group].Elements().FirstOrDefault(static value => value.Name.LocalName == "CONTENTS");
            var units = contents?.Elements().ToArray() is { Length: > 0 } listed ? listed : [groups[group]];
            for (var unit = 0; unit < units.Length; unit++)
            {
                var title = TitleOf(units[unit]);
                parts.Add(new EuFormexMainBodyPartNotServed(
                    PartIdentity(EuFormexMainBodyPartKind.TreatyPartNotServed, source.ExpressionIdentity.PublisherExpressionId, entry,
                        group.ToString(System.Globalization.CultureInfo.InvariantCulture), unit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizedText(units[unit]))))),
                    EuFormexMainBodyPartKind.TreatyPartNotServed,
                    title.Length > 0 ? title : groupTitle));
            }
        }

        return Array.AsReadOnly(parts.ToArray());
    }

    private static string TitleOf(XElement element) =>
        element.Elements().FirstOrDefault(static value => value.Name.LocalName == "TITLE") is { } title ? NormalizedText(title) : string.Empty;

    private static string NormalizedText(XElement element) =>
        string.Join(' ', string.Concat(element.DescendantNodes().OfType<XText>().Select(static text => text.Value))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A part's identity: its kind, the expression, the package entry and the coordinates that place it.</summary>
    private static string PartIdentity(EuFormexMainBodyPartKind kind, string expression, string entry, params string[] coordinates)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        EuFormexMainBodyArticle.Append(hash, "lex-v3-eu-formex-main-body-part/1");
        EuFormexMainBodyArticle.Append(hash, ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
        EuFormexMainBodyArticle.Append(hash, expression);
        EuFormexMainBodyArticle.Append(hash, entry);
        foreach (var coordinate in coordinates) EuFormexMainBodyArticle.Append(hash, coordinate);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
