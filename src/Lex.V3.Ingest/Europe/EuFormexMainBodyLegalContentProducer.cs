using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
    string? Target);

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
        IReadOnlyList<EuFormexMainBodyToken> tokens)
    {
        PublisherExpressionId = publisherExpressionId;
        PackageEntry = packageEntry;
        PublisherIdentifier = publisherIdentifier;
        Heading = heading;
        Language = language;
        PublisherDate = publisherDate;
        SearchableText = searchableText;
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
    public string IdentitySha256 { get; }

    private static string IdentityOf(EuFormexMainBodyArticle article)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-eu-formex-main-body-article/1");
        Append(hash, article.PublisherExpressionId);
        Append(hash, article.PackageEntry);
        Append(hash, article.PublisherIdentifier);
        Append(hash, article.Heading);
        Append(hash, article.Language);
        Append(hash, article.PublisherDate);
        Append(hash, article.SearchableText);
        foreach (var token in article.Tokens)
        {
            Append(hash, ((int)token.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, token.Text);
            Append(hash, token.Target ?? "");
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
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

public sealed class EuFormexMainBodyLegalContentOutcome
{
    internal EuFormexMainBodyLegalContentOutcome(
        EuFormexPackageOutcome source,
        EuFormexMainBodyLegalContentDisposition disposition,
        IReadOnlyList<EuFormexMainBodyArticle> articles,
        string? detail)
    {
        Source = source;
        Disposition = disposition;
        Articles = Array.AsReadOnly(articles.ToArray());
        Detail = detail;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        EuFormexMainBodyArticle.Append(hash, "lex-v3-eu-formex-main-body-outcome/1");
        EuFormexMainBodyArticle.Append(hash, EuFormexMainBodyLegalContentProducer.Profile);
        EuFormexMainBodyArticle.Append(hash, source.ExpressionIdentity.PublisherWorkId);
        EuFormexMainBodyArticle.Append(hash, source.ExpressionIdentity.PublisherExpressionId);
        EuFormexMainBodyArticle.Append(hash, ((int)disposition).ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var article in Articles) EuFormexMainBodyArticle.Append(hash, article.IdentitySha256);
        EuFormexMainBodyArticle.Append(hash, detail ?? "");
        SemanticIdentitySha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public EuFormexPackageOutcome Source { get; }
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
    public const string Profile =
        "lex-v3-eu-formex-main-body-profile/1;root=ACT;units=ARTICLE;exclude=recitals,final,annex";

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
            if (source.Kind != EuFormexPackageOutcomeKind.Acquired)
            {
                outcomes.Add(new(source, Map(source.Kind), [], source.Detail));
                continue;
            }

            var inventory = source.AcquiredInventory!;
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
            outcomes.Add(new(source, parsed.Disposition, parsed.Articles, parsed.Detail));
        }
        return new EuFormexMainBodyLegalContentPopulation(formex, outcomes);
    }

    private static EuFormexMainBodyLegalContentDisposition Map(EuFormexPackageOutcomeKind kind) => kind switch
    {
        EuFormexPackageOutcomeKind.NotEligible => EuFormexMainBodyLegalContentDisposition.NotEligible,
        EuFormexPackageOutcomeKind.Unavailable => EuFormexMainBodyLegalContentDisposition.PackageUnavailable,
        EuFormexPackageOutcomeKind.Refused => EuFormexMainBodyLegalContentDisposition.PackageRefused,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static (EuFormexMainBodyLegalContentDisposition Disposition,
        IReadOnlyList<EuFormexMainBodyArticle> Articles, string? Detail) Parse(
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

            var names = new HashSet<string>(StringComparer.Ordinal);
            var articles = new List<EuFormexMainBodyArticle>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            long totalXmlBytes = 0;
            var actCount = 0;
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

                XDocument document;
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
                    document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                }
                catch (Exception exception) when (exception is InvalidDataException or XmlException)
                {
                    return Refused(EuFormexMainBodyLegalContentDisposition.XmlRejected,
                        $"Formex XML entry {entry.FullName} is invalid");
                }

                if (!string.Equals(document.Root?.Name.LocalName, "ACT", StringComparison.Ordinal)) continue;
                actCount++;
                var language = RequiredSingleValue(document.Root!, "LG.DOC");
                var publisherDate = document.Root!.Descendants()
                    .FirstOrDefault(static value => value.Name.LocalName == "BIB.INSTANCE")?
                    .Elements().FirstOrDefault(static value => value.Name.LocalName == "DATE")?
                    .Attribute("ISO")?.Value;
                if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(publisherDate))
                    return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                        $"Formex ACT entry {entry.FullName} lacks one language or publisher date");

                foreach (var element in document.Root.Descendants()
                    .Where(static value => value.Name.LocalName == "ARTICLE"))
                {
                    var identifier = element.Attribute("IDENTIFIER")?.Value;
                    var heading = element.Elements().SingleOrDefault(static value =>
                        value.Name.LocalName == "TI.ART")?.Value;
                    if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(heading))
                        return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                            $"Formex ACT entry {entry.FullName} contains an article without one identifier and heading");
                    var tokens = TokensOf(element);
                    if (tokens.Count == 0)
                        return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                            $"Formex article {identifier} contains no legal text");
                    var article = new EuFormexMainBodyArticle(
                        source.ExpressionIdentity.PublisherExpressionId,
                        entry.FullName,
                        identifier,
                        heading,
                        language,
                        publisherDate,
                        SearchableTextOf(element),
                        tokens);
                    if (!identities.Add(article.IdentitySha256))
                        return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                            $"Formex package contains duplicate article identity {identifier}");
                    articles.Add(article);
                }
            }

            if (actCount == 0)
                return Refused(EuFormexMainBodyLegalContentDisposition.MainBodyMissing,
                    "the retained Formex package contains no ACT unit");
            if (actCount != 1)
                return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                    "the retained Formex package contains more than one ACT main-body unit");
            if (articles.Count == 0)
                return Refused(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape,
                    "the retained Formex ACT contains no ARTICLE unit");
            return (EuFormexMainBodyLegalContentDisposition.Admitted,
                Array.AsReadOnly(articles.ToArray()), null);
        }
        catch (InvalidDataException exception)
        {
            return Refused(EuFormexMainBodyLegalContentDisposition.PackageUnreadable, exception.Message);
        }
    }

    private static IReadOnlyList<EuFormexMainBodyToken> TokensOf(XElement article)
    {
        var tokens = new List<EuFormexMainBodyToken>();
        AppendTokens(article, tokens);
        return Array.AsReadOnly(tokens.ToArray());
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

    private static void AppendTokens(XElement element, List<EuFormexMainBodyToken> tokens)
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
                var note = DisplayText(child);
                if (!string.IsNullOrWhiteSpace(note))
                    tokens.Add(new(EuFormexMainBodyTokenKind.Footnote, note, null));
                continue;
            }
            if (child.Name.LocalName is "REF.DOC.OJ" or "REF.DOC" or "LINK")
            {
                var reference = DisplayText(child);
                var target = child.Attributes().FirstOrDefault(static value =>
                    value.Name.LocalName is "REF" or "HREF" or "FILE")?.Value;
                if (!string.IsNullOrWhiteSpace(reference))
                    tokens.Add(new(EuFormexMainBodyTokenKind.Reference, reference, target));
                continue;
            }
            AppendTokens(child, tokens);
        }
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
        IReadOnlyList<EuFormexMainBodyArticle> Articles, string Detail) Refused(
        EuFormexMainBodyLegalContentDisposition disposition,
        string detail) => (disposition, Array.Empty<EuFormexMainBodyArticle>(), detail);
}
